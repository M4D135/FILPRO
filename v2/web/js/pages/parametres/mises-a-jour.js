// Paramètres › Mises à jour : source (dossier local ou UNC, ou ZIP choisi à la main), offre vérifiée, préparation,
// installation et retour arrière. Chaque étape demande une action explicite ; la préparation, l'installation et le retour
// arrière passent par la fenêtre « FILPRO — Confirmation Windows » hors démonstration. Sous un autre compte, la section
// est remplacée par l'explication (codeUpdates).
import { esc, glyph, card, notes, sectionHead, paint, button, facts } from './common.js';
import { reveal } from '../../ui.js';
import { confirmDialog } from '../../kit/index.js';
import { confirmUnlessNative, codeUpdates, CROSS_ACCOUNT_UPDATES_TEXT } from '../../native-confirmation.js';

const options = (items) => items.map(([value, text]) => `<option value="${value}">${text}</option>`).join('');

function markup() {
  return `${sectionHead({ title: 'Mises à jour', lead: 'FILPRO ne cherche rien sur Internet : la mise à jour vient d’un dossier local ou réseau contenant le ZIP PROD-LIGHT et son SHA256SUMS, ou d’un ZIP choisi à la main.', icon: 'refresh', tone: 'info' })}
    <div class="callout warn" data-update-unavailable hidden></div>
    <div class="pr-grid">
      ${card({ id: 'installed', title: 'Version installée', sub: 'État de la dernière action', iconName: 'shield', toneName: 'info', cls: 'pr-wide',
    body: `<div data-setting="actions">${facts([['Installée', null, 'installed'], ['Version visée', null, 'target'], ['Version conservée', null, 'previous']])}<div data-update-status role="status"></div>
        <div class="actions-row" data-update-actions><button class="btn primary" type="button" data-install hidden>${glyph('check')}<span>Installer après confirmation</span></button><button class="btn" type="button" data-rollback hidden>${glyph('undo')}<span>Revenir à la version précédente</span></button></div></div>` })}
      <div class="pr-wide" data-update-controls>
      ${card({ id: 'source', title: 'Source des mises à jour', sub: 'Mémorisée sur ce poste, sans connexion', iconName: 'folder', cls: 'pr-wide',
    body: `<div class="pr-form" data-setting="source">
        <label class="pr-field">Source<select class="input" data-source>${options([['folder', 'Dossier local ou partage UNC'], ['manualZip', 'ZIP choisi manuellement']])}</select></label>
        <div class="pr-field" data-source-path><span>Chemin de la source</span><div class="pr-path"><input class="input mono" type="text" data-update-path autocomplete="off" spellcheck="false" aria-label="Chemin de la source">${button('browse', 'Parcourir', 'folder')}</div></div>
        <label class="pr-field" data-hash-row hidden>Empreinte SHA-256 du ZIP<input class="input mono" type="text" data-update-hash maxlength="64" autocomplete="off" spellcheck="false"><small class="muted">Facultative si le fichier SHA256SUMS fourni avec le pack est présent à côté du ZIP. L’archive seule ne constitue pas une preuve d’intégrité.</small></label>
        <div class="actions-row">${button('save-source', 'Mémoriser cette source', 'pin')}<button class="btn primary" type="button" data-act="inspect">${glyph('search')}<span>Rechercher et vérifier l’offre</span></button></div>
      </div>` })}
      ${card({ id: 'offer', title: 'Offre trouvée', sub: 'Version, taille et empreinte contrôlées', iconName: 'download', cls: 'pr-wide',
    body: `<div data-offer><p class="muted">Aucune offre lue. « Rechercher et vérifier l’offre » lit la source sur votre demande.</p></div>
        <div class="actions-row"><button class="btn primary" type="button" data-prepare hidden>${glyph('download')}<span>Préparer la mise à jour</span></button></div>` })}
      </div>
      <div class="pr-footnote reveal">${notes([
        'Préparer copie et vérifie le pack dans un dossier temporaire local ; l’application installée reste inchangée jusqu’à l’installation.',
        'À l’installation, FILPRO ferme proprement ses travaux. La nouvelle version doit confirmer son démarrage, sinon l’ancienne copie est remise.',
        'Le moteur du Socle déjà extrait et la configuration par défaut du dossier sont recopiés dans la nouvelle version. Le retour arrière ne restaure ni les profils ni les données métier.',
      ])}</div>
    </div>`;
}

