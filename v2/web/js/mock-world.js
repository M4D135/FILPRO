// Prévisualisation navigateur de la démonstration : mémoire uniquement, aucun fetch, fichier, registre ou pont natif.
// Les opérations natives restent refusées dans mock.js, même avec des arguments de mode forgés.
import { profilesInventory, softwareInventory, backupPlan, restorePlan, resetSelections, setRecommendationPreference } from './mock-poste.js';

const clone = (value) => structuredClone(value);
const now = () => new Date().toISOString();
const id = () => globalThis.crypto.randomUUID().replaceAll('-', '');
const fail = (message, code = 'demonstration.boundary') => { throw Object.assign(new Error(message), { code }); };
const terminal = (job) => ['succeeded','partial','cancelled','failed'].includes(job.state);
const destinations = [
  { id:'local',label:'Atelier — disque local fictif',path:'D:\\FILPRO-DEMO\\Local',kind:'local' },
  { id:'usb',label:'Intervention — clé USB fictive',path:'E:\\FILPRO-DEMO\\USB',kind:'usb' },
  { id:'lan',label:'Équipe — partage fictif',path:'\\\\partage.example.invalid\\FILPRO-DEMO',kind:'unc' },
  { id:'reserve',label:'Réserve — second poste fictif',path:'D:\\FILPRO-DEMO\\Reserve',kind:'local' },
];
const assets = { importConfigurationPath:'D:\\FILPRO-DEMO\\Local\\configuration-exemple.json',exportConfigurationPath:'D:\\FILPRO-DEMO\\Local\\export-demonstration.json',updateFolderPath:'D:\\FILPRO-DEMO\\Local\\MisesAJour',updateZipPath:'D:\\FILPRO-DEMO\\Local\\MisesAJour\\FILPRO_DEMO_PROD.zip',recoveryDestination:'D:\\FILPRO-DEMO\\Local\\Recuperation',safetyBackupRoot:'D:\\FILPRO-DEMO\\Reserve\\Prealable' };
const scenarios = [{id:'succeeded',label:'Parcours complet',description:'Préparation et résultat fictif réussis.'},{id:'partial',label:'Résultat partiel',description:'Omission fictive signalée.'},{id:'failed',label:'Erreur contrôlée',description:'Erreur fictive, état précédent conservé.'}];
const defaults = {theme:'system',motion:'system',navigationCollapsed:false,autoSelectCurrentTask:true,notifyOnCompletion:true,recommendedSelection:true,migApp:false,vss:false,continueOnError:false,encrypted:false,conflictPolicy:'keepBoth'};
let generation = id(); let outcome = 'succeeded'; let sequence = 0; let sink = () => {};
const stores = new Map(); const jobs = new Map(); const plans = new Map(); const timers = new Map(); const receipts = new Map(); const profileStates = new Map();
let settings = clone(defaults); let revision = id(); let bootstrap = null;
const documents = new Map([[assets.importConfigurationPath,{...defaults,theme:'dark',motion:'reduced',navigationCollapsed:true}]]);
const previews = new Map();
let update = {demonstration:true,stage:'idle',transactionId:null,currentVersion:'2.0.1-demo',targetVersion:null,action:'Aucune recherche de mise à jour engagée.',progressPercent:null,canInstall:false,canRollback:false,error:null,warnings:['Démonstration — aucun binaire réel remplacé.']};
let offer = null; let previousVersion = null;
const defaultSource = {demonstration:true,kind:'folder',folder:null}; let sourcePref = {...defaultSource};

