(() => {
  const root = document.documentElement;
  root.dataset.assetsReady = 'false';
  let preference = {};
  try { preference = JSON.parse(localStorage.getItem('codeusagemonit-appearance') || '{}') || {}; } catch {}
  const theme = ['system', 'light', 'dark'].includes(preference.theme) ? preference.theme : 'system';
  root.dataset.theme = theme;
  root.dataset.resolvedTheme = theme === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : theme;
  root.dataset.accent = ['blue', 'teal', 'violet', 'orange'].includes(preference.accent) ? preference.accent : 'blue';
})();
