/* ESTADO VIVO — lo que cambia solo mientras alguien lo mira.

   POR QUÉ EXISTE. La fila virtual es la única pantalla del producto cuyo contenido cambia sin que
   nadie la toque: el operador llama a alguien en el local y, en el teléfono de quien espera en la
   calle, un 3 pasa a ser un 2. Hasta ahora ese cambio era una sustitución de texto. Quien miraba
   de reojo veía un número distinto y no sabía si había avanzado, si había retrocedido, o si
   llevaba diez minutos viendo lo mismo. El dato llegaba; la noticia no.

   QUÉ RESUELVE. Tres cosas, y sólo tres: QUÉ cambió (el valor se distingue de sus vecinos), EN QUÉ
   DIRECCIÓN (el valor viejo sale por un lado y el nuevo entra por el contrario) y QUÉ SIGNIFICA
   (la etapa del turno cambia de superficie, no sólo de frase).

   POR QUÉ AQUÍ Y NO EN BLAZOR. El cambio semántico lo produce Blazor —SignalR empuja
   "QueueChanged" y el componente vuelve a pintar—, pero interpolar eso a sesenta cuadros por
   segundo desde el circuito sería mandar sesenta renders por un cable. Blazor dice QUÉ pasó; esto
   dice cómo se ve que pasó, y lo dice con CSS.

   POR QUÉ NO INSERTA NODOS. El valor viejo que sale de escena es el pseudo-elemento ::after del
   propio valor, alimentado por un atributo. Meter un <span> dentro de un elemento que Blazor
   gobierna le corre los índices al diff y acaba borrando lo que no es suyo: aquí no hay ningún
   nodo nuevo, sólo atributos, que es lo único que Blazor deja en paz. */
