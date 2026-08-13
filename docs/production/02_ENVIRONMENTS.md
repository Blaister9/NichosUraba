# 02 — Ambientes

Dos ambientes, separados en todo: base de datos, almacenamiento de objetos, secretos, llaves de
Data Protection y cuentas. No comparten nada.

## Demo

| Recurso | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Demo` |
| Datos | Un solo negocio real acompañado: Studio Laura usuga |
| Cuentas | `*.demo` |
| PostgreSQL | Instancia Demo |
| Almacenamiento | Proveedor `Local` sobre volumen, o bucket Demo |
| `DemoSeed__Enabled` | `false` desde el 13 de agosto de 2026 |
| Rama desplegada | `release/pilot-demo` |

Demo existe para enseñar el producto. Puede romperse y reponerse sin consecuencias.

### El sembrado está apagado a propósito

El 13 de agosto de 2026 se retiraron de la Demo los veinte negocios ficticios y de prueba, y quedó
únicamente Studio Laura usuga. Eso **obliga** a mantener `DemoSeed__Enabled=false`: el sembrado
vuelve a crear los negocios de muestra y, peor, reinstala membresías que apuntan a negocios que ya
no existen. Esa escritura ocurre en el arranque y fuera del bloque tolerante a fallos, así que
reactivarlo con la base actual deja el contenedor en un ciclo de caída y el despliegue en 502, sin
que un rollback lo arregle.

Para volver a tener una demostración con negocios de muestra hay que **recrear la base**, no
reencender el sembrado sobre ésta.

### El environment de Railway que aloja Demo se llama `production`

Riesgo de confusión, vigente y sin resolver:

| Dónde se lee | Valor |
| --- | --- |
| Railway, nombre del environment | `production` |
| Railway, nombre del proyecto | `skillful-sparkle` |
| Dominio público | `nichosuraba-production.up.railway.app` |
| `ASPNETCORE_ENVIRONMENT` | `Demo` |
| Bucket | `urabaconecta-demo-media` |

Quien mire Railway leerá «production» y creerá que está frente a datos reales; lo único que manda es
`ASPNETCORE_ENVIRONMENT`. Mientras no se renombre, **la palabra `production` en Railway no significa
nada** para este servicio.

Procedimiento para renombrarlo a `demo` más adelante, en este orden:

1. Comprobar en la consola de Railway que el proyecto tiene un solo environment; renombrar es una
   operación de metadatos y no recrea servicios, volúmenes ni la base, pero conviene verlo antes.
2. Anotar el dominio público actual. El dominio pertenece al servicio, no al environment, así que no
   debería cambiar; si Railway ofreciera regenerarlo, **rechazarlo**: cambiarlo invalida los enlaces
   ya repartidos para capacitación.
3. Renombrar el environment en Ajustes.
4. Volver a enlazar el repositorio local: `railway link` y elegir el nuevo nombre. El enlace guarda
   el identificador, no el nombre, pero conviene dejar el `status` legible.
5. Comprobar `railway status`: proyecto, environment `demo`, servicio `NichosUraba`, rama observada
   `release/pilot-demo`.
6. Comprobar `/health/live` y `/health/ready`, y que el SHA vivo no cambió.

No hace falta tocar la aplicación: **ningún archivo del repositorio lee `RAILWAY_ENVIRONMENT`,
`RAILWAY_PROJECT_NAME` ni `RAILWAY_SERVICE_NAME`**, y ningún script de `ops/` fija el nombre del
environment. El alcance del cambio es el nombre en la consola y en la memoria de quien opera.

## Production

| Recurso | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| Datos | Reales. Base limpia, sin sembrado |
| Cuentas | Un `PlatformAdmin` real; el resto por invitación |
| PostgreSQL | Instancia Production nueva |
| Almacenamiento | Proveedor `S3` sobre bucket Production |
| `DemoSeed__Enabled` | Ausente o `false` |
| Data Protection | Volumen `/app/keys` propio, independiente del de Demo |
| Rama desplegada | `release/founder-production` |

## Barreras que impiden confundirlos

`StartupGuard` corre antes de la primera petición. En `Production` la aplicación **se niega a
arrancar** si:

| Condición | Mensaje |
| --- | --- |
| `DemoSeed__Enabled=true` | `DemoSeed__Enabled debe ser false en Production` |
| `DemoBootstrap__Enabled=true` | `DemoBootstrap__Enabled debe ser false en Production` |
| `DemoAccess__SharedPassword` presente | `no debe estar definida en Production` |
| `DemoSeed__AdminPassword` / `BusinessPassword` presentes | `no debe estar definida en Production` |
| Cadena de conexión que apunta a una base Demo | `apunta a una base de datos Demo` |
| Bucket o dominio público con nombre de Demo | `apunta a un bucket Demo` |
| Contraseña de demostración conocida en cualquier variable | `contraseña de demostración conocida` |
| Falta cualquiera de los siete `Legal__*` | `Faltan variables jurídicas obligatorias` |
| Falta configuración de almacenamiento, o proveedor ≠ `S3` | `Falta configurar el almacenamiento` |
| Falta `URABACONECTA_TRACKING_HMAC_KEY` | `Falta URABACONECTA_TRACKING_HMAC_KEY` |
| Falta `DataProtection__KeysPath` | `las cookies no sobrevivirían a un reinicio` |
| `DetailedErrors=true` | `expondría rastros de pila` |

Los problemas se reportan **todos juntos** en un solo mensaje, no de uno en uno.

Barreras adicionales, por código y no por configuración:

- El sembrado ficticio retorna inmediatamente si el ambiente no es `Development` ni `Demo`.
- `DemoBootstrap` lanza excepción si se habilita fuera de `Demo`.
- `DemoAccess__SharedPassword` lanza excepción si se usa fuera de `Demo`.
- `ProductionBootstrap` lanza excepción si se habilita fuera de `Production`.

## Rotación entre ambientes

Los secretos de Production son **nuevos**, nunca los de Demo. Demo no se rota mientras esté en
uso, salvo que uno de sus secretos se haya expuesto. Ver `04_SECRETS.md`.
