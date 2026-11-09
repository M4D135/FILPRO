import { call, onEvent } from '../bridge.js';
import { icon } from '../icons.js';
import { esc, errorText, applyVars, reveal } from '../ui.js';
import { confirmDialog, openDialog } from '../kit/index.js';
import { SETTING_LABELS, reviewConfiguration, configurationChanged, decideBootstrap } from '../configuration-ui.js';
import { confirmUnlessNative, codeUpdates, CROSS_ACCOUNT_UPDATES_TEXT } from '../native-confirmation.js';

const booleanFields = ['navigationCollapsed', 'autoSelectCurrentTask', 'notifyOnCompletion', 'recommendedSelection', 'vss', 'continueOnError', 'encrypted'];
const options = (items) => items.map(([value, text]) => `<option value="${value}">${text}</option>`).join('');
function layout(demo) {
  return `<div class="page workflow-page"><header class="page-header reveal"><div class="head-row"><span class="page-icon">${icon('settings')}</span><div class="titles"><h1>Paramètres</h1><p class="subtitle">Préférences, configuration du pack et mises à jour à votre demande.</p></div></div><button class="btn" type="button" data-refresh>${icon('refresh')}Actualiser</button></header>
    <div data-capability-notices></div><div data-feedback role="status"></div><section class="card reveal" data-config aria-busy="true"><div class="card-head"><span class="card-icon">${icon('settings')}</span><h2>Réglages de FILPRO</h2></div><div data-bootstrap></div>
    <form class="workflow-form" data-settings><label>Thème<select class="input" name="theme">${options([['system','Suivre Windows'],['light','Clair'],['dark','Sombre'],['contrast','Contraste élevé']])}</select></label>
    <label>Animations<select class="input" name="motion">${options([['system','Suivre Windows'],['full','Riches'],['reduced','Réduites']])}</select></label>
    <label class="wide">Conflits de restauration<select class="input" name="conflictPolicy">${options([['keepBoth','Coexistence : préserver les deux versions'],['keepCurrent','Garder les valeurs actuelles'],['useSaved','Rétablir les valeurs sauvegardées']])}</select></label>
    ${booleanFields.map((key) => `<label class="check-row"><input class="k-checkbox" type="checkbox" name="${key}"><span>${esc(SETTING_LABELS[key])}</span></label>`).join('')}
    <div class="wide actions-row"><button class="btn primary" type="submit" data-save disabled>Enregistrer les réglages</button><span class="muted" data-dirty></span></div></form></section>
    <section class="card reveal"><div class="card-head"><span class="card-icon">${icon('sparkles')}</span><h2>Démonstration</h2></div>
    <p>Explorez FILPRO avec des profils et des sauvegardes fictifs. Les essais restent en mémoire et ne modifient aucune donnée Windows.</p>
    <div class="actions-row">${demo
      ? '<button class="btn primary" type="button" data-demo-action="session.demoExit">Quitter la démonstration</button><button class="btn" type="button" data-demo-action="session.demoReset">Réinitialiser les données fictives</button>'
      : '<button class="btn" type="button" data-demo-action="session.demoEnter">Ouvrir la démonstration</button>'}</div></section>
    <section class="card reveal"><div class="card-head"><span class="card-icon">${icon('folder')}</span><h2>Configuration à distribuer</h2></div><p>Configurez ce pack, puis définissez ses réglages par défaut. Un nouvel utilisateur les recevra au premier lancement ; un utilisateur existant choisira de les appliquer ou de garder les siens.</p>
    <div class="actions-row"><button class="btn primary" type="button" data-defaults disabled>Définir comme configuration par défaut</button><button class="btn" type="button" data-export disabled>Exporter la configuration</button><button class="btn" type="button" data-import disabled>Importer une configuration</button></div><p class="muted">Le document contient uniquement les champs autorisés : aucun secret, compte, SID, clé de chiffrement ou historique.</p></section>
    <section class="card reveal"><div class="card-head"><span class="card-icon">${icon('object')}</span><h2>Choix de logiciels communs</h2></div><p data-common-status>Lecture des choix communs…</p><div class="actions-row"><a class="btn" href="#/sauvegarder">Configurer depuis un profil</a><button class="btn" type="button" data-clear-common disabled>Effacer les choix communs</button><button class="btn" type="button" data-template-export>Exporter le modèle</button><button class="btn" type="button" data-template-import>Importer un patch</button><button class="btn" type="button" data-template-source>Chercher dans le dossier de diffusion</button></div><p class="muted">Ils s’appliquent aux profils sans sélection mémorisée. Un patch explicite peut partager ces choix sans modifier les profils ; les conflits locaux sont conservés par défaut.</p></section>
    <section class="card reveal"><div class="card-head"><span class="card-icon">${icon('refresh')}</span><h2>Mises à jour</h2><span class="meta" data-installed>—</span></div><div class="callout warn" data-update-unavailable hidden></div><div data-update-controls><p>Choisissez une source. La recherche, la préparation et l’installation demandent chacune une action explicite.</p>
    <div class="workflow-form"><label>Source<select class="input" data-source>${options([['folder','Dossier local ou partage UNC'],['manualZip','ZIP choisi manuellement']])}</select></label>
    <label data-source-path>Chemin de la source<div class="actions-row"><input class="input mono" type="text" data-update-path autocomplete="off" spellcheck="false"><button class="btn" type="button" data-browse>Parcourir</button></div></label>
    <label class="wide" data-hash-row hidden>Empreinte SHA-256 du ZIP<input class="input mono" type="text" data-update-hash maxlength="64" autocomplete="off" spellcheck="false"><small class="muted">Facultative si le fichier SHA256SUMS fourni avec le pack est présent à côté du ZIP. L’archive seule ne constitue pas une preuve d’intégrité.</small></label></div>
    <div class="actions-row"><button class="btn" type="button" data-save-source>Mémoriser cette source</button><button class="btn" type="button" data-inspect>Rechercher et vérifier l’offre</button></div><div data-offer></div><div data-update-status role="status"></div>
    <div class="actions-row"><button class="btn primary" type="button" data-prepare hidden>Préparer la mise à jour</button><button class="btn primary" type="button" data-install hidden>Installer après confirmation</button><button class="btn" type="button" data-rollback hidden>Revenir à la version précédente</button></div></div></section>
    ${demo ? `<section class="card reveal"><div class="card-head"><span class="card-icon">${icon('sparkles')}</span><h2>Essayer les scénarios</h2></div><p>La prochaine sauvegarde, restauration, réparation ou récupération peut réussir, produire un résultat partiel ou échouer volontairement. Pour essayer l’annulation, utilisez le suivi pendant une action.</p><div class="workflow-form"><label>Issue de la prochaine opération<select class="input" data-scenario></select></label></div><div class="actions-row"><a class="btn" href="#/sauvegarder">Sauvegarder / restaurer</a><a class="btn" href="#/analyser">Analyser / réparer</a><a class="btn" href="#/recuperer">Récupérer des fichiers</a></div><p class="muted" data-demo-count></p></section>` : ''}</div>`;
}

