# J-MOTION-06 · Product → Action · Pickup Orders

## Auditoría antes de implementar (2026-09-02, Bogotá)

Inherited HEAD: `d7aa2f83ffe5bdc52fe5e6db9145629174091106`. Fetch y worktree list
comprobados. Worktree limpio nuevo: `C:/Users/santi/Documents/NichosUraba-j-motion-06`,
rama `codex/j-motion-06`. El checkout histórico no se utiliza ni se modifica.

Business comprobado por UI y GET público en DEV: **Lúmina Coral Beauty Store · DEMO**,
slug `lumina-coral-beauty-demo`. Products + PickupOrders; cuatro productos disponibles:
Labial Coral Satín ($29.900), Paleta Tropical 6 tonos ($49.900), Sérum facial vitamina C
($39.900), Mascarilla nutritiva capilar ($34.900). Los cuatro tienen WebP en `/media/`.
No se crean productos. Alternativa sin media: Restaurante Sazón Local, ya sembrado.

Journey real: `BusinessProfileView` muestra hasta cuatro productos. «Agregar» enlaza
a `/negocios/{slug}/pedidos#producto-{id}` sin sumar unidades. `PickupOrdering` muestra
la carta por categorías, controles 0–20 y carrito en memoria del circuito Blazor.
«Continuar» salta al resumen/formulario en la misma página. Se elige franja, alias,
celular, nota opcional y consentimiento. POST crea el pedido; se muestra número y
total, enlace con tracking code, Mi actividad y PushPrompt. No existe product-details,
checkout externo ni pago online. No se inventan etapas de dominio.

API: GET `/api/v1/public/businesses/{slug}/menu`, GET `.../pickup-slots`, POST `.../orders`;
GET `/api/v1/public/orders/{code}` permite verificar lo creado. Contratos:
`ProductDto`, `PickupMenuDto`, `CreatePickupOrderRequest`, `PickupOrderCreatedDto`,
`PickupOrderTrackingDto` en ApiContracts.cs. `OrderingUseCases.CreateAsync` valida
disponibilidad, consentimiento y franja; el servidor calcula precio/total y persiste
snapshots de líneas. Estado inicial `Pending`; sus estados posteriores quedan fuera
del trabajo. Se conservan recuperación local del token, publicación de notificaciones,
autorización y capabilities. Suites existentes: OrderingJourney, OrderingTwoProductJourney,
OrderingHardeningJourney, OrderingApi, OrderingTests y PickupSlotAvailabilityTests.

## Decisión espacial

SOURCE OBJECT: tarjeta real de producto, su media, nombre, precio y stepper.
TARGET OBJECT: la zona de acción **dentro de esa misma tarjeta**, después el resumen
real y su confirmación. SHARED PROPERTIES: identidad/ProductId, nombre, unidades,
importe, contorno de la superficie. La fotografía permanece en el mismo nodo y lugar.

Dock inline contextual, con espacio reservado. En desktop el producto y los controles
comparten una tarjeta de ancho limitado; en móvil los controles ocupan su segundo
renglón, debajo de la identidad. No hay dock fijo nuevo ni colisión con bottom nav o
teclado. Se conserva el acceso Continuar al formulario existente. Selección activa
es estado UI distinto de «en tu pedido»: cambiar A→B no elimina A del carrito.

No hay vuelo espacial entre dos rectángulos coincidentes: por ello no se etiqueta
como shared bounds. Se evaluaron FLIP/View Transition/manual shared geometry; no
aportan al dock que vive en el propio producto. WAAPI local transforma media/acción
sin clon, carga de imagen, bloqueo de eventos ni espera antes de aplicar estado.
Resumen y confirmación preservan las mismas líneas y superficie en el DOM.

## Fichas state-first (escritas antes del código)

| Transición | SOURCE STATE | TARGET STATE | TRIGGER | DRIVEN BY | PROPERTIES | INTERRUPTIBLE | FALLBACK | REDUCED MOTION | WHY MOTION HELPS / Compose |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A · Resting → selected | producto sin foco contextual | ProductId activo, texto seleccionado y aria-pressed | elegir identidad, CTA o +/-; fragmento válido desde perfil | estado de PickupOrdering | media scale .97→1, acción translateY 5→0 / opacity, borde estático | sí, cancela anterior; B manda | estados y controles visibles | inmediato | une intención con el mismo objeto; coordinated transition, 180 ms |
| B · Selected → action | seleccionado, cantidad 0 | cantidad >0, En tu pedido, Continuar | agregar unidad | dictionary quantities y activeProduct | slot de CTA/estado en contenedor persistente | sí | texto final instantáneo | inmediato | confirma incorporación sin nuevo panel; AnimatedContent-like / coordinated, 160 ms |
| C · Quantity | n unidades y total actual | clamp(n±1,0,20), total actualizado | +/- | cantidades reales | dígito e importe translateY ±4→0 / opacity, anchuras estables | sí, cancela y reemplaza; sin ruleta ni cola | texto nuevo ya renderizado | inmediato, sin anuncio por frame | relaciona cantidad e importe; single value / AnimatedContent-like, 120 ms |
| D · Action → order | producto(s) en carrito | resumen/formulario existente enfocado | Continuar | carrito real + intención de navegación | énfasis breve en la línea del producto / superficie, scroll inmediato | sí; no se bloquea entrada | fragmento existente y foco lógico | identidad estática, scroll inmediato | reconoce qué se está pidiendo; coordinated transition, 180 ms, sin shared bounds ficticio |
| E · Submit → confirmation | resumen + formulario válido, busy | pedido creado por servidor | respuesta CreatePickupOrderAsync | PickupOrderCreatedDto, líneas verificadas por API | mismas líneas/superficie, relevo de titular/CTA opacity y translateY 6→0 | no retrasa creación; doble envío guardado | número, total, código/enlaces y confirmación estáticos | inmediato | cierra acción manteniendo identidad; AnimatedContent-like / coordinated, 200 ms |

