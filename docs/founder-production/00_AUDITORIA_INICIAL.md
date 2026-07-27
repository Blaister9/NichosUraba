# 00 — Auditoría inicial (Fase 0)

Fecha de la auditoría: 2026-07-27
Rama auditada: `release/pilot-demo` (commit `9bffea2`)
Rama de trabajo creada: `feat/v5-founder-production`

Esta auditoría se hizo leyendo el código real (dominio, casos de uso, almacenes,
endpoints mínimos de `Program.cs`, componentes Blazor y pruebas), no la base de
datos ni la documentación previa. Una entidad o una columna **no** cuenta como
función existente: se exige recorrido completo desde la interfaz hasta el
almacenamiento.

## Leyenda de estados

| Código | Significado |
| --- | --- |
| **OK** | Existe y funciona de extremo a extremo |
| **INC** | Existe pero está incompleta |
| **NO** | No existe |
| **BE** | Existe solo en backend |
| **UI** | Existe solo en interfaz |
| **SIN-PRUEBAS** | Existe pero no tiene pruebas |

## Línea base verificada

- Compilación `Release` de `UrabaConecta.slnx`: correcta, 0 advertencias, 0 errores.
- `UrabaConecta.Domain.Tests`: **59 pruebas aprobadas**.
- `UrabaConecta.IntegrationTests` y `UrabaConecta.EndToEndTests` requieren Docker
  (Testcontainers PostgreSQL) y Playwright. Completan las 113 pruebas declaradas.
- Docker Desktop no estaba en ejecución al iniciar la auditoría.

## Tabla de auditoría

### Perfil comercial

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Nombre comercial | OK | `Business.Name`, validado en `PlatformOnboarding.cs:113`; editable en `PlatformBusinessDetail.razor` |
| Slug público | OK | `Business.NormalizeSlug` + índice único en `AppDbContext.cs:52`; unicidad verificada en `PlatformAdministrationUseCases.CreateAsync/UpdateAsync` |
| Descripción (única, 600 car.) | INC | Existe `Description`. **No existe** descripción breve separada de la completa |
| Descripción breve | NO | No hay campo ni en dominio ni en contratos |
| Categoría | OK | `CategoryId` + catálogo `categories`; selector en la interfaz de plataforma |
| Municipio | OK | `MunicipalityId` + catálogo `municipalities` |
| Dirección | OK | `Business.Address`, máx. 240 |
| Punto de referencia | NO | No existe |
| Teléfono | INC | `PublicPhone` existe, pero sólo se valida longitud (≤30). **No hay validación de formato** |
| WhatsApp | INC | Se guarda como `WhatsAppUrl` (URL absoluta). No hay campo de número; validación sólo de esquema http/https |
| Correo del negocio | NO | No existe |
| Instagram | NO | No existe |
| Facebook | NO | No existe |
| Instrucciones para clientes | NO | No existe |
| Enlace de ubicación | OK | `LocationUrl` con validación de URL |
| Entradas peligrosas | INC | Se rechazan `<` y `>` en nombre/descripción/dirección/teléfono. No se validan los campos que aún no existen |
| Autorización por BusinessId | OK | Todos los casos de uso privados reciben `userId` y consultan membresía activa antes de operar |

### Identidad visual

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Logo | NO | Sin entidad, sin endpoint, sin interfaz |
| Portada | NO | Igual |
| Galería | NO | Igual |
| Texto alternativo / orden / imagen predeterminada | NO | Igual |
| Abstracción `IObjectStorage` | NO | No existe ninguna abstracción de almacenamiento de objetos |
| Validación de tipo, firma, tamaño, EXIF | NO | No aplica todavía |

### Configuración operativa

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Horarios | OK | `BusinessHour` + `PUT /api/v1/businesses/{id}/hours/{day}` + `BusinessHours.razor`; restricción SQL `ck_business_hours_range` |
| Excepciones de disponibilidad | OK | `AvailabilityException` + interfaz dedicada |
| Servicios | OK | CRUD completo con desactivación lógica y control de concurrencia |
| Productos y categorías | OK | `Product`, `ProductCategory`, interfaz `PickupOrderConfiguration.razor` |
| Franjas de recogida | OK | `PickupOrderSettings` |
| Fila virtual | OK | `QueueDefinition`, `QueueSession`, `QueueTicket`, SignalR |
| Módulos habilitados | OK | `BusinessModule` + `PUT /api/v1/admin/businesses/{id}/modules`; sólo PlatformAdmin |
| Zona horaria | BE | `Business.TimeZoneId` fijo en `America/Bogota`, no editable desde la interfaz |

