# Resultados de pruebas de onboarding

Fecha de ejecución: 2026-07-26.

## Cobertura añadida

- Dominio: estados, publicación, suspensión, concurrencia, funciones y lista de preparación.
- Integración PostgreSQL real: autorización global, alta, aislamiento del administrador, cuenta piloto, activación/suspensión/reactivación y concurrencia.
- E2E Playwright: los siete escenarios solicitados para citas, turnos, pedidos, configuración incompleta, suspensión, aislamiento y contraseña temporal.

## Resultado

Corrida final Release:

- Unitarias: 59/59.
- Integración con PostgreSQL: 30/30.
- E2E Playwright: 24/24.
- Total: 113/113.
- Compilación: 0 advertencias, 0 errores.
- Migración: aplicada; no hay cambios pendientes del modelo.
- Contenedores: `live` y `ready` respondieron `200`; reinicio conservó conteos `4|10|3` para negocios, membresías y funciones, confirmando persistencia y seed idempotente.

Las 93 pruebas de V3 permanecen incluidas y aprobadas.
