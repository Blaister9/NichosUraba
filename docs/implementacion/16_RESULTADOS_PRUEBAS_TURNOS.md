# Resultados de pruebas — turnos virtuales

**Fecha:** 25 de julio de 2026

## Verificaciones

| Verificación | Resultado |
|---|---|
| `dotnet restore UrabaConecta.slnx` | Correcto |
| `dotnet build UrabaConecta.slnx --no-restore` | Correcto, 0 advertencias y 0 errores |
| migración en PostgreSQL | `AddVirtualQueues` aplicada |
| modelo frente a migraciones | sin cambios pendientes |
| SignalR real | grupos público e individual recibieron eventos desde Kestrel |
| viewport | Chromium a 360 × 800 sin desbordamiento horizontal |

## Cobertura agregada

- dominio: configuración, jornada, numeración, versiones y todas las transiciones;
- PostgreSQL: HMAC sin código en claro, alias protegido, secuencia concurrente única y contigua;
- concurrencia operativa: dos llamados con la misma versión dejan exactamente un turno llamado;
- autorización: propietaria, trabajadora autorizada, trabajadora sin permiso, propietaria de otro negocio y revocación inmediata;
- aislamiento: recurso inexistente o ajeno no expone datos;
- jornada: apertura, pausa, reanudación y bloqueo de cierre con pendientes;
- Chromium real: directorio, perfil, toma, seguimiento, panel, llamado, atención, configuración, aislamiento y móvil.

Las pruebas de integración y E2E crean PostgreSQL 17 aislado con Testcontainers. No usan EF Core InMemory, pausas arbitrarias ni reintentos de suite.

## Suites

| Suite | Pasaron | Fallaron | Omitidas |
|---|---:|---:|---:|
| Dominio y seguridad | 41 | 0 | 0 |
| Integración con PostgreSQL real | 20 | 0 | 0 |
| E2E Chromium real | 12 | 0 | 0 |
| **Total** | **73** | **0** | **0** |

La línea base de 59 pruebas permanece: se agregaron 7 unitarias, 6 de integración y 1 recorrido E2E que verifica siete escenarios de la vertical.
