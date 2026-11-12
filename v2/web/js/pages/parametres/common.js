// Paramètres : pièces communes des sections (balisage, typographie française). Reprend les pièces de l'écran Paramètres de
// Domenia 2 pour que les deux applications se présentent de la même façon. Toute valeur venant de l'hôte passe par esc() ;
// les variables CSS passent par le CSSOM (CSP : aucun attribut style).
import { glyph } from '../../kit/glyphs.js';
import { esc, applyVars } from '../../ui.js';

export { esc, glyph };

/** Espace insécable (avant « : », « ; », « ? », « ! », « % », à l'intérieur des guillemets). */
export const NBSP = ' ';
export const COLON = `${NBSP}: `;

/** En-tête d'une section, dans la colonne de contenu. */
export function sectionHead(s, actions = '') {
  return `<header class="pr-sec-head reveal">
    <span class="pr-sec-icon" data-tone="${esc(s.tone ?? 'accent')}">${glyph(s.icon)}</span>
    <div class="pr-sec-titles"><h2>${esc(s.title)}</h2><p>${esc(s.lead)}</p></div>
    <div class="pr-sec-actions">${actions}</div>
  </header>`;
}

/** Carte d'une section : icône, titre, sous-titre, actions d'en-tête ; corps en balisage de confiance. */
export function card({ id = '', title, sub = '', iconName = 'settings', actions = '', body = '', cls = '', toneName = '' }) {
  return `<section class="card pr-card spot reveal ${esc(cls)}"${id ? ` data-card="${esc(id)}"` : ''}>
    <div class="pr-card-head"><span class="pr-card-icon"${toneName ? ` data-tone="${esc(toneName)}"` : ''}>${glyph(iconName)}</span>
      <div class="pr-card-titles"><h3>${esc(title)}</h3>${sub ? `<span class="pr-card-sub">${esc(sub)}</span>` : ''}</div>
      ${actions ? `<div class="pr-card-actions">${actions}</div>` : ''}</div>
    <div class="pr-card-body">${body}</div>
  </section>`;
}

/** Ligne de réglage : libellé et description à gauche, commande à droite. */
export function row({ label, desc = '', control = '', setting = '' }) {
  return `<div class="pr-row"${setting ? ` data-setting="${esc(setting)}"` : ''}>
    <div class="pr-row-text"><span class="pr-row-label">${esc(label)}</span>${desc ? `<span class="pr-row-desc">${esc(desc)}</span>` : ''}</div>
    <div class="pr-row-control">${control}</div>
  </div>`;
}

/** Phrases à puces (règles, garanties). */
export function notes(lines) {
  if (!lines?.length) return '';
  return `<ul class="pr-notes">${lines.map((l, n) => `<li data-j="${n}">${glyph('info')}<span>${esc(l)}</span></li>`).join('')}</ul>`;
}

/** Lignes clé / valeur (dl). */
export function facts(items) {
  return `<dl class="pr-facts">${items.filter(Boolean).map(([k, v, slot]) => `<div class="pr-fact"><dt>${esc(k)}</dt><dd${slot ? ` data-fact="${esc(slot)}"` : ''}>${v === null || v === undefined || v === ''
    ? '<span class="pr-none">—</span>' : esc(v)}</dd></div>`).join('')}</dl>`;
}

/**
 * Choix segmenté (groupe de boutons radio) sur le contrôle .segmented du kit. Un seul arrêt de tabulation (le bouton coché) ;
 * les flèches déplacent et choisissent (radioKeys), comme le motif « radio group » de l'APG.
 */
export function segmented(name, label, options, value) {
  const any = options.some((o) => String(o.value) === String(value));
  return `<div class="segmented pr-seg" role="radiogroup" aria-label="${esc(label)}" data-seg="${esc(name)}"><span class="thumb" aria-hidden="true"></span>${options.map((o, n) => {
    const on = String(o.value) === String(value);
    return `<button type="button" role="radio" aria-checked="${on}" tabindex="${on || (!any && n === 0) ? 0 : -1}" data-value="${esc(o.value)}"${o.tip ? ` data-tip="${esc(o.tip)}"` : ''}>${o.icon ? glyph(o.icon) : ''}<span>${esc(o.label)}</span></button>`;
  }).join('')}</div>`;
}

/** Clavier des groupes radio : ← → ↑ ↓ passent au suivant et le choisissent, Début et Fin au premier et au dernier. */
export function radioKeys(e) {
  const radio = e.target instanceof Element ? e.target.closest('[role="radio"]') : null;
  const group = radio?.closest('[role="radiogroup"]');
  if (!radio || !group) return false;
  const keys = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };
  if (!(e.key in keys) && e.key !== 'Home' && e.key !== 'End') return false;
  const radios = [...group.querySelectorAll('[role="radio"]')].filter((r) => !r.disabled && r.getAttribute('aria-disabled') !== 'true');
  if (!radios.length) return false;
  e.preventDefault();
  const at = Math.max(0, radios.indexOf(radio));
  const next = e.key === 'Home' ? radios[0] : e.key === 'End' ? radios[radios.length - 1] : radios[(at + keys[e.key] + radios.length) % radios.length];
  next.focus();
  next.click();
  return true;
}

/** Ramène l'unique arrêt de tabulation d'un groupe radio sur son bouton coché. */
export function rovingTabs(group) {
  const radios = [...group.querySelectorAll('[role="radio"]')];
  const checked = radios.find((r) => r.getAttribute('aria-checked') === 'true') ?? radios[0];
  radios.forEach((r) => r.setAttribute('tabindex', r === checked ? '0' : '-1'));
}

/** Coche la valeur d'un groupe radio (segments ou cartes) et déplace le curseur du segment. */
export function pressRadio(group, value) {
  group.querySelectorAll('[role="radio"]').forEach((b) => b.setAttribute('aria-checked', String((b.dataset.value ?? b.dataset.themeChoice) === String(value))));
  rovingTabs(group);
  syncSegments(group.parentElement ?? group);
}

/** Interrupteur (case à cocher role="switch"). */
export function toggle(name, label, checked) {
  return `<label class="pr-switch"><input class="switch" type="checkbox" role="switch" data-toggle="${esc(name)}" aria-label="${esc(label)}"${checked ? ' checked' : ''}></label>`;
}

/** Bouton avec icône ; data-act porte l'action. kind : '' | 'primary' | 'ghost'. */
export function button(act, label, iconName, { kind = '', extra = '' } = {}) {
  const cls = ['btn', kind].filter(Boolean).join(' ');
  return `<button type="button" class="${cls}" data-act="${esc(act)}"${extra}>${iconName ? glyph(iconName) : ''}<span>${esc(label)}</span></button>`;
}

/** Garde le curseur de chaque .pr-seg d'une portée sous son bouton coché (transform seulement). */
export function syncSegments(scope) {
  for (const seg of scope.querySelectorAll('.pr-seg')) {
    const thumb = seg.querySelector(':scope > .thumb');
    const on = seg.querySelector('button[aria-checked="true"]');
    if (!thumb) continue;
    if (!on) { thumb.style.setProperty('opacity', '0'); continue; }
    thumb.style.removeProperty('opacity');
    thumb.style.setProperty('--x', `${on.offsetLeft}px`);
    thumb.style.setProperty('--w', `${on.offsetWidth}px`);
  }
}

/** Peint les variables data-* d'une portée (tons, délais) par le CSSOM. */
export function paint(scope) {
  applyVars(scope);
  syncSegments(scope);
}
