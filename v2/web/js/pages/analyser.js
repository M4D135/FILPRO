// Inventaire du fournisseur actif puis plan de réparation contrôlé, avant confirmation et suivi de l'exécution.
import { call } from '../bridge.js';
import { icon } from '../icons.js';
import { esc, errorText, applyVars, reveal, formatNumber, formatDateTime } from '../ui.js';
import { pill, openDialog } from '../kit/index.js';
import { followOperation } from '../operations.js';
import { primaryLabel, decisionLine, confirmUnlessNative } from '../native-confirmation.js';

const SESSION_STATE = {
  active: { tone: 'ok', label: 'Session active' },
  disconnected: { tone: 'warn', label: 'Déconnectée' },
  none: { tone: 'off', label: 'Aucune session' },
  unknown: { tone: 'warn', label: 'Sessions inconnues' },
};
const SIZE_STATE = {
  mesuree: { tone: 'ok', label: 'mesurés' },
  partielle: { tone: 'warn', label: 'partiel' },
  inconnue: { tone: 'off', label: 'taille inconnue' },
  aucun: { tone: 'off', label: 'aucun dossier' },
};
const DEMO_PROFILE_STATE = {
  original: 'État fictif initial', restored: 'Restauration fictive terminée', restorePartial: 'Restauration fictive partielle à examiner',
  repairPrepared: 'Réparation fictive préparée ; nouveau profil à initialiser', repairPartial: 'Réparation fictive partielle à examiner',
};

function syncRepair(main, state, ctx) {
  const profile = state.profiles.find((item) => item.sid === state.sid);
  const safe = profile?.loaded === false && profile?.migrationSupported !== false && profile?.session?.state === 'none'
    && profile?.session?.count === 0 && profile?.session?.readStatus === 'complete';
  main.querySelector('[data-repair]').disabled = state.planning || state.resuming || state.profileLoading || !safe || ctx.session?.capabilities?.repair !== true;
  main.querySelector('[data-resume-repair]').disabled = state.planning || state.resuming || ctx.session?.capabilities?.repair !== true
    || (ctx.session?.demonstration === true && !state.receipts.size);
  main.querySelector('[data-repair-receipt]').disabled = state.planning || state.resuming || !state.receipts.size;
  const base = ctx.session?.capabilities?.repair !== true ? 'Le service de réparation est indisponible dans cette session.'
    : !safe ? 'La réparation exige un profil non chargé, sans session active ou déconnectée, et un inventaire des sessions complet.'
      : `${profile.account} : le moteur examinera la sauvegarde préalable et la préservation du profil avant la réparation.`;
  const fictitious = state.demoStates?.get(state.sid);
  main.querySelector('[data-repair-notice]').textContent = base + (fictitious ? ` ${DEMO_PROFILE_STATE[fictitious.state] ?? fictitious.state}.` : '');
}

function loadedPill(loaded) {
  if (loaded === true) return pill('ok', 'Ruche chargée', { size: 'sm' });
  if (loaded === false) return pill('off', 'Non chargée', { size: 'sm' });
  return pill('warn', 'Chargement inconnu', { size: 'sm' });
}

function sessionPill(session) {
  const s = SESSION_STATE[String(session?.state).toLowerCase()] ?? SESSION_STATE.unknown;
  const count = session?.count;
  const label = typeof count === 'number' ? `${s.label} · ${formatNumber(count)}` : s.label;
  return pill(s.tone, label, { size: 'sm' });
}

function sizePill(size) {
  const s = SIZE_STATE[String(size?.state).toLowerCase()] ?? SIZE_STATE.inconnue;
  return pill(s.tone, size?.text || s.label, { size: 'sm' });
}

function triState(group) {
  if (group.selected === true) return pill('ok', 'tout retenu', { size: 'sm' });
  if (group.selected === false) return pill('off', 'rien', { size: 'sm' });
  return pill('info', `${formatNumber(group.selectedCount)} retenu(s)`, { size: 'sm' });
}

