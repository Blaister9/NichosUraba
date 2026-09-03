# J-MOTION-07 · Tracking / Activity Timeline Insertion · Pickup Orders

## Auditoría antes de implementar (2026-09-02, Bogotá)

Inherited HEAD: `083ccda301ee58454ced86e855095a61cca92172`, que es `origin/dev/motion-home`.
Worktree limpio nuevo: `C:/Users/santi/Documents/NichosUraba-j-motion-07`, rama
`codex/j-motion-07`. El checkout histórico con cambios de Owner/PlatformAdmin no se usa.

Superficie real: `/seguimiento/pedidos/{Code}` (`PickupOrderTracking.razor`, InteractiveServer).
«Mi actividad» es `/seguimiento` (`TrackingLookup.razor`): una consulta por código que además
recupera el último pedido guardado en el navegador. El panel del negocio que mueve el estado es
`/panel/{businessId}/pedidos` (`PickupOrderOperations.razor`).

Estados reales del dominio (`PickupOrderStatus`): `Pending`, `Accepted`, `Rejected`,
`Preparing`, `ReadyForPickup`, `Delivered`, `Cancelled`. Transiciones válidas, según
`PickupOrder.Transition`:

```
Pending        → Accepted | Rejected | Cancelled
Accepted       → Preparing | Cancelled
Preparing      → ReadyForPickup | Cancelled
ReadyForPickup → Delivered | Cancelled
```

`Rejected`, `Delivered` y `Cancelled` son finales. No hay ciclos: un pedido pasa por cada
etapa como mucho una vez. Ese hecho es el que sostiene la deduplicación de la historia.

## ¿Existía historial? Sí, y no hace falta tabla nueva

`PickupOrder` guarda un único `Status` con su `UpdatedAtUtc` y su `Version`: **no** hay
historial de transiciones en la entidad. Pero `ChangeStatusAsync` publica, en la misma
operación, un aviso al cliente por cada cambio —`OrderAccepted`, `OrderRejected`,
`OrderPreparing`, `OrderReady`, `OrderDelivered`, `OrderCancelled`—, y esa fila se guarda con
su `CreatedAtUtc` y con una clave única `Notification.Key(audiencia, tipo, pedido)`.

Es decir: ya existía un registro append-only, uno por etapa, imposible de duplicar, con hora
real y con identidad propia. La historia se lee de ahí. **Migración requerida: NO.** Crear una
tabla de auditoría habría sido sustituir un dato real por uno fabricado, y convertir
J-MOTION-07 en un sistema de auditoría general, que es justo lo que el encargo excluye.

Las dos costuras del aviso, cubiertas con fechas verdaderas del propio pedido:

1. **El primer hito.** Crear el pedido publica `OrderPlaced` a la *bandeja del negocio*, no al
   cliente. El hito «Pedido recibido» se deriva de `PickupOrder.CreatedAtUtc`, que ya existía
   como columna y sólo faltaba exponer en el DTO de seguimiento.
2. **La cancelación pública.** `CancelPublicAsync` cambia el estado y **no** publica aviso. Por
   eso el estado actual siempre se representa: si ningún hito lo cubre, se añade uno con
   `UpdatedAtUtc` y la etiqueta autoritativa del servidor. El encabezado y la historia no
   pueden contar cosas distintas.

## Dos defectos preexistentes que el recorrido destapó

Los dos estaban en `083ccda` y ninguno lo introduce este trabajo. Sin ellos J-MOTION-07 no
existe: la historia sólo crecería al recargar a mano.

**1. La suscripción en vivo pedía un código inexistente.** En `PickupOrderTracking.razor`:

```razor
<OperationsLive EntityType="PickupOrder" TrackingCode="Code" ... />
```

`TrackingCode` es `string`, así que Blazor pasaba la **cadena literal** `"Code"`, no el
parámetro. El hub respondía `HubException: Operación no encontrada`, el `catch` silencioso de
`OperationsLive` se lo tragaba y la pantalla no se enteraba de ningún cambio hasta recargar.
`BusinessId="BusinessId"` funciona por ser `Guid` —un parámetro no-string se compila como
expresión—, y por eso el panel sí tenía tiempo real. Corregido a `TrackingCode="@Code"`.
Comprobado con el registro del servidor antes y después.

`AppointmentTracking.razor` tiene **el mismo defecto** en su línea equivalente. No se toca:
las citas están fuera del alcance de esta misión. Queda reportado.

**2. La relectura devolvía el estado congelado.** `TrackAsync` leía con
`OrderingStore.FindByCodeAsync`, que es una consulta **con seguimiento**. En InteractiveServer
el `AppDbContext` vive lo que vive el circuito, así que la segunda lectura devolvía la
instancia ya cargada y el encabezado se quedaba en «Pendiente» mientras la historia —que se
lee con `AsNoTracking`— ya mostraba «Aceptado». Se añade `ReadByCodeAsync` con `AsNoTracking`
y `TrackAsync` la usa. `FindByCodeAsync` se conserva tal cual para `CancelPublicAsync`, que sí
necesita la entidad seguida para guardar.

