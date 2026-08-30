/* Modo instalable: registro del trabajador de servicio y estado de instalación.

   El navegador decide si ofrece instalar. Se captura ese permiso para abrir su diálogo desde una
   acción explícita. Cuando no existe prompt programático sólo se conserva una instrucción manual
   mínima en dispositivos donde ese camino es conocido; no se simula un instalador propio.

   El disparo del diálogo nativo va por delegación de eventos y no por interoperabilidad de
   Blazor: Chrome exige que prompt() se llame dentro del gesto de la persona, y un @onclick de
   InteractiveServer viaja al servidor y vuelve. Escuchando el clic aquí, la llamada ocurre dentro
   del propio evento del DOM y nunca depende de lo que tarde el circuito. */
(() => {
  const INSTALADA_LEGACY = 'urabaAppInstalada';
  const DESCARTADA = 'urabaInstalarDescartada';
  const DIAS_DE_DESCARTE = 14;
  const CORTESIA_MS = 1_600;
  /* Aceptar el diálogo del navegador no es haber instalado: la única confirmación es
     appinstalled (o el cambio de display-mode). Esa señal se espera una ventana acotada y no
     para siempre. Doce segundos cubren con holgura lo que tarda Chrome en escribir el acceso
     directo —uno o dos normalmente, y unos pocos más en un teléfono lento bajando los iconos
     del manifiesto—, y siguen siendo poco tiempo para quien mira la pantalla. Sin este límite,
     un appinstalled que no llega nunca —el sistema cancela la instalación, o el navegador
     simplemente no emite el evento— dejaba a la persona en "Terminando la instalación…" sin
     botón, sin "Ahora no" y sin más salida que recargar. */
  const ESPERA_INSTALACION_MS = 12_000;

  /** El BeforeInstallPromptEvent que el navegador nos dejó guardar, o null. */
  let oferta = null;
  let instalacionPendiente = false;
  /** Temporizador de recuperación del estado pendiente, o 0 si no hay ninguno en marcha. */
  let relojDeEspera = 0;
  let instaladaEnEstaSesion = false;
  let invitacionLista = false;
  /* Referencias .NET que quieren enterarse de los cambios de estado, por clave propia del
     componente: Blazor no garantiza que la misma referencia llegue como el mismo objeto de
     JavaScript, así que darse de baja por identidad dejaría oyentes muertos acumulándose. */
  const oyentes = new Map();

  const guardar = (clave, valor) => { try { localStorage.setItem(clave, valor); } catch { } };
  const leer = clave => { try { return localStorage.getItem(clave); } catch { return null; } };
  const borrar = clave => { try { localStorage.removeItem(clave); } catch { } };

  // Versiones anteriores recordaban para siempre una instalación que podía no haber terminado.
  // Se retira esa marca al cargar: instalada sólo significa una señal real de esta sesión.
  borrar(INSTALADA_LEGACY);

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

  /* No se mantienen matrices por marca de teléfono. La presencia de navigator.standalone es la
     capacidad que expone iOS/WebKit; Android y Chromium desktop sólo se distinguen para ofrecer el
     camino de menú que realmente existe cuando el evento nativo todavía no está disponible. */
  const caminoManual = () => {
    if ('standalone' in navigator) return {
      platform: 'ios',
      menu: 'Compartir',
      steps: [
        'Toca Compartir en la barra del navegador.',
        'Elige “Añadir a pantalla de inicio” y confirma.'
      ]
    };
    if (/Android/i.test(navigator.userAgent || '')) return {
      platform: 'android',
      menu: 'Menú del navegador',
      steps: [
        'Abre el menú del navegador.',
        'Elige “Instalar aplicación” o “Añadir a pantalla de inicio”.'
      ]
    };
    if (/(?:Chrome|Chromium|Edg)\//i.test(navigator.userAgent || '')) return {
      platform: 'desktop',
      menu: 'Menú del navegador',
      steps: [
        'Abre el menú de este navegador.',
        'Elige “Instalar UrabáConecta” o “Instalar aplicación”.'
      ]
    };
    return { platform: '', menu: '', steps: [] };
  };

  const descartada = () => {
    const cuando = Number(leer(DESCARTADA));
    if (!cuando) return false;
    if (Date.now() - cuando < DIAS_DE_DESCARTE * 86_400_000) return true;
    borrar(DESCARTADA);
    return false;
  };

  const estado = () => {
    const pasos = caminoManual();
    // Aceptar el diálogo no equivale a instalar. Sólo appinstalled o el modo standalone permiten
    // afirmar que la instalación terminó; mientras tanto se conserva un estado pendiente honesto.
    const comoApp = enModoApp();
    let mode;
    if (comoApp || instaladaEnEstaSesion) mode = 'installed';
    else if (instalacionPendiente) mode = 'pending';
    else if (oferta) mode = 'native';
    else if (pasos.steps.length > 0) mode = 'manual';
    else mode = 'unavailable';
    return {
      mode,
      runningAsApp: comoApp,
      dismissed: descartada(),
      ready: invitacionLista,
      platform: pasos.platform,
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

  const soltarEspera = () => {
    if (!relojDeEspera) return;
    window.clearTimeout(relojDeEspera);
    relojDeEspera = 0;
  };

  /* Salir de pendiente por cualquier vía —se instaló, el navegador volvió a ofrecer su diálogo,
     el intento falló— cancela también la recuperación: un temporizador vivo sobre un estado que
     ya cambió sólo puede llegar tarde y contradecirlo. */
  const cerrarEspera = () => {
    soltarEspera();
    instalacionPendiente = false;
  };

  /* Recuperación acotada, no reintento: al vencer no se afirma nada que no se sepa. No se marca
     instalada —no hubo señal— ni se guarda un descarte que la persona no pidió, así que el plazo
     de catorce días queda como estaba. Se suelta el pendiente y el estado vuelve a calcularse
     solo; como la oferta nativa ya se gastó, lo que queda es el camino manual del navegador, con
     su "Ahora no" otra vez disponible. Dispara una vez y no se reprograma: no hay ciclo. */
  const esperarInstalacion = () => {
    soltarEspera();
    relojDeEspera = window.setTimeout(() => {
      relojDeEspera = 0;
      if (!instalacionPendiente) return;
      instalacionPendiente = false;
      avisar();
    }, ESPERA_INSTALACION_MS);
  };

  // La card no compite con la primera pintura. Aparece después de una breve cortesía o en cuanto
  // la persona interactúa: sigue siendo visible, pero nunca es una ventana que bloquea la entrada.
  const revelarInvitacion = () => {
    if (invitacionLista) return;
    invitacionLista = true;
    avisar();
  };
  const revelarTrasInteraccion = evento => { if (evento.isTrusted) revelarInvitacion(); };
  window.addEventListener('pointerdown', revelarTrasInteraccion, { once: true, passive: true });
  window.addEventListener('keydown', revelarTrasInteraccion, { once: true });
  const programarCortesia = () => window.setTimeout(revelarInvitacion, CORTESIA_MS);
  if (document.readyState === 'complete') programarCortesia();
  else window.addEventListener('load', programarCortesia, { once: true });

  window.addEventListener('beforeinstallprompt', evento => {
    // Sin preventDefault, Chrome puede pintar su propia franja encima de la nuestra.
    evento.preventDefault();
    oferta = evento;
    cerrarEspera();
    avisar();
  });

  window.addEventListener('appinstalled', () => {
    oferta = null;
    cerrarEspera();
    instaladaEnEstaSesion = true;
    borrar(DESCARTADA);
    avisar();
  });

  /* Instalar desde el navegador y abrir después la aplicación no recarga esta pestaña; el cambio
     de display-mode es la única señal de que ya ocurrió. Va protegido porque los navegadores
     viejos no escuchan cambios en una consulta de medios, y una excepción suelta aquí se llevaría
     por delante lo que viene después: el objeto de instalación y el registro del sw. */
  try {
    window.matchMedia('(display-mode: standalone)')
      .addEventListener('change', evento => {
        if (evento.matches) {
          cerrarEspera();
          instaladaEnEstaSesion = true;
          avisar();
        }
      });
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
      if (outcome === 'accepted') {
        // El pendiente y su recuperación se arman en la misma línea: separarlos es justamente
        // como se llegó al atasco, con el estado puesto y el reloj sin arrancar.
        if (!instaladaEnEstaSesion) { instalacionPendiente = true; esperarInstalacion(); }
      } else {
        cerrarEspera();
        guardar(DESCARTADA, String(Date.now()));
      }
      avisar();
      return outcome;
    } catch {
      // Chrome lanza si el gesto ya caducó. Se cae a las instrucciones manuales, que siempre valen.
      cerrarEspera();
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
