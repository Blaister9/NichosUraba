# Administración de plataforma

La superficie global vive en `/admin/negocios` y la API en `/api/v1/admin`. Ambas exigen el rol `PlatformAdmin`; una persona propietaria normal recibe `403`.

Permite listar, buscar, filtrar por estado/municipio/función, crear, editar información, consultar responsable, funciones y lista de preparación, activar, suspender, reactivar, archivar y eliminar borradores seguros. No muestra datos personales de clientes.

## Suspensión

Suspender exige motivo, despublica el negocio y bloquea citas, turnos y pedidos públicos nuevos. También bloquea abrir una jornada o agregar un turno presencial. Los seguimientos históricos y las lecturas privadas se conservan; el panel identifica el establecimiento suspendido.

## Auditoría

`platform_audit_entries` registra actor, fecha, acción y estados anterior/nuevo para altas, edición, funciones, responsable, cuenta piloto, activación, suspensión, reactivación, archivo y cambio de contraseña temporal. Nunca guarda contraseñas.

## Aislamiento

El administrador global no obtiene membresía. Los endpoints operativos continúan validando membresía y permisos por `BusinessId`; cambiar un identificador de URL no amplía acceso.
