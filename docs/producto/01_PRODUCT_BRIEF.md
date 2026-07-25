# UrabaConecta — Product brief

## 1. Propósito

UrabaConecta es una plataforma web multiestablecimiento que permite a negocios pequeños publicar una presencia digital operativa sin reemplazar su contabilidad, POS, inventario o ERP.

La demo debe probar si un establecimiento puede recibir y gestionar una operación concreta desde el celular y si un cliente puede completarla sin crear una cuenta:

- reservar una cita;
- tomar un turno virtual;
- solicitar un pedido para recoger y pagar en el local.

**Estado de la oportunidad:** hipótesis de producto. La investigación previa del repositorio no demuestra demanda local para esta plataforma generalista. La demo es un instrumento de aprendizaje y no justifica por sí sola construir un producto comercial completo.

## 2. Problema

### Hipótesis principal

Algunos establecimientos pequeños dependen de llamadas, mensajes y presencia física para informar su oferta y coordinar citas, turnos o pedidos. Esto puede producir interrupciones, espera, solicitudes incompletas y poca visibilidad del estado.

### Lo que no se afirma

- No se afirma que todos los negocios de Urabá tengan este problema.
- No se afirma que los métodos actuales sean deficientes.
- No se afirma que exista disposición a pagar.
- No se afirma que un mismo establecimiento necesite los tres módulos.
- No se afirma que UrabaConecta sustituya WhatsApp, POS, inventarios o sistemas administrativos.

## 3. Usuarios y compradores

| Actor | Necesidad | Cuenta |
|---|---|---|
| Visitante | Encontrar negocios y consultar oferta | No |
| Cliente invitado | Crear y seguir una cita, turno o pedido | No |
| Trabajador | Atender operaciones de uno o más negocios asignados | Sí |
| Propietario | Configurar el negocio, oferta, equipo y operaciones | Sí |
| Administrador de plataforma | Habilitar negocios, municipios, categorías y propietarios | Sí |

**Comprador hipotético:** propietario o administrador del establecimiento. Debe validarse por separado del trabajador que usa el panel y del cliente final.

## 4. Propuesta de valor

### Para el público

“Encuentre un negocio, haga su solicitud en pocos pasos y consulte el estado con un código, sin crear una cuenta.”

### Para el negocio

“Publique su oferta y gestione citas, turnos o pedidos desde un panel sencillo, sin cambiar sus sistemas administrativos.”

## 5. Alcance geográfico

El modelo admite municipios configurables. La demo habilita:

- Apartadó;
- Carepa;
- Chigorodó;
- Turbo.

No se implementan mapas, geolocalización, cobertura de domicilios ni reglas tributarias por municipio.

## 6. Módulos

### 6.1 Directorio público

- búsqueda por texto;
- filtro por municipio y categoría;
- ficha pública del establecimiento;
- visualización de módulos disponibles;
- catálogo público de servicios o productos.

### 6.2 Agendamiento

- disponibilidad por servicio y trabajador;
- solicitud de cita;
- confirmación o rechazo por el negocio;
- consulta por código;
- finalización como completada, ausente o cancelada.

### 6.3 Turnos virtuales

- apertura y cierre de cola;
- turno anónimo;
- número y código de seguimiento;
- posición aproximada;
- llamada del siguiente;
- finalización, omisión o cancelación;
- actualización inmediata de la pantalla pública.

### 6.4 Pedidos para recoger

- menú y carrito;
- franja de recogida;
- solicitud sin pago;
- precio congelado al enviar;
- aceptación, rechazo o solicitud de ajuste;
- preparación, listo y entregado;
- consulta por código.

### 6.5 Administración

- gestión de perfil, horarios, servicios, productos y trabajadores;
- operación diaria por módulo;
- administración de negocios, propietarios, categorías y municipios.

## 7. Datos ficticios obligatorios de la demo

