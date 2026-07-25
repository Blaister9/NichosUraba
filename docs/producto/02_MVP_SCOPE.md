# UrabaConecta — Alcance del MVP

## 1. Objetivo de la demo

Entregar una sola aplicación web desplegable que demuestre, de extremo a extremo, los tres flujos obligatorios con datos ficticios y aislamiento probado entre establecimientos.

La demo es técnicamente completa, pero comercialmente exploratoria. No se autoriza ampliar alcance por solicitudes particulares de un negocio.

## 2. Incluido

### Plataforma común

- Blazor Web App en .NET 10 con renderizado Interactive Auto.
- Registro e inicio de sesión solo para propietarios, trabajadores y administradores.
- ASP.NET Core Identity.
- PostgreSQL y Entity Framework Core.
- directorio público con búsqueda y filtros;
- ficha pública por slug;
- panel mobile-first;
- módulos habilitables por negocio;
- seguimiento público con código opaco;
- consentimiento versionado cuando se solicitan nombre o teléfono;
- auditoría mínima de acciones administrativas;
- zonas horarias por negocio;
- manejo uniforme de errores;
- datos ficticios idempotentes de demo;
- pruebas unitarias, integración, autorización, aislamiento y E2E.

### Agendamiento

- servicios, duración y precio informativo opcional;
- perfiles de trabajadores y servicios que pueden prestar;
- horario semanal y excepciones;
- consulta de fechas y franjas disponibles;
- asignación automática de un trabajador elegible;
- solicitud de cita;
- confirmación o rechazo;
- consulta de estado;
- cancelación permitida según estado;
- completada o ausente;
- prevención de solapamiento.

### Turnos

- configuración y apertura/cierre de cola;
- solicitud anónima;
- numeración secuencial por negocio y día local;
- código de seguimiento;
- turno actual y cantidad de personas por delante;
- llamar siguiente;
- completar, omitir o cancelar;
- tablero público actualizado con SignalR;
- recuperación por HTTP al reconectar.

### Pedidos para recoger

- categorías simples y productos;
- disponibilidad manual de producto;
- carrito y cantidades;
- observaciones breves;
- franjas de recogida;
- nombre o alias y teléfono;
- consentimiento;
- solicitud sin pago;
- instantánea de nombre, precio y cantidad;
- total inmutable después del envío;
- aceptar, rechazar, solicitar ajuste, preparar, marcar listo y entregar;
- aceptación o rechazo del ajuste por el cliente;
- seguimiento público.

### Administración del negocio

- perfil, dirección textual, municipio, categoría, teléfono público y horario;
- publicación o despublicación;
- trabajadores y permisos;
- servicios, productos y disponibilidad;
- operación por módulo;
- acceso únicamente a negocios asignados.

### Administración de plataforma

- CRUD controlado de municipios y categorías;
- alta, suspensión y publicación de negocios;
- asignación del primer propietario;
- consulta de usuarios propietarios;
- acceso transversal explícito y auditado.

## 3. Excluido

- pagos, pasarelas, transferencias o conciliación;
- facturación electrónica, contabilidad, nómina o impuestos;
- inventario avanzado, reservas de inventario o recetas;
- domicilios, repartidores, cobertura o GPS;
- chat o mensajería bidireccional;
- reseñas, calificaciones, fidelización o cupones;
- promociones complejas;
- inteligencia artificial;
- integración automática con WhatsApp, redes sociales o terceros;
- aplicación móvil nativa;
- notificaciones push, SMS o correo transaccional;
- planes de suscripción, cobro recurrente o límites comerciales;
- documentos de identidad, historia clínica o datos sensibles;
- mapas y geocodificación;
- importaciones masivas;
- analítica avanzada;
- personalización funcional exclusiva para un negocio;
- microservicios, bus de eventos externo, colas distribuidas, Redis, Elasticsearch o Kubernetes.

Un botón manual para copiar un enlace o texto no constituye integración con WhatsApp y puede incluirse solo si no llama una API externa.

## 4. Alcance exacto de la demo

### Salón Bella Urabá

- ficha pública en Apartadó;
- módulo de citas habilitado;
- al menos tres servicios ficticios;
- al menos dos trabajadores ficticios;
- disponibilidad semanal y una excepción;
- una cita pendiente, una confirmada y una completada como datos de muestra.

### Barbería El Corte

- ficha pública en Chigorodó;
- módulo de turnos habilitado;
- cola configurable;
- pantalla pública de estado;
- al menos tres turnos ficticios en distintos estados.

### Restaurante Sazón Local

- ficha pública en Carepa;
- módulo de pedidos habilitado;
- al menos dos categorías y seis productos ficticios;
- franjas de recogida;
- al menos dos pedidos ficticios en distintos estados.

### Turbo

- municipio disponible como filtro;
- puede aparecer sin negocios publicados;
- debe mostrar un estado vacío útil, no datos inventados.

## 5. Definición de terminado

Una historia está terminada únicamente si:

1. compila con advertencias tratadas según la política del repositorio;
2. tiene migración revisada cuando cambia persistencia;
3. valida entrada en servidor;
4. aplica autorización y filtro de `BusinessId`;
5. incluye manejo de vacío, carga, éxito y error;
6. funciona en viewport móvil de 360 × 800;
7. tiene pruebas unitarias para invariantes;
8. tiene pruebas de integración para persistencia y autorización;
9. tiene E2E para el camino crítico cuando modifica un flujo;
10. no registra datos personales en logs;
11. actualiza los contratos y documentación afectados;
12. conserva ejecutables las verticales ya entregadas.

La demo completa está terminada cuando:

- los tres flujos obligatorios funcionan de punta a punta;
- cada negocio solo ve y modifica sus datos;
- las pruebas de acceso cruzado responden 404 o 403 según el caso definido;
- una cita concurrente no produce doble reserva;
- la numeración de turnos no se duplica;
- el total histórico de un pedido no cambia al editar el catálogo;
- los códigos públicos no son secuenciales;
- los datos ficticios se cargan de forma idempotente;
- existe configuración reproducible para desarrollo y despliegue;
- todas las pruebas pasan.

## 6. Reglas para impedir expansión de alcance

1. Toda solicitud nueva debe vincularse con un criterio de aceptación existente.
2. Si requiere un proveedor externo, queda fuera del MVP.
3. Si reemplaza contabilidad, POS, inventario o ERP, queda fuera.
4. Si solo beneficia a un establecimiento y no es configurable, queda fuera.
5. Si introduce una nueva clase de datos personales, requiere revisión de privacidad y no entra automáticamente.
6. Si exige otro proceso desplegable o base de datos, se rechaza.
7. Si no mejora uno de los tres flujos, el directorio o el aislamiento, pasa a “posterior”.
8. El backlog no puede añadir trabajo “por si acaso”.
9. Cambiar una decisión obligatoria requiere un ADR, impacto en todos los documentos y aprobación del responsable de producto.
10. Ninguna integración futura se simula como si funcionara; se muestra como “no disponible” o se omite.

## 7. Compatibilidad y rendimiento objetivo

- navegadores móviles actuales con soporte para WebAssembly y JavaScript;
- degradación a interacción de servidor durante la primera carga de Interactive Auto;
- contenido público útil con renderizado inicial del servidor;
- objetivo de carga inicial pública menor a 2,5 s en red móvil razonable, medido en el entorno de prueba;
- acciones con respuesta visible inmediata;
- listas paginadas; no cargar catálogos u operaciones completos;
- imágenes de negocio opcionales, comprimidas y con límites definidos.

Estos objetivos son criterios de prueba, no garantías de producción.

