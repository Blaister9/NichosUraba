# Cambios de modelo — turnos virtuales

## Migración

`AddVirtualQueues`

## Objetos

| Objeto | Propósito y restricciones |
|---|---|
| `business_memberships.CanManageQueues` | permiso granular; `false` para membresías existentes, con acceso implícito para `Owner` |
| `queue_definitions` | configuración persistente; una definición activa por negocio mediante índice parcial |
| `queue_sessions` | jornadas históricas; una `Open` o `Paused` por definición mediante índice parcial |
| `queue_tickets` | número, fuente, estado, HMAC, alias protegido, fechas y versiones |

Índices principales:

- único `(QueueSessionId, Number)`;
- único `PublicCodeHash`;
- `(BusinessId, Status)`;
- `(QueueSessionId, Status, Number)`;
- claves alternativas `(BusinessId, Id)` para FKs compuestas.

Las FKs compuestas impiden asociar jornadas o turnos con otro establecimiento. Definición, jornada, turno y membresía usan token de concurrencia.

## Compatibilidad y reversión

Las membresías anteriores permanecen sin permiso explícito de turnos; propietarias conservan acceso efectivo por rol. No cambian citas, servicios, personal ni auditoría histórica.

Revertir a `AddMembershipAdministration` elimina configuración, jornadas, turnos y `CanManageQueues`. Antes de hacerlo se debe respaldar PostgreSQL: la historia de filas se perdería.
