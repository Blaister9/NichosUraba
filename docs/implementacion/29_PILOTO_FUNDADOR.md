# Piloto fundador — avisos confiables y capacidades por negocio

Estado: implementado y probado en local. **No se creó ningún entorno Production.**

## 1. El problema que se venía a resolver

En uso real de la Demo pasaron tres cosas:

- algunos Push de la barbería llegaron y otros no;
- un pedido de Lúmina no avisó al operador;
- varios cambios de estado —aceptado, rechazado— no llegaron al cliente.

La causa no era el envío. Era que **el envío ERA el aviso**: los casos de uso llamaban al servicio
Web Push justo después de guardar la operación, y si esa llamada fallaba no quedaba nada. Ni una
fila, ni un registro, ni una pantalla donde mirar. Y encima, tres fallos cualesquiera —incluida una
caída pasajera del proveedor— desactivaban la suscripción para siempre.

## 2. Cómo quedó

```
hecho de negocio (cita, turno, pedido, cambio de estado)
   │
   ├─► notifications             fila durable. Es la bandeja. Se escribe SIEMPRE.
   ├─► SignalR                   acelerador. Si falla, sólo se pierde inmediatez.
   └─► notification_deliveries   una fila por dispositivo elegible
          └─► trabajador de fondo: reparte, envía, reintenta, diagnostica
```

`INotificationPublisher` es la única puerta. No habla con nadie de fuera y no puede tumbar la
operación que lo llamó: **si el proveedor Push está caído, el pedido se crea igual y el negocio lo
encuentra en su bandeja.**

Reglas que fija el dominio:

| Situación | Qué pasa |
|---|---|
| Entrega correcta | `Sent`; la suscripción limpia su racha de fallos |
| 404 o 410 | `Expired`, sin reintento, y **la suscripción se retira**: el navegador dice que ese destino ya no existe |
| Cualquier otro fallo | `Pending` con espera creciente —0 s, 30 s, 2 min, 10 min, 30 min, 2 h— y **el dispositivo NO se toca** |
| Se agotan los seis intentos | `Abandoned`. Deja de intentarse; el aviso sigue en la bandeja |
| Sin dispositivos | El aviso se guarda igual y queda repartido con cero entregas |
| Doble clic | Un solo aviso: la clave de deduplicación es única en la base |

El trabajador reclama lotes con `FOR UPDATE SKIP LOCKED`, así que dos instancias no envían lo mismo
dos veces. Un reinicio no pierde nada: lo que quedó en `Pending` sigue en `Pending`.

### Lo que se mira cuando alguien dice "no me llegó"

- **Propietario**: `/panel/{negocio}/avisos` lleva la bandeja y, debajo, cuántos dispositivos tiene
  activos, cuántos avisos se entregaron hoy y cuántos quedaron en cola.
- **Administración técnica**: `/admin/salud` añade el estado del buzón en toda la plataforma, con
  los negocios ordenados por avisos abandonados y marcando cuáles son decorado.

Ninguna de las dos guarda ni enseña el endpoint del dispositivo. El motivo de un fallo se registra
como tipo de excepción y código, nunca el mensaje del proveedor: ese mensaje puede llevar dentro la
dirección que identifica el navegador de una persona.

## 3. Capacidades: una plataforma, no cinco aplicaciones

`BusinessModuleKind` pasó de tres valores a seis.

- **Operaciones** —`Appointments`, `VirtualQueues`, `PickupOrders`—: lo que el negocio abre al
  público. Al menos una es obligatoria para publicar.
- **Material** —`Services`, `Products`, `Staff`—: lo que esas operaciones consumen. Se deducen
  (citas traen servicios y personal; pedidos traen productos) y se pueden fijar a mano cuando un
  negocio necesita una combinación que la deducción no acierta.

La categoría **propone** una combinación al dar de alta y sirve para que la gente encuentre el
negocio. No decide funciones: eso habría convertido cada vertical nueva en un condicional más.

Consecuencias visibles:

- la configuración de una droguería ya no ofrece Servicios, Personal ni Turnos;
- y ocultar la tarjeta no es la única defensa: escribir la dirección a mano devuelve
  `CAPABILITY_DISABLED`, que la pantalla traduce a "este establecimiento no tiene esa función
  habilitada" —distinto de "no tiene acceso", que mandaba a pedir permisos que ya se tienen—.

Categorías del piloto sembradas en todos los ambientes, sólo insertando lo que falta:
`odontologia`, `veterinarias`, `spa-y-belleza`, `droguerias`, `opticas`.

## 4. Tiempo real

`QueueHub` no se tocó. `OperationsHub` en `/hubs/operations` cubre pedidos, citas y la bandeja.

Lo que viaja por el cable es una señal, nunca datos: quien la recibe vuelve a preguntar a la API,
que es donde vive la autorización. Unirse al grupo de un negocio exige membresía activa y el permiso
del canal, porque saber *cuándo* entra un pedido en un negocio ajeno ya es saber algo.

La conexión se abre **después** del primer render interactivo. Abrirla durante el prerenderizado
añadía una negociación que se descarta al terminar de pintar, y ese coste se cobraba en el tiempo de
respuesta del documento.

## 5. Migración

Una sola, aditiva: `20260822144024_AddNotificationOutbox`. Crea `notifications` y
`notification_deliveries`. No altera ninguna tabla existente y su `Down` sólo borra las dos nuevas.

Los tres valores nuevos de `BusinessModuleKind` **no** necesitaron esquema: la columna ya guardaba
el nombre como texto.

## 6. Pruebas

| Suite | Resultado |
|---|---|
| Dominio | 207 correctas |
| Integración | 273 correctas (13 nuevas, sólo del buzón) |
| Extremo a extremo | 71 correctas (10 nuevas: las cinco verticales y la composición móvil) |

Las trece del buzón cubren: proveedor caído, 404, 410, dos dispositivos con distinta suerte,
reintento con espera, agotamiento de intentos, reinicio del trabajador, doble clic, bandeja ajena y
código de seguimiento inexistente.

La primera versión de esas pruebas **no pasaba**, y por una razón buena: la reserva de la entrega se
escribía con SQL directo y EF no veía que se soltaba al terminar, así que cada fallo pasajero
arrastraba dos minutos de reserva caducando antes de poder reintentar. Sin las pruebas, el defecto
habría llegado al piloto como "los avisos tardan".

## 7. Deuda que no bloquea el piloto

- **La bandeja es del negocio, no de cada persona.** Marcar leído lo marca para todo el equipo. Se
  guarda quién lo leyó, así que separarlo por persona más adelante no pierde información. Para un
  piloto de uno a tres operadores por negocio, un buzón compartido es el modelo honesto.
- **`OperationsHub` no tiene límite de peticiones por invocación**, igual que `QueueHub`. El código
  de seguimiento son 132 bits de un generador criptográfico y el concentrador sólo responde
  encontrado/no encontrado, así que la fuerza bruta no es practicable; queda anotado porque es una
  puerta más con el mismo patrón.
- **Un cuerpo JSON con UTF-8 inválido devuelve 500 y no 400.** Es comportamiento del marco anterior
  a este trabajo; el manejador de excepciones no traduce `BadHttpRequestException`.
- **Las promociones ahora se encolan en vez de enviarse en el acto.** El mensaje de la pantalla dice
  "en camino a N dispositivos" y no "enviada a N", que es lo cierto.
