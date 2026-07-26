# Cambios de modelo — V2-04

La migración `AddPickupOrders` agrega:

- `ordering_product_categories`: catálogo lógico por negocio.
- `ordering_products`: producto, categoría, precio de referencia y versión.
- `ordering_pickup_settings`: habilitación, mensaje, preparación mínima, intervalo, capacidad, horario y siguiente número.
- `ordering_pickup_orders`: número único por negocio, franja UTC, datos protegidos, hash público, consentimiento, totales, estado y versión.
- `ordering_pickup_order_lines`: producto opcional, nombre y precio congelados, cantidad, nota protegida y total.
- `CanManageOrders` en membresías.
- `PickupOrderId` opcional y único en comprobantes de consentimiento.

## Integridad y concurrencia

- Claves foráneas compuestas con `BusinessId` impiden referencias cruzadas entre establecimientos.
- Índices únicos: `(BusinessId, PublicOrderNumber)` y `PublicCodeHash`.
- La creación bloquea la configuración con `FOR UPDATE`, toma un `pg_advisory_xact_lock` derivado de negocio/franja, vuelve a leer disponibilidad/precios y verifica capacidad antes de confirmar.
- Las líneas conservan nombre, precio unitario y total históricos aunque el catálogo cambie.
- Pedidos, productos, categorías, configuración y membresías usan tokens de concurrencia.
