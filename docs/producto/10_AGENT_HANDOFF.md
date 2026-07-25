# UrabaConecta — Relevo al siguiente agente

## 1. Objetivo del siguiente agente

Construir la primera vertical completa de UrabaConecta: un visitante encuentra **Salón Bella Urabá**, solicita una cita, el negocio la confirma o rechaza y el cliente consulta el estado con un código público no predecible.

No entregar infraestructura aislada como resultado final de la iteración.

## 2. Archivos que debe leer, en este orden

1. `docs/producto/10_AGENT_HANDOFF.md`
2. `docs/producto/02_MVP_SCOPE.md`
3. `docs/producto/03_USER_FLOWS.md`
4. `docs/producto/04_ARCHITECTURE.md`
5. `docs/producto/05_DOMAIN_MODEL.md`
6. `docs/producto/06_DATA_MODEL.md`
7. `docs/producto/07_API_AND_COMPONENTS.md`
8. `docs/producto/08_BACKLOG.md`
9. `docs/producto/09_TEST_STRATEGY.md`
10. `docs/producto/01_PRODUCT_BRIEF.md`

Si encuentra una contradicción, debe detener esa decisión concreta, documentarla y escoger la interpretación más restrictiva que preserve privacidad, aislamiento y alcance. No debe ampliar producto para resolverla.

## 3. Decisiones obligatorias

1. C# y .NET 10.
2. ASP.NET Core + Blazor Web App con Interactive Auto.
3. EF Core + PostgreSQL + ASP.NET Core Identity.
4. Monolito modular, vertical slices, una aplicación y una base.
5. Proyectos y dependencias de `04_ARCHITECTURE.md`.
6. `BusinessId` obligatorio en toda entidad de negocio.
7. Autorización por recurso en cada solicitud; el selector visual no autoriza.
8. Cita, turno y pedido son agregados distintos.
9. Código público aleatorio de 128 bits; solo HMAC almacenado.
10. Teléfono cifrado y ausente de logs.
11. SignalR solo para turnos, no para citas.
12. Precios del pedido se congelan en instantáneas.
13. Sin pagos, WhatsApp automático, microservicios ni demás exclusiones.

## 4. Qué puede modificar

- código y configuración de la nueva solución;
- migraciones;
- datos ficticios;
- pruebas;
- documentación técnica generada por el código, si mantiene coherencia;
- esta documentación únicamente para corregir una contradicción real o registrar un ADR.

Toda modificación de una decisión obligatoria requiere un ADR en `docs/producto/decisions/` con contexto, alternativas, impacto y aprobación humana.

## 5. Qué no puede reinterpretar

- no convertir la demo en marketplace transaccional;
- no añadir cuentas para clientes invitados;
- no pedir documento de identidad;
- no añadir pagos o estado de pago;
- no usar una entidad genérica para los tres flujos;
- no confiar solo en filtros UI o globales para aislamiento;
- no almacenar código público en claro;
- no sustituir PostgreSQL por EF InMemory en integración;
- no crear un servicio por módulo;
- no implementar turnos o pedidos antes de cerrar la primera cita E2E;
- no personalizar lógica para un negocio específico; los datos sí son ficticios y específicos.

## 6. Primera tarea exacta

Implementar las historias `V1-01` a `V1-05` de `08_BACKLOG.md` como una única vertical entregable.

### Resultado funcional

1. Inicio público muestra Salón Bella Urabá en Apartadó.
2. Ficha pública muestra servicios.
3. Cliente elige servicio, fecha y franja.
4. Envía alias, teléfono y consentimiento.
5. Recibe código de seguimiento.
6. Propietario inicia sesión.
7. Ve la cita en su negocio y confirma.
8. Cliente consulta el código y ve `Confirmada`.
9. Propietario marca `Completada`.
10. Cliente ve estado terminal.

### Seguridad mínima de la misma vertical

