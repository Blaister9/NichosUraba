# UrabaConecta — API y componentes

## 1. Convenciones de contratos

- Base: `/api/v1`.
- JSON en `camelCase`.
- El deserializador configura miembros JSON no mapeados como error; un campo inesperado, incluido `businessId`, responde `400 VALIDATION_FAILED`.
- Identificadores internos como UUID.
- Fechas/horas de respuesta en ISO 8601 con offset del negocio; comandos envían instantes ISO 8601.
- Dinero:

```json
{ "amount": 25000.00, "currency": "COP" }
```

- Listados paginados:

```json
{ "items": [], "page": 1, "pageSize": 20, "total": 0 }
```

- Errores con `application/problem+json`:

```json
{
  "type": "https://urabaconecta.local/problems/slot-unavailable",
  "title": "Ese horario ya no está disponible.",
  "status": 409,
  "code": "SLOT_UNAVAILABLE",
  "traceId": "00-...",
  "errors": {}
}
```

- Todo comando privado obtiene `BusinessId` de la ruta y lo reautoriza.
- Todo comando público obtiene `BusinessId` desde el slug o recurso resuelto; nunca lo acepta en el body.
- Endpoints mutables con cookie exigen antiforgery.
- Los códigos públicos viajan solo en ruta/cuerpo sobre HTTPS, nunca en logs ni telemetría.

## 2. Páginas públicas

| Ruta Blazor | Renderizado | Contenido |
|---|---|---|
| `/` | SSR + Interactive Auto | búsqueda, municipio, categoría, resultados |
| `/negocios/{slug}` | SSR + Interactive Auto | perfil, horario, oferta y módulos |
| `/negocios/{slug}/citas` | Interactive Auto | servicio, fecha, franja y datos |
| `/seguimiento/citas` | Interactive Auto | entrada de código |
| `/seguimiento/citas/{code}` | Interactive Auto | estado y cancelación |
| `/negocios/{slug}/turnos` | Interactive Auto | estado de cola y solicitud |
| `/seguimiento/turnos/{code}` | Interactive Auto + SignalR | número, turno actual y posición |
| `/negocios/{slug}/pedidos` | Interactive Auto | menú, carrito y franja |
| `/seguimiento/pedidos/{code}` | Interactive Auto | estado, ajuste y cancelación |
| `/privacidad` | SSR | resumen informativo provisional |

El código puede ocultarse visualmente después de cargar el seguimiento, pero la URL debe tratarse como sensible y no incluirse en analítica.

## 3. Páginas privadas

### Negocio

| Ruta | Permiso |
|---|---|
| `/panel` | autenticado |
| `/panel/{businessId}` | `BusinessMember` |
| `/panel/{businessId}/perfil` | `BusinessProfile.Manage` |
| `/panel/{businessId}/equipo` | `Workers.Manage` |
| `/panel/{businessId}/servicios` | `Catalog.Manage` |
| `/panel/{businessId}/productos` | `Catalog.Manage` |
| `/panel/{businessId}/disponibilidad` | `Catalog.Manage` |
| `/panel/{businessId}/citas` | `Appointments.Manage` |
| `/panel/{businessId}/turnos` | `Queue.Manage` |
| `/panel/{businessId}/pedidos` | `Orders.Manage` |

### Plataforma

| Ruta | Política |
|---|---|
| `/admin/negocios` | `PlatformAdmin` |
| `/admin/municipios` | `PlatformAdmin` |
| `/admin/categorias` | `PlatformAdmin` |
| `/admin/propietarios` | `PlatformAdmin` |
| `/admin/auditoria` | `PlatformAdmin` |

## 4. Componentes Blazor

### Compartidos

- `AppShell`;
- `PublicHeader`;
- `BusinessCard`;
- `SearchFilters`;
- `ModuleActionCard`;
- `LargeActionButton`;
- `StatusBadge`;
- `EmptyState`;
- `InlineError`;
- `LoadingSkeleton`;
- `ConfirmationDialog`;
- `PhoneField`;
- `ConsentCheckbox`;
- `TrackingCodeCard`;
- `PagedList`;
- `BusinessSwitcher`;
- `PermissionGate`;
- `ReconnectBanner`.

### Citas

