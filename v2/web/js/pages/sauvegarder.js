// Sélections par SID, plans conservés par l'hôte puis confirmation avant préparation longue.
// Le moteur réel et la démonstration utilisent des routes de démarrage distinctes ; seul le suivi annonce la fin.
import { call } from '../bridge.js';
import { icon } from '../icons.js';
import { esc, errorText, applyVars, reveal, formatNumber } from '../ui.js';
import { createTabs, openDialog, pill, confirmDialog } from '../kit/index.js';
import { followOperation } from '../operations.js';
import { mountRestore } from '../restore-panel.js';
import { primaryLabel, decisionLine } from '../native-confirmation.js';

const INVARIANTS = [
  'Confirmation explicite avant toute préparation.',
  'Sauvegarde préalable avant toute restauration.',
  'Politiques de conflits par portée (fichiers, registre, modèles communs).',
  'Multi-profils : un profil à la fois, jamais d’écrasement silencieux.',
  'Clés de chiffrement demandées à l’opérateur, jamais journalisées.',
  'Hives du registre déchargées dans un bloc finally.',
  'Reprise et annulation honnêtes : un arrêt demandé n’est pas un arrêt achevé.',
];

const POLICY_LABEL = {
  keepBoth: { tone: 'info', label: 'Coexistence (défaut)' },
  keepCurrent: { tone: 'off', label: 'Garder l’actuel' },
  useSaved: { tone: 'ok', label: 'Rétablir le sauvegardé' },
};

// Le pont renvoie les énumérations en camelCase : la table d'étiquettes est insensible à la casse, pour qu'une décision
// « keepCurrent » ou « useSaved » ne retombe jamais sur l'étiquette de la coexistence par défaut.
const POLICY_BY_KEY = Object.fromEntries(Object.entries(POLICY_LABEL).map(([k, v]) => [k.toLowerCase(), v]));

function policyLabel(mode) {
  return POLICY_BY_KEY[String(mode ?? '').toLowerCase()] ?? POLICY_LABEL.keepBoth;
}

const POLICY_OPTIONS = [
  { value: 'keepBoth', label: 'Coexistence (défaut)' },
  { value: 'keepCurrent', label: 'Garder l’actuel' },
  { value: 'useSaved', label: 'Rétablir le sauvegardé' },
];

const PRIOR_LABEL = {
  identifiee: { tone: 'warn', label: 'identifiée selon l’opérateur (non vérifiée)' },
  declaree: { tone: 'warn', label: 'déclarée par l’opérateur (non contrôlée)' },
  nonapplicable: { tone: 'off', label: 'aucune destination existante' },
  absente: { tone: 'alert', label: 'absente' },
};

function invariantList() {
  return `<ul class="feature-list cascade">${INVARIANTS.map((t, n) => `<li data-j="${n}"><span class="mk" aria-hidden="true"></span><span><span class="t">${esc(t)}</span></span></li>`).join('')}</ul>`;
}

function empty(text) {
  return `<div class="empty"><div class="art">${icon('info')}</div><p>${esc(text)}</p></div>`;
}

function lines(value) {
  return String(value ?? '').split(/[\n,;]+/).map((s) => s.trim()).filter(Boolean);
}

function sizePill(size) {
  const state = String(size?.state).toLowerCase();
  const tone = state === 'mesuree' ? 'ok' : state === 'partielle' ? 'warn' : 'off';
  return pill(tone, size?.text || 'taille inconnue', { size: 'sm' });
}

function loadedPill(loaded) {
  if (loaded === true) return pill('alert', 'chargé', { size: 'sm' });
  if (loaded === false) return pill('ok', 'non chargé', { size: 'sm' });
  return pill('info', 'chargement inconnu', { size: 'sm' });
}

