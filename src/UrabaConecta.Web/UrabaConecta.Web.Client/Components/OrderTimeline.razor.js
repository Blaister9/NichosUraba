/* EL HITO QUE ACABA DE ENTRAR.

   Decoración sobre el último render, nunca una cola de cambios. Blazor ya pintó la historia
   completa y en orden; esto sólo hace visible cuál de esos hitos es nuevo. No hay observador del
   documento, ni clonado de nodos, ni callback por cuadro, ni temporizador que reintente.

   POR QUÉ SE ANIMA LA ALTURA. La lista crece por definición, y lo que hay debajo —el aviso, el
   botón de cancelar— tiene que ceder el sitio. Si el hito aparece a tamaño completo de golpe, eso
   es un salto. Animar su altura de cero a la suya convierte el salto en un desplazamiento
   intencionado y acotado a un solo elemento pequeño, de una sola pasada.

   POR QUÉ NO SE TOCA EL SCROLL. Quien está leyendo arriba no pierde su sitio: llega un hito nuevo
   y la página no se mueve sola. Si el hito queda fuera de la vista, se enterará al bajar, que es
   donde la historia lo está esperando. */

const instancias = new WeakMap();
const curva = 'cubic-bezier(.2, .9, .3, 1)';

/* Los mismos nombres que declara app.css: la jerarquía —asentarse < entrar < escalar— tiene un
   solo sitio donde vivir, y es la hoja de estilos. */
const TOKENS = ['--motion-history-settle', '--motion-history-insert', '--motion-status-escalate'];
const RESPALDO = [220, 340, 500];

function instancia(raiz) {
    let estado = instancias.get(raiz);
    if (estado) return estado;
    const reduce = matchMedia('(prefers-reduced-motion: reduce)');
    const connection = navigator.connection;
    estado = { reduce, connection, hechos: new WeakSet(), vivas: new Map() };
    /* Quien pide menos movimiento y quien navega con ahorro de datos reciben la misma historia sin
       un solo viaje: los hitos ya están en el DOM, con su marca y su hora. */
    estado.quieto = () => estado.reduce.matches || Boolean(estado.connection?.saveData);
    instancias.set(raiz, estado);
    return estado;
}

function duracion(raiz, indice) {
    const leido = getComputedStyle(raiz).getPropertyValue(TOKENS[indice]).trim();
    if (leido.endsWith('ms')) return parseFloat(leido);
    if (leido.endsWith('s')) return parseFloat(leido) * 1000;
    return RESPALDO[indice];
}

/* Un segundo hito puede llegar mientras el primero todavía viaja. No se encola ni se espera: la
   animación anterior de ESE elemento se termina en seco y la nueva sale del estado actual. Ningún
   hecho se pierde por eso, porque los hechos son filas del servidor y ya están pintados. */
function rebasar(estado, elemento) {
    const previa = estado.vivas.get(elemento);
    if (!previa) return;
    try { previa.finish(); } catch { previa.cancel(); }
    estado.vivas.delete(elemento);
}

function animar(estado, raiz, elemento) {
    if (!elemento.animate) return;
    rebasar(estado, elemento);
    const alto = elemento.getBoundingClientRect().height;
    if (!alto) return;
    const viaje = elemento.animate([
        { height: '0px', opacity: 0, transform: 'translateY(-6px)' },
        { height: `${alto}px`, opacity: 1, transform: 'none' }
    ], { duration: duracion(raiz, 1), easing: curva, fill: 'none' });
    estado.vivas.set(elemento, viaje);
    viaje.finished
        .then(() => { if (estado.vivas.get(elemento) === viaje) estado.vivas.delete(elemento); })
        .catch(() => { /* cancelada al desmontar: no hay nada que limpiar */ });

    /* "Listo para recoger" es el único hito que pide algo de la persona. Gana peso una vez y se
       queda quieto: sin pulso infinito, sin alarma. */
    if (elemento.dataset.timelineEscalate !== 'true') return;
    const marca = elemento.querySelector('.hito-marca');
    marca?.animate([
        { transform: 'scale(1)' }, { transform: 'scale(1.35)' }, { transform: 'scale(1)' }
    ], { duration: duracion(raiz, 2), easing: curva, fill: 'none' });
}

export function sync(raiz) {
    if (!raiz) return;
    const estado = instancia(raiz);
    for (const elemento of raiz.querySelectorAll('[data-timeline-new="true"]')) {
        // Un render posterior por cualquier otro motivo no vuelve a estrenar el mismo hito.
        if (estado.hechos.has(elemento)) continue;
        estado.hechos.add(elemento);
        if (estado.quieto()) continue;
        animar(estado, raiz, elemento);
    }
}

export function dispose(raiz) {
    const estado = instancias.get(raiz);
    if (!estado) return;
    for (const viaje of estado.vivas.values()) viaje.cancel();
    estado.vivas.clear();
    instancias.delete(raiz);
}
