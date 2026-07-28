# 10 — Privacidad y consentimiento

## Documentos publicados

| Ruta | Documento |
| --- | --- |
| `/legal/politica-de-datos` | Política de tratamiento de datos |
| `/legal/aviso-de-privacidad` | Aviso de privacidad |
| `/legal/terminos` | Términos y condiciones |
| `/legal/retencion` | Política de eliminación y retención |
| `/legal/reclamos` | Canal de consultas y reclamos |

Están enlazados desde el pie de página de todas las pantallas.

## Los datos del responsable no están en el código

Razón social, identificación, domicilio y correos se leen de la configuración. **No se
inventaron.** Mientras falten, cada documento muestra un aviso que dice explícitamente que aún no
está configurado, en vez de mostrar datos falsos.

```
Legal__ResponsibleName
Legal__Identification
Legal__Address
Legal__PrivacyEmail
Legal__SupportEmail
Legal__PolicyVersion
Legal__PolicyEffectiveDate
```

En Production, **la aplicación no arranca** si falta cualquiera de las siete. Los valores reales
están pendientes de que el usuario los aporte: ver
[15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).

## Consentimiento en los formularios públicos

Los tres formularios públicos —citas, turnos virtuales y pedidos para recoger— muestran:

- una casilla de aceptación explícita;
- el enlace a la política de tratamiento de datos y al aviso de privacidad;
- la versión que se está aceptando.

El servidor **rechaza** una solicitud sin aceptación o con una versión distinta de la vigente
(`CONSENT_REQUIRED`). El formulario consulta la versión al servidor antes de mostrarse, de modo
que la que se muestra y la que se exige son siempre la misma.

Los turnos virtuales, que hasta esta versión no pedían consentimiento, ahora también generan su
recibo.

## Evidencia que se guarda

Cada operación crea un `ConsentReceipt` con:

- el negocio;
- la versión de la política aceptada;
- la finalidad, en texto;
- la fecha y hora UTC de la aceptación;
- el vínculo con la cita, el turno o el pedido;
- opcionalmente, la dirección IP de origen.

No se guarda nada más. No hay huella del navegador, ni identificadores publicitarios, ni
seguimiento entre sitios.

## Datos que se recogen del público

| Dato | Por qué | Cómo se guarda |
| --- | --- | --- |
| Nombre o alias | Para llamar a la persona | Cifrado |
| Teléfono | Para avisar sobre la operación | Cifrado, con los últimos 4 dígitos en claro para búsqueda |
| Nota breve | Contexto opcional de la solicitud | Cifrada |

No se piden documentos de identidad, ni direcciones de casa, ni fechas de nacimiento, ni datos
sensibles de ninguna clase.

## Código de seguimiento

Se genera aleatoriamente y se entrega una sola vez. En la base de datos se guarda únicamente su
HMAC-SHA256: leer la base no permite reconstruir los enlaces de seguimiento.

## Derechos de las personas

La política indica el correo `Legal__PrivacyEmail` para conocer, actualizar, rectificar y
solicitar la supresión de datos. Hoy la atención de esas solicitudes es un procedimiento manual
sobre la base de datos.

## Versionado de la política

`Legal__PolicyVersion` identifica la versión vigente. Al publicar un texto nuevo:

1. Actualice el texto.
2. Suba la versión, por ejemplo de `2026-1` a `2026-2`.
3. Actualice `Legal__PolicyEffectiveDate`.

Los recibos anteriores conservan la versión que la persona aceptó en su momento; nunca se
reescriben.

## Aviso

Los textos de los cinco documentos son una redacción operativa, no un dictamen jurídico.
**Deben ser revisados por una persona con competencia legal en protección de datos en Colombia
antes de recibir el primer dato real.** Está registrado como bloqueo en
[15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).
