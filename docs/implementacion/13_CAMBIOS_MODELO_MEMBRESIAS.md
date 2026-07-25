# Cambios de modelo — membresías

## Migración

`20260725225146_AddMembershipAdministration`

## Cambios

| Objeto | Cambio |
|---|---|
| `business_memberships` | `CanManageAppointments`, `CanManageMembers`, `CreatedAtUtc`, `UpdatedAtUtc`, `DeactivatedAtUtc`, `Version` |
| `AspNetUsers` | `DisplayName` |
| `membership_audit_entries` | nueva tabla de historial inmutable |

Se conserva `CanManageConfiguration` existente y `Role` como fuente de `IsOwner`. La membresía sigue siendo única por `BusinessId + UserId`.

Índices agregados:

- `(BusinessId, IsActive)`;
- `(BusinessId, Role, IsActive)`;
- auditoría por `(BusinessId, OccurredAtUtc)`;
- auditoría por `(BusinessId, MembershipId, OccurredAtUtc)`.

La auditoría referencia negocio en cascada y membresía con borrado restringido. La FK de membresía a cuenta cambió a borrado restringido para evitar eliminar historial mediante borrado de Identity.

## Compatibilidad

La migración marca `CanManageAppointments=true` en membresías existentes para conservar el acceso previo a citas. Propietarios existentes reciben configuración y administración de miembros. Fechas existentes se inicializan en el instante de migración y el nombre visible se deriva temporalmente de la parte local del correo; el seed Development asigna nombres ficticios explícitos.

No se modifica ni elimina `StaffMember`, relaciones servicio-personal ni citas históricas.

## Reversión

Antes de revertir debe respaldarse PostgreSQL. Ejecutar:

```powershell
dotnet ef database update 20260725221914_AddPrivateBusinessConfiguration `
  --project src/UrabaConecta.Infrastructure `
  --startup-project src/UrabaConecta.Web/UrabaConecta.Web
```

La reversión elimina auditoría, nombre visible y los permisos/versiones nuevos; también restaura borrado en cascada desde Identity. Se perdería todo historial de membresías generado después de esta vertical.
