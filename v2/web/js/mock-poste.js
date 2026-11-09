// Mock navigateur des écrans poste (Analyser, Sauvegarder) : mêmes formes que les fournisseurs C# (BridgeJson : camelCase,
// énumérations camelCase, dates UTC à la seconde). Ce n'est PAS le moteur : il ne touche jamais le système et n'est utilisé
// que quand window.chrome.webview est absent. Les groupes reproduisent ce que SoftwareCatalog produirait pour le monde fictif.

function iso(minutesAgo) {
  const d = new Date(Date.now() - minutesAgo * 60_000);
  return d.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

const SIDS = {
  camille: 'S-1-5-21-111222333-444555666-777888999-1000',
  lea: 'S-1-5-21-111222333-444555666-777888999-1001',
  karim: 'S-1-5-21-111222333-444555666-777888999-1002',
  sophie: 'S-1-5-21-111222333-444555666-777888999-1003',
  hugo: 'S-1-5-21-111222333-444555666-777888999-1004',
  nadia: 'S-1-12-1-700000005-1005-2005-3005',
};

// Session memory only: no localStorage, host bridge, file, registry or network access.
const savedSelections = new Map();
let commonSelection = null;
let recommendedSelection = true;

export function resetSelections() { savedSelections.clear(); commonSelection = null; recommendedSelection = true; }
export function setRecommendationPreference(value) { recommendedSelection = value === true; }
export function readCommonSelection() { return { demonstration:true,hasSelection:commonSelection!==null,appData:structuredClone(commonSelection?.files??[]),registryKeys:structuredClone(commonSelection?.registry??[]) }; }
export function clearCommonSelection() { commonSelection=null;return readCommonSelection(); }
export function setCommonSelection(sid, groupIds) {
  const groups=softwareInventory(sid).groups;
  if(!Array.isArray(groupIds)||groupIds.length>500||new Set(groupIds).size!==groupIds.length||groupIds.some((id)=>typeof id!=='string'||id.length>1024||!groups.some((group)=>group.id===id)))throw mockError('arguments.invalid','Choix communs invalides.');
  const selected=groups.filter((group)=>groupIds.includes(group.id));
  commonSelection={files:structuredClone(selected.flatMap((group)=>group.files)),registry:structuredClone(selected.flatMap((group)=>group.registry))};
  return readCommonSelection();
}

function requireProfile(sid, code = 'selection.profile') {
  if (typeof sid !== 'string' || !Object.values(SIDS).includes(sid)) {
    throw mockError(code, 'Profil absent du monde de démonstration.');
  }
}

function itemKey(item) {
  return `${item.scope}|${item.rel.replace(/\//g, '\\').replace(/\\+$/, '')}`.toLowerCase();
}

function session(state, count, ids, readStatus = 'complete') {
  return { state, count, ids, source: 'Simulation', readStatus, error: null };
}

export function profilesInventory() {
  const profiles = [
    { sid: SIDS.camille, identityKind: 'compteLocalOuDomaine', migrationSupported: true, account: 'DEMO\\camille', localPath: 'C:\\Users\\camille', loaded: true, session: session('active', 1, [1]), exists: true, warnings: [], isFslogix: false, isCentralProfile: false, hasRedirected: false, excluded: false, lastUse: iso(20) },
    { sid: SIDS.lea, identityKind: 'compteLocalOuDomaine', migrationSupported: true, account: 'DEMO\\lea', localPath: 'C:\\Users\\lea', loaded: false, session: session('disconnected', 1, [2]), exists: true, warnings: [], isFslogix: false, isCentralProfile: false, hasRedirected: false, excluded: false, lastUse: iso(60 * 40) },
    { sid: SIDS.karim, identityKind: 'compteLocalOuDomaine', migrationSupported: true, account: 'DEMO\\karim', localPath: 'C:\\Users\\karim', loaded: null, session: session('none', 0, []), exists: true, warnings: [], isFslogix: false, isCentralProfile: false, hasRedirected: false, excluded: false, lastUse: iso(60 * 24 * 5) },
    { sid: SIDS.sophie, identityKind: 'compteLocalOuDomaine', migrationSupported: true, account: 'DEMO\\sophie', localPath: 'C:\\Users\\sophie', loaded: false, session: session('none', 0, []), exists: true, warnings: ['copie locale peut-être plus ancienne'], isFslogix: false, isCentralProfile: true, hasRedirected: false, excluded: false, lastUse: iso(60 * 24 * 9) },
    { sid: SIDS.hugo, identityKind: 'compteLocalOuDomaine', migrationSupported: true, account: 'DEMO\\hugo', localPath: 'C:\\Users\\hugo', loaded: false, session: session('none', 0, []), exists: true, warnings: [], isFslogix: false, isCentralProfile: false, hasRedirected: false, excluded: false, lastUse: iso(60 * 24 * 12) },
    { sid: SIDS.nadia, identityKind: 'entra', migrationSupported: false, account: 'DEMO\\nadia', localPath: 'C:\\Users\\nadia', loaded: false, session: session('none', 0, []), exists: true, warnings: [], isFslogix: false, isCentralProfile: false, hasRedirected: false, excluded: false, lastUse: iso(60 * 24 * 20) },
  ];
  return { sessionReadStatus: 'complete', sessionError: null, profiles };
}

function file(scope, rel, category, selected, recommend, size, complete) {
  return { scope, rel, name: rel, category, selected, recommend, known: false, size, sizeMeasured: size != null, sizeComplete: size != null && complete };
}
function reg(rel, selected, recommend) {
  return { scope: 'hkcu', rel, name: rel, category: 'autre', selected, recommend, known: false, size: null, sizeMeasured: false, sizeComplete: false };
}
function size(state, bytes, text, extra = {}) {
  return { state, bytes, measured: state === 'mesuree' || state === 'partielle', complete: state === 'mesuree', text, unknownPaths: 0, overlappingPaths: extra.overlapping ?? 0, duplicatePaths: 0, notice: 'Fichiers uniquement ; taille des clés HKCU non mesurée. Dossiers imbriqués comptés une seule fois par base AppData. Mesures indicatives, hors caches exclus et sans instantané.' };
}

function buildGroups() {
  const chromeFiles = [
    file('local', 'Google\\Chrome\\User Data', 'donnees', true, true, 849346560, true),
    file('local', 'Google\\Chrome\\User Data\\Default\\Cache', 'caches', true, false, 125829120, false),
  ];
  const notepadFiles = [file('roaming', 'Notepad++', 'donnees', true, true, 44040192, true)];
  const officeFiles = [file('roaming', 'Microsoft\\Word', 'donnees', false, false, 18874368, true)];
  const vlcFiles = [file('roaming', 'vlc', 'donnees', false, false, 6291456, true)];
  const groups = [
    { id: 'known:chrome', name: 'Google Chrome', linked: true, files: chromeFiles, registry: [reg('Software\\Google\\Chrome', true, true)] },
    { id: 'known:office', name: 'Microsoft Office / Outlook', linked: true, files: officeFiles, registry: [reg('Software\\Microsoft\\Office', false, false)] },
    { id: 'known:notepadpp', name: 'Notepad++', linked: true, files: notepadFiles, registry: [reg('Software\\Notepad++', true, true)] },
    { id: 'known:vlc', name: 'VLC', linked: true, files: vlcFiles, registry: [reg('Software\\VideoLAN\\VLC', false, false)] },
  ];
  const sizes = {
    'known:chrome': size('partielle', 849346560, 'au moins 810,0 Mio (partiel)', { overlapping: 1 }),
    'known:office': size('mesuree', 18874368, '18,0 Mio mesurés'),
    'known:notepadpp': size('mesuree', 44040192, '42,0 Mio mesurés'),
    'known:vlc': size('mesuree', 6291456, '6,0 Mio mesurés'),
  };
  return groups.map((g) => {
    const members = g.files.length + g.registry.length;
    const selectedCount = g.files.filter((f) => f.selected).length + g.registry.filter((r) => r.selected).length;
    const selected = selectedCount === 0 ? false : selectedCount === members ? true : null;
    return {
      id: g.id, name: g.name, linked: g.linked, files: g.files, registry: g.registry,
      selected, selectedCount,
      summary: `${g.files.length} dossier(s) · ${g.registry.length} clé(s) · ${selectedCount}/${members} retenus`,
      size: sizes[g.id],
    };
  });
}

export function softwareInventory(sid) {
  requireProfile(sid);
  const last = lastSelection(sid) ?? commonSelection;
  const files = new Set((last?.files ?? []).map(itemKey));
  const registry = new Set((last?.registry ?? []).map(itemKey));
  const groups = buildGroups().map((group) => {
    if (last) {
      group.files.forEach((item) => { item.selected = files.has(itemKey(item)); });
      group.registry.forEach((item) => { item.selected = registry.has(itemKey(item)); });
    } else if (!recommendedSelection) {
      [...group.files, ...group.registry].forEach((item) => { item.selected = false; });
    }
    const members = [...group.files, ...group.registry];
    const selectedCount = members.filter((item) => item.selected).length;
    return { ...group, selectedCount, selected: selectedCount === 0 ? false : selectedCount === members.length ? true : null,
      summary: `${group.files.length} dossier(s) · ${group.registry.length} clé(s) · ${selectedCount}/${members.length} retenus` };
  });
  return { sid, groups, registryError: null, discovery: { filesComplete: true, registryComplete: true, filesReason: null, registryReason: null }, capturedAt: iso(0) };
}

export function lastSelection(sid) {
  if (savedSelections.has(sid)) return structuredClone(savedSelections.get(sid));
  if (sid !== SIDS.camille) return null;
  return {
    sid,
    files: [file('roaming', 'Notepad++', 'donnees', true, false, null, false)],
    registry: [reg('Software\\Notepad++', true, false)],
    capturedAt: iso(60 * 24 * 2),
  };
}

export function saveSelection(args) {
  if (!args || typeof args !== 'object' || Array.isArray(args)
      || Object.keys(args).some((key) => !['sid', 'groupIds'].includes(key))
      || typeof args.sid !== 'string' || !args.sid.trim()
      || !Array.isArray(args.groupIds) || args.groupIds.length > 500) {
    throw mockError('arguments.invalid', 'Arguments invalides : sid et groupIds sont requis ; la liste peut être vide.');
  }
  const sid = args.sid.trim();
  requireProfile(sid, 'backup.selection');
  const requested = new Set();
  for (const id of args.groupIds) {
    if (typeof id !== 'string' || !id.trim() || id.length > 1024 || requested.has(id)) {
      throw mockError('arguments.invalid', 'Arguments invalides : identifiant de logiciel invalide ou répété.');
    }
    requested.add(id);
  }
  const inventory = softwareInventory(sid);
  const known = new Set(inventory.groups.map((group) => group.id));
  if ([...requested].some((id) => !known.has(id))) {
    throw mockError('arguments.invalid', 'Arguments invalides : logiciel absent de l’inventaire courant.');
  }
  const groups = inventory.groups.filter((group) => requested.has(group.id));
  const retained = (items) => items.map((item) => ({ ...item, selected: true, recommend: false, known: false,
    size: null, sizeMeasured: false, sizeComplete: false }));
  savedSelections.set(sid, {
    sid, files: retained(groups.flatMap((group) => group.files)),
    registry: retained(groups.flatMap((group) => group.registry)), capturedAt: iso(0),
  });
  return softwareInventory(sid);
}

export function conflictPreview(sid) {
  const groups = softwareInventory(sid).groups;
  const policies = groups.map((g) => ({ id: g.id, mode: 'keepBoth' }));
  const names = groups.map((g) => g.name).join(', ');
  const summary = `Coexistence demandée des fichiers ; registre actuel conservé : ${names}\r\n`
    + 'Validation utilisateur restante : l’activation effective des réglages (registre, services, sessions) doit être contrôlée dans la session cible après restauration.';
  return { choices: groups, policies, summary };
}

// ---------- Plan de sauvegarde (aperçu canonisé, miroir de BackupPlan.Create pour la prévisualisation) ----------
// Ce n'est PAS le moteur : aucune mesure, aucune écriture. Il reproduit les formes et les refus de la fonction pure pour
// développer l'écran de confirmation sans hôte WebView2. La confirmation (Write) reste refusée, comme dans le pont réel.

const RULE_EXCLUSIONS = [
  { kind: 'fluxNomme', target: null, reason: 'Flux de données nommés non copiés par ce plan ; le moteur les annoncera s’il en rencontre.', origin: 'regle' },
  { kind: 'application', target: null, reason: 'Applications non migrées : elles devront être installées sur le poste de destination.', origin: 'regle' },
  { kind: 'ruche', target: null, reason: 'Ruches du registre non montées par ce plan : l’examen du registre appartient au moteur, qui les décharge dans un finally.', origin: 'regle' },
  { kind: 'dossierVide', target: null, reason: 'Dossiers vides non retenus : un dossier vide n’est pas un contenu à sauvegarder.', origin: 'regle' },
  { kind: 'magasin', target: null, reason: 'Magasins de sauvegarde FILPRO voisins écartés du contenu copié, pour éviter de sauvegarder une sauvegarde.', origin: 'regle' },
];

const SPACE_NOTICE = 'Aucun octet n’a été mesuré par ce plan. Le moteur doit calculer le magasin, le temporaire et les marges '
  + 'avec les règles retenues ; la copie reste bloquée tant que cette estimation manque ou dépasse l’espace disponible.';
const PLAN_NOTICE = 'Plan de préparation : aucun magasin créé, aucun profil lu, aucune écriture, aucune ruche montée, aucune '
  + 'sonde de support. La mesure du besoin d’espace, la confirmation et l’exécution appartiennent au moteur. Un arrêt demandé '
  + 'n’est pas un arrêt achevé.';

function mockError(code, message) {
  return Object.assign(new Error(message), { code });
}

function hex32() {
  let s = '';
  while (s.length < 32) s += Math.floor(Math.random() * 0x100000000).toString(16).padStart(8, '0');
  return s.slice(0, 32);
}

export function backupPlan(args = {}) {
  const sids = Array.isArray(args.sids) ? args.sids.map((s) => String(s).trim()).filter(Boolean) : [];
  if (!sids.length) return Promise.reject(mockError('backup.selection', 'Sélectionner au moins un profil avec un compte explicite avant la sauvegarde.'));
  if (new Set(sids).size !== sids.length) return Promise.reject(mockError('backup.selection', 'Un profil ne peut apparaître deux fois dans le même plan.'));
  const destination = requireRooted(args.destination, 'backup.destination', 'Destination absolue requise.');
  const encrypted = !!args.encrypted;
  const noCompress = !!args.noCompress;
  const useVss = !!args.useVss;
  const requestedKey = args.keyPath == null ? null : (String(args.keyPath).trim() || null);
  if (encrypted && noCompress) return Promise.reject(mockError('backup.plan', 'Le chiffrement ne peut pas être combiné avec un magasin non compressé.'));
  if (!encrypted && requestedKey) return Promise.reject(mockError('backup.destination', 'Chemin de clé indiqué sans sauvegarde chiffrée demandée.'));
  const keyPath = encrypted ? requireRooted(requestedKey, 'backup.destination', 'Fichier clé requis pour ce magasin chiffré : chemin de clé invalide.') : null;
  if (keyPath && isWithin(keyPath, destination, true)) return Promise.reject(mockError('backup.destination', 'Le fichier clé doit rester hors du dossier de sauvegarde.'));

  const bySid = Object.fromEntries(profilesInventory().profiles.map((p) => [p.sid, p]));

  const warnings = [];
  const profiles = [];
  for (const sid of sids) {
    const p = bySid[sid];
    if (!p) return Promise.reject(mockError('backup.selection', 'Profil inconnu : actualiser la liste des profils avant de préparer le plan.'));
    if (p.identityKind === 'entra' || sid.startsWith('S-1-12-1-')) return Promise.reject(mockError('backup.selection', 'Profil Entra non pris en charge par USMT.'));
    if (p.loaded === true && !useVss) return Promise.reject(mockError('backup.selection', `Profil chargé : activer VSS ou fermer ce qui utilise ce profil (${p.account}).`));
    if (isWithin(destination, p.localPath, true)) return Promise.reject(mockError('backup.destination', 'Destination dans un profil source : choisir un autre emplacement.'));
    if (keyPath && isWithin(keyPath, p.localPath, true)) return Promise.reject(mockError('backup.destination', 'Déplacer la clé hors des profils sources.'));
    if (p.loaded == null) warnings.push(`Chargement du profil ${p.account} non établi en démonstration : le moteur devra le vérifier avant toute copie.`);
    const groups = softwareInventory(sid).groups;
    const selectedFiles = groups.flatMap((g) => g.files).filter((f) => f.selected && (f.scope === 'local' || f.scope === 'roaming'));
    const selectedRegistry = groups.flatMap((g) => g.registry).filter((r) => r.selected && r.scope === 'hkcu');
    profiles.push({ sid: p.sid, identityKind: p.identityKind, account: p.account, profilePath: p.localPath ?? '', loaded: p.loaded, files: selectedFiles, registry: selectedRegistry });
  }
  profiles.sort((a, b) => (a.account.toLowerCase() < b.account.toLowerCase() ? -1 : a.account.toLowerCase() > b.account.toLowerCase() ? 1 : (a.sid < b.sid ? -1 : a.sid > b.sid ? 1 : 0)));

  warnings.push('Plan établi sur le monde fictif de démonstration : aucune donnée réelle, aucune sauvegarde réelle possible à partir de ce plan.');
  if (!encrypted) warnings.push('Sauvegarde non chiffrée par FILPRO. Protéger le support et son accès.');
  if (profiles.some((p) => p.loaded === true)) warnings.push('Un ou plusieurs profils sont chargés : VSS demandé, sa disponibilité sera contrôlée par le moteur avant toute copie.');

  const totalFiles = profiles.reduce((n, p) => n + p.files.length, 0);
  const totalKeys = profiles.reduce((n, p) => n + p.registry.length, 0);
  const scope = `Profils ${profiles.length} · dossiers AppData ${totalFiles} · clés HKCU ${totalKeys}. Contenu principal des fichiers ordinaires. `
    + 'Pas de migration des applications, des ruches du registre, des flux nommés, ni des dossiers vides.';

  return Promise.resolve({
    schemaVersion: 1,
    productVersion: '2.0.1-dev',
    id: hex32(),
    createdUtc: iso(0),
    demonstration: true,
    purpose: String(args.purpose ?? '').trim() || 'Sauvegarde des profils sélectionnés',
    destination,
    encrypted,
    keyPath,
    useVss,
    migApp: args.migApp === true,
    noCompress,
    continueOnError: !!args.continueOnError,
    profiles,
    space: { state: 'inconnu', storeBytes: null, tempBytes: null, text: 'Taille à calculer avant confirmation', notice: SPACE_NOTICE, measuredAt: null },
    exclusions: RULE_EXCLUSIONS,
    hasUnknownSpace: true,
    requiresManualSpaceAcknowledgement: true,
    scope,
    warnings,
    notice: PLAN_NOTICE,
  });
}

export function confirmPlan() {
  // Le pont réel refuse toute Write avant exécution (moteur d'écriture absent). En prévisualisation (démonstration), le
  // même refus honnête est renvoyé : rien n'est préparé ni exécuté.
  return Promise.reject(mockError('simulation.readOnly', 'Écritures désactivées en démonstration : aucune action réelle tentée.'));
}

// ---------- Plan de restauration (aperçu canonisé, miroir de RestorePlan.Validate pour la prévisualisation) ----------
// Ce n'est PAS le moteur : aucune lecture du magasin, aucune mesure, aucune écriture, aucun LoadState. Il reproduit les
// formes et les refus de la fonction pure (codes restore.* et conflict.policy) pour développer l'écran de confirmation sans
// hôte WebView2. L'exploitabilité du magasin reste une déclaration de l'opérateur : aucun manifeste n'est relu ici.

const RESTORE_PLAN_NOTICE = 'Plan de préparation : aucun profil lu, aucune écriture, aucune ruche montée, aucun LoadState '
  + 'exécuté. La mesure de l’espace, le contrôle des règles et de la clé, la sauvegarde préalable effective et la '
  + 'restauration appartiennent au moteur. Un arrêt demandé n’est pas un arrêt achevé.';

const DECLARED_READY_STATUS = 'Prêt déclaré par l’opérateur : aucun manifeste de magasin n’a été relu par FILPRO dans ce lot.';
const DECLARED_NOT_READY_STATUS = 'Non prêt déclaré par l’opérateur : le plan refuse ce magasin tant qu’il n’est pas déclaré exploitable.';

const RESTORE_CONFLICT_NOTICE = 'Portées de conflits lues chez le fournisseur poste pour le profil cible retenu (monde '
  + 'fictif en démonstration) : ce ne sont pas les portées du magasin, que le moteur relira avant LoadState. Une décision '
  + 'est exigée pour chaque groupe affiché et deux portées qui se chevauchent avec des politiques différentes sont '
  + 'refusées.';

const RESTORE_LIMITATIONS = [
  'Aucun manifeste de magasin n’a été relu : l’exploitabilité du magasin est déclarée par l’opérateur et le moteur devra la contrôler avant LoadState.',
  'Aucun SID ni chemin source n’est emprunté à la cible : la revue des identités ne montre que les comptes, faute de manifeste relu.',
  'Les portées de conflits proviennent du fournisseur poste, pas du magasin : le moteur les revalidera sur le contenu réellement restauré.',
  'Le compte qui exécute FILPRO est exclu des destinations ; en démonstration, il s’agit du compte fictif DEMO\\camille.',
  'Fraîcheur et empreinte du plan non contrôlées à la confirmation : aucun magasin de plans n’existe dans ce lot.',
  'Aucune restauration exécutée : aucune écriture, aucune ruche montée, aucune mesure d’espace.',
];

const EXPLICIT_ACCOUNT = /^[^\\*?"\x00-\x1f/:]+\\[^\\*?"\x00-\x1f/:]+$/;
const TARGET_SID = /^S-1-5-21-\d+-\d+-\d+-\d+$/;
const ROOTED = /^(?:[A-Za-z]:\\|\\\\[^\\/:*?"<>|\x00-\x1f]+\\[^\\/:*?"<>|\x00-\x1f]+)/;
const INVALID_PATH_CHARS = /[\x00-\x1f:*?"<>|]/;
const RELATIVE_ESCAPE = /(^|\\)\.\.?($|\\)/;

function normalizePath(value) {
  const unc = value.startsWith('\\\\');
  let out = '';
  for (const c of value.slice(unc ? 2 : 0)) {
    if (c === '\\' && out.length > 0 && out.endsWith('\\')) continue;
    out += c;
  }
  return (unc ? '\\\\' : '') + out.replace(/\\+$/, '');
}

function isWithin(candidate, parent, allowRoot) {
  if (!candidate || !parent) return false;
  const a = candidate.toLowerCase();
  const b = parent.toLowerCase();
  if (a === b) return allowRoot;
  return a.length > b.length && a.startsWith(b) && candidate[b.length] === '\\';
}

function requireRooted(value, code, message) {
  const raw = String(value ?? '').trim().replace(/\//g, '\\');
  if (!raw || !ROOTED.test(raw)) throw mockError(code, message);
  const tail = raw.slice(2);
  if (INVALID_PATH_CHARS.test(tail) || RELATIVE_ESCAPE.test(tail)) throw mockError(code, message);
  return normalizePath(raw);
}

function requireAccount(value, code, who) {
  const account = String(value ?? '').trim();
  if (!account || account.length > 512 || account.startsWith('S-1-') || !EXPLICIT_ACCOUNT.test(account)) {
    throw mockError(code, `Compte non explicite ou non résolu (${who}) : ${value || '(compte absent)'}. Choisir explicitement un compte existant.`);
  }
  return account;
}

function isEntraAccount(account) {
  return account.startsWith('S-1-12-1-') || account.toLowerCase().startsWith('azuread\\');
}

function summarizeConflicts(choices, policies) {
  if (!choices.length) return 'Aucun réglage spécifique à restaurer.';
  const map = Object.fromEntries((policies ?? []).map((p) => [p.id, p.mode]));
  const restored = [];
  const kept = [];
  const mixed = [];
  const unconfigured = [];
  for (const choice of choices) {
    const mode = map[choice.id];
    if (!mode) { unconfigured.push(choice.name); continue; }
    if (mode === 'useSaved') restored.push(choice.name);
    else if (mode === 'keepCurrent') kept.push(choice.name);
    else mixed.push(choice.name);
  }
  const lines = [];
  if (restored.length) lines.push('Rétablissement demandé du contenu sauvegardé : ' + restored.join(', '));
  if (kept.length) lines.push('Conservation demandée des réglages actuels en cas de conflit : ' + kept.join(', '));
  if (mixed.length) lines.push('Coexistence demandée des fichiers ; registre actuel conservé : ' + mixed.join(', '));
  if (unconfigured.length) lines.push('Politique de conflit non définie : ' + unconfigured.join(', '));
  if (!lines.length) lines.push('Aucune action spécifique sur les conflits de réglages.');
  lines.push('Validation utilisateur restante : l’activation effective des réglages (registre, services, sessions) doit être contrôlée dans la session cible après restauration.');
  return lines.join('\r\n');
}

function scopesOf(group, mode) {
  const scopes = [];
  for (const f of group.files ?? []) {
    const rel = String(f.rel ?? '').replace(/\//g, '\\').replace(/\\+$/, '');
    scopes.push({ type: 'File', path: `${f.scope === 'roaming' ? 'Roaming' : 'Local'}|${rel}`, mode });
  }
  for (const k of group.registry ?? []) {
    const rel = String(k.rel ?? '').replace(/\//g, '\\').replace(/\\+$/, '');
    scopes.push({ type: 'Registry', path: rel, mode });
  }
  return scopes;
}

function resolveConflictPolicies(choices, policies) {
  if (choices.length > 500) throw mockError('conflict.policy', 'Trop de groupes de réglages.');
  const effective = (policies ?? []).length ? policies : choices.map((c) => ({ id: c.id, mode: 'keepBoth' }));
  const available = {};
  for (const choice of choices) {
    if (available[choice.id]) throw mockError('conflict.policy', 'Groupe de réglages ambigu.');
    available[choice.id] = choice;
  }
  const seen = {};
  const result = [];
  for (const policy of effective) {
    const mode = String(policy?.mode ?? '');
    if (!policy?.id || !available[policy.id] || seen[policy.id]) throw mockError('conflict.policy', 'Politique sans groupe sauvegardé ou en double.');
    if (!['keepBoth', 'keepCurrent', 'useSaved'].includes(mode)) throw mockError('conflict.policy', 'Politique de restauration inconnue.');
    seen[policy.id] = true;
    result.push({ id: policy.id, mode });
  }
  if (result.length !== choices.length) throw mockError('conflict.policy', 'Décision manquante pour un groupe de réglages.');
  const scopes = [];
  const patterns = {};
  for (const row of result) {
    for (const scope of scopesOf(available[row.id], row.mode)) {
      for (const previous of scopes) {
        const overlaps = previous.path.toLowerCase() === scope.path.toLowerCase()
          || scope.path.toLowerCase().startsWith(`${previous.path.toLowerCase()}\\`)
          || previous.path.toLowerCase().startsWith(`${scope.path.toLowerCase()}\\`);
        if (previous.type === scope.type && previous.mode !== scope.mode && overlaps) {
          throw mockError('conflict.policy', 'Deux logiciels chevauchent les mêmes réglages avec des politiques différentes. Choisir une politique commune.');
        }
      }
      scopes.push(scope);
      const key = `${scope.type}|${scope.path}`;
      if (patterns[key] && patterns[key] !== scope.mode) {
        throw mockError('conflict.policy', 'Deux logiciels imposent des politiques différentes au même réglage.');
      }
      patterns[key] = scope.mode;
    }
  }
  return result.sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}

export function restorePlan(args = {}, inspection = null) {
  const bundle = String(args.bundle ?? '').trim();
  if (!bundle) throw mockError('restore.store', 'Chemin du magasin source absolu requis : choisir une sauvegarde à restaurer.');
  const normalizedBundle = requireRooted(bundle, 'restore.store', 'Chemin du magasin source absolu requis : choisir une sauvegarde à restaurer.');
  const ready = !!args.storeReady;
  if (!ready) throw mockError('restore.store', 'Source absente, incomplète ou non interprétable.');

  const manifestUsers = (Array.isArray(args.manifestUsers) ? args.manifestUsers : []).map((u) => String(u).trim()).filter(Boolean);
  const usersIn = Array.isArray(args.users) ? args.users : [];
  if (!usersIn.length) throw mockError('restore.selection', 'Sélectionner au moins un compte source à restaurer.');
  const legacy = !!args.legacy;
  const partial = !!args.partial;
  const encrypted = !!args.encrypted;

  const users = [];
  for (const user of usersIn) {
    const account = requireAccount(user, 'restore.selection', 'compte source');
    if (isEntraAccount(account)) throw mockError('restore.selection', `Identité Entra non prise en charge par cette restauration : ${account}.`);
    if (users.some((u) => u.toLowerCase() === account.toLowerCase())) throw mockError('restore.selection', `Compte source en double : ${account}`);
    users.push(account);
  }
  users.sort((a, b) => (a.toLowerCase() < b.toLowerCase() ? -1 : a.toLowerCase() > b.toLowerCase() ? 1 : 0));

  const warnings = [];
  if (legacy) {
    if (!args.legacyAcknowledged) {
      throw mockError('restore.store', 'Ancien magasin : cocher « J’ai examiné les limites du magasin ancien et ses règles » avant de restaurer.');
    }
    warnings.push('Ancien magasin sans manifeste FILPRO : comptes source non garantis et règles non vérifiables ; la restauration reste à valider en session cible.');
  } else {
    const manifest = manifestUsers.map((u) => u.toLowerCase());
    for (const user of users) {
      if (!manifest.includes(user.toLowerCase())) throw mockError('restore.store', `Compte source absent du manifeste : ${user}.`);
    }
    if (partial) {
      if (!args.partialAcknowledged) throw mockError('restore.store', 'Le magasin contient des omissions. Examiner ses journaux puis reconnaître explicitement cet état partiel.');
      warnings.push('Magasin partiel reconnu par l’opérateur : les omissions restent à examiner dans les journaux du magasin.');
    }
    if (!String(args.usmtVersion ?? '').trim()) {
      warnings.push('Version USMT de création non enregistrée dans le manifeste : aucune équivalence supposée, relire le plan et tester la restauration.');
    }
  }

  // Correspondances : la cible par défaut est le compte source ; doublon, fusion et compte non sélectionné refusés.
  const rows = Array.isArray(args.mappings) ? args.mappings : [];
  const selected = users.map((u) => u.toLowerCase());
  const bySource = {};
  for (const row of rows) {
    const source = requireAccount(row?.source, 'restore.mapping', 'compte source d’une correspondance');
    if (!selected.includes(source.toLowerCase())) throw mockError('restore.mapping', `Correspondance portant sur un compte non sélectionné : ${source}.`);
    (bySource[source.toLowerCase()] ||= []).push({ source, target: row?.target ?? null });
  }
  const destinations = [];
  const mappings = [];
  for (const user of users) {
    const found = bySource[user.toLowerCase()] ?? [];
    if (found.length > 1) throw mockError('restore.mapping', `Correspondance de compte en double : ${user}.`);
    const target = found.length === 1 ? requireAccount(found[0].target, 'restore.mapping', 'compte cible') : user;
    if (isEntraAccount(target)) throw mockError('restore.mapping', `Identité Entra non prise en charge comme cible : ${target}.`);
    if (destinations.some((d) => d.toLowerCase() === target.toLowerCase())) {
      throw mockError('restore.mapping', `Fusion de plusieurs comptes vers une destination interdite. Créer des opérations distinctes : ${target}.`);
    }
    destinations.push(target);
    mappings.push({ source: user, target, targetSid: null });
  }

  // Profils cibles relus chez le fournisseur poste (le JavaScript ne fournit que le SID).
  const bySid = Object.fromEntries(profilesInventory().profiles.map((p) => [p.sid, p]));
  const targetSids = Array.isArray(args.targetSids) ? args.targetSids.map((s) => String(s).trim()).filter(Boolean) : [];
  const targets = [];
  for (const rawSid of targetSids) {
    const candidate = bySid[rawSid];
    if (!candidate) throw mockError('restore.target', 'Profil cible inconnu : actualiser la liste des profils avant de préparer la restauration.');
    const sid = candidate.sid;
    if (!TARGET_SID.test(sid)) throw mockError('restore.target', `Identité de destination non prise en charge pour cette restauration : ${sid}.`);
    if (candidate.identityKind === 'entra' || sid.startsWith('S-1-12-1-')) throw mockError('restore.target', 'Profil cible Entra non pris en charge par USMT.');
    if (sid === SIDS.camille) throw mockError('restore.target', 'Le compte qui exécute FILPRO ne peut pas être une destination.');
    if (targets.some((target) => target.sid === sid)) throw mockError('restore.target', 'Deux profils cibles partagent le même SID.');
    const account = requireAccount(candidate.account, 'restore.target', 'compte cible');
    if (!destinations.some((d) => d.toLowerCase() === account.toLowerCase())) throw mockError('restore.target', `Profil cible hors correspondance : ${account}.`);
    const path = requireRooted(candidate.localPath, 'restore.target', 'Chemin du profil cible absolu requis : recalculer les cibles.');
    if (candidate.loaded === true) throw mockError('restore.target', `Profil cible encore chargé : fermer la session du compte ${account} avant la restauration.`);
    if (candidate.loaded == null) warnings.push(`Chargement du profil cible ${account} non établi en démonstration : le moteur devra le vérifier avant LoadState.`);
    targets.push({ sid, identityKind: candidate.identityKind, account, profilePath: path, loaded: candidate.loaded });
  }
  targets.sort((a, b) => (a.account.toLowerCase() < b.account.toLowerCase() ? -1 : a.account.toLowerCase() > b.account.toLowerCase() ? 1 : 0));
  for (const mapping of mappings) {
    const hit = targets.find((t) => t.account.toLowerCase() === mapping.target.toLowerCase());
    if (hit) mapping.targetSid = hit.sid;
  }

  // Sauvegarde préalable : exigée dès qu'une destination existe, jamais déclarée vérifiée par le plan.
  let prior;
  const priorRoot = String(args.priorRoot ?? '').trim();
  const priorKey = String(args.priorKeyPath ?? '').trim();
  if (!targets.length) {
    prior = {
      state: 'nonApplicable',
      root: priorRoot ? requireRooted(priorRoot, 'restore.prior', 'Dossier de sauvegarde préalable absolu requis.') : null,
      keyPath: priorKey ? requireRooted(priorKey, 'restore.prior', 'Clé de sauvegarde préalable absolue requise.') : null,
      verified: false,
      statement: 'Aucun profil de destination existant à sauvegarder préalablement.',
    };
  } else {
    if (!priorRoot) throw mockError('restore.prior', 'Dossier de sauvegarde préalable existant requis avant de restaurer sur un profil déjà présent.');
    const root = requireRooted(priorRoot, 'restore.prior', 'Dossier de sauvegarde préalable existant requis.');
    if (isWithin(root, normalizedBundle, true)) throw mockError('restore.prior', 'La sauvegarde préalable doit être hors du magasin à restaurer.');
    for (const target of targets) {
      if (isWithin(root, target.profilePath, true)) throw mockError('restore.prior', 'La sauvegarde préalable ne peut pas être dans un profil de destination.');
    }
    let keyPath = null;
    if (priorKey) {
      keyPath = requireRooted(priorKey, 'restore.prior', 'Clé de sauvegarde préalable absolue requise.');
      for (const target of targets) {
        if (isWithin(keyPath, target.profilePath, true)) throw mockError('restore.prior', 'La clé de sécurité ne doit pas être dans un profil sauvegardé.');
      }
    }
    const identified = !!args.priorIdentified;
    prior = {
      state: identified ? 'identifiee' : 'declaree',
      root,
      keyPath,
      verified: false,
      statement: String(args.priorStatement ?? '').trim() || (identified
        ? 'Magasin identifié et règles contrôlées. Le dernier contrôle natif est une information enregistrée, pas une vérification actuelle du contenu.'
        : 'Sauvegarde externe déclarée par l’opérateur, non contrôlée par FILPRO.'),
    };
    warnings.push(identified
      ? 'Sauvegarde préalable identifiée : le dernier contrôle natif est une information enregistrée, pas une vérification actuelle du contenu.'
      : 'Sauvegarde préalable déclarée par l’opérateur, non contrôlée par FILPRO : le moteur devra la vérifier avant LoadState.');
  }

  // Clé du magasin chiffré : seul le chemin est porté, jamais le contenu.
  let sourceKeyPath = null;
  if (encrypted) {
    sourceKeyPath = requireRooted(args.sourceKeyPath, 'restore.store', 'Fichier clé de la sauvegarde requis pour ce magasin chiffré : chemin de clé invalide.');
    warnings.push('Existence, contenu et correspondance de la clé avec le magasin restent à contrôler par le moteur ; la clé n’est jamais affichée ni journalisée.');
  } else if (String(args.sourceKeyPath ?? '').trim()) {
    throw mockError('restore.store', 'Chemin de clé indiqué sans magasin chiffré.');
  }

  // Le lecteur du magasin est l'autorité quand une inspection est fournie ; l'ancien aperçu pur reste testable à part.
  let conflictSid = String(args.conflictSid ?? '').trim();
  let choices = [];
  if (!conflictSid && !inspection) {
    if (targetSids.length > 1) {
      throw mockError('arguments.invalid', 'Arguments invalides : préciser le profil cible dont les réglages sont examinés.');
    }
    conflictSid = targetSids[0] ?? '';
  } else if (conflictSid && !bySid[conflictSid]) {
    throw mockError('restore.target', 'Profil cible inconnu pour l’examen des conflits : actualiser la liste des profils.');
  }
  if (conflictSid && !targetSids.includes(conflictSid)) {
    throw mockError('restore.target', 'Le profil examiné pour les conflits doit appartenir aux destinations sélectionnées.');
  }
  if (inspection) choices = inspection.conflictChoices ?? [];
  else if (conflictSid) choices = softwareInventory(conflictSid).groups;
  const policies = resolveConflictPolicies(choices, Array.isArray(args.conflictPolicies) ? args.conflictPolicies : []);
  const conflictSummary = summarizeConflicts(choices, policies);

  // Revue des identités : sans manifeste relu, aucun SID ni chemin source n'est emprunté à la cible.
  const identityPairs = mappings.map((m) => ({
    sourceAccount: m.source,
    sourceSid: inspection?.sourceProfiles?.find((source) => source.account.toLowerCase() === m.source.toLowerCase())?.sid ?? null,
    sourcePath: inspection?.sourceProfiles?.find((source) => source.account.toLowerCase() === m.source.toLowerCase())?.profilePath ?? null,
    targetAccount: m.target,
    targetSid: m.targetSid,
  }));
  const reviewLines = ['Correspondances de comptes pour la restauration'
    + (String(args.sourceComputerName ?? '').trim() ? ` (poste source : ${String(args.sourceComputerName).trim()})` : '')
    + ' :'];
  for (const pair of identityPairs) {
    reviewLines.push(`  ${pair.sourceAccount} → ${pair.targetSid ? `${pair.targetAccount} [${pair.targetSid}]` : `${pair.targetAccount} (nouveau profil)`}`);
  }
  reviewLines.push('Validation utilisateur restante : l’activation effective des réglages (registre, services, sessions) doit être contrôlée dans la session cible après restauration.');

  warnings.push('Plan établi sur le monde fictif de démonstration : aucune donnée réelle, aucune restauration réelle à partir de ce plan.');

  const plan = {
    schemaVersion: 1,
    productVersion: '2.0.1-dev',
    id: hex32(),
    createdUtc: iso(0),
    demonstration: true,
    purpose: String(args.purpose ?? '').trim() || 'Restauration des comptes sélectionnés sur ce poste',
    bundle: normalizedBundle,
    legacy,
    encrypted,
    sourceKeyPath,
    sourceComputerName: String(args.sourceComputerName ?? '').trim() || null,
    sourceCreatedUtc: String(args.sourceCreatedUtc ?? '').trim() || null,
    users,
    mappings,
    identityPairs,
    targets,
    priorBackup: prior,
    conflictPolicies: policies,
    conflictSummary,
    hasUnknownSpace: true,
    requiresManualSpaceAcknowledgement: true,
    warnings,
    identityReview: reviewLines.join('\r\n'),
    notice: RESTORE_PLAN_NOTICE,
  };

  return Promise.resolve({
    plan,
    store: {
      bundle: normalizedBundle,
      status: inspection?.store?.status ?? (ready ? DECLARED_READY_STATUS : DECLARED_NOT_READY_STATUS),
      ready,
      declaredByOperator: !inspection,
      legacy,
      partial,
      encrypted,
      manifestUserCount: manifestUsers.length,
      usmtVersion: String(args.usmtVersion ?? '').trim() || null,
      sourceComputerName: plan.sourceComputerName,
      sourceCreatedUtc: plan.sourceCreatedUtc,
    },
    conflictNotice: inspection ? 'Portées relues dans le magasin fictif, jamais empruntées au profil cible.' : RESTORE_CONFLICT_NOTICE,
    limitations: inspection ? ['Contrôle fictif en mémoire ; aucune validation de restauration Windows.'] : RESTORE_LIMITATIONS,
  });
}

export function confirmRestorePlan() {
  // Comme sauvegarder.confirmPlan : le pont refuse toute Write avant exécution (moteur de restauration absent). Les
  // accusés sont recontrôlés côté serveur ; en prévisualisation, le même refus honnête est renvoyé.
  return Promise.reject(mockError('simulation.readOnly', 'Écritures désactivées en démonstration : aucune action réelle tentée.'));
}

