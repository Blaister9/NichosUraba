# Ejecución local

## Requisitos

- .NET SDK 10.0.301 o parche compatible de .NET 10.
- Docker Desktop con Docker Compose.
- PowerShell.

## Inicio

Desde la raíz del repositorio:

```powershell
Copy-Item .env.example .env
$env:URABACONECTA_TRACKING_HMAC_KEY = "reemplace-por-una-clave-local-de-32-bytes-o-mas"
docker compose up -d
docker compose ps
dotnet tool restore
dotnet restore UrabaConecta.slnx
dotnet build UrabaConecta.slnx --no-restore
dotnet tool run dotnet-ef database update `
  --project src\UrabaConecta.Infrastructure\UrabaConecta.Infrastructure.csproj `
  --startup-project src\UrabaConecta.Web\UrabaConecta.Web\UrabaConecta.Web.csproj `
  --context AppDbContext
dotnet run --no-build --project src\UrabaConecta.Web\UrabaConecta.Web\UrabaConecta.Web.csproj
```

La aplicación queda en `http://localhost:5129` y, con el perfil HTTPS, en `https://localhost:7179`.

`.env` configura Compose; .NET recibe la clave HMAC mediante la variable de entorno mostrada. En Development existe un valor de respaldo inseguro para facilitar la demo. Fuera de Development, el arranque falla si falta la clave.

## Pruebas

```powershell
dotnet build UrabaConecta.slnx --no-restore
& tests\UrabaConecta.EndToEndTests\bin\Debug\net10.0\playwright.ps1 install chromium
dotnet test UrabaConecta.slnx --no-build
```

Integración y E2E crean PostgreSQL 17 aislado mediante Testcontainers. No usan EF Core InMemory.

## Verificación

- `http://localhost:5129/health/live`: proceso vivo.
- `http://localhost:5129/health/ready`: conexión PostgreSQL disponible.
- `http://localhost:5129`: directorio.
- `http://localhost:5129/negocios/salon-bella-uraba`: perfil.
- `http://localhost:5129/Account/Login`: ingreso.

Para detener la base:

```powershell
docker compose down
```

No use `-v` si desea conservar el volumen local.
