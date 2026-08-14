# 17 — Production Launch Gate

Estado al 13 de agosto de 2026:

```
DEMO-ACTIVATION-READY
PRODUCTION-BLOCKED-BY-EXTERNAL-CONFIG
```

La Demo queda como **referencia funcional validada** y cerrada. Production está bloqueada
únicamente por trece entradas externas que sólo puede suministrar o autorizar el responsable.

Este documento es la secuencia de lanzamiento, y nada más. No abre fases de producto, no propone
mejoras y no vuelve sobre lo cerrado. Cuando lleguen las trece entradas, la ejecución siguiente se
limita a recorrerlo de arriba abajo.

---

## Puerta de entrada — las trece entradas externas

Ninguna se inventa. Ninguna se deriva de la Demo. Mientras falte una sola, la secuencia no empieza.

### Datos jurídicos (1–7)

| # | Variable | Qué es |
| --- | --- | --- |
| 1 | `Legal__ResponsibleName` | Razón social o persona responsable del tratamiento |
| 2 | `Legal__Identification` | NIT o cédula |
| 3 | `Legal__Address` | Domicilio para notificaciones |
| 4 | `Legal__PrivacyEmail` | Canal para ejercer derechos sobre datos personales |
| 5 | `Legal__SupportEmail` | Canal de consultas y reclamos |
| 6 | `Legal__PolicyVersion` | Identificador de versión, por ejemplo `2026-1` |
| 7 | `Legal__PolicyEffectiveDate` | Fecha de entrada en vigencia |

### Almacenamiento de objetos (8–12)

| # | Variable | ¿Secreta? | Restricción que impone la aplicación |
| --- | --- | --- | --- |
| 8 | `ObjectStorage__ServiceUrl` | No | Extremo S3 de la cuenta R2 |
| 9 | `ObjectStorage__Bucket` | No | **No puede contener «demo»**. Bucket nuevo |
| 10 | `ObjectStorage__PublicBaseUrl` | No | **No puede contener «demo»**. Es el dominio del bucket nuevo |
| 11 | `ObjectStorage__AccessKey` | **Sí** | Token acotado al bucket de Production |
| 12 | `ObjectStorage__SecretKey` | **Sí** | Secreto de ese token |

Los detalles de creación en Cloudflare están en
[16_VARIABLES_PARA_CREAR_PRODUCTION.md](16_VARIABLES_PARA_CREAR_PRODUCTION.md), §3.

### Autorización (13)

| # | Qué |
| --- | --- |
| 13 | Autorización expresa para crear recursos facturables en Railway: servicio web, PostgreSQL y volumen |

Costos y alternativas: [14_COSTS.md](14_COSTS.md). Hasta que exista esta autorización **no se crea
ningún recurso de pago**.

---

## Invariantes que la ejecución no puede romper

Se comprueban al final, en el paso 16, y cualquiera que falle detiene el lanzamiento.

1. `StartupGuard` no se relaja ni se rodea. Si impide el arranque, se corrigen las causas.
2. `ObjectStorage__Provider=S3`. Nunca `Local`.
3. No se reutiliza el bucket de Demo ni sus credenciales.
4. No se copia la base de Demo a Production. Studio Laura se crea deliberadamente.
5. Ningún secreto de Demo viaja a Production.
6. Ninguna variable `Demo*` queda definida en Production.

---

## Secuencia

Cada paso termina en una comprobación. Si falla, se detiene ahí.

### 0 — Rama de despliegue

`release/founder-production` avanza sin merge hasta el commit validado en Demo:

```bash
git checkout release/founder-production && git merge --ff-only feat/founder-launch && git push origin release/founder-production
```

**Comprobación:** `git log --oneline -1` muestra `94f5df6` o posterior.

### 1 — Crear el ambiente y sus recursos

En Railway, environment o proyecto **nuevo**, nunca el que aloja Demo:

- servicio web desde `release/founder-production`;
- PostgreSQL **nuevo y vacío**;
- volumen montado en `/app/keys`, distinto del de Demo;
- health check en `/health/ready`;
- reinicio `ON_FAILURE`, máximo 3 reintentos;
- una réplica, sin autoescalado.

Servicio web y base **en la misma región**. La Demo quedó partida entre US West y US East y cada
ida y vuelta a la base cuesta unos 73 ms; corregirlo después obliga a recrear un servicio con
volumen.

**Comprobación:** los tres recursos existen y el volumen aparece montado en `/app/keys`.

### 2 — Bucket R2 de Production

Con los valores 8–12 ya en mano: bucket nuevo, **versionado activado**, acceso público habilitado,
y token de API acotado a ese bucket.

**Comprobación:** el bucket responde y su dominio público es distinto del de Demo.

### 3 — Variables y secretos

Literales, se pegan tal cual:

```
ASPNETCORE_ENVIRONMENT=Production
ObjectStorage__Provider=S3
ObjectStorage__Region=auto
DataProtection__KeysPath=/app/keys
DataProtection__ApplicationName=UrabaConecta.Production
DetailedErrors=false
```

