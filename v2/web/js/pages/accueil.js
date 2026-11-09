// Accueil FILPRO : vue agrégée servie par le fournisseur actif, avec disponibilité explicite de chaque inventaire.
// Aucune donnée fictive n'est présentée comme réelle : le bandeau et les cartes le disent, et l'indisponibilité est affichée.
import { call } from '../bridge.js';
import { icon } from '../icons.js';
import { esc, errorText, applyVars, reveal, countUp, formatNumber, relTime, formatDateTime } from '../ui.js';
import { pill } from '../kit/index.js';

export const stylesheet = true;

const PROFILE_STATE = {
  actif: { tone: 'ok', label: 'Actif' },
  inactif: { tone: 'off', label: 'Inactif' },
  verrouille: { tone: 'warn', label: 'Verrouillé' },
  deconnecte: { tone: 'warn', label: 'Session déconnectée' },
  inconnu: { tone: 'warn', label: 'État inconnu' },
};
const BACKUP_STATE = {
  reussie: { tone: 'ok', label: 'Réussie' },
  encours: { tone: 'info', label: 'En cours' },
  echouee: { tone: 'alert', label: 'Échouée' },
  annulee: { tone: 'off', label: 'Annulée' },
  partielle: { tone: 'warn', label: 'Partielle' },
};
const JOURNAL_LEVEL = {
  info: { tone: 'info', label: 'Info' },
  avertissement: { tone: 'warn', label: 'Attention' },
  erreur: { tone: 'alert', label: 'Erreur' },
};

function size(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return '—';
  const units = ['o', 'Ko', 'Mo', 'Go', 'To'];
  let n = bytes; let u = 0;
  while (n >= 1024 && u < units.length - 1) { n /= 1024; u += 1; }
  return `${formatNumber(u === 0 ? n : Math.round(n * 10) / 10, u === 0 ? 0 : 1)} ${units[u]}`;
}

function statePill(map, key) {
  const s = map[String(key).toLowerCase()] ?? { tone: 'off', label: String(key ?? '—') };
  return pill(s.tone, s.label, { size: 'sm' });
}

function layout(demonstration) {
  return `<div class="page accueil">
    <header class="page-header reveal">
      <div class="head-row"><span class="page-icon">${icon('home')}</span>
        <div class="titles"><h1 class="gradient-text">Accueil</h1>
          <p class="subtitle">Vos profils, vos sauvegardes et les dernières opérations.</p></div></div>
      <div class="actions">
        <button class="btn" type="button" data-act="refresh">${icon('refresh')}Actualiser</button>
      </div>
    </header>

    <section class="hero reveal spot" data-demo>
      <span class="glow-ring" aria-hidden="true"></span>
      <span class="orb a" aria-hidden="true"></span><span class="orb b" aria-hidden="true"></span>
      <div class="eyebrow">${icon('shield')}<span data-mode-title>${demonstration ? 'Espace de démonstration' : 'Vue d’ensemble du poste'}</span></div>
      <h2>Gérez vos profils Windows</h2>
      <p class="lead" data-mode-lead>${demonstration ? 'Explorez les profils, les sauvegardes et les réparations avec des données fictives. Les opérations restent entièrement en mémoire.' : 'Examinez les profils et les opérations de cette session. Une préparation longue demande toujours votre confirmation.'}</p>
      <div class="hero-row home-actions">
        <a class="btn primary" href="#/sauvegarder">${icon('disk')}Préparer une sauvegarde</a>
        <a class="btn" href="#/analyser">${icon('users')}Voir les profils</a>
      </div>
      <div class="hero-row" data-hero-chips aria-busy="true"></div>
    </section>

    <div class="grid home-kpis" role="group" aria-label="Indicateurs de la session" aria-busy="true">
      <section class="card kpi span-3 reveal spot"><div class="label">${icon('users')}Profils suivis</div>
        <div class="value"><b data-kpi="profiles">—</b></div><div class="foot" data-kpi="activeprofiles">—</div></section>
      <section class="card kpi span-3 reveal spot"><div class="label">${icon('activity')}Sessions actives</div>
        <div class="value"><b data-kpi="sessions">—</b></div><div class="foot" data-kpi="sessionnotice">—</div></section>
      <section class="card kpi span-3 reveal spot"><div class="label">${icon('disk')}Sauvegardes</div>
        <div class="value"><b data-kpi="backups">—</b></div><div class="foot" data-kpi="lastbackup">—</div></section>
      <section class="card kpi span-3 reveal spot"><div class="label">${icon('clock')}Tâches actives</div>
        <div class="value"><b data-kpi="tasks">—</b></div><div class="foot" data-kpi="stopping">—</div></section>
    </div>

    <div class="grid">
      <section class="card span-6 reveal spot" data-card="profiles" aria-busy="true">
        <div class="card-head"><span class="card-icon">${icon('users')}</span><h3>Profils récents</h3>
          <span class="meta" data-mode-meta>—</span></div>
        <div class="list" data-list="profiles"></div>
      </section>
      <section class="card span-6 reveal spot" data-card="backups" aria-busy="true">
        <div class="card-head"><span class="card-icon">${icon('disk')}</span><h3>Sauvegardes récentes</h3>
          <span class="meta" data-mode-meta>—</span></div>
      <div class="list" data-list="backups"></div>
      </section>
    </div>

    <section class="card reveal spot" data-card="journal" aria-busy="true">
      <div class="card-head"><span class="card-icon">${icon('clock')}</span><h3>Journal récent</h3>
        <span class="meta" data-mode-meta>—</span></div>
      <div class="timeline" data-list="journal"></div>
    </section>
  </div>`;
}

