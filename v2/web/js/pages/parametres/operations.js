// Paramètres › Sauvegarde et restauration : options cochées d'office dans les plans, traitement des doublons à la
// restauration et suivi des opérations. Les changements forment un brouillon de la page (gardé d'une section à l'autre),
// enregistré d'un seul geste par « Enregistrer les réglages », avec le contrôle de fraîcheur de la configuration.
import { esc, glyph, card, row, toggle, notes, sectionHead, paint, radioKeys, pressRadio, button } from './common.js';
import { reveal } from '../../ui.js';

const POLICIES = [
  { value: 'keepBoth', label: 'Conserver les deux fichiers', sub: 'Le fichier restauré s’ajoute à côté de l’actuel. Pour le registre, les valeurs actuelles sont gardées : deux valeurs ne peuvent pas occuper la même entrée.', icon: 'copy' },
  { value: 'keepCurrent', label: 'Garder l’actuel', sub: 'Ce qui existe déjà sur le profil cible n’est pas remplacé.', icon: 'shield' },
  { value: 'useSaved', label: 'Rétablir le sauvegardé', sub: 'Les fichiers et valeurs de la sauvegarde remplacent ceux du profil cible.', icon: 'refresh' },
];
const BACKUP = [
  ['encrypted', 'Chiffrement', 'Proposé coché dans le plan de chaque sauvegarde. Le plan peut toujours le changer avant la confirmation.'],
  ['vss', 'Instantané VSS', 'Proposé coché : les fichiers ouverts sont lus depuis un cliché du volume.'],
  ['continueOnError', 'Continuer après une erreur', 'Proposé coché : une erreur de lecture n’arrête pas la sauvegarde. Des éléments peuvent alors manquer ; le plan et le bilan le signalent.'],
  ['recommendedSelection', 'Réglages recommandés', 'Les logiciels recommandés sont sélectionnés pour un profil qui n’a ni sélection mémorisée ni choix commun.'],
];
const FOLLOW = [
  ['autoSelectCurrentTask', 'Sélectionner la tâche en cours', 'Après le lancement d’une opération, Suivi s’ouvre sur elle.'],
  ['notifyOnCompletion', 'Notification de fin', 'Une notification apparaît quand une opération se termine, même depuis un autre écran.'],
];

function policyCards(value) {
  return `<div class="pr-choices" role="radiogroup" aria-label="Doublons à la restauration" data-setting="conflictPolicy">${POLICIES.map((o) => {
    const on = o.value === value;
    return `<button type="button" class="pr-choice" role="radio" aria-checked="${on}" tabindex="${on ? 0 : -1}" data-value="${esc(o.value)}">
      <span class="pr-choice-ico" aria-hidden="true">${glyph(o.icon)}</span><span class="pr-choice-text"><b>${esc(o.label)}</b><span>${esc(o.sub)}</span></span>
      <span class="pr-theme-check" aria-hidden="true">${glyph('check')}</span></button>`;
  }).join('')}</div>`;
}

function markup(s) {
  const rows = (items) => items.map(([key, label, desc]) => row({ label, desc, control: toggle(key, label, s[key] === true), setting: key })).join('');
  return `${sectionHead({ title: 'Sauvegarde et restauration', lead: 'Les options proposées d’office dans les plans, le traitement des doublons et le suivi des opérations. Chaque plan reste modifiable avant sa confirmation.', icon: 'disk', tone: 'ok' })}
    <div class="pr-grid">
      ${card({ id: 'backup', title: 'Options proposées pour une sauvegarde', sub: 'Valeurs cochées d’office dans le plan', iconName: 'disk', toneName: 'ok', body: rows(BACKUP) })}
      ${card({ id: 'follow', title: 'Suivi des opérations', sub: 'Pendant et après une opération', iconName: 'activity', body: rows(FOLLOW) })}
      ${card({ id: 'policy', title: 'Doublons à la restauration', sub: 'Choix proposé pour chaque groupe dans Restaurer, après la lecture du magasin', iconName: 'layers', cls: 'pr-wide', body: policyCards(s.conflictPolicy ?? 'keepBoth') })}
      <div class="pr-savebar pr-wide" data-savebar>
        <span class="pr-savebar-text" data-dirty>Aucune modification en attente.</span>
        ${button('discard', 'Annuler les modifications', 'undo', { kind: 'ghost' })}
        <button class="btn primary" type="button" data-save>${glyph('check')}<span>Enregistrer les réglages</span></button>
      </div>
      <div class="pr-footnote reveal">${notes([
        'Ces réglages sont enregistrés dans les réglages de FILPRO de ce poste, avec une copie préalable et un contrôle de fraîcheur.',
        'Ils font partie de la configuration qui peut être définie par défaut pour le dossier, exportée ou importée.',
      ])}</div>
    </div>`;
}

export function mount(host, sctx) {
  const { st } = sctx;
  const values = () => ({ ...(st.config?.settings ?? {}), ...(st.draft ?? {}) });
  host.innerHTML = markup(values());
  paint(host);
  const stopReveal = reveal(host);

  const set = (key, value) => { st.draft = { ...(st.draft ?? {}), [key]: value }; sctx.emit(); };
  const sync = () => {
    const s = values();
    const off = st.busy || !st.config;
    host.querySelectorAll('[data-toggle]').forEach((input) => { input.checked = s[input.dataset.toggle] === true; input.disabled = off; });
    const choices = host.querySelector('.pr-choices');
    pressRadio(choices, s.conflictPolicy ?? 'keepBoth');
    choices.querySelectorAll('[role="radio"]').forEach((b) => { b.disabled = off; });
    const dirty = sctx.dirty();
    host.querySelector('[data-savebar]').classList.toggle('on', dirty);
    host.querySelector('[data-dirty]').textContent = dirty ? 'Modifications non enregistrées.' : 'Aucune modification en attente.';
    host.querySelector('[data-save]').disabled = off || !dirty;
    host.querySelector('[data-act="discard"]').disabled = off || !dirty;
  };
  const onChange = (e) => {
    const input = e.target instanceof HTMLInputElement ? e.target : null;
    if (input?.dataset.toggle) set(input.dataset.toggle, input.checked);
  };
  const onClick = (e) => {
    const t = e.target instanceof Element ? e.target : null;
    const choice = t?.closest('.pr-choice');
    if (choice && !choice.disabled) { set('conflictPolicy', choice.dataset.value); return; }
    if (t?.closest('[data-save]')) sctx.run(() => sctx.saveDraft());
    else if (t?.closest('[data-act="discard"]')) { st.draft = null; sctx.feedback(''); sctx.emit(); }
  };
  const onKey = (e) => { radioKeys(e); };
  host.addEventListener('change', onChange);
  host.addEventListener('click', onClick);
  host.addEventListener('keydown', onKey);
  const off = sctx.on(sync);
  sync();

  return {
    destroy() {
      off(); stopReveal();
      host.removeEventListener('change', onChange);
      host.removeEventListener('click', onClick);
      host.removeEventListener('keydown', onKey);
    },
  };
}