- `ServicePicker`;
- `DatePicker`;
- `AvailableSlotGrid`;
- `AppointmentContactForm`;
- `AppointmentReview`;
- `AppointmentStatusTimeline`;
- `AppointmentList`;
- `AppointmentActionPanel`.

### Turnos

- `QueuePublicStatus`;
- `TakeTicketButton`;
- `TicketTrackingCard`;
- `QueueOperatorBoard`;
- `CurrentTicketPanel`;
- `WaitingTicketsList`.

### Pedidos

- `MenuCategoryTabs`;
- `ProductCard`;
- `CartDrawer`;
- `QuantityStepper`;
- `PickupSlotPicker`;
- `OrderContactForm`;
- `OrderReview`;
- `OrderStatusTimeline`;
- `AdjustmentResponsePanel`;
- `OrderOperatorBoard`.

Reglas UI:

- área táctil mínima 44 × 44 CSS px;
- una acción primaria por pantalla;
- etiquetas visibles, no solo placeholder;
- foco en primer error;
- contraste WCAG AA en colores principales;
- no depender solo de color;
- mensajes con `aria-live`;
- estado del servidor como fuente de verdad tras cada comando.

## 5. API pública — directorio

### `GET /api/v1/public/businesses`

Parámetros:

- `q` opcional, máximo 80;
- `municipality` slug opcional;
- `category` slug opcional;
- `page` 1..1000;
- `pageSize` 1..24.

Respuesta:

```json
{
  "items": [
    {
      "slug": "salon-bella-uraba",
      "name": "Salón Bella Urabá",
      "category": { "slug": "belleza", "name": "Belleza" },
      "municipality": { "slug": "apartado", "name": "Apartadó" },
      "description": "Datos ficticios para demostración.",
      "enabledModules": ["Scheduling"]
    }
  ],
  "page": 1,
  "pageSize": 12,
  "total": 1
}
```

### `GET /api/v1/public/businesses/{slug}`

Devuelve perfil publicado, horario y módulos. `404` si no existe, no está activo o no está publicado.

### `GET /api/v1/public/municipalities`

Solo activos, ordenados.

### `GET /api/v1/public/categories`

Solo activas con al menos un negocio publicado, salvo parámetro administrativo no público.

## 6. API pública — citas

### `GET /api/v1/public/businesses/{slug}/services`

Devuelve servicios activos con duración y precio informativo.

### `GET /api/v1/public/businesses/{slug}/appointment-slots`

Parámetros:

- `serviceId` requerido;
- `date` en `YYYY-MM-DD`.

Respuesta:

```json
{
  "businessTimeZone": "America/Bogota",
  "date": "2026-08-03",
  "slots": [
    { "start": "2026-08-03T09:00:00-05:00", "end": "2026-08-03T09:45:00-05:00" }
  ]
}
```

No expone trabajador si el negocio no lo decidió como dato público.

### `POST /api/v1/public/businesses/{slug}/appointments`

Solicitud:

```json
{
  "serviceId": "9ef8865e-5510-4a1c-858c-34fbd1508507",
  "start": "2026-08-03T09:00:00-05:00",
  "customerAlias": "Ana",
  "phone": "3001234567",
  "consentNoticeVersion": "pilot-1",
  "consentAccepted": true
}
```

Validaciones:

- módulo y negocio activos;
- servicio activo;
- instante futuro y dentro del horizonte;
- alias 2..100 caracteres;
- teléfono normalizado, 7..15 dígitos;
- consentimiento verdadero y versión vigente;
- franja disponible al confirmar transacción.

Respuesta `201`:

```json
{
  "trackingCode": "Sxj7F0-6Qq5KxE7m7kGv8A",
  "status": "Pending",
  "serviceName": "Corte y peinado",
  "start": "2026-08-03T09:00:00-05:00"
}
```

### `GET /api/v1/public/appointments/{code}`

Respuesta:

```json
{
  "status": "Confirmed",
  "statusLabel": "Confirmada",
  "businessName": "Salón Bella Urabá",
  "serviceName": "Corte y peinado",
  "start": "2026-08-03T09:00:00-05:00",
  "phoneMasked": "******4567",
  "canCancel": true,
  "updatedAt": "2026-07-30T15:12:00Z"
}
```

### `POST /api/v1/public/appointments/{code}/cancel`

Body opcional `{ "reason": "Ya no puedo asistir" }`, máximo 160. Responde `204`.

