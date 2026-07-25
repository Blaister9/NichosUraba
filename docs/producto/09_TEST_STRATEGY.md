# UrabaConecta — Estrategia de pruebas

## 1. Objetivo

Demostrar que los tres flujos funcionan, que las invariantes sobreviven a concurrencia y que ningún usuario o negocio accede a datos ajenos.

No se usa EF InMemory para afirmar comportamiento de persistencia. Las pruebas de integración ejecutan PostgreSQL real efímero.

## 2. Pirámide

| Nivel | Herramienta sugerida | Qué prueba |
|---|---|---|
| Dominio | xUnit + FluentAssertions opcional | estados, cálculos, invariantes |
| Aplicación | xUnit + dobles de puertos | decisiones de slices sin I/O |
| Integración | `WebApplicationFactory` + PostgreSQL/Testcontainers | API, EF, Identity, transacciones, restricciones |
| Componentes | bUnit opcional | estados de componentes complejos |
| E2E | Microsoft Playwright | navegador, responsive y flujos completos |

No añadir una biblioteca si la plataforma estándar cubre el caso.

## 3. Pruebas unitarias

### Negocios

- solo negocio activo puede publicarse;
- módulo desconocido se rechaza;
- último propietario no puede desactivarse;
- permiso de propietario es implícito.

### Citas

- duración válida;
- transición de cada estado;
- terminal no reabre;
- cancelación permitida;
- cálculo de franjas con horario y excepción;
- conversión `America/Bogota` ↔ UTC;
- trabajador elegible;
- instantáneas de servicio no cambian;
- alias/teléfono/consentimiento requeridos.

### Turnos

- apertura/cierre;
- numeración creciente;
- siguiente por número más bajo en espera;
- estados válidos;
- restaurar omitido máximo una vez;
- cálculo de personas por delante;
- cambio de día local.

### Pedidos

- cantidades y máximo de líneas;
- cálculo de línea y total;
- servidor ignora precio cliente;
- instantáneas;
- transiciones;
- ajuste requiere contenido;
- cancelación según estado;
- no existe concepto de pago.

### Privacidad

- generación de 128 bits;
- codificación Base64URL;
- HMAC determinista para consulta y distinto por clave;
- enmascarado de teléfono;
- supresión elimina campos.

## 4. Pruebas de integración

### Infraestructura

- migración desde base vacía;
- migración desde versión anterior;
- `btree_gist` y exclusión de citas;
- FKs compuestas impiden asociación cruzada;
- índices únicos de slug, membresía, código y turno;
- cifrado/descifrado con claves de prueba;
- semilla idempotente.

### API

Por endpoint:

- caso feliz;
- validación;
- recurso inexistente;
- módulo deshabilitado;
- negocio suspendido;
- concurrencia/versión;
- autorización;
- respuesta `ProblemDetails` y código estable.

### Seguimiento público

- código válido;
- código aleatorio inválido;
- código de otro flujo;
- código invalidado;
- respuesta genérica equivalente;
- límite de intentos;
- código nunca aparece en log.

## 5. Matriz obligatoria de autorización

Actores:

- anónimo;
- trabajador A con permiso;
- trabajador A sin permiso;
- propietario A;
- trabajador B;
- propietario B;
- administrador de plataforma;
- usuario autenticado sin membresía.

Recursos:

- perfil;
- membresía;
- servicio/producto;
- cita;
- turno;
- pedido;
- catálogo global;
- auditoría.

Cada combinación se expresa como prueba parametrizada. Resultado esperado:

- anónimo en privado: 401;
- miembro sin permiso: 403;
- miembro de otro negocio: 404;
- propietario del mismo negocio: permitido según acción;
- `PlatformAdmin`: solo por endpoint `/admin`, no por asumir membresía.

## 6. Pruebas obligatorias de aislamiento

Crear Business A y Business B con usuarios y datos equivalentes.

### Lectura

1. Usuario A lista citas: no aparece ninguna de B.
2. Usuario A solicita ID de cita B: 404.
3. Usuario A cambia `businessId` de la ruta: 404.
4. Consulta pública del slug A no devuelve servicios/productos B.

### Escritura

1. Usuario A intenta actualizar servicio B: 404.
2. Usuario A intenta confirmar cita B: 404.
3. Usuario A intenta añadir producto B a pedido A: validación/FK rechaza.
4. Usuario A envía `businessId=B` en body: responde 400 por propiedad no declarada y no crea datos.
5. Usuario A intenta asociar Staff A con Service B: rechazo.

### Eliminación y configuración

1. Usuario A no desactiva membresía B.
2. Usuario A no habilita módulo B.
3. Administrador de plataforma sí actúa mediante slice `/admin` y genera auditoría.

### Verificación de base

- una consulta sin alcance en un repositorio de negocio falla en prueba de arquitectura o revisión;
- `SaveChanges` rechaza entidad con `BusinessId` distinto del scope;
- FKs compuestas impiden relaciones cruzadas aun con SQL/EF incorrecto.

Cualquier falla de aislamiento bloquea la entrega.

## 7. Pruebas E2E

### E2E-01 — Cita

1. Visitante filtra Apartadó.
2. Abre Salón Bella Urabá.
3. Elige servicio, fecha y hora.
4. Envía alias, teléfono y consentimiento.
5. Guarda código.
6. Propietario inicia sesión y confirma.
7. Cliente consulta y ve confirmada.
8. Negocio marca completada.
9. Cliente ve completada.

