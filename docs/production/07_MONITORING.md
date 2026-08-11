# 07 — Observabilidad y alertas

## Registro estructurado

Fuera de Development el registro sale en **JSON con ámbitos** (`AddJsonConsole` con
`IncludeScopes`), que es lo que hace consultable el visor de Railway.

Cada petición pasa por `RequestCorrelationMiddleware`, que adjunta a todo lo que se registre
durante esa petición:

| Campo | Origen |
| --- | --- |
| `CorrelationId` | Cabecera `X-Correlation-Id` del cliente si es válida; si no, el identificador de la traza |
| `RequestId` | `TraceIdentifier` de la petición |
| `Environment` | Nombre del ambiente |
| `AppVersion` | Versión del ensamblado |
| `Commit` | `Deployment__Commit` |
| `ActorUserId` | Sólo si hay sesión. **Identificador, nunca el correo** |
| `BusinessId` | Sólo si la ruta lo lleva |

La misma correlación viaja de vuelta en la cabecera `X-Correlation-Id` de la respuesta, así que
una socia puede reportar «me falló y decía `abc123`» y el operador encuentra la traza exacta.

La cabecera entrante se acota antes de registrarse: máximo 64 caracteres, sólo alfanuméricos,
guion y guion bajo. Un valor con salto de línea se descarta, porque permitiría falsificar
entradas en el registro.

## Lo que nunca se registra

- Contraseñas y tokens de invitación
- Claves HMAC
- Cadenas de conexión (por eso los fallos de migración registran el **tipo** de excepción, no su
  mensaje: Npgsql incluye la cadena en el texto)
- Correos, teléfonos, alias y notas de cliente
- Cualquier dato personal que no sea estrictamente necesario

## Salud

| Extremo | Autenticación | Para qué |
| --- | --- | --- |
| `/health/live` | Anónimo | ¿El proceso responde? Nunca consulta la base |
| `/health/ready` | Anónimo | ¿Puede recibir tráfico? Comprueba PostgreSQL **y el esquema** |
| `/api/v1/admin/health` | `PlatformAdmin` | Pantalla privada con el detalle completo |

`/health/ready` agrupa dos comprobaciones:

1. `postgresql` — conectividad del `DbContext`.
2. `migrations` — falla si la migración de arranque falló **o** si quedan migraciones pendientes.

Esto es deliberado: una instancia con el esquema atrasado responde errores de columna inexistente.
Que no pase la readiness hace que Railway conserve el despliegue anterior sirviendo, en lugar de
publicar una versión rota.

La pantalla privada informa: ambiente, versión, commit, fecha de despliegue, estado de PostgreSQL,
estado y proveedor del almacenamiento, estado de Data Protection, si el sembrado Demo está
activo, **tiempo de proceso** y **resultado de la migración**.

El tiempo de proceso delata los reinicios silenciosos, que de otro modo sólo se notan porque las
sesiones se caen.

## Alertas

Railway Hobby ofrece notificaciones de despliegue y de uso, pero no alertas por métrica con
umbral. Lo que no se puede configurar se vigila a mano; se documenta aquí para que la revisión
sea una rutina y no un descubrimiento.

| Señal | Umbral | Cómo | Acción |
| --- | --- | --- | --- |
| Respuestas 5xx | Cualquiera en Production | Visor de registros, filtrando por nivel `Error` | Buscar la correlación y diagnosticar |
| Health check fallido | 2 sondeos seguidos | Notificación de despliegue de Railway | `11_ROLLBACK.md` |
| Reinicios | Más de 1 al día | Comparar el tiempo de proceso en la pantalla de salud | Revisar memoria y registros |
| CPU alta | > 80 % sostenido 15 min | Panel de métricas | Revisar consultas y N+1 |
| Memoria alta | > 80 % del contenedor | Panel de métricas | Revisar fugas y tamaño de respuestas |
| Almacenamiento | > 80 % del volumen | Panel de métricas | Ampliar volumen o purgar según retención |
| Gasto | Alerta suave en USD 4, límite duro en USD 5 | Ajustes de uso de Railway | **No subir de plan sin autorización** |
| Errores de PostgreSQL | Cualquiera | Registros del servicio de base | Revisar conexiones y migraciones |

### Revisión manual mientras no haya alertas automáticas

- **Diaria durante las primeras 48 h de cada incorporación**: pantalla de salud y errores.
- **Semanal en régimen normal**: gasto, métricas, tiempo de proceso y registros de error.

## Comprobación tras cada despliegue

```powershell
./ops/smoke-production.ps1 -BaseUrl https://<dominio>
```

Sólo lectura: no crea datos. Comprueba portada, salud, inicio de sesión, directorio, las cinco
páginas legales, que la consola privada exija sesión, que ninguna respuesta exponga rastros de
pila y que estén las cabeceras de seguridad.

Para una verificación autenticada, manual y también de sólo lectura:

```powershell
./ops/acceptance-production.ps1 -BaseUrl https://<dominio> -Email <correo-admin>
```