function optionsCard() {
  return `<section class="card reveal spot">
    <div class="card-head"><span class="card-icon">${icon('disk')}</span><h3>Destination et options</h3><span class="meta">local, USB ou partage LAN</span></div>
    <div class="stack">
      <div class="k-field"><label for="bk-dest">Destination du magasin (chemin absolu)</label>
        <div class="actions-row"><input class="input mono" id="bk-dest" type="text" data-dest list="bk-destinations" autocomplete="off" spellcheck="false" placeholder="D:\\Sauvegardes\\FILPRO"><datalist id="bk-destinations"></datalist><button class="btn" type="button" data-pick-destination>Parcourir</button></div></div>
      <label class="k-check-row"><input type="checkbox" class="k-checkbox" data-vss><span class="k-check-text">Utiliser VSS (profils chargés)</span></label>
      <label class="k-check-row"><input type="checkbox" class="k-checkbox" data-enc><span class="k-check-text">Chiffrer le magasin</span></label>
      <label class="k-check-row"><input type="checkbox" class="k-checkbox" data-continue><span class="k-check-text">Continuer après une erreur non bloquante et annoncer un résultat partiel</span></label>
      <div class="k-field" data-keyrow hidden><label for="bk-key">Fichier clé (hors magasin et hors profils sources)</label>
        <input class="input mono" id="bk-key" type="text" data-key autocomplete="off" spellcheck="false" placeholder="D:\\cles\\cle.key"></div>
    </div>
  </section>`;
}

function backupLayout() {
  return `<section class="card reveal spot">
    <div class="card-head"><span class="card-icon">${icon('users')}</span><h3>Profils à sauvegarder</h3><span class="meta" data-meta="profiles" aria-live="polite">—</span></div>
    <p class="muted">Cochez chaque profil à inclure. « Voir les logiciels » affiche ses choix sans modifier les autres profils.</p>
    <div class="list" data-list="profiles"></div>
  </section>
  <section class="card reveal spot">
    <div class="card-head"><span class="card-icon">${icon('object')}</span><h3>Sélection fichiers + registre</h3><span class="meta" data-meta="selection">—</span></div>
    <div data-selection></div>
  </section>
  <section class="card reveal spot">
    <div class="card-head"><span class="card-icon">${icon('shield')}</span><h3>Politiques de conflit (aperçu)</h3><span class="meta">coexistence par défaut</span></div>
    <div data-conflict></div>
  </section>
  ${optionsCard()}
  <p class="callout warn" data-plan-status role="status" hidden></p><div class="actions-row"><button class="btn primary" type="button" data-act="plan">${icon('disk')}Préparer une sauvegarde</button></div>`;
}

function renderProfileList(list, inventory, state) {
  const profiles = inventory?.profiles ?? [];
  if (!profiles.length) { list.innerHTML = empty('Aucun profil lu. ' + (inventory?.sessionError || '')); return; }
  list.innerHTML = profiles.map((p) => `<div class="list-row" data-sid="${esc(p.sid)}">
    <input class="k-checkbox" type="checkbox" data-include-sid="${esc(p.sid)}" aria-label="Inclure ${esc(p.account)} dans la sauvegarde"${state.selectedSids.has(p.sid) ? ' checked' : ''}${String(p.identityKind).toLowerCase() === 'entra' ? ' disabled title="Profil Entra non pris en charge"' : ''}>
    <span class="avatar" aria-hidden="true">${esc((p.account || '?').replace(/^[^\\]*\\/, '').slice(0, 1).toUpperCase())}</span>
    <div class="body"><div class="title">${esc(p.account)}</div><div class="sub mono">${esc(p.sid)}</div>
      ${String(p.identityKind).toLowerCase() === 'entra' ? '<div class="sub">Profil Entra non pris en charge</div>' : ''}</div>
    ${loadedPill(p.loaded)}
    <button class="btn" type="button" data-review-sid="${esc(p.sid)}" aria-label="Voir les logiciels de ${esc(p.account)}">Voir les logiciels</button>
  </div>`).join('');
}

function selectionDraft(inventory, preview) {
  const choices = new Map((inventory?.groups ?? []).map((g) => [g.id, g.selected === true ? true : g.selected === false ? false : null]));
  return { inventory, preview, choices, dirty: false, saving: false, error: null };
}

function selectionComplete(inventory) {
  return inventory?.discovery?.filesComplete === true && inventory?.discovery?.registryComplete === true;
}