## Las tres capas, que no se mezclan

| Capa | Qué responde | De dónde sale |
|---|---|---|
| CURRENT STATE | dónde está ahora | `order.Status` / `StatusLabel` del servidor |
| TIMELINE | qué ha pasado | hito derivado de `CreatedAtUtc` + avisos guardados |
| NEXT EXPECTED | qué falta | tabla de transiciones del dominio |

## State-first sheets

**TIMELINE INSERTION — el hito nuevo**

- Source history: `[Recibido]` · Target history: `[Recibido, Aceptado]`
- New event: el aviso guardado que aún no estaba en pantalla
- Trigger: `TrackingChanged` por SignalR → `Load()` → historia entera releída
- Driven by: estado, no gesto · Properties: `height` 0→auto, `opacity`, `translateY` −6→0
- Compose class: **AnimatedVisibility-like insertion** + *animateContentSize* (la lista crece)
- Interruptible: sí, `finish()` y rebase; ningún hecho depende de la animación
- Fallback: sin módulo JS la historia sale completa y en orden, sin viaje
- Reduced motion: sin recorrido; el hito nuevo queda marcado en el riel, quieto
- Why motion helps: distingue «esto acaba de ocurrir» de «la lista reapareció»

**ACTIVE → HISTORY — lo anterior no desaparece**

- El hito que era actual pierde su superficie y baja el peso de su texto; conserva su sitio
- Compose class: **coordinated layout transition** (transición de estado, no salida)
- Properties: `background-color` de la caja y de la marca, `--motion-history-settle`

**CURRENT STATUS — el encabezado**

- Compose class: **AnimatedContent-like replacement**, el que ya tenía la etiqueta `.status`
- Sale de la misma lectura que la historia, ahora sin caché de seguimiento

**SEMANTIC ESCALATION — `ReadyForPickup`**

- Único estado que pide algo de la persona. La marca gana peso **una vez** y se asienta
- Compose class: **single value transition**, `--motion-status-escalate`
- Sin pulso infinito, sin alarma

**CTA**

- No se inventan botones. «Cancelar pedido» sigue apareciendo mientras el dominio lo permite
  (`CanPublicCancel`), y desaparece solo cuando deja de permitirlo

MotionSites references: ninguna. No hizo falta investigación: la pieza es una inserción en
lista y una transición de estado, las dos ya descritas por la taxonomía Compose.

## Modelo de datos de la historia

- **Event identity**: el `Id` del aviso (Guid). El hito derivado usa `recibido`; el de respaldo
  del estado actual, `estado-actual`.
- **Ordering**: `CreatedAtUtc` ascendente; el orden canónico del recorrido sólo desempata
  hitos con la misma hora.
- **Deduplication**: por estado, y en el servidor por la clave única del aviso. Un snapshot
  repetido no inserta nada porque el hito ya está por identidad.
- **Out-of-order**: no aplica. Lo que llega por el cable es una señal, nunca datos: la pantalla
  vuelve a leer la historia **entera** y la ordena por hora. No hay deltas que puedan cruzarse.
- **Rapid updates**: tres transiciones seguidas dejan los tres hitos. Es lo contrario de
  J-MOTION-05: allí gana el último valor; aquí no se pierde ningún hecho, porque los hechos son
  filas del servidor y no estado acumulado en el navegador.

## Motion tokens

Tres, coherentes con los que ya existen y con la misma curva que asienta:

```
--motion-history-settle: .22s   /* micro estado: lo actual pasa a historia */
--motion-history-insert: .34s   /* el hito que entra */
--motion-status-escalate: .5s   /* el estado que pide algo de la persona */
```

Jerarquía: asentarse < entrar < escalar. El módulo los lee de la hoja de estilos; no los copia.

## Scroll y crecimiento

No se llama a `scrollIntoView` ni a `scrollTo`: quien está leyendo arriba no pierde su sitio.
La lista crece por definición, y lo que hay debajo cede el sitio en un movimiento acotado a un
solo elemento. La historia tiene un techo real de cinco hitos —creación más cuatro
transiciones—, así que no puede convertirse en veinte tarjetas.

## Alcance

No se tocan Home, la fila virtual, Product → Action, el panel del negocio, Appointments,
notificaciones ni la generación del código de seguimiento. `TrackingUpdates` se conserva
intacto porque lo sigue usando el seguimiento de citas; el pedido pasa a `OrderTimeline`, que
mantiene `data-testid="tracking-update"` y `data-kind` en los hitos que vienen de un aviso para
no romper lo que ya los comprobaba.
