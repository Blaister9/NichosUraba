# Resultados de pruebas

**Fecha de ejecución:** 25 de julio de 2026

| Verificación | Resultado |
|---|---|
| `docker compose up -d` | PostgreSQL 17 `healthy` en puerto 5433 |
| `dotnet restore UrabaConecta.slnx` | Correcto |
| `dotnet build UrabaConecta.slnx --no-restore` | Correcto, 0 advertencias, 0 errores |
| Migración inicial | Aplicada a base vacía |
| Seed local | 2 negocios, 3 servicios del salón, 6 días de horario y 3 membresías |
| Restricción de solapamiento | `ex_appointments_no_active_overlap` presente |
| `/health/live` | HTTP 200 |
| `/health/ready` | HTTP 200 |
| Directorio local | 1 resultado para “Bella”: Salón Bella Urabá |

## Suites

| Suite | Pasaron | Fallaron | Omitidas |
|---|---:|---:|---:|
| Dominio y seguridad | 16 | 0 | 0 |
| Integración con PostgreSQL real | 5 | 0 | 0 |
| E2E Playwright Chromium móvil | 2 | 0 | 0 |
| **Total** | **23** | **0** | **0** |

La integración comprobó creación, persistencia, consentimiento, HMAC, código inválido, fuera de horario, doble reserva concurrente, filtros, cookies Identity, transiciones, configuración y aislamiento. E2E recorrió la cita pública hasta `Completed` y negó la URL del salón al propietario del segundo negocio.
