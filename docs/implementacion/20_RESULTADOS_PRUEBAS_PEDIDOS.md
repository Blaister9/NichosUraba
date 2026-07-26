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

Los resultados finales de la ejecución se registran en el cierre de la tarea. Las pruebas existentes de citas, configuración, membresías y turnos permanecen como regresión obligatoria.
