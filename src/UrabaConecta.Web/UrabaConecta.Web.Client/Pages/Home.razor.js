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

export function initialize() {
  if (!globalThis.__urabaAhoraScrollBound) {
    globalThis.__urabaAhoraScrollBound = true;
    try {
      localStorage.removeItem(retired[0]);
      sessionStorage.removeItem(retired[1]);
    } catch { /* almacenamiento bloqueado: nada que limpiar */ }
    document.addEventListener('click', onDocumentClick, true);
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
    sessionStorage.setItem(scrollKey, String(window.scrollY));
    sessionStorage.setItem(returnKey, '1');
    if (step) sessionStorage.setItem(sceneKey, JSON.stringify({
      context: step.dataset.stageContext,
      index: sceneNodes(step).findIndex(x => x.classList.contains('is-active'))
    }));
  } catch { /* La navegación sigue aunque el almacenamiento esté bloqueado. */ }

  document.querySelector('.ahora-home')?.classList.add('is-leaving');
  // La foto que se venía mirando crece hacia la ficha en vez de cortarse: la continuidad es de la
  // media, así que es la media la que se lleva el gesto.
  const media = step?.querySelector('[data-stage-media]');
  media?.classList.add('is-opening');

  const plain = event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey
    && !event.altKey && !link.target && link.origin === location.origin;
  if (plain && !quiet()) {
    event.preventDefault();
    const destination = link.href;
    setTimeout(() => location.assign(destination), 180);
  }
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
    if (step.dataset.stageSynced !== context) {
      const handoff = step.dataset.stageSynced !== undefined;
      step.dataset.stageSynced = context;
      apply(step, 0, { animate: false });
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
    media.dataset.stageReady = 'true';
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

function select(step, wanted) {
  const scenes = sceneNodes(step);
  if (scenes.length === 0) return;
  apply(step, Math.max(0, Math.min(scenes.length - 1, wanted)), { animate: true });
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
  const open = step.querySelector('[data-stage-open]');
  if (open && data.sceneUrl) {
    open.setAttribute('href', data.sceneUrl);
    open.setAttribute('aria-label', data.sceneCta || '');
  }

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

function queueScrollRestore() {
  if (sessionStorage.getItem(returnKey) !== '1' || location.pathname !== '/') return;
  const top = Number(sessionStorage.getItem(scrollKey) || 0);
  let waitAttempt = 0;
  const waitForFeed = () => {
    if (!document.querySelector('[data-testid="feed-piece"]')) {
      if (waitAttempt++ < 600) setTimeout(waitForFeed, 100);
      else sessionStorage.removeItem(returnKey);
      return;
    }
    restoreScene();
    let restoreAttempt = 0;
    const restore = () => {
      window.scrollTo({ top, behavior: 'instant' });
      restoreAttempt++;
      if (restoreAttempt < 6) setTimeout(restore, restoreAttempt * 90);
      else sessionStorage.removeItem(returnKey);
    };
    restore();
  };
  requestAnimationFrame(waitForFeed);
}

/// Volver de un negocio devuelve la escena que se estaba mirando, no la primera: el contexto es
/// lo que se estaba explorando, y perderlo obliga a rehacer el camino.
function restoreScene() {
  try {
    const saved = JSON.parse(sessionStorage.getItem(sceneKey) || 'null');
    if (!saved) return;
    const step = document.querySelector('.stage-step');
    if (!step || step.dataset.stageContext !== saved.context) return;
    syncStage();
    apply(step, Number(saved.index) || 0, { animate: false });
  } catch { /* El scroll vertical sigue restaurándose. */ }
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