function layout() {
  return `<div class="page analyser">
    <header class="page-header reveal">
      <div class="head-row"><span class="page-icon">${icon('tools')}</span>
        <div class="titles"><h1 class="gradient-text">Analyser / réparer</h1>
          <p class="subtitle">Examiner les profils, leurs sessions et préparer une réparation protégée.</p></div></div>
      <div class="actions"><button class="btn" type="button" data-act="refresh">${icon('refresh')}Actualiser</button></div>
    </header>
    <section class="hero reveal spot">
      <span class="glow-ring" aria-hidden="true"></span>
      <div class="eyebrow">${icon('shield')}<span>Examen avant confirmation</span></div>
      <h2>Profils et logiciels du poste</h2>
      <p class="lead">Choisissez un profil pour voir ses sessions et la sélection groupée fichiers + registre.
        Une réparation passe par un plan explicite et une confirmation avant toute préparation longue.</p>
    </section>
    <div class="grid">
      <section class="card span-5 reveal spot" data-card="profiles" aria-busy="true">
        <div class="card-head"><span class="card-icon">${icon('users')}</span><h3>Profils</h3><span class="meta" data-meta="profiles">—</span></div>
        <div class="list" data-list="profiles"></div>
      </section>
      <section class="card span-7 reveal spot" data-card="detail" aria-busy="true">
        <div class="card-head"><span class="card-icon">${icon('object')}</span><h3>Sélection des logiciels</h3><span class="meta" data-meta="detail">—</span></div>
        <div data-detail></div>
      </section>
    </div>
    <section class="card" data-repair-card><div class="card-head"><span class="card-icon">${icon('shield')}</span><h3>Réparation protégée</h3></div>
      <p data-repair-notice>Choisissez un profil pour connaître les actions disponibles.</p><div class="k-field"><label for="repair-safety">Dossier de sauvegarde préalable indépendant</label><input class="input mono" id="repair-safety" data-safety type="text" autocomplete="off" spellcheck="false"></div>
      <button class="btn" type="button" data-repair disabled>Examiner le plan de réparation</button>
      <div class="workflow-result"><h4>Reprendre une réparation</h4><p>Un reçu conservé permet d’examiner le retour à l’état préservé, y compris après la fermeture de FILPRO. Le moteur vérifiera le reçu et les données avant toute modification.</p>
        <label class="k-field" data-receipt-choice hidden>Reçu de cette démonstration<select class="input" data-repair-receipt></select></label>
        <p class="muted" data-receipt-status></p><button class="btn" type="button" data-resume-repair disabled>Reprendre depuis un reçu de réparation</button></div>
      <div data-repair-error role="alert"></div></section>
  </div>`;
}

function empty(text) {
  return `<div class="empty"><div class="art">${icon('info')}</div><p>${esc(text)}</p></div>`;
}

function renderProfiles(list, meta, inventory) {
  const profiles = inventory?.profiles ?? [];
  const status = String(inventory?.sessionReadStatus ?? 'unavailable').toLowerCase();
  meta.textContent = status === 'complete' ? 'sessions lues' : status === 'partial' ? 'sessions partielles' : 'sessions non lues';
  if (!profiles.length) { list.innerHTML = empty('Aucun profil lu. ' + (inventory?.sessionError || 'Le fournisseur n’a renvoyé aucun profil.')); return; }
  list.innerHTML = profiles.map((p, i) => `<div class="list-row" role="button" tabindex="0" data-sid="${esc(p.sid)}">
    <span class="avatar" aria-hidden="true">${esc((p.account || '?').replace(/^[^\\]*\\/, '').slice(0, 1).toUpperCase())}</span>
    <div class="body"><div class="title">${esc(p.account)}</div>
      <div class="sub mono">${esc(p.sid)}${p.localPath ? ' · ' + esc(p.localPath) : ''}</div>
      <div class="sub">${esc(p.identityKind === 'entra' ? 'Entra (hors périmètre USMT)' : 'Compte local ou domaine')}</div></div>
    <div class="row-pills">${sessionPill(p.session)}${loadedPill(p.loaded)}</div>
  </div>`).join('');
}

