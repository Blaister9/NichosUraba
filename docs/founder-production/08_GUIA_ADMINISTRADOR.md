# 08 — Guía para la administración de plataforma

## Pantallas

| Ruta | Para qué |
| --- | --- |
| `/admin/negocios` | Listado y filtros de todos los negocios |
| `/admin/negocios/nuevo` | Alta guiada |
| `/admin/negocios/{id}` | Configuración, checklist, imágenes, accesos, revisión e historial |
| `/admin/negocios/{id}/vista-previa` | Ficha pública antes de publicar |
| `/admin/accesos` | Socias, invitaciones, reinicio de accesos y auditoría |
| `/admin/salud` | Estado de la instalación |

Ninguna está enlazada desde el sitio público y todas exigen el rol correspondiente.

## Incorporar una socia

1. `/admin/accesos` → **Invitar a una socia**.
2. Copie el enlace y entrégueselo directamente.
3. Cuando lo acepte, aparecerá en **Socias con acceso**.

Para retirarla, **Revocar acceso**: pierde el rol y se cierran sus sesiones. Los negocios que
haya creado no se ven afectados.

## Revisar un negocio

1. La socia lo envía a revisión: el estado pasa a **En revisión**.
2. Abra la vista previa y verifique:
   - la descripción breve se entiende sola en el directorio;
   - el logo y la portada se ven bien;
   - el teléfono y la dirección son correctos;
   - los horarios y el catálogo tienen sentido;
   - hay una persona propietaria asignada.
3. **Aprobar y publicar**, o **Devolver con observaciones** escribiendo qué corregir.

Las observaciones quedan visibles para la socia y en el historial de estados.

## Suspender y reactivar

Suspender exige un motivo. El negocio sale del directorio y deja de recibir operaciones nuevas;
el historial y los enlaces de seguimiento existentes se conservan. **Reactivar** lo devuelve a
publicado.

## Archivar

Deja el negocio en solo lectura administrativa. No se puede editar ni publicar sin una
restauración explícita. Es la salida correcta para un negocio que deja la plataforma: **no** hay
eliminación física desde la interfaz salvo para borradores sin ninguna operación registrada.

## Reiniciar el acceso de una cuenta

`/admin/accesos` → **Reiniciar el acceso de una cuenta**. Escriba el correo y genere el enlace.
Esto cierra de inmediato las sesiones abiertas de esa persona y le permite fijar una contraseña
nueva. Nunca se muestra la contraseña anterior.

## Auditoría

- **Auditoría de accesos** (`/admin/accesos`): invitaciones creadas, aceptadas, revocadas y
  reenviadas; reinicios administrativos; alta y revocación de socias. Incluye responsable, fecha
  UTC y dirección IP.
- **Auditoría por negocio** (`/api/v1/admin/businesses/{id}/audit`): creación, edición, cambios de
  módulos, cambios de estado y movimientos de imágenes.
- **Historial de estados** (en la pantalla del negocio): de qué estado a cuál, quién y por qué.

Ninguna de las tres almacena contraseñas, tokens, secretos ni cadenas de conexión.

## Pantalla de salud

`/admin/salud` muestra ambiente, versión, commit, fecha de despliegue, estado de la base de datos
y de las migraciones pendientes, proveedor y estado del almacenamiento de objetos, estado de las
llaves de Data Protection y si la semilla Demo está habilitada.

Si aparece la semilla habilitada en un ambiente productivo, la pantalla lo señala como una
inconsistencia grave.

## Antes de cada despliegue

Consulte [11_RUNBOOK_PRODUCCION.md](11_RUNBOOK_PRODUCCION.md) y
[12_GO_LIVE_CHECKLIST.md](12_GO_LIVE_CHECKLIST.md).
