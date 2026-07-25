# UrabaConecta — Backlog por vertical slices

## 1. Reglas de ejecución

- Prioridades: `P0` obligatoria para demo; `P1` necesaria antes de piloto; `P2` posterior.
- Cada historia deja la solución compilando y ejecutable.
- Las historias V1-01 a V1-05 forman **una sola primera vertical de producto: agendamiento completo**. No se acepta entregar infraestructura durante varias iteraciones sin cerrar V1-05.
- No iniciar turnos o pedidos hasta que las pruebas de aislamiento y la cita E2E estén verdes.
- Cada cambio de modelo incluye migración y prueba en PostgreSQL real.

## 2. Mapa de entregas

| Etapa | Resultado demostrable |
|---|---|
| V1 | Un cliente agenda en Salón Bella Urabá, el negocio confirma y el cliente consulta |
| V2 | Aislamiento reforzado y administración reutilizable de negocios/catálogos |
| V3 | Cola virtual completa y tiempo real |
| V4 | Pedido para recoger completo con precios históricos |
| V5 | Administración de plataforma, privacidad, datos demo y despliegue |

## V1-01 — Solución mínima ejecutable y PostgreSQL

**Prioridad:** P0  
**Objetivo:** crear el esqueleto mínimo necesario para la primera cita, no una plataforma genérica.

**Usuario:** equipo de desarrollo.

**Comportamiento:**

- solución con los proyectos definidos en arquitectura;
- Blazor Web App .NET 10 con Interactive Auto;
- conexión PostgreSQL;
- `DbContext`, migración inicial y health check;
- manejo global de `ProblemDetails`;
- página de inicio simple.

**Criterios de aceptación:**

1. `dotnet build` finaliza sin errores.
2. La aplicación inicia con configuración local documentada.
3. La migración crea la base desde cero.
4. `/health/live` responde sin base y `/health/ready` verifica base.
5. No existen proyectos o servicios adicionales.

**Dependencias:** ninguna.

**Pruebas:**

- prueba de arranque;
- migración desde base vacía;
- health checks.

## V1-02 — Identidad y alcance de negocio mínimo

**Prioridad:** P0  
**Objetivo:** autenticar al personal de Salón Bella Urabá y establecer aislamiento antes de manejar citas.

**Usuario:** propietario y trabajador.

**Comportamiento:**

- Identity con `Guid`;
- `Business`, `BusinessMembership`, permisos y `BusinessScope`;
- inicio/cierre de sesión;
- selector de negocio asignado;
- políticas `BusinessMember`, `BusinessOwner`, `Appointments.Manage`;
- semilla ficticia del salón y dos usuarios de demo.

**Criterios de aceptación:**

1. Usuario no autenticado no entra al panel.
2. Miembro activo entra solo al negocio asignado.
3. Cambiar `businessId` en URL a un negocio ajeno no revela datos.
4. Trabajador sin permiso recibe 403 dentro de su negocio.
5. No hay registro público de propietarios.

**Dependencias:** V1-01.

**Pruebas:**

- integración con cookies;
- lectura y comando cruzados entre dos negocios ficticios;
- rol y permiso.

## V1-03 — Directorio y ficha pública mínima

**Prioridad:** P0  
**Objetivo:** permitir que el visitante encuentre el salón y llegue al agendamiento.

**Usuario:** visitante.

**Comportamiento:**

- municipios/categorías;
- búsqueda y filtros;
- ficha pública del salón;
- solo negocios activos/publicados;
- estado vacío para Turbo.

**Criterios de aceptación:**

1. Buscar “Bella” encuentra el salón.
2. Filtrar Apartadó conserva el resultado; Turbo muestra vacío.
3. Negocio no publicado o suspendido responde 404.
4. La ficha muestra solo datos públicos.
5. Funciona en 360 × 800 sin desplazamiento horizontal.

**Dependencias:** V1-01 y entidad Business de V1-02.

**Pruebas:**

- consultas públicas;
- visibilidad por estado;
- E2E móvil desde inicio hasta ficha.

## V1-04 — Servicios, trabajadores y disponibilidad del salón

**Prioridad:** P0  
**Objetivo:** configurar datos suficientes para calcular franjas reales.

**Usuario:** propietario.

**Comportamiento:**

- CRUD de servicios;
- perfiles de trabajadores;
- relación trabajador-servicio;
- reglas semanales y excepciones;
- consulta pública de servicios y franjas;
- asignación determinista de trabajador elegible.

**Criterios de aceptación:**

1. Servicio activo aparece; inactivo no.
2. Una excepción cerrada elimina franjas.
3. Una cita activa elimina solapamientos.
4. Entidades relacionadas siempre comparten `BusinessId`.
5. El propietario de otro negocio no puede asociar sus IDs.

**Dependencias:** V1-02, V1-03.

**Pruebas:**

