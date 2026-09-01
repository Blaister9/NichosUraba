// El mismo nombre que lee CookiePlacePreference en el servidor. Si cambia aquí, cambia allí.
const placeCookie = 'uc_lugar';
const scrollKey = 'urabaAhoraScroll';
const returnKey = 'urabaAhoraReturn';
const sceneKey = 'urabaAhoraEscena';

// Restos de cuando la preferencia vivía en el navegador y el servidor no podía leerla. Se limpian
// una vez para que no quede una segunda fuente capaz de contradecir a la cookie.
const retired = ['urabaPreferredMunicipality', 'urabaAhoraFilter'];

const EASE = 'cubic-bezier(.2,.8,.2,1)';
const quiet = () => matchMedia('(prefers-reduced-motion: reduce)').matches
  || Boolean(navigator.connection?.saveData);
/// Si el navegador sabe animar entre dos documentos, la continuidad al abrir un negocio la resuelve
/// él con las dos pantallas reales —lo dice shared-scene.js y lo describe el CSS—, y esta pantalla
/// no tiene que hacer nada más que dejarla ir.
const continuous = () => 'onpageswap' in window;

export function initialize() {
  if (!globalThis.__urabaAhoraScrollBound) {
    globalThis.__urabaAhoraScrollBound = true;
    try {
      localStorage.removeItem(retired[0]);
      sessionStorage.removeItem(retired[1]);
    } catch { /* almacenamiento bloqueado: nada que limpiar */ }
    document.addEventListener('click', onDocumentClick, true);
    // El recorrido es la fuente del estado, así que se escucha donde ocurre y sin retenerlo: nada
    // aquí cambia la posición del scroll, sólo la lee.
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onResize, { passive: true });
    window.addEventListener('wheel', onTraversalIntent, { passive: true });
    window.addEventListener('touchmove', onTraversalIntent, { passive: true });
    window.addEventListener('keydown', onTraversalIntent);
    window.addEventListener('popstate', onReturn);
    window.addEventListener('pageshow', onReturn);
    document.addEventListener('enhancedload', onReturn);
  }
  syncStage();
  queueScrollRestore();
}

function onReturn() {
  syncStage();
  queueScrollRestore();
}

const chapterNodes = step => [...step.querySelectorAll('[data-stage-chapter]')];

/// EL ESTADO DE LA SECUENCIA. Una sola lectura del recorrido decide dos cosas —qué capítulo manda y
/// en qué fase va— y de ahí cuelga todo lo demás: el recorte de la media, el sitio del contexto, la
/// identidad que viaja a la ficha, la oferta que entra y cuál de las dos acciones es la protagonista.
/// No hay seis animaciones con relojes distintos: hay un estado y una hoja de estilos que lo lee.
let ultimoDesplazamiento = -1;
let restaurandoRecorrido = false;

