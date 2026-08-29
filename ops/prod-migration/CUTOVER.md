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
| 0.6 | `ProductionBootstrap__Enabled=true` + `AdminEmail` + `AdminPassword` (≥16 car.) y **arrancar** `prod-real` | crea los 4 roles y el único `PlatformAdmin`; es de una sola ejecución |
| 0.7 | Retirar `ProductionBootstrap__AdminPassword` tras el primer arranque | `StartupGuard` lo exige |
| 0.8 | Confirmar cabeza de migraciones `20260825014007_AddBusinessLocationAndFulfillment` y las 5 categorías | `00_preflight.sql` lo vuelve a comprobar |

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

```bash
export DEST_ObjectStorage__AccessKey=...     # bucket productivo
export DEST_ObjectStorage__SecretKey=...
export DEST_ObjectStorage__Bucket=urabaconecta-media
export DEST_ObjectStorage__ServiceUrl=https://<cuenta>.r2.cloudflarestorage.com
export DEST_ObjectStorage__Region=auto

railway run --service NichosUraba --environment production -- python copy_media.py            # simulacro
railway run --service NichosUraba --environment production -- python copy_media.py --confirm  # copia real
```

Conserva la `StorageKey` exacta, así que **ninguna fila de base cambia**. Es
idempotente: reejecutar salta lo ya presente con el mismo tamaño.

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