function emptyList(text) {
  return `<div class="empty"><div class="art">${icon('info')}</div><p>${esc(text)}</p></div>`;
}

function renderProfiles(list, profiles) {
  if (!profiles?.length) { list.innerHTML = emptyList('Aucun profil suivi pour le moment.'); return; }
  list.innerHTML = profiles.slice(0, 6).map((p) => `<div class="list-row">
    <span class="avatar" aria-hidden="true">${esc((p.name || '?').slice(0, 1).toUpperCase())}</span>
    <div class="body"><div class="title">${esc(p.name)}</div>
      <div class="sub">${esc(p.kind)} · ${Number.isFinite(p.sessionCount) ? `${formatNumber(p.sessionCount)} session(s)` : 'sessions inconnues'}${p.lastActivity ? ` · ${esc(relTime(p.lastActivity) ?? '')}` : ''}</div></div>
    ${statePill(PROFILE_STATE, p.state)}</div>`).join('');
}

function renderBackups(list, backups) {
  if (!backups?.length) { list.innerHTML = emptyList('Aucune sauvegarde dans le magasin.'); return; }
  list.innerHTML = backups.slice(0, 6).map((b) => `<div class="list-row">
    <span class="avatar pc" aria-hidden="true">${icon('disk')}</span>
    <div class="body"><div class="title">${esc(b.scope)}</div>
      <div class="sub mono">${esc(b.id)} · ${esc(size(b.sizeBytes))} · ${esc(formatDateTime(b.createdAt) ?? '')}</div></div>
    ${statePill(BACKUP_STATE, b.state)}</div>`).join('');
}

function renderJournal(list, journal) {
  if (!journal?.length) { list.innerHTML = emptyList('Le journal est vide.'); return; }
  list.innerHTML = journal.slice(0, 8).map((e) => {
    const lvl = JOURNAL_LEVEL[String(e.level).toLowerCase()] ?? JOURNAL_LEVEL.info;
    return `<div class="ev" data-tone="${lvl.tone}">
      <div class="when">${esc(formatDateTime(e.at) ?? '')} · ${esc(lvl.label)}</div>
      <div>${esc(e.message)}</div></div>`;
  }).join('');
}

function renderChips(host, snapshot) {
  const running = (snapshot.tasks ?? []).filter((t) => String(t.state).toLowerCase() === 'encours').length;
  host.innerHTML = [
    pill(snapshot.demonstration ? 'accent' : 'info', snapshot.demonstration ? 'Données fictives' : 'Données du poste', { icon: 'database', size: 'sm' }),
    pill(running ? 'info' : 'off', snapshot.availability?.tasks === true ? `${formatNumber(running)} tâche(s) ${snapshot.demonstration ? 'simulée(s) ' : ''}en cours` : 'Suivi indisponible', { size: 'sm' }),
    pill('off', `Actualisé ${relTime(snapshot.generatedAt) ?? ''}`, { size: 'sm' }),
  ].join('');
}

function unavailable(main, message) {
  main.innerHTML = `<div class="page accueil"><section class="card error-card" role="alert"><div class="empty">
    <div class="art">${icon('alert')}</div><h2>Accueil indisponible</h2>
    <p>${esc(message)}</p>
    <div class="home-actions"><button class="btn primary" type="button" data-act="retry">${icon('refresh')}Réessayer</button>
      <a class="btn" href="#/analyser">${icon('users')}Voir les profils</a></div>
  </div></section></div>`;
}