function renderSelection(host, draft) {
  const inventory = draft?.inventory;
  const groups = inventory?.groups ?? [];
  if (!inventory) { host.innerHTML = empty('Aucune donnée de sélection.'); return; }
  const complete = selectionComplete(inventory);
  const notes = [inventory.registryError, inventory.discovery?.filesComplete === false ? 'Découverte des fichiers partielle : des logiciels peuvent manquer à cette liste.' : null,
    !complete ? 'Enregistrement indisponible : les fichiers et le registre n’ont pas tous été lus. Les derniers choix sont conservés pour éviter de perdre des éléments non relus.' : null].filter(Boolean);
  const note = notes.length ? `<div class="callout warn">${icon('info')}<div>${notes.map((n) => `<p>${esc(n)}</p>`).join('')}${!complete ? `<button class="btn" type="button" data-act="reload-selection"${draft.reading ? ' disabled' : ''}>${draft.reading ? 'Lecture en cours…' : 'Relire cet inventaire'}</button>` : ''}</div></div>` : '';
  if (!groups.length) { host.innerHTML = note + empty('Aucun logiciel détecté pour ce profil.'); return; }
  const unresolved = [...draft.choices.values()].some((choice) => choice === null);
  const status = draft.reading ? 'Nouvelle lecture des fichiers et du registre…' : draft.saving ? 'Enregistrement en cours pour ce profil…' : draft.error || (draft.dirty ? 'Choix modifiés : enregistrez-les pour les inclure dans le plan.' : 'Choix relus pour ce profil.');
  host.innerHTML = note + `<p class="muted">Chaque case retient ensemble les fichiers et les clés du registre du logiciel. Les choix sont conservés séparément pour chaque profil.</p>
    <div class="stack">${groups.map((g) => `<div class="list-row">
    <input class="k-checkbox" type="checkbox" data-software-group="${esc(g.id)}" aria-label="Inclure les fichiers et les clés de ${esc(g.name)}"${draft.choices.get(g.id) === true ? ' checked' : ''}${draft.saving || !complete ? ' disabled' : ''}>
    <div class="body"><div class="title">${esc(g.name)}</div>
      <div class="sub mono">${formatNumber((g.files ?? []).length)} dossier(s) · ${formatNumber((g.registry ?? []).length)} clé(s)</div>
      ${(g.files ?? []).some((f) => String(f.category).toLowerCase() === 'modele') ? '<div class="sub">Modèles communs inclus dans ce groupe</div>' : ''}</div>
    ${sizePill(g.size)}</div>`).join('')}</div>
    ${unresolved ? '<p class="muted">Une sélection partielle existe : choisissez explicitement d’inclure ou d’exclure chaque groupe concerné avant d’enregistrer.</p>' : ''}
    <p class="muted" role="status" data-selection-status>${esc(status)}</p>
    <div class="actions-row"><button class="btn" type="button" data-act="save-selection"${!draft.dirty || draft.saving || unresolved || !complete ? ' disabled' : ''}${draft.saving ? ' aria-busy="true"' : ''}>Enregistrer les choix de ce profil</button><button class="btn" type="button" data-act="common-selection"${draft.dirty || draft.saving || unresolved || !complete ? ' disabled' : ''}>Utiliser comme choix communs</button></div>`;
  host.querySelectorAll('[data-software-group]').forEach((box) => {
    box.indeterminate = draft.choices.get(box.dataset.softwareGroup) === null;
    box.setAttribute('aria-checked', box.indeterminate ? 'mixed' : String(box.checked));
  });
}

function updateBackupState(panel, state) {
  panel.querySelector('[data-meta="profiles"]').textContent = `${formatNumber(state.selectedSids.size)} sélectionné(s) sur ${formatNumber(state.profiles.length)}`;
  const pending = [...state.selectedSids].some((sid) => {
    const draft = state.drafts.get(sid);
    return draft?.dirty || draft?.saving || draft?.reading;
  });
  const plan = panel.querySelector('[data-act="plan"]');
  const incomplete = [...state.selectedSids].filter((sid) => {
    const inventory = state.drafts.get(sid)?.inventory;
    return inventory && !selectionComplete(inventory);
  });
  const notice = panel.querySelector('[data-plan-status]');
  notice.hidden = incomplete.length === 0;
  notice.textContent = incomplete.length ? `Inventaire fichiers + registre incomplet pour ${incomplete.map((sid) => state.profiles.find((profile) => profile.sid === sid)?.account || sid).join(', ')}. La préparation est indisponible ; les derniers choix restent conservés. Relisez ce profil après avoir résolu le motif indiqué dans sa sélection.` : '';
  plan.disabled = state.planning || !state.selectedSids.size || pending || incomplete.length > 0 || !state.canBackup;
  plan.title = incomplete.length ? 'L’inventaire des fichiers et du registre doit être complet pour chaque profil retenu.' : pending ? 'Enregistrez les choix modifiés des profils sélectionnés avant de préparer le plan.' : '';
  panel.querySelectorAll('[data-sid]').forEach((row) => row.classList.toggle('is-active', row.dataset.sid === state.sid));
  panel.querySelectorAll('[data-review-sid]').forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.reviewSid === state.sid)));
  const profile = state.profiles.find((p) => p.sid === state.sid);
  panel.querySelector('[data-meta="selection"]').textContent = profile?.account || 'Aucun profil affiché';
}

