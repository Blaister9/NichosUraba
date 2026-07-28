# 01 — Arquitectura de producción

## Capas

| Proyecto | Responsabilidad |
| --- | --- |
| `UrabaConecta.Domain` | Reglas de negocio puras. Sin dependencias de infraestructura |
| `UrabaConecta.Contracts` | DTOs y la interfaz `IUrabaConectaApi` que comparten servidor y cliente |
| `UrabaConecta.Application` | Casos de uso, autorización funcional y abstracciones (`IObjectStorage`, `IImageProcessor`, `IAccessInvitationStore`, …) |
| `UrabaConecta.Infrastructure` | EF Core sobre PostgreSQL, Identity, almacenamiento de objetos, procesamiento de imágenes, validaciones de arranque |
| `UrabaConecta.Web` | Blazor Web App (servidor + WebAssembly), endpoints mínimos, SignalR |

La dirección de las dependencias es siempre hacia adentro: `Web → Infrastructure → Application → Domain`.

## Autorización en dos niveles

1. **Ruta.** Las políticas de `Program.cs` filtran por rol: `PlatformAdmin`,
   `PlatformOperator` (administrador o socia), `BusinessOwner`, `Workers.Manage`, etc.
2. **Caso de uso.** Cada operación vuelve a comprobar el permiso real contra la base de datos:
   la membresía activa del usuario en ese `BusinessId`, o —para la consola— si el negocio fue
   dado de alta por esa socia (`Business.CreatedByUserId`).

Ocultar un botón nunca es la protección: las pruebas de integración comprueban 401 y 403
directamente contra la API.

## El actor administrativo

`PlatformActor(UserId, IsPlatformAdmin, IsPartnerOperator, IpAddress, CorrelationId)` se construye
en el borde HTTP a partir de los *claims* de la petición. Los casos de uso nunca deducen el rol
por su cuenta ni lo aceptan como parámetro del cliente.

## Almacenamiento de imágenes

```
Blazor (InputFile)
  → POST multipart /api/v1/admin/businesses/{id}/images
    → BusinessImageUseCases (límites, alcance, auditoría)
      → IImageProcessor  (firma binaria, reescalado, EXIF)
        → IObjectStorage (Local en Development y Demo, S3/R2 en Production)
          → business_images (sólo la referencia y los metadatos)
```

El binario **nunca** entra a PostgreSQL, ni al filesystem efímero del contenedor, ni a `wwwroot`,
ni al volumen de llaves de Data Protection.

## Estados del negocio

```
Draft ──► PendingConfiguration ──► PendingReview ──► Active
              ▲                        │              │
              └────── rechazo ─────────┘              ├──► Suspended ──► Active
              ▲                                       │
              └────── cambio de configuración ────────┘
                                                      └──► Archived (solo lectura)
```

- `Draft`, `PendingConfiguration` y `PendingReview` no aparecen en el directorio público.
- Un cambio de configuración sobre un negocio publicado o en revisión lo devuelve a
  `PendingConfiguration` y lo despublica.
- `Archived` es solo lectura administrativa. No hay eliminación física desde la interfaz salvo
  para borradores sin ninguna operación registrada.

## Datos personales

- Alias, teléfono y notas se cifran con Data Protection antes de guardarse.
- Los códigos públicos de seguimiento se guardan sólo como HMAC-SHA256.
- Los tokens de invitación se guardan sólo como HMAC-SHA256.
- La auditoría almacena instantáneas de estado y resúmenes, nunca secretos.

## Persistencia

| Dato | Dónde vive |
| --- | --- |
| Negocios, citas, turnos, pedidos, auditoría | PostgreSQL |
| Llaves de Data Protection | Volumen `/app/keys` |
| Imágenes | Volumen `/app/media` (Local) o bucket R2 (Production) |
| Secretos | Variables de entorno del proveedor. Nunca en Git |