function showSnapshot(main, snapshot) {
  const profiles = snapshot.profiles ?? [];
  const sessions = snapshot.sessions ?? [];
  const backups = snapshot.backups ?? [];
  const tasks = snapshot.tasks ?? [];
  const activeProfiles = profiles.filter((p) => String(p.state).toLowerCase() === 'actif').length;
  const activeSessions = sessions.filter((s) => String(s.state).toLowerCase() === 'active').length;
  const running = tasks.filter((t) => String(t.state).toLowerCase() === 'encours').length;
  const stopping = tasks.filter((t) => String(t.state).toLowerCase() === 'arretdemande').length;
  const unknownSessions = profiles.filter((p) => !Number.isFinite(p.sessionCount)).length;
  const available = snapshot.availability ?? {};
  const last = backups[0];

  const count = (key, value, known, delay) => { const host = main.querySelector(`[data-kpi="${key}"]`); if (known) countUp(host, value, { delay }); else host.textContent = '—'; };
  count('profiles', profiles.length, available.profiles === true, 120);
  count('sessions', activeSessions, available.profiles === true && unknownSessions === 0, 200);
  count('backups', backups.length, available.backups === true, 280);
  count('tasks', running + stopping, available.tasks === true, 360);
  main.querySelector('[data-kpi="activeprofiles"]').textContent = available.profiles === true ? `${formatNumber(activeProfiles)} actif(s) ${snapshot.demonstration ? 'dans cet exemple' : 'lus sur ce poste'}` : 'Inventaire indisponible';
  main.querySelector('[data-kpi="sessionnotice"]').textContent = unknownSessions ? `${formatNumber(activeSessions)} active(s) connues · ${formatNumber(unknownSessions)} état(s) inconnu(s)` : snapshot.demonstration ? 'dans cet exemple' : 'lues sur ce poste';
  main.querySelector('[data-kpi="stopping"]').textContent = available.tasks === true ? `${formatNumber(stopping)} arrêt(s) demandé(s) · cette session` : 'Suivi indisponible';
  main.querySelector('[data-kpi="lastbackup"]').textContent = available.backups !== true ? 'Magasins non inventoriés' : last ? `dernière : ${relTime(last.createdAt) ?? '—'}` : 'aucune sauvegarde inventoriée';

  if (available.profiles === true) renderProfiles(main.querySelector('[data-list="profiles"]'), profiles);
  else main.querySelector('[data-list="profiles"]').innerHTML = emptyList('Inventaire des profils indisponible. Aucun total ne peut être établi.');
  if (available.backups === true) renderBackups(main.querySelector('[data-list="backups"]'), backups);
  else main.querySelector('[data-list="backups"]').innerHTML = emptyList('Les magasins ne sont pas inventoriés globalement. Choisissez un magasin dans Restaurer ou Récupérer pour en vérifier le contenu.') + '<a class="btn" href="#/sauvegarder?tab=restaurer">Inspecter un magasin</a>';
  if (available.journal === true) renderJournal(main.querySelector('[data-list="journal"]'), snapshot.journal ?? []);
  else main.querySelector('[data-list="journal"]').innerHTML = emptyList('Journal indisponible pour cette session.');
  main.querySelectorAll('[data-mode-meta]').forEach((label) => { label.textContent = snapshot.demonstration ? 'démonstration' : 'cette session'; });
  renderChips(main.querySelector('[data-hero-chips]'), snapshot);
  main.querySelector('[data-hero-chips]').removeAttribute('aria-busy');
  main.querySelector('.home-kpis').removeAttribute('aria-busy');
  ['profiles', 'backups', 'journal'].forEach((k) => main.querySelector(`[data-card="${k}"]`)?.removeAttribute('aria-busy'));
  applyVars(main);
}

export function render(main, ctx = {}) {
  let active = true;
  let loading = false;
  let page;
  let stop = () => {};

  function mount() {
    stop();
    main.innerHTML = layout(ctx.session?.demonstration === true);
    page = main.firstElementChild;
    applyVars(page);
    stop = reveal(page);
  }

  async function load(notify = false) {
    if (!active || loading) return;
    if (!page?.querySelector('[data-kpi="profiles"]')) mount();
    loading = true;
    const requestPage = page;
    const button = requestPage.querySelector('[data-act="refresh"]');
    button.disabled = true;
    button.setAttribute('aria-busy', 'true');
    button.classList.add('spinning');
    try {
      const snapshot = await call('accueil.summary', {});
      // Une réponse tardive ne doit jamais repeindre l'écran ouvert entre-temps.
      if (!active || !requestPage.isConnected) return;
      if (!snapshot || typeof snapshot.demonstration !== 'boolean' || snapshot.demonstration !== ctx.session?.demonstration)
        throw new Error('La vue d’ensemble ne correspond pas au contexte de cette session. Actualisez FILPRO.');
      showSnapshot(requestPage, snapshot);
      if (notify) ctx.toast?.('Vue d’ensemble actualisée.', { tone: 'info', life: 2400 });
    } catch (err) {
      if (!active || !requestPage.isConnected) return;
      stop();
      unavailable(main, errorText(err, 'La vue d’ensemble est momentanément indisponible.'));
      page = main.firstElementChild;
    } finally {
      loading = false;
      button.disabled = false;
      button.removeAttribute('aria-busy');
      button.classList.remove('spinning');
    }
  }

  // Un seul gestionnaire par montage, y compris après plusieurs actualisations ou un échec.
  const onClick = (event) => {
    const action = event.target instanceof Element ? event.target.closest('[data-act]')?.dataset.act : null;
    if (action === 'refresh' || action === 'retry') load(true);
  };
  main.addEventListener('click', onClick);
  mount();
  load();
  return () => {
    active = false;
    stop();
    main.removeEventListener('click', onClick);
  };
}