function renderSoftware(host, inventory, last) {
  if (!inventory) { host.innerHTML = empty('Aucune donnée de sélection.'); return; }
  const groups = inventory.groups ?? [];
  const notes = [];
  if (inventory.registryError) notes.push(inventory.registryError);
  if (inventory.discovery && inventory.discovery.filesComplete === false) notes.push('Découverte des fichiers partielle : une liste incomplète ne prouve pas l’absence d’un logiciel.');
  const noteBlock = notes.length ? `<div class="callout warn">${icon('info')}<div>${notes.map((n) => `<p>${esc(n)}</p>`).join('')}</div></div>` : '';
  const lastBlock = last
    ? `<div class="callout info">${icon('clock')}<div><p>Dernière sélection enregistrée pour ce compte (${esc(formatDateTime(last.capturedAt) ?? '')}) : ${formatNumber((last.files ?? []).length)} fichier(s), ${formatNumber((last.registry ?? []).length)} clé(s).</p></div></div>`
    : `<div class="callout off">${icon('info')}<div><p>Aucune dernière sélection enregistrée pour ce compte.</p></div></div>`;
  if (!groups.length) { host.innerHTML = noteBlock + lastBlock + empty('Aucun logiciel détecté pour ce profil.'); return; }
  // Ces cartes arrivent après le premier reveal(main) : l'entrée du kit doit être autonome.
  const cards = groups.map((g) => `<div class="card nested pop spot">
    <div class="card-head"><span class="card-icon">${icon('chip')}</span><h3>${esc(g.name)}</h3>
      <span class="meta">${g.linked ? 'logiciel reconnu' : 'périmètre propre'}</span></div>
    <div class="list">
      <div class="list-row"><div class="body"><div class="title">${icon('folder')} Fichiers (${formatNumber((g.files ?? []).length)})</div>
        <div class="sub mono">${(g.files ?? []).map((f) => `${f.scope}\\${esc(f.rel)}`).join(' · ') || 'aucun'}</div></div>${sizePill(g.size)}</div>
      <div class="list-row"><div class="body"><div class="title">${icon('key')} Clés du registre (${formatNumber((g.registry ?? []).length)})</div>
        <div class="sub mono">${(g.registry ?? []).map((k) => 'HKCU\\' + esc(k.rel)).join(' · ') || 'aucune'}</div></div>${triState(g)}</div>
    </div>
  </div>`).join('');
  host.innerHTML = noteBlock + lastBlock + `<div class="stack">${cards}</div>`;
}

async function loadSoftware(main, sid, state) {
  const request = ++state.softwareRequest;
  const current = () => state.active && state.sid === sid && state.softwareRequest === request;
  const host = main.querySelector('[data-detail]');
  const meta = main.querySelector('[data-meta="detail"]');
  const card = main.querySelector('[data-card="detail"]');
  card?.setAttribute('aria-busy', 'true');
  host.innerHTML = empty('Lecture de la sélection…');
  try {
    const [inventory, last] = await Promise.all([
      call('analyser.software', { sid }),
      call('analyser.lastSelection', { sid }).catch(() => null),
    ]);
    if (!current()) return;
    meta.textContent = `${formatNumber((inventory?.groups ?? []).length)} logiciel(s)`;
    renderSoftware(host, inventory, last);
    applyVars(host);
  } catch (err) {
    console.error(err);
    if (current()) host.innerHTML = empty(errorText(err, 'Sélection indisponible.'));
  } finally {
    if (current()) card?.removeAttribute('aria-busy');
  }
}