### E2E-02 — Conflicto de cita

Dos contextos de navegador seleccionan la misma franja y envían simultáneamente. Uno recibe `201`; otro ve mensaje de horario ocupado. Solo existe una cita activa.

### E2E-03 — Turno

1. Visitante abre barbería.
2. Toma turno anónimo.
3. Trabajador llama siguiente.
4. Pantalla pública se actualiza por SignalR.
5. Trabajador completa.
6. Cliente ve completado.

### E2E-04 — Reconexión de turno

Desconectar SignalR, cambiar estado, reconectar y verificar recuperación HTTP.

### E2E-05 — Pedido

1. Visitante arma carrito.
2. Selecciona franja y envía datos/consentimiento.
3. Restaurante solicita ajuste.
4. Cliente acepta.
5. Restaurante acepta, prepara, marca listo y entrega.
6. Total permanece igual.

### E2E-06 — Aislamiento visual

Usuario A cambia manualmente la URL a Business B y no ve contenido.

### E2E-07 — Estados vacíos

Turbo sin negocios, negocio sin catálogo, cola sin turnos y panel sin operaciones.

### Viewports

- móvil: 360 × 800;
- móvil medio: 390 × 844;
- escritorio: 1366 × 768.

Motor mínimo: Chromium. Antes de piloto, añadir WebKit si el público objetivo usa iPhone.

## 8. Concurrencia

### Citas

- 10 solicitudes paralelas misma franja: exactamente una activa;
- franjas adyacentes `[inicio, fin)` no se consideran solapadas;
- cancelar libera franja;
- dos confirmaciones con misma versión: una gana, otra 409.

### Turnos

- 20 solicitudes paralelas: números únicos y consecutivos;
- dos `call-next`: tickets distintos o uno vacío, nunca mismo ticket;
- cierre concurrente con solicitud: resultado consistente y transaccional.

### Pedidos

- dos pedidos al último cupo de franja: solo capacidad permitida;
- edición de precio concurrente: `CATALOG_CHANGED` o instantánea nueva, nunca precio enviado por cliente;
- dos transiciones simultáneas: una gana, otra 409.

## 9. Casos de error

- PostgreSQL temporalmente no disponible;
- clave de cifrado ausente: aplicación no inicia;
- versión de consentimiento inactiva;
- zona horaria inválida en configuración;
- fecha durante cambio de año;
- entrada con HTML/script;
- texto por encima de límites;
- body demasiado grande;
- código manipulado;
- sesión expirada;
- usuario desactivado;
- negocio suspendido durante formulario;
- módulo deshabilitado entre lectura y envío;
- doble clic;
- pérdida de SignalR;
- refresh en paso final;
- reenvío del mismo comando.

Para reenvíos públicos, no se implementa idempotency key general en MVP; la UI bloquea doble envío y las restricciones evitan corrupción. Si el piloto demuestra duplicados por red, se añade clave idempotente como slice.

## 10. Datos de prueba

- IDs deterministas solo en semillas de prueba;
- teléfonos ficticios reservados y visiblemente no reales;
- no copiar datos de negocios reales;
- dos negocios por módulo para aislamiento, aunque la UI demo muestre tres principales;
- reloj falso para retención y cambio de día;
- generador criptográfico real en integración, determinista solo en unidad;
- base limpia por colección o transacción según estabilidad.

## 11. Seguridad y privacidad

- análisis de dependencias;
- secretos no versionados;
- cookies seguras en configuración de producción;
- antiforgery;
- rate limiting;
- XSS por observaciones;
- acceso directo a objetos;
- enumeración de códigos;
- revisión automática de logs para patrones de teléfono/código;
- cabeceras CSP, frame-ancestors, content-type y referrer policy;
- OpenAPI no expuesto públicamente en producción.

No se declara cumplimiento legal por pasar pruebas técnicas.

## 12. Rendimiento

Con datos de demo ampliados:

- 1.000 negocios;
- 100.000 operaciones distribuidas;
- 500 servicios/productos por negocio como límite de estrés, no caso normal.

Objetivos:

- lectura pública p95 < 500 ms en servidor bajo carga de prueba acordada;
- comando p95 < 800 ms excluyendo red;
- consulta siempre paginada;
- cero consultas N+1 detectadas en rutas principales;
- payload de directorio limitado.

## 13. Criterios de aprobación

La entrega se aprueba si:

1. `dotnet build --configuration Release` pasa.
2. Todas las pruebas unitarias e integración pasan.
3. E2E-01, 02, 03, 05, 06 y 07 pasan; E2E-04 antes de cerrar turnos.
4. Cero fallas de aislamiento.
5. Cero doble reserva, número duplicado o total mutable.
6. Cero PII en logs inspeccionados.
7. Migración desde cero y semilla idempotente pasan.
8. No existen capacidades excluidas.
9. No hay errores de accesibilidad de severidad crítica en caminos principales.
10. El responsable de producto demuestra los tres flujos en móvil.

Una prueba inestable se considera fallida; no se reintenta automáticamente para ocultarla.

## 14. Comandos esperados

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test tests/UrabaConecta.Domain.Tests --configuration Release --no-build
dotnet test tests/UrabaConecta.IntegrationTests --configuration Release --no-build
dotnet test tests/UrabaConecta.E2ETests --configuration Release --no-build
```

El agente de implementación puede agregar un script orquestador, pero estos comandos deben seguir funcionando.