- crear un segundo negocio ficticio privado para pruebas;
- probar que propietario A no lee ni modifica citas de B;
- probar que `businessId` inesperado en el body responde 400 y no cambia el alcance;
- código aleatorio/HMAC;
- teléfono cifrado;
- no PII en logs;
- concurrencia de misma franja.

## 7. Orden técnico dentro de la primera tarea

1. Crear solución y proyectos.
2. Configurar PostgreSQL, Identity y migración mínima.
3. Implementar `Business`, membresía y autorización.
4. Implementar directorio mínimo del salón.
5. Implementar servicio, trabajador y disponibilidad.
6. Implementar creación y seguimiento de cita.
7. Implementar panel y transiciones.
8. Agregar restricción PostgreSQL de solapamiento.
9. Completar integración y E2E.
10. Cargar datos ficticios idempotentes.

Cada paso debe compilar; la entrega no termina hasta el paso 10.

## 8. Estructura esperada

```text
UrabaConecta.sln
src/
  UrabaConecta.Contracts/
  UrabaConecta.Domain/
  UrabaConecta.Application/
  UrabaConecta.Infrastructure/
  UrabaConecta.Web/
  UrabaConecta.Web.Client/
tests/
  UrabaConecta.Domain.Tests/
  UrabaConecta.IntegrationTests/
  UrabaConecta.E2ETests/
docs/producto/
```

## 9. Comandos esperados

Los nombres exactos de migración pueden variar; los demás comandos deben mantenerse.

```powershell
dotnet --version
dotnet restore
dotnet build --configuration Release --no-restore

dotnet ef database update `
  --project src/UrabaConecta.Infrastructure `
  --startup-project src/UrabaConecta.Web

dotnet test tests/UrabaConecta.Domain.Tests `
  --configuration Release --no-build

dotnet test tests/UrabaConecta.IntegrationTests `
  --configuration Release --no-build

dotnet test tests/UrabaConecta.E2ETests `
  --configuration Release --no-build

dotnet run --project src/UrabaConecta.Web
```

El agente debe documentar requisitos locales de PostgreSQL y Playwright. No debe crear recursos cloud.

## 10. Resultado verificable esperado

- build Release verde;
- migración desde base vacía verde;
- datos demo idempotentes;
- cita E2E verde en 360 × 800;
- prueba concurrente: una sola cita activa por franja/trabajador;
- matriz de aislamiento verde;
- código no almacenado en claro;
- teléfono no legible en base ni logs;
- ninguna capacidad excluida;
- instrucciones locales de ejecución.

## 11. Datos ficticios mínimos

### Salón Bella Urabá

- municipio: Apartadó;
- módulo: citas;
- tres servicios con duraciones distintas;
- dos trabajadores;
- horario semanal;
- una excepción;
- propietario y trabajador de demo.

### Negocio B de seguridad

- no publicado;
- propietario distinto;
- un servicio y una cita;
- existe exclusivamente para probar aislamiento.

No usar teléfonos, correos o direcciones de personas/negocios reales.

## 12. Bloqueos reales antes de comenzar

No hay bloqueo técnico para iniciar la primera vertical si el entorno dispone de:

- SDK .NET 10;
- PostgreSQL;
- runtime compatible con Playwright.

Antes de un piloto con personas reales sí bloquean:

1. revisión jurídica del aviso, finalidades, retención, supresión y responsabilidades;
2. definición del responsable real del tratamiento;
3. validación con negocios sobre proceso y disposición a pagar;
4. selección de infraestructura con backups, cifrado y custodia de claves.

Estos bloqueos no impiden construir la demo con datos ficticios.

## 13. Criterio para rechazar trabajo

Rechazar una entrega si:

- solo hay autenticación/base sin flujo completo;
- aislamiento se prueba únicamente con UI;
- usa SQLite/InMemory para validar concurrencia PostgreSQL;
- crea cita sin revalidar franja;
- almacena código o teléfono en claro;
- requiere más de una aplicación desplegable;
- incluye una exclusión del MVP;
- no tiene E2E del flujo completo.