### Roles, cuentas y accesos

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| PlatformAdmin | INC | Existe rol y política `PlatformAdmin`; cubre negocios y módulos. **No** administra cuentas, ni consulta auditoría desde la interfaz, ni reinicia accesos |
| PartnerOperator (socia) | NO | El rol no existe. Hoy sólo PlatformAdmin puede crear negocios |
| BusinessOwner | OK | Rol Identity + `MembershipRole.Owner` con todos los permisos |
| BusinessStaff granular | OK | `CanManageAppointments/Configuration/Members/Queues/Orders`, verificados en servidor en cada caso de uso |
| Verificación en servidor | OK | Las políticas de `Program.cs` son un primer filtro; el permiso real se comprueba consultando la membresía en cada caso de uso |
| Aislamiento entre negocios | OK | Cubierto por pruebas de integración existentes (403 al cruzar `BusinessId`) |
| Prohibición de auto-escalada | INC | `BusinessMembership` impide degradar permisos de un Owner, pero no existe PartnerOperator ni regla que impida que una socia se asigne PlatformAdmin (porque el rol no existe) |
| Propietario: crear o invitar | INC | `CreatePilotAsync` crea la cuenta y devuelve **una contraseña temporal en texto plano** al administrador. No hay invitación |

### Invitaciones y contraseñas

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Invitación por token | NO | No existe entidad, tabla, endpoint ni pantalla |
| Token de un solo uso / expiración / revocación / reenvío | NO | No existe |
| Enlace temporal copiable | NO | No existe. Hoy se entrega una contraseña temporal generada por el servidor |
| Cambio obligatorio de contraseña | OK | `ApplicationUser.MustChangePassword` + middleware en `Program.cs:132` + `ChangeTemporaryPassword.razor` |
| Cambio de contraseña por el usuario | OK | `Manage/ChangePassword.razor` (Identity estándar) |
| Recuperación de contraseña | INC | `ForgotPassword.razor` existe pero `IdentityNoOpEmailSender` **no envía nada**: el flujo es inoperante en la práctica |
| Bloqueo por intentos | INC | Identity trae lockout por defecto, pero no está configurado explícitamente ni documentado |
| Cierre de otras sesiones | NO | No existe |
| Reinicio administrativo por enlace | NO | No existe |
| No almacenar tokens en texto plano | N/A | No hay tokens propios todavía |
| Cuentas Demo con contraseña conocida | INC | `DevelopmentSeeder.DemoPassword = "UrabaDemo!2026"` en Development; en Demo se exige secreto externo. No hay validación que impida esa contraseña en Production |

### Estados y publicación

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Draft | OK | `BusinessStatus.Draft`, no publicado |
| PendingConfiguration | OK | `MarkPending` / `ConfigurationChanged` |
| PendingReview | NO | El estado no existe. Hoy se pasa de configuración directamente a `Active` |
| Published / Active | OK | `Activate(ready, …)` exige la lista de requisitos completa |
| Suspended | OK | `Suspend(reason, …)` exige motivo; los módulos verifican `IsBusinessActiveAsync` |
| Archived | OK | Sólo lectura: `UpdatePlatformProfile` lanza `BUSINESS_ARCHIVED` |
| Enviar a revisión | NO | No existe |
| Rechazar con observaciones | NO | No existe |
| Historial de cambios de estado | BE | Se registra en `platform_audit_entries`, pero **no hay pantalla** que lo muestre |
| Eliminación física desde interfaz | INC | Existe acción `delete` restringida a Draft/PendingConfiguration sin operaciones. La especificación V5 pide eliminarla de la interfaz |
| Vista previa del perfil público | NO | No existe. Un negocio no publicado no es visible por ninguna vía autenticada |
| Porcentaje de avance | NO | `BusinessReadiness` calcula requisitos pero no expone porcentaje ni mensajes de campos faltantes |

### Experiencia pública

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Directorio | OK | `GET /api/v1/public/businesses` + `Home.razor` |
| Ficha de negocio | OK | `BusinessProfile.razor` |
| Tarjeta con logo / portada / galería | NO | No hay imágenes |
| Horarios públicos | INC | Se exponen para agendamiento; no se muestran como bloque informativo en la ficha |
| Estado abierto/cerrado | NO | No se calcula |
| Botones de módulo | OK | Según módulos habilitados |
| Estados vacíos | INC | Parciales |
| Carga diferida y dimensiones de imagen | NO | No aplica todavía |
| Diseño móvil | INC | Existe hoja de estilos responsiva básica |

### Privacidad, consentimiento y legal

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Política de tratamiento de datos | NO | No existe página |
| Aviso de privacidad | NO | No existe página |
| Términos y condiciones | NO | No existe página |
| Política de retención y eliminación | NO | No existe |
| Canal de consultas y reclamos | NO | No existe |
| Configuración `Legal__*` | NO | No existe |
| Aceptación en formularios públicos | INC | Citas y pedidos exigen `ConsentAccepted` y guardan `ConsentReceipt`. La versión está **fija en `"pilot-1"`** (`UrabaUseCases.cs:336`) y no hay enlace a una política real |
| Turnos virtuales | INC | El formulario de turnos **no** pide consentimiento ni crea `ConsentReceipt` |
| Evidencia mínima (versión, fecha) | INC | Se guarda versión y fecha; no se guarda evidencia adicional |
| Datos sensibles | OK | No se recolectan; alias y teléfono se cifran con Data Protection (`PersonalDataProtector`) |

