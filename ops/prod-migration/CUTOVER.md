# UrabáConecta · Procedimiento de cutover a `prod-real`

Estado: **preparado, no ejecutado**. Nada de lo aquí descrito se ha aplicado a `prod-real`.

Alcance: `Delicadas` (`266e8c06-dbc8-4f4b-8937-d32f69fb87cf`) y
`Studio Laura Usuga` (`9dc7d8ea-0333-4146-9e50-9cf124ac9f0c`).

La ventana crítica —entre la relectura final y el apply— es el paso 3→6. Todo lo
demás se hace antes, sin prisa y sin tocar el piloto.

---

## Fase 0 · Antes del día del corte (sin ventana, sin freeze)

| # | Acción | Comprobación |
|---|---|---|
| 0.1 | Crear bucket R2 productivo con nombre **sin** `demo` (p. ej. `urabaconecta-media`) y su dominio público | `StartupGuard` aborta el arranque si `Bucket` o `PublicBaseUrl` contienen `-demo`, `demo-`, `_demo`, `demo.` o `/demo` |
| 0.2 | Fijar en `prod-real`: `ObjectStorage__{Provider=S3,Bucket,ServiceUrl,PublicBaseUrl,AccessKey,SecretKey,Region}` | `list-variables` |
| 0.3 | Fijar `DataProtection__KeysPath` con volumen propio. **No copiar `/app/keys` del piloto** | anillo nuevo; las cookies del piloto se invalidan, que es lo deseado |
| 0.4 | Fijar `URABACONECTA_TRACKING_HMAC_KEY` (y opcionalmente la de invitación) **nuevas** | no se migran operaciones, así que ningún código público depende de las viejas |
| 0.5 | Variables jurídicas y `DetailedErrors=false`; `DemoSeed/ShowcaseSeed/DemoBootstrap/DemoAccess` ausentes o `false` | `StartupGuard.Validate` sin problemas |
| 0.6 | `ProductionBootstrap__Enabled=true` + `AdminEmail` + `AdminPassword` y **arrancar** `prod-real` | crea los 4 roles y el único `PlatformAdmin`; una sola ejecución, bloqueada después por su propia fila en `platform_access_audits` |
| 0.7 | Retirar `ProductionBootstrap__AdminPassword` y poner `Enabled=false`; **redesplegar** para que el contenedor deje de verlas | borrar variables no reinicia el servicio por sí solo |
| 0.8 | Confirmar cabeza de migraciones `20260825014007_AddBusinessLocationAndFulfillment` y las 5 categorías | `00_preflight.sql` lo vuelve a comprobar |

> **Cuidado con el orden en 0.6.** `Program.cs` hace `await BootstrapProductionAdminAsync(...)`
> **sin try/catch**, y `Validate` lanza si la contraseña no cumple. Poner `Enabled=true`
> antes que una contraseña válida deja Production sin arrancar. Pon primero el correo y la
> contraseña, y `Enabled=true` al final.
>
> La contraseña exige: **≥16 caracteres**, con mayúscula, minúscula, dígito y al menos un
> carácter no alfanumérico; y no puede ser `UrabaDemo!2026`, `Password1!` ni `Admin123!`.
> La cuenta nace con `MustChangePassword=true`.

> `prod-real` no expone proxy TCP. Para aplicar SQL: `railway ssh -s Postgres-84xK -e prod-real`,
> o crear un proxy temporal y **retirarlo** al terminar.

---

## Fase 1 · Freeze corto

1. Avisar a las dos Owners (`delicadasskin01@gmail.com`, `marcelcomerce17@gmail.com`)
   de una pausa de ~20 minutos.
2. Congelar escrituras en el piloto. La forma menos invasiva, sin tocar código:
   escalar el servicio `NichosUraba` a 0 réplicas, o poner el negocio en
   `PendingConfiguration` desde la consola de administración.
   **Anotar la hora exacta del freeze.**

---

## Fase 2 · Copia de seguridad final del piloto

```bash
railway ssh -s Postgres -e production \
  sh -c 'pg_dump --format=custom --no-owner --no-privileges "$DATABASE_URL"' > pilot_YYYYMMDD_HHMM.dump
```

`pg_dump` debe correr **dentro** del contenedor: el servidor es PostgreSQL 18.6 y un
cliente 17.x se niega por diferencia de versión. Verificar que el fichero pesa lo
esperado y guardarlo fuera de Railway antes de continuar.

---

## Fase 3 · Relectura final (empieza la ventana crítica)

```bash
railway run --service Postgres --environment production -- \
  psql "$DATABASE_PUBLIC_URL" -X -A -t -f generate.sql > _raw.txt
python split.py _raw.txt pkg
```

Con el freeze activo el estado ya no cambia, así que este paquete es el definitivo.

---

## Fase 4 · Comparación contra el ensayo

```bash
diff pkg/_snapshot.txt _snapshot_ensayo.txt
```

- Sin diferencias → seguir.
- Con diferencias → **es lo esperado si Laura siguió trabajando**. El paquete
  regenerado ya refleja el estado nuevo; basta releer `_snapshot.txt` y confirmar
  que los conteos son coherentes. No hay que rehacer nada a mano.
