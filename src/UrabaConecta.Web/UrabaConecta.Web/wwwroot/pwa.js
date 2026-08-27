/* Modo instalable: registro del trabajador de servicio y estado de instalación.

   El navegador decide si ofrece instalar. Se captura ese permiso para abrir su diálogo desde una
   acción explícita. Cuando no existe prompt programático sólo se conserva una instrucción manual
   mínima en dispositivos donde ese camino es conocido; no se simula un instalador propio.

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

  /* No se mantienen matrices por marca o navegador. La presencia de navigator.standalone es la
     capacidad que expone el entorno móvil basado en WebKit; Android se reconoce como plataforma,
     no por fabricante. En ambos casos la ayuda describe el menú: nunca promete abrirlo sola. */
  const caminoManual = () => {
    if ('standalone' in navigator) return {
      menu: 'Compartir',
      steps: ['Toca Compartir y elige “Añadir a pantalla de inicio”.']
    };
    if (/Android/i.test(navigator.userAgent || '')) return {
      menu: 'Menú del navegador',
      steps: ['Abre el menú del navegador y elige “Instalar aplicación” o “Añadir a pantalla de inicio”.']
    };
    return { menu: '', steps: [] };
  };

  const descartada = () => {
    const cuando = Number(leer(DESCARTADA));
    if (!cuando) return false;
    return Date.now() - cuando < DIAS_DE_DESCARTE * 86_400_000;
  };

  const estado = () => {
    const pasos = caminoManual();
    // Correr como aplicación se comprueba ahora; una instalación aceptada también se recuerda para
    // no volver a ofrecerla en pestañas del mismo dispositivo. Si el navegador vuelve a emitir
    // beforeinstallprompt, esa señal más reciente limpia la marca y habilita de nuevo la oferta.
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
      platform: pasos.steps.length > 0 ? 'mobile' : '',
      browser: '',
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
      else guardar(DESCARTADA, String(Date.now()));
      avisar();
      return outcome;
    } catch {
      // Chrome lanza si el gesto ya caducó. Se cae a las instrucciones manuales, que siempre valen.
      avisar();
      return 'error';
    }
  };

  const descartarInvitacion = () => {
    guardar(DESCARTADA, String(Date.now()));
    avisar();
  };

  document.addEventListener('click', evento => {
    const objetivo = evento.target instanceof Element ? evento.target : null;
    const boton = objetivo?.closest('[data-uraba-instalar]');
    if (boton) abrirDialogo();
    // Se persiste dentro del clic, antes del viaje de @onclick a Blazor Server. Así una navegación
    // inmediata no puede adelantarse a la decisión que la interfaz ya confirmó visualmente.
    const descarte = objetivo?.closest('[data-uraba-descartar-instalacion]');
    if (descarte) descartarInvitacion();
  }, true);

  window.urabaApp = {
    install: {
      state: estado,
      prompt: abrirDialogo,
      dismiss: descartarInvitacion,
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