function closed(args, allowed, required = []) {
  if (!args || typeof args !== 'object' || Array.isArray(args) || Object.keys(args).some((key) => !allowed.includes(key)) || required.some((key) => !(key in args))) fail('Arguments absents ou hors contrat.','arguments.invalid');
}
function confirmed(args) { if(args.confirmed !== true) fail('Confirmation explicite requise.','execution.confirmation'); }
function pathAllowed(path) {
  if(typeof path!=='string'||path.length>1024||path.includes('/')||/[\x00-\x1f]/.test(path)||path.split('\\').some((part)=>part==='.'||part==='..')||!destinations.some((d)=>path.toLowerCase()===d.path.toLowerCase()||path.toLowerCase().startsWith(d.path.toLowerCase()+'\\'))) fail('Choisir un emplacement du catalogue fictif ; aucun chemin réel n’est utilisé.');
  return path;
}
function target(sid) {
  const profile=profilesInventory().profiles.find((item)=>item.sid===sid);
  if(!profile||profile.loaded!==false||!profile.migrationSupported||profile.session.state!=='none'||profile.session.count!==0||profile.session.readStatus!=='complete')fail('Profil chargé, session ouverte ou identité fictive non prise en charge.','restore.target');
  return profile;
}
function storeAt(path) { const store=[...stores.values()].find((item)=>item.bundle.toLowerCase()===String(path).toLowerCase());if(!store)fail('Magasin absent du catalogue fictif. Aucun chemin réel n’est ouvert.');return store; }
function addStore(profiles,destination,partial=false,createdAt=now(),groups=null,encrypted=false,migApp=false) {
  const storeId=`DEMO-SAV-${String(++sequence).padStart(3,'0')}`;
  const bundle=destination+'\\'+storeId;
  const sourceProfiles=profiles.map((profile)=>({account:profile.account,sid:profile.sid,profilePath:profile.profilePath??profile.localPath}));
  const key=(item)=>`${item.scope}|${item.rel}`.toLowerCase();
  const selections=profiles.map((profile)=>{
    const known=softwareInventory(profile.sid).groups;
    const files=profile.files??known.flatMap((group)=>group.files).filter((item)=>item.selected);
    const registry=profile.registry??known.flatMap((group)=>group.registry).filter((item)=>item.selected);
    const fileIds=new Set(files.map(key));const registryIds=new Set(registry.map(key));
    return {files,groups:known.map((group)=>({...group,files:group.files.filter((item)=>fileIds.has(key(item))),registry:group.registry.filter((item)=>registryIds.has(key(item)))})).filter((group)=>group.files.length+group.registry.length>0)};
  });
  const retained=new Map();
  for(const group of groups??selections.flatMap((selection)=>selection.groups)){
    const previous=retained.get(group.id);
    const unique=(items)=>[...new Map(items.map((item)=>[key(item),{...item,selected:true}])).values()];
    const files=unique([...(previous?.files??[]),...group.files]);
    const registry=unique([...(previous?.registry??[]),...group.registry]);
    retained.set(group.id,{...group,files,registry,selected:true,selectedCount:files.length+registry.length});
  }
  const entries=sourceProfiles.flatMap((profile,index)=>[
    {id:`${storeId}-${index}-doc`,relativePath:`profiles/${index}/Documents/Présentation fictive.txt`,bytes:640,corrupted:false},
    {id:`${storeId}-${index}-desk`,relativePath:`profiles/${index}/Desktop/Lisez-moi-démo.txt`,bytes:320,corrupted:false},
    ...selections[index].files.map((file,n)=>({id:`${storeId}-${index}-app-${n}`,relativePath:`profiles/${index}/AppData/${file.scope}/${file.rel.replaceAll('\\','/')}/exemple-fictif.txt`,bytes:128,corrupted:false})),
  ]);
  const store={id:storeId,bundle,label:`Magasin fictif ${storeId}`,createdUtc:createdAt,accounts:sourceProfiles.map((p)=>p.account),state:partial?'partial':'succeeded',partial,encrypted};
  const inspection={id:storeId,bundle,manifestSha256:'a'.repeat(64),sourceProfiles,entries,catalogComplete:!migApp,payloadVerified:true,
    store:{bundle,ready:true,legacy:false,partial,simulated:true,encrypted,status:partial?'Partiel fictif':'Prêt fictif',manifestUsers:store.accounts,sourceComputerName:'POSTE-DEMO.example.invalid',sourceCreatedUtc:createdAt,usmtVersion:null},
    warnings:[partial?'Magasin fictif partiel : fichier verrouillé omis.':'Manifeste et contrôles fictifs en mémoire.'],conflictChoices:clone([...retained.values()])};
  if(migApp)inspection.warnings.push('MigApp fictif inclus : son périmètre supplémentaire n’est pas entièrement énuméré dans ce catalogue.');
  stores.set(bundle,{...store,inspection});return inspection;
}
function seed() {
  for(const profile of profilesInventory().profiles)profileStates.set(profile.sid,{sid:profile.sid,account:profile.account,state:'original',receiptPath:null,sourceStoreId:null});
  const profiles=profilesInventory().profiles.filter((profile)=>profile.migrationSupported);
  for(let i=0;i<60;i++){
    const chosen=i===0?profiles.filter((profile)=>profile.session.state==='none'&&profile.loaded===false).slice(0,2):i%3===0?[profiles[(i+1)%profiles.length],profiles[(i+2)%profiles.length]]:[profiles[i%profiles.length]];
    const at=new Date(Date.now()-(i*7+1)*3600000).toISOString();const inspection=addStore(chosen,destinations[i%4].path,i%11===10,at);
    const state=i%17===16?'failed':i%13===12?'cancelled':i%11===10?'partial':'succeeded';
    const kind=['failed','cancelled'].includes(state)?'restore':'backup';
    const job={id:`demo-history-${String(i+1).padStart(3,'0')}`,kind,title:`${kind==='backup'?'Sauvegarde':'Restauration'} fictive · ${chosen.map((p)=>p.account).join(', ')}`,state,currentAction:state==='succeeded'?'Opération fictive terminée':'Scénario historique fictif à examiner',progressPercent:state==='succeeded'?100:64,estimatedRemainingSeconds:null,startedAt:new Date(Date.parse(at)-240000).toISOString(),finishedAt:at,demonstration:true,error:state==='failed'?{code:'demonstration.history',message:'Exemple fictif de support indisponible.'}:null,profileSid:chosen[0].sid,completedUnits:8,totalUnits:8,warnings:inspection.warnings,resultPath:inspection.bundle,cancellationRequested:false};jobs.set(job.id,job);
  }
}
seed();
function configState(){setRecommendationPreference(settings.recommendedSelection);if(bootstrap&&(bootstrap.expectedRevision!==revision||previews.get(bootstrap.id)?.expires<Date.now())){previews.delete(bootstrap.id);bootstrap=previewConfig('bootstrap',assets.importConfigurationPath,documents.get(assets.importConfigurationPath));}return {demonstration:true,revision,settings:clone(settings),bootstrap:bootstrap?clone(bootstrap):null,warnings:['Démonstration — réglages et documents uniquement en mémoire.']};}
function validSettings(value){if(!value||Object.keys(value).some((key)=>!(key in defaults))||!['system','light','dark','contrast'].includes(value.theme)||!['system','full','reduced'].includes(value.motion)||!['keepBoth','keepCurrent','useSaved'].includes(value.conflictPolicy)||Object.entries(defaults).some(([key,old])=>typeof old==='boolean'&&typeof value[key]!=='boolean'))fail('Réglages hors du contrat fermé.','configuration.format');return clone(value);}
function previewConfig(kind,sourceLabel,value){const preview={id:id(),kind,demonstration:true,sourceLabel,sha256:'b'.repeat(64),expectedRevision:revision,changes:Object.keys(defaults).map((key)=>({key,before:String(settings[key]),after:String(value[key]),disposition:'replace'})),packIdentity:kind==='import'?null:'FILPRO-DEMO-PACK',notice:'Aperçu fictif avec sauvegarde préalable en mémoire ; aucun fichier réel modifié.'};previews.set(preview.id,{preview,value:clone(value),expires:Date.now()+600000});return preview;}
bootstrap=previewConfig('bootstrap',assets.importConfigurationPath,documents.get(assets.importConfigurationPath));
function resetWorld(){
  for(const timer of timers.values())clearTimeout(timer);
  timers.clear();stores.clear();jobs.clear();plans.clear();receipts.clear();profileStates.clear();previews.clear();documents.clear();resetSelections();
  generation=id();outcome='succeeded';sequence=0;settings=clone(defaults);revision=id();offer=null;previousVersion=null;sourcePref={...defaultSource};
  documents.set(assets.importConfigurationPath,{...defaults,theme:'dark',motion:'reduced',navigationCollapsed:true});
  update={demonstration:true,stage:'idle',transactionId:null,currentVersion:'2.0.1-demo',targetVersion:null,action:'Aucune recherche de mise à jour engagée.',progressPercent:null,canInstall:false,canRollback:false,error:null,warnings:['Démonstration — aucun binaire réel remplacé.']};
  seed();bootstrap=previewConfig('bootstrap',assets.importConfigurationPath,documents.get(assets.importConfigurationPath));
}
function resultConfig(notice,writtenPath=null,backupPath=null){return {demonstration:true,state:configState(),writtenPath,backupPath,notice};}
function applyConfig(args,kind){confirmed(args);const pending=previews.get(args.previewId);if(!pending||pending.preview.kind!==kind||pending.expires<Date.now()||pending.preview.expectedRevision!==revision)fail('Aperçu fictif absent, expiré ou périmé.');const backupPath=`D:\\FILPRO-DEMO\\Reserve\\configuration-avant-${revision}.json`;documents.set(backupPath,clone(settings));let writtenPath=null;if(kind==='defaults'){writtenPath='D:\\FILPRO-DEMO\\Local\\FILPRO.defaults.json';documents.set(writtenPath,clone(pending.value));}else{settings=clone(pending.value);revision=id();}previews.delete(args.previewId);if(kind==='bootstrap')bootstrap=null;return resultConfig(kind==='defaults'?'Défauts du pack fictif enregistrés en mémoire ; vrai PROD inchangé.':'Réglages fictifs appliqués avec copie préalable.',writtenPath,backupPath);}
function emit(job){sink('operations.changed',clone(job));}
function updateEvent(){sink('updates.changed',clone(update));}
function keepPlan(kind,plan){plans.set(plan.id,{kind,plan:clone(plan),expires:Date.now()+600000});return plan;}
function start(kind,args){
  closed(args,kind==='restore'?['planId','confirmed','acknowledgeCapacity','acknowledgePriorBackup','acknowledgeIdentityReview']:['planId','confirmed','acknowledgeCapacity'],['planId','confirmed','acknowledgeCapacity']);confirmed(args);
  if(args.acknowledgeCapacity!==true||kind==='restore'&&(args.acknowledgePriorBackup!==true||args.acknowledgeIdentityReview!==true))fail('Accusés du plan requis.','execution.confirmation');
  const pending=plans.get(args.planId);if(!pending||pending.kind!==kind||pending.expires<Date.now())fail('Plan fictif absent, périmé ou déjà consommé.');
  if([...jobs.values()].some((job)=>!terminal(job)))fail('Une opération fictive est déjà en cours.','execution.busy');
  const plan=pending.plan;if(plan.demonstration!==true)fail('Un plan réel ne peut pas être simulé.');
  if(kind==='backup'){pathAllowed(plan.destination);if(plan.keyPath)pathAllowed(plan.keyPath);for(const p of plan.profiles){if(p.loaded==null||p.loaded===true&&!plan.useVss)fail('Chargement connu et VSS explicite requis pour un profil chargé.');}}
  if(kind==='restore'){storeAt(plan.bundle);plan.targets.forEach((p)=>target(p.sid));if(plan.priorBackup.root)pathAllowed(plan.priorBackup.root);}
  if(kind==='repair')target(plan.sid);if(kind==='recover')pathAllowed(plan.destination);
  plans.delete(args.planId);const chosenOutcome=kind==='restore'&&storeAt(plan.bundle).partial&&outcome==='succeeded'?'partial':outcome;outcome='succeeded';
  const job={id:id(),kind,title:{backup:'Sauvegarde multi-profils fictive',restore:'Restauration multi-profils fictive',repair:'Réparation de profil fictive',recover:'Récupération de fichiers fictifs'}[kind],state:'pending',currentAction:'En attente de préparation',progressPercent:null,estimatedRemainingSeconds:null,startedAt:now(),finishedAt:null,demonstration:true,error:null,profileSid:null,completedUnits:null,totalUnits:null,warnings:[],resultPath:null,cancellationRequested:false};jobs.set(job.id,job);emit(job);
  const actions=['Vérification du plan fictif','Préparation des données fictives','Lecture des fichiers fictifs','Lecture des clés fictives','Traitement des profils','Contrôle des conflits fictifs','Vérification du résultat fictif','Libération des ressources fictives'];let step=0;
  const tick=()=>{
    if(job.cancellationRequested){job.state='cancelled';job.currentAction='Opération arrêtée après nettoyage fictif';job.finishedAt=now();job.estimatedRemainingSeconds=null;timers.delete(job.id);emit(job);return;}
    if(chosenOutcome==='failed'&&step===4){job.state='failed';job.currentAction='Erreur fictive demandée';job.error={code:'demonstration.failure',message:'Le résultat fictif n’a pas été validé ; les données précédentes restent conservées.'};job.finishedAt=now();job.estimatedRemainingSeconds=null;timers.delete(job.id);emit(job);return;}
    if(step===actions.length){
      job.state=chosenOutcome==='partial'?'partial':'succeeded';job.currentAction=job.state==='partial'?'Résultat fictif partiel à examiner':'Opération fictive terminée';job.finishedAt=now();job.estimatedRemainingSeconds=null;job.progressPercent=job.state==='succeeded'?100:87;job.warnings=[job.state==='partial'?'Un fichier fictif verrouillé a été omis.':'Démonstration — aucune donnée réelle lue ou écrite.'];
      if(kind==='backup')job.resultPath=addStore(plan.profiles,plan.destination,job.state==='partial',now(),null,plan.encrypted,plan.migApp).bundle;
      else if(kind==='repair'){job.resultPath=assets.safetyBackupRoot+'\\reparation-'+job.id+'.json';receipts.set(job.resultPath,clone(profileStates.get(plan.sid)));profileStates.set(plan.sid,{sid:plan.sid,account:plan.account,state:job.state==='partial'?'repairPartial':'repairPrepared',receiptPath:job.resultPath,sourceStoreId:null});}
      else {job.resultPath=(kind==='recover'?plan.destination:assets.safetyBackupRoot)+'\\resultat-'+job.id;if(kind==='restore')for(const p of plan.targets)profileStates.set(p.sid,{sid:p.sid,account:p.account,state:job.state==='partial'?'restorePartial':'restored',receiptPath:job.resultPath,sourceStoreId:storeAt(plan.bundle).id});}
      timers.delete(job.id);emit(job);return;
    }
    job.state=step<2?'preparing':'running';job.currentAction=actions[step];job.completedUnits=step;job.totalUnits=actions.length;job.progressPercent=100*step/actions.length;job.estimatedRemainingSeconds=(actions.length-step)*.35;
    const sids=plan.profiles?.map((p)=>p.sid)??plan.targets?.map((p)=>p.sid)??(plan.sid?[plan.sid]:[]);job.profileSid=sids.length?sids[Math.min(sids.length-1,Math.floor(step*sids.length/actions.length))]:null;emit(job);step++;const timer=setTimeout(tick,350);timer.unref?.();timers.set(job.id,timer);
  };const timer=setTimeout(tick,20);timer.unref?.();timers.set(job.id,timer);return clone(job);
}

