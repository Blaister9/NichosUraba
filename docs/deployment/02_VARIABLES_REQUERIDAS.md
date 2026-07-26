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

Generación local sin imprimir el resultado:

```powershell
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$secret = [Convert]::ToBase64String($bytes)
# Pegue $secret directamente en Railway y limpie la variable al terminar.
```

Railway permite referencias entre servicios. No convertir las variables `PG*` en valores rastreados
ni usar la contraseña de `compose.yaml`.
