# J-MOTION-05 · Virtual Queues

Continuación del worktree `C:/Users/santi/Documents/NichosUraba-j-motion-05`, heredado en
`de565ed16a05abf9d1c33229865d021b10aca57f` (HEAD separado). Se conservó la implementación
heredada y se reprodujo su fallo antes de modificar producto. Destino autorizado:
`origin/dev/motion-home`, Railway `skillful-sparkle / dev / UrabaConecta-dev`.

## Fallo original y causa

`Live_state_delta_narrates_the_real_queue_transitions`, línea heredada 179:
tras llamar, iniciar atención y completar el primer turno anterior, esperaba `2`
en `[data-live-value][data-live-delta]`; observó `3` durante 20 segundos.

`Rapid_updates_never_leave_a_stale_value_on_screen`, línea heredada 305:
tras cancelar tres tickets por la API, esperaba `0` esperando y observó `3`.
La instrumentación confirmó los tres tickets `Cancelled`, la API HTTP devolviendo
`waitingCount: 0` y el circuito mostrando `3` / `75 min`.

**Bug preexistente de producto: YES.** `QueueStore` usa consultas con tracking de EF.
La API HTTP obtiene un scope por petición, pero `ServerUrabaConectaApi` conservaba
el mismo caso de uso, store y DbContext durante el circuito de Blazor. Una consulta
posterior podía devolver las entidades ya rastreadas con su estado anterior.
Serializar consultas evitaba concurrencia del contexto, pero no renovaba sus datos.

La corrección está en el límite de la API del circuito: cada operación de cola usa
un scope nuevo, conservando dentro de él la operación completa, sus versiones,
transacción, permisos y notificaciones. No se cambiaron `QueueStore`, reglas de
dominio, columnas, migraciones ni contratos.

Había además dos problemas de invalidación: el cliente sólo escuchaba cambios de su
propio ticket, aunque completar otro modifica su posición; la conexión SignalR del
panel salía desde el servidor sin la cookie del navegador y su suscripción privada
fallaba. Seguimiento y panel usan ahora la invalidación pública `QueueChanged` ya
existente. No transporta datos privados. El panel sigue leyendo y escribiendo a
través de la API autorizada del circuito. `SubscribeOperations` mantiene su control
de acceso. `SubscribeTicket` sigue validando el código antes de añadir los grupos.

## Estados reales

| Dato | Regla existente |
| --- | --- |
| GENERAL QUEUE | `Closed`, `Open`, `Paused` en `QueueSession` |
| PERSONAL TICKET | `Waiting`, `Called`, `InService`, `Completed`, `Skipped`, `Cancelled` |
| POSITION | `PeopleAhead`: tickets de la misma jornada con número menor y estado `Waiting`, `Called` o `InService`; cero si el ticket propio no está `Waiting` |
| CALLED/current | Menor número en `Called` o `InService`; sólo se llama otro al resolver el actual |
| NEXT | Derivado: ticket `Waiting` y `PeopleAhead == 0`; no existe columna/enum Next |
| SERVED | `Completed`, etiqueta pública `Atendido` |
| Waiting count | Sólo tickets `Waiting`, no incluye a quien ya está llamado o en atención |
| Orden | `Number` ascendente, asignado con bloqueo de la jornada |
| Operador | `CallNext`, `start`, `complete`; también existen `recall`, `skip`, `restore`, `cancel` |

El E2E nominal agrega tres turnos por mostrador y uno por el formulario público.
Mantiene contextos de navegador separados para operador y consumidor. El caso
comprobado recorre lo siguiente (A1/A2/A3 delante, B consumidor):

