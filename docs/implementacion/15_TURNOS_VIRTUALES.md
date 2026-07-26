# Turnos virtuales

**Fecha:** 25 de julio de 2026  
**Rama:** `feat/v2-turnos-virtuales`

## Alcance

La primera vertical de filas virtuales opera en la Barbería El Corte de Chigorodó. El visitante la encuentra en el directorio, abre `/negocios/barberia-el-corte/turnos`, consulta estado, turno actual, cantidad en espera y tiempo aproximado; puede tomar un turno con alias opcional y seguirlo mediante un código individual.

La configuración persistente (`QueueDefinition`) está separada de cada jornada (`QueueSession`). El modelo admite varias definiciones históricas, pero un índice parcial permite solo una activa por establecimiento. Una jornada puede estar `Open`, `Paused` o `Closed`; pausar conserva los turnos y cerrar se bloquea mientras haya turnos activos.

Los estados de turno son `Waiting`, `Called`, `InService`, `Completed`, `Skipped` y `Cancelled`. Las transiciones están en dominio. Un turno omitido puede volver a espera una sola vez. No existe cancelación masiva ni cierre silencioso.

## Operación privada

La ruta `/panel/{businessId}/turnos` ofrece llamar siguiente, volver a llamar, iniciar y completar atención, marcar inasistencia, devolver una vez a espera, cancelar y agregar una persona presencial. Los turnos presenciales usan la misma secuencia y se distinguen por `Source`.

La configuración vive en `/panel/{businessId}/configuracion/turnos`: habilitación, nombre, duración promedio, capacidad y mensaje público. Deshabilitar exige cerrar antes la jornada.

`CanManageQueues` es un permiso persistido e independiente. Una persona propietaria lo tiene de forma implícita; una trabajadora solo si se le concede. Cada solicitud vuelve a leer la membresía, por lo que revocación o desactivación tienen efecto inmediato.

## Privacidad y tiempo real

El flujo público solo acepta un alias corto opcional. Se protege con Data Protection; el código aleatorio de 128 bits se entrega una vez y en PostgreSQL solo queda HMAC-SHA256. No se solicitan teléfono, documento, nombre completo ni cuenta.

SignalR usa grupos separados:

- `queue-public:{definitionId}`: solo señal de cambio público;
- `queue-ticket:{ticketId}`: señal para el código individual;
- `queue-operations:{businessId}`: suscripción autenticada y autorizada.

El mensaje no lleva datos del turno: al recibirlo, el cliente vuelve a consultar HTTP. La reconexión repite la suscripción y refresca; seguimiento conserva además un refresco HTTP defensivo. No hay grupo global ni backplane.

## Concurrencia

Creación, numeración, llamado y comandos toman bloqueo PostgreSQL `FOR UPDATE` sobre la jornada. `NextNumber` se incrementa dentro de la transacción y `(QueueSessionId, Number)` es único. Los comandos operativos exigen la versión observada de jornada y turno; dos operadores con la misma versión producen un éxito y un `409 CONCURRENCY_CONFLICT`.

No se usaron bloqueos en memoria ni EF InMemory.