- unitarias de cálculo de franjas, zona horaria y duración;
- integración de FKs compuestas;
- autorización CRUD.

## V1-05 — Cita completa de extremo a extremo

**Prioridad:** P0  
**Objetivo:** cerrar la primera vertical comercializable/demostrable.

**Usuario:** cliente invitado y negocio.

**Comportamiento:**

- formulario público;
- consentimiento versionado;
- teléfono cifrado;
- código aleatorio con HMAC almacenado;
- cita `Pending`;
- panel para confirmar/rechazar;
- seguimiento;
- completar/ausente/cancelar;
- restricción de solapamiento.

**Criterios de aceptación:**

1. Se cumplen los once pasos del flujo de cita.
2. El código tiene 128 bits de aleatoriedad y no se almacena en texto plano.
3. Dos solicitudes concurrentes al mismo espacio producen una cita y un `409 SLOT_UNAVAILABLE`.
4. Una transición inválida produce `409 INVALID_STATE_TRANSITION`.
5. Otro negocio no puede leer ni modificar la cita.
6. El log no contiene alias, teléfono ni código.
7. E2E móvil recorre visitante → negocio → seguimiento → completada.

**Dependencias:** V1-01 a V1-04.

**Pruebas:**

- invariantes de estado;
- integración con PostgreSQL y concurrencia;
- rate limit de seguimiento;
- E2E completo.

**Hito:** solo al terminar esta historia se considera entregada la primera vertical.

## V2-01 — Administración reutilizable de perfil y módulos

**Prioridad:** P0  
**Objetivo:** generalizar lo mínimo usado por citas para los tres negocios.

**Usuario:** propietario.

**Comportamiento:**

- edición de perfil y horario;
- habilitación de módulos permitidos;
- publicación/despublicación;
- navegación del panel según capacidades.

**Criterios de aceptación:**

1. Solo propietario gestiona módulos y publicación.
2. Módulo deshabilitado desaparece y bloquea endpoint.
3. Negocio suspendido no recibe nuevas operaciones.
4. Toda actualización usa versión de concurrencia.

**Dependencias:** V1.

**Pruebas:** autorización, concurrencia, visibilidad pública.

## V2-02 — Equipo y permisos

**Prioridad:** P0  
**Objetivo:** permitir operación por trabajadores sin dar privilegios de propietario.

**Usuario:** propietario y trabajador.

**Comportamiento:**

- alta/desactivación de membresía;
- permisos por módulo;
- vínculo opcional con `StaffMember`;
- prevención de eliminar último propietario.

**Criterios de aceptación:**

1. Trabajador solo ve módulos autorizados.
2. Desactivación corta acceso en la siguiente solicitud.
3. No se puede asociar usuario o staff de otro negocio.
4. Último propietario permanece.

**Dependencias:** V1-02, V2-01.

**Pruebas:** matriz de roles/permisos y aislamiento.

## V3-01 — Cola virtual completa

**Prioridad:** P0  
**Objetivo:** ejecutar el flujo de Barbería El Corte.

**Usuario:** cliente anónimo y trabajador.

**Comportamiento:**

- configuración;
- abrir/cerrar cola;
- tomar turno sin PII;
- secuencia por día local;
- llamar siguiente;
- completar, omitir, restaurar o cancelar;
- seguimiento público.

**Criterios de aceptación:**

1. Se cumplen los ocho pasos del flujo.
2. Números son únicos y crecientes por día.
3. Dos llamadas concurrentes no seleccionan el mismo turno.
4. Cola cerrada no emite turno.
5. Restaurar omitido solo una vez.
6. Pantalla pública no muestra personas ni códigos.

**Dependencias:** V2.

**Pruebas:** estados, concurrencia, medianoche local, aislamiento y E2E.

## V3-02 — Actualización inmediata de cola

**Prioridad:** P0  
**Objetivo:** actualizar pantalla pública sin hacer SignalR fuente de verdad.

**Usuario:** cliente en espera.

**Comportamiento:**

- `QueueHub`;
- grupos públicos y por código;
- eventos mínimos;
- reconexión y recuperación HTTP;
- estado de conexión visible.

**Criterios de aceptación:**

1. Llamar siguiente actualiza dos navegadores.
2. Un cliente reconectado obtiene estado actual aunque perdió eventos.
3. Evento no contiene PII ni código.
4. Sin SignalR, el botón de actualizar mantiene el flujo.

**Dependencias:** V3-01.

**Pruebas:** integración de hub y E2E multi-contexto.

## V4-01 — Menú y franjas de recogida

**Prioridad:** P0  
**Objetivo:** publicar el menú de Sazón Local y franjas con capacidad.

**Usuario:** propietario y visitante.

**Comportamiento:**

- categorías y productos;
- disponibilidad manual;
- precios;
- configuración de franjas;
- menú público con versión;
- carrito local en navegador.