| Store BEFORE | Operación | Store AFTER | API de B / tablero | Razor de B |
| --- | --- | --- | --- | --- |
| A1,A2,A3 `Waiting` | B toma turno | B `Waiting` | ahead=3, waiting=4 | espera, «Faltan 3 turnos» |
| A1 `Waiting` | llamar | A1 `Called` | ahead=3, waiting=3, current=A1 | espera |
| A1 `Called` | iniciar | A1 `InService` | ahead=3, waiting=3 | espera |
| A1 `InService` | completar | A1 `Completed` | ahead=2, waiting=3, current=null | espera, «Faltan 2 turnos» |
| A2 `Waiting` | llamar/iniciar | A2 `InService` | ahead=2, waiting=2 | espera |
| A2 `InService` | completar | A2 `Completed` | ahead=1, waiting=2 | espera, «Falta 1 turno» |
| A3 `Waiting` | llamar/iniciar | A3 `InService` | ahead=1, waiting=1 | espera |
| A3 `InService` | completar | A3 `Completed` | ahead=0, waiting=1 | siguiente |
| B `Waiting` | llamar | B `Called` | status=Called, canCancel=false, waiting=0 | llamado, «¡Es tu turno!» |
| B `Called` | iniciar | B `InService` | status=InService | atención |
| B `InService` | completar | B `Completed` | status=Completed | cerrado, «Atendido» |

Los TRX conservan snapshots independientes de PostgreSQL, API y Razor antes/después
de cada atención completa. El test espera la desaparición real de la fila servida:
el retorno de un clic por sí solo no garantiza que la mutación haya terminado.

## Live source y fichas state-first

Live source: SignalR existente `/hubs/queue`, evento `QueueChanged`. Al recibirlo,
Blazor consulta la fuente real y renderiza. Se recarga después de suscribir y al
reconectar. Seguimiento conserva su respaldo de 15 segundos, cancelado al disponer
el componente; no se creó otro transporte ni un sondeo para motion. Su generación
de lectura impide que una respuesta anterior sobreescriba la más reciente.

| Ficha | SOURCE → TARGET | TRIGGER / COMPOSE | MECHANISM |
| --- | --- | --- | --- |
| QUEUE COUNT | `QueuePublicStatusDto.WaitingCount` anterior → actual | texto real cambia; `.fila-cifras [data-live-value]` | slot estable; antiguo `::after` sale, valor nuevo entra; dirección baja/sube, 200 ms |
| PERSONAL POSITION | `QueueTicketTrackingDto.PeopleAhead` anterior → actual | texto real cambia; `.mi-turno-posicion [data-live-delta]` | mismo nodo numérico; viaje direccional 200 ms y acento ±delta de 600 ms |
| NEXT | `Waiting` con ahead>0 → `Waiting` con ahead=0 | `Etapa=siguiente`; `.mi-turno[data-live-etapa]` | titular/explicación, borde ámbar y énfasis único de 320 ms |
| YOUR TURN | `Waiting` → `Called` | `Etapa=llamado`; `.mi-turno[data-live-etapa]` | superficie coral, jerarquía y énfasis único de 520 ms; después quieto |
| CTA | formulario tomar → confirmación con enlace seguir | `data-live-clave=tomar/confirmado`, `data-live-swap` | contenedor persistente centrado, mismo ancho/posición y alto reservado; relevo de 320 ms |

El botón de cancelar usa exclusivamente `CanCancel`; su espacio permanece cuando
el dominio retira la acción. No se inventó una acción para el estado llamado.
La confirmación y el seguimiento recuperan su superficie oscura: una regla tardía
de `.narrow` estaba reemplazándola por el fondo claro. Las correcciones de contraste
del texto auxiliar y los avisos se limitan a `.fila-tablero`.

## Motor, accesibilidad y reposo

`live-state.js` conserva su arquitectura: `MutationObserver` por región, comparación
del texto/etapa/clave anterior contra el actual, animaciones CSS y timers finitos de
respaldo en un `WeakMap`. El filtro no observa los atributos que escribe el motor.
No inserta nodos dentro del DOM administrado por Blazor. No contiene rAF ni intervalos.
La animación de armado es finita y se complementa con un único escaneo inicial para
cuando el script diferido carga después del primer `animationstart`.

