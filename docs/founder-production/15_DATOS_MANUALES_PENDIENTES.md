# 15 — Datos y decisiones pendientes del usuario

Nada de lo que sigue se inventó ni se completó con valores de relleno. Cada punto exige una
decisión, una credencial o un dato que sólo el usuario puede aportar.

## 1. Datos jurídicos — bloqueante

Sin estas siete variables, **la aplicación no arranca en Production** y los documentos legales
muestran un aviso de que no están configurados.

| Variable | Qué es |
| --- | --- |
| `Legal__ResponsibleName` | Razón social o nombre de la persona responsable del tratamiento |
| `Legal__Identification` | NIT o cédula |
| `Legal__Address` | Domicilio para notificaciones |
| `Legal__PrivacyEmail` | Correo para ejercer derechos sobre datos personales |
| `Legal__SupportEmail` | Canal de consultas y reclamos |
| `Legal__PolicyVersion` | Identificador de la versión, por ejemplo `2026-1` |
| `Legal__PolicyEffectiveDate` | Fecha de entrada en vigencia |

## 2. Revisión legal de los textos — bloqueante

Los cinco documentos de `/legal/*` son una redacción operativa, **no un dictamen jurídico**.
Deben ser revisados por alguien con competencia en protección de datos en Colombia antes de
recibir el primer dato real de una persona.

## 3. Cuenta de Cloudflare R2 — bloqueante, con costo

Activar R2 exige iniciar sesión en Cloudflare y **registrar un método de pago**, aunque el
consumo del piloto quede por debajo del nivel gratuito. Esa decisión es del usuario.

Los pasos exactos, pantalla por pantalla, están en
[05_IMAGENES_Y_R2.md](05_IMAGENES_Y_R2.md#configuración-de-cloudflare-r2--pasos-manuales).

Resultado esperado: valores para `ObjectStorage__ServiceUrl`, `ObjectStorage__Bucket`,
`ObjectStorage__AccessKey`, `ObjectStorage__SecretKey` y `ObjectStorage__PublicBaseUrl`.

## 4. Recursos de producción separados — con costo

Producción exige recursos **nuevos**, no los de la demostración:

- un proyecto o servicio de despliegue aparte;
- una base de datos PostgreSQL nueva y vacía;
- un volumen de Data Protection nuevo;
- un bucket R2 nuevo.

Cada recurso adicional puede generar cobro en el proveedor. Consúltelo antes de crearlos.

## 5. Cifrado de las llaves en reposo — decisión de seguridad

Hoy el anillo de llaves se guarda **sin cifrar** en el volumen; está verificado y documentado en
[14_RESULTADOS_PRUEBAS.md](14_RESULTADOS_PRUEBAS.md#hallazgo-las-llaves-no-están-cifradas-en-reposo).
Quien acceda al volumen puede descifrar los alias y teléfonos de los clientes.

V5 soporta cifrarlo con un certificado X.509. Para activarlo hay que:

1. generar un certificado PKCS#12 con su contraseña;
2. entregarlo en base64 en `DataProtection__CertificateBase64` y la contraseña en
   `DataProtection__CertificatePassword`;
3. **custodiar el certificado fuera del proveedor**: si se pierde, las llaves quedan ilegibles y
   con ellas todos los datos personales cifrados.

La alternativa es aceptar el riesgo por escrito para el piloto. Es una decisión del usuario.

## 6. Proveedor de correo — no bloqueante para el piloto

Sin proveedor de correo:

- `/Account/ForgotPassword` no entrega nada;
- las invitaciones se entregan copiando el enlace a mano.

Para el piloto de cinco negocios acompañados es viable. Para crecer hace falta contratar un
proveedor (Resend, Postmark, Amazon SES u otro), lo que implica una credencial y un costo.

## 7. Automatización de respaldos — decisión operativa

El procedimiento manual está documentado y probado. Automatizarlo exige decidir dónde se guardan
los archivos y dónde vive la credencial de acceso a la base. Ver
[09_BACKUP_Y_RESTORE.md](09_BACKUP_Y_RESTORE.md#automatización).

## 8. Dominio propio — opcional

Hoy la demostración vive en un subdominio del proveedor. Un dominio propio mejora la confianza y
permite servir las imágenes desde `imagenes.sudominio.co`. Implica registro anual y configuración
de DNS.

## 9. Pendientes técnicos menores

Registrados para no darlos por hechos:

- **Exportación de datos por negocio.** Hoy se hace con consultas sobre la base restaurada.
- **Identificador de correlación en las respuestas.** El `traceId` está en el log y en la
  auditoría, pero no viaja en un encabezado de respuesta.
- **Alertas automáticas por 5xx.** Dependen del plan del proveedor.
- **Eliminación física de imágenes.** La eliminación es lógica; el borrado del objeto en el bucket
  es un paso administrativo aparte que todavía no tiene interfaz.
- **Zona horaria del negocio.** Está fija en `America/Bogota` y no es editable desde la interfaz.

## 10. Lo que no se ejecutó y por qué

| Acción | Por qué se detuvo |
| --- | --- |
| Desplegar en la instancia Demo de Railway | Exige iniciar sesión en el proveedor |
| Aplicar migraciones sobre la base Demo en la nube | Exige credenciales y es una operación sobre datos existentes |
| Crear el proyecto de producción | Genera cobro; la decisión es del usuario |
| Crear el bucket R2 | Exige cuenta y método de pago |
| Crear cuentas de negocios reales | La instrucción es no crear clientes reales todavía |