async function load(main, state, ctx) {
  const request = ++state.profileRequest;
  const current = () => state.active && state.profileRequest === request;
  const list = main.querySelector('[data-list="profiles"]');
  const meta = main.querySelector('[data-meta="profiles"]');
  const card = main.querySelector('[data-card="profiles"]');
  state.profileLoading = true; syncRepair(main, state, ctx);
  card?.setAttribute('aria-busy', 'true');
  list.innerHTML = empty('Lecture des profils…');
  try {
    const inventory = await call('analyser.profiles', {});
    if (!current()) return;
    state.profiles = inventory.profiles || [];
    renderProfiles(list, meta, inventory);
    const rows = [...list.querySelectorAll('[data-sid]')];
    const select = (el) => {
      rows.forEach((r) => r.classList.toggle('is-active', r === el));
      state.sid = el.getAttribute('data-sid');
      syncRepair(main, state, ctx);
      loadSoftware(main, state.sid, state);
    };
    rows.forEach((r) => {
      r.addEventListener('click', () => select(r));
      r.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(r); } });
    });
    if (rows.length) select(rows.find((row) => row.dataset.sid === state.sid) ?? rows[0]);
    else { state.sid = null; main.querySelector('[data-detail]').innerHTML = empty('Aucun profil disponible pour cet inventaire.'); main.querySelector('[data-card="detail]').removeAttribute('aria-busy'); }
  } catch (err) {
    console.error(err);
    if (current()) {
      state.profiles = []; state.sid = null;
      list.innerHTML = empty(errorText(err, 'Profils indisponibles.'));
      main.querySelector('[data-detail]').innerHTML = empty('Relancez l’inventaire des profils pour consulter les logiciels.');
      main.querySelector('[data-card="detail]').removeAttribute('aria-busy');
    }
  } finally {
    if (current()) { state.profileLoading = false; card?.removeAttribute('aria-busy'); syncRepair(main, state, ctx); }
  }
}