**Criterios de aceptación:**

1. Solo productos activos/disponibles aparecen.
2. Precio no puede ser negativo.
3. Carrito limita líneas y cantidades.
4. Franjas respetan horario, anticipación y capacidad.
5. Otro negocio no modifica catálogo.

**Dependencias:** V2.

**Pruebas:** catálogo, franja, autorización y UI móvil.

## V4-02 — Pedido completo para recoger

**Prioridad:** P0  
**Objetivo:** ejecutar el flujo de solicitud sin pago.

**Usuario:** cliente invitado y restaurante.

**Comportamiento:**

- envío con consentimiento;
- cálculo de servidor;
- instantáneas de producto/precio;
- código de seguimiento;
- aceptar, rechazar, ajustar, preparar, listo y entregar;
- aceptar ajuste o cancelar por cliente.

**Criterios de aceptación:**

1. Se cumplen los catorce pasos obligatorios.
2. El body no acepta precio autoritativo.
3. Cambiar catálogo no altera pedido previo.
4. Catálogo cambiado antes de enviar devuelve `CATALOG_CHANGED`.
5. No existen tablas, campos ni endpoints de pago.
6. Estados inválidos se bloquean.
7. Aislamiento y E2E completos.

**Dependencias:** V4-01 y privacidad de V1-05.

**Pruebas:** pricing, snapshots, capacidad concurrente, estados, E2E.

## V5-01 — Administración de plataforma

**Prioridad:** P0  
**Objetivo:** administrar datos globales sin reutilizar el panel de negocio.

**Usuario:** administrador de plataforma.

**Comportamiento:**

- municipios/categorías;
- alta y estado de negocio;
- asignación de propietario;
- auditoría;
- acceso transversal explícito.

**Criterios de aceptación:**

1. Solo `PlatformAdmin` accede.
2. Acciones quedan auditadas.
3. Slugs y catálogos respetan unicidad.
4. Acceso transversal no usa handlers de negocio con filtro desactivado.

**Dependencias:** V2.

**Pruebas:** rol, auditoría y rutas separadas.

## V5-02 — Retención, supresión y endurecimiento

**Prioridad:** P1 antes de datos reales  
**Objetivo:** minimizar exposición de datos personales.

**Usuario:** responsable de plataforma.

**Comportamiento:**

- proceso invocable de supresión;
- invalidación de códigos;
- limpieza de turnos;
- logs sin PII;
- rate limiting;
- cabeceras de seguridad;
- textos provisionales identificados.

**Criterios de aceptación:**

1. Supresión borra alias, teléfono y notas según política.
2. Código invalidado deja de resolver.
3. Métricas anonimizadas no reidentifican.
4. Búsqueda repetida de códigos produce 429.
5. Escaneo automatizado no encuentra PII en logs de prueba.

**Dependencias:** V1, V3, V4.

**Pruebas:** retención con reloj falso, seguridad y regresión.

## V5-03 — Datos ficticios completos

**Prioridad:** P0  
**Objetivo:** entregar una demo repetible con los tres negocios.

**Usuario:** demostrador.

**Comportamiento:**

- semilla idempotente;
- usuarios y contraseñas de demo solo en entorno Development;
- servicios, disponibilidad, cola, productos y operaciones de muestra;
- Turbo vacío.

**Criterios de aceptación:**

1. Ejecutar semilla dos veces no duplica.
2. Producción no carga credenciales demo.
3. Todos los datos se identifican como ficticios.
4. Cada módulo abre con estados útiles.

**Dependencias:** V1, V3, V4, V5-01.

**Pruebas:** idempotencia y smoke E2E.

## V5-04 — Preparación de despliegue y aprobación

**Prioridad:** P0  
**Objetivo:** empaquetar una sola aplicación reproducible.

**Usuario:** equipo de desarrollo/operación.

**Comportamiento:**

- configuración validada;
- migración controlada;
- build release;
- contenedor opcional;
- health checks;
- documentación de backup/restore;
- suite completa.

**Criterios de aceptación:**

1. Un comando construye la solución.
2. Un comando ejecuta pruebas.
3. Una base nueva migra y carga demo.
4. Una restauración de respaldo se prueba.
5. La aplicación solo requiere proceso web, PostgreSQL y almacenamiento seguro de claves.
6. Todos los criterios de `09_TEST_STRATEGY.md` pasan.

**Dependencias:** todas las P0.

**Pruebas:** pipeline limpio y smoke en configuración Release.

## 3. Posterior al MVP — no implementar

- proveedores de WhatsApp/notificación;
- pagos;
- entregas;
- suscripciones;
- aplicación móvil;
- importación masiva;
- analítica avanzada;
- expiración automática compleja de citas;
- selección explícita de trabajador por cliente;
- imágenes cargadas por usuarios.

