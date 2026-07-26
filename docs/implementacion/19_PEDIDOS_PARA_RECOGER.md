# V2-04 — Pedidos para recoger

## Alcance implementado

Vertical demostrativa para **Restaurante Sazón Local**, Carepa (`restaurant-sazon-local`). Permite consultar menú por categorías, armar un carrito, escoger una franja con capacidad, crear un pedido sin cuenta ni pago en línea, conservar un enlace privado y seguir su estado.

El panel privado separa dos capacidades:

- `CanManageOrders`: consulta datos del cliente y opera estados.
- `CanManageConfiguration`: administra configuración, categorías, productos, disponibilidad y precios.

Las cuentas propietarias conservan ambas capacidades implícitamente. La asignación o revocación se evalúa en cada solicitud.

## Estados

`Pending → Accepted → Preparing → ReadyForPickup → Delivered`.

También se admite `Pending → Rejected` y cancelación desde estados activos. El cliente solo puede cancelar desde `Pending` o `Accepted`. Toda transición exige la versión observada; rechazo y cancelación exigen motivo.

## Seguridad y privacidad

- Alias, celular y notas se almacenan protegidos mediante ASP.NET Core Data Protection.
- El enlace público contiene un código aleatorio; PostgreSQL guarda únicamente su HMAC.
- El seguimiento muestra solo los últimos cuatro dígitos del celular y no expone notas.
- El consentimiento queda registrado y relacionado con el pedido.
- No se reciben documentos, correo, dirección ni datos de pago.

## Fuera de alcance

Pagos, domicilios, inventario, cocina/KDS, facturación electrónica, notificaciones externas, promociones y multi-sede.
