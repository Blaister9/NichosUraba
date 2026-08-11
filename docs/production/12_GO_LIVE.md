# 12 — Go-live

Regla que gobierna todo el documento: **no se incorporan los cinco negocios a la vez.** Se lleva
uno a producción real, se observa, y sólo entonces entran los demás.

## Fase 0 — Autorización (antes de crear nada)

- [ ] `14_COSTS.md` leído y precios reverificados
- [ ] Decisión sobre si Demo permanece encendida
- [ ] **Autorización explícita si el ambiente genera costo adicional**

Sin esta fase, nada de lo que sigue debe ejecutarse.

## Fase 1 — Crear el ambiente

- [ ] Instancia PostgreSQL Production, nueva y vacía
- [ ] Servicio web Production, rama `release/founder-production`
- [ ] Volumen montado en `/app/keys` (distinto del de Demo)
- [ ] Bucket R2 Production con **versionado activado**
- [ ] Credenciales R2 acotadas a ese bucket
- [ ] Health check apuntando a `/health/ready`
- [ ] Reinicio `ON_FAILURE`, máximo 3 reintentos
- [ ] Todas las variables de `SECRETS_INVENTORY.md`, con secretos **nuevos**
- [ ] Los siete `Legal__*` con datos reales
- [ ] Certificado de Data Protection configurado (`06_DATA_PROTECTION.md`)

**Aún no generar el dominio público.**

## Fase 2 — Primer arranque

- [ ] `/health/live` responde 200
- [ ] `/health/ready` responde 200
- [ ] Los registros muestran las migraciones aplicadas, sin errores
- [ ] La pantalla de salud informa: ambiente `Production`, proveedor `S3`, Data Protection
      `Persistente en /app/keys`, sembrado Demo **falso**, commit correcto

Si `StartupGuard` impidió el arranque, el mensaje lista **todos** los problemas a la vez:
corregirlos juntos y redesplegar.

## Fase 3 — Administrador de plataforma

- [ ] `ProductionBootstrap__Enabled=true`, `AdminEmail` real y `AdminPassword` temporal
- [ ] Redesplegar; los registros confirman la creación
- [ ] Iniciar sesión: la aplicación **exige cambiar la contraseña temporal**
- [ ] Cambiar la contraseña
- [ ] **Retirar las tres variables `ProductionBootstrap__*`**
- [ ] Redesplegar y confirmar que el arranque sigue limpio

> **Criterio de detención:** si el administrador no puede iniciar sesión, detener el go-live y
> resolverlo antes de continuar. `ProductionBootstrap` no repone el acceso una segunda vez.

- [ ] Crear un **segundo** `PlatformAdmin` por invitación (mitiga el riesgo 4 de
      `15_KNOWN_RISKS.md`)

## Fase 4 — Verificación de persistencia

La más importante y la que más se omite.

- [ ] Reiniciar el servicio en Railway
- [ ] Recargar **sin volver a iniciar sesión**: la sesión sigue viva
- [ ] Comprobar los siete pasos de verificación de `06_DATA_PROTECTION.md`

Si la sesión se cae tras el reinicio, el volumen no persiste: **detener**.

## Fase 5 — Dominio y comprobación

- [ ] Generar el dominio en Railway
- [ ] `./ops/smoke-production.ps1 -BaseUrl https://<dominio>` → sin fallas
- [ ] `./ops/acceptance-production.ps1 -BaseUrl https://<dominio> -Email <admin>` → sin observaciones
- [ ] Las cinco páginas legales muestran la versión y fecha correctas

## Fase 6 — Primera socia

- [ ] El administrador la invita desde la consola
- [ ] Ella recibe un **enlace temporal de un solo uso** y define su propia contraseña
- [ ] Entra y ve la consola con su alcance

Nunca se envía una contraseña por WhatsApp. Nunca se comparte una cuenta.

## Fase 7 — Primer negocio fundador

Ver `13_FIRST_BUSINESS_CHECKLIST.md`.

## Fase 8 — Primera operación real

- [ ] Una clienta real agenda una cita (o toma un turno, o hace un pedido)
- [ ] El propietario la ve en su panel
- [ ] El seguimiento por código funciona desde el teléfono de la clienta
- [ ] El cambio de estado se refleja

## Fase 9 — Observación de 24 a 48 horas

- [ ] Respaldo ejecutado y verificado
- [ ] Sin errores 5xx en los registros
- [ ] Sin reinicios inesperados (tiempo de proceso creciente y continuo)
- [ ] Gasto dentro de lo previsto
- [ ] El propietario reporta que la herramienta le sirve

## Fase 10 — Los cuatro restantes

Sólo con la fase 9 limpia. De uno en uno, repitiendo las fases 6, 7 y 8 para cada negocio.

Entre uno y otro, comprobar que el gasto sigue dentro del límite.
