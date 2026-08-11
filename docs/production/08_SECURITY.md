# 08 — Seguridad

Auditoría del estado real del código, no de intenciones. Cada fila se verificó leyendo la
implementación.

## Transporte y cabeceras

| Control | Estado | Dónde |
| --- | --- | --- |
| HTTPS | Railway termina TLS; la aplicación redirige con `UseHttpsRedirection` | `Program.cs` |
| HSTS | Activo fuera de Development | `Program.cs` |
| Cabeceras reenviadas | `X-Forwarded-For` y `X-Forwarded-Proto` confiadas | `ForwardedHeadersOptions` |
| `X-Content-Type-Options` | `nosniff` | Middleware propio |
| `X-Frame-Options` | `DENY` | Middleware propio |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Middleware propio |
| CSP | `default-src 'self'`; scripts sin `unsafe-eval` salvo `wasm-unsafe-eval` (lo exige Blazor WebAssembly); imágenes limitadas a `'self'`, `data:` y el dominio del bucket | `ContentSecurityPolicyFactory` |

`style-src` conserva `'unsafe-inline'`: Blazor emite estilos en línea y quitarlo rompería el
render. Es una desviación conocida y acotada a estilos, no a scripts.

## Sesión y autorización

| Control | Estado |
| --- | --- |
| Cookie `Secure` | Siempre fuera de Development |
| Cookie `HttpOnly` | Explícito |
| Cookie `SameSite` | `Lax` explícito, no heredado del marco |
| Antiforgery | `UseAntiforgery` global |
| Longitud mínima de contraseña | 10 caracteres |
| Bloqueo por intentos | 5 intentos, 15 minutos, activo para cuentas nuevas |
| Correo confirmado | Exigido para iniciar sesión |
| Contraseña temporal | `MustChangePassword` fuerza el cambio antes de usar la aplicación |
| Rutas de API | Responden 401/403 en lugar de redirigir al inicio de sesión |

**Por qué `SameSite` importa aquí:** la subida de imágenes (`POST /api/v1/admin/businesses/{id}/images`)
lleva `DisableAntiforgery()` por ser `multipart/form-data`. Lo único que impide que otro sitio
envíe ese formulario con la cookie de la víctima es que `SameSite=Lax` no acompañe la petición
cruzada. Estaba funcionando por omisión del marco; ahora está escrito en el código, que es donde
debe estar un control del que depende una defensa.

## Autorización por rol

| Política | Roles |
| --- | --- |
| `PlatformAdmin` | `PlatformAdmin` |
| `PlatformOperator` | `PlatformAdmin`, `PartnerOperator` |
| `BusinessOwner` | `BusinessOwner` |
| `Appointments.Manage`, `Workers.Manage`, `BusinessConfiguration.Manage` | `BusinessOwner`, `BusinessWorker` |

El alcance fino (qué negocios ve cada socia, qué acciones puede ejecutar) lo impone el caso de
uso, no la ruta. El `PlatformActor` se construye a partir de la petición autenticada
(`http.User.IsInRole`, IP de la conexión), **nunca de la carga útil del cliente**.

## Aislamiento por negocio

Toda entidad de negocio implementa `IBusinessOwned`. Los casos de uso privados reciben el
identificador del usuario y el del negocio, y comprueban membresía activa antes de resolver.
Cubierto por pruebas de integración que verifican que el propietario de un negocio recibe 403
sobre otro.

## Subida de archivos

Es la superficie más expuesta y está bien cerrada:

| Control | Implementación |
| --- | --- |
| Tamaño | 5 MiB, rechazado con 413 antes de leer el cuerpo completo |
| Tipos admitidos | JPEG, PNG y WebP únicamente |
| **Firma binaria real** | `Image.Load` con una `Configuration` que **sólo registra** JPEG, PNG y WebP. La extensión y el `Content-Type` declarados no deciden nada |
| SVG | **Bloqueado**: no está registrado, así que falla al cargar. Es el vector clásico de XSS por imagen |
| GIF, BMP, TIFF, ejecutables | Bloqueados por el mismo mecanismo |
| EXIF y metadatos | `ExifProfile`, `XmpProfile`, `IccProfile` e `IptcProfile` se anulan, también por fotograma. Evita publicar geolocalización y datos del dispositivo |
| Recompresión | Siempre se reescala y se reescribe como WebP: el binario que se guarda **no es el que subió el usuario** |
| Directory traversal | `LocalObjectStorage.Resolve` normaliza la ruta y exige que quede bajo la raíz; si no, excepción |
| Límite de galería | 8 imágenes por negocio |

## Limitación de tasa

| Política | Límite | Aplica a |
| --- | --- | --- |
| `public-write` | 12/min por IP | Crear citas, turnos, pedidos; cancelar; reportar adelanto |
| `public-sensitive-read` | 1200/min por IP | Consultas de seguimiento por código |

El código de seguimiento es la única credencial del cliente, y sólo le alcanza para avisar que
envió un comprobante. Verificar un adelanto no tiene ruta pública.

## Errores

- `ApiExceptionHandler` sólo traduce `ApiException`; el resto cae en el manejador estándar, que
  responde `ProblemDetails` **sin rastro de pila**.
- `DetailedErrors` está en `false` en `appsettings.Production.json` y `StartupGuard` **impide
  arrancar** si alguien lo pone en `true`.
- `ops/smoke-production.ps1` comprueba en cada despliegue que ninguna respuesta contenga rastros.

## Pendiente, por decisión

| Tema | Estado |
| --- | --- |
| Cifrado del anillo de Data Protection | Opcional y sin configurar. Ver `06_DATA_PROTECTION.md` |
| Segundo factor | Fuera de alcance para esta etapa |
| `style-src 'unsafe-inline'` | Lo exige Blazor. Acotado a estilos |
| Alertas automáticas por métrica | No disponibles en Hobby. Revisión manual documentada |
