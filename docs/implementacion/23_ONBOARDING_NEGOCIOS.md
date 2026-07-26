# Onboarding de negocios piloto

## Flujo

`PlatformAdmin` ingresa a `/admin/negocios`, crea el negocio mediante el asistente de cuatro pasos y define identidad pública, funciones, responsable y configuración inicial. El alta acepta una cuenta existente o crea una cuenta piloto solo en ambientes `Development` o `Demo`.

El negocio nace en `Draft`. Si se solicita continuar y la lista está completa, pasa por `PendingConfiguration` y se activa; si falta un requisito permanece privado. El administrador de plataforma no recibe membresía operativa.

## Estados

- `Draft`: privado y editable.
- `PendingConfiguration`: privado; requiere completar la lista.
- `Active`: publicado y habilitado para operaciones nuevas.
- `Suspended`: privado; conserva configuración, membresías e historial.
- `Archived`: privado y no editable.

Las transiciones y ediciones exigen `Version`; una escritura obsoleta responde `409`.

## Lista de preparación

Siempre exige información pública, responsable activo, al menos una función y permisos del responsable. Citas exige horario y servicio; turnos exige definición de fila; pedidos exige configuración de franjas, categoría y producto activos.

No se puede activar con requisitos pendientes. Cambiar funciones en un negocio activo lo devuelve a `PendingConfiguration`.

## Funciones

`Appointments`, `VirtualQueues` y `PickupOrders` se persisten en `business_modules`. Desactivar una función conserva sus tablas y datos, pero elimina su exposición pública.

## Eliminación

Solo se permite para `Draft` o `PendingConfiguration` sin citas, jornadas/turnos ni pedidos. En cualquier otro caso se debe archivar.
