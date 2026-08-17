/* Modo instalable: registro del trabajador de servicio y estado de instalación.

   El navegador decide por su cuenta si ofrece instalar y dónde lo ofrece. En Android suele
   guardarlo dentro del menú de tres puntos, que es justo el sitio donde una persona normal no
   entra. Por eso aquí se hacen dos cosas: se captura el permiso del navegador para abrir el
   diálogo nativo cuando queramos, y cuando ese permiso no llega se describe el camino manual del
   navegador concreto que se está usando, en vez de callar.

   El disparo del diálogo nativo va por delegación de eventos y no por interoperabilidad de
   Blazor: Chrome exige que prompt() se llame dentro del gesto de la persona, y un @onclick de
   InteractiveServer viaja al servidor y vuelve. Escuchando el clic aquí, la llamada ocurre dentro
   del propio evento del DOM y nunca depende de lo que tarde el circuito. */
(() => {
  const INSTALADA = 'urabaAppInstalada';
  const DESCARTADA = 'urabaInstalarDescartada';
  const DIAS_DE_DESCARTE = 14;

  /** El BeforeInstallPromptEvent que el navegador nos dejó guardar, o null. */
  let oferta = null;
  /* Referencias .NET que quieren enterarse de los cambios de estado, por clave propia del
     componente: Blazor no garantiza que la misma referencia llegue como el mismo objeto de
     JavaScript, así que darse de baja por identidad dejaría oyentes muertos acumulándose. */
  const oyentes = new Map();

  const guardar = (clave, valor) => { try { localStorage.setItem(clave, valor); } catch { } };
  const leer = clave => { try { return localStorage.getItem(clave); } catch { return null; } };
  const borrar = clave => { try { localStorage.removeItem(clave); } catch { } };

  /* Correr como aplicación es algo que se comprueba ahora, no que se recuerda: lo dice el modo de
     presentación que informa el propio navegador.

     Aquí había también document.referrer.startsWith('android-app://'). Chrome de Android pone ese
     referrer a CUALQUIER enlace abierto desde otra aplicación —WhatsApp, el correo, las notas—,
     que es exactamente como llega la gente a la Demo. Concluíamos "ya está instalada", quitábamos
     el botón del DOM y la persona se quedaba sin ninguna salida. Un TWA de verdad ya se declara
     con display-mode: standalone, así que no se pierde ningún caso legítimo. */
  const enModoApp = () =>
    ['standalone', 'minimal-ui', 'fullscreen', 'window-controls-overlay']
      .some(modo => window.matchMedia(`(display-mode: ${modo})`).matches) ||
    window.navigator.standalone === true;

  const ua = navigator.userAgent || '';
  const esAndroid = /Android/i.test(ua);
  // iPadOS se anuncia como escritorio desde iOS 13; el número de puntos táctiles lo delata.
  const esIOS = /iPhone|iPad|iPod/i.test(ua) ||
    (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

  const navegador = () => {
    if (/SamsungBrowser/i.test(ua)) return 'samsung';
    if (/HuaweiBrowser|HonorBrowser/i.test(ua)) return 'huawei';
    if (/MiuiBrowser|HeyTapBrowser|OppoBrowser|VivoBrowser/i.test(ua)) return 'oem';
    if (/FxiOS/i.test(ua) || /Firefox/i.test(ua)) return 'firefox';
    if (/OPR|OPT\//i.test(ua)) return 'opera';
    if (/Edg/i.test(ua)) return 'edge';
    if (/CriOS/i.test(ua)) return 'chrome-ios';
    if (/Chrome|Chromium/i.test(ua)) return 'chrome';
    if (/Safari/i.test(ua)) return 'safari';
    return 'otro';
  };

  const plataforma = () => esIOS ? 'ios' : esAndroid ? 'android' : 'escritorio';

  /* Instrucciones manuales. Son literales de cada navegador, no una frase genérica: "abre el
     menú" no sirve si la persona no sabe cuál de los dos menús de la pantalla es. */
  const camino = () => {
    const cual = navegador();
    if (esIOS) {
      if (cual === 'safari') return {
        menu: 'Compartir',
        steps: ['Toca el botón Compartir, abajo en el centro.',
                'Baja y elige “Añadir a pantalla de inicio”.',
                'Confirma con “Añadir”.']
      };
      // En iOS sólo Safari puede instalar: cualquier otro navegador usa su motor pero no expone
      // la opción, así que la única salida honesta es mandar a Safari.
      return {
        menu: 'Safari',
        steps: ['Abre urabaconecta en Safari.',
                'Toca Compartir y elige “Añadir a pantalla de inicio”.']
      };
    }
    if (esAndroid) {
      if (cual === 'samsung') return {
        menu: 'Menú',
        steps: ['Toca el menú (☰) abajo a la derecha.',
                'Elige “Añadir página a”.',
                'Elige “Pantalla de inicio”.']
      };
      if (cual === 'firefox') return {
        menu: 'Menú',
        steps: ['Toca el menú (⋮) de la barra.', 'Elige “Instalar”.']
      };
      return {
        menu: 'Menú',
        steps: ['Toca el menú (⋮) arriba a la derecha.',
                'Elige “Instalar aplicación” o “Añadir a pantalla de inicio”.',
                'Confirma con “Instalar”.']
      };
    }
    if (cual === 'chrome' || cual === 'edge' || cual === 'opera') return {
      menu: 'Barra de direcciones',
      steps: ['Busca el icono de instalar al final de la barra de direcciones.',
              'O abre el menú (⋮) y elige “Instalar UrabáConecta”.']
    };
    return { menu: '', steps: [] };
  };

  const descartada = () => {
    const cuando = Number(leer(DESCARTADA));
    if (!cuando) return false;
    return Date.now() - cuando < DIAS_DE_DESCARTE * 86_400_000;
  };

  const estado = () => {
    const pasos = camino();
    // Dos cosas distintas: correr como aplicación se comprueba; haberla instalado alguna vez sólo
    // se recuerda. La marca sirve para rotular el estado, nunca para retirar la ayuda: si la marca
    // quedó de una instalación que ya no existe, quien mira esta pestaña necesita el camino igual.
    const comoApp = enModoApp();
    let mode;
    if (comoApp || leer(INSTALADA) === '1') mode = 'installed';
    else if (oferta) mode = 'native';
    else if (pasos.steps.length > 0) mode = 'manual';
    else mode = 'unavailable';
    return {
      mode,
      runningAsApp: comoApp,
      dismissed: descartada(),
      platform: plataforma(),
      browser: navegador(),
      menu: pasos.menu,
      steps: pasos.steps
    };
  };

  const avisar = () => {
    const actual = estado();
    for (const [clave, oyente] of [...oyentes]) {
      oyente.invokeMethodAsync('EstadoDeInstalacionCambio', actual).catch(() => oyentes.delete(clave));
    }
  };

  window.addEventListener('beforeinstallprompt', evento => {
    // Sin preventDefault, Chrome puede pintar su propia franja encima de la nuestra.
    evento.preventDefault();
    oferta = evento;
    borrar(INSTALADA);
    avisar();
  });

  window.addEventListener('appinstalled', () => {
    oferta = null;
    guardar(INSTALADA, '1');
    borrar(DESCARTADA);
    avisar();
  });

  /* Instalar desde el navegador y abrir después la aplicación no recarga esta pestaña; el cambio
     de display-mode es la única señal de que ya ocurrió. Va protegido porque los navegadores
     viejos no escuchan cambios en una consulta de medios, y una excepción suelta aquí se llevaría
     por delante lo que viene después: el objeto de instalación y el registro del sw. */
  try {
    window.matchMedia('(display-mode: standalone)')
      .addEventListener('change', evento => { if (evento.matches) { guardar(INSTALADA, '1'); avisar(); } });
  } catch { }

  /* El clic se atiende aquí, en fase de captura, para que prompt() ocurra dentro del gesto. */
  const abrirDialogo = async () => {
    if (!oferta) { avisar(); return 'unavailable'; }
    const evento = oferta;
    // Un BeforeInstallPromptEvent sólo se puede usar una vez: se suelta antes de esperar la
    // respuesta para no reintentar sobre un evento ya gastado.
    oferta = null;
    try {
      evento.prompt();
      const { outcome } = await evento.userChoice;
      if (outcome === 'accepted') guardar(INSTALADA, '1');
      else oferta = evento;
      avisar();
      return outcome;
    } catch {
      // Chrome lanza si el gesto ya caducó. Se cae a las instrucciones manuales, que siempre valen.
      avisar();
      return 'error';
    }
  };

  document.addEventListener('click', evento => {
    const boton = evento.target instanceof Element
      ? evento.target.closest('[data-uraba-instalar]') : null;
    if (boton) abrirDialogo();
  }, true);

  window.urabaApp = {
    install: {
      state: estado,
      prompt: abrirDialogo,
      dismiss: () => { guardar(DESCARTADA, String(Date.now())); avisar(); },
      watch: (clave, oyente) => { oyentes.set(clave, oyente); return estado(); },
      unwatch: clave => { oyentes.delete(clave); }
    }
  };

  if (!('serviceWorker' in navigator)) return;
  window.addEventListener('load', async () => {
    try {
      const registration = await navigator.serviceWorker.register('/sw.js', { scope: '/' });
      registration.update().catch(() => {});
    } catch (error) {
      console.warn('No fue posible registrar el modo instalable.', error);
    }
  }, { once: true });
})();
