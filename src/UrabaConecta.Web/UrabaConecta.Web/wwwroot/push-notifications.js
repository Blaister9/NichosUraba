(() => {
  const decodeKey = value => {
    const padding = '='.repeat((4 - value.length % 4) % 4);
    const base64 = (value + padding).replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(base64), char => char.charCodeAt(0));
  };

  const followedScopes = () => {
    try { return JSON.parse(localStorage.getItem('urabaPushScopes') || '[]'); }
    catch { return []; }
  };
  const rememberScope = (path, active) => {
    const scopes = new Set(followedScopes());
    if (active) scopes.add(path); else scopes.delete(path);
    localStorage.setItem('urabaPushScopes', JSON.stringify([...scopes]));
  };

  const configuration = async registrationPath => {
    if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window))
      return { supported: false, configured: false, permission: 'unsupported' };
    try {
      const response = await fetch('/api/v1/public/push/config', { credentials: 'same-origin' });
      const config = response.ok ? await response.json() : { enabled: false };
      return { supported: true, configured: Boolean(config.enabled), publicKey: config.publicKey,
        permission: Notification.permission, active: registrationPath ? followedScopes().includes(registrationPath) : false };
    } catch {
      return { supported: true, configured: false, permission: Notification.permission };
    }
  };

  const subscriptionBody = subscription => {
    const json = subscription.toJSON();
    return { endpoint: json.endpoint, keys: { p256dh: json.keys?.p256dh, auth: json.keys?.auth } };
  };

  window.urabaPush = {
    availability: configuration,
    subscribe: async registrationPath => {
      const config = await configuration();
      if (!config.supported || !config.configured) throw new Error('Los avisos no están disponibles.');
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') return { active: false, permission };
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription() ||
        await registration.pushManager.subscribe({ userVisibleOnly: true,
          applicationServerKey: decodeKey(config.publicKey) });
      const response = await fetch(registrationPath, { method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(subscriptionBody(subscription)) });
      if (!response.ok) throw new Error('No pudimos guardar los avisos en este dispositivo.');
      rememberScope(registrationPath, true);
      return { active: true, permission };
    },
    unsubscribe: async registrationPath => {
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();
      if (subscription) {
        const response = await fetch(`${registrationPath}/remove`, { method: 'POST',
          credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ endpoint: subscription.endpoint }) });
        if (!response.ok) throw new Error('No pudimos desactivar los avisos.');
      }
      rememberScope(registrationPath, false);
      return { active: false, permission: Notification.permission };
    }
  };
})();
