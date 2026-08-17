// El mismo nombre que lee CookiePlacePreference en el servidor. Si cambia aquí, cambia allí.
const placeCookie = 'uc_lugar';
const scrollKey = 'urabaAhoraScroll';
const returnKey = 'urabaAhoraReturn';

// Restos de cuando la preferencia vivía en el navegador y el servidor no podía leerla. Se limpian
// una vez para que no quede una segunda fuente capaz de contradecir a la cookie.
const retired = ['urabaPreferredMunicipality', 'urabaAhoraFilter'];

export function initialize() {
  if (!globalThis.__urabaAhoraScrollBound) {
    globalThis.__urabaAhoraScrollBound = true;
    try {
      localStorage.removeItem(retired[0]);
      sessionStorage.removeItem(retired[1]);
    } catch { /* almacenamiento bloqueado: nada que limpiar */ }
    document.addEventListener('click', event => {
      if (!event.target.closest?.('.feed-business-link')) return;
      sessionStorage.setItem(scrollKey, String(window.scrollY));
      sessionStorage.setItem(returnKey, '1');
      document.querySelector('.ahora-home')?.classList.add('is-leaving');
    }, true);
    window.addEventListener('popstate', queueScrollRestore);
    window.addEventListener('pageshow', queueScrollRestore);
    document.addEventListener('enhancedload', queueScrollRestore);
  }
  queueScrollRestore();
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
