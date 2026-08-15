const preferenceKey = 'urabaPreferredMunicipality';
const filterKey = 'urabaAhoraFilter';
const scrollKey = 'urabaAhoraScroll';
const returnKey = 'urabaAhoraReturn';

export function initialize() {
  if (!globalThis.__urabaAhoraScrollBound) {
    globalThis.__urabaAhoraScrollBound = true;
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
  return {
    municipality: localStorage.getItem(preferenceKey) || '',
    vertical: sessionStorage.getItem(filterKey) || 'ahora'
  };
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

export function rememberMunicipality(slug) {
  if (slug) localStorage.setItem(preferenceKey, slug);
  else localStorage.removeItem(preferenceKey);
}

export function rememberVertical(vertical) {
  if (vertical && vertical !== 'ahora') sessionStorage.setItem(filterKey, vertical);
  else sessionStorage.removeItem(filterKey);
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