async function saveSelection(panel, ctx, state) {
  const sid = state.sid;
  const draft = state.drafts.get(sid);
  if (!draft || !draft.dirty || draft.saving || !selectionComplete(draft.inventory) || [...draft.choices.values()].some((choice) => choice === null)) return;
  draft.saving = true;
  draft.error = null;
  renderSelection(panel.querySelector('[data-selection]'), draft);
  updateBackupState(panel, state);
  try {
    const inventory = await call('sauvegarder.saveSelection', { sid, groupIds: [...draft.choices].filter(([, on]) => on).map(([id]) => id) });
    state.drafts.set(sid, selectionDraft(inventory, draft.preview));
    if (state.active) ctx.toast?.(`Choix enregistrés pour ${state.profiles.find((p) => p.sid === sid)?.account || 'ce profil'}.`, { tone: 'ok' });
  } catch (err) {
    draft.error = errorText(err, 'Enregistrement impossible : les choix restent à enregistrer.');
    draft.saving = false;
    if (state.active) ctx.toast?.(draft.error, { tone: 'alert' });
  } finally {
    if (state.active) {
      if (state.sid === sid) renderSelection(panel.querySelector('[data-selection]'), state.drafts.get(sid));
      updateBackupState(panel, state);
    }
  }
}

async function saveCommonSelection(panel, ctx, state) {
  const sid = state.sid;
  const draft = state.drafts.get(sid);
  if (!draft || draft.dirty || draft.saving || !selectionComplete(draft.inventory) || [...draft.choices.values()].some((choice) => choice === null)) return;
  const groupIds = [...draft.choices].filter(([, on]) => on).map(([id]) => id);
  const accepted = await confirmDialog({ title: 'Utiliser comme choix communs',
    message: `${groupIds.length} groupe(s) deviendront les choix initiaux des profils sans sélection mémorisée. Les sélections déjà enregistrées par profil restent prioritaires. Ces choix ne sont pas inclus dans la configuration partageable du pack.`,
    confirmLabel: 'Enregistrer les choix communs' });
  if (!accepted || !state.active || draft !== state.drafts.get(sid) || draft.dirty || draft.saving) return;
  draft.saving = true;
  if (state.sid === sid) renderSelection(panel.querySelector('[data-selection]'), draft);
  updateBackupState(panel, state);
  try {
    await call('sauvegarder.setCommonSelection', { sid, groupIds });
    // Les brouillons explicites sont conservés. Les autres inventaires sont relus avec le nouveau modèle commun.
    for (const [otherSid, other] of state.drafts) if (otherSid !== sid && !other.dirty && !other.saving) state.drafts.delete(otherSid);
    if (state.active) ctx.toast?.('Choix communs enregistrés. Les profils sans historique les reprendront à leur prochaine lecture.', { tone: 'ok' });
  } catch (err) {
    if (state.active) ctx.toast?.(errorText(err), { tone: 'alert' });
  } finally {
    draft.saving = false;
    if (state.active) {
      if (state.sid === sid) renderSelection(panel.querySelector('[data-selection]'), draft);
      else if (!state.drafts.has(state.sid)) loadSelection(panel, state, state.sid);
      updateBackupState(panel, state);
    }
  }
}