export function mount(host, sctx) {
  const { st, call, run, feedback, demo, ctx } = sctx;
  host.innerHTML = markup();
  paint(host);
  const stopReveal = reveal(host);
  const $ = (selector) => host.querySelector(selector);
  // Autre compte : ni recherche ni installation ; la version installée reste affichée. Relu à chaque rafraîchissement.
  const updatesAvailable = () => ctx.session?.capabilities?.updates === true && codeUpdates(ctx.session);
  if (!codeUpdates(ctx.session)) {
    $('[data-update-controls]').hidden = true;
    $('[data-update-actions]').hidden = true;
    const notice = $('[data-update-unavailable]');
    notice.innerHTML = `${glyph('lock')}<div><p>${esc(CROSS_ACCOUNT_UPDATES_TEXT)}</p></div>`;
    notice.hidden = false;
  }
  const source = $('[data-source]');
  const path = $('[data-update-path]');
  const hash = $('[data-update-hash]');

  // Saisie en cours de la source : gardée par la page d'une section à l'autre.
  const keepDraft = () => { st.sourceDraft = { kind: source.value, path: path.value, hash: hash.value }; };
  const paintOffer = () => {
    const found = st.offer;
    $('[data-offer]').innerHTML = found
      ? `<div class="workflow-result"><strong>${esc(found.version)}${found.demonstration ? ' · fictive' : ''}</strong><span>${esc(found.fileName)} · ${(found.bytes / 1048576).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mio</span><code>SHA-256 : ${esc(found.sha256)}</code><p>${esc(found.integrityNotice)}</p>${(found.warnings || []).map((warning) => `<p>${esc(warning)}</p>`).join('')}</div>`
      : '<p class="muted">Aucune offre lue. « Rechercher et vérifier l’offre » lit la source sur votre demande.</p>';
    $('[data-prepare]').hidden = !found?.newer;
  };
  const invalidateOffer = () => { st.offer = null; paintOffer(); };
  const applySource = (resetPath) => {
    $('[data-hash-row]').hidden = source.value !== 'manualZip';
    if (resetPath) path.value = demo ? st.catalog?.assets?.[source.value === 'folder' ? 'updateFolderPath' : 'updateZipPath'] ?? '' : (source.value === 'folder' ? st.source?.folder ?? '' : '');
  };
  const restore = () => {
    if (st.sourceDraft) { source.value = st.sourceDraft.kind; path.value = st.sourceDraft.path; hash.value = st.sourceDraft.hash; applySource(false); }
    else { if (st.source?.kind) source.value = st.source.kind; applySource(true); }
  };
  const showUpdate = (value) => {
    if (!value) return;
    $('[data-fact="installed"]').textContent = `${value.currentVersion}${value.demonstration ? ' · fictive' : ''}`;
    for (const [slot, text] of [['target', value.targetVersion], ['previous', value.previousVersion]]) {
      const el = $(`[data-fact="${slot}"]`);
      if (text) el.textContent = text; else el.innerHTML = '<span class="pr-none">—</span>';
    }
    $('[data-update-status]').innerHTML = `<p class="callout ${value.error ? 'alert' : 'info'}">${esc(value.action)}${value.error ? ' — ' + esc(value.error) : ''}</p>${Number.isFinite(value.progressPercent) ? `<progress class="operation-progress" max="100" value="${Math.max(0, Math.min(100, value.progressPercent))}" aria-label="Progression de la mise à jour"></progress>` : ''}${(value.warnings || []).map((warning) => `<p class="muted">${esc(warning)}</p>`).join('')}`;
    $('[data-install]').hidden = !value.canInstall;
    $('[data-rollback]').hidden = !value.canRollback;
  };
  let shown;
  const sync = () => {
    // La zone d'état (role=status) n'est réécrite que sur une nouvelle valeur : sinon chaque action la ferait réannoncer.
    const value = st.errors.update ?? st.update ?? null;
    if (value !== shown) {
      shown = value;
      if (st.errors.update) $('[data-update-status]').textContent = st.errors.update; else if (st.update) showUpdate(st.update);
    }
    const available = updatesAvailable();
    for (const el of host.querySelectorAll('[data-source], [data-update-path], [data-update-hash], [data-act="browse"], [data-act="save-source"], [data-act="inspect"], [data-prepare], [data-install], [data-rollback]')) el.disabled = st.busy || !available;
  };

  const saveSource = async () => {
    st.source = await call('updates.saveSource', { kind: source.value, folder: source.value === 'folder' ? path.value.trim() : null });
  };
  // Chemin vide : une indication près du champ, sans appel à l'hôte ni état d'échec.
  const pathMissing = (needed) => {
    if (!needed || path.value.trim()) return false;
    feedback(source.value === 'folder' ? 'Indiquez d’abord le dossier qui contient le ZIP PROD-LIGHT et son SHA256SUMS.' : 'Choisissez d’abord le ZIP de mise à jour.');
    path.focus();
    return true;
  };
  const refresh = async () => { st.update = await call('updates.read', {}); sctx.emit(); };

  const onClick = (e) => {
    const t = e.target instanceof Element ? e.target : null;
    const btn = t?.closest('[data-act], [data-prepare], [data-install], [data-rollback]');
    if (!btn || btn.disabled) return;
    // Chemin vide : contrôlé avant run(), tant que le champ est actif et peut recevoir le focus.
    if (btn.dataset.act === 'save-source') {
      if (!pathMissing(source.value === 'folder')) run(async () => {
        await saveSource(); feedback(demo ? 'Choix fictif mémorisé dans cette démonstration.' : 'Source mémorisée sur ce poste. Aucune recherche ni connexion effectuée.');
      });
    }
    else if (btn.dataset.act === 'browse') run(async () => {
      const chosen = await sctx.choose(source.value === 'folder' ? 'folder' : 'updateZip', source.value === 'folder' ? 'updateFolderPath' : 'updateZipPath');
      if (chosen && st.alive) { path.value = chosen; keepDraft(); invalidateOffer(); }
    });
    else if (btn.dataset.act === 'inspect') {
      if (!pathMissing(true)) run(async () => {
        const args = { kind: source.value, path: path.value.trim() };
        if (source.value === 'manualZip') args.sha256 = hash.value.trim() || null;
        await saveSource();
        const found = await call('updates.inspect', args); if (!st.alive) return;
        st.offer = found; paintOffer();
        await refresh();
      });
    }
    else if (btn.matches('[data-prepare]')) run(async () => {
      const offer = st.offer;
      if (!offer || !await confirmUnlessNative(ctx.session, () => confirmDialog({ title: 'Préparer cette mise à jour', message: `Le pack ${offer.version} sera ${demo ? 'simulé en mémoire' : 'copié et validé dans un dossier temporaire local'}. Cette préparation peut prendre plusieurs minutes. L’application installée reste inchangée jusqu’à votre accord suivant.`, confirmLabel: 'Confirmer la préparation' }))) return;
      st.update = await call('updates.prepare', { offerId: offer.id, confirmed: true });
      if (st.alive) { st.offer = { ...offer, newer: false }; paintOffer(); sctx.emit(); }
    });
    else {
      const rollback = btn.matches('[data-rollback]');
      run(async () => {
        const title = rollback ? 'Revenir à la version précédente' : 'Installer la mise à jour';
        if (!st.update?.transactionId || !await confirmUnlessNative(ctx.session, () => confirmDialog({ title, message: demo ? 'Seule la version fictive de cette démonstration sera modifiée.' : 'FILPRO se ferme proprement. L’assistant conserve la version précédente et attend la vérification du démarrage avant de valider l’installation.', confirmLabel: title }))) return;
        const op = demo ? `demonstration.update${rollback ? 'Rollback' : 'Install'}` : `updates.${rollback ? 'rollback' : 'install'}`;
        const result = await call(op, { transactionId: st.update.transactionId, confirmed: true });
        feedback(result.notice); if (!result.closeApplicationRequired) await refresh();
      });
    }
  };
  const onInput = (e) => {
    if (e.target === path || e.target === hash) { keepDraft(); invalidateOffer(); }
  };
  const onChange = (e) => {
    if (e.target === source) { applySource(true); keepDraft(); invalidateOffer(); }
  };
  host.addEventListener('click', onClick);
  host.addEventListener('input', onInput);
  host.addEventListener('change', onChange);
  const off = sctx.on(sync);
  restore(); paintOffer(); sync();

  return {
    // Après une lecture complète de la page : la source mémorisée et la démonstration remplacent la saisie.
    loaded() { restore(); paintOffer(); sync(); },
    destroy() {
      off(); stopReveal();
      host.removeEventListener('click', onClick);
      host.removeEventListener('input', onInput);
      host.removeEventListener('change', onChange);
    },
  };
}
