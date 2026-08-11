# 01 — Arquitectura

## Capas

```
UrabaConecta.Domain          Entidades y reglas. Sin dependencias de infraestructura.
UrabaConecta.Application     Casos de uso, puertos (interfaces), opciones.
UrabaConecta.Contracts       DTO compartidos entre servidor y cliente WebAssembly.
UrabaConecta.Infrastructure  EF Core/PostgreSQL, Identity, almacenamiento S3, seguridad.
UrabaConecta.Web             Blazor Web App (servidor + WebAssembly), API mínima, SignalR.
```

## Ejecución

Un solo contenedor con la aplicación web, más PostgreSQL administrado y un volumen para las
llaves de Data Protection. Sin colas, sin caché externa, sin microservicios.

- **Render**: Blazor con render interactivo de servidor y WebAssembly. Las páginas públicas se
  sirven prerenderizadas; la consola privada usa circuito de servidor.
- **Tiempo real**: SignalR (`/hubs/queue`) para las filas virtuales.
- **API**: `/api/v1/public` (anónima), `/api/v1/admin` (plataforma y socias),
  `/api/v1/businesses` (miembros del negocio).
- **Imágenes**: se normalizan al subirlas y se guardan en un bucket compatible con S3
  (Cloudflare R2). Se sirven desde el dominio público del bucket, no desde la aplicación.

## Secuencia de arranque

El orden importa y es deliberado:

```
1. StartupGuard.ThrowIfInvalid(...)     → configuración inapta = no arranca
2. app.Build()
3. MigrateDatabaseAsync(...)            → esquema al día, en todo ambiente
4. SeedDevelopmentAsync(...)            → sólo Development y Demo
5. BootstrapDemoAdminAsync(...)         → sólo Demo, con interruptor
6. NormalizeDemoAccessAsync(...)        → sólo Demo, con secreto
7. BootstrapProductionAdminAsync(...)   → sólo Production, con interruptor, una vez
8. app.RunAsync()
```

El paso 1 ocurre antes de construir el host: una configuración peligrosa no llega ni a abrir una
conexión. Los pasos 4 a 7 se excluyen mutuamente por ambiente.

## Persistencia de datos personales

El alias, el teléfono y las notas del cliente se guardan cifrados mediante
`IPersonalDataProtector`, que se apoya en el anillo de llaves de Data Protection. **Perder el
volumen `/app/keys` vuelve ilegibles esos datos.** Ver `06_DATA_PROTECTION.md`.

Los códigos públicos de seguimiento no se guardan en claro: se guarda su HMAC, derivado de
`URABACONECTA_TRACKING_HMAC_KEY`. Perder esa clave invalida todos los códigos entregados a
clientes.

## Aislamiento por negocio

Toda entidad de negocio implementa `IBusinessOwned`. Los casos de uso reciben el `BusinessId`
y el identificador del usuario, y comprueban la membresía activa antes de resolver. La consola de
plataforma recibe un `PlatformActor` construido a partir de la petición autenticada, nunca de la
carga útil del cliente.

## Regiones

La aplicación y PostgreSQL están hoy en regiones distintas (US West y US East), lo que añade
alrededor de 73 ms por consulta. No se movió nada como parte de este endurecimiento: mover una
base productiva es una operación con ventana de indisponibilidad y debe decidirse aparte. Ver
`15_KNOWN_RISKS.md`.