function renderConflict(host, preview) {
  if (!preview) { host.innerHTML = empty('Aperçu indisponible.'); return; }
  const byId = {};
  (preview.policies ?? []).forEach((p) => { byId[p.id] = p.mode; });
  const rows = (preview.choices ?? []).map((g) => {
    const s = policyLabel(byId[g.id] ?? 'keepBoth');
    return `<div class="list-row"><div class="body"><div class="title">${esc(g.name)}</div>
      <div class="sub mono">${formatNumber((g.files ?? []).length)} fichier(s) · ${formatNumber((g.registry ?? []).length)} clé(s)</div></div>
      ${pill(s.tone, s.label, { size: 'sm' })}</div>`;
  }).join('');
  host.innerHTML = `<div class="callout info">${icon('shield')}<div><pre class="plan">${esc(preview.summary ?? '')}</pre></div></div>
    <div class="list">${rows || empty('Aucun réglage spécifique.')}</div>`;
}

function planSummary(plan) {
  const profiles = plan.profiles ?? [];
  const rows = profiles.map((p) => `<div class="list-row"><div class="body">
      <div class="title">${esc(p.account)}</div>
      <div class="sub mono">${esc(p.sid)}${p.profilePath ? ' · ' + esc(p.profilePath) : ''}</div>
      <div class="sub">${formatNumber((p.files ?? []).length)} dossier(s) · ${formatNumber((p.registry ?? []).length)} clé(s)</div></div>
    </div>`).join('');
  const kv = (label, value) => `<div class="list-row"><div class="body"><div class="title">${esc(label)}</div><div class="sub">${value}</div></div></div>`;
  const enc = plan.encrypted ? pill('ok', 'chiffrée', { size: 'sm' }) : pill('off', 'non chiffrée', { size: 'sm' });
  const size = pill('off', plan.space?.text || 'taille inconnue', { size: 'sm' });
  const mode = plan.demonstration ? pill('warn', 'monde fictif', { size: 'sm' }) : pill('off', 'poste réel', { size: 'sm' });
  const excl = (plan.exclusions ?? []).map((e) => `<li><b>${esc(e.kind)}</b>${e.target ? ' · ' + esc(e.target) : ''} — ${esc(e.reason)} <span class="muted">(${esc(e.origin)})</span></li>`).join('');
  const warns = (plan.warnings ?? []).map((w) => `<li>${esc(w)}</li>`).join('');
  return `<div class="stack">
    <div class="callout info">${icon('info')}<div><p>${esc(plan.scope || '')}</p></div></div>
    <div class="list">${rows || empty('Aucun profil dans le plan.')}</div>
    <div class="list">
      ${kv('Destination', `<span class="mono">${esc(plan.destination)}</span>`)}
      ${kv('Chiffrement', enc)}
      ${kv('Besoin d’espace', size)}
      ${kv('Origine des données', mode)}
    </div>
    ${plan.space?.notice ? `<div class="callout warn">${icon('alert')}<div><p>${esc(plan.space.notice)}</p></div></div>` : ''}
    ${warns ? `<div class="callout info">${icon('info')}<div><ul class="feature-list">${warns}</ul></div></div>` : ''}
    ${excl ? `<div class="callout off">${icon('shield')}<div><p class="muted">Exclusions annoncées par le plan :</p><ul class="feature-list">${excl}</ul></div></div>` : ''}
    <p class="muted">${esc(plan.notice || '')}</p>
  </div>`;
}

async function confirmPlan(ctx, plan) {
  try {
    const demo = ctx.session?.demonstration === true;
    const args = { planId: plan.id, acknowledgeCapacity: true };
    if (demo) args.confirmed = true;
    const job = await call(demo ? 'demonstration.backup' : 'sauvegarder.confirmPlan', args);
    followOperation(job, ctx);
  } catch (err) {
    console.error(err);
    ctx.toast?.(errorText(err, 'Confirmation refusée : rien n’a été préparé ni exécuté.'), { tone: 'warn' });
  }
}

function openConfirmPlanDialog(ctx, plan) {
  const needsAck = plan.requiresManualSpaceAcknowledgement !== false;
  const ackField = needsAck
    ? `<div class="k-field k-field-check"><label class="k-check-row"><input type="checkbox" class="k-checkbox" data-ack>
        <span class="k-check-text">Je comprends que la taille du plan est inconnue et j’assume l’accusé de capacité avant toute préparation longue.</span></label></div>`
    : '';
  const dlg = openDialog({
    title: 'Confirmer le plan de sauvegarde',
    subtitle: 'Votre accord lance la préparation longue, puis la sauvegarde selon les contrôles du moteur.',
    icon: 'shield',
    tone: 'info',
    size: 'md',
    badge: plan.demonstration ? 'Démonstration' : 'Poste réel · aperçu',
    body: planSummary(plan) + ackField + decisionLine(ctx.session),
    actions: [
      { id: 'cancel', label: 'Annuler', safe: true },
      { id: 'confirm', label: primaryLabel(ctx.session, 'Confirmer la préparation'), icon: 'shield', primary: true, disabled: needsAck },
    ],
  });
  const ack = dlg.body.querySelector('[data-ack]');
  if (ack) {
    const sync = () => dlg.setDisabled('confirm', !ack.checked);
    ack.addEventListener('change', sync);
    sync();
  }
  dlg.result.then((answer) => { if (answer === 'confirm') confirmPlan(ctx, plan); });
}

