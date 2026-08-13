# 16 — Variables exactas para crear Production

Este documento existe para que crear el ambiente productivo sea copiar y pegar, no recordar. No
contiene ningún valor sensible y no debe contenerlo nunca: los secretos se escriben directamente
en el panel de variables de Railway.

Estado al 13 de agosto de 2026: **Production no está creada.** Falta la autorización de costo
(`14_COSTS.md`), los siete `Legal__*` reales y el bucket R2 productivo. La Demo sí está desplegada,
limpia y verificada de punta a punta.

## 1. Lo que hay que crear en Railway

| # | Recurso | Detalle |
| --- | --- | --- |
| 1 | Environment nuevo, o proyecto nuevo | No reutilizar el que hoy aloja Demo |
| 2 | Servicio web | Rama `release/founder-production` |
| 3 | PostgreSQL nuevo y vacío | Nunca una copia de la base Demo |
| 4 | Volumen montado en `/app/keys` | Distinto del volumen de Demo |
| 5 | Health check | `/health/ready` |

Sobre la región: la Demo tiene la aplicación en US West y PostgreSQL en US East, y cada ida y
vuelta a la base cuesta unos 73 ms por esa distancia. Al crear Production **conviene poner el
servicio web y la base en la misma región**; es gratis hacerlo bien desde el principio y caro
corregirlo después, porque implica recrear un servicio con volumen.

## 2. Variables no sensibles — valores literales

Se pegan tal cual:

```
ASPNETCORE_ENVIRONMENT=Production
ObjectStorage__Provider=S3
ObjectStorage__Region=auto
DataProtection__KeysPath=/app/keys
DataProtection__ApplicationName=UrabaConecta.Production
DetailedErrors=false
```

`DataProtection__ApplicationName` distinto del de Demo no es un adorno: aísla el anillo de llaves
entre ambientes, de modo que ninguna cookie ni ningún dato cifrado de uno pueda leerse desde el
otro aunque los volúmenes se confundieran.

## 3. Cloudflare R2 — lo que hay que crear y qué va en cada variable

El bucket de Production es **nuevo**. La aplicación se niega a arrancar si el bucket o la URL
pública contienen la palabra «demo», así que el nombre no puede derivarse del actual.

| Variable | ¿Secreta? | Qué poner |
| --- | --- | --- |
| `ObjectStorage__ServiceUrl` | No | `https://<ID_DE_CUENTA>.r2.cloudflarestorage.com`. Es el **mismo extremo** que ya usa la Demo: se copia de la variable homónima del servicio actual. El identificador de cuenta no cambia por crear otro bucket |
| `ObjectStorage__Bucket` | No | `urabaconecta-production-media` |
| `ObjectStorage__PublicBaseUrl` | No | El `https://pub-<hash>.r2.dev` que Cloudflare asigna **a ese bucket**. Es distinto del de Demo y sólo aparece tras habilitar el acceso público del bucket desde el panel |
| `ObjectStorage__AccessKey` | **Sí** | Identificador del token de API de R2, acotado al bucket de Production |
| `ObjectStorage__SecretKey` | **Sí** | Secreto de ese mismo token |

Pasos en Cloudflare, en este orden:

1. Crear el bucket `urabaconecta-production-media`.
2. Activar **versionado** en él (`12_GO_LIVE.md`, fase 1).
3. Habilitar el acceso público de desarrollo (`r2.dev`) y copiar el dominio resultante: ése es
   `ObjectStorage__PublicBaseUrl`.
4. Crear un token de API de R2 con permiso de **lectura y escritura acotado a ese bucket**, no a
   toda la cuenta. Su identificador y su secreto son `ObjectStorage__AccessKey` y
   `ObjectStorage__SecretKey`.
5. No reutilizar el token de Demo. Un token compartido convierte cualquier incidente de la
   demostración en un incidente de los datos reales.

## 4. Los siete datos jurídicos — bloqueantes

Sin ellos `StartupGuard` aborta el arranque y lista los que falten. No se inventan.

```
Legal__ResponsibleName=      # razón social o persona responsable del tratamiento
Legal__Identification=       # NIT o cédula
Legal__Address=              # domicilio para notificaciones
Legal__PrivacyEmail=         # canal para ejercer derechos sobre datos personales
Legal__SupportEmail=         # canal de consultas y reclamos
Legal__PolicyVersion=        # identificador de versión, por ejemplo 2026-1
Legal__PolicyEffectiveDate=  # fecha de entrada en vigencia
```

