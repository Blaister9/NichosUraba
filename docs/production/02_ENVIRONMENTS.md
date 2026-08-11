# 02 — Ambientes

Dos ambientes, separados en todo: base de datos, almacenamiento de objetos, secretos, llaves de
Data Protection y cuentas. No comparten nada.

## Demo

| Recurso | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Demo` |
| Datos | Ficticios. Tres negocios de muestra |
| Cuentas | `*.demo`, contraseña compartida operativa |
| PostgreSQL | Instancia Demo |
| Almacenamiento | Proveedor `Local` sobre volumen, o bucket Demo |
| `DemoSeed__Enabled` | `true` |
| Rama desplegada | `release/pilot-demo` |

Demo existe para enseñar el producto. Puede romperse, reponerse y volver a sembrarse sin
consecuencias.

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
