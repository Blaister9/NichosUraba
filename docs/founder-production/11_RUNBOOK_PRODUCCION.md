# 11 — Runbook de producción

## Ambientes

| Ambiente | Base de datos | Imágenes | Semilla | Datos |
| --- | --- | --- | --- | --- |
| Development | Local en Docker | Carpeta temporal | Sí | Ficticios |
| Demo | PostgreSQL Demo | Volumen `/app/media` | Sí, con secretos externos | Ficticios |
| Production | PostgreSQL Production | Bucket R2 | **No** | Reales |

Demo y Production **no comparten** base de datos, ni volumen de llaves, ni bucket, ni secretos.

## Validaciones de arranque

En Production la aplicación se niega a arrancar si:

- `DemoSeed__Enabled` es `true`;
- `DemoSeed__AdminPassword` o `DemoSeed__BusinessPassword` están definidas;
- falta cualquiera de las siete variables `Legal__*`;
- falta cualquier variable de almacenamiento de objetos;
- el proveedor de almacenamiento es `Local`;
- falta `DataProtection__KeysPath`;
- la cadena de conexión apunta a una base Demo;
- se detecta una contraseña de demostración conocida.

Fuera de Development también exige `URABACONECTA_TRACKING_HMAC_KEY`, y lo hace **al arrancar**,
no en la primera petición: una instancia mal configurada no llega a reportarse como saludable.

El mensaje de error enumera todos los problemas de una vez.

## Variables requeridas en Production

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection
URABACONECTA_TRACKING_HMAC_KEY
URABACONECTA_INVITATION_HMAC_KEY
DataProtection__KeysPath=/app/keys
DataProtection__ApplicationName
DataProtection__CertificateBase64        (opcional, ver Fase 10)
DataProtection__CertificatePassword      (opcional)
ObjectStorage__Provider=S3
ObjectStorage__ServiceUrl
ObjectStorage__Bucket
ObjectStorage__AccessKey
ObjectStorage__SecretKey
ObjectStorage__PublicBaseUrl
ObjectStorage__Region=auto
Legal__ResponsibleName
Legal__Identification
Legal__Address
Legal__PrivacyEmail
Legal__SupportEmail
Legal__PolicyVersion
Legal__PolicyEffectiveDate
DemoSeed__Enabled=false
Deployment__Commit
Deployment__DeployedAtUtc
```

Ningún valor de estos aparece en el repositorio.

## Despliegue

1. Respaldo manual: base de datos y volumen de llaves ([09](09_BACKUP_Y_RESTORE.md)).
2. Confirme el estado actual en `/admin/salud`.
3. Despliegue la revisión nueva. Las migraciones se aplican al arrancar.
4. Espere a que `/health/ready` responda 200.
5. Ejecute los *smoke tests* de abajo.
6. Registre `Deployment__Commit` y `Deployment__DeployedAtUtc`.

## Smoke tests

| # | Comprobación | Esperado |
| --- | --- | --- |
| 1 | `GET /health/live` | 200 |
| 2 | `GET /health/ready` | 200 |
| 3 | `GET /api/v1/public/businesses` | 200 con la lista publicada |
| 4 | `GET /api/v1/public/legal` | Los datos reales del responsable |
| 5 | `GET /legal/politica-de-datos` | Sin el aviso de «no configurado» |
| 6 | Iniciar sesión como PlatformAdmin | Entra al panel |
| 7 | `/admin/salud` | Base conectada, sin migraciones pendientes, almacenamiento S3 disponible, semilla Demo deshabilitada |
| 8 | Abrir una ficha pública | Logo y portada se muestran desde el dominio de imágenes |
| 9 | Crear una cita de prueba y cancelarla | 201 y luego cancelada |
| 10 | Reiniciar el servicio y repetir 6 | La sesión y las imágenes persisten |

## Observabilidad

- `/health/live` — el proceso responde.
- `/health/ready` — además, PostgreSQL responde.
- `/admin/salud` — ambiente, versión, commit, fecha de despliegue, migraciones pendientes,
  proveedor y estado del almacenamiento, estado de las llaves y semilla Demo.
- Logs estructurados de ASP.NET Core en la salida estándar, recogidos por el proveedor.
- Las excepciones no controladas devuelven un `ProblemDetails` sin detalles internos y quedan en
  el log con su `traceId`, que también viaja como `CorrelationId` en la auditoría.

**Todavía no hay** un identificador de correlación propio en los encabezados de respuesta ni
alertas automáticas por 5xx: ver [15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).

## Incidentes frecuentes

| Síntoma | Causa probable | Qué hacer |
| --- | --- | --- |
| La aplicación no arranca y el log enumera variables | Falta configuración obligatoria | Complete las variables y vuelva a desplegar |
| Todas las sesiones se cerraron tras un despliegue | Se perdió el volumen de llaves | Restaure el volumen; los datos cifrados anteriores dependen de él |
| Los alias y teléfonos aparecen ilegibles | Se perdió el volumen de llaves | Restaure la copia inicial bloqueada |
| Las imágenes no cargan | Credenciales de R2 o dominio público mal configurados | Verifique en `/admin/salud` |
| Un negocio nuevo no ofrece cupos de cita | No hay horario o no hay personal vinculado al servicio | Revise el horario y el personal en la configuración del negocio |
| Se publicó algo incompleto | Se aprobó sin revisar la vista previa | Suspenda, corrija y vuelva a publicar |
