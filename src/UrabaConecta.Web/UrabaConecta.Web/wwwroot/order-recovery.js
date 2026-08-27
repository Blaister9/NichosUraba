(() => {
  const KEY = 'urabaLastPickupOrder';
  const valid = value => typeof value === 'string' && value.length >= 20 && value.length <= 128;

  window.urabaOrderRecovery = {
    save: trackingCode => {
      if (!valid(trackingCode)) return false;
      try { localStorage.setItem(KEY, trackingCode); return true; }
      catch { return false; }
    },
    load: () => {
      try {
        const trackingCode = localStorage.getItem(KEY);
        return valid(trackingCode) ? trackingCode : null;
      } catch { return null; }
    },
    clear: () => {
      try { localStorage.removeItem(KEY); } catch { }
    }
  };
})();
