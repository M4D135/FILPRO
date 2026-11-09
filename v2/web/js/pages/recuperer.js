import { call, onEvent } from '../bridge.js';
import { icon } from '../icons.js';
import { esc, errorText, applyVars, reveal } from '../ui.js';
import { openDialog } from '../kit/index.js';
import { followOperation } from '../operations.js';
import { primaryLabel, decisionLine } from '../native-confirmation.js';

// Retour depuis le suivi : ce chemin reste dans la mémoire de la génération courante, jamais dans le stockage web.
let rememberedStore = null;

export function render(main, ctx) {
  const demo = ctx.session?.demonstration === true;
  const generation = ctx.session?.generation;
  const previousBundle = rememberedStore?.generation === generation ? rememberedStore.bundle : null;
  main.innerHTML = `<div class="page workflow-page"><header class="page-header reveal"><div class="head-row"><span class="page-icon">${icon('folder')}</span><div class="titles"><h1>Récupérer des fichiers</h1><p class="subtitle">Extraire les éléments d’un magasin vers un nouveau dossier, sans remplacer un profil.</p></div></div></header>
    <section class="card reveal"><div class="card-head"><h2>Magasin source</h2></div><div class="workflow-form"><label class="wide">${demo ? 'Magasin fictif' : 'Dossier du magasin'}${demo?'<select class="input" data-source></select>':'<div class="actions-row"><input class="input mono" type="text" data-source autocomplete="off" spellcheck="false"><button class="btn" type="button" data-pick-source>Parcourir</button></div>'}</label></div><div class="actions-row"><button class="btn" type="button" data-inspect>Lire le catalogue</button><button class="btn primary" type="button" data-catalog hidden>Préparer le catalogue</button></div><div data-info role="status"></div></section>
    <section class="card reveal"><div class="card-head"><h2>Fichiers à récupérer</h2><span class="meta" data-count>—</span></div><label class="workflow-search">Rechercher dans le catalogue<input class="input" type="search" data-search></label><div data-files><p class="muted">Lisez le catalogue du magasin pour choisir ses fichiers.</p></div></section>
    <section class="card reveal"><div class="workflow-form"><label class="wide">Nouveau dossier de destination<input class="input mono" type="text" data-destination autocomplete="off" spellcheck="false"></label><label class="wide" data-key-row hidden>Fichier clé du magasin<input class="input mono" type="text" data-key autocomplete="off" spellcheck="false"></label></div><p class="muted">Le moteur contrôle les chemins, les empreintes et l’absence d’écrasement. Les fichiers absents ou corrompus restent signalés.</p><button class="btn primary" type="button" data-plan disabled>Examiner le plan de récupération</button></section><div data-error role="alert"></div></div>`;
  applyVars(main); const stopReveal = reveal(main);
  let active = true; let busy = false; let inspection = null; let request = 0; let catalogJobId = null;
  const selected = new Set(); const source = main.querySelector('[data-source]');
  const error = (err) => { if (active) main.querySelector('[data-error]').innerHTML = `<p class="callout alert">${esc(errorText(err))}</p>`; };
  const missingCatalog = () => Boolean(inspection && !inspection.catalogComplete && inspection.entries.length === 0);
  const redraw = () => {
    const query = main.querySelector('[data-search]').value.toLocaleLowerCase('fr-FR');
    const entries = (inspection?.entries || []).filter((entry) => entry.relativePath.toLocaleLowerCase('fr-FR').includes(query));
    main.querySelector('[data-count]').textContent = `${selected.size} sélectionné(s)`;
    main.querySelector('[data-files]').innerHTML = entries.length ? `<div class="workflow-table-wrap"><table class="workflow-table"><thead><tr><th>Choisir</th><th>Fichier</th><th>Taille</th></tr></thead><tbody>${entries.map((entry) => `<tr><td><input type="checkbox" class="k-checkbox" data-entry="${esc(entry.id)}" aria-label="Récupérer ${esc(entry.relativePath)}"${selected.has(entry.id)?' checked':''}${busy||entry.corrupted?' disabled':''}></td><td>${esc(entry.relativePath)}${entry.corrupted?' — corrompu':''}</td><td>${entry.bytes == null?'inconnue':`${entry.bytes.toLocaleString('fr-FR')} octets`}</td></tr>`).join('')}</tbody></table></div>` : `<p class="muted">${!inspection ? 'Lisez le catalogue du magasin pour choisir ses fichiers.' : missingCatalog() ? 'Préparez le catalogue avant de choisir les fichiers. Cette étape demande une confirmation.' : 'Aucun fichier ne correspond à cette recherche.'}</p>`;
    main.querySelector('[data-plan]').disabled = busy || !selected.size || ctx.session?.capabilities?.recover !== true;
    main.querySelector('[data-catalog]').hidden = !missingCatalog();
    main.querySelector('[data-catalog]').disabled = busy || Boolean(catalogJobId) || ctx.session?.capabilities?.recover !== true;
    main.querySelector('[data-inspect]').disabled = busy || !source.value.trim();
    for (const input of main.querySelectorAll('[data-source], [data-pick-source], [data-key], [data-destination]')) input.disabled = busy;
  };
  main.querySelector('[data-files]').addEventListener('change', (event) => {
    const id = event.target.dataset.entry; if (!id) return;
    request++;
    if (event.target.checked) { if (selected.size >= 500) { event.target.checked=false; error(new Error('Sélection limitée à 500 fichiers par récupération.')); return; } selected.add(id); } else selected.delete(id);
    main.querySelector('[data-count]').textContent = `${selected.size} sélectionné(s)`;
    main.querySelector('[data-plan]').disabled = busy || !selected.size || ctx.session?.capabilities?.recover !== true;
  });
  main.querySelector('[data-search]').addEventListener('input', redraw);
  const invalidate = () => { request++; inspection=null; selected.clear(); rememberedStore=null; redraw(); main.querySelector('[data-info]').innerHTML=''; };
  source.addEventListener('change',invalidate); source.addEventListener('input', () => { if (inspection) invalidate(); });
  main.querySelector('[data-pick-source]')?.addEventListener('click', async () => { try { const result=await call('session.choosePath',{kind:'folder'}); if(active&&result.path){source.value=result.path;invalidate();} }catch(err){error(err);} });
  async function inspect() {
    if(busy||!source.value.trim())return; invalidate(); busy=true; const mine=++request; const path=source.value.trim(); redraw();
    main.querySelector('[data-error]').innerHTML='';
    try {
      const found=await call('sauvegarder.inspectStore',{path}); if(!active||mine!==request||source.value.trim()!==path)return;
      inspection=found; selected.clear();
      rememberedStore={generation,bundle:found.bundle};
      main.querySelector('[data-info]').innerHTML=`<p class="callout info">${esc(found.store.status)} · ${found.entries.length} fichier(s) catalogué(s). ${missingCatalog()?'Catalogue non préparé : une lecture longue doit être confirmée.':found.catalogComplete?'Catalogue annoncé complet.':'Catalogue partiel : aucune absence n’est déduite.'}</p>${found.sourcePayloadSha256?`<div class="workflow-result"><span>Catalogue lié à l’empreinte de l’archive${demo?' fictive':''}</span><code>${esc(found.sourcePayloadSha256)}</code></div>`:''}${(found.warnings||[]).map((warning)=>`<p class="muted">${esc(warning)}</p>`).join('')}`;
      main.querySelector('[data-key-row]').hidden=!found.store.encrypted;
    }catch(err){error(err);}finally{busy=false;if(active)redraw();}
  }
  main.querySelector('[data-inspect]').addEventListener('click', inspect);
  main.querySelector('[data-catalog]').addEventListener('click', async () => {
    if(busy||catalogJobId||!missingCatalog())return;
    const found=inspection; const mine=++request; busy=true; redraw();
    try {
      const dialog=openDialog({title:'Préparer le catalogue du magasin',icon:'folder',size:'md',badge:demo?'Démonstration — données fictives':'Lecture du magasin',
        body:`<p>Le moteur va vérifier l’archive et préparer son catalogue. Cette lecture peut prendre plusieurs minutes ; elle commence après votre accord.</p><div class="workflow-result"><span>Magasin</span><code>${esc(found.bundle)}</code></div>${demo?'<p class="callout info">Le catalogue sera construit uniquement dans le monde fictif en mémoire.</p>':''}<label class="k-check-row"><input class="k-checkbox" type="checkbox" data-ack><span>Je confirme cette préparation longue et l’espace temporaire disponible.</span></label>${decisionLine(ctx.session)}`,
        actions:[{id:'cancel',label:'Annuler',safe:true},{id:'catalog',label:primaryLabel(ctx.session,'Confirmer la préparation'),primary:true,disabled:true}]});
      const ack=dialog.body.querySelector('[data-ack]');ack.addEventListener('change',()=>dialog.setDisabled('catalog',!ack.checked));
      if(await dialog.result!=='catalog'||!active||mine!==request||inspection!==found)return;
      const job=await call(demo?'demonstration.catalogStore':'recuperer.catalog',{bundle:found.bundle,keyPath:found.store.encrypted?main.querySelector('[data-key]').value.trim()||null:null,confirmed:true,spaceAcknowledged:true});
      if(!active)return;
      catalogJobId=job.id; rememberedStore={generation,bundle:found.bundle}; followOperation(job,ctx);
    }catch(err){error(err);}finally{busy=false;if(active)redraw();}
  });
  main.querySelector('[data-plan]').addEventListener('click',async()=>{
    if(busy||!inspection)return; busy=true; const mine=++request; redraw();
    try{
      const plan=await call('recuperer.plan',{bundle:inspection.bundle,destination:main.querySelector('[data-destination]').value.trim(),entryIds:[...selected],keyPath:inspection.store.encrypted?main.querySelector('[data-key]').value.trim()||null:null});
      if(!active||mine!==request)return;
      const dialog=openDialog({title:'Confirmer la récupération',icon:'folder',size:'lg',badge:demo?'Démonstration — données fictives':'Extraction de fichiers',
        body:`<p>${esc(plan.notice)}</p><p>Destination : <code>${esc(plan.destination)}</code></p><ul>${plan.entries.map((entry)=>`<li>${esc(entry.relativePath)}</li>`).join('')}</ul>${(plan.warnings||[]).map((warning)=>`<p class="callout warn">${esc(warning)}</p>`).join('')}<label class="k-check-row"><input class="k-checkbox" type="checkbox" data-ack><span>Je confirme le nouveau dossier et la capacité disponible avant la préparation longue.</span></label>${decisionLine(ctx.session)}`,
        actions:[{id:'cancel',label:'Annuler',safe:true},{id:'start',label:primaryLabel(ctx.session,'Confirmer la préparation'),primary:true,disabled:true}]});
      const ack=dialog.body.querySelector('[data-ack]');ack.addEventListener('change',()=>dialog.setDisabled('start',!ack.checked));
      if(await dialog.result!=='start'||!active||mine!==request)return;
      const job=await call(demo?'demonstration.recover':'recuperer.confirmPlan',{planId:plan.id,confirmed:true,acknowledgeCapacity:true}); if(active)followOperation(job,ctx);
    }catch(err){error(err);}finally{busy=false;if(active)redraw();}
  });
  for(const input of main.querySelectorAll('[data-destination], [data-key]'))input.addEventListener('input',()=>{request++;});
  const stopCatalogEvents=onEvent('operations.changed',(job)=>{
    if(!active||job.id!==catalogJobId||!['succeeded','partial','failed','cancelled'].includes(String(job.state).toLowerCase()))return;
    catalogJobId=null; inspect();
  });
  async function initialize() {
    busy=true; redraw();
    let bundle=previousBundle; let available=null;
    try {
      if(demo) {
        const catalog=await call('demonstration.catalog',{}); if(!active)return;
        available=new Set(catalog.stores.map((store)=>store.bundle));
        source.innerHTML=catalog.stores.map((store)=>`<option value="${esc(store.bundle)}">${esc(store.label)} · ${esc(store.accounts.join(', '))}</option>`).join('');
        main.querySelector('[data-destination]').value=catalog.assets.recoveryDestination;
      }
      if(ctx.params?.operation) {
        const job=await call('operations.get',{id:ctx.params.operation}); if(!active)return;
        if(String(job.kind).toLowerCase()!=='catalog'||!['succeeded','partial'].includes(String(job.state).toLowerCase())||job.demonstration!==demo||!job.resultPath)
          throw new Error('Cette opération ne fournit pas de catalogue terminé à relire.');
        bundle=job.resultPath;
      }
      if(bundle) {
        if(available&&!available.has(bundle))throw new Error('Ce magasin ne fait pas partie de la démonstration courante.');
        source.value=bundle;
      }
    }catch(err){bundle=null;error(err);}finally{busy=false;if(active)redraw();}
    if(active&&bundle)await inspect();
  }
  initialize();
  if(ctx.session?.capabilities?.recover!==true)error(new Error('La récupération n’est pas disponible dans cette session.'));
  return()=>{active=false;request++;stopCatalogEvents();stopReveal();};
}