Más: las siete `Legal__*` (1–7), las cinco `ObjectStorage__*` (8–12),
`ConnectionStrings__DefaultConnection` de la base nueva, y `URABACONECTA_TRACKING_HMAC_KEY` y
`URABACONECTA_INVITATION_HMAC_KEY` **generadas nuevas**, de 32 bytes o más.

Perder `URABACONECTA_TRACKING_HMAC_KEY` invalida todos los códigos de seguimiento entregados a
clientes. Se custodia desde el primer día.

**Comprobación —** ninguna de estas existe en Production:

```
DemoSeed__Enabled  DemoSeed__AdminPassword  DemoSeed__BusinessPassword
DemoBootstrap__Enabled  DemoBootstrap__AdminEmail  DemoBootstrap__AdminPassword
DemoBootstrap__Token  DemoAccess__SharedPassword
```

### 4 — Migraciones

`DatabaseMigrator` corre en el arranque en todo ambiente y `Database:MigrateOnStartup` vale `true`
por omisión: **no hay paso manual**. Las migraciones son críticas y corren fuera de cualquier
bloque tolerante a fallos, así que un fallo aquí impide arrancar, que es lo correcto.

**Comprobación:** la pantalla de salud informa las migraciones aplicadas en el arranque y la base
«sin migraciones pendientes».

### 5 — Primer despliegue

Sin dominio público todavía.

**Comprobación:** el despliegue queda `SUCCESS` con la instancia `RUNNING`. Si `StartupGuard` lo
impidió, el mensaje enumera **todos** los problemas a la vez: se corrigen juntos y se redespliega.

### 6 — Salud

```bash
curl -s -o /dev/null -w "live=%{http_code}\n" https://<dominio>/health/live
curl -s -o /dev/null -w "ready=%{http_code}\n" https://<dominio>/health/ready
```

**Comprobación:** ambos 200.

> Un 502 puntual durante el cambio de contenedor es normal: se repite la comprobación cuando el
> despliegue figura `SUCCESS`.

### 7 — Administrador inicial

`ProductionBootstrap__Enabled=true`, `ProductionBootstrap__AdminEmail` real —nunca `.demo`— y
`ProductionBootstrap__AdminPassword` temporal de 16 caracteres o más con mayúscula, minúscula,
dígito y un carácter no alfanumérico. Redesplegar.

Iniciar sesión: la aplicación **exige cambiar la contraseña temporal**. Cambiarla, **retirar las
tres variables** `ProductionBootstrap__*` y redesplegar.

**Comprobación:** el administrador entra con su contraseña definitiva y el arranque sigue limpio
sin esas variables.

> **Criterio de detención.** `ProductionBootstrap` no repone el acceso una segunda vez. Si el
> administrador no puede entrar, el lanzamiento se detiene aquí.

### 8 — Crear Studio Laura

Desde la interfaz, con los datos ya validados en Demo. **No se copia la base de Demo.**

| Campo | Valor |
| --- | --- |
| Nombre | Studio Laura usuga |
| Slug | `laura-usuga` |
| Municipio | Apartadó |
| Categoría | Belleza y cuidado personal |
| Dirección | Calle 77 # 73-111 |
| Teléfono público | 3124550933 |
| Zona horaria | `America/Bogota` |
| Módulo | Citas |
| Horario | Lunes 09:00–19:00 · martes a sábado 08:00–18:00 · domingo cerrado |
| Servicio | pestañas pelo a pelo — 120 min — $100.000 |
| Servicio | liftin de pestañas — 60 min — $60.000 |

Descripción y logo: se recuperan de la Demo, donde están verificados.

Lo que el negocio **no ha aportado** y nadie rellena por él: portada, WhatsApp, correo público,
Instagram, Facebook, enlace de ubicación, punto de referencia, instrucciones al cliente y textos
alternativos de imágenes.

### 9 — Perfil operativo

En **Personal**, crear un perfil activo, que participa en la disponibilidad, con **los dos
servicios asignados**.

**Comprobación:** la pantalla de Servicios **no** muestra el aviso «Nadie tiene asignado este
servicio». Ése fue exactamente el fallo que dejó la Demo sin horas durante semanas; es el paso que
más fácil se olvida porque nada obliga a darlo.

### 10 — Publicar y smoke público

Publicar el negocio y recorrer sin sesión: home, directorio, ficha de Studio Laura, servicios,
horarios y la pantalla de reserva.

```bash
node private/founder-launch-2/scripts/capturas-responsive.mjs https://<dominio> production
```

**Comprobación:** las cuatro superficies en 375×812, 390×844 y 1366×768, sin desborde horizontal y
sin una sola respuesta 5xx. Quedan además las capturas de Home y ficha en móvil.

**Comprobación adicional:** el pie **no** dice «Demostración con negocios y datos ficticios». El
servidor marca `<body data-ambiente="Production">` y el CSS oculta ese aviso fuera de Demo.