- Revalidar el manifest contra R2:

```bash
railway run --service NichosUraba --environment production -- python r2_list.py > r2_now.csv
python check_manifest.py r2_now.csv     # debe decir MANIFEST VALIDO
```

---

## Fase 5 · Copia de media

Corre **dentro** del contenedor de Production: las credenciales del destino son
variables selladas y `railway run` no las expone a un proceso local. Se sube el script
y el manifest por stdin, que `railway ssh` sí reenvía.

```bash
SSH="railway ssh -s UrabaConecta-prod-real -e prod-real"
base64 -w0 copy_media.sh          | $SSH sh -c 'base64 -d > /tmp/copy_media.sh'
base64 -w0 pkg/media_manifest.csv | $SSH sh -c 'base64 -d > /tmp/media_manifest.csv'

$SSH sh -c 'sh /tmp/copy_media.sh --check'                              # ambos buckets responden
$SSH sh -c 'sh /tmp/copy_media.sh --dry-run /tmp/media_manifest.csv'    # qué copiaría
$SSH sh -c 'sh /tmp/copy_media.sh --confirm /tmp/media_manifest.csv'    # copia real
```

Conserva la `StorageKey` exacta, así que **ninguna fila de base cambia**. Es
idempotente: reejecutar salta lo ya presente con el mismo tamaño. Si algún objeto falla,
el script termina distinto de cero — **aborta antes del apply de base**, que todavía es
reversible en ese punto.

---

## Fase 6 · Apply sobre `prod-real`

```bash
psql "<prod-real>" -v ON_ERROR_STOP=1 -f apply.sql
```

Una sola transacción y una sola sesión (el ayudante vive en `pg_temp`).
`00_preflight.sql` aborta antes de escribir si faltan roles, admin, categorías o
la cabeza de migraciones no coincide.

---

## Fase 7 · Validación

```bash
psql "<prod-real>" -f 90_verify.sql
```

Debe dar `READY` para ambos negocios y `TODAS LAS ASERCIONES PASAN`.
Además, a mano:

- entrar con cada Owner real y comprobar que ve su negocio;
- abrir la ficha pública de ambos y comprobar que logo, portada, galería,
  servicios y productos cargan desde el bucket nuevo;
- comprobar que la promoción de Laura aparece si seguía vigente;
- comprobar que el primer pedido de Delicadas sale con número **1**.

---

## Fase 8 · Reapertura

1. Apuntar el dominio a `prod-real`.
2. Restaurar réplicas del piloto **sólo si** se quiere dejarlo accesible; lo
   recomendable es dejarlo caído para que nadie siga operando en él.
3. Avisar a las dos Owners.

---

## Fase 8 bis · Retirar el token temporal de R2  — OBLIGATORIO

El origen se lee con un token de Cloudflare creado **sólo para este corte**: permiso
`Object Read only`, acotado a `urabaconecta-demo-media`. Comprobado en el preflight: ese
mismo token recibe **403** contra `urabaconecta-prod-media`, así que no puede escribir en
Production ni tocar nada más.

En cuanto la copia real termine bien, y antes de dar el corte por cerrado:

1. Borrar las cuatro variables del servicio `UrabaConecta-prod-real`:

```bash
for v in MigrationSourceR2__AccessKey MigrationSourceR2__SecretKey \
         MigrationSourceR2__Endpoint MigrationSourceR2__Bucket; do
  railway variables delete "$v" --service UrabaConecta-prod-real --environment prod-real
done
railway variables delete MigrationSourceR2__Region --service UrabaConecta-prod-real --environment prod-real
railway redeploy --service UrabaConecta-prod-real --environment prod-real --yes
```

2. Revocar el token en Cloudflare → R2 → API → Manage API Tokens.

3. Demostrar que Production sigue sirviendo su media con sus propias credenciales:

```bash
railway ssh -s UrabaConecta-prod-real -e prod-real sh -c 'sh /tmp/copy_media.sh --check'
```

Debe decir `origen SIN CREDENCIALES` y `destino HTTP 200`. Y las fichas públicas de los
dos negocios deben seguir cargando sus imágenes.

Un despliegue de Railway no arrastra `/tmp`, así que `copy_media.sh` y el manifest
desaparecen solos del contenedor en el siguiente redespliegue. Aun así conviene borrarlos:

```bash
railway ssh -s UrabaConecta-prod-real -e prod-real sh -c 'rm -f /tmp/copy_media.sh /tmp/media_manifest.csv'
```

---

## Reversión

Antes del paso 6 no hay nada que revertir: no se ha escrito en `prod-real`.
Después del paso 6, si algo sale mal, la salida es dejar el dominio apuntando al
piloto y reabrirlo — el piloto conserva todos sus datos intactos, porque la
migración nunca escribe en él.

---

## Desmontar el ensayo

```bash
"C:\Program Files\PostgreSQL\17\bin\pg_ctl" -D "<scratchpad>/pgdata" stop
rm -rf "<scratchpad>/pgdata"
```
