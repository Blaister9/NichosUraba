# Cambios de modelo de onboarding

Migración: `20260726052940_AddPlatformOnboarding`.

Cambios principales:

- `BusinessStatus`: `Draft`, `PendingConfiguration`, `Active`, `Suspended`, `Archived`.
- `businesses`: enlaces públicos opcionales, motivo de suspensión, fechas y `Version` de concurrencia.
- `business_modules`: función, habilitación, fecha y versión; clave compuesta por negocio/función.
- `platform_audit_entries`: acción, actor, instante y estados serializados.
- `AspNetUsers.MustChangePassword`: obliga el cambio de clave piloto.

Los tres negocios demo reciben funciones explícitas mediante un seed idempotente. La migración conserva los negocios existentes activos y publicados.

Aplicación:

```powershell
dotnet ef database update --project src/UrabaConecta.Infrastructure --startup-project src/UrabaConecta.Web/UrabaConecta.Web
dotnet ef migrations has-pending-model-changes --project src/UrabaConecta.Infrastructure --startup-project src/UrabaConecta.Web/UrabaConecta.Web
```
