# 05 — Respaldo y restauración

Railway Hobby no ofrece respaldos administrados con retención ni restauración a un punto en el
tiempo. El respaldo es responsabilidad de la operación, y por eso existen los scripts de `ops/`.

## Los tres activos que hay que respaldar

| Activo | Qué pasa si se pierde | Cómo se respalda |
| --- | --- | --- |
| PostgreSQL | Se pierden negocios, citas, turnos, pedidos y cuentas | `ops/backup-postgres.ps1` |
| `/app/keys` (Data Protection) | Los datos personales cifrados quedan **ilegibles para siempre** | Copia del volumen. Ver `06_DATA_PROTECTION.md` |
| Bucket R2 | Se pierden las imágenes de los negocios | Ver más abajo |

Respaldar PostgreSQL y olvidar `/app/keys` produce una restauración que arranca pero cuyos
teléfonos y notas de cliente no se pueden leer. Los dos van juntos, siempre.

## PostgreSQL

### Respaldo

```powershell
$env:URABACONECTA_BACKUP_CONNECTION = "postgresql://usuario:clave@host:puerto/base"
```

```powershell
./ops/backup-postgres.ps1 -Destination "D:\respaldos\urabaconecta" -RetentionDays 30
```

El script:

- vuelca en formato `custom` (comprimido, restaurable por partes) con `--compress=9`;
- nombra el archivo con marca de tiempo UTC;
- **verifica el resultado** con `pg_restore --list`, no sólo su tamaño;
- descarta el archivo si `pg_dump` falló a medias, para no dejar un respaldo engañoso;
- aplica la retención **sólo después** de un respaldo verificado;
- nunca imprime la cadena de conexión.

No contiene credenciales: la conexión llega por variable de entorno y se descarta al cerrar la
consola.

### Restauración

```powershell
$env:URABACONECTA_RESTORE_CONNECTION = "postgresql://usuario:clave@host:puerto/postgres"
```

```powershell
./ops/restore-postgres.ps1 -DumpFile "D:\respaldos\urabaconecta-production-20260810-030000.dump" -TargetDatabase urabaconecta_ensayo
```

La cadena apunta a la base de mantenimiento (`postgres`); el script crea la base destino. Por
omisión **se niega a escribir sobre una base existente**; `-Overwrite` lo permite y exige
teclear el nombre de la base para confirmar.

Verifica el volcado **antes** de tocar la base destino, y al terminar comprueba que quedaron
tablas y que el historial de migraciones de EF viajó con ellas.

### Restauración probada

Ejecutada de verdad durante este endurecimiento, no descrita en teoría:

| Paso | Resultado |
| --- | --- |
| Base de origen con el esquema productivo completo | 10 migraciones aplicadas |
| `ops/backup-postgres.ps1` | 230 objetos restaurables, verificados con `pg_restore --list` |
| `ops/restore-postgres.ps1` en base temporal | **35 tablas, 10 migraciones registradas** |

El ensayo destapó dos defectos reales de los scripts, ya corregidos: `psql` devuelve `null` (no
cadena vacía) cuando la consulta no tiene filas, y Windows PowerShell elimina las comillas dobles
al invocar ejecutables nativos, lo que rompía el identificador `"__EFMigrationsHistory"`.

### Calendario

| Cuándo | Qué |
| --- | --- |
| Diario | Respaldo automático o manual |
| Antes de cada despliegue con migraciones | Respaldo obligatorio |
| Mensual | Ensayo de restauración en base temporal |
| Retención | 30 días |

El archivo debe salir de la máquina que lo genera. Una única copia en el portátil no es un
respaldo.

## Data Protection

Ver `06_DATA_PROTECTION.md`.

## Cloudflare R2

Estructura de claves dentro del bucket:

```
businesses/{businessId}/{kind}/{imageId}.webp
```

donde `kind` es `logo`, `cover` o `gallery`.

Estrategia de recuperación:

1. **Versionado de objetos activado en el bucket.** Es la defensa contra un borrado accidental y
   no cuesta trabajo operativo.
2. Inventario periódico del bucket (`rclone lsjson` o el panel de R2) guardado junto a los
   respaldos de PostgreSQL, para poder detectar qué falta.
3. Las imágenes son **reponibles**: si se pierde una, el propietario la vuelve a subir. Es el
   activo menos crítico de los tres.

En el bucket no se guarda ningún secreto: sólo imágenes ya normalizadas, sin metadatos EXIF.