Interrupt behavior: limpia la decoración anterior, cancela su timer y reinicia la
animación con el estado más nuevo. Newest-state-wins: el texto final ya es el de
Blazor; el motor nunca posterga la escritura del valor real. Previous==current no
incrementa transiciones. El final del viaje numérico ya no corta el acento de 600 ms.
Al terminar se retiran `anim`, `antes`, `delta` y `paso`; quedan sólo diagnóstico y
memoria del estado visto.

ARIA: `polite` para cifras/etapas ordinarias; `assertive` sólo en `Called`, con titular
y explicación atómicos. Los pseudo-elementos decorativos tienen alternativa vacía,
verificada en el árbol accesible, para no anunciar el valor viejo ni cada frame.
El estado final conserva toda la información textual.

Reduced motion y Save-Data: actualización instantánea, sin desplazamiento, escala,
fantasma ni transiciones; mantiene el énfasis estático. Cambiar la preferencia durante
una animación la retira. Se probó con operaciones reales de cola y, por separado,
con una prueba interna del motor para interrupciones controladas.

Idle stability: tras asentarse, ventanas de tres segundos sin interacción verifican
0 mutaciones, 0 llamadas rAF, 0 animaciones activas, 0 infinitas y 0 atributos
transitorios. El mismo gate cubre tablero, seguimiento, reduce y Save-Data.

## Evidencia y alcance

En `artifacts/j-motion-05/` están las capturas de tomar, confirmado, espera,
siguiente, llamado y atendido en 1440×1000, 1920×1080, 390×844 y 360×800.
Las pruebas comprueban overflow, identidad del contenedor, continuidad del CTA,
contraste en claro/oscuro y CLS <0,01 durante el recorrido.

Suites: `QueueJourneyTests`, `LiveStateEngineTests`, `QueueApiTests`, `QueueTests`.
Smoke de Home: municipio/escena móvil, cámara fija de escritorio, shared bounds
J-MOTION-03A y frontera estable J-MOTION-04C a 1440×1000 y 390×844.
Home, `site-motion.js`, `shared-scene.js`, municipio, rail, sticky y release lock:
sin cambios respecto de `de565ed`. No se trabajó en las deudas excluidas ni J-MOTION-06.

La entrega final `artifacts/j-motion-05/DELIVERY.md` registra los resultados definitivos,
commit único, URL de GitHub, deployment ID y verificación del destino DEV.

## Prueba HUMAN exacta en DEV

1. Abrir `https://dev.urabaconecta.com` en dos sesiones separadas: operador autorizado
   de Barbería El Corte y consumidor. Comprobar la marca DEV.
2. En el panel de turnos del operador, abrir/reanudar jornada si hace falta. Resolver
   sólo los turnos de prueba propios que pudieran quedar; no cancelar turnos ajenos.
3. Con la fila de prueba vacía, agregar tres presenciales: `Human A1`, `Human A2`,
   `Human A3`.
4. En el consumidor, abrir `/negocios/barberia-el-corte/turnos`, introducir `Human B`,
   aceptar el aviso y tomar turno. Verificar la continuidad del espacio de confirmación.
5. Abrir «Seguir mi turno»: debe indicar tres delante. Guardar ese enlace individual.
6. Operador: «Llamar siguiente» → «Iniciar atención» → «Completar» para A1, A2 y A3.
   Sin recargar el consumidor, observar 3→2→1→0 y después «Eres el siguiente».
7. Llamar B: debe aparecer «¡Es tu turno!», superficie coral y énfasis único; desaparece
   cancelar conservando el espacio. Esperar cinco segundos: nada continúa pulsando.
8. Iniciar atención y completar B: observar «Te están atendiendo» y «Atendido».
9. Repetir con movimiento reducido; comprobar la misma información sin viaje.
   Revisar los cuatro tamaños y la lectura con lector de pantalla real.

PROD modified: NO. La aprobación humana de motion queda pendiente de este recorrido.
