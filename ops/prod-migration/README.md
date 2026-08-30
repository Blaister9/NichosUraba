# Herramienta de cutover piloto → Production

Extrae dos negocios reales del piloto y los reconstruye en `prod-real` sin arrastrar
material Demo. Sólo la herramienta está versionada: **el paquete que produce no**, porque
lleva datos personales y hashes de contraseña. Se regenera en cada corte.

Alcance fijo, escrito dentro de `generate.sql`:

| Negocio | Id |
|---|---|
| Delicadas | `266e8c06-dbc8-4f4b-8937-d32f69fb87cf` |
| Studio Laura Usuga | `9dc7d8ea-0333-4146-9e50-9cf124ac9f0c` |

## Qué hace cada pieza

| Fichero | Papel |
|---|---|
| `generate.sql` | Lee el piloto **sólo lectura** y emite el paquete por stdout, separado con marcas `-- @@FILE:` |
| `split.py` | Reparte esa salida en `pkg/`. Marca `20_identity.sql` como sensible y nunca lo vuelca a consola |
| `00_helper.sql` | `pg_temp.uc_upsert(tabla, jsonb, claves)`. Vive en `pg_temp`: no deja objetos en el destino |
| `00_preflight.sql` | Aborta antes de escribir si no cuadran cabeza de migraciones, roles, admin único o categorías |
| `apply.sql` | Aplica el paquete completo en una transacción y una sesión |
| `80_fixups.sql` | `CreatedByUserId` → PlatformAdmin del destino. Sólo actúa si es `NULL` |
| `90_verify.sql` | Readiness reproducido en SQL, inventario y aserciones de exclusión |
| `fingerprint.sql` | Huella de filas y contenido, para probar idempotencia entre dos ejecuciones |
| `r2_list.py` | Lista el bucket con `ListObjectsV2` firmado. No imprime credenciales |
| `check_manifest.py` | Contrasta el manifest contra el bucket vivo |
| `copy_media.py` | Copia los objetos conservando la `StorageKey`. Simulacro salvo `--confirm`. Requiere Python y credenciales legibles en local |
| `copy_media.sh` | La misma copia, en `sh` + `curl` + `openssl`, para correr **dentro** del contenedor de Production. Modos `--check`, `--dry-run`, `--confirm` |
| `05_target_baseline.sql` | **Sólo ensayo.** Simula prod-real tras el bootstrap. No se ejecuta en el corte |
| `parity*.sql`, `idx_detail.sql`, `pilot_unchanged.sql` | Diagnósticos de paridad de esquema y de no-modificación del origen |

## Decisiones que la herramienta aplica sola

- **Laura pasa a `spa-y-belleza`.** No se importa `belleza-cuidado-personal`, que venía
  de `DevelopmentSeeder` y no existe en Production.
- **`NextOrderNumber = 1`** en Delicadas: no se migran pedidos, y arrancar en 1006
  insinuaría un historial inexistente.
- **`CreatedByUserId` se remapea.** No tiene FK, así que una copia literal dejaría dos
  negocios apuntando a cuentas Demo inexistentes en el destino.
- **El rol `BusinessOwner` se resuelve por nombre** contra el destino. Nunca se reutiliza
  el `RoleId` del piloto.
- **Sólo membresías activas.** La Owner Demo de Laura queda fuera por el `WHERE`.
- **Sólo imágenes con `IsDeleted = false`**, con la misma `StorageKey`: la URL pública se
  compone en ejecución, así que cambiar de bucket no toca ninguna fila.
- **Sólo promociones activas y dentro de su ventana** en el instante del corte.

## Lo que nunca cruza

Citas y pedidos históricos, sus consentimientos, suscripciones push, invitaciones
consumidas, negocios Demo (`7777…`), fixtures de auditoría y humo, usuarios Demo,
imágenes borradas y el anillo de Data Protection del piloto.

Los hashes de contraseña sí viajan: son PBKDF2 autocontenidos y no dependen de ese
anillo, así que las dos Owners entran con las credenciales que ya usan.

## Por qué hay dos copiadores

Las credenciales de R2 de Production son variables **selladas** del servicio: `railway run`
no las inyecta en un proceso local, así que la copia no puede ejecutarse desde una
terminal. Tiene que correr dentro del contenedor, y ese contenedor —`aspnet:10.0.11`—
no lleva Python. De ahí `copy_media.sh`, que sólo necesita `curl` y `openssl`, ya
presentes en la imagen.

El destino usa las credenciales selladas que el contenedor ya tiene. El origen entra por
variables aparte, `MigrationSourceR2__*`, con un token temporal de **sólo lectura**
acotado al bucket del piloto. Las normales `ObjectStorage__*` nunca se tocan: siguen
representando el destino.

Al terminar la copia real hay que retirar las cuatro `MigrationSourceR2__*` y revocar el
token en Cloudflare. El detalle está en [CUTOVER.md](CUTOVER.md).

Se sube al contenedor por stdin, que `railway ssh` sí reenvía:

```bash
base64 -w0 copy_media.sh | railway ssh -s UrabaConecta-prod-real -e prod-real \
  sh -c 'base64 -d > /tmp/copy_media.sh'
railway ssh -s UrabaConecta-prod-real -e prod-real sh -c 'sh /tmp/copy_media.sh --check'
```

## Uso

El procedimiento completo, con sus fases y su ventana, está en [CUTOVER.md](CUTOVER.md).