async function prepareBackupPlan(panel, ctx, state) {
  const sids = [...state.selectedSids];
  if (!sids.length) { ctx.toast?.('Sélectionner au moins un profil avant de préparer le plan.', { tone: 'warn' }); return; }
  if (state.planning) return;
  if (sids.some((sid) => state.drafts.get(sid)?.dirty || state.drafts.get(sid)?.saving)) {
    ctx.toast?.('Enregistrez les choix modifiés de chaque profil sélectionné avant de préparer le plan.', { tone: 'warn' });
    return;
  }
  const destination = (panel.querySelector('[data-dest]')?.value || '').trim();
  if (!destination) { ctx.toast?.('Indiquer une destination absolue avant de préparer le plan.', { tone: 'warn' }); return; }
  const encrypted = !!panel.querySelector('[data-enc]')?.checked;
  const keyPath = encrypted ? (panel.querySelector('[data-key]')?.value || '').trim() || null : null;
  const useVss = !!panel.querySelector('[data-vss]')?.checked;
  const btn = panel.querySelector('[data-act="plan"]');
  state.planning = true;
  updateBackupState(panel, state);
  btn?.setAttribute('aria-busy', 'true');
  try {
    const continueOnError = !!panel.querySelector('[data-continue]')?.checked;
    const plan = await call('sauvegarder.plan', { sids, destination, encrypted, keyPath, useVss, continueOnError });
    if (state.active) openConfirmPlanDialog(ctx, plan);
  } catch (err) {
    console.error(err);
    if (state.active) ctx.toast?.(errorText(err, 'Le plan n’a pas pu être préparé.'), { tone: 'alert' });
  } finally {
    state.planning = false;
    btn?.removeAttribute('aria-busy');
    if (state.active) updateBackupState(panel, state);
  }
}

async function loadSelection(main, state, sid, refresh = false) {
  const sel = main.querySelector('[data-selection]');
  const conf = main.querySelector('[data-conflict]');
  const request = ++state.selectionRequest;
  const current = () => state.active && state.sid === sid && state.selectionRequest === request;
  const savedDraft = state.drafts.get(sid);
  if (savedDraft && !refresh) {
    renderSelection(sel, savedDraft);
    renderConflict(conf, savedDraft.preview);
    updateBackupState(main, state);
    return;
  }
  if (savedDraft) { savedDraft.reading = true; savedDraft.error = null; renderSelection(sel, savedDraft); updateBackupState(main, state); }
  else sel.innerHTML = empty('Lecture de la sélection…');
  conf.innerHTML = empty('Calcul de l’aperçu…');
  try {
    const [inventory, preview] = await Promise.all([
      call('sauvegarder.software', { sid }),
      call('sauvegarder.conflictPolicies', { sid }).catch(() => null),
    ]);
    if (!current()) return;
    const draft = selectionDraft(inventory, preview);
    state.drafts.set(sid, draft);
    renderSelection(sel, draft);
    renderConflict(conf, preview);
    updateBackupState(main, state);
    applyVars(main);
  } catch (err) {
    console.error(err);
    if (!current()) return;
    if (savedDraft) savedDraft.error = errorText(err, 'Sélection indisponible ; les choix précédents restent visibles.');
    else sel.innerHTML = empty(errorText(err, 'Sélection indisponible.'));
    conf.innerHTML = '';
  } finally {
    if (savedDraft) savedDraft.reading = false;
    if (current()) {
      if (savedDraft && state.drafts.get(sid) === savedDraft) renderSelection(sel, savedDraft);
      updateBackupState(main, state);
    }
  }
}

