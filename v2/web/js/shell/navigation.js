// Navigation de la coque FILPRO : écrans, thèmes et motion, préférences locales.
// Navigation uniquement : rien ici ne lit le système. Les écrans appellent eux-mêmes le pont.
import { icon } from '../icons.js';
import { esc } from '../ui.js';

// La disponibilité d'une action vient des capacités du service actif, jamais de la présence de son bouton.
export const PAGES = [
  { id: 'accueil', label: 'Accueil', icon: 'home', section: 'Pilotage', keywords: 'profils sessions sauvegardes tâches journal' },
  { id: 'suivi', label: 'Suivi en direct', icon: 'activity', section: 'Pilotage', keywords: 'tâches progression suivi' },
  { id: 'journal', label: 'Journal', icon: 'clock', section: 'Pilotage', keywords: 'historique journal opérations' },
  { id: 'analyser', label: 'Analyser / réparer', icon: 'tools', section: 'Opérations', keywords: 'analyser réparer profil' },
  { id: 'sauvegarder', label: 'Sauvegarder', icon: 'disk', section: 'Opérations', keywords: 'sauvegarder restaurer magasin' },
  { id: 'recuperer', label: 'Récupérer des fichiers', icon: 'folder', section: 'Opérations', keywords: 'récupérer fichiers supprimés' },
  { id: 'parametres', label: 'Paramètres', icon: 'settings', section: null, keywords: 'préférences réglages stockage' },
];

export const THEMES = [
  { id: 'light', label: 'Thème clair', icon: 'sun' },
  { id: 'dark', label: 'Thème sombre', icon: 'moon' },
  { id: 'contrast', label: 'Contraste élevé', icon: 'contrast' },
  { id: 'system', label: 'Thème : suivre Windows', icon: 'monitor' },
];

export const MOTIONS = [
  { id: 'full', label: 'Animations riches', short: 'Riches', icon: 'sparkles' },
  { id: 'reduced', label: 'Animations réduites', short: 'Réduites', icon: 'pause' },
  { id: 'system', label: 'Animations : suivre Windows', short: 'Windows', icon: 'monitor' },
];

// ---------- Préférences locales (localStorage ; jamais de secret, jamais de donnée métier) ----------

const PREFS_KEY = 'filpro.prefs.v1';

export function loadPrefs() {
  let saved = {};
  try { saved = JSON.parse(localStorage.getItem(PREFS_KEY) ?? '{}') ?? {}; } catch { saved = {}; }
  return {
    theme: THEMES.some((t) => t.id === saved.theme) ? saved.theme : 'system',
    motion: MOTIONS.some((m) => m.id === saved.motion) ? saved.motion : 'full',
    navCollapsed: saved.navCollapsed === true,
  };
}

export function savePrefs(prefs) {
  try { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs)); } catch { /* stockage plein ou indisponible : la session suffit */ }
}

export function resolvedTheme(theme) {
  if (theme !== 'system') return theme;
  if (matchMedia('(forced-colors: active), (prefers-contrast: more)').matches) return 'contrast';
  return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

// ---------- Marqueur de la coque ----------

function navItem(page, n) {
  const kbd = n < 9 ? `<kbd class="nav-kbd" aria-hidden="true">Alt ${n + 1}</kbd>` : '';
  const keys = n < 9 ? ` aria-keyshortcuts="Alt+${n + 1}"` : '';
  return `<a class="nav-item" href="#/${page.id}" data-id="${page.id}" aria-label="${esc(page.label)}"${keys} data-tip="${esc(page.label)}">`
    + `${icon(page.icon)}<span class="nav-label">${esc(page.label)}</span>${kbd}</a>`;
}

export function navMarkup() {
  const sections = ['Pilotage', 'Opérations'].map((name) => {
    const items = PAGES.map((p, n) => (p.section === name ? navItem(p, n) : '')).join('');
    return `<div class="nav-section">${esc(name)}</div>${items}`;
  }).join('');
  const bottom = PAGES.map((p, n) => (p.section === null ? navItem(p, n) : '')).join('');
  // Repli du menu TOUT EN BAS, toujours visible (pied collant du rail) : icône et libellé menu déplié, icône seule menu replié.
  return `<nav class="nav" aria-label="Navigation principale">
    <div class="brand" data-tip="FILPRO 2"><span class="brand-mark">${icon('filpro')}</span><span class="brand-name"><span class="brand-title">FILPRO</span><span class="brand-sub">Profils &amp; postes</span></span></div>
    <span class="nav-pill" aria-hidden="true"></span>
    ${sections}
    <div class="nav-spacer"></div>
    ${bottom}
    <div class="nav-foot">
      <button class="nav-item nav-toggle" type="button" aria-expanded="true" aria-label="Replier le menu" aria-keyshortcuts="Control+B" data-tip="Replier le menu (Ctrl B)">
        ${icon('panel')}<span class="nav-label">Replier le menu</span><kbd class="nav-kbd" aria-hidden="true">Ctrl B</kbd>
      </button>
    </div>
  </nav>`;
}

export function topbarMarkup() {
  const themes = THEMES.map((t) => `<button type="button" data-set="${t.id}" aria-pressed="false" aria-label="${esc(t.label)}" data-tip="${esc(t.label)}">${icon(t.icon)}</button>`).join('');
  return `<header class="topbar">
    <span class="mode-chip" hidden><span class="dot"></span><span class="mode-label">—</span></span>
    <span class="topbar-spacer"></span>
    <div class="segmented theme-switch" role="group" aria-label="Thème">${themes}</div>
  </header>`;
}