## 7. API pública — turnos

### `GET /api/v1/public/businesses/{slug}/queue`

```json
{
  "isOpen": true,
  "currentNumber": 18,
  "waitingCount": 4,
  "estimatedMinutesPerTicket": 15,
  "stateVersion": 22
}
```

La espera es estimada y debe etiquetarse como tal.

### `POST /api/v1/public/businesses/{slug}/queue/tickets`

Sin body o `{}`. No solicita PII.

Respuesta `201`:

```json
{
  "number": 23,
  "trackingCode": "uB6YPDPmQh_S2TQyRshtvQ",
  "peopleAhead": 4
}
```

Errores: `QUEUE_CLOSED`, `QUEUE_CAPACITY_REACHED`.

### `GET /api/v1/public/queue/tickets/{code}`

```json
{
  "number": 23,
  "status": "Waiting",
  "currentNumber": 18,
  "peopleAhead": 4,
  "stateVersion": 22
}
```

### `POST /api/v1/public/queue/tickets/{code}/cancel`

Solo `Waiting`. Responde `204`.

## 8. API pública — pedidos

### `GET /api/v1/public/businesses/{slug}/menu`

Devuelve categorías y productos activos/disponibles, con `catalogVersion` calculada.

### `GET /api/v1/public/businesses/{slug}/pickup-slots?date=YYYY-MM-DD`

Devuelve franjas con capacidad disponible.

### `POST /api/v1/public/businesses/{slug}/pickup-orders`

Solicitud:

```json
{
  "catalogVersion": "W/\"menu-42\"",
  "items": [
    { "productId": "bc612902-3c37-40c8-8f76-362c74650229", "quantity": 2 }
  ],
  "notes": "Sin cebolla",
  "pickupStart": "2026-08-03T12:30:00-05:00",
  "customerAlias": "Luis",
  "phone": "3101234567",
  "consentNoticeVersion": "pilot-1",
  "consentAccepted": true
}
```

El servidor ignora cualquier precio enviado.

Respuesta `201`:

```json
{
  "trackingCode": "Fgx6AkgWUnBKQBFHXW-2HA",
  "status": "Submitted",
  "pickupStart": "2026-08-03T12:30:00-05:00",
  "total": { "amount": 44000.00, "currency": "COP" },
  "items": [
    {
      "productName": "Almuerzo de la casa",
      "unitPrice": { "amount": 22000.00, "currency": "COP" },
      "quantity": 2,
      "lineTotal": { "amount": 44000.00, "currency": "COP" }
    }
  ]
}
```

`CATALOG_CHANGED` devuelve estado actualizado del carrito sin crear pedido.

### `GET /api/v1/public/pickup-orders/{code}`

Devuelve estado, instantáneas, total, franja, ajuste pendiente, acciones permitidas y teléfono enmascarado.

### `POST /api/v1/public/pickup-orders/{code}/accept-adjustment`

Solo `AdjustmentRequested`; responde estado `Accepted`.

### `POST /api/v1/public/pickup-orders/{code}/cancel`

Solo estados permitidos; motivo opcional.

## 9. API privada — negocio

Prefijo: `/api/v1/businesses/{businessId}`.

### Perfil y equipo

| Método y ruta | Permiso | Resultado |
|---|---|---|
| `GET /profile` | miembro | perfil privado |
| `PUT /profile` | `BusinessProfile.Manage` | actualiza campos permitidos |
| `GET /memberships` | `Workers.Manage` | equipo |
| `POST /memberships` | `Workers.Manage` | asigna usuario existente/temporal |
| `PUT /memberships/{id}/permissions` | `Workers.Manage` | reemplaza permisos |
| `DELETE /memberships/{id}` | `Workers.Manage` | desactiva |
| `GET /staff` | miembro | perfiles operativos |
| `POST /staff` | `Catalog.Manage` | crea perfil |

### Catálogo y disponibilidad

| Método y ruta | Permiso |
|---|---|
| `GET/POST /services` | `Catalog.Manage` para escritura |
| `PUT /services/{id}` | `Catalog.Manage` |
| `GET/POST /availability-rules` | `Catalog.Manage` |
| `GET/POST /availability-exceptions` | `Catalog.Manage` |
| `GET/POST /product-categories` | `Catalog.Manage` |
| `GET/POST /products` | `Catalog.Manage` |
| `PUT /products/{id}` | `Catalog.Manage` |
| `PUT /modules/{module}` | propietario |