function onTraversalIntent(event) {
  if (event.type === 'keydown'
    && !['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(event.key)) return;
  document.querySelectorAll('.stage-step').forEach(step => {
    step.removeAttribute('data-stage-manual');
    step.dataset.stageTraversing = 'true';
  });
}

function onScroll() {
  if (restaurandoRecorrido) return;
  const y = window.scrollY;
  if (Math.abs(y - ultimoDesplazamiento) < 4) return;
  ultimoDesplazamiento = y;
  const steps = [...document.querySelectorAll('.stage-step[data-capitulos="vivo"]')];
  steps.forEach(step => {
    if (step.dataset.stageTraversing === 'true') {
      step.dataset.stageTraversalY = String(y);
      step.removeAttribute('data-stage-traversing');
    }
  });
  // Enfocar o clicar un control puede hacer que el navegador acomode unos píxeles la página antes
  // del gesto. Eso no es recorrer la secuencia y no debe cambiar el negocio bajo el puntero. Rueda,
  // gesto vertical o teclas de desplazamiento limpian este resguardo antes del scroll real.
  steps.forEach(readChapters);
}

function onResize() {
  ultimoDesplazamiento = -1;
  document.querySelectorAll('.stage-step[data-capitulos="vivo"]').forEach(step => {
    measureCamera(step);
    medirEscena(step);
    readChapters(step);
  });
}

/// La cámara no cambia de tamaño al compactarse —recorta y desplaza, que no son maquetación—, así que
/// su alto se mide una vez y sirve para dos cosas: saber dónde empieza el campo del capítulo y dónde
/// tiene que aterrizar uno cuando se salta a él desde los controles.
function measureCamera(step) {
  const camera = step.querySelector('[data-stage-camera]');
  if (!camera) return;
  const header = document.querySelector('.site-header');
  if (header?.offsetHeight) {
    document.documentElement.style.setProperty('--alto-cabecera', `${Math.round(header.offsetHeight)}px`);
  }
  const alto = Math.round(camera.getBoundingClientRect().height);
  if (alto > 0) step.style.setProperty('--tope-capitulo', `${alto + 12}px`);
}

function readChapters(step) {
  // Toda lectura geométrica —venga de scroll, resize o una resincronización— respeta el atajo hasta
  // que exista una intención vertical real. Mantener la puerta aquí evita que dos entradas distintas
  // puedan contradecirse y deshabiliten las flechas entre dos clics rápidos.
  if (step.dataset.stageManual === 'true') return;
  const chapters = chapterNodes(step);
  if (chapters.length === 0) return;
  const camera = step.querySelector('[data-stage-camera]');
  const cameraBox = camera?.getBoundingClientRect();
  const stickyTop = camera ? parseFloat(getComputedStyle(camera).top) || 0 : 0;
  // Cuando el último capítulo suelta la cámara, su caja ya está fuera del viewport. La línea de
  // lectura conserva el borde que tenía fijada para que el estado no rebote entre soltar y fijar.
  const linea = camera ? Math.max(cameraBox?.bottom || 0, stickyTop + camera.offsetHeight) : 0;

  // Manda el último capítulo cuyo borde ya cruzó la cámara. Esta regla usa una sola línea estable
  // del layout y no compite con cuántos píxeles casualmente quedan visibles bajo el pliegue.
  let elegido = 0;
  chapters.forEach((chapter, index) => {
    const caja = chapter.getBoundingClientRect();
    if (caja.top <= linea + 1) elegido = index;
  });

  // El avance dentro del capítulo es lo que hay recorrido de su propio alto: el capítulo dura lo que
  // mide, ni un milisegundo más.
  const caja = chapters[elegido].getBoundingClientRect();
  const avance = Math.max(0, Math.min(1, (linea - caja.top) / Math.max(1, caja.height)));
  const fase = avance < 0.14 ? 'a' : avance < 0.52 ? 'b' : 'c';
  commitState(step, elegido, fase, avance, { animate: true });
}

/// El único punto que publica el estado y deriva la composición. Los atributos son también el
/// contrato observable de la escena: negocio, capítulo, fase y progreso siempre avanzan juntos.
function commitState(step, chapter, phase, progress, { animate }) {
  const scenes = sceneNodes(step);
  const scene = scenes[chapter];
  if (!scene) return;

  const normalized = Math.max(0, Math.min(1, Number(progress) || 0));
  step.dataset.activeBusiness = scene.dataset.sceneVt || '';
  step.dataset.activeChapter = String(chapter);
  step.dataset.activePhase = phase;
  step.dataset.fase = phase;
  step.dataset.progress = normalized.toFixed(3);
  step.dataset.finalChapter = chapter === scenes.length - 1 ? 'true' : 'false';

  // El movimiento fino también sale del mismo progreso. El capítulo cambia por umbrales; recorte,
  // profundidad y compactación viajan con cada píxel de scroll nativo entre esos umbrales.
  const activo = scenes.findIndex(x => x.classList.contains('is-active'));
  if (chapter !== activo) {
    apply(step, chapter, { animate });
    // El capítulo nuevo trae otra altura de panel y otra oferta: la geometría de la que cuelga la
    // composición se vuelve a medir aquí y no en cada fotograma.
    medirEscena(step);
    // El relevo es el único salto real del recorrido: el progreso vuelve de ~1 a ~0 sin que el dedo
    // haya recorrido nada. Marcarlo permite recorrer ESE tramo y sólo ese.
    step.__relevo = true;
  }

  // El movimiento fino sale de un solo número. El capítulo cambia por umbrales; recorte, profundidad,
  // compactación y sombra son funciones continuas de ese número, así que no pueden desincronizarse.
  step.__progresoObjetivo = normalized;
  if (quiet()) { step.__progresoPintado = normalized; pintarEscena(step, normalized); }
  else programarPintado(step);
}

/// La geometría de la que cuelga la composición. Medirla una vez por capítulo y por redimensión
/// —y no en cada fotograma— deja el bucle de pintado en escrituras puras, sin forzar maquetación.
function medirEscena(step) {
  const media = step.querySelector('[data-stage-media]');
  const camera = step.querySelector('[data-stage-camera]');
  step.__geo = {
    desktop: matchMedia('(min-width: 900px)').matches,
    mediaHeight: media?.getBoundingClientRect().height || 0,
    contextHeight: step.querySelector('[data-stage-context]')?.offsetHeight || 0,
    stickyTop: camera ? parseFloat(getComputedStyle(camera).top) || 0 : 0
  };
}

/// Escribe la composición para un progreso dado. Todas las propiedades salen del mismo número: si el
/// recorte va por la mitad, la profundidad, la sombra y el panel van exactamente por la mitad.
function pintarEscena(step, progreso) {
  const geo = step.__geo || (medirEscena(step), step.__geo);
  const { desktop, mediaHeight, contextHeight, stickyTop } = geo;
  const crop = progreso * (desktop ? .56 : .66);
  const contextShift = desktop ? 0 : -progreso * mediaHeight * .66;
  const visualBottom = desktop ? mediaHeight
    : Math.max(mediaHeight * (1 - crop), mediaHeight + contextShift + contextHeight);
  step.style.setProperty('--progreso-escena', progreso.toFixed(4));
  step.style.setProperty('--recorte-escena', `${(crop * 100).toFixed(2)}%`);
  step.style.setProperty('--escala-media', (1 + progreso * .045).toFixed(4));
  step.style.setProperty('--deriva-media', `${(-progreso * 12).toFixed(2)}px`);
  step.style.setProperty('--desplaza-contexto', `${contextShift.toFixed(2)}px`);
  step.style.setProperty('--tope-panel', `${Math.round(stickyTop + visualBottom + 12)}px`);
}

/// El único reloj de la escena. No anima nada por su cuenta: acerca lo pintado a lo que el recorrido
/// ya decidió y se apaga al llegar.
///
/// Dos constantes y no una, porque son dos problemas distintos. Mientras se recorre un capítulo el
/// progreso ya viene continuo del scroll y lo único que hace falta es no ir por detrás del dedo: con
/// TAU_RECORRIDO el retraso es de un fotograma y no se percibe. En el relevo entre capítulos el
/// progreso sí salta —vuelve de ~1 a ~0 sin que nadie haya recorrido nada— y ahí se usa TAU_RELEVO,
/// que convierte ese corte en un tramo de unos 15 fotogramas. Medido: el mayor salto por fotograma
/// en el relevo baja de ~.57 a ~.10.
const TAU_RECORRIDO = 26;
const TAU_RELEVO = 110;
function programarPintado(step) {
  if (step.__pintando) return;
  step.__pintando = true;
  let anterior = performance.now();
  const paso = ahora => {
    const objetivo = step.__progresoObjetivo ?? 0;
    const actual = step.__progresoPintado ?? objetivo;
    const dt = Math.min(64, Math.max(1, ahora - anterior));
    anterior = ahora;
    const k = 1 - Math.exp(-dt / (step.__relevo ? TAU_RELEVO : TAU_RECORRIDO));
    const siguiente = actual + (objetivo - actual) * k;
    const llegado = Math.abs(objetivo - siguiente) < .0005;
    step.__progresoPintado = llegado ? objetivo : siguiente;
    if (llegado) step.__relevo = false;
    pintarEscena(step, step.__progresoPintado);
    if (llegado || !step.isConnected) { step.__pintando = false; return; }
    requestAnimationFrame(paso);
  };
  requestAnimationFrame(paso);
}

/// Un solo oyente para toda la pantalla: los controles de la escena y la salida hacia un negocio.
function onDocumentClick(event) {
  const control = event.target.closest?.('[data-stage-prev],[data-stage-next],[data-stage-scene]');
  if (control) {
    const step = control.closest('.stage-step');
    const media = step?.querySelector('[data-stage-media]');
    if (!media) return;
    // Sin escenas montadas el enlace sigue siendo un enlace: no se cancela nada.
    if (media.dataset.stageReady !== 'true') return;
    event.preventDefault();
    const scenes = sceneNodes(step);
    const active = scenes.findIndex(x => x.classList.contains('is-active'));
    const wanted = control.hasAttribute('data-stage-prev') ? active - 1
      : control.hasAttribute('data-stage-next') ? active + 1
      : Number(control.dataset.stageScene);
    select(step, wanted);
    return;
  }

  const link = event.target.closest?.('.feed-business-link');
  if (!link) return;
  const step = link.closest('.stage-step');
  try {
    const returnTop = step?.dataset.stageManual === 'true'
      ? Number(step.dataset.stageManualTop || step.dataset.stageTraversalY || 0)
      : window.scrollY;
    sessionStorage.setItem(scrollKey, String(returnTop));
    sessionStorage.setItem(returnKey, '1');
    if (step) sessionStorage.setItem(sceneKey, JSON.stringify({
      context: step.dataset.stageContext,
      index: sceneNodes(step).findIndex(x => x.classList.contains('is-active'))
    }));
  } catch { /* La navegación sigue aunque el almacenamiento esté bloqueado. */ }

  // LA SALIDA. Abrir en otra pestaña, con teclas, o hacia fuera del sitio: eso es del navegador y
  // aquí no se toca nada. Sin movimiento tampoco: se navega como siempre y no se adorna la salida.
  const plain = event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey
    && !event.altKey && !link.target && link.origin === location.origin;
  if (!plain || quiet()) return;

  const destination = link.href;
  if (continuous()) {
    // Se fuerza la navegación de documento, que es la que este enlace ya hacía. No es un retraso
    // —se va en el mismo gesto— sino la condición del mecanismo: el enrutador interactivo de Blazor
    // cambiaría la pantalla sin cambiar de documento, y con un solo documento el navegador no tiene
    // dos pantallas que animar. Con dos, la continuidad la coreografía él con las reales.
    event.preventDefault();
    location.assign(destination);
    return;
  }

  // Sin esa API queda el mecanismo viejo, y sólo aquí: su trabajo es que el toque tenga respuesta
  // mientras el documento nuevo llega, y por eso sigue siendo una escala con temporizador.
  document.querySelector('.ahora-home')?.classList.add('is-leaving');
  step?.querySelector('[data-stage-media]')?.classList.add('is-opening');
  event.preventDefault();
  setTimeout(() => location.assign(destination), 180);
}

const sceneNodes = step => [...step.querySelectorAll('[data-stage-scene]')];

/// Monta la escena y deja constancia de que ya está viva. La señal es explícita porque el HTML
/// prerenderizado existe antes que este módulo: sin ella, algo podía tocar la escena a medio montar.
export function syncStage() {
  document.querySelectorAll('.stage-step').forEach(step => {
    const media = step.querySelector('[data-stage-media]');
    if (!media) return;

    if (media.dataset.stageBound !== '1') {
      media.dataset.stageBound = '1';
      bindKeyboard(step, media);
      bindSwipe(step, media);
    }

    const context = step.dataset.stageContext || '';
    if (step.dataset.stageTraversalY === undefined) step.dataset.stageTraversalY = String(window.scrollY);
    const contextChanged = step.dataset.stageSynced !== context;
    if (contextChanged) {
      const handoff = step.dataset.stageSynced !== undefined;
      step.dataset.stageSynced = context;
      commitState(step, 0, 'a', 0, { animate: false });
      // Cambiar de municipio transforma la escena dentro del mismo marco. Tres elementos y no más:
      // el título, su entrada y el bloque accionable. Un fade-up general convertiría el cambio en
      // una recarga disfrazada.
      if (handoff) {
        reveal([
          step.querySelector('[data-stage-title]'),
          step.querySelector('[data-stage-intro]'),
          step.querySelector('[data-stage-context]')
        ]);
        step.querySelector('[data-stage-title]')?.focus({ preventScroll: true });
      }
    }
    // La secuencia se declara viva sólo cuando hay quien la lleve: hasta entonces el CSS deja la
    // pantalla completa y legible, sin cámara fija y sin oferta escondida.
    if (step.querySelector('[data-stage-chapter]')) {
      step.dataset.capitulos = 'vivo';
      measureCamera(step);
      // Un rerender del mismo feed no es recorrido. Releer aquí pisaría un atajo o la escena que
      // acaba de restaurarse al volver de la ficha; sólo la primera composición necesita inferir el
      // capítulo desde la geometría. Después lo hacen exclusivamente scroll y resize.
      if (contextChanged) readChapters(step);
    }
    // El primer OnAfterRender todavía va a sustituir este árbol al habilitar la interacción. Publicar
    // ready en ese mismo ciclo permitiría tocar un control que ya no será el nodo vigente. Esperar
    // una tarea y comprobar isConnected convierten la señal en un contrato del árbol final, no del transitorio.
    media.dataset.stageReady = 'pending';
    if (step.dataset.stageInteractive === 'true') {
      setTimeout(() => {
        if (media.isConnected && step.isConnected && step.dataset.stageInteractive === 'true') {
          media.dataset.stageReady = 'true';
        }
      }, 0);
    }
  });
}

function bindKeyboard(step, media) {
  media.addEventListener('keydown', event => {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
    event.preventDefault();
    const scenes = sceneNodes(step);
    const active = scenes.findIndex(x => x.classList.contains('is-active'));
    select(step, active + (event.key === 'ArrowRight' ? 1 : -1));
  });
}

/// El gesto táctil es de la media, no de la página: sólo se toma el arrastre cuando es claramente
/// horizontal, así que deslizar para leer sigue funcionando igual que antes.
function bindSwipe(step, media) {
  let startX = 0, startY = 0, tracking = false, decided = false, horizontal = false;

  media.addEventListener('pointerdown', event => {
    if (event.pointerType === 'mouse') return;
    tracking = true; decided = false; horizontal = false;
    startX = event.clientX; startY = event.clientY;
  }, { passive: true });

  media.addEventListener('pointermove', event => {
    if (!tracking) return;
    const dx = event.clientX - startX;
    const dy = event.clientY - startY;
    if (!decided) {
      if (Math.abs(dx) < 8 && Math.abs(dy) < 8) return;
      decided = true;
      horizontal = Math.abs(dx) > Math.abs(dy);
    }
    if (!horizontal) { tracking = false; return; }
    if (!quiet()) media.style.setProperty('--stage-drag', `${Math.max(-64, Math.min(64, dx * .32))}px`);
  }, { passive: true });

  const release = event => {
    if (!tracking) return;
    const dx = event.clientX - startX;
    tracking = false;
    media.style.removeProperty('--stage-drag');
    if (!horizontal || Math.abs(dx) < 44) return;
    const scenes = sceneNodes(step);
    const active = scenes.findIndex(x => x.classList.contains('is-active'));
    select(step, active + (dx < 0 ? 1 : -1));
  };
  media.addEventListener('pointerup', release, { passive: true });
  media.addEventListener('pointercancel', () => {
    tracking = false;
    media.style.removeProperty('--stage-drag');
  }, { passive: true });
}

/// Las flechas, el gesto y la lista siguen estando, pero ya no son la experiencia: son un acceso
/// rápido a otra escena sin mover la página bajo la mano. Cuando la persona vuelve a desplazar, el
/// recorrido nativo retoma el mando y alinea el estado con el capítulo que está atravesando.
function select(step, wanted) {
  const scenes = sceneNodes(step);
  if (scenes.length === 0) return;
  const index = Math.max(0, Math.min(scenes.length - 1, wanted));
  commitState(step, index, 'a', 0, { animate: true });
  // Los ajustes de foco/clic no son recorrido; una intención vertical real retira esta marca.
  ultimoDesplazamiento = window.scrollY;
  step.dataset.stageManual = 'true';
  step.dataset.stageManualTop = step.dataset.stageTraversalY || String(window.scrollY);
}

/// Pinta una escena: la foto se funde, el contexto se reescribe y los controles dicen dónde está.
function apply(step, index, { animate }) {
  const scenes = sceneNodes(step);
  const scene = scenes[index];
  if (!scene) return;

  scenes.forEach((node, position) => {
    const active = position === index;
    node.classList.toggle('is-active', active);
    if (active) node.setAttribute('aria-current', 'true');
    else node.removeAttribute('aria-current');
  });
  // El capítulo y la cámara son el mismo estado: lo que se lee abajo es de lo que se está hablando
  // arriba, y por eso lo enciende la misma línea de código y no un segundo mecanismo.
  chapterNodes(step).forEach((node, position) => node.classList.toggle('is-active', position === index));
  chapterNodes(step).forEach((node, position) => {
    if (position === index) node.setAttribute('aria-current', 'true');
    else node.removeAttribute('aria-current');
  });

  const media = step.querySelector('[data-stage-media]');
  const data = scene.dataset;
  crossfade(media, data.sceneImage, data.sceneAlt);

  media.dataset.stageArt = data.sceneImage ? '' : '1';
  setText(step, '[data-stage-intro]', data.sceneQuestion);
  setText(step, '[data-stage-kicker]', data.sceneLine);
  setText(step, '[data-stage-name]', data.sceneName);
  setText(step, '[data-stage-cta]', data.sceneCta);

  const state = step.querySelector('[data-stage-state]');
  if (state) {
    state.classList.toggle('is-live', data.sceneLive === '1');
    const label = state.querySelector('span:last-child');
    if (label) label.textContent = data.sceneState || '';
  }

  const action = step.querySelector('[data-stage-action]');
  if (action && data.sceneUrl) action.setAttribute('href', data.sceneUrl);
  // La fotografía abre la ficha del negocio; el botón se queda con la acción operativa.
  const open = step.querySelector('[data-stage-open]');
  if (open && data.sceneFicha) open.setAttribute('href', data.sceneFicha);

  const badges = step.querySelector('[data-stage-badges]');
  if (badges) badges.innerHTML = badgeMarkup(data);

  const counter = step.querySelector('[data-stage-counter]');
  if (counter) counter.textContent = `${index + 1} / ${scenes.length}`;
  const previous = step.querySelector('[data-stage-prev]');
  const next = step.querySelector('[data-stage-next]');
  if (previous) previous.disabled = index <= 0;
  if (next) next.disabled = index >= scenes.length - 1;

  const wrap = step.querySelector('[data-testid="feed-piece"]');
  if (wrap && data.sceneCategoria) wrap.dataset.categoria = data.sceneCategoria;
  // La identidad viaja con la escena: cambiar de negocio aquí cambia con quién se comparte el
  // contenedor al abrirlo. Si dejara de actualizarse, abrir el tercer negocio intentaría continuar
  // con la ficha del primero.
  if (wrap && data.sceneVt) wrap.dataset.escenaVt = data.sceneVt;

  // Cambiar de negocio revela tres cosas: quién es, en qué estado está y qué puedes hacer.
  if (animate) reveal([
    step.querySelector('.context-main'),
    step.querySelector('.context-facts'),
    step.querySelector('[data-stage-action]')
  ]);
}

function badgeMarkup(data) {
  let markup = '';
  if (data.sceneLive === '1') markup += '<span class="live-badge"><i></i> EN VIVO</span>';
  if (data.sceneSponsored === '1') markup += '<span class="sponsored-badge">Patrocinado</span>';
  if (data.scenePromo === '1') markup += '<span class="promotion-badge">Hoy</span>';
  return markup;
}

function setText(step, selector, value) {
  const node = step.querySelector(selector);
  if (node) node.textContent = value || '';
}

/// Dos imágenes que se turnan. La saliente no se descarta hasta que la entrante ya está decodificada:
/// eso es lo que hace que la foto continúe en vez de parpadear.
function crossfade(media, src, alt) {
  if (!media) return;
  const images = [...media.querySelectorAll('[data-stage-image]')];
  if (images.length < 2) return;

  const current = images.find(x => x.classList.contains('is-current')) || images[0];
  const waiting = images.find(x => x !== current);
  const token = (Number(media.dataset.stageToken || 0) + 1) % 1024;
  media.dataset.stageToken = String(token);

  if (!src) {
    current.classList.remove('is-current');
    images.forEach(x => { x.hidden = true; x.removeAttribute('src'); });
    return;
  }
  if (current.getAttribute('src') === src && !current.hidden) return;

  waiting.hidden = false;
  waiting.classList.remove('is-current', 'is-leaving');
  waiting.setAttribute('src', src);
  waiting.alt = '';
  waiting.setAttribute('aria-hidden', 'true');

  const show = () => {
    if (media.dataset.stageToken !== String(token)) return;
    current.classList.add('is-leaving');
    current.classList.remove('is-current');
    current.setAttribute('aria-hidden', 'true');
    current.alt = '';
    waiting.classList.add('is-current');
    waiting.setAttribute('aria-hidden', 'false');
    waiting.alt = alt || '';
    setTimeout(() => {
      if (waiting.classList.contains('is-current')) {
        current.classList.remove('is-leaving');
        current.hidden = current.getAttribute('src') === null;
      }
    }, quiet() ? 0 : 460);
  };

  if (waiting.complete) waiting.decode?.().catch(() => {}).finally(show);
  else waiting.addEventListener('load', show, { once: true });
}

/// Un revelado acotado: tres nodos como máximo y escalonados. Nunca la pantalla entera.
function reveal(nodes) {
  if (quiet()) return;
  nodes.filter(Boolean).slice(0, 3).forEach((node, position) => {
    node.animate?.(
      [{ opacity: .3, transform: 'translateY(8px)' }, { opacity: 1, transform: 'none' }],
      { duration: 300, delay: position * 60, easing: EASE, fill: 'both' });
  });
}

let restorationToken = 0;
function queueScrollRestore() {
  if (sessionStorage.getItem(returnKey) !== '1' || location.pathname !== '/') return;
  const token = ++restorationToken;
  const top = Number(sessionStorage.getItem(scrollKey) || 0);
  let waitAttempt = 0;
  const waitForFeed = () => {
    if (token !== restorationToken) return;
    if (!document.querySelector('[data-testid="feed-piece"]')) {
      if (waitAttempt++ < 600) setTimeout(waitForFeed, 100);
      else sessionStorage.removeItem(returnKey);
      return;
    }
    restaurandoRecorrido = true;
    let restoreAttempt = 0;
    const restore = () => {
      if (token !== restorationToken) return;
      // Cada intento vuelve a resolver la escena: el primer árbol puede ser el prerenderizado que
      // Blazor sustituye al habilitar la interacción. Guardar su referencia restauraría un nodo
      // desconectado y dejaría la pantalla nueva en el capítulo inicial.
      restoreScene();
      window.scrollTo({ top, behavior: 'instant' });
      restoreAttempt++;
      if (restoreAttempt < 6) setTimeout(restore, restoreAttempt * 90);
      else {
        ultimoDesplazamiento = window.scrollY;
        restaurandoRecorrido = false;
        sessionStorage.removeItem(returnKey);
        finishFiniteAnimations();
      }
    };
    restore();
  };
  // Una tarea, no un fotograma. Recuperar la escena sólo necesita que el feed esté en el DOM, y
  // colgarlo del pintado ataba una mecánica de datos a que el navegador estuviera dibujando: con el
  // compositor detenido —una pestaña que no se dibuja, un navegador sin cabeza— la escena que se
  // estaba mirando no volvía nunca.
  setTimeout(waitForFeed, 0);
}

/// Una página recuperada del historial puede volver sin ticks del compositor (ocurre en pestañas en
/// segundo plano y en navegadores sin cabeza). En ese caso las transiciones finitas quedan vivas en
/// currentTime=0 y el navegador considera inestable cualquier control. Terminar sólo las finitas
/// conserva los pulsos de estado y deja la pantalla en el mismo estado visual al que ya iba.
function finishFiniteAnimations() {
  document.getAnimations().forEach(animation => {
    if (animation.effect?.getTiming().iterations === Infinity) return;
    try { animation.finish(); } catch { animation.cancel(); }
  });
}

/// Volver de un negocio devuelve la escena que se estaba mirando, no la primera: el contexto es
/// lo que se estaba explorando, y perderlo obliga a rehacer el camino.
function restoreScene() {
  try {
    const saved = JSON.parse(sessionStorage.getItem(sceneKey) || 'null');
    if (!saved) return null;
    const step = document.querySelector('.stage-step');
    if (!step || step.dataset.stageContext !== saved.context) return null;
    syncStage();
    const index = Number(saved.index) || 0;
    commitState(step, index, 'a', 0, { animate: false });
    step.dataset.stageManual = 'true';
    step.dataset.stageManualTop = sessionStorage.getItem(scrollKey) || '0';
    return { step, index };
  } catch { return null; /* El scroll vertical sigue restaurándose. */ }
}

// Escribir la cookie es lo único que el servidor no puede hacer por su cuenta: cuando la pantalla
// ya respondió no queda encabezado donde ponerla. Seis meses, sin subdominios y sin viajar en
// peticiones de terceros.
export function rememberPlace(slug) {
  if (!slug) return;
  const secure = location.protocol === 'https:' ? '; secure' : '';
  document.cookie = `${placeCookie}=${encodeURIComponent(slug)}; path=/; max-age=15552000; samesite=lax${secure}`;
}

export function requestLocation() {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) return reject(new Error('Geolocation unavailable'));
    navigator.geolocation.getCurrentPosition(
      position => resolve({ latitude: position.coords.latitude, longitude: position.coords.longitude }),
      () => reject(new Error('Location denied')),
      { enableHighAccuracy: false, timeout: 8000, maximumAge: 300000 });
  });
}
