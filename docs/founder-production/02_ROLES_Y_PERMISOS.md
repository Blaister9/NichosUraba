# 02 — Roles y permisos

## Los cuatro perfiles

### PlatformAdmin — administración técnica

Un único perfil, para la persona que responde técnicamente por la plataforma.

Puede:

- ver y administrar **todos** los negocios;
- invitar y revocar socias (`PartnerOperator`);
- revisar, publicar, suspender, reactivar y archivar negocios;
- rechazar una revisión con observaciones;
- reiniciar el acceso de cualquier cuenta;
- consultar la auditoría de negocios y de accesos;
- abrir la pantalla de salud de la instalación.

### PartnerOperator — socia

Una cuenta individual por socia. Prepara negocios, pero no decide qué se publica.

Puede:

- crear negocios;
- completar el perfil comercial;
- seleccionar módulos;
- cargar logo, portada y galería;
- invitar a la persona propietaria y al personal del negocio;
- previsualizar la ficha pública;
- enviar a revisión;
- ver el historial de estados de sus negocios.

**No puede**, y está comprobado con pruebas:

| Intento | Resultado |
| --- | --- |
| Crear otra socia o promoverse a administradora | 403 |
| Publicar, suspender, reactivar o archivar | 403 |
| Rechazar su propia revisión | 403 |
| Ver o editar un negocio que no dio de alta | 403 |
| Listar socias o revocar accesos | 403 |
| Reiniciar la contraseña de otra cuenta | 403 |
| Consultar la auditoría global | 403 |
| Abrir la pantalla de salud | 403 |

El alcance se decide por `Business.CreatedByUserId`: una socia sólo ve, en la consola y en el
listado, los negocios que ella misma dio de alta.

### BusinessOwner — propietario del negocio

Administra únicamente su negocio y los módulos habilitados: citas, turnos, pedidos,
configuración, horarios y personal. No accede a la consola de plataforma.

### BusinessStaff — personal del negocio

Conserva los permisos granulares existentes, verificados en servidor en cada operación:

| Permiso | Alcance |
| --- | --- |
| `CanManageAppointments` | Ver y cambiar el estado de las citas |
| `CanManageConfiguration` | Servicios, horarios, excepciones, personal |
| `CanManageMembers` | Membresías y permisos del equipo |
| `CanManageQueues` | Fila virtual |
| `CanManageOrders` | Pedidos para recoger |

Una persona propietaria conserva siempre todos los permisos: el dominio rechaza degradarlos
(`OWNER_PERMISSIONS_REQUIRED`).

## Aislamiento entre negocios

Cada caso de uso privado recibe el `userId` autenticado y comprueba la membresía activa para el
`BusinessId` indicado antes de tocar nada. Un propietario que pida datos de otro negocio recibe
403, no una lista vacía.

## Cómo se decide el rol

`PlatformActor` se construye en el borde HTTP leyendo los *claims* de la petición
(`ClaimTypes.NameIdentifier`, `IsInRole`). El cliente no puede declarar su propio rol.

## Cambios de rol

- Un `PartnerOperator` se crea únicamente por invitación emitida por el `PlatformAdmin`.
- Revocar el rol de socia también invalida su *security stamp*, lo que cierra todas sus sesiones.
- El administrador no puede revocarse a sí mismo (`SELF_REVOKE_FORBIDDEN`).