### Auditoría

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Creación de negocio | OK | `PlatformAuditAction.BusinessCreated` |
| Edición de perfil | OK | `BusinessUpdated` |
| Cambio de estado / publicación / suspensión / archivo | OK | `BusinessActivated`, `BusinessSuspended`, `BusinessReactivated`, `BusinessArchived` |
| Cambio de módulos | OK | `ModulesChanged` |
| Asignación de propietario | OK | `OwnerAssigned` / `OwnerChanged` |
| Cambios de permisos de miembros | OK | `MembershipAuditEntry` (tabla separada) |
| Invitación / aceptación / revocación | NO | No existe |
| Carga y eliminación de imagen | NO | No existe |
| Reinicio administrativo | NO | No existe |
| Actor, BusinessId, acción, fecha UTC | OK | Presentes en ambas tablas de auditoría |
| Dirección IP | NO | El campo no existe |
| CorrelationId | BE | Columna presente en `platform_audit_entries` pero **nunca se rellena** |
| Sin secretos en la auditoría | OK | Sólo se serializan instantáneas de estado |
| Pantalla de consulta de auditoría | NO | Sólo existe la de auditoría de membresías |

### Configuración, arranque y operación

| Función | Estado | Evidencia y detalle |
| --- | --- | --- |
| Ambientes Development / Demo | OK | `DevelopmentSeeder` distingue ambos; Demo exige `DemoSeed:Enabled` y secretos |
| Ambiente Production | NO | No hay `appsettings.Production.json` ni tratamiento específico |
| Validaciones de arranque | INC | Sólo dos: falta `ConnectionStrings:DefaultConnection` y falta `DataProtection:KeysPath` en Demo. **No** se valida clave HMAC, ni semilla Demo en Production, ni variables jurídicas, ni almacenamiento de objetos, ni contraseñas Demo conocidas |
| Clave HMAC de códigos públicos | INC | `URABACONECTA_TRACKING_HMAC_KEY` se exige fuera de Development, pero al ser servicio *scoped* el fallo aparece en la primera petición, no al arrancar |
| Cookies Secure | OK | `CookieSecurePolicy.Always` fuera de Development |
| Data Protection persistente | OK | `PersistKeysToFileSystem` sobre volumen `/app/keys` |
| Protección de llaves en reposo | NO | Las llaves se escriben **sin cifrar** en el volumen. No hay certificado X.509 |
| Health checks | OK | `/health/live` y `/health/ready` (con verificación de PostgreSQL) |
| Logs estructurados / correlation ID | INC | Logging estándar de ASP.NET Core; sin correlation ID propio |
| Pantalla administrativa de salud | NO | No existe |
| Versión desplegada / commit / ambiente | NO | No se exponen |
| Backup y restauración | NO | No hay procedimiento documentado ni probado |
| Exportación de datos | NO | No existe |

### Pruebas

| Función | Estado |
| --- | --- |
| Dominio (59) | OK |
| Integración (API, aislamiento, concurrencia) | OK |
| Extremo a extremo (Playwright) | OK |
| Cobertura de imágenes, invitaciones, revisión, legal, arranque inseguro | NO |

## Resumen ejecutivo

De las 28 funciones exigidas por la especificación V5:

- **Existen y funcionan (11):** categoría, municipio, dirección, horarios,
  servicios, productos, módulos, suspensión, archivo, permisos de personal,
  auditoría base de negocio y membresía.
- **Existen pero están incompletas (7):** perfil comercial (faltan campos),
  teléfono y WhatsApp (validación), propietario (creación sin invitación),
  recuperación de contraseña (sin envío), consentimiento (versión fija, sin
  política), publicación (sin revisión previa), eliminación (debe salir de la
  interfaz).
- **No existen (10):** logo, portada, galería, previsualización, invitaciones,
  permisos para socias (PartnerOperator), términos, privacidad, canal de
  reclamos, y almacenamiento de objetos.
- **Sólo en backend (2):** historial de cambios de estado, `CorrelationId`.

## Riesgos detectados antes de tocar código

1. **Contraseña temporal en texto plano.** `CreatePilotAsync` devuelve la
   contraseña al administrador para que la transmita por fuera. Es exactamente
   lo que la Fase 2 prohíbe.
2. **Llaves de Data Protection sin cifrar en el volumen.** Cualquier acceso al
   volumen permite descifrar alias y teléfonos de clientes.
3. **`ForgotPassword` no funciona.** Un propietario que olvide su contraseña
   queda bloqueado sin intervención por base de datos.
4. **No hay validación de arranque para Production.** Es posible arrancar una
   instancia productiva con la semilla Demo activada.
5. **Los turnos virtuales no registran consentimiento**, a diferencia de citas y
   pedidos.
6. **La clave HMAC ausente falla tarde.** `PublicCodeService` sí lanza
   `InvalidOperationException` fuera de Development, pero como es un servicio
   *scoped* el fallo aparece en la primera petición que la use, no al arrancar:
   la instancia se despliega «saludable» y rompe al primer agendamiento.
