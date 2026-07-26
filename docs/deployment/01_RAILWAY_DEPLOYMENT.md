# Despliegue controlado en Railway

## Alcance

Desplegar exclusivamente `release/pilot-demo` desde `https://github.com/Blaister9/NichosUraba`,
usando el `Dockerfile` raíz. El proyecto contiene un servicio web, PostgreSQL administrado y dos
volúmenes: el propio de PostgreSQL y `urabaconecta-keys` montado en `/app/keys`.

## Configuración del servicio web

1. Crear proyecto desde el repositorio y fijar la rama `release/pilot-demo`.
2. Confirmar que el builder detectó el `Dockerfile`; no usar `compose.yaml` en Railway.
3. Referenciar PostgreSQL mediante la red privada y configurar las variables de
   `02_VARIABLES_REQUERIDAS.md`.
4. Montar un volumen persistente en `/app/keys`.
5. Configurar `/health/ready` como health check.
6. Usar una réplica y política de reinicio `ON_FAILURE`, con máximo 3 reintentos. No usar `ALWAYS`
   para ocultar fallos de configuración.
7. Generar el dominio Railway únicamente después de que `ready` responda `200`.

Railway termina TLS. La aplicación confía `X-Forwarded-Proto`, marca la cookie de autenticación como
`Secure` fuera de Development y escucha `http://0.0.0.0:$PORT`.

## Migración y seed

En `Demo`, el arranque ejecuta `Database.MigrateAsync()` antes del seed. El seed requiere
`DemoSeed__Enabled=true`, es idempotente, solo crea elementos ausentes y no cambia contraseñas de
cuentas existentes. No se usa `EnsureCreated`.

Antes de promover otro commit:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project src\UrabaConecta.Infrastructure\UrabaConecta.Infrastructure.csproj `
  --startup-project src\UrabaConecta.Web\UrabaConecta.Web\UrabaConecta.Web.csproj `
  --context AppDbContext
```

## Criterio de aprobación

La URL no se entrega hasta verificar HTTPS, ambos health checks, volumen de llaves tras reinicio,
persistencia PostgreSQL, seed repetido, recorridos públicos y privados, aislamiento y móvil.
