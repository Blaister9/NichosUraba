# Estado V1 — Agendamiento

## Línea base

**Fecha de inicio:** 25 de julio de 2026  
**Rama:** `feat/v1-agendamiento`  
**Estado inicial del árbol:** limpio  
**SDK disponible:** .NET SDK 10.0.301  
**Docker CLI disponible:** 28.0.4  
**Docker Compose disponible:** 2.34.0

El repositorio contenía investigación y especificación, pero no solución ni código de aplicación.

## Estado por historia

| Historia | Estado | Evidencia | Prueba asociada |
|---|---|---|---|
| V1-01 Solución y PostgreSQL | En progreso | Pendiente de creación de solución, migración y health checks | Build, migración y health checks |
| V1-02 Identidad y aislamiento | Pendiente | — | Matriz de autorización e integración |
| V1-03 Directorio y perfil | Pendiente | — | Integración y E2E público |
| V1-04 Servicios y disponibilidad | Pendiente | — | Unitarias e integración |
| V1-05 Cita completa | Pendiente | — | Unitarias, integración, concurrencia y E2E |

## Contradicciones registradas

1. `04_ARCHITECTURE.md` define proyectos `Contracts` y `Web.Client`, mientras la misión muestra una estructura mínima sin ellos. Se conserva la arquitectura: `Web.Client` es requerido por Interactive Auto y `Contracts` evita que el cliente dependa de capas servidoras.
2. `05_DOMAIN_MODEL.md` no incluye observación en `Appointment`, pero la misión la exige como dato opcional corto. Se añade `CustomerNotes` con máximo 300 caracteres, cifrado o protegido junto con los datos personales, sin alterar estados ni flujo.
3. La misión solicita roles Identity `PlatformAdmin`, `BusinessOwner` y `BusinessWorker`; la especificación modela además roles dentro de `BusinessMembership`. Se usan ambos: el rol Identity expresa clase de usuario y la membresía persistida autoriza cada negocio. Ningún rol global concede acceso a datos empresariales.

## Límites de esta implementación

- Solo Salón Bella Urabá y el flujo de citas son visibles públicamente.
- El segundo negocio existe únicamente para pruebas de aislamiento.
- No se implementan turnos, pedidos, pagos, WhatsApp, IA, PWA ni operación offline.