export function setEventHandler(handler){sink=typeof handler==='function'?handler:()=>{};}
export function demoCatalog(){return {demonstration:true,generation,destinations:clone(destinations),stores:[...stores.values()].map(({inspection,...summary})=>clone(summary)).sort((a,b)=>Date.parse(b.createdUtc)-Date.parse(a.createdUtc)),scenarios:clone(scenarios),selectedScenario:outcome,assets:clone(assets),profileStates:clone([...profileStates.values()])};}
export function demoSession(){return {mode:'Démonstration',demonstration:true,banner:'Actions fictives en mémoire, sans accès au poste, au réseau ou aux fichiers réels.',generatedAt:now(),generation,capabilities:{backup:true,restore:true,repair:true,recover:true,configuration:true,updates:true,filePicker:false,nativeConfirmation:false,crossAccount:false,codeUpdates:true,presenceLocked:false}};}
export function demoAccueil(){const profiles=profilesInventory().profiles;const list=[...jobs.values()].sort((a,b)=>Date.parse(b.startedAt)-Date.parse(a.startedAt));return {generatedAt:now(),mode:'Démonstration',demonstration:true,profiles:profiles.map((p)=>({name:p.account+' · fictif',kind:'Poste de démonstration',state:p.session.state==='active'?'actif':'inactif',sessionCount:p.session.count,lastActivity:p.lastUse})),sessions:profiles.map((p)=>({profile:p.account+' · fictif',state:p.session.state==='active'?'active':p.session.state==='disconnected'?'deconnectee':p.session.state==='none'?'fermee':'inconnue',lastSeen:p.lastUse})),backups:[...stores.values()].map((s)=>({id:s.id,createdAt:s.createdUtc,sizeBytes:s.inspection.entries.reduce((n,e)=>n+e.bytes,0),scope:s.accounts.join(', '),state:s.partial?'partielle':'reussie'})),tasks:list.map((j)=>({id:j.id,title:j.title,state:j.cancellationRequested&&!terminal(j)?'arretdemande':terminal(j)?j.state==='cancelled'?'annulee':j.state==='failed'?'echouee':j.state==='partial'?'partielle':'terminee':j.state==='pending'?'enattente':'encours',progressPercent:j.progressPercent??null})),journal:list.map((j)=>({at:j.finishedAt||j.startedAt,level:j.state==='failed'?'erreur':j.state==='partial'?'avertissement':'info',message:j.title+' — '+(j.error?.message||j.currentAction)})),availability:{profiles:true,backups:true,tasks:true,journal:true,realSystemActions:false}};}

