/* Señales de que algo está pasando: navegación en curso y fotografía que termina de cargar.

   Las dos cosas viven aquí y no en un componente porque no pertenecen a ninguna pantalla: pasan
   entre pantallas, y el armazón es lo único que sigue vivo mientras ocurren.

   POR QUÉ HACE FALTA. Los destinos públicos —la barra inferior, la cabecera, la ficha de un
   negocio, el patrocinado— llevan data-enhance-nav="false", así que cada uno es una carga de
   documento completa. Entre el toque y la primera pintura de la página nueva el navegador no
   pinta nada: la pantalla vieja se queda quieta. Con red buena son doscientos milisegundos y no
   se nota; con datos móviles en Urabá son dos segundos en los que la persona ya volvió a tocar,
   convencida de que el primer toque no entró. */
(() => {
  const CLASE = 'uc-nav-progress';
  /* La barra no sale al instante: una navegación que resuelve en 60 ms sólo produciría un
     parpadeo, que es ruido y no información. Espera lo que tarda en dejar de ser instantánea. */
  const RETRASO_MS = 80;
  /* Si a los quince segundos seguimos aquí, la navegación no va a llegar —se canceló, falló, o el
     navegador la descartó— y una barra eterna miente. Se retira sola. */
  const LIMITE_MS = 15_000;

  /* Cada cuánto se mira si el enrutador ya cambió de dirección. Ver el vigía, más abajo. */
  const VIGIA_MS = 100;
  /* Lo que se deja pasar entre que la dirección cambia y se retira la barra: el enrutador
     actualiza la URL justo antes de pintar, y quitarla en ese mismo instante deja un parpadeo en
     el que ya no hay barra y todavía no hay pantalla. */
  const ASENTAR_MS = 150;

  let reloj = 0;
  let corte = 0;
  let vigia = 0;
  /* El temporizador que retira la barra cuando la dirección ya cambió. Vive aquí, con los
     demás, y no suelto dentro del vigía: un temporizador que no se puede cancelar sobrevive
     a la navegación que lo programó y acaba apagando la barra de la siguiente. */
  let asentar = 0;
  let barra = null;

  const reduce = () => {
    try { return window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
    catch { return false; }
  };

  const quitar = () => {
    window.clearTimeout(reloj); reloj = 0;
    window.clearTimeout(corte); corte = 0;
    window.clearInterval(vigia); vigia = 0;
    window.clearTimeout(asentar); asentar = 0;
    barra?.remove();
    barra = null;
  };

  const pintar = () => {
    if (barra) return;
    barra = document.createElement('div');
    barra.className = CLASE;
    /* No es un control ni un texto que alguien deba oír: el lector de pantalla ya anuncia la
       página nueva cuando llega. Aquí sólo sobraría. */
    barra.setAttribute('aria-hidden', 'true');
    if (reduce()) barra.dataset.quieta = '1';
    document.body.appendChild(barra);
  };

  const empezar = () => {
    quitar();
    const partida = location.href;
    reloj = window.setTimeout(pintar, RETRASO_MS);
    corte = window.setTimeout(quitar, LIMITE_MS);
    /* EL VIGÍA. Los destinos de este sitio se reparten entre tres formas de navegar, y sólo dos
       avisan cuando terminan. Una carga de documento entera se lleva la barra por delante junto
       con el resto de la página; la navegación mejorada emite enhancedload. Pero entre dos
       páginas interactivas manda el enrutador de Blazor: no recarga el documento y no emite
       ningún evento propio, así que la barra se quedaba encendida para siempre —comprobado con
       /seguimiento— prometiendo una espera que ya había terminado.
       Lo único que las tres formas comparten es que la dirección cambia. El vigía mira eso y
       nada más, sólo mientras hay una navegación en curso: no queda ningún temporizador vivo
       cuando no se está navegando. */
    vigia = window.setInterval(() => {
      if (location.href === partida) return;
      window.clearInterval(vigia); vigia = 0;
      asentar = window.setTimeout(quitar, ASENTAR_MS);
    }, VIGIA_MS);
  };

  /* Qué cuenta como "me voy a otra pantalla". Todo lo demás —abrir en pestaña nueva, un ancla de
     la misma página, llamar por teléfono, descargar— deja la pantalla donde está, y anunciar una
     navegación que no ocurre es peor que no anunciar nada. */
  const esNavegacion = (evento, enlace) => {
    if (evento.defaultPrevented || evento.button !== 0) return false;
    if (evento.metaKey || evento.ctrlKey || evento.shiftKey || evento.altKey) return false;
    if (enlace.target && enlace.target !== '_self') return false;
    if (enlace.hasAttribute('download')) return false;
    const destino = enlace.getAttribute('href');
    if (!destino || destino.startsWith('#')) return false;
    let url;
    try { url = new URL(enlace.href, location.href); } catch { return false; }
    if (url.origin !== location.origin) return false;
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return false;
    // Misma dirección exacta: el navegador no va a ninguna parte.
    return url.href !== location.href;
  };

  document.addEventListener('click', evento => {
    const enlace = evento.target instanceof Element ? evento.target.closest('a[href]') : null;
    if (enlace && esNavegacion(evento, enlace)) empezar();
  }, true);

  /* Volver con el botón atrás restaura la página desde la caché del navegador sin recargarla: sin
     esto, la barra que dejamos puesta al salir reaparecería con la página vieja y se quedaría. */
  window.addEventListener('pageshow', quitar);
  // Navegación mejorada de Blazor: el documento no cambia, así que hay que retirarla a mano.
  document.addEventListener('enhancedload', quitar);

  /* FOTOGRAFÍA QUE LLEGA. El hueco ya está reservado —las imágenes públicas traen width/height y
     su contenedor tiene alto propio—, así que nada salta de sitio. Lo que se ve es el salto de
     color: el hueco pasa de verde apagado a fotografía de golpe. Un fundido corto lo convierte en
     algo que aparece en vez de algo que da un tirón. */
  const PUBLICAS = '.feed-media img, .card-cover, .profile-cover, .gallery-photo, .guided-banner img';

  const marcar = imagen => {
    if (imagen.dataset.ucMedia) return;
    imagen.dataset.ucMedia = '1';
    imagen.classList.add('uc-media-lista');
  };

  /* load no burbujea, pero sí baja en captura. Un único oyente en el documento cubre también las
     fotografías que Blazor añade después, sin volver a recorrer el DOM en cada render. */
  document.addEventListener('load', evento => {
    const objetivo = evento.target;
    if (objetivo instanceof HTMLImageElement && objetivo.matches(PUBLICAS)) marcar(objetivo);
  }, true);

  /* Las que ya estaban cargadas cuando llegó este script —caché del navegador, casi siempre— no
     van a emitir load nunca. Se marcan sin fundido: ya estaban ahí, y hacerlas aparecer ahora
     sería inventar una espera que no existió. */
  const yaEstaban = () => {
    for (const imagen of document.querySelectorAll(PUBLICAS)) {
      if (imagen.complete && !imagen.dataset.ucMedia) {
        imagen.dataset.ucMedia = '1';
        imagen.classList.add('uc-media-lista', 'uc-media-sin-fundido');
      }
    }
  };
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', yaEstaban, { once: true });
  } else {
    yaEstaban();
  }
})();