Cancel/back: deseleccionar conserva unidades; quitar última unidad vuelve a cantidad
cero sin borrar controles. Volver a productos desde resumen devuelve foco al producto
activo. Escape cierra selección sólo desde la tarjeta, nunca captura el formulario.

Easing: `cubic-bezier(.2,.8,.2,1)`. Estado primero; ninguna animación gobierna datos.
No Blazor por frame, rAF, intervalos ni observer global. Preferencias reduce y Save-Data
desactivan decoración; se reutiliza la media de catálogo. No referencias MotionSites:
la geometría real resuelve la decisión y no requiere investigación externa.

## Gates previstos

Journey UI/API real y verificación persistida de líneas/precios/código; selección
A→B→C y cantidades rápidas; cancelar/volver; contraste, nombres accesibles y foco;
reduce, Save-Data, reposo (0 animaciones/mutaciones/rAF) y CLS por interacción.
Capturas 1440×1000, 1920×1080, 390×844, 360×800. Smoke limitado Home/J-MOTION-04C,
shared scene/J-MOTION-03A, Queue/J-MOTION-05, Search y My Activity.
Un commit/push a `origin/dev/motion-home` sólo después de PASS. Deploy exclusivamente
`skillful-sparkle / dev / UrabaConecta-dev`. Nunca PROD ni J-MOTION-07.

## Refinamiento y evidencia

El dock ocupa la misma tarjeta y la misma media. `product-quantity` evita activar
el pulso genérico heredado de `.cantidad`; site-motion.js queda intacto. El módulo
colocado junto a Razor se importa con `Assets` para respetar el fingerprint real.
Las acciones aplican datos antes de decorar; el último producto cancela las
animaciones anteriores. No hay mutation observer, rAF, intervalos ni imágenes nuevas.
Continue y volver conservan además la navegación nativa por fragmento si falla el
módulo de motion.

La primera versión retiraba el catálogo al confirmar y produjo CLS 0,49: rechazada.
La final lo conserva como contexto `inert`, mantiene el encabezado, lista y líneas
del resumen en los mismos nodos, reserva una vez la altura existente al enviar y
enfoca la confirmación con el encabezado visible bajo la navegación superior.
Sólo se transforma/opaca contenido; la altura reservada no se anima. Una lectura
del pedido persistido confirma precios de líneas antes de mostrarlos. Si esa lectura
falla después de crear, se mantienen nombres/cantidades y total autoritativo, sin
inventar precios de líneas ni habilitar un segundo envío.

Validación final por viewport (mayor valor de cualquier fase):

| Canvas | CLS | Suma de shifts, incluso los asociados a input | Resultado |
| --- | --- | --- | --- |
| 1440×1000 | 0 | 0,000048 | PASS |
| 1920×1080 | 0,000022 | 0,000025 | PASS |
| 390×844 | 0 | 0,000052 | PASS |
| 360×800 | 0 | 0,000424 | PASS |
| 390×844 reduce | 0 | 0,000352 | PASS |
| 360×800 Save-Data | 0 | 0,000246 | PASS |

Cada recorrido crea mediante UI un pedido real local de dos Labiales Coral Satín:
$59.800, cantidad 2, producto y precio verificados por GET del código persistido,
estado Pending, recuperación desde Mi actividad. Se conserva el mismo nodo de
media/CTA y la misma línea de resumen al confirmar. Rapid A→B→C deja C activo,
un solo seleccionado, precio/CTA correctos; cantidades 2→7→2 no dejan valor viejo.
Escape/cerrar y volver al producto conservan cantidades y foco. Tras asentarse,
ventanas de 1,8 s verifican cero mutaciones, cero llamadas rAF y cero animaciones
activas. Reduce y Save-Data hacen cero llamadas WAAPI.

