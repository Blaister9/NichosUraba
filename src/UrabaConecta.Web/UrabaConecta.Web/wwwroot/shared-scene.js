/* LA ESCENA QUE SE ABRE — quién participa en la transición entre documentos y quién no.

   Home y la ficha de un negocio son dos documentos distintos: abrir un negocio desde la escena es
   una navegación completa, a propósito. La API de View Transitions entre documentos permite que la
   escena que se estaba mirando se convierta en el encabezado de la ficha sin inventar una SPA y sin
   retrasar la navegación: el navegador captura las dos pantallas y anima entre ellas mientras la
   carga sigue su curso normal.

   Este guion no anima nada. Sólo decide QUIÉN es el mismo objeto a los dos lados; cómo viaja está
   escrito en el CSS. La regla es una sola: los dos documentos tienen que hablar del mismo negocio.

   POR QUÉ AQUÍ Y SÍNCRONO. El documento que llega se captura en su primer pintado, así que la
   decisión tiene que estar tomada antes: cuando el <head> se está leyendo y el cuerpo todavía no
   existe. Por eso la identidad se saca de la dirección —/negocios/{slug}— y no del DOM, y por eso
   este guion va donde va, igual que theme.js y por la misma razón.

   NO SE USA pagereveal. Ese evento sólo ocurre cuando el documento efectivamente se pinta: en una
   pestaña que nunca se dibuja —un navegador sin cabeza, una vista oculta— no llega nunca, y la
   decisión se quedaría sin tomar en el único sitio donde después no se puede corregir. La captura
   del documento que se va sí tiene su momento propio y fiable, que es pageswap. */
(() => {
  const IDENTIDAD = 'uc_escena_vt';
  /* La ficha del negocio y nada más: /negocios/{slug}. Las pantallas operativas —la fila, los
     horarios, la carta— cuelgan de la misma raíz pero no enseñan el encabezado del negocio, así que
     no hay contenedor que continuar y no entran. */
  const FICHA = /^\/negocios\/([^/?#]+)\/?$/;

  const negocioDe = direccion => {
    try { return new URL(direccion, location.href).pathname.match(FICHA)?.[1] ?? ''; }
    catch { return ''; }
  };
  const guardado = () => { try { return sessionStorage.getItem(IDENTIDAD) || ''; } catch { return ''; } };
  const guardar = valor => { try { sessionStorage.setItem(IDENTIDAD, valor); } catch { /* bloqueado */ } };

  /* Sin movimiento, o con ahorro de datos, se navega y ya está: la continuidad ayuda a entender a
     dónde se fue la pantalla, no es una condición para llegar. Mismo criterio que la escena de Home. */
  const callado = () => {
    try {
      return matchMedia('(prefers-reduced-motion: reduce)').matches
        || navigator.connection?.saveData === true;
    } catch { return false; }
  };

  /* La marca en <html> es la que enciende el nombre de la transición, que vive en el CSS y se aplica
     a un único nodo por documento: el que lleva data-escena-vt. Encenderla y apagarla aquí es lo que
     garantiza que nunca haya dos nodos con el mismo nombre ni un nombre vivo fuera de su momento. */
  const marcar = continua => { document.documentElement.dataset.escena = continua ? 'continua' : 'corta'; };

  /* AL LLEGAR. Continúa si esta pantalla es la ficha del negocio que el documento anterior dejó
     anotado al irse. Con cualquier otro origen no hay nada que continuar y la entrada es limpia. */
  const aqui = negocioDe(location.href);
  marcar(aqui !== '' && aqui === guardado() && !callado());

  /* AL SALIR. Se conserva el nombre sólo si el destino es la ficha del negocio que esta pantalla
     está enseñando. Cualquier otro destino —la fila, la carta, volver a Home, la cabecera— se queda
     sin nombre a los dos lados: sin pareja, una instantánea que se desvanece sola encima de la
     pantalla nueva es peor que un corte, y el corte aquí es instantáneo porque la raíz no se anima.

     Se decide con el destino real de la navegación, no con lo que se tocó: así da igual si el viaje
     lo empezó un dedo, el teclado o el propio guion de la escena. */
  addEventListener('pageswap', evento => {
    const nodo = document.querySelector('[data-escena-vt]');
    const mio = nodo?.dataset.escenaVt || '';
    const continua = mio !== '' && mio === negocioDe(evento.activation?.entry?.url || '') && !callado();
    marcar(continua);
    guardar(continua ? mio : '');
  });
})();
