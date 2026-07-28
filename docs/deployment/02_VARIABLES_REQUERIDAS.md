# Variables requeridas

Configurar en Railway; nunca copiar valores a Git, logs o capturas.

| Variable | Tipo | Valor o fuente |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | configuración | `Demo` |
| `PORT` | Railway | inyectada por Railway |
| `ConnectionStrings__DefaultConnection` | secreto/referencia | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};SSL Mode=Prefer` |
| `URABACONECTA_TRACKING_HMAC_KEY` | secreto | 32 bytes aleatorios o más, codificados en Base64 |
| `DataProtection__KeysPath` | configuración | `/app/keys` |
| `DemoSeed__Enabled` | configuración | `true` solo para este entorno |
| `DemoSeed__AdminPassword` | secreto | clave aleatoria exclusiva de administración |
| `DemoSeed__BusinessPassword` | secreto | clave aleatoria distinta para cuentas operativas |

## Bootstrap administrativo temporal

Use estas variables únicamente si se perdió el acceso de la única cuenta `PlatformAdmin` de Demo:

| Variable | Tipo | Uso temporal |
|---|---|---|
| `DemoBootstrap__Enabled` | configuración | `true` durante un solo despliegue de recuperación |
| `DemoBootstrap__AdminEmail` | configuración | correo fijo de la cuenta administrativa Demo |
| `DemoBootstrap__AdminPassword` | secreto | contraseña temporal aleatoria de 16 caracteres o más |

El bootstrap conserva el identificador de la cuenta existente, restablece su acceso con ASP.NET
Identity, cierra sus sesiones y exige cambiar la contraseña al ingresar. Una marca persistida en la
auditoría impide que vuelva a ejecutarse.

Después del primer ingreso y cambio de contraseña:

1. establezca `DemoBootstrap__Enabled=false`;
2. elimine `DemoBootstrap__AdminPassword`;
3. redespliegue y compruebe nuevamente el acceso.

Estas tres variables están prohibidas en `Production`.

Generación local sin imprimir el resultado:

```powershell
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$secret = [Convert]::ToBase64String($bytes)
# Pegue $secret directamente en Railway y limpie la variable al terminar.
```

Railway permite referencias entre servicios. No convertir las variables `PG*` en valores rastreados
ni usar la contraseña de `compose.yaml`.