| Negocio | Municipio | Categoría | Módulo |
|---|---|---|---|
| Salón Bella Urabá | Apartadó | Belleza | Citas |
| Barbería El Corte | Chigorodó | Barbería | Turnos |
| Restaurante Sazón Local | Carepa | Restaurante | Pedidos para recoger |

Los nombres, direcciones, teléfonos, trabajadores, servicios, productos y operaciones de demostración serán claramente ficticios.

## 8. Métricas del piloto

### Experiencia del cliente

- tasa de finalización por flujo;
- mediana de tiempo para enviar una solicitud;
- porcentaje de errores de validación por formulario;
- porcentaje de consultas de seguimiento exitosas;
- abandono por paso.

### Operación del negocio

- tiempo desde solicitud hasta primera acción;
- porcentaje de operaciones atendidas;
- número de cambios de estado inválidos bloqueados;
- duplicidades de cita prevenidas;
- tiempo para llamar al siguiente turno;
- diferencias entre total enviado y total histórico del pedido: debe ser cero.

### Calidad técnica

- tasa de solicitudes con error 5xx;
- percentil 95 de lectura pública y de comandos;
- pérdida de eventos SignalR: la reconexión debe recuperar estado por HTTP;
- incidentes de aislamiento entre negocios: debe ser cero;
- exposición de datos personales en logs: debe ser cero.

### Señal comercial

- negocios que completan al menos cinco operaciones reales de prueba;
- propietarios que aceptan continuar un piloto;
- método actual y costo operativo documentados;
- disposición a pagar medida mediante compromiso, no opinión.

No se fija una meta comercial definitiva antes de entrevistas y piloto.

## 9. Supuestos

1. Los clientes pueden usar un navegador móvil moderno.
2. El negocio dispone al menos de un teléfono con conectividad intermitente o estable.
3. El negocio acepta operar desde un panel web.
4. Una cuenta puede pertenecer a varios establecimientos.
5. Cada establecimiento habilita solo los módulos que necesita.
6. La zona horaria inicial es `America/Bogota` y se almacena por negocio.
7. La moneda inicial es COP.
8. Las citas requieren alias o nombre y teléfono; los turnos pueden ser anónimos.
9. Los pedidos se pagan únicamente en el establecimiento.
10. La demo usa datos ficticios hasta completar revisión jurídica y de privacidad.

## 10. Riesgos principales

| Riesgo | Consecuencia | Tratamiento en la demo |
|---|---|---|
| No existe dolor o disposición a pagar | Producto sin mercado | Medir método actual, frecuencia, costo y compromiso antes de ampliar |
| Producto horizontal demasiado amplio | Complejidad y propuesta difusa | Módulos independientes y primera vertical centrada en citas |
| Fuga de datos entre negocios | Daño grave y bloqueo del piloto | `BusinessId`, autorización por recurso, filtros y pruebas negativas |
| Códigos públicos enumerables | Exposición de operaciones y teléfonos | Código aleatorio de 128 bits, hash almacenado, rate limiting y respuesta genérica |
| Doble reserva o acciones concurrentes | Mala experiencia y conflictos | Transacciones, restricción de solapamiento y control de concurrencia |
| Panel difícil en celulares | Baja adopción | Mobile-first, botones grandes, formularios breves y pruebas E2E móviles |
| Recolección excesiva de datos | Riesgo legal y operativo | Minimización, retención corta, supresión y ausencia de datos sensibles |
| Competencia con herramientas existentes | Baja diferenciación | No prometer reemplazo; validar el flujo puntual y la facilidad de uso |

## 11. Preguntas que el piloto debe responder

1. ¿Qué flujo genera un problema repetido y cuantificable?
2. ¿Quién usa el panel y quién decide pagar?
3. ¿Qué método y software usa hoy cada negocio?
4. ¿La operación digital reduce tiempo o interrupciones?
5. ¿Los clientes completan el flujo sin ayuda?
6. ¿El negocio mantiene actualizados horarios, oferta y estados?
7. ¿Qué módulo merece continuar y cuáles deben archivarse?