async function loadProfiles(main, state) {
  const list = main.querySelector('[data-list="profiles"]');
  const meta = main.querySelector('[data-meta="profiles"]');
  list.innerHTML = empty('Lecture des profils…');
  try {
    const inventory = await call('sauvegarder.profiles', {});
    if (!state.active) return;
    state.profiles = inventory?.profiles ?? [];
    const initial = state.demonstration
      ? state.profiles.filter((p) => p.migrationSupported && p.loaded === false && String(p.session?.state).toLowerCase() === 'none').slice(0, 2)
      : state.profiles.filter((p) => String(p.identityKind).toLowerCase() !== 'entra').slice(0, 1);
    const first = initial[0];
    initial.forEach((profile) => state.selectedSids.add(profile.sid));
    renderProfileList(list, inventory, state);
    const select = (sid) => {
      state.sid = sid;
      updateBackupState(main, state);
      loadSelection(main, state, state.sid);
    };
    list.querySelectorAll('[data-review-sid]').forEach((button) => {
      button.addEventListener('click', () => select(button.dataset.reviewSid));
    });
    list.querySelectorAll('[data-include-sid]').forEach((box) => {
      box.addEventListener('change', () => {
        if (box.checked) state.selectedSids.add(box.dataset.includeSid);
        else state.selectedSids.delete(box.dataset.includeSid);
        updateBackupState(main, state);
      });
    });
    updateBackupState(main, state);
    if (first || state.profiles[0]) select((first || state.profiles[0]).sid);
    else main.querySelector('[data-selection]').innerHTML = empty('Aucun profil disponible.');
  } catch (err) {
    console.error(err);
    if (!state.active) return;
    list.innerHTML = empty(errorText(err, 'Profils indisponibles.'));
    meta.textContent = 'Lecture impossible';
  }
}

