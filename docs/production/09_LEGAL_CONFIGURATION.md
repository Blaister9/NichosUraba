# 09 — Configuración legal, privacidad y retención

## Variables obligatorias

Production **no arranca** si falta cualquiera de las siete. No tienen valor por omisión y **no se
inventan aquí**: los aporta la persona o sociedad que responde por el tratamiento de datos.

| Variable | Qué es | Ejemplo de forma (no de contenido) |
| --- | --- | --- |
| `Legal__ResponsibleName` | Nombre legal del responsable del tratamiento | Razón social o nombre completo |
| `Legal__Identification` | Identificación del responsable | NIT o número de cédula |
| `Legal__Address` | Domicilio para notificaciones | Dirección física en Colombia |
| `Legal__PrivacyEmail` | Canal de consultas y reclamos sobre datos | Buzón atendido |
| `Legal__SupportEmail` | Canal de soporte de la plataforma | Buzón atendido |
| `Legal__PolicyVersion` | Versión de la política vigente | `2026-1` |
| `Legal__PolicyEffectiveDate` | Fecha desde la que rige esa versión | `2026-08-15` |

Los dos correos deben ser buzones **realmente atendidos**: la ley colombiana obliga a responder
consultas y reclamos en plazos definidos, y ese canal es la vía formal.

En Demo estos valores están rellenos con textos que dicen explícitamente que son de demostración,
para que nadie los confunda con datos reales.

## Documentos publicados

Cinco páginas públicas, servidas en `/legal/{documento}` y enlazadas desde el pie de toda página:

| Ruta | Documento |
| --- | --- |
| `/legal/politica-de-datos` | Política de tratamiento de datos |
| `/legal/aviso-de-privacidad` | Aviso de privacidad |
| `/legal/terminos` | Términos y condiciones |
| `/legal/retencion` | Política de eliminación y retención |
| `/legal/reclamos` | Consultas y reclamos |

Cada página muestra la versión y la fecha de vigencia tomadas de las variables anteriores, así
que actualizar la política es cambiar `Legal__PolicyVersion` y `Legal__PolicyEffectiveDate`.

## Consentimiento

`IConsentPolicyProvider` expone la versión **efectiva**: la que el servidor exigirá en los
formularios públicos. Al agendar una cita, tomar un turno o hacer un pedido se registra un
`ConsentReceipt` con:

- la versión de política aceptada;
- la fecha y hora;
- la finalidad;
- el vínculo con la operación concreta (cita, turno o pedido).

Es el consentimiento mínimo necesario para prestar el servicio. No se pide autorización
publicitaria, no se ceden datos a terceros y **no se recogen datos sensibles** ni documentos de
identidad.

Datos recogidos del cliente final: un nombre o alias, un teléfono de contacto y, opcionalmente,
una nota. Los tres se guardan **cifrados**.

## Retención y borrado

Política definida para esta etapa. La implementación preferente es el **archivo lógico y la
anonimización**, no el borrado físico.

| Dato | Retención | Al vencer |
| --- | --- | --- |
| Citas | 24 meses desde la fecha de la cita | Anonimizar alias, teléfono y notas; conservar el registro agregado |
| Pedidos | 24 meses desde la entrega | Igual que citas |
| Turnos | 6 meses | Anonimizar; el turno tiene poco valor histórico |
| Cuentas de usuario | Mientras la membresía esté activa, más 12 meses | Desactivar; anonimizar si se solicita supresión |
| Auditoría de accesos | 36 meses | Conservar: es la evidencia de quién hizo qué |
| Imágenes | Mientras el negocio esté publicado | Eliminar del bucket al archivar el negocio |
| Registros de aplicación | Según la retención de Railway | No contienen datos personales |

Principios:

1. **No se borra físicamente desde la interfaz por omisión.** Los negocios se archivan; las
   membresías se desactivan. Un clic no destruye historia.
2. La supresión a solicitud del titular se atiende por el canal de `Legal__PrivacyEmail` y la
   ejecuta la administración de plataforma, con registro en auditoría.
3. La anonimización conserva la fila y su valor estadístico, y elimina lo que identifica.
4. La auditoría **no se anonimiza**: es lo que permite reconstruir un incidente.

La ejecución periódica de esta política es hoy **manual**. Automatizarla es trabajo futuro; ver
`15_KNOWN_RISKS.md`.

## Antes del go-live

- [ ] Los siete `Legal__*` con valores reales, revisados por quien responde legalmente
- [ ] Los dos buzones existen y alguien los atiende
- [ ] Las cinco páginas legales se abren y muestran la versión correcta
- [ ] La política de retención de arriba se ha leído y aceptado
