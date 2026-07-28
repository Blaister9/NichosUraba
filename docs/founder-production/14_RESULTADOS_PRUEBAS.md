# 14 — Resultados de pruebas

Fecha de ejecución: 2026-07-27
Rama: `feat/v5-founder-production`
Entorno: Windows 11, .NET 10.0.301, Docker 28.0.4, PostgreSQL 17-alpine, Chromium por Playwright

## Compilación

```
dotnet restore UrabaConecta.slnx      correcto
dotnet build -c Release               0 advertencias, 0 errores
```

## Suite completa

| Proyecto | Antes de V5 | Después | Resultado |
| --- | --- | --- | --- |
| `UrabaConecta.Domain.Tests` | 59 | **95** | Todas aprobadas |
| `UrabaConecta.IntegrationTests` | 30 | **52** | Todas aprobadas |
| `UrabaConecta.EndToEndTests` | 24 | **25** | Todas aprobadas |
| **Total** | **113** | **172** | **172 aprobadas, 0 con error, 0 omitidas** |

No se eliminó ni se deshabilitó ninguna prueba.

## Pruebas existentes que se ajustaron, y por qué

| Prueba | Ajuste | Motivo |
| --- | --- | --- |
| `Activation_suspension_module_preservation…` | Ahora comprueba que un negocio recién creado **no** está listo, que activar da 400, y completa el checklist antes de publicar | El checklist de V5 exige identidad visual y contacto |
| Escenarios 1 a 5 de `PlatformOnboardingJourneyTests` | Pasan por completar el checklist, enviar a revisión y publicar | Ya no hay publicación automática al crear |
| `QueueApiTests` y `QueueJourneyTests` | Envían la aceptación del aviso | Los turnos ahora exigen consentimiento |

## Cobertura nueva

### Dominio (36 pruebas)

Perfil comercial ampliado y validación de teléfono, correo y dominio de las redes; descripción
breve obligatoria y acotada; envío a revisión, rechazo con observaciones y publicación; el cambio
de configuración cancela una revisión en curso; checklist con porcentaje y mensajes de campos
faltantes; imágenes con metadatos validados, texto alternativo sin HTML, eliminación lógica y
concurrencia optimista; invitaciones de un solo uso, expiradas, revocadas, reenviadas y
bloqueadas por intentos; auditoría que trunca la IP y acota el resumen; recibo de consentimiento
vinculado a un turno; y las nueve validaciones de arranque de Production.

### Integración (22 pruebas)

- **Roles:** la socia entra a la consola y un propietario no; la socia no puede crear otra socia,
  ni administrar socias, ni reiniciar accesos, ni ver la auditoría global, ni ver la salud; sólo
  ve y edita los negocios que ella creó; puede enviar a revisión pero no aprobar ni rechazar.
- **Invitaciones:** el enlace se consume una sola vez; el token en claro no está en la base, sólo
  su HMAC; una invitación revocada o vencida no se acepta; reenviar anula la anterior; el
  reinicio administrativo cierra las sesiones y la contraseña anterior deja de servir; aceptar
  una invitación de propietario crea la membresía y queda en auditoría sin secretos.
- **Imágenes:** un SVG declarado como PNG se rechaza; un ejecutable con cabecera MZ declarado
  como JPEG se rechaza; un archivo de 6 MB devuelve 413; un JPEG con perfil EXIF y coordenada GPS
  se sirve **sin** EXIF y reescalado de 2400 a 1600 px, verificado descargando el archivo
  servido; el logo y la portada se mantienen únicos al reemplazarlos; la novena fotografía
  devuelve 409; una socia ajena recibe 403 al listar o cargar imágenes de otro negocio.
- **Revisión:** ciclo completo con vista previa antes de publicar, rechazo con observaciones,
  reenvío, publicación y aparición en el directorio con logo y portada; el historial de estados
  registra cada paso; un negocio incompleto recibe 409 con el detalle de lo que falta.
- **Consentimiento:** un turno sin aceptación o con una versión distinta se rechaza; el aceptado
  genera su recibo con la versión correcta; el endpoint público de textos legales informa la
  versión que el servidor exigirá.
- **Salud:** anónimo recibe 401; el administrador ve el estado real de base, almacenamiento y
  Data Protection.
- **Agendamiento:** un negocio creado desde la consola queda realmente agendable al publicarse,
  con horario de 08:00 a 18:00 y cupos disponibles.

### Extremo a extremo (1 prueba, la Fase 14 completa)

