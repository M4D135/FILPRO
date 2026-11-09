import { call } from './bridge.js';
import { icon } from './icons.js';
import { esc, errorText, formatDateTime } from './ui.js';
import { openDialog } from './kit/index.js';
import { followOperation } from './operations.js';
import { primaryLabel, decisionLine } from './native-confirmation.js';

const policies = [['keepBoth','Coexistence — préserver les deux versions'],['keepCurrent','Garder l’actuel'],['useSaved','Rétablir le sauvegardé']];
const eligible = (profile) => profile.loaded === false && profile.migrationSupported !== false && profile.session?.state === 'none' && profile.session?.count === 0 && profile.session?.readStatus === 'complete';

export function mountRestore(main, ctx) {
  const demo = ctx.session?.demonstration === true;
  main.innerHTML = `<div class="workflow-page"><section class="card"><div class="card-head"><span class="card-icon">${icon('database')}</span><h2>Choisir le magasin</h2></div>
    <div class="workflow-form"><label class="wide">${demo ? 'Magasin fictif' : 'Dossier du magasin'}${demo ? '<select class="input" data-bundle></select>' : '<div class="actions-row"><input class="input mono" type="text" data-bundle autocomplete="off" spellcheck="false"><button class="btn" type="button" data-pick>Parcourir</button></div>'}</label></div>
    <div class="actions-row"><button class="btn" type="button" data-inspect>Lire le manifeste</button></div><div data-store-info role="status"></div></section>
    <section class="card"><div class="card-head"><span class="card-icon">${icon('users')}</span><h2>Correspondances de profils</h2></div><p>Chaque source sélectionnée est associée explicitement à son profil cible. Les profils chargés, courants ou non pris en charge sont refusés.</p><div data-mappings><p class="muted">Lisez d’abord un magasin.</p></div></section>
    <section class="card"><div class="card-head"><span class="card-icon">${icon('shield')}</span><h2>Réglages et sauvegarde préalable</h2></div><div data-policies></div>
    <div class="workflow-form"><label class="wide">Dossier de sauvegarde préalable<input class="input mono" data-prior type="text" autocomplete="off" spellcheck="false"></label><label class="wide">Clé indépendante de la sauvegarde préalable${demo ? ' (fictive, facultative)' : ''}<input class="input mono" data-prior-key type="text" autocomplete="off" spellcheck="false"></label>
    <label class="wide" data-source-key-row hidden>Clé du magasin source<input class="input mono" data-source-key type="text" autocomplete="off" spellcheck="false"></label>
    <label class="check-row wide" data-partial-row hidden><input type="checkbox" class="k-checkbox" data-partial-ack><span>J’ai examiné les omissions annoncées du magasin partiel.</span></label><label class="check-row wide" data-legacy-row hidden><input type="checkbox" class="k-checkbox" data-legacy-ack><span>J’ai examiné les limites du magasin ancien.</span></label></div></section>
    <div data-error role="alert"></div><div class="actions-row"><button class="btn primary" type="button" data-plan disabled>Examiner le plan de restauration</button></div></div>`;
  let active = true; let busy = false; let catalog = null; let profiles = []; let inspection = null; let request = 0;
  const selected = new Set(); const targets = new Map(); const choices = new Map();
  const bundle = main.querySelector('[data-bundle]');
  const showError = (err) => { if (active) main.querySelector('[data-error]').innerHTML = `<p class="callout alert">${esc(errorText(err))}</p>`; };
  const clearInspection = () => {
    request++; inspection = null; selected.clear(); targets.clear(); choices.clear();
    main.querySelector('[data-plan]').disabled = true;
    main.querySelector('[data-mappings]').innerHTML = '<p class="muted">Lisez le manifeste de ce magasin.</p>';
    main.querySelector('[data-policies]').innerHTML = ''; main.querySelector('[data-store-info]').innerHTML = '';
    main.querySelector('[data-source-key]').value = '';
    main.querySelector('[data-partial-ack]').checked = false; main.querySelector('[data-legacy-ack]').checked = false;
  };
  bundle.addEventListener('change', clearInspection);
  bundle.addEventListener('input', () => { if (inspection) clearInspection(); });
  main.querySelector('[data-pick]')?.addEventListener('click', async () => {
    try { const result = await call('session.choosePath', { kind:'folder' }); if (active && result.path) { bundle.value = result.path; clearInspection(); } } catch (err) { showError(err); }
  });
  const renderMappings = () => {
    if (!inspection) return;
    const rows = inspection.sourceProfiles || [];
    main.querySelector('[data-mappings]').innerHTML = `<div class="workflow-table-wrap"><table class="workflow-table"><thead><tr><th>Inclure</th><th>Profil source</th><th>Profil cible</th></tr></thead><tbody>${rows.map((source, index) => `<tr><td><input class="k-checkbox" type="checkbox" data-source-index="${index}" aria-label="Restaurer ${esc(source.account)}"${selected.has(source.account) ? ' checked' : ''}></td><td><strong>${esc(source.account)}</strong><small class="block mono">${esc(source.sid || 'Identité source absente du magasin ancien')}</small></td><td><select class="input" data-target-index="${index}" aria-label="Destination de ${esc(source.account)}"><option value="">Choisir un profil cible</option>${profiles.map((profile) => `<option value="${esc(profile.sid)}"${targets.get(source.account) === profile.sid ? ' selected' : ''}${eligible(profile) ? '' : ' disabled'}>${esc(profile.account)}${eligible(profile) ? '' : ' — indisponible'}</option>`).join('')}</select></td></tr>`).join('')}</tbody></table></div>`;
  };
  main.querySelector('[data-mappings]').addEventListener('change', (event) => {
    const index = event.target.dataset.sourceIndex ?? event.target.dataset.targetIndex;
    const source = inspection?.sourceProfiles?.[Number(index)]; if (!source) return;
    request++;
    if (event.target.hasAttribute('data-source-index')) { if (event.target.checked) selected.add(source.account); else selected.delete(source.account); }
    else targets.set(source.account, event.target.value);
  });
  main.querySelector('[data-policies]').addEventListener('change', (event) => { if (event.target.dataset.policy) { request++; choices.set(event.target.dataset.policy, event.target.value); } });
  for (const input of main.querySelectorAll('[data-prior], [data-prior-key], [data-source-key], [data-partial-ack], [data-legacy-ack]')) input.addEventListener('input', () => { request++; });
  async function inspect() {
    if (busy) return; clearInspection(); busy = true; const mine = ++request; const path = bundle.value.trim();
    main.querySelector('[data-inspect]').disabled = true;
    try {
      const found = await call('sauvegarder.inspectStore', { path });
      if (!active || mine !== request || path !== bundle.value.trim()) return;
      inspection = found; selected.clear(); targets.clear(); choices.clear();
      for (const source of found.sourceProfiles || []) {
        selected.add(source.account);
        const matching = profiles.find((profile) => eligible(profile) && profile.account.toLowerCase() === source.account.toLowerCase());
        if (matching) targets.set(source.account, matching.sid);
      }
      const store = found.store;
      main.querySelector('[data-store-info]').innerHTML = `<div class="workflow-result"><strong>${esc(store.status)}</strong><span>${(found.sourceProfiles || []).length} profil(s) · ${(found.entries || []).length} fichier(s) catalogué(s)</span><code>Manifeste : ${esc(found.manifestSha256)}</code><p>${found.payloadVerified ? demo ? 'Contrôle du contenu fictif en mémoire.' : 'Contrôle du contenu effectué par le lecteur.' : 'Le contenu complet sera contrôlé avant l’écriture ; le manifeste seul ne prouve pas son intégrité.'}</p>${(found.warnings || []).map((warning) => `<p>${esc(warning)}</p>`).join('')}</div>`;
      renderMappings();
      const groups = found.conflictChoices;
      main.querySelector('[data-policies]').innerHTML = groups == null ? '<p class="callout warn">Ce magasin ne fournit pas de sélection fiable des réglages. Le moteur décidera si ce parcours est pris en charge.</p>' : groups.length ? `<div class="workflow-form">${groups.map((group) => {
        const mode = ctx.settings?.conflictPolicy || 'keepBoth'; choices.set(group.id, mode);
        return `<label>${esc(group.name)}<select class="input" data-policy="${esc(group.id)}">${policies.map(([id,text]) => `<option value="${id}"${id === mode ? ' selected' : ''}>${text}</option>`).join('')}</select></label>`;
      }).join('')}</div>` : '<p class="muted">Le magasin ne contient aucun réglage de logiciel sélectionné.</p>';
      main.querySelector('[data-source-key-row]').hidden = !store.encrypted;
      main.querySelector('[data-partial-row]').hidden = !store.partial;
      main.querySelector('[data-legacy-row]').hidden = !store.legacy;
      main.querySelector('[data-plan]').disabled = !store.ready || ctx.session?.capabilities?.restore !== true;
      if (ctx.session?.capabilities?.restore !== true) showError(new Error('La restauration n’est pas disponible dans cette session.'));
    } catch (err) { showError(err); }
    finally { busy = false; if (active) main.querySelector('[data-inspect]').disabled = false; }
  }
  main.querySelector('[data-inspect]').addEventListener('click', inspect);
  main.querySelector('[data-plan]').addEventListener('click', async () => {
    if (busy || !inspection) return;
    const users = [...selected];
    if (!users.length || users.some((source) => !targets.get(source))) { showError(new Error('Choisissez une destination pour chaque source sélectionnée.')); return; }
    const targetSids = users.map((source) => targets.get(source));
    if (new Set(targetSids).size !== targetSids.length) { showError(new Error('Deux sources ne peuvent pas restaurer silencieusement le même profil cible.')); return; }
    const store = inspection.store;
    const mappings = users.map((source) => ({ source, target: profiles.find((profile) => profile.sid === targets.get(source)).account }));
    const args = { bundle: inspection.bundle, storeReady: store.ready, manifestUsers: store.manifestUsers || inspection.sourceProfiles.map((source) => source.account), users, mappings, targetSids,
      conflictPolicies: [...choices].map(([id,mode]) => ({ id,mode })), legacy: store.legacy, legacyAcknowledged: main.querySelector('[data-legacy-ack]').checked,
      partial: store.partial, partialAcknowledged: main.querySelector('[data-partial-ack]').checked, encrypted: store.encrypted,
      sourceKeyPath: store.encrypted ? main.querySelector('[data-source-key]').value.trim() || null : null,
      priorRoot: main.querySelector('[data-prior]').value.trim(), priorKeyPath: main.querySelector('[data-prior-key]').value.trim() || null, priorIdentified: false };
    busy = true; const mine = ++request; main.querySelector('[data-plan]').disabled = true;
    try {
      const preview = await call('sauvegarder.restorePlan', args); if (!active || mine !== request) return;
      const plan = preview.plan ?? preview;
      const dialog = openDialog({ title:'Confirmer la restauration', icon:'shield', size:'lg', badge:demo?'Démonstration — données fictives':'Restauration des profils',
        body: `<p>La préparation longue commence seulement après votre accord. Le moteur contrôlera de nouveau le magasin, les profils et la sauvegarde préalable.</p><div class="workflow-table-wrap"><table class="workflow-table"><thead><tr><th>Source</th><th>Cible</th></tr></thead><tbody>${(plan.mappings || []).map((pair) => `<tr><td>${esc(pair.source)}</td><td>${esc(pair.target)}</td></tr>`).join('')}</tbody></table></div><p>${esc(plan.conflictSummary || '')}</p>${(plan.warnings || []).map((warning) => `<p class="callout warn">${esc(warning)}</p>`).join('')}
          <div class="workflow-stack">${[['capacity','Je prends en compte la capacité nécessaire avant la préparation.'],['prior','J’ai prévu une sauvegarde préalable indépendante que le moteur devra vérifier.'],['identity','J’ai relu chaque correspondance de comptes et les décisions de conflit.']].map(([id,text]) => `<label class="k-check-row"><input class="k-checkbox" type="checkbox" data-ack="${id}"><span>${text}</span></label>`).join('')}</div>${decisionLine(ctx.session)}`,
        actions:[{id:'cancel',label:'Annuler',safe:true},{id:'start',label:primaryLabel(ctx.session,'Confirmer la préparation'),primary:true,disabled:true}] });
      const boxes = [...dialog.body.querySelectorAll('[data-ack]')]; boxes.forEach((box) => box.addEventListener('change', () => dialog.setDisabled('start',boxes.some((item) => !item.checked))));
      if (await dialog.result !== 'start' || !active || mine !== request) return;
      const options = { planId:plan.id, acknowledgeCapacity:true, acknowledgePriorBackup:true, acknowledgeIdentityReview:true };
      if (demo) options.confirmed = true;
      const job = await call(demo?'demonstration.restore':'sauvegarder.confirmRestorePlan',options);
      if (active) followOperation(job,ctx);
    } catch (err) { showError(err); }
    finally { busy = false; if (active) main.querySelector('[data-plan]').disabled = !inspection?.store?.ready || ctx.session?.capabilities?.restore !== true; }
  });
  (async () => {
    const results = await Promise.allSettled([call('sauvegarder.profiles',{}),demo?call('demonstration.catalog',{}):Promise.resolve(null)]);
    if (!active) return;
    if (results[0].status === 'fulfilled') { profiles = results[0].value.profiles || []; renderMappings(); } else showError(results[0].reason);
    if (demo && results[1].status === 'fulfilled') {
      catalog = results[1].value;
      bundle.innerHTML = catalog.stores.map((store) => `<option value="${esc(store.bundle)}">${esc(store.label)} · ${esc(store.accounts.join(', '))} · ${esc(formatDateTime(store.createdUtc) || '')}${store.partial?' · partiel':''}</option>`).join('');
      main.querySelector('[data-prior]').value = catalog.assets.safetyBackupRoot;
    } else if (demo) showError(results[1].reason);
  })();
  return () => { active = false; request++; };
}
