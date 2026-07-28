# 03 — Invitaciones y accesos

## Principio

**Nadie, ni siquiera la administración, conoce la contraseña de otra persona.** No se muestran
contraseñas, no se envían por WhatsApp y no se generan credenciales fijas. Cada persona define
su propia contraseña al abrir un enlace de un solo uso.

## Cómo funciona el enlace

1. El administrador (o la socia, para su negocio) escribe el correo y el nombre visible.
2. El servidor genera un token aleatorio de 256 bits.
3. Se persiste **sólo el HMAC-SHA256** del token; el valor en claro no se guarda en ninguna parte.
4. Se devuelve una única vez la ruta `/Account/AcceptInvitation?token=…` para copiarla.
5. La persona abre el enlace, ve para qué es y define su contraseña.
6. El token queda consumido, la cuenta activa y la acción registrada en auditoría.

El enlace vence a las 72 horas por defecto (configurable entre 1 y 720 horas). Un reinicio
administrativo de contraseña usa 4 horas por defecto.

## Estados de una invitación

| Estado | Significado |
| --- | --- |
| `Pending` | Vigente y sin usar |
| `Accepted` | Ya se consumió; no admite un segundo uso |
| `Revoked` | Anulada manualmente |
| `Expired` | Venció por tiempo |

Tras cinco intentos fallidos, la invitación queda bloqueada 15 minutos.

## Operaciones disponibles

| Acción | Quién | Efecto |
| --- | --- | --- |
| Invitar socia | PlatformAdmin | Crea la cuenta al aceptar y le asigna el rol `PartnerOperator` |
| Invitar propietario o personal | PlatformAdmin o la socia responsable | Crea la membresía en el negocio al aceptar |
| Reenviar | Quien puede administrar esa invitación | **Anula el enlace anterior** y emite uno nuevo |
| Revocar | Quien puede administrar esa invitación | El enlace deja de servir de inmediato |
| Reiniciar acceso | PlatformAdmin | Cierra las sesiones abiertas y emite un enlace temporal |
| Revocar socia | PlatformAdmin | Quita el rol y cierra sus sesiones |

## Cambio de contraseña por la propia persona

Disponible en `/Account/Manage/ChangePassword`. Al fijar una contraseña nueva se renueva el
*security stamp*, lo que cierra las demás sesiones abiertas.

## Bloqueo por intentos de inicio de sesión

Identity está configurado explícitamente: cinco intentos fallidos bloquean la cuenta 15 minutos.

## Cuentas Demo

Las cuentas ficticias (`admin@urabaconecta.demo`, `socia@urabaconecta.demo`, …) sólo existen en
Development y en Demo, y en Demo exigen dos secretos externos distintos
(`DemoSeed__AdminPassword` y `DemoSeed__BusinessPassword`).

En Production el arranque **falla** si la semilla está habilitada, si esos secretos están
definidos o si se detecta una contraseña de demostración conocida. Ver
[09_BACKUP_Y_RESTORE.md](09_BACKUP_Y_RESTORE.md) y
[11_RUNBOOK_PRODUCCION.md](11_RUNBOOK_PRODUCCION.md).

## Lo que esta versión todavía no hace

- **No hay envío de correo.** `IdentityNoOpEmailSender` sigue siendo un no-operativo, así que
  `/Account/ForgotPassword` no entrega nada. La recuperación de acceso para el piloto se hace
  por el reinicio administrativo descrito arriba, entregando el enlace por un canal directo.
- Para incorporar recuperación autoservicio hay que contratar un proveedor de correo. Está
  registrado en [15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).

## Cómo entregar un enlace de forma segura

1. Genérelo desde la interfaz y cópielo con el botón del campo.
2. Entréguelo por un canal directo con la persona (llamada, mensaje personal).
3. Pídale que lo abra el mismo día. Si vence, use «Reenviar».
4. Nunca lo publique en un grupo ni lo reenvíe a terceros: quien tenga el enlace puede fijar
   la contraseña de esa cuenta hasta que se use o venza.