export async function worldCall(op,args={}){
  switch(op){
    case 'session.info':closed(args,[]);return demoSession();
    case 'session.ready':closed(args,['generation'],['generation']);if(args.generation!==generation)fail('Page d’une autre génération.','session.stale');return {ready:true,generation};
    case 'session.demoExit':fail('La prévisualisation navigateur ne possède pas de moteur Windows. Ouvrez FILPRO.exe pour utiliser le contexte normal.','feature.unavailable');break;
    case 'session.demoEnter':case 'session.demoReset':closed(args,['confirmed'],['confirmed']);confirmed(args);if([...jobs.values()].some((j)=>!terminal(j)))fail('Terminer ou annuler l’opération avant de recréer la démonstration.','session.busy');resetWorld();return {restarting:true};
    case 'demonstration.catalog':closed(args,[]);return demoCatalog();
    case 'demonstration.scenario':closed(args,['outcome'],['outcome']);if(!scenarios.some((s)=>s.id===args.outcome))fail('Scénario inconnu.');if([...jobs.values()].some((j)=>!terminal(j)))fail('Une opération est en cours.');outcome=args.outcome;return demoCatalog();
    case 'accueil.summary':closed(args,[]);return demoAccueil();
    case 'operations.list':closed(args,[]);return clone([...jobs.values()].sort((a,b)=>Date.parse(b.startedAt)-Date.parse(a.startedAt)));
    case 'operations.get':closed(args,['id'],['id']);if(!jobs.has(args.id))fail('Opération inconnue.','operation.unknown');return clone(jobs.get(args.id));
    case 'operations.cancel':{closed(args,['id','confirmed'],['id','confirmed']);confirmed(args);const job=jobs.get(args.id);if(!job)fail('Opération inconnue.','operation.unknown');if(terminal(job))return clone(job);job.cancellationRequested=true;job.currentAction='Arrêt demandé — nettoyage fictif en cours';emit(job);return new Promise((resolve)=>{const check=()=>terminal(job)?resolve(clone(job)):setTimeout(check,25);check();});}
    case 'sauvegarder.plan':return keepPlan('backup',await backupPlan(args));
    case 'sauvegarder.inspectStore':closed(args,['path'],['path']);return clone(storeAt(args.path).inspection);
    case 'sauvegarder.restorePlan':{const inspection=storeAt(args.bundle).inspection;const result=await restorePlan({...args,storeReady:inspection.store.ready,manifestUsers:inspection.store.manifestUsers,legacy:inspection.store.legacy,partial:inspection.store.partial,encrypted:inspection.store.encrypted},inspection);keepPlan('restore',result.plan);return result;}
    case 'analyser.repairPlan':{closed(args,['sid','safetyBackupRoot'],['sid','safetyBackupRoot']);const p=target(args.sid);pathAllowed(args.safetyBackupRoot);return keepPlan('repair',{id:id(),createdUtc:now(),demonstration:true,sid:p.sid,account:p.account,profilePath:p.localPath,preservedProfilePath:p.localPath+'.preserve-demo',safetyBackupRoot:args.safetyBackupRoot,actions:['Sauvegarde préalable fictive','Préservation du dossier fictif','Réparation fictive des références','Création du reçu fictif'],warnings:[],notice:'Aucun compte ou registre Windows modifié dans cette démonstration.'});}
    case 'recuperer.plan':{closed(args,['bundle','destination','entryIds','keyPath'],['bundle','destination','entryIds']);const s=storeAt(args.bundle).inspection;pathAllowed(args.destination);if(args.keyPath!=null)pathAllowed(args.keyPath);if(s.store.encrypted&&!args.keyPath)fail('Clé fictive requise pour ce magasin chiffré.');if(args.destination.toLowerCase()===s.bundle.toLowerCase()||args.destination.toLowerCase().startsWith(s.bundle.toLowerCase()+'\\'))fail('Récupération dans un dossier indépendant requise.');if(!Array.isArray(args.entryIds)||!args.entryIds.length||args.entryIds.length>500||new Set(args.entryIds).size!==args.entryIds.length||args.entryIds.some((entryId)=>!s.entries.some((entry)=>entry.id===entryId)))fail('Sélection de fichiers fictifs invalide.');return keepPlan('recover',{id:id(),createdUtc:now(),demonstration:true,bundle:s.bundle,storeId:s.id,manifestSha256:s.manifestSha256,destination:args.destination,keyPath:args.keyPath??null,entries:s.entries.filter((entry)=>args.entryIds.includes(entry.id)),encrypted:s.store.encrypted,warnings:[],notice:'Extraction fictive dans un nouveau dossier en mémoire.'});}
    case 'demonstration.backup':return start('backup',args);
    case 'demonstration.restore':return start('restore',args);
    case 'demonstration.repair':return start('repair',args);
    case 'demonstration.recover':return start('recover',args);
    case 'demonstration.repairRollback':{closed(args,['receiptPath','confirmed'],['receiptPath','confirmed']);confirmed(args);if([...jobs.values()].some((job)=>!terminal(job)))fail('Terminer l’opération avant le retour arrière.','execution.busy');if(!receipts.has(args.receiptPath))fail('Reçu fictif inconnu.');const previous=receipts.get(args.receiptPath);target(previous.sid);profileStates.set(previous.sid,clone(previous));receipts.delete(args.receiptPath);const job={id:id(),kind:'rollback',title:'Retour arrière fictif de réparation',state:'succeeded',currentAction:'État fictif précédent rétabli',progressPercent:100,estimatedRemainingSeconds:null,startedAt:now(),finishedAt:now(),demonstration:true,error:null,profileSid:previous.sid,completedUnits:1,totalUnits:1,warnings:['Aucune donnée réelle modifiée.'],resultPath:args.receiptPath,cancellationRequested:false};jobs.set(job.id,job);emit(job);return clone(job);}
    case 'configuration.read':closed(args,[]);return configState();
    case 'configuration.save':closed(args,['settings','expectedRevision'],['settings','expectedRevision']);if(args.expectedRevision!==revision)fail('Réglages fictifs modifiés depuis la lecture.');settings=validSettings(args.settings);revision=id();return resultConfig('Réglages fictifs enregistrés.');
    case 'configuration.export':closed(args,['destinationPath','confirmed'],['destinationPath','confirmed']);confirmed(args);pathAllowed(args.destinationPath);documents.set(args.destinationPath,clone(settings));return resultConfig('Export fictif conservé en mémoire, aucun fichier réel créé.',args.destinationPath);
    case 'configuration.previewImport':closed(args,['sourcePath'],['sourcePath']);pathAllowed(args.sourcePath);if(!documents.has(args.sourcePath))fail('Document fictif absent.');return previewConfig('import',args.sourcePath,documents.get(args.sourcePath));
    case 'configuration.previewDefaults':closed(args,[]);return previewConfig('defaults','D:\\FILPRO-DEMO\\Local\\FILPRO.defaults.json',settings);
    case 'configuration.applyImport':case 'configuration.applyDefaults':closed(args,['previewId','confirmed'],['previewId','confirmed']);return applyConfig(args,op.endsWith('applyImport')?'import':'defaults');
    case 'configuration.decideBootstrap':closed(args,['previewId','apply','confirmed'],['previewId','apply','confirmed']);confirmed(args);if(bootstrap?.id!==args.previewId||typeof args.apply!=='boolean')fail('Proposition fictive périmée.');if(args.apply)return applyConfig(args,'bootstrap');previews.delete(args.previewId);bootstrap=null;return resultConfig('Réglages fictifs conservés.');
    case 'updates.read':closed(args,[]);return clone(update);
    case 'updates.source':closed(args,[]);return clone(sourcePref);
    case 'updates.saveSource':{closed(args,['kind','folder'],['kind']);if(!['folder','manualZip'].includes(args.kind))fail('Source inconnue.');if(args.kind!=='folder'&&args.folder!==null&&args.folder!==undefined)fail('Source fictive invalide.');const folder=args.kind==='folder'?pathAllowed(args.folder):null;sourcePref={demonstration:true,kind:args.kind,folder};return clone(sourcePref);}
    case 'updates.inspect':{closed(args,['kind','path','sha256'],['kind']);if(!['folder','manualZip'].includes(args.kind))fail('Source inconnue.');pathAllowed(args.path);if(args.sha256&&!/^[a-f0-9]{64}$/i.test(args.sha256))fail('Empreinte SHA-256 invalide.');offer={id:id(),demonstration:true,source:args.kind,version:'2.1.0-demo',fileName:'FILPRO_2.1.0_DEMO_PROD_Windows-x64.zip',bytes:80000000,sha256:'c'.repeat(64),newer:update.currentVersion!=='2.1.0-demo',integrityNotice:'Validation fictive en mémoire ; aucune archive réelle ouverte.',warnings:[]};update={...update,stage:'available',targetVersion:offer.version,action:offer.newer?'Version fictive disponible':'Version fictive déjà actuelle',canInstall:false};updateEvent();return clone(offer);}
    case 'updates.prepare':closed(args,['offerId','confirmed'],['offerId','confirmed']);confirmed(args);if(offer?.id!==args.offerId||!offer.newer)fail('Offre fictive absente ou actuelle.');update={...update,stage:'prepared',transactionId:id(),action:'Pack fictif validé — installation à confirmer',progressPercent:100,canInstall:true,canRollback:false};updateEvent();return clone(update);
    case 'demonstration.updateInstall':case 'demonstration.updateRollback':{closed(args,['transactionId','confirmed'],['transactionId','confirmed']);confirmed(args);if([...jobs.values()].some((j)=>!terminal(j)))fail('Terminer les opérations avant la mise à jour.');if(!args.transactionId||args.transactionId!==update.transactionId)fail('Transaction fictive inconnue.');const rollback=op.endsWith('Rollback');if(rollback){if(!update.canRollback||!previousVersion)fail('Aucun retour arrière fictif disponible.');update={...update,stage:'rolledBack',currentVersion:previousVersion,action:'Version fictive précédente rétablie',canRollback:false,canInstall:false};}else{if(!update.canInstall)fail('Préparer le pack fictif avant installation.');previousVersion=update.currentVersion;update={...update,stage:'committed',currentVersion:update.targetVersion,action:'Mise à jour fictive validée',canRollback:true,canInstall:false};}updateEvent();return {transactionId:args.transactionId,demonstration:true,closeApplicationRequired:false,notice:'Version fictive modifiée uniquement en mémoire ; aucun EXE réel remplacé.'};}
    default:return undefined;
  }
}