export function render(main, ctx) {
  main.innerHTML = layout();
  applyVars(main);
  const stop = reveal(main);
  const state = { active:true, sid:null, profiles:[], profileRequest:0, softwareRequest:0, planning:false, resuming:false, profileLoading:false, demoStates:new Map(), receipts:new Map() };
  const demo=ctx.session?.demonstration===true;
  const loadDemoState=async()=>{
    if(!demo)return;
    const catalog=await call('demonstration.catalog',{});if(!state.active)return;
    main.querySelector('[data-safety]').value=catalog.assets.safetyBackupRoot;
    state.demoStates=new Map((catalog.profileStates??[]).map((profile)=>[profile.sid,profile]));
    state.receipts=new Map((catalog.repairReceipts??[]).map((receipt)=>[receipt.receiptPath,receipt]));
    const select=main.querySelector('[data-repair-receipt]');const previous=select.value;
    select.innerHTML=[...state.receipts.values()].map((receipt)=>`<option value="${esc(receipt.receiptPath)}">${esc(receipt.account)} · ${esc(receipt.receiptPath)}</option>`).join('');
    if(state.receipts.has(previous))select.value=previous;
    main.querySelector('[data-receipt-choice]').hidden=false;
    main.querySelector('[data-receipt-status]').textContent=state.receipts.size?'Seuls les reçus créés dans cette démonstration peuvent être choisis.':'Aucun reçu fictif disponible. Préparez une réparation pour essayer ce retour arrière.';
    syncRepair(main,state,ctx);
  };
  const demoError=(err)=>{if(state.active)ctx.toast(errorText(err),{tone:'warn'});};
  main.querySelector('[data-act="refresh"]').addEventListener('click', () => {load(main,state,ctx);loadDemoState().catch(demoError);});
  main.querySelector('[data-repair]').addEventListener('click',async()=>{
    if(state.planning||state.resuming||!state.sid)return;state.planning=true;const sid=state.sid;
    syncRepair(main,state,ctx);
    const button=main.querySelector('[data-repair]');button.disabled=true;
    try{
      const plan=await call('analyser.repairPlan',{sid,safetyBackupRoot:main.querySelector('[data-safety]').value.trim()});
      if(!state.active||sid!==state.sid)return;
      const dialog=openDialog({title:'Confirmer la réparation du profil',icon:'shield',size:'lg',badge:plan.demonstration?'Démonstration — données fictives':'Réparation du profil',
        body:`<p>${esc(plan.notice)}</p><dl class="workflow-facts"><div><dt>Profil</dt><dd>${esc(plan.account)}</dd></div><div><dt>Ancien dossier préservé</dt><dd>${esc(plan.preservedProfilePath)}</dd></div><div><dt>Sauvegarde préalable</dt><dd>${esc(plan.safetyBackupRoot)}</dd></div></dl><ol>${plan.actions.map((action)=>`<li>${esc(action)}</li>`).join('')}</ol>${(plan.warnings||[]).map((warning)=>`<p class="callout warn">${esc(warning)}</p>`).join('')}<label class="k-check-row"><input class="k-checkbox" type="checkbox" data-ack><span>J’ai relu le profil, la sauvegarde préalable et les actions prévues. Je confirme la préparation longue.</span></label>${decisionLine(ctx.session)}`,
        actions:[{id:'cancel',label:'Annuler',safe:true},{id:'start',label:primaryLabel(ctx.session,'Confirmer la préparation'),primary:true,disabled:true}]});
      const ack=dialog.body.querySelector('[data-ack]');ack.addEventListener('change',()=>dialog.setDisabled('start',!ack.checked));
      if(await dialog.result!=='start'||!state.active||state.sid!==sid)return;
      const job=await call(ctx.session?.demonstration?'demonstration.repair':'analyser.confirmRepair',{planId:plan.id,confirmed:true,acknowledgeCapacity:true});
      if(state.active)followOperation(job,ctx);
    }catch(err){if(state.active)main.querySelector('[data-repair-error]').innerHTML=`<p class="callout alert">${esc(errorText(err))}</p>`;}
    finally{state.planning=false;if(state.active)syncRepair(main,state,ctx);}
  });
  main.querySelector('[data-resume-repair]').addEventListener('click',async()=>{
    if(state.planning||state.resuming)return;state.resuming=true;syncRepair(main,state,ctx);
    main.querySelector('[data-repair-error]').innerHTML='';
    try{
      const receiptPath=demo?main.querySelector('[data-repair-receipt]').value:(await call('session.choosePath',{kind:'repairReceipt'}))?.path;
      if(!receiptPath||!state.active)return;
      if(demo&&!state.receipts.has(receiptPath))throw new Error('Choisissez un reçu connu de cette démonstration.');
      const ask=()=>openDialog({title:'Retour arrière de la réparation',icon:'shield',size:'md',badge:demo?'Démonstration — reçu fictif':'Reçu de réparation',
        body:`<p>${demo?'Le monde fictif':'Le moteur'} relira le reçu et vérifiera que le profil peut revenir à son état préservé. Toute incohérence bloque l’action.</p><div class="workflow-result"><span>Reçu choisi</span><code>${esc(receiptPath)}</code></div><p>Confirmez la préparation du retour arrière.</p>`,
        actions:[{id:'cancel',label:'Annuler',safe:true},{id:'ok',label:'Confirmer le retour arrière',primary:true}]}).result.then((answer)=>answer==='ok');
      if(!await confirmUnlessNative(ctx.session,ask)||!state.active)return;
      const job=await call(demo?'demonstration.repairRollback':'analyser.rollbackRepair',{receiptPath,confirmed:true});
      if(state.active)followOperation(job,ctx);
    }catch(err){if(state.active)main.querySelector('[data-repair-error]').innerHTML=`<p class="callout alert">${esc(errorText(err))}</p>`;}
    finally{state.resuming=false;if(state.active)syncRepair(main,state,ctx);}
  });
  loadDemoState().catch(demoError);
  load(main,state,ctx);
  return () => { state.active=false;state.profileRequest++;state.softwareRequest++;stop(); };
}