export function render(main, ctx) {
  const demo = ctx.session?.demonstration === true;
  main.innerHTML = layout(demo); applyVars(main); const stopReveal = reveal(main);
  // Autre compte : ni recherche ni installation ; la version installée reste affichée.
  const updatesAvailable = ctx.session?.capabilities?.updates === true && codeUpdates(ctx.session);
  if (!codeUpdates(ctx.session)) {
    main.querySelector('[data-update-controls]').hidden = true;
    const notice = main.querySelector('[data-update-unavailable]');
    notice.innerHTML = `${icon('lock')}<div><p>${esc(CROSS_ACCOUNT_UPDATES_TEXT)}</p></div>`;
    notice.hidden = false;
  }
  const capabilityWarnings = ctx.session?.capabilityWarnings ?? [];
  if (capabilityWarnings.length) main.querySelector('[data-capability-notices]').innerHTML = `<div class="callout warn"><div><strong>Disponibilité de cette session</strong><ul>${capabilityWarnings.map((warning) => `<li>${esc(warning)}</li>`).join('')}</ul></div></div>`;
  let active = true; let config = null; let catalog = null; let offer = null; let update = null; let common = null; let sourcePreference = null; let dirty = false; let busy = false;
  const form = main.querySelector('[data-settings]');
  const feedback = (text, alert = false) => { if (active) main.querySelector('[data-feedback]').innerHTML = `<div class="callout ${alert ? 'alert' : 'info'}"${alert ? ' role="alert"' : ''}>${esc(text)}</div>`; };
  const sync = () => {
    for (const input of form.elements) input.disabled = busy || !config;
    main.querySelector('[data-save]').disabled = busy || !config || !dirty;
    main.querySelector('[data-dirty]').textContent = dirty ? 'Modifications non enregistrées' : '';
    for (const key of ['defaults','export','import']) main.querySelector(`[data-${key}]`).disabled = busy || !config || dirty;
    main.querySelector('[data-clear-common]').disabled = busy || common?.hasSelection !== true;
    for (const key of ['template-export','template-import','template-source']) main.querySelector(`[data-${key}]`).disabled = busy || !common;
    main.querySelector('[data-refresh]').disabled = busy;
    for (const key of ['source','update-path','update-hash','browse','save-source','inspect','prepare','install','rollback']) main.querySelector(`[data-${key}]`).disabled = busy || !updatesAvailable;
    const scenario = main.querySelector('[data-scenario]'); if (scenario) scenario.disabled = busy || !catalog;
    main.querySelectorAll('[data-demo-action]').forEach((button) => {
      button.disabled = busy || typeof ctx.switchDemonstration !== 'function' || ctx.session?.demonstration == null;
    });
  };
  const showCommon = (value) => {
    if (!active) return; common = value;
    main.querySelector('[data-common-status]').textContent = value.hasSelection
      ? `${value.appData?.length ?? 0} dossier(s) et ${value.registryKeys?.length ?? 0} clé(s) constituent les choix communs. Une liste vide est un choix explicite.`
      : 'Aucun choix commun enregistré. Les recommandations s’appliquent lorsqu’elles sont activées et qu’aucun choix du profil n’existe.';
    sync();
  };
  const showConfig = (state) => {
    config = state; dirty = false;
    for (const [key, value] of Object.entries(state.settings || {})) {
      const input = form.elements.namedItem(key);
      if (!input) continue;
      if (input.type === 'checkbox') input.checked = value === true; else input.value = value;
    }
    const bootstrap = main.querySelector('[data-bootstrap]');
    bootstrap.innerHTML = [...new Set(state.warnings || [])].map((warning) => `<p class="callout warn" role="status">${esc(warning)}</p>`).join('')
      + (state.bootstrap ? '<div class="callout info"><p>Ce pack propose une configuration. Vous pouvez examiner les changements et garder vos réglages.</p><button class="btn" type="button" data-review-bootstrap>Examiner la configuration fournie</button></div>' : '');
    bootstrap.querySelector('button')?.addEventListener('click', () => run(async () => { const result = await decideBootstrap(state.bootstrap); if (result) acceptResult(result); }));
    sync();
  };
  const acceptResult = (result) => { if (!active) return; showConfig(result.state); configurationChanged(result.state); feedback([result.notice, result.writtenPath, result.backupPath ? `Copie préalable : ${result.backupPath}` : ''].filter(Boolean).join(' ')); };
  const showUpdate = (value) => {
    if (!active) return; update = value;
    main.querySelector('[data-installed]').textContent = `Installée : ${value.currentVersion}${value.demonstration ? ' · fictive' : ''}`;
    main.querySelector('[data-update-status]').innerHTML = `<p class="callout ${value.error ? 'alert' : 'info'}">${esc(value.action)}${value.error ? ' — ' + esc(value.error) : ''}</p>${Number.isFinite(value.progressPercent) ? `<progress class="operation-progress" max="100" value="${Math.max(0,Math.min(100,value.progressPercent))}" aria-label="Progression de la mise à jour"></progress>` : ''}${(value.warnings || []).map((warning) => `<p class="muted">${esc(warning)}</p>`).join('')}`;
    main.querySelector('[data-install]').hidden = !value.canInstall;
    main.querySelector('[data-rollback]').hidden = !value.canRollback;
  };
  async function run(work) {
    if (busy) return; busy = true; sync();
    try { await work(); } catch (err) { feedback(errorText(err), true); }
    finally { busy = false; if (active) sync(); }
  }
  form.addEventListener('change', () => { dirty = true; sync(); });
  main.querySelectorAll('[data-demo-action]').forEach((button) => {
    button.addEventListener('click', () => run(() => ctx.switchDemonstration(button.dataset.demoAction)));
  });
  form.addEventListener('submit', (event) => {
    event.preventDefault(); run(async () => {
      const settings = Object.fromEntries(['theme','motion','conflictPolicy',...booleanFields].map((key) => [key, form.elements.namedItem(key).type === 'checkbox' ? form.elements.namedItem(key).checked : form.elements.namedItem(key).value]));
      settings.migApp = false; // Champ conservé pour relire les anciennes configurations.
      acceptResult(await call('configuration.save', { settings, expectedRevision: config.revision }));
    });
  });
  const choose = async (kind, asset) => demo ? catalog?.assets?.[asset] : (await call('session.choosePath', { kind }))?.path;
  main.querySelector('[data-defaults]').addEventListener('click', () => run(async () => {
    const preview = await call('configuration.previewDefaults', {});
    if (active && await reviewConfiguration(preview) === 'apply') acceptResult(await call('configuration.applyDefaults', { previewId: preview.id, confirmed: true }));
  }));
  main.querySelector('[data-export]').addEventListener('click', () => run(async () => {
    const destinationPath = await choose('saveConfiguration', 'exportConfigurationPath');
    if (destinationPath && await confirmDialog({ title: 'Exporter les réglages partageables', message: `Destination ${demo ? 'fictive' : 'choisie'} : ${destinationPath}. Aucun secret ou profil ne sera inclus.`, confirmLabel: 'Exporter' })) acceptResult(await call('configuration.export', { destinationPath, confirmed: true }));
  }));
  main.querySelector('[data-import]').addEventListener('click', () => run(async () => {
    const sourcePath = await choose('openConfiguration', 'importConfigurationPath'); if (!sourcePath) return;
    const preview = await call('configuration.previewImport', { sourcePath });
    if (active && await reviewConfiguration(preview) === 'apply') acceptResult(await call('configuration.applyImport', { previewId: preview.id, confirmed: true }));
  }));
  const reviewPatch = async (preview) => {
    const names = { add: 'Ajout', identical: 'Identique', conflict: 'Conflit local' };
    const hasConflicts = preview.changes.some((item) => item.disposition === 'conflict');
    const dialog = openDialog({ title: 'Examiner le patch de modèles', icon: 'object', size: 'lg',
      subtitle: preview.demonstration ? 'Démonstration — données fictives' : 'Aucun profil Windows ne sera modifié',
      body: `<p>${esc(preview.notice)}</p><p class="mono">SHA-256 : ${esc(preview.sha256)}</p><div class="workflow-table-wrap"><table class="workflow-table"><thead><tr><th>Portée et chemin</th><th>Local</th><th>Proposé</th><th>État</th></tr></thead><tbody>${preview.changes.map((item) => `<tr><th>${esc(item.scope)} · ${esc(item.relativePath)}</th><td>${esc(item.before ?? '—')}</td><td>${esc(item.after)}</td><td>${esc(names[item.disposition] ?? item.disposition)}</td></tr>`).join('')}</tbody></table></div>`,
      actions: [{ id: 'cancel', label: 'Annuler', safe: true }, { id: 'keep', label: hasConflicts ? 'Ajouter et garder mes conflits' : 'Appliquer le patch', primary: true }, ...(hasConflicts ? [{ id: 'replace', label: 'Appliquer aussi aux conflits' }] : [])],
    });
    const choice = await dialog.result;
    if (!active || !['keep','replace'].includes(choice)) return;
    const result = await call('sauvegarder.applyCommonPatch', { previewId: preview.id, replaceConflicts: choice === 'replace', confirmed: true });
    if (active) { showCommon(result.state); feedback([result.notice, result.backupPath ? `Sauvegarde : ${result.backupPath}` : ''].filter(Boolean).join(' ')); }
  };
  main.querySelector('[data-template-export]').addEventListener('click', () => run(async () => {
    const destination = await choose('saveConfiguration', 'exportConfigurationPath'); if (!destination) return;
    if (!await confirmDialog({ title: 'Exporter les choix communs', message: 'Le JSON et sa somme .sha256 contiendront les chemins relatifs de logiciels et les clés HKCU sélectionnées. Aucun compte, profil, clé de chiffrement ou consentement ne sera inclus.', confirmLabel: 'Exporter ce modèle' })) return;
    const result = await call('sauvegarder.exportCommonPatch', { destination, confirmed: true }); feedback(`${result.notice} ${result.writtenPath ?? ''}`);
  }));
  main.querySelector('[data-template-import]').addEventListener('click', () => run(async () => {
    const source = await choose('openConfiguration', 'importConfigurationPath'); if (source) await reviewPatch(await call('sauvegarder.previewCommonPatch', { source }));
  }));
  main.querySelector('[data-template-source]').addEventListener('click', () => run(async () => reviewPatch(await call('sauvegarder.previewCommonPatchFromSource', {}))));
  main.querySelector('[data-clear-common]').addEventListener('click', () => run(async () => {
    if (!await confirmDialog({ title: 'Effacer les choix communs', message: 'Les sélections enregistrées séparément pour chaque profil seront conservées. Les profils sans historique reprendront les recommandations activées.', confirmLabel: 'Effacer les choix communs' })) return;
    showCommon(await call('sauvegarder.clearCommonSelection', { confirmed: true }));
    feedback('Choix communs effacés ; sélections propres aux profils conservées.');
  }));
  const source = main.querySelector('[data-source]');
  const invalidateOffer = () => { offer = null; main.querySelector('[data-offer]').innerHTML = ''; main.querySelector('[data-prepare]').hidden = true; };
  const updateSource = () => {
    main.querySelector('[data-hash-row]').hidden = source.value !== 'manualZip';
    main.querySelector('[data-update-path]').value = demo ? catalog?.assets?.[source.value === 'folder' ? 'updateFolderPath' : 'updateZipPath'] ?? '' : (source.value === 'folder' ? sourcePreference?.folder ?? '' : '');
    invalidateOffer();
  };
  source.addEventListener('change', updateSource);
  const saveSource = async () => {
    sourcePreference = await call('updates.saveSource', { kind: source.value, folder: source.value === 'folder' ? main.querySelector('[data-update-path]').value.trim() : null });
  };
  main.querySelector('[data-save-source]').addEventListener('click', () => run(async () => {
    await saveSource(); feedback(demo ? 'Choix fictif mémorisé dans cette démonstration.' : 'Source mémorisée sur ce poste. Aucune recherche ni connexion effectuée.');
  }));
  main.querySelector('[data-update-path]').addEventListener('input', invalidateOffer);
  main.querySelector('[data-update-hash]').addEventListener('input', invalidateOffer);
  main.querySelector('[data-browse]').addEventListener('click', () => run(async () => { const path = await choose(source.value === 'folder' ? 'folder' : 'updateZip', source.value === 'folder' ? 'updateFolderPath' : 'updateZipPath'); if (path && active) { main.querySelector('[data-update-path]').value = path; invalidateOffer(); } }));
  main.querySelector('[data-inspect]').addEventListener('click', () => run(async () => {
    const args = { kind: source.value, path: main.querySelector('[data-update-path]').value.trim() };
    if (source.value === 'manualZip') args.sha256 = main.querySelector('[data-update-hash]').value.trim() || null;
    await saveSource();
    const found = await call('updates.inspect', args); if (!active) return; offer = found;
    main.querySelector('[data-offer]').innerHTML = `<div class="workflow-result"><strong>${esc(found.version)}${found.demonstration ? ' · fictive' : ''}</strong><span>${esc(found.fileName)} · ${(found.bytes / 1048576).toLocaleString('fr-FR',{maximumFractionDigits:1})} Mio</span><code>SHA-256 : ${esc(found.sha256)}</code><p>${esc(found.integrityNotice)}</p>${(found.warnings || []).map((warning) => `<p>${esc(warning)}</p>`).join('')}</div>`;
    main.querySelector('[data-prepare]').hidden = !found.newer;
    showUpdate(await call('updates.read', {}));
  }));
  main.querySelector('[data-prepare]').addEventListener('click', () => run(async () => {
    if (!offer || !await confirmUnlessNative(ctx.session, () => confirmDialog({ title: 'Préparer cette mise à jour', message: `Le pack ${offer.version} sera ${demo ? 'simulé en mémoire' : 'copié et validé dans un dossier temporaire local'}. Cette préparation peut prendre plusieurs minutes. L’application installée reste inchangée jusqu’à votre accord suivant.`, confirmLabel: 'Confirmer la préparation' }))) return;
    showUpdate(await call('updates.prepare', { offerId: offer.id, confirmed: true }));
    if (active) main.querySelector('[data-prepare]').hidden = true;
  }));
  for (const rollback of [false,true]) main.querySelector(rollback ? '[data-rollback]' : '[data-install]').addEventListener('click', () => run(async () => {
    const title = rollback ? 'Revenir à la version précédente' : 'Installer la mise à jour';
    if (!update?.transactionId || !await confirmUnlessNative(ctx.session, () => confirmDialog({ title, message: demo ? 'Seule la version fictive de cette démonstration sera modifiée.' : 'FILPRO se ferme proprement. L’assistant conserve la version précédente et attend la vérification du démarrage avant de valider l’installation.', confirmLabel: title }))) return;
    const op = demo ? `demonstration.update${rollback ? 'Rollback' : 'Install'}` : `updates.${rollback ? 'rollback' : 'install'}`;
    const result = await call(op, { transactionId: update.transactionId, confirmed: true });
    feedback(result.notice); if (!result.closeApplicationRequired) showUpdate(await call('updates.read', {}));
  }));
  main.querySelector('[data-scenario]')?.addEventListener('change', (event) => run(async () => {
    try { const next = await call('demonstration.scenario', { outcome: event.target.value }); catalog = next; feedback('Scénario fictif sélectionné pour la prochaine opération.'); }
    finally { if (active) event.target.value = catalog?.selectedScenario ?? 'succeeded'; }
  }));
  const unlisten = onEvent('updates.changed', showUpdate);
  const leave = ctx.guardLeave?.(() => !dirty || confirmDialog({ title: 'Réglages non enregistrés', message: 'Quitter cette page abandonne les changements non enregistrés. Les réglages déjà sauvegardés restent conservés.', confirmLabel: 'Quitter sans enregistrer' }));
  async function load() {
    if (busy) return; busy = true; sync();
    const results = await Promise.allSettled([call('configuration.read', {}), call('updates.read', {}), demo ? call('demonstration.catalog', {}) : Promise.resolve(null), call('sauvegarder.readCommonSelection', {}), call('updates.source', {})]);
    if (!active) return;
    if (results[0].status === 'fulfilled') showConfig(results[0].value); else feedback(errorText(results[0].reason), true);
    if (results[1].status === 'fulfilled') showUpdate(results[1].value); else main.querySelector('[data-update-status]').textContent = errorText(results[1].reason);
    if (demo && results[2].status === 'fulfilled') {
      catalog = results[2].value;
      main.querySelector('[data-scenario]').innerHTML = catalog.scenarios.map((choice) => `<option value="${esc(choice.id)}">${esc(choice.label)}</option>`).join('');
      main.querySelector('[data-scenario]').value = catalog.selectedScenario;
      main.querySelector('[data-demo-count]').textContent = `${catalog.stores.length} magasins et ${catalog.destinations.length} destinations fictifs. Aucun accès aux services réels.`;
      updateSource();
    } else if (demo) feedback(errorText(results[2].reason), true);
    if (results[3].status === 'fulfilled') showCommon(results[3].value); else main.querySelector('[data-common-status]').textContent = errorText(results[3].reason);
    if (results[4].status === 'fulfilled') { sourcePreference = results[4].value; source.value = sourcePreference.kind; updateSource(); }
    else feedback(errorText(results[4].reason), true);
    main.querySelector('[data-config]').removeAttribute('aria-busy');
    busy = false; sync();
  }
  main.querySelector('[data-refresh]').addEventListener('click', async () => { if (!dirty || await confirmDialog({ title:'Actualiser les réglages', message:'Les changements non enregistrés seront abandonnés.', confirmLabel:'Actualiser' })) load(); });
  load(); return () => { if (tokenInput) tokenInput.value = ''; active = false; unlisten(); leave?.(); stopReveal(); };
}
