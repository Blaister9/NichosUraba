// Motion is decoration over the latest render, never a queue of product changes.
// No document observer, image clone, frame callback, polling or delayed domain update.
const instances = new WeakMap();
const easing = 'cubic-bezier(.2,.8,.2,1)';

function instance(root) {
    let state = instances.get(root);
    if (state) return state;
    const reduce = matchMedia('(prefers-reduced-motion: reduce)');
    const connection = navigator.connection;
    state = { active: null, stage: null, values: new WeakMap(), quantities: new Map(), animations: new Map(), reduce, connection };
    state.stop = () => {
        for (const animation of state.animations.values()) animation.cancel();
        state.animations.clear();
    };
    state.preference = () => { if (reduce.matches || connection?.saveData) state.stop(); };
    state.keyboard = event => {
        if (event.key === ' ' && event.target.matches('.product-cta[role=button][aria-disabled=false]')) {
            event.preventDefault();
            event.target.click();
        }
    };
    root.addEventListener('keydown', state.keyboard);
    reduce.addEventListener('change', state.preference);
    connection?.addEventListener('change', state.preference);
    instances.set(root, state);
    return state;
}

function animate(state, element, frames, duration) {
    if (!element) return;
    state.animations.get(element)?.cancel();
    state.animations.delete(element);
    if (state.reduce.matches || state.connection?.saveData || !element.animate) return;
    const animation = element.animate(frames, { duration, easing, fill: 'none' });
    state.animations.set(element, animation);
    const finish = () => {
        if (state.animations.get(element) === animation) state.animations.delete(element);
    };
    animation.onfinish = finish;
    animation.oncancel = finish;
}

export function sync(root, focusId) {
    if (!root?.isConnected) return;
    const state = instance(root);
    const active = root.dataset.activeProduct || null;
    const stage = root.dataset.orderStage;
    const card = [...root.querySelectorAll('[data-product-id]')].find(el => el.dataset.productId === active);
    const changedProduct = active !== state.active;
    const confirmed = stage === 'confirmed' && state.stage !== stage;
    if (stage === 'submitting' && state.stage !== stage) {
        // One layout read per submit; reserve the existing surface before the async response.
        const composer = root.querySelector('.order-composer');
        const height = composer?.getBoundingClientRect().height;
        if (height) composer.style.setProperty('--order-reserved-height', `${height}px`);
    }
    if (changedProduct || confirmed) state.stop();
    if (changedProduct && card && stage !== 'confirmed') {
        // The actual media node stays in its card; no second visible image or shared-name collision.
        animate(state, card.querySelector('.catalogo-foto'), [{ transform: 'scale(.97)' }, { transform: 'scale(1)' }], 180);
        animate(state, card.querySelector('.product-dock'), [{ transform: 'translateY(5px)', opacity: .65 }, { transform: 'translateY(0)', opacity: 1 }], 180);
        animate(state, card.querySelector('[data-product-state]'), [{ opacity: .4 }, { opacity: 1 }], 160);
    }
    // Read the complete new values before writing any animation. Stable slots already show them.
    const values = [...root.querySelectorAll('[data-action-value]')].map(el => ({ el, text: el.textContent.trim(), before: state.values.get(el) }));
    const quantity = Number(card?.dataset.quantity || 0);
    const previous = state.quantities.get(active) || 0;
    const offset = quantity < previous ? -4 : 4;
    for (const { el, text, before } of values) {
        if (before !== undefined && before !== text && stage !== 'confirmed') {
            animate(state, el, [{ transform: `translateY(${offset}px)`, opacity: .45 }, { transform: 'translateY(0)', opacity: 1 }], el.closest('.product-cta') ? 160 : 120);
        }
        state.values.set(el, text);
    }
    if (active) state.quantities.set(active, quantity);
    state.active = active;
    state.stage = stage;
    if (confirmed && !focusId) focusId = 'order-heading';
    if (focusId) {
        const target = root.querySelector(`#${CSS.escape(focusId)}`);
        const focus = target?.matches('[data-product-id]') ? target.querySelector('.product-choice') : target;
        if (focus) {
            focus.focus({ preventScroll: true });
            // Instant navigation works with keyboard, reduced motion and a resized virtual keyboard.
            const scrollTarget = focusId === 'order-heading' ? target.closest('.order-composer') : target;
            scrollTarget.scrollIntoView({ block: 'start', behavior: 'instant' });
        }
        if (focusId === 'order-heading' && !confirmed) {
            const line = [...root.querySelectorAll('[data-summary-product]')].find(el => el.dataset.summaryProduct === active);
            animate(state, line || root.querySelector('.resumen-lineas'), [{ transform: 'translateY(5px)', opacity: .5 }, { transform: 'translateY(0)', opacity: 1 }], 180);
        }
    }
    if (confirmed) {
        animate(state, root.querySelector('.order-heading'), [{ transform: 'translateY(6px)', opacity: .4 }, { transform: 'translateY(0)', opacity: 1 }], 200);
        animate(state, root.querySelector('.order-confirmation'), [{ opacity: .4 }, { opacity: 1 }], 200);
    }
}

export function dispose(root) {
    const state = instances.get(root);
    if (!state) return;
    state.stop();
    root.removeEventListener('keydown', state.keyboard);
    state.reduce.removeEventListener('change', state.preference);
    state.connection?.removeEventListener('change', state.preference);
    instances.delete(root);
}