export function render(main, ctx) {
  main.innerHTML = `<div class="page sauvegarder">
    <header class="page-header reveal">
      <div class="head-row"><span class="page-icon">${icon('disk')}</span>
        <div class="titles"><h1 class="gradient-text">Sauvegarder</h1>
          <p class="subtitle">Sauvegardes et restaurations du magasin FILPRO.</p></div></div>
    </header>
    <section class="hero reveal spot">
      <span class="glow-ring" aria-hidden="true"></span>
      <div class="eyebrow">${icon('shield')}<span>${ctx.session?.demonstration ? 'Démonstration — données fictives' : 'Plans contrôlés avant exécution'}</span></div>
      <h1>Sauvegarder et restaurer</h1>
      <p class="lead">Choisissez les profils à sauvegarder, vérifiez leurs sélections fichiers + registre et l’aperçu des politiques de conflit.
        La préparation longue commence après votre confirmation. Le bilan distingue réussite, résultat partiel et arrêt.</p>
    </section>
    <div data-tabs></div>
  </div>`;
  applyVars(main);
  const stop = reveal(main);
  let backupState = null;
  const removeLeaveGuard = ctx.guardLeave?.(async () => {
    const drafts = [...(backupState?.drafts.values() ?? [])];
    if (drafts.some((draft) => draft.saving)) {
      ctx.toast?.('Attendez la fin de l’enregistrement des choix avant de quitter cet écran.', { tone: 'info' });
      return false;
    }
    const pending = [...(backupState?.drafts ?? [])].filter(([, draft]) => draft.dirty);
    if (!pending.length) return true;
    const names = pending.map(([sid]) => backupState.profiles.find((profile) => profile.sid === sid)?.account || sid);
    const dialog = openDialog({
      title: 'Choix de logiciels non enregistrés',
      subtitle: 'Les derniers choix enregistrés restent conservés.',
      icon: 'shield', tone: 'warn',
      body: `<p>Les changements de cette page seront perdus pour :</p><ul>${names.map((name) => `<li>${esc(name)}</li>`).join('')}</ul>`,
      actions: [
        { id: 'stay', label: 'Rester sur cet écran', safe: true, primary: true },
        { id: 'leave', label: 'Quitter sans enregistrer', danger: true },
      ],
    });
    return await dialog.result === 'leave';
  });

  const tabs = createTabs(main.querySelector('[data-tabs]'), {
    label: 'Sauvegarder ou restaurer',
    key: ctx.params?.tab ? undefined : 'sauvegarder',
    selected: ctx.params?.tab === 'restaurer' ? 'restaurer' : 'sauvegarder',
    tabs: [
      {
        id: 'sauvegarder', label: 'Sauvegarder', icon: 'disk',
        render: (panelEl) => {
          panelEl.innerHTML = backupLayout();
          applyVars(panelEl);
          const s = reveal(panelEl);
          const state = { active: true, sid: null, profiles: [], selectedSids: new Set(), drafts: new Map(), selectionRequest: 0, planning: false, demonstration: ctx.session?.demonstration === true, canBackup: ctx.session?.capabilities?.backup === true };
          backupState = state;
          const enc = panelEl.querySelector('[data-enc]');
          const keyrow = panelEl.querySelector('[data-keyrow]');
          enc?.addEventListener('change', () => { if (keyrow) keyrow.hidden = !enc.checked; });
          enc.checked = ctx.settings?.encrypted === true;
          keyrow.hidden = !enc.checked;
          panelEl.querySelector('[data-vss]').checked = ctx.settings?.vss === true;
          panelEl.querySelector('[data-continue]').checked = ctx.settings?.continueOnError === true;
          panelEl.querySelector('[data-pick-destination]').addEventListener('click', async () => {
            try {
              if (ctx.session?.demonstration) { panelEl.querySelector('[data-dest]').focus(); return; }
              const result = await call('session.choosePath', { kind: 'folder' });
              if (state.active && result.path) panelEl.querySelector('[data-dest]').value = result.path;
            } catch (err) { if (state.active) ctx.toast(errorText(err), { tone: 'alert' }); }
          });
          if (ctx.session?.demonstration) call('demonstration.catalog', {}).then((catalog) => {
            if (!state.active) return;
            panelEl.querySelector('#bk-destinations').innerHTML = catalog.destinations.map((destination) => `<option value="${esc(destination.path)}">${esc(destination.label)}</option>`).join('');
            panelEl.querySelector('[data-dest]').value = catalog.destinations[0]?.path ?? '';
            panelEl.querySelector('[data-pick-destination]').textContent = 'Choisir une destination fictive';
            if (enc.checked) panelEl.querySelector('[data-key]').value = catalog.destinations[1]?.path + '\\cle-fictive.key';
          }).catch((err) => { if (state.active) ctx.toast(errorText(err), { tone: 'alert' }); });
          if (!state.canBackup) panelEl.querySelector('[data-act="plan"]').insertAdjacentHTML('beforebegin', '<p class="callout warn">La sauvegarde n’est pas disponible dans cette session. L’inventaire reste consultable.</p>');
          const selection = panelEl.querySelector('[data-selection]');
          selection.addEventListener('change', (event) => {
            const box = event.target;
            if (!(box instanceof HTMLInputElement) || !box.hasAttribute('data-software-group')) return;
            const draft = state.drafts.get(state.sid);
            if (!draft || draft.saving || !selectionComplete(draft.inventory) || !draft.choices.has(box.dataset.softwareGroup)) return;
            draft.choices.set(box.dataset.softwareGroup, box.checked);
            draft.dirty = true;
            draft.error = null;
            const groupId = box.dataset.softwareGroup;
            renderSelection(selection, draft);
            [...selection.querySelectorAll('[data-software-group]')].find((input) => input.dataset.softwareGroup === groupId)?.focus();
            updateBackupState(panelEl, state);
          });
          selection.addEventListener('click', (event) => {
            if (event.target instanceof Element && event.target.closest('[data-act="save-selection"]')) saveSelection(panelEl, ctx, state);
            if (event.target instanceof Element && event.target.closest('[data-act="common-selection"]')) saveCommonSelection(panelEl, ctx, state);
            if (event.target instanceof Element && event.target.closest('[data-act="reload-selection"]')) loadSelection(panelEl, state, state.sid, true);
          });
          panelEl.querySelector('[data-act="plan"]').addEventListener('click', () => prepareBackupPlan(panelEl, ctx, state));
          updateBackupState(panelEl, state);
          loadProfiles(panelEl, state);
          return () => { state.active = false; s(); };
        },
      },
      {
        id: 'restaurer', label: 'Restaurer', icon: 'sync',
        render: (panelEl) => mountRestore(panelEl, ctx),
      },
    ],
  });

  return () => { removeLeaveGuard?.(); tabs.destroy(); stop(); };
}
