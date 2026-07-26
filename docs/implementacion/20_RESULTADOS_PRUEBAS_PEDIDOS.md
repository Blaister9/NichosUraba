# Resultados de pruebas — V2-04

## Cobertura incorporada

- Dominio: validaciones de producto/configuración, precio histórico, total, secuencia, estados, concurrencia optimista y permiso.
- Integración PostgreSQL: menú y creación pública, HMAC/PII, capacidad concurrente, precio congelado, aislamiento, permiso y versión obsoleta.
- E2E Chromium móvil: ocho escenarios desde el perfil público hasta operación completa, configuración, aislamiento y ancho de 360 px.

## Comandos

```powershell
dotnet build UrabaConecta.slnx --configuration Release
dotnet test tests\UrabaConecta.Domain.Tests --configuration Release
dotnet test tests\UrabaConecta.IntegrationTests --configuration Release
dotnet test tests\UrabaConecta.EndToEndTests --configuration Release
```

## Ejecución final local

- Build Release: **0 errores, 0 advertencias**.
- Unitarias: **51/51**.
- Integración PostgreSQL real: **25/25**.
- E2E Chromium real: **13/13**; el recorrido nuevo cubre ocho escenarios a 360 × 800 px.
- Total: **89/89**, incluidas las 73 pruebas previas de regresión.
- Migración aplicada en PostgreSQL local y `/health/live` + `/health/ready`: **HTTP 200**.
