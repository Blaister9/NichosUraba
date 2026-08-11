# 00 — Estado de preparación productiva

Resumen del endurecimiento aplicado sobre `feat/v5-founder-production` (commit base `ade4051`)
para operar con los primeros negocios reales en un ambiente separado de la demostración.

## Qué cambió en el código

| Área | Antes | Ahora |
| --- | --- | --- |
| Migraciones | `MigrateAsync()` sólo corría dentro del sembrado de Demo, así que **Production nunca aplicaba el esquema** | `DatabaseMigrator` corre en todo ambiente, antes del sembrado y de las cuentas |
| Fallo de migración | Excepción durante el arranque: contenedor caído, 502 | Se registra, el proceso vive y `/health/ready` falla: Railway conserva el despliegue anterior |
| Cuenta inicial | No existía camino productivo; sólo el bootstrap de Demo | `ProductionAdminBootstrap`: un `PlatformAdmin`, una sola vez, bloqueado por ambiente |
| Enriquecimiento Demo | Protegido sólo en la ruta de base existente | Protegido en las dos rutas de sembrado |
| `StartupGuard` | Sembrado, bootstrap, legales, almacenamiento, base Demo, llaves | Añade bucket Demo, `DetailedErrors` y la contraseña del arranque productivo |
| Cookie de sesión | `Secure` fuera de Development; `SameSite` heredado del marco | `Secure`, `HttpOnly` y `SameSite=Lax` explícitos |
| Registro | Texto plano sin correlación | JSON con ámbitos: correlación, petición, ambiente, versión, commit, actor y negocio |
| Salud privada | Ambiente, versión, commit, base, almacenamiento, Data Protection | Añade tiempo de proceso y resultado de la migración |

## Qué se comprobó de verdad

- **Restauración real ejecutada**: volcado de un esquema productivo completo (10 migraciones,
  230 objetos), restaurado en una base temporal → 35 tablas y 10 migraciones. Ver
  `05_BACKUP_RESTORE.md`.
- **Pruebas**: suite completa en Release contra PostgreSQL real. Ver `docs/production/` y el
  informe de la entrega.
- **Secretos**: barrido del historial completo de Git. Sin cadenas de conexión, claves HMAC,
  credenciales de R2 ni certificados. Ver `04_SECRETS.md`.

## Qué NO cambió, por decisión

- No se agregaron funciones comerciales, ni pasarelas de pago, ni API de WhatsApp.
- No se rediseñó la aplicación ni se movió la región de ningún recurso.
- No se creó ningún recurso de pago en Railway. Ver `14_COSTS.md`.

## Lo que falta antes de operar

Nada de esto es código: son datos y decisiones que sólo puede aportar la persona responsable.

1. Los siete valores de `Legal__*` con datos jurídicos reales (`09_LEGAL_CONFIGURATION.md`).
2. Los secretos productivos nuevos (`04_SECRETS.md`).
3. La creación de los recursos en Railway, que puede tener costo (`14_COSTS.md`).
4. El correo real del primer `PlatformAdmin`.

## Decisión

Ver el apartado 20 de la entrega. El código está listo; el ambiente todavía no existe.
