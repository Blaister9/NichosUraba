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
  let attempt = 0;
  const restore = () => {
    if (!document.querySelector('[data-testid="feed-piece"]') && attempt < 12) {
      attempt++;
      setTimeout(restore, 50);
      return;
    }
    window.scrollTo({ top, behavior: 'instant' });
    attempt++;
    if (attempt < 5) setTimeout(restore, attempt * 70);
    else sessionStorage.removeItem(returnKey);
  };
  requestAnimationFrame(restore);
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
