# Membresías y permisos

**Fecha:** 25 de julio de 2026  
**Rama:** `feat/v2-membresias-permisos`

## Alcance entregado

La ruta privada `/panel/{businessId}/equipo` permite listar membresías activas e inactivas, consultar cuenta, rol, permisos, fechas e historial; vincular una cuenta existente por correo exacto; crear una cuenta ficticia solo en Development; cambiar permisos; activar o desactivar acceso; y conceder o retirar propiedad.

Cuenta Identity, membresía de autorización y perfil operativo continúan separados. Desactivar una membresía no elimina la cuenta, perfiles operativos, citas ni historia.

## Permisos

- `IsOwner`: se representa con `MembershipRole.Owner` y concede los tres permisos de forma efectiva.
- `CanManageAppointments`: autoriza lectura y cambios de citas.
- `CanManageConfiguration`: autoriza servicios, personal, horarios y excepciones.
- `CanManageMembers`: autoriza equipo y permisos.

Cada solicitud privada vuelve a consultar la membresía activa. Los roles globales son una puerta de autenticación, no la decisión de acceso al establecimiento.

Un miembro no propietario no se autoasigna permisos, no concede permisos que no posee, no administra propietarios y no concede propiedad. Solo un propietario activo concede o retira propiedad.

## Último propietario y concurrencia

Toda mutación de membresía abre una transacción PostgreSQL y bloquea con `FOR UPDATE` las membresías del establecimiento. Antes de desactivar o retirar propiedad se cuenta nuevamente a los propietarios activos. Si quedaría cero, responde `409 LAST_OWNER_REQUIRED`. El token `Version` rechaza ediciones obsoletas con `409 CONCURRENCY_CONFLICT`.

## Cuentas ficticias

`POST .../memberships/create-development` solo se mapea en Development. La contraseña temporal usa aleatoriedad criptográfica, se devuelve una sola vez y no entra en auditoría, logs ni documentación. La infraestructura actual no fuerza cambio en el primer ingreso; esa limitación se muestra en pantalla. Fuera de Development la creación directa no aparece y se informa que la invitación productiva está pendiente.

## Auditoría

`membership_audit_entries` conserva de forma inmutable establecimiento, membresía, actor, acción, UTC y estados relevantes anteriores/nuevos en JSON controlado. Acciones: `MemberLinked`, `MemberActivated`, `MemberDeactivated`, `PermissionsChanged`, `OwnerGranted` y `OwnerRevoked`. No guarda contraseñas, cookies, tokens ni códigos públicos.

## Rutas

- UI: `/panel/{businessId}/equipo`
- API: `GET /api/v1/businesses/{businessId}/memberships`
- API: `GET /api/v1/businesses/{businessId}/memberships/{membershipId}`
- API: `POST .../memberships/link-existing`
- API Development: `POST .../memberships/create-development`
- API: `PUT .../memberships/{membershipId}/permissions`
- API: `POST .../memberships/{membershipId}/activate|deactivate|grant-owner|revoke-owner`
- API: `GET .../memberships/{membershipId}/audit`

La interfaz usa tarjetas, controles táctiles y formularios de una columna en móvil; se verificó a `360 × 800` sin desplazamiento horizontal.