Todos los `PUT` incluyen `version`; conflicto responde `409 CONCURRENCY_CONFLICT`.

### Citas

- `GET /appointments?date=&status=&page=`;
- `GET /appointments/{id}`;
- `POST /appointments/{id}/confirm`;
- `POST /appointments/{id}/reject`;
- `POST /appointments/{id}/complete`;
- `POST /appointments/{id}/no-show`;
- `POST /appointments/{id}/cancel`.

Body de transición:

```json
{ "version": 3, "reason": "Opcional según acción" }
```

### Turnos

- `GET /queue`;
- `POST /queue/open`;
- `POST /queue/close`;
- `POST /queue/call-next`;
- `POST /queue/tickets/{id}/complete`;
- `POST /queue/tickets/{id}/skip`;
- `POST /queue/tickets/{id}/restore`;
- `POST /queue/tickets/{id}/cancel`.

`call-next` responde ticket llamado y nueva `stateVersion`.

### Pedidos

- `GET /pickup-orders?status=&date=&page=`;
- `GET /pickup-orders/{id}`;
- `POST /pickup-orders/{id}/accept`;
- `POST /pickup-orders/{id}/reject`;
- `POST /pickup-orders/{id}/request-adjustment`;
- `POST /pickup-orders/{id}/start-preparing`;
- `POST /pickup-orders/{id}/mark-ready`;
- `POST /pickup-orders/{id}/deliver`;
- `POST /pickup-orders/{id}/cancel`.

Solicitud de ajuste:

```json
{
  "version": 1,
  "message": "Podemos tenerlo listo a la 1:00 p. m.",
  "proposedPickupStart": "2026-08-03T13:00:00-05:00"
}
```

No existe endpoint de pago.

## 10. API de plataforma

Prefijo `/api/v1/admin`.

- CRUD de `/municipalities`;
- CRUD de `/categories`;
- `GET/POST /businesses`;
- `PUT /businesses/{id}/status`;
- `PUT /businesses/{id}/publication`;
- `POST /businesses/{id}/owners`;
- `GET /owners`;
- `GET /audit`.

Requiere `PlatformAdmin`; todas las acciones escriben auditoría.

## 11. Eventos SignalR

Hub: `/hubs/queue`.

### Suscripción

El cliente invoca:

```text
SubscribeToPublicQueue(businessSlug)
SubscribeToTicket(trackingCode)
```

El servidor valida negocio publicado/módulo o código antes de agregar al grupo.

### `QueueStateChanged`

```json
{
  "businessSlug": "barberia-el-corte",
  "isOpen": true,
  "currentNumber": 19,
  "waitingCount": 3,
  "stateVersion": 23
}
```

### `TicketStateChanged`

```json
{
  "number": 23,
  "status": "Waiting",
  "peopleAhead": 3,
  "stateVersion": 23
}
```

No contiene código, alias, teléfono ni IDs internos.

## 12. Validaciones transversales

- texto recortado y normalizado;
- límites de longitud en cliente y servidor;
- lista blanca de estados y permisos;
- teléfonos normalizados antes de cifrar;
- no aceptar HTML;
- observaciones se muestran codificadas;
- máximo de ítems, cantidades y tamaño de body;
- rate limiting en búsqueda, creación y seguimiento;
- antiforgery en comandos autenticados y de cookie;
- no confiar en campos deshabilitados/ocultos;
- rechazar propiedades JSON no declaradas;
- errores genéricos para códigos inexistentes;
- correlación por `traceId`.

## 13. Códigos de error estables

`VALIDATION_FAILED`, `AUTHENTICATION_REQUIRED`, `FORBIDDEN`, `NOT_FOUND`, `MODULE_DISABLED`, `BUSINESS_SUSPENDED`, `SLOT_UNAVAILABLE`, `INVALID_STATE_TRANSITION`, `QUEUE_CLOSED`, `QUEUE_CAPACITY_REACHED`, `CATALOG_CHANGED`, `PICKUP_SLOT_UNAVAILABLE`, `CONCURRENCY_CONFLICT`, `RATE_LIMITED`, `UNEXPECTED_ERROR`.

El cliente móvil futuro debe depender de estos códigos, no de textos en español.
