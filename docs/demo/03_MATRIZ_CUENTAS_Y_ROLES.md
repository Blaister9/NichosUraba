# Matriz de cuentas y roles

| Correo | Rol | Negocio | Módulo | Ruta principal | Estado |
|---|---|---|---|---|---|
| `admin@urabaconecta.demo` | PlatformAdmin | Plataforma | Administración global | `/admin/negocios` | Activa |
| `socia@urabaconecta.demo` | PartnerOperator | Consola de socias | Preparación de negocios | `/panel` | Activa |
| `propietaria@bella.demo` | BusinessOwner | Salón Bella Urabá | Appointments | `/panel/11111111-1111-1111-1111-111111111111/citas` | Activa |
| `propietario@corte.demo` | BusinessOwner | Barbería El Corte | VirtualQueues | `/panel/33333333-3333-3333-3333-333333333333/turnos` | Activa |
| `propietario@sazon.demo` | BusinessOwner | Restaurante Sazón Local | PickupOrders | `/panel/55555555-5555-5555-5555-555555555555/pedidos` | Activa |

## Reglas de acceso

- PlatformAdmin usa una credencial exclusiva.
- Las cuatro cuentas operativas usan el secreto Demo operativo administrado en
  Railway.
- Ninguna credencial se documenta ni se incluye en capturas.
- Cada BusinessOwner solo administra el negocio de su membresía.
- PartnerOperator prepara negocios, pero no los aprueba ni publica.
- Una invitación debe ser aceptada antes de que la membresía propietaria cuente
  como requisito completo.
