# Riesgos y pendientes — V2-04

## Riesgos conocidos

- La capacidad representa pedidos activos, no carga real de cocina por complejidad del producto.
- Las franjas usan un único intervalo semanal del establecimiento y un horario diario común de recepción.
- El motivo rápido del panel es deliberadamente básico; antes de producción requiere captura explícita y catálogo de causas.
- La protección de datos depende de conservar el anillo de claves fuera del contenedor en producción.
- No hay notificaciones: la persona debe conservar y consultar el enlace.
- El rate limit actual es por IP; una red compartida puede agrupar clientes distintos.
- La prueba de demanda comercial y voluntad de pago sigue pendiente; esta vertical es un piloto técnico, no evidencia de nicho validado.

## Antes de producción

Definir retención/borrado de PII, respaldo de claves, auditoría de cambios de estado, observabilidad sin datos sensibles, recuperación del código, política de cancelación, accesibilidad formal, pruebas de carga y esquema de operación cuando la cocina se atrasa.

No incorporar pagos, domicilios o inventario hasta validar el uso real del flujo básico con establecimientos de Urabá.
