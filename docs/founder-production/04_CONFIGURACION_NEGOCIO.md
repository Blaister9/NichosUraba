# 04 — Configuración de un negocio

## Checklist de onboarding

La pantalla del negocio muestra una barra de avance y, para cada requisito no cumplido, el
mensaje concreto de lo que falta.

| Clave | Requisito | Aplica |
| --- | --- | --- |
| `public-information` | Nombre, descripción breve y descripción completa | Siempre |
| `contact` | Al menos un teléfono, WhatsApp o correo público | Siempre |
| `location` | Dirección | Siempre |
| `logo` | Logo cargado | Siempre |
| `cover` | Imagen de portada cargada | Siempre |
| `modules` | Al menos una función habilitada | Siempre |
| `hours` | Horario de atención | Si hay citas |
| `services` | Un servicio activo | Si hay citas |
| `queue` | Fila virtual configurada | Si hay turnos |
| `pickup-settings` | Franjas de recogida | Si hay pedidos |
| `catalog-category` | Una categoría del menú | Si hay pedidos |
| `catalog-product` | Un producto activo | Si hay pedidos |
| `active-owner` | Persona propietaria asignada | Siempre |
| `permissions` | Su membresía está activa | Siempre |

El botón «Enviar a revisión» permanece deshabilitado hasta que el porcentaje llega al 100 %.

## Campos del perfil comercial

| Campo | Límite | Validación |
| --- | --- | --- |
| Nombre comercial | 160 | Obligatorio, sin HTML |
| Identificador público (slug) | 3–120 | Único, normalizado sin tildes |
| Descripción breve | 160 | Obligatoria |
| Descripción completa | 600 | Sin HTML |
| Categoría | — | Del catálogo |
| Municipio | — | Del catálogo |
| Dirección | 240 | Sin HTML |
| Punto de referencia | 160 | Sin HTML |
| Teléfono | 30 | 7 a 15 dígitos |
| WhatsApp | 500 | URL `http`/`https` |
| Correo público | 160 | Formato de correo |
| Instagram | 500 | El dominio debe ser `instagram.com` |
| Facebook | 500 | El dominio debe ser `facebook.com` |
| Enlace de ubicación | 500 | URL `http`/`https` |
| Instrucciones para clientes | 600 | Sin HTML |

Todas las escrituras usan concurrencia optimista: si alguien más guardó mientras usted editaba,
la respuesta es 409 y hay que recargar.

## Módulos

Citas, turnos virtuales y pedidos para recoger se habilitan por separado. Desactivar uno conserva
su configuración y su historial; sólo deja de ofrecerse al público. Cambiar los módulos de un
negocio publicado lo devuelve a configuración pendiente.

## Horarios y catálogos

Cada módulo mantiene su configuración propia, accesible desde el panel del negocio:

- **Citas:** horario semanal, excepciones por fecha, servicios y personal.
- **Turnos:** definición de la fila, duración media y capacidad de espera.
- **Pedidos:** franjas de recogida, categorías y productos.

## Flujo de revisión

1. La socia completa el checklist y pulsa **Enviar a revisión**. El negocio pasa a `PendingReview`
   y sigue sin ser público.
2. La administración abre la vista previa y decide.
3. **Aprobar y publicar** deja el negocio en `Active` y visible en el directorio.
4. **Devolver con observaciones** lo regresa a `PendingConfiguration` con una nota que la socia ve
   en su pantalla.
5. Todo cambio de estado queda en el historial, con fecha, responsable y nota.

## Vista previa

`/admin/negocios/{id}/vista-previa` muestra exactamente la ficha pública —logo, portada, galería,
contacto, horarios y servicios— antes de publicar. Los botones de operación pública aparecen
desactivados para evitar confusiones.