La accesibilidad automática verifica nombres, aria-pressed, selección textual,
foco, contraste AA en la superficie del pedido, ausencia de live spam numérico y
estado final anunciado. El test adicional usa el catálogo sin media de Sazón Local,
tema oscuro, cantidad 1→0, cierre y módulo de motion bloqueado. Se corrigió el fondo
del total en oscuro sólo con CSS de PickupOrdering. El teclado se comprueba con
inputmode=tel y viewport móvil reducido a 480 px; el teclado nativo y lector de
pantalla físico quedan para revisión HUMAN, no se presentan como automatizados.

Evidencia local ignorada por Git: `artifacts/j-motion-06/` (capturas de reposo,
selección, composer, confirmación, teclado y fallback; métricas),
`artifacts/test-results/` (TRX), y `artifacts/build-final.log`.
Build final: cero errores, cero warnings. Domain: 28/28; Ordering API: 10/10;
ProductActionJourneyTests final: 7/7 (seis journeys con media y fallback oscuro).
Smoke aprobado: cuatro Home/shared scene/handoff, tres OrderingJourney (incluyen
notificaciones y My Activity), cuatro OrderingHardening, dos OrderingTwoProduct y
Queue Rapid_updates. La ejecución conjunta reveló contaminación de precio de
OrderingHardening sobre OrderingTwoProduct: este último pasa 2/2 en base aislada.
La prueba ampliada de cola de cuatro tamaños se detuvo en el fixture de alias
presencial (se crea «Sin alias» antes de iniciar el recorrido); no se modificó cola.
Su smoke con mutaciones reales por API y actualización de UI pasa en base aislada.
No se declara toda la suite del repositorio verde.

## Dato de DEV detectado fuera del cambio

El showcase publicado en DEV no tiene teléfono, WhatsApp ni correo público. La copia
local reprodujo que crear un pedido actualiza PickupOrderSettings y el guard de
readiness existente lo pasa a PendingConfiguration, despublicándolo por falta de
contacto. La suite local utiliza únicamente un correo reservado `.test` en su copia
para completar el fixture; no añade productos ni modifica reglas de readiness.
Se solicitó autorización separada para completar el contacto de prueba de Lúmina
en DEV antes de escribir pedidos allí. Hasta resolverlo, la verificación DEV debe
ser de selección/motion/health/assets sin provocar la despublicación del negocio.
La entrega final registra si se resolvió esa precondición y el código DEV, si existe.

Home/J-MOTION-01/J-MOTION-03A/J-MOTION-04C/J-MOTION-05: archivos intactos. No se
modifican endpoints, contratos, dominio, Owner, PlatformAdmin, seguimiento ni
notificaciones. Única implementación de producto: PickupOrdering.razor + CSS/JS
colocados. No MotionSites: cero referencias; la geometría del producto bastó.

## Exact HUMAN test steps

1. Abrir DEV y comprobar la marca del entorno. Visitar
   `https://dev.urabaconecta.com/negocios/lumina-coral-beauty-demo`.
2. En Labial Coral Satín, pulsar «Agregar»: debe abrir la carta enfocando y marcando
   ese mismo producto, sin sumar una unidad todavía.
3. Cerrar selección, elegir la foto y agregar. La media y el contenedor de CTA
   permanecen; aparece cantidad 1, $29.900 y «Continuar». Pulsar +: 2 y $59.800.
4. Elegir rápidamente Labial → Paleta → Sérum. Sólo Sérum queda seleccionado;
   no cambian las unidades que ya se habían agregado. Volver al Labial y alternar
   +/− rápido: el último valor y su importe deben quedar asentados sin pulsación continua.
5. Cerrar o pulsar Escape desde el producto. Se conserva el carrito y el foco vuelve
   a la imagen. Pulsar Continuar: reconocer nombre, cantidad e importe en Tu pedido.
   «Volver a productos» devuelve contexto y foco sin vaciar lo elegido.
6. Probar 390×844 y 360×800: foto/dock visibles, navegación inferior libre, formulario
   desplazable con teclado y botón Confirmar accesible. Repetir 1440×1000 y 1920×1080:
   acción a la derecha, tarjeta limitada a 880 px, resumen a 680 px.
7. Antes del envío en DEV, comprobar que está resuelta la precondición de contacto
   descrita arriba. Elegir franja, alias de prueba, celular de prueba y nota «Prueba
   J-MOTION-06, sin entrega real»; aceptar el aviso y confirmar una sola vez.
8. Ver «Recibimos tu pedido», mismas líneas/cantidades, total y código, con enlaces
   «Seguir mi pedido» y «Mi actividad». Abrir Mi actividad para recuperar ese pedido.
   No evaluar ni modificar motion del seguimiento en esta tarea.
9. Repetir selección/cantidad/confirmación con reduced motion. Revisar también tema
   oscuro y lectura con lector real: el estado tiene texto, foco y ARIA sin depender
   del movimiento. Esperar cinco segundos sin interacción: todo debe quedar quieto.

La revisión perceptual humana queda pendiente; los checks automáticos no equivalen
a HUMAN_PASS. La entrega final en `artifacts/j-motion-06/DELIVERY.md` registra commit,
GitHub, deployment ID, estado DEV y cualquier precondición aún pendiente.