`FounderProductionJourneyTests` recorre por la interfaz real, en Chromium:

1. El administrador invita a una socia y copia el enlace.
2. La socia lo abre y define su propia contraseña.
3. Crea el negocio y completa el perfil desde el formulario.
4. Carga logo, portada y una fotografía.
5. Invita a la persona propietaria y copia su enlace; esa persona activa su acceso.
6. Previsualiza la ficha; el directorio público todavía no la muestra.
7. Envía a revisión; el botón de publicar no le aparece.
8. El administrador la devuelve con observaciones; la socia corrige y reenvía.
9. El administrador aprueba y publica; el negocio aparece con su logo.
10. La persona propietaria entra y accede a su configuración.
11. Recibe 403 al pedir datos de otro negocio y al abrir la consola de plataforma.
12. Un visitante agenda una cita aceptando el aviso; el recibo guarda la versión vigente.
13. La persona propietaria confirma la cita.
14. Se suspende: sale del directorio y una cita nueva devuelve 404.
15. Se reactiva: vuelve a aparecer.
16. El historial de estados y la auditoría de accesos registran todo, sin contraseñas.

## Docker local

```
docker compose build                  correcto
docker compose up -d                  app y postgres saludables
```

| Comprobación | Resultado |
| --- | --- |
| `/health/ready` | 200 |
| `/api/v1/public/legal` | Devuelve la configuración de `appsettings.Demo.json`, versión `demo-1` |
| `/api/v1/admin/health` sin sesión | 401 |
| `/legal/politica-de-datos` | 200 |
| Inicio de sesión del administrador | 302 correcto |
| Carga de logo por multipart | 201, servido en `/media/businesses/…/logo/….png` |
| Ficha pública | Devuelve `logoUrl` y la imagen responde 200 como `image/png` |
| Reinicio del contenedor | La imagen sigue disponible y la sesión sigue válida |

## Restauración probada

Ejecutada sobre la pila local, no simulada.

```
pg_dump -Fc                      107 189 bytes
CREATE DATABASE uraba_restore_test
pg_restore --no-owner            sin errores
```

| Tabla | Origen | Restaurada |
| --- | --- | --- |
| `businesses` | 4 | 4 |
| `business_images` | 1 | 1 |
| `consent_receipts` | 2 | 2 |
| `appointments` | 1 | 1 |
| `AspNetUsers` | 12 | 12 |

Respaldo de volúmenes verificado listando el contenido de los archivos comprimidos: el volumen de
llaves contiene el anillo y el de imágenes contiene el logo cargado.

## Hallazgo: las llaves no están cifradas en reposo

Al inspeccionar el volumen se confirmó que el anillo se guarda **sin cifrar**: el XML tiene
`requiresEncryption="true"` y no tiene `decryptorType`, es decir, ASP.NET Core lo persistió en
claro porque no hay ningún encriptador configurado.

V5 añade la posibilidad de cifrarlo con un certificado X.509 entregado en
`DataProtection__CertificateBase64`. **No se activó**, porque exige que el usuario genere y
custodie ese certificado. Mientras no se active, quien acceda al volumen puede descifrar los
alias y teléfonos de los clientes. Ver [15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).

## Defectos preexistentes encontrados y corregidos

1. **`new TimeOnly(8)` invocaba el constructor de *ticks*.** Todo negocio creado desde la consola
   nacía con un horario de 08:00:00.0000008 a 08:00:00.0000018 y con franjas de recogida
   igualmente vacías. En la práctica: **ningún negocio creado desde la consola podía recibir una
   cita ni un pedido**. Lo encontró la prueba de recorrido completo; queda cubierto por
   `A_business_created_from_the_console_is_bookable_once_published`.
2. **Las rutas de la API redirigían al inicio de sesión** en vez de responder 401 o 403, lo que
   convierte un fallo de autorización en una respuesta HTML de 200 para cualquier cliente que
   siga redirecciones.

## Lo que no se ejecutó

- **Despliegue en la instancia Demo de Railway.** Requiere iniciar sesión en Railway y aplicar
  migraciones sobre la base compartida; ambas cosas están fuera de lo que se puede hacer sin las
  credenciales del usuario.
- **Carga de imágenes contra un bucket R2 real.** Requiere una cuenta de Cloudflare con método de
  pago. La ruta de S3 está implementada y compila; lo verificado end to end es el proveedor local.
- **Restauración de un volumen en Railway.** Se probó el procedimiento equivalente en Docker.
