# Resultados de pruebas — configuración privada

**Fecha:** 25 de julio de 2026

## Entorno y comandos

| Verificación | Resultado |
|---|---|
| `docker compose up -d` | PostgreSQL 17 saludable en `localhost:5433` |
| `dotnet restore UrabaConecta.slnx` | Correcto |
| `dotnet build UrabaConecta.slnx --no-restore` | Correcto, 0 advertencias y 0 errores |
| `dotnet ef database update ...` | Migración `AddPrivateBusinessConfiguration` aplicada |
| aplicación en `http://127.0.0.1:5129` | inicio, perfil y health checks HTTP 200; ruta privada anónima redirige a ingreso |
| Playwright Chromium | instalado y ejecutado sin reintentos automáticos |

## Suites

| Suite | Pasaron | Fallaron | Omitidas |
|---|---:|---:|---:|
| Dominio y seguridad | 22 | 0 | 0 |
| Integración con PostgreSQL real | 8 | 0 | 0 |
| E2E Chromium | 6 | 0 | 0 |
| **Total** | **36** | **0** | **0** |

Las 23 pruebas anteriores se conservan: 16 unitarias, 5 de integración y 2 E2E. Se añadieron 13 pruebas.

## Cobertura nueva

- reglas de duración, precio, activación, horario, excepción, participación y versión;
- CRUD controlado y visibilidad pública de servicios;
- persistencia de personal, horario, cierre parcial y motivo;
- rechazo de asociación cruzada;
- propietario propio, propietario ajeno, visitante, trabajador sin permiso y trabajador autorizado;
- conflicto concurrente con `409`;
- efecto de horario/excepción y servicio inactivo sobre rutas públicas;
- CRUD de servicio en navegador, aislamiento visual y viewport de 360 px.

Integración y E2E crean PostgreSQL real con Testcontainers. No se usó EF Core InMemory ni pausas arbitrarias para sincronizar los escenarios nuevos.