## 5. Secretos propios de Production

Todos **nuevos**. Ninguno se copia de Demo.

| Variable | Cómo se obtiene |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | La cadena del PostgreSQL de Production. Por red privada del proveedor, no por el proxy público |
| `URABACONECTA_TRACKING_HMAC_KEY` | 32 bytes aleatorios o más. Si se pierde, **todos los códigos de seguimiento entregados a clientes dejan de resolver** |
| `URABACONECTA_INVITATION_HMAC_KEY` | Otros 32 bytes aleatorios |
| `DataProtection__CertificateBase64` | Opcional pero recomendada; ver §7 |
| `DataProtection__CertificatePassword` | Con la anterior |

## 6. Prohibidas en Production

La aplicación no arranca si alguna está definida. Al clonar variables desde Demo es el error
fácil de cometer:

```
DemoSeed__Enabled            DemoSeed__AdminPassword      DemoSeed__BusinessPassword
DemoBootstrap__Enabled       DemoBootstrap__AdminEmail    DemoBootstrap__AdminPassword
DemoBootstrap__Token         DemoAccess__SharedPassword
```

## 7. Data Protection — lo que ya está comprobado y lo que falta

Comprobado en la Demo el 13 de agosto de 2026, sobre el mismo mecanismo que usará Production:

- La pantalla de salud informa `Persistente en /app/keys`.
- Tras **reiniciar el contenedor** (redespliegue completo, contenedor nuevo, volumen conservado),
  una cita creada antes del reinicio seguía mostrando el alias y el teléfono descifrados en el
  panel del propietario, sin una sola respuesta 5xx.

Es decir: el anillo **no** depende del sistema de archivos efímero del contenedor, sino del
volumen, y sobrevive a los reinicios. Production hereda esa propiedad si y sólo si se le monta su
propio volumen en `/app/keys`.

Lo que **sigue pendiente y es una decisión, no una tarea**: el anillo se guarda **sin cifrar**.
Quien acceda al volumen puede descifrar los alias y teléfonos de los clientes. Se resuelve con
`DataProtection__CertificateBase64`, custodiando el `.pfx` fuera del proveedor —si se pierde, los
datos cifrados con él son irrecuperables—, o se acepta el riesgo por escrito para el piloto.

## 8. Orden de arranque

1. Crear los recursos de §1 sin dominio público.
2. Cargar §2, §3, §4 y §5. Revisar §6.
3. Primer despliegue. Si `StartupGuard` lo impide, el mensaje enumera **todos** los problemas a la
   vez: corregirlos juntos.
4. `/health/live` y `/health/ready` en 200.
5. Administrador inicial con `ProductionBootstrap__*`, y **retirar las tres variables** en cuanto
   cambie su contraseña (`12_GO_LIVE.md`, fase 3).
6. Crear Studio Laura deliberadamente desde la interfaz, con sus datos reales. No copiar la base
   Demo, que arrastra pruebas.
7. **Crear su perfil operativo en Personal.** Sin él el negocio no ofrece ni una hora, que es
   exactamente el fallo que tuvo la Demo.

## 9. Datos reales de Studio Laura verificados en Demo

Sirven para reponerlos en Production sin volver a averiguarlos:

| Campo | Valor |
| --- | --- |
| Nombre | Studio Laura usuga |
| Slug | `laura-usuga` |
| Municipio | Apartadó |
| Categoría | Belleza y cuidado personal |
| Dirección | Calle 77 # 73-111 |
| Teléfono público | 3124550933 |
| Horario | Lunes 09:00–19:00; martes a sábado 08:00–18:00; domingo cerrado |
| Servicio | pestañas pelo a pelo — 120 min — $100.000 |
| Servicio | liftin de pestañas — 60 min — $60.000 |
| Módulo | Citas |
| Zona horaria | `America/Bogota` |

Lo que el negocio **todavía no ha aportado** y nadie debe rellenar por él: portada, WhatsApp,
correo público, Instagram, Facebook, enlace de ubicación, punto de referencia, instrucciones para
el cliente y los textos alternativos de las imágenes.