(() => {
  /* Cómo se entera de que una región entró al DOM. No hay observador sobre document.body: la
     región trae una animación de un centésimo de segundo que no pinta nada, y animationstart sube
     hasta el documento. Sirve igual para una carga completa, para una navegación mejorada y para
     el enrutador de Blazor, que no emite ningún evento propio. */
  const ARMA = 'uc-live-arma';

  const VALOR = '[data-live-value]';
  const TEXTO = '[data-live-text]';
  const CAMBIO = '[data-live-swap]';

  /* Los mismos nombres que declara app.css. Se leen de la región, no se copian aquí, para que la
     jerarquía —valor < estado < crítico— tenga un solo sitio donde vivir. */
  const TOKENS = ['--motion-live-valor', '--motion-live-estado', '--motion-live-critico'];
  const RESPALDO = [200, 320, 520];

  /* Quien pide menos movimiento, y quien navega con ahorro de datos, reciben el mismo estado sin
     un solo viaje: el valor nuevo ya está en el DOM y la etapa sigue cambiando de superficie. */
  const quieto = () => {
    try {
      return matchMedia('(prefers-reduced-motion: reduce)').matches
        || Boolean(navigator.connection?.saveData);
    } catch { return false; }
  };

  const milis = (valor, respaldo) => {
    const texto = (valor || '').trim();
    if (texto.endsWith('ms')) return parseFloat(texto) || respaldo;
    if (texto.endsWith('s')) return (parseFloat(texto) || 0) * 1000 || respaldo;
    return respaldo;
  };

  /* Lo que el elemento dice hoy. Se compara contra lo último visto y no contra lo que llegó por la
     red: un repintado que no cambia nada no es una noticia, y la hidratación vuelve a escribir el
     mismo número en cada arranque. */
  const leer = elemento => (elemento.textContent || '').trim();

  const entero = texto => {
    const n = parseInt(String(texto).replace(/[^\d-]/g, ''), 10);
    return Number.isFinite(n) ? n : null;
  };

  /* Una sola forma de encender cualquier animación de este archivo, y una sola de apagarla. Quitar
     el atributo, forzar el reflujo y volver a ponerlo es lo que la reinicia cuando el estado nuevo
     llega antes de que termine el anterior: no hay cola, manda el último. */
  const relojes = new WeakMap();

  const apagarMasTarde = (elemento, duracion) => {
    clearTimeout(relojes.get(elemento));
    relojes.set(elemento, setTimeout(() => apagar(elemento), duracion + 400));
  };

  const encender = (elemento, duracion) => {
    elemento.removeAttribute('data-uc-live-anim');
    void elemento.offsetWidth;
    elemento.setAttribute('data-uc-live-anim', '1');
    apagarMasTarde(elemento, duracion);
  };

  const apagar = elemento => {
    clearTimeout(relojes.get(elemento));
    relojes.delete(elemento);
    elemento.removeAttribute('data-uc-live-anim');
    elemento.removeAttribute('data-uc-live-antes');
    elemento.removeAttribute('data-uc-live-delta');
  };

  const contar = elemento => {
    const n = parseInt(elemento.getAttribute('data-uc-live-n') || '0', 10) + 1;
    elemento.setAttribute('data-uc-live-n', String(n));
  };

  /* UNA CIFRA QUE CAMBIA. El valor viejo sale y el nuevo entra desde el lado contrario, de manera
     que la dirección se lee sin tener que recordar el número anterior. Bajar es avanzar en una
     fila; subir es que entró alguien. No es una ruleta: es un solo viaje de media letra. */
  function cifra(elemento, tiempos) {
    const ahora = leer(elemento);
    const antes = elemento.dataset.ucLiveVisto;
    if (antes === ahora) return;
    elemento.dataset.ucLiveVisto = ahora;
    // Primer pintado: es el punto de partida, no un cambio. Nada que anunciar.
    if (antes === undefined) return;

    const a = entero(antes);
    const b = entero(ahora);
    const sentido = a === null || b === null || a === b ? '' : (b < a ? 'baja' : 'sube');
    contar(elemento);
    if (sentido) elemento.setAttribute('data-uc-live-sentido', sentido);
    else elemento.removeAttribute('data-uc-live-sentido');
    if (quieto()) { apagar(elemento); return; }

    apagar(elemento);
    elemento.setAttribute('data-uc-live-antes', antes);
    /* El acento sólo va donde la magnitud significa algo para quien mira: cuántos turnos se fueron
       delante de ti. En el tablero del negocio ninguna de las tres cifras es tuya, y un "−1"
       flotando sobre cada una sería adorno. */
    const acento = sentido && elemento.hasAttribute('data-live-delta');
    if (acento) {
      const salto = b - a;
      elemento.setAttribute('data-uc-live-delta', salto > 0 ? '+' + salto : '−' + Math.abs(salto));
    }
    // El acento dura más que el viaje de la cifra —hay que poder leerlo—, así que el reloj de
    // seguridad se mide contra lo más largo que se encendió y no contra lo primero que termina.
    encender(elemento, acento ? tiempos.valor * 3 : tiempos.valor);
  }

  /* UNA FRASE QUE CAMBIA. "Faltan 3 turnos" y "Eres el siguiente" no son el mismo dato con otro
     número: son dos cosas distintas que hacer. Se reemplaza el contenido, no se desliza. */
  function frase(elemento, tiempos) {
    const ahora = leer(elemento);
    const antes = elemento.dataset.ucLiveVisto;
    if (antes === ahora) return;
    elemento.dataset.ucLiveVisto = ahora;
    if (antes === undefined) return;
    contar(elemento);
    if (quieto()) { apagar(elemento); return; }
    encender(elemento, tiempos.estado);
  }

  /* EL MISMO SITIO CON OTRO PROPÓSITO. "Tomar turno" y "Estás en la fila" ocupan el mismo hueco de
     la pantalla y son el mismo momento del recorrido. Se anima el contenedor, no dos bloques
     distintos que aparecen y desaparecen por su cuenta. */
  function relevo(elemento, tiempos) {
    const ahora = elemento.getAttribute('data-live-clave') || '';
    const antes = elemento.dataset.ucLiveVisto;
    if (antes === ahora) return;
    elemento.dataset.ucLiveVisto = ahora;
    if (antes === undefined) return;
    contar(elemento);
    if (quieto()) { apagar(elemento); return; }
    encender(elemento, tiempos.estado);
  }

  /* LA ETAPA. Una sola lectura decide qué es esto ahora —espera, siguiente, llamado, atención,
     cerrado— y de ahí cuelgan la superficie, el recorte y el peso del titular. La hoja de estilos
     pinta el estado en reposo; aquí sólo se marca el instante del salto y su tamaño: llegar a "es
     tu turno" no puede pesar lo mismo que pasar de 3 a 2. */
  function etapa(region, tiempos) {
    const ahora = region.getAttribute('data-live-etapa') || '';
    const antes = region.dataset.ucLiveEtapa;
    if (antes === ahora) return;
    region.dataset.ucLiveEtapa = ahora;
    if (antes === undefined) return;
    contar(region);
    clearTimeout(relojes.get(region));
    relojes.delete(region);
    if (quieto()) { region.removeAttribute('data-uc-live-paso'); return; }

    const paso = ahora === 'llamado' ? 'climax' : ahora === 'siguiente' ? 'eleva' : 'cambia';
    const duracion = paso === 'climax' ? tiempos.critico : tiempos.estado;
    region.removeAttribute('data-uc-live-paso');
    void region.offsetWidth;
    region.setAttribute('data-uc-live-paso', paso);
    clearTimeout(relojes.get(region));
    relojes.set(region, setTimeout(() => {
      clearTimeout(relojes.get(region));
      relojes.delete(region);
      region.removeAttribute('data-uc-live-paso');
    }, duracion + 400));
  }

  function revisar(region, tiempos) {
    if (!region.isConnected) return;
    region.toggleAttribute('data-uc-live-quieto', quieto());
    etapa(region, tiempos);
    if (region.matches(CAMBIO)) relevo(region, tiempos);
    for (const elemento of region.querySelectorAll(VALOR)) cifra(elemento, tiempos);
    for (const elemento of region.querySelectorAll(TEXTO)) frase(elemento, tiempos);
    for (const elemento of region.querySelectorAll(CAMBIO)) relevo(elemento, tiempos);
  }

  function armar(region) {
    if (region.dataset.ucLive) return;
    region.dataset.ucLive = '1';
    const estilo = getComputedStyle(region);
    const [valor, estado, critico] = TOKENS.map((nombre, i) =>
      milis(estilo.getPropertyValue(nombre), RESPALDO[i]));
    const tiempos = { valor, estado, critico };
    /* Se observa la región y no el documento: fuera de la fila no hay nada vivo que mirar, y un
       observador sobre body se despierta en cada render de cualquier pantalla del sitio. El filtro
       de atributos deja fuera todo lo que escribe este archivo, así que apuntar el cambio no
       provoca otra vuelta. */
    new MutationObserver(() => revisar(region, tiempos)).observe(region, {
      subtree: true, childList: true, characterData: true,
      attributes: true,
      attributeFilter: ['data-live-etapa', 'data-live-clave', 'data-estado-turno', 'data-estado']
    });
    // Línea base: se apunta lo que hay sin anunciar nada. Todavía no ha cambiado nada.
    revisar(region, tiempos);
  }

  document.addEventListener('animationstart', evento => {
    if (evento.animationName !== ARMA) return;
    if (evento.target instanceof Element) armar(evento.target);
  }, true);

  /* Terminó de contarlo: el valor viejo y el acento se retiran en cuanto la animación acaba, no
     cuando vence el reloj de seguridad. Un adorno que sobrevive a su animación es información
     falsa sobre el estado actual. */
  document.addEventListener('animationend', evento => {
    const objetivo = evento.target;
    if (!(objetivo instanceof Element)) return;
    if (evento.animationName === ARMA || !evento.animationName.startsWith('uc-live-')) return;
    if (objetivo.hasAttribute('data-uc-live-paso')) {
      clearTimeout(relojes.get(objetivo));
      relojes.delete(objetivo);
      objetivo.removeAttribute('data-uc-live-paso');
      return;
    }
    // El viaje y su fantasma terminan a los 200 ms; el acento necesita sus 600 ms completos.
    if (objetivo.hasAttribute('data-uc-live-delta') && evento.animationName !== 'uc-live-acento') {
      objetivo.removeAttribute('data-uc-live-antes');
      return;
    }
    apagar(objetivo);
  }, true);

  // La primera animación puede haber arrancado antes de descargar este script diferido.
  const presentes = () => document.querySelectorAll('[data-live-state]').forEach(armar);
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', presentes, { once: true });
  else presentes();

  const preferencia = () => document.querySelectorAll('[data-live-state]').forEach(region => {
    region.toggleAttribute('data-uc-live-quieto', quieto());
    if (!quieto()) return;
    for (const elemento of [region, ...region.querySelectorAll('[data-uc-live-anim]')]) {
      apagar(elemento);
      elemento.removeAttribute('data-uc-live-paso');
    }
  });
  matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change', preferencia);
  navigator.connection?.addEventListener('change', preferencia);
})();
