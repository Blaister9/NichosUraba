# Estado V1 — Agendamiento

## Línea base

**Fecha:** 25 de julio de 2026
**Rama:** `feat/v1-agendamiento`  
**Estado inicial:** repositorio sin solución ni código de aplicación
**Herramientas:** .NET SDK 10.0.301, Docker 28.0.4 y Docker Compose 2.34.0

## Estado por historia

| Historia | Estado | Evidencia | Prueba asociada |
|---|---|---|---|
| V1-01 Solución y PostgreSQL | Terminada | Solución .NET 10, Compose, `AppDbContext`, migración inicial, `/health/live` y `/health/ready` | Build sin errores; migración aplicada; ambos health checks HTTP 200 |
| V1-02 Identidad y aislamiento | Terminada | Identity con `Guid`, tres roles, membresías persistidas, políticas y filtros explícitos por negocio | Integración con cookies: propietaria, otro propietario, trabajadora, visitante y modificación cruzada |
| V1-03 Directorio y perfil | Terminada | Inicio, búsqueda, filtros, estado vacío, ficha por `slug` y diseño desde 360 px | Integración de búsqueda/filtros y E2E móvil |
| V1-04 Servicios y disponibilidad | Terminada | Servicios, personal, relación servicio-personal, horario y excepciones; CRUD privado por API; cálculo `America/Bogota` | Unitarias de franjas; integración de configuración y referencia cruzada |
| V1-05 Cita completa | Terminada | Solicitud invitada, consentimiento, protección de datos, HMAC, seguimiento, panel, transiciones y exclusión PostgreSQL | Integración de persistencia/concurrencia y E2E visitante → negocio → seguimiento |

## Contradicciones resueltas

1. `04_ARCHITECTURE.md` define `Contracts` y `Web.Client`, aunque la estructura mínima de la misión no los enumera. Se conservaron porque Interactive Auto necesita el ensamblado cliente y los contratos compartidos evitan referencias indebidas.
2. `05_DOMAIN_MODEL.md` no enumera la observación, mientras la misión exige una observación opcional corta. Se añadió con máximo 300 caracteres y protección equivalente a los demás datos personales.
3. Identity usa roles globales, pero el acceso a datos se decide además con `BusinessMembership`. Un rol nunca concede por sí solo acceso a otro negocio.

## Alcance respetado

Solo el agendamiento de Salón Bella Urabá es público. El segundo negocio existe para comprobar aislamiento. No se implementaron turnos, pedidos, pagos, WhatsApp, IA, PWA, operación offline ni otros módulos.
