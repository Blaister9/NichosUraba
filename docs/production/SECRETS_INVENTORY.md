# Inventario de secretos y configuración

**Este documento contiene nombres y propósitos. Nunca valores.** Si alguna vez aparece un valor
real en este archivo, se trata de un secreto quemado: rótelo y elimínelo del historial.

Convención: en Railway las variables se escriben con doble guion bajo (`Legal__ResponsibleName`);
en `appsettings.json` el mismo valor se anida con dos puntos (`Legal:ResponsibleName`).

## Secretos (valores sensibles)

| Variable | Propósito | Production | Si se pierde | Si se filtra |
| --- | --- | --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | Cadena de conexión a PostgreSQL, con usuario y contraseña | Obligatoria | Sin acceso a datos | Acceso total a los datos personales: rotar de inmediato |
| `URABACONECTA_TRACKING_HMAC_KEY` | Deriva el hash de los códigos públicos de seguimiento | Obligatoria | **Todos los códigos entregados a clientes dejan de resolver** | Se pueden adivinar códigos de terceros: rotar y asumir la invalidación |
| `URABACONECTA_INVITATION_HMAC_KEY` | Deriva el hash de los tokens de invitación. Si falta, se reutiliza la anterior | Recomendada | Las invitaciones pendientes dejan de ser válidas | Se pueden falsificar invitaciones: rotar y revocar las pendientes |
| `ObjectStorage__AccessKey` | Identificador de la credencial de Cloudflare R2 | Obligatoria | No se pueden subir ni borrar imágenes | Rotar en R2 |
| `ObjectStorage__SecretKey` | Secreto de la credencial de R2 | Obligatoria | Igual que la anterior | Acceso de escritura al bucket: rotar en R2 |
| `DataProtection__CertificateBase64` | Certificado X.509 en base64 que cifra el anillo de llaves en reposo | Recomendada | Las llaves cifradas con él son irrecuperables | Quien tenga el volumen puede descifrar: rotar certificado y llaves |
| `DataProtection__CertificatePassword` | Contraseña del `.pfx` anterior | Con el anterior | Igual que el anterior | Igual que el anterior |
| `ProductionBootstrap__AdminPassword` | Contraseña temporal del primer administrador | Sólo en el primer arranque | Usar la recuperación administrativa | Rotar y **retirar la variable** |

## Configuración obligatoria no secreta

| Variable | Propósito |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Debe ser exactamente `Production` |
| `ObjectStorage__Provider` | `S3` en Production. El proveedor local es efímero |
| `ObjectStorage__ServiceUrl` | Extremo S3 de la cuenta de R2 |
| `ObjectStorage__Bucket` | Bucket de Production. No puede contener «demo» |
| `ObjectStorage__PublicBaseUrl` | Dominio público desde el que se sirven las imágenes |
| `ObjectStorage__Region` | `auto` para R2 |
| `DataProtection__KeysPath` | `/app/keys`, sobre volumen persistente |
| `DataProtection__ApplicationName` | Aísla el anillo de llaves entre ambientes |
| `Legal__ResponsibleName` | Responsable del tratamiento de datos |
| `Legal__Identification` | Identificación del responsable (NIT o cédula) |
| `Legal__Address` | Domicilio del responsable |
| `Legal__PrivacyEmail` | Canal de consultas y reclamos de datos personales |
| `Legal__SupportEmail` | Canal de soporte |
| `Legal__PolicyVersion` | Versión de la política que se exige aceptar |
| `Legal__PolicyEffectiveDate` | Fecha de vigencia de esa versión |
| `Deployment__Commit` | SHA desplegado, visible en la pantalla de salud |
| `Deployment__DeployedAtUtc` | Marca de despliegue |

## Configuración opcional

| Variable | Omisión | Propósito |
| --- | --- | --- |
| `Database__MigrateOnStartup` | `true` | Aplicar migraciones al arrancar. Apagarlo no permite servir con esquema atrasado: la readiness sigue exigiéndolo |
| `RateLimits__PublicWritesPerMinute` | `12` | Escrituras públicas por IP y minuto |
| `RateLimits__SensitiveReadsPerMinute` | `1200` | Consultas de seguimiento por IP y minuto |
| `DetailedErrors` | `false` | **Debe permanecer en `false`**: la aplicación se niega a arrancar con `true` |

## Prohibidas en Production

La aplicación **no arranca** si alguna de estas está definida:

- `DemoSeed__Enabled=true`, `DemoSeed__AdminPassword`, `DemoSeed__BusinessPassword`
- `DemoBootstrap__Enabled=true`, `DemoBootstrap__AdminEmail`, `DemoBootstrap__AdminPassword`
- `DemoAccess__SharedPassword`

## Reglas

1. Los secretos de Production son **nuevos**. No se reutiliza ninguno de Demo.
2. Cualquier secreto mostrado, pegado en un chat o proyectado durante la demostración se
   considera comprometido y no se lleva a Production.
3. Sólo viven en el panel de variables de Railway. No en Git, ni en `.env`, ni en capturas.
4. `ProductionBootstrap__*` se retira en cuanto el administrador cambia su contraseña.
5. Rotación anual, y de inmediato cuando alguien con acceso deja de necesitarlo.