### 11 — Reserva controlada desde la interfaz

Navegador real, no endpoint. La cita **nace del recorrido público**.

```bash
node private/founder-launch-2/scripts/reserva-e2e.mjs https://<dominio> laura-usuga <businessId> <fecha> production
```

Elegir una fecha futura cercana en día abierto. El guion selecciona el servicio, espera a que el
resumen de precio confirme el cambio —sin esa espera se fotografían las horas del servicio
anterior—, pide las horas, reserva, y abre el panel del propietario.

**Comprobación:** el número de horas y la primera y la última cuadran con el horario del día y la
duración del servicio; la confirmación muestra el código de seguimiento; **cero 5xx**.

### 12 — La cita en el panel del propietario

**Comprobación:** la cita aparece con cliente, servicio, fecha, **hora local de `America/Bogota`** y
estado `Pendiente`. La hora mostrada es la misma que eligió el cliente.

### 13 — Reiniciar el servicio

Redespliegue completo: contenedor nuevo, volumen conservado.

**Comprobación:** el despliegue vuelve a `SUCCESS` con la instancia `RUNNING`.

### 14 — Datos protegidos y reserva, después del reinicio

```bash
node private/founder-launch-2/scripts/salud-y-cifrado.mjs https://<dominio> <businessId> "<alias>" despues-de-reiniciar
```

**Comprobación:** la salud informa `Persistente en /app/keys`; el alias y el teléfono de la cita
creada **antes** del reinicio siguen descifrándose en el panel; la reserva pública sigue ofreciendo
horas; cero 5xx.

> Al leer el panel hay que esperar a que el circuito termine de pintar. Una lectura demasiado
> temprana del DOM da un falso negativo que parece pérdida del anillo de llaves y no lo es.

Si el alias **no** se descifra, el anillo no sobrevivió: el volumen no está montado donde debe o
`DataProtection__KeysPath` no apunta a él. Detener el lanzamiento.

### 15 — Retirar la cita de smoke

Cancelarla. Ocupa una hora real del calendario de un negocio real.

**Comprobación:** la hora vuelve a ofrecerse en la reserva pública.

### 16 — Cierre

Repasar los seis invariantes. Si los dieciséis pasos pasaron:

```
PRODUCTION-LAUNCH-COMPLETE
```

Si alguno falló, se registra cuál y por qué, y el estado sigue siendo
`PRODUCTION-BLOCKED`.

---

## Lo que la ejecución siguiente NO hace

- No abre una fase de producto.
- No mejora, refactoriza ni audita nada.
- No reabre scheduling, `EligibleStaff`, servicios, horarios, timezone, reserva pública, citas del
  propietario, Data Protection, limpieza de negocios de la Demo, header móvil ni Founder Launch
  visual. Están cerrados salvo regresión demostrable.
- No incorpora un segundo negocio. Studio Laura primero, acompañado, y los demás sólo después de
  validarlo.

---

## Estado de la Demo al cerrar

| Punto | Estado |
| --- | --- |
| Dominio | `https://nichosuraba-production.up.railway.app` |
| Commit | `94f5df6` |
| `health/live` · `health/ready` | 200 · 200 |
| Negocios en el directorio | 1 — Studio Laura usuga |
| Reserva pública → cita → panel del propietario | Verificado en navegador, sin 5xx |
| Data Protection | Persistente en volumen; sobrevivió a un reinicio completo |
| `DemoSeed__Enabled` | `false`, y **no se vuelve a activar sobre esta base** |
| Pruebas | 443 verdes |

### Mecanismo de recuperación de las cuentas Demo

Queda **operativo y sin usar**. La señal `rotacion-20260813` ya se consumió, de modo que ningún
redespliegue repone ninguna contraseña por sí solo.

Para establecer o rotar credenciales, el responsable declara en el servicio de Demo una **señal
nueva** y una contraseña temporal, y redespliega:

```
DemoBootstrap__Enabled=true
DemoBootstrap__AdminEmail=admin@urabaconecta.demo
DemoBootstrap__Token=<etiqueta nueva, corta, sin corchetes>
DemoBootstrap__AdminPassword=<temporal, 16+ caracteres, may/min/dígito/no alfanumérico>
```

Eso habilita **exactamente una** recuperación de `admin@urabaconecta.demo`, que arranca exigiendo
cambiar la contraseña temporal. Repetir la misma señal no repone nada. Desde esa cuenta, en
`/admin/accesos`, «Reiniciar el acceso de una cuenta» emite un enlace de un solo uso para cualquier
otra —`socia@urabaconecta.demo` entre ellas— con el que la persona define su propia contraseña.

Cada recuperación añade su entrada a la auditoría de accesos y **ninguna borra la anterior**.

Conviene retirar `DemoBootstrap__AdminPassword` una vez usada: la contraseña temporal deja de
servir en el primer inicio de sesión, pero la variable sigue siendo un secreto guardado sin
necesidad.
