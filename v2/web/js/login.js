import { icon } from './icons.js';

const root = document.documentElement;
const dark = matchMedia('(prefers-color-scheme: dark)');
const contrast = matchMedia('(forced-colors: active), (prefers-contrast: more)');
const applyTheme = () => { root.dataset.theme = contrast.matches ? 'contrast' : dark.matches ? 'dark' : 'light'; };
[dark, contrast].forEach((query) => query.addEventListener('change', applyTheme));
applyTheme();
document.querySelectorAll('[data-icon]').forEach((node) => { node.innerHTML = icon(node.dataset.icon); });
const open = document.querySelector('[data-open]');
const close = document.querySelector('[data-close]');
const message = document.querySelector('[data-message]');
const channel = window.chrome?.webview;
let ready = false;
let pending = false;
const send = (action) => channel?.postMessage({ type: 'filpro.startup', action });
let layoutFrame = 0;
const requestLayout = () => {
  if (layoutFrame) return;
  layoutFrame = requestAnimationFrame(() => { layoutFrame = 0; send('layout'); });
};
const layoutObserver = new ResizeObserver(requestLayout);
document.querySelectorAll('.entry-access-inner, .entry-access-footer').forEach((node) => layoutObserver.observe(node));
window.addEventListener('resize', requestLayout);
const busy = (value) => {
  pending = value;
  open.disabled = value || !ready;
  close.disabled = value;
  document.querySelector('[data-open-label]').textContent = value ? 'Ouverture en cours…' : 'Ouvrir FILPRO';
};
channel?.addEventListener('message', (event) => {
  const result = event.data;
  if (result?.type !== 'filpro.startup') return;
  if (result.state === 'ready' && typeof result.account === 'string' && typeof result.elevated === 'boolean') {
    ready = true;
    document.querySelector('[data-account]').textContent = result.account;
    document.querySelector('[data-access]').textContent = result.elevated ? 'Droits administrateur actifs' : 'Session Windows ouverte';
    document.querySelector('[data-explanation]').textContent = result.elevated
      ? 'Les droits administrateur sont déjà actifs. Votre espace est prêt à s’ouvrir.'
      : 'Confirmez la demande Windows pour ouvrir FILPRO avec les droits administrateur.';
    document.querySelector('[data-version]').textContent = result.version ? `FILPRO ${result.version}` : 'FILPRO';
    busy(false);
    open.focus({ preventScroll: true });
  } else if (result.state === 'opening') {
    busy(true); message.dataset.error = 'false'; message.textContent = 'Ouverture de votre espace…';
  } else if (result.state === 'cancelled' || result.state === 'error') {
    busy(false); message.dataset.error = String(result.state === 'error');
    message.textContent = result.message ?? 'L’ouverture a été interrompue.';
    open.focus({ preventScroll: true });
  }
  requestLayout();
});
open.addEventListener('click', () => {
  if (!ready || pending) return;
  busy(true); message.dataset.error = 'false'; message.textContent = 'En attente de l’autorisation Windows…'; send('open');
});
close.addEventListener('click', () => { if (!pending) send('close'); });
if (channel) send('info');
else {
  document.querySelector('[data-account]').textContent = 'Aperçu de l’interface';
  document.querySelector('[data-access]').textContent = 'Compte lu uniquement dans l’application Windows';
  document.querySelector('[data-explanation]').textContent = 'Ouvrez FILPRO.exe pour utiliser votre session Windows.';
  close.hidden = true;
}
