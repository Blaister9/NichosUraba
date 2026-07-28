# 12 — Checklist de salida a producción

Cada punto se marca sólo cuando está **verificado**, no cuando está implementado.

## A. Bloqueos que dependen del usuario

Ninguno de estos se puede resolver desde el código.

- [ ] Datos jurídicos reales del responsable del tratamiento (siete variables `Legal__*`).
- [ ] Revisión de los cinco textos legales por una persona con competencia legal en Colombia.
- [ ] Cuenta de Cloudflare con R2 activado y método de pago registrado.
- [ ] Bucket de producción creado, con dominio público y credenciales de sólo ese bucket.
- [ ] Proyecto de producción separado del de demostración en el proveedor de despliegue.
- [ ] PostgreSQL de producción, nuevo y vacío.
- [ ] Volumen de Data Protection nuevo.
- [ ] Decisión sobre el cifrado de las llaves en reposo (certificado X.509) o aceptación
      explícita del riesgo documentado en la sección 10.

## B. Configuración

- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] `DemoSeed__Enabled=false` y sin contraseñas de semilla definidas.
- [ ] Las siete variables `Legal__*` con valores reales.
- [ ] Las siete variables `ObjectStorage__*` apuntando al bucket de producción.
- [ ] `URABACONECTA_TRACKING_HMAC_KEY` y `URABACONECTA_INVITATION_HMAC_KEY` nuevas y distintas
      de las de Demo.
- [ ] `DataProtection__KeysPath` sobre un volumen persistente.
- [ ] `Deployment__Commit` y `Deployment__DeployedAtUtc` registrados.
- [ ] La cadena de conexión **no** contiene la palabra `demo`.

## C. Arranque

- [ ] La aplicación arranca sin errores de configuración.
- [ ] `/health/live` y `/health/ready` responden 200.
- [ ] `/admin/salud` muestra: ambiente Production, base conectada, sin migraciones pendientes,
      almacenamiento S3 disponible, semilla Demo deshabilitada.

## D. Datos

- [ ] La base no contiene ningún negocio ficticio.
- [ ] La base no contiene ninguna cuenta `*.demo`.
- [ ] Existe exactamente una cuenta `PlatformAdmin`, creada por invitación.
- [ ] La contraseña de esa cuenta la definió la propia persona, por enlace de un solo uso.

## E. Legal

- [ ] Los cinco documentos de `/legal/*` muestran los datos reales, sin el aviso de
      «no configurado».
- [ ] Los formularios de citas, turnos y pedidos muestran el enlace a la política y la versión
      vigente.
- [ ] Una operación de prueba genera su recibo de consentimiento con la versión correcta.

## F. Respaldos

- [ ] Respaldo manual de PostgreSQL tomado y verificado.
- [ ] Copia inicial del volumen de llaves tomada, verificada y **guardada fuera del proveedor**.
- [ ] Restauración probada sobre una base temporal, comparando conteos.
- [ ] Cadencia de respaldo acordada y con responsable asignado.

## G. Seguridad

- [ ] No hay secretos en Git (`git log -p | grep` sobre los nombres de variable no arroja valores).
- [ ] Las cookies van con `Secure` (automático fuera de Development).
- [ ] Las rutas de la API responden 401 y 403, no redirecciones.
- [ ] Un propietario de un negocio recibe 403 al pedir datos de otro.
- [ ] Una socia recibe 403 al intentar publicar o crear otra socia.

## H. Primer negocio real

- [ ] Se incorpora **uno solo**, acompañado.
- [ ] Recorrido completo: alta, perfil, imágenes, módulo, horarios, catálogo, propietario,
      vista previa, revisión, publicación.
- [ ] La persona propietaria entra con su propia contraseña.
- [ ] Se procesa una operación real de principio a fin.
- [ ] Se reinicia el servicio y todo persiste.
- [ ] Sólo después de validar el primero se incorporan los otros cuatro.

## I. Cierre

- [ ] Se registró la fecha de salida y el commit desplegado.
- [ ] Las guías 06, 07 y 08 se entregaron a quien corresponde.
- [ ] Hay un canal acordado para reportar incidentes.
