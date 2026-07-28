# 13 — Plan de reversión

## Principio

Las migraciones de V5 son **aditivas**: agregan columnas anulables, tablas e índices nuevos, y no
eliminan ni renombran nada existente. Por eso la versión anterior sigue funcionando contra la base
ya migrada, y revertir el código no exige revertir la base.

Migraciones introducidas:

- `AddFounderProduction` — campos nuevos del perfil, `business_images`, `access_invitations`,
  `business_status_changes`, `platform_access_audits`.
- `AddQueueConsentEvidence` — `QueueTicketId` e `IpAddress` en `consent_receipts`.

## Decisión rápida

| Situación | Acción |
| --- | --- |
| La aplicación no arranca por configuración | Corrija las variables. **No** reverse: el arranque falló antes de tocar la base |
| Un defecto funcional sin corrupción de datos | Reversión de código (A) |
| Datos corrompidos o migración a medias | Restauración de la base (B) |
| Se perdió el volumen de llaves | Restauración del volumen (C). Es lo más urgente |

## A. Reversión de código

1. Identifique el commit anterior estable.
2. Despliegue esa revisión en el proveedor.
3. Verifique `/health/ready` y los *smoke tests* 1 a 5 del runbook.

Las columnas y tablas nuevas quedan presentes pero sin uso. No hace falta borrarlas.

**Excepción:** si ya se emitieron invitaciones, revertir a una versión sin
`/Account/AcceptInvitation` deja esos enlaces inutilizables. Revóquelos y vuelva a emitirlos
después.

## B. Restauración de la base de datos

1. Detenga el servicio de aplicación para que nadie escriba.
2. Restaure sobre una base **temporal** y compare conteos
   ([09](09_BACKUP_Y_RESTORE.md)).
3. Sólo si los conteos cuadran, apunte la aplicación a la base restaurada o reemplace la
   original.
4. Arranque y ejecute los *smoke tests* completos.

Se pierde todo lo ocurrido entre el respaldo y el incidente. Comuníquelo a los negocios afectados.

## C. Restauración del volumen de llaves

Síntoma: las sesiones se cierran y los alias y teléfonos aparecen ilegibles.

1. Detenga el servicio.
2. Restaure el volumen desde el respaldo, preferiblemente la copia inicial bloqueada.
3. Arranque y verifique que una cita anterior muestra correctamente el alias del cliente.

Sin ese respaldo, los datos personales cifrados **no son recuperables**. Es el riesgo que
justifica la copia inicial bloqueada.

## D. Reversión de las imágenes

Las imágenes en R2 no se ven afectadas por una reversión de código. Si se revirtió la base a un
punto anterior a una carga, la fila desaparece pero el objeto queda huérfano en el bucket: no
rompe nada y puede limpiarse después comparando con `business_images.StorageKey`.

## Si hubiera que revertir la base

No es necesario para V5, pero si se decidiera:

```bash
dotnet ef database update AddPlatformOnboarding \
  --project src/UrabaConecta.Infrastructure \
  --startup-project src/UrabaConecta.Web/UrabaConecta.Web
```

Esto **elimina** `business_images`, `access_invitations`, `business_status_changes`,
`platform_access_audits` y las columnas nuevas, con los datos que contengan. Tome un respaldo
antes y trátelo como último recurso.

## Después de cualquier reversión

1. Registre qué pasó, qué se revirtió y qué datos se perdieron.
2. Avise a las socias y a los negocios afectados.
3. Corrija la causa antes de volver a desplegar.
4. Repita el checklist de [12_GO_LIVE_CHECKLIST.md](12_GO_LIVE_CHECKLIST.md).
