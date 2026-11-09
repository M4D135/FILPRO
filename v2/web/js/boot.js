// Garde de démarrage, chargée comme script classique dans <head> (CSP : fichier externe, aucun script inline).
// 1. Applique le thème et la motion enregistrés avant la première peinture (mêmes clés et règles que app.js).
// 2. Si l'interface ne démarre jamais (un module échoue à se charger ou à s'analyser), arrête l'animation et le dit.
(() => {
  const root = document.documentElement;
  const THEMES = ['light', 'dark', 'contrast', 'system'];
  const MOTIONS = ['full', 'reduced', 'system'];
  let saved = {};
  try { saved = JSON.parse(localStorage.getItem('filpro.prefs.v1') ?? '{}') ?? {}; } catch { saved = {}; }
  const theme = THEMES.includes(saved.theme) ? saved.theme : 'system';
  const motion = MOTIONS.includes(saved.motion) ? saved.motion : 'full';
  let resolved = theme;
  if (theme === 'system') {
    if (matchMedia('(forced-colors: active), (prefers-contrast: more)').matches) resolved = 'contrast';
    else resolved = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  root.dataset.theme = resolved;
  root.dataset.themePref = theme;
  root.dataset.motion = motion;

  const START_TIMEOUT_MS = 20_000;
  const fail = (text) => {
    const p = document.querySelector('#app > .boot');
    if (!p || p.classList.contains('failed')) return;
    p.classList.add('failed');
    p.setAttribute('role', 'alert');
    p.textContent = text;
  };
  // Phase de capture : les erreurs de chargement de script et de feuille de style ne bullesent pas. N'agit que pendant l'écran de démarrage.
  window.addEventListener('error', (e) => {
    if (!document.querySelector('#app > .boot')) return;
    console.error('Démarrage de l’interface en échec', e.error ?? e.target);
    fail('FILPRO n’a pas pu démarrer : un fichier de l’interface est manquant ou illisible. Fermez puis relancez l’application.');
  }, true);
  setTimeout(() => {
    fail('FILPRO ne démarre pas : l’interface n’a pas répondu. Fermez puis relancez l’application.');
  }, START_TIMEOUT_MS);
})();
