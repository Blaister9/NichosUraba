# 10 — Respuesta a incidentes

## Primer paso, siempre

Abrir la pantalla privada de salud (`/api/v1/admin/health`, o
`ops/acceptance-production.ps1`) y anotar: ambiente, versión, commit, tiempo de proceso, estado de
la base, de las migraciones, del almacenamiento y de Data Protection.

El **tiempo de proceso** es el dato más revelador: si es de minutos, hubo un reinicio y el
problema probablemente es de arranque, no de tráfico.

## Clasificación

| Síntoma | Gravedad | Ir a |
| --- | --- | --- |
| El sitio no responde | Alta | A |
| Responde pero `ready` falla | Alta | B |
| Errores 5xx intermitentes | Media | C |
| Sesiones que se caen solas | Alta | D |
| Datos personales ilegibles | **Crítica** | E |
| Sospecha de acceso no autorizado | **Crítica** | F |
| Lentitud | Baja | G |

---

### A — No responde

1. Estado del servicio en Railway. ¿Desplegando, caído, reiniciando en bucle?
2. Registros del arranque. `StartupGuard` reporta **todos** los problemas de configuración juntos,
   en un solo mensaje que empieza por `La configuración de Production no es apta para operar`.
3. Si es configuración: corregir la variable y redesplegar.
4. Si no arranca por otra causa: `11_ROLLBACK.md`.

### B — `ready` falla y `live` responde

Es el diseño funcionando: el proceso vive pero se declara no apto.

1. Consultar la pantalla de salud: informa si falló la migración y con qué tipo de excepción.
2. Si falló la migración: **no reintentar a ciegas**. Restaurar el respaldo previo en una base
   temporal, aplicar la migración allí y averiguar por qué falla.
3. Si quedan migraciones pendientes: `Database__MigrateOnStartup` puede estar en `false`.
4. Mientras `ready` falle, Railway conserva el despliegue anterior: **hay tiempo para pensar**.

### C — 5xx intermitentes

1. Filtrar registros por nivel `Error`. Cada entrada trae `CorrelationId`, `Commit` y, si aplica,
   `ActorUserId` y `BusinessId`.
2. Si el usuario reportó el fallo, pedirle el valor de `X-Correlation-Id`; lleva directo a la traza.
3. Correlacionar con el commit: ¿empezaron tras un despliegue concreto?

### D — Sesiones que se caen

Casi siempre es Data Protection.

1. ¿La pantalla de salud dice `Persistente en /app/keys`?
2. Si dice «En memoria» o «No existe la ruta», el volumen no está montado: **es urgente**, porque
   además los datos personales nuevos se están cifrando con llaves que se perderán.
3. Comprobar el montaje del volumen en Railway y redesplegar.
4. Ver `06_DATA_PROTECTION.md`.

### E — Datos personales ilegibles

Las llaves cambiaron o se perdieron.

1. **No borrar nada. No redesplegar.**
2. Restaurar el respaldo de llaves más reciente sobre `/app/keys`.
3. Si no hay respaldo de llaves, los datos cifrados con las llaves perdidas son irrecuperables.
   Los negocios, servicios, horarios y estructura siguen intactos; lo que se pierde son alias,
   teléfonos y notas.
4. Documentar el alcance: cuántos registros y de qué periodo.
5. Evaluar la obligación de notificar a los titulares.

### F — Sospecha de acceso no autorizado

1. Rotar de inmediato: cadena de conexión, claves HMAC y credenciales de R2.
2. Forzar el cierre de sesiones: cambiar la contraseña del afectado invalida su sello de
   seguridad y con él sus sesiones.
3. Revisar `/api/v1/admin/access-audit`: invitaciones, aceptaciones, revocaciones y reinicios de
   acceso, con actor, IP y fecha.
4. Revisar la auditoría por negocio: `/api/v1/admin/businesses/{id}/audit`.
5. Conservar los registros antes de que Railway los rote.
6. Si hubo exposición de datos personales, evaluar la notificación a la autoridad y a los
   titulares.

### G — Lentitud

1. Métricas de CPU y memoria en Railway.
2. Recordar que la aplicación y PostgreSQL están en regiones distintas: unos 73 ms por consulta.
   Una pantalla lenta suele ser número de consultas, no potencia. Ver `15_KNOWN_RISKS.md`.
3. Las pruebas de regresión de rendimiento afirman cuántas sentencias cuesta cada pantalla; una
   regresión de N+1 las rompe.

---

## Qué anotar de cada incidente

| Campo | |
| --- | --- |
| Fecha y hora (UTC) | |
| Síntoma observado | |
| Commit desplegado | |
| Correlaciones relevantes | |
| Causa raíz | |
| Acción tomada | |
| ¿Hubo datos personales afectados? | |
| Qué evitaría la repetición | |

## Contactos

| Rol | Quién |
| --- | --- |
| Responsable técnico | _(pendiente)_ |
| Responsable legal / datos | `Legal__PrivacyEmail` |
| Soporte a socias | `Legal__SupportEmail` |
