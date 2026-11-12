// Paramètres : même écran que les Paramètres de Domenia 2. En-tête avec l'état de l'application, recherche d'un réglage, rail
// de sections qui glisse et une section à la fois (Apparence, Sauvegarde et restauration, Logiciels communs, Configuration à
// diffuser, Mises à jour, Démonstration). L'adresse porte la section (#/parametres?section=mises-a-jour). Les lectures de
// l'hôte sont faites une fois pour l'écran et partagées par les sections ; les réglages de sauvegarde en cours de saisie
// restent en brouillon d'une section à l'autre jusqu'à « Enregistrer les réglages ».
import { call, onEvent } from '../bridge.js';
import { icon } from '../icons.js';
import { esc, errorText, reveal, fold, announce, motionReduced } from '../ui.js';
import { confirmDialog } from '../kit/index.js';
import { configurationChanged } from '../configuration-ui.js';
import { glyph, paint, syncSegments, COLON } from './parametres/common.js';
import * as apparence from './parametres/apparence.js';
import * as operations from './parametres/operations.js';
import * as logiciels from './parametres/logiciels.js';
import * as diffusion from './parametres/diffusion.js';
import * as misesAJour from './parametres/mises-a-jour.js';
import * as demonstration from './parametres/demonstration.js';

/** L'écran a sa propre feuille de style (css/pages/parametres.css), chargée par le routeur. */
export const stylesheet = true;

const SECTIONS = [
  { id: 'apparence', label: 'Apparence', desc: 'Thème, animations, menu', icon: 'sparkles', tone: 'accent', mod: apparence },
  { id: 'operations', label: 'Sauvegarde et restauration', desc: 'Options proposées, doublons, suivi', icon: 'disk', tone: 'ok', mod: operations },
  { id: 'logiciels', label: 'Logiciels communs', desc: 'Choix partagés, modèles', icon: 'object', tone: 'info', mod: logiciels },
  { id: 'diffusion', label: 'Configuration à diffuser', desc: 'Défaut du dossier, export et import', icon: 'layers', tone: 'accent', mod: diffusion },
  { id: 'mises-a-jour', label: 'Mises à jour', desc: 'Dossier, ZIP, version', icon: 'refresh', tone: 'info', mod: misesAJour },
  { id: 'demonstration', label: 'Démonstration', desc: 'Profils fictifs et scénarios', icon: 'play', tone: 'accent', mod: demonstration },
];

// Index de la recherche : libellé, mots, section et ligne à éclairer (data-setting).
const INDEX = [
  ['Thème clair, sombre ou contraste élevé', 'theme couleurs sombre clair contraste windows', 'apparence', 'theme'],
  ['Animations riches, réduites ou suivre Windows', 'animations mouvement reduire', 'apparence', 'motion'],
  ['Menu replié en icônes', 'menu navigation replier ctrl b', 'apparence', 'nav'],
  ['Chiffrement des sauvegardes', 'chiffrement cle chiffree', 'operations', 'encrypted'],
  ['Instantané VSS', 'vss cliche shadow fichiers ouverts', 'operations', 'vss'],
  ['Continuer après une erreur', 'erreur poursuite continuer partiel', 'operations', 'continueOnError'],
  ['Réglages recommandés', 'recommandations logiciels selection', 'operations', 'recommendedSelection'],
  ['Doublons à la restauration', 'conflit doublon garder actuel sauvegarde deux fichiers registre', 'operations', 'conflictPolicy'],
  ['Sélectionner la tâche en cours', 'suivi tache operation ouvrir', 'operations', 'autoSelectCurrentTask'],
  ['Notification de fin', 'notification fin operation terminee', 'operations', 'notifyOnCompletion'],
  ['Choix de logiciels communs', 'logiciels communs selection profils effacer', 'logiciels', 'common'],
  ['Modèle de logiciels à partager', 'modele patch exporter importer json sha256 diffusion', 'logiciels', 'templates'],
  ['Définir comme configuration par défaut', 'defaut dossier pack diffuser premier lancement', 'diffusion', 'defaults'],
  ['Exporter ou importer la configuration', 'exporter importer configuration partager', 'diffusion', 'share'],
  ['Source des mises à jour', 'mise a jour dossier unc zip sha256sums source memoriser', 'mises-a-jour', 'source'],
  ['Revenir à la version précédente', 'retour arriere version precedente installer', 'mises-a-jour', 'actions'],
  ['Ouvrir ou quitter la démonstration', 'demonstration fictif simulation reinitialiser', 'demonstration', 'demo'],
  ['Issue de la prochaine opération fictive', 'scenario echec partiel issue', 'demonstration', 'scenario'],
].map(([label, words, section, setting]) => ({ label, words: fold(`${label} ${words}`), section, setting }));

const THEME_LABELS = { light: 'Clair', dark: 'Sombre', contrast: 'Contraste élevé', system: 'Suivre Windows' };
const MOTION_LABELS = { full: 'animations riches', reduced: 'animations réduites', system: 'animations de Windows' };
const POLICY_SHORT = { keepBoth: 'conserver les deux', keepCurrent: 'garder l’actuel', useSaved: 'rétablir le sauvegardé' };
/** Réglages de la barre supérieure : enregistrés par la coque, jamais par le brouillon de cette page. */
const PRESENTATION = new Set(['theme', 'motion', 'navigationCollapsed']);

function heroMarkup() {
  const nav = SECTIONS.map((s, n) => `<li><button type="button" class="pr-nav-item" data-section="${s.id}" data-j="${n}" aria-current="false">
      <span class="pr-nav-ico" data-tone="${s.tone}">${glyph(s.icon)}</span>
      <span class="pr-nav-text"><span class="pr-nav-label">${esc(s.label)}</span><span class="pr-nav-desc">${esc(s.desc)}</span></span>
      <span class="pr-nav-badge" data-badge="${s.id}" hidden></span></button></li>`).join('');
  const tile = (id, label, iconName, section) => `<button type="button" class="pr-pulse-tile" data-pulse="${id}" data-go-section="${section}" aria-busy="true">
      <span class="pr-pulse-ico">${glyph(iconName)}</span><span class="pr-pulse-k">${esc(label)}</span>
      <span class="pr-pulse-v"><span class="skeleton pr-sk w70"></span></span><span class="pr-pulse-s"></span></button>`;
  return `<div class="page pr-page">
    <section class="hero pr-hero reveal"><span class="glow-ring hero-ring" aria-hidden="true"></span><div class="pr-hero-clip" aria-hidden="true"><div class="orbs"><span class="orb a"></span><span class="orb b"></span><span class="orb c"></span></div></div>
      <div class="pr-hero-grid">
        <div class="pr-hero-main">
          <div class="eyebrow">${icon('settings')}<span>Réglages de FILPRO</span></div>
          <h1 class="gradient-text">Paramètres</h1>
          <p class="lead">Apparence, options des sauvegardes, logiciels communs, configuration du dossier et mises à jour${COLON}chaque réglage est conservé sur ce poste. Les mises à jour ne sont recherchées que sur votre demande.</p>
          <div class="chips pr-chips" data-slot="chips"></div>
          <div class="pr-hero-tools"><div class="pr-search" role="search">
            <span class="pr-search-box">${glyph('search')}<input type="search" class="input" id="pr-search" placeholder="Rechercher un réglage…" autocomplete="off" spellcheck="false"
              role="combobox" aria-expanded="false" aria-controls="pr-search-list" aria-autocomplete="list" aria-label="Rechercher un réglage"><kbd>/</kbd></span>
            <ul class="pr-search-list" id="pr-search-list" role="listbox" aria-label="Réglages trouvés" hidden></ul>
          </div><button class="btn" type="button" data-refresh>${glyph('refresh')}<span>Actualiser</span></button></div>
        </div>
        <aside class="pr-pulse" aria-label="État de FILPRO">
          ${tile('appearance', 'Apparence', 'sparkles', 'apparence')}
          ${tile('operations', 'Sauvegardes', 'disk', 'operations')}
          ${tile('common', 'Logiciels communs', 'object', 'logiciels')}
          ${tile('updates', 'Mises à jour', 'refresh', 'mises-a-jour')}
        </aside>
      </div>
      <div data-slot="notices"></div>
    </section>
    <div class="pr-layout">
      <nav class="pr-nav reveal" aria-label="Sections des paramètres"><span class="pr-nav-pill" aria-hidden="true"></span><ul>${nav}</ul></nav>
      <div class="pr-content" data-slot="content" tabindex="-1"><div data-feedback role="status"></div><div data-slot="section"></div></div>
    </div>
  </div>`;
}

export function render(main, ctx) {
  const demo = ctx.session?.demonstration === true;
  const st = {
    alive: true, busy: false, section: null, mounted: null, token: 0,
    config: null, update: null, common: null, catalog: null, source: null, offer: null,
    draft: null, // réglages de sauvegarde modifiés et pas encore enregistrés
    errors: {},
  };
  const params = ctx.params ?? {};
  main.innerHTML = heroMarkup();
  paint(main);
  const stopReveal = reveal(main);
  const nav = main.querySelector('.pr-nav');
  const pill = nav.querySelector('.pr-nav-pill');
  const host = main.querySelector('[data-slot="section"]');
  const content = main.querySelector('[data-slot="content"]');
  const listeners = new Set();

  // ----- État partagé par les sections -----

  const emit = () => {
    if (!st.alive) return;
    for (const fn of [...listeners]) { try { fn(); } catch (err) { console.error(err); } }
    paintTiles();
    main.querySelector('[data-refresh]').disabled = st.busy;
  };
  const feedback = (text, alert = false) => {
    if (!st.alive) return;
    main.querySelector('[data-feedback]').innerHTML = text ? `<div class="callout ${alert ? 'alert' : 'info'}"${alert ? ' role="alert"' : ''}>${esc(text)}</div>` : '';
  };
  async function run(work) {
    if (st.busy) return; st.busy = true; emit();
    try { await work(); } catch (err) { feedback(errorText(err), true); }
    finally { st.busy = false; emit(); }
  }
  const dirty = () => st.draft !== null && st.config !== null
    && Object.entries(st.draft).some(([key, value]) => st.config.settings?.[key] !== value);
  const setConfig = (state) => { st.config = state; if (!dirty()) st.draft = null; emit(); };
  const acceptResult = (result) => {
    if (!st.alive) return;
    st.draft = null;
    setConfig(result.state); configurationChanged(result.state);
    feedback([result.notice, result.writtenPath, result.backupPath ? `Copie préalable : ${result.backupPath}` : ''].filter(Boolean).join(' '));
  };
  /**
   * Enregistre le brouillon des réglages de sauvegarde. Le thème, les animations et le menu ont pu être enregistrés par la
   * coque entre-temps : seule une autre modification des réglages de ce brouillon fait refuser l'enregistrement.
   */
  async function saveDraft() {
    const current = await call('configuration.read', {});
    if (current.revision !== st.config.revision && Object.keys(current.settings || {}).some((key) => !PRESENTATION.has(key)
      && JSON.stringify(current.settings[key]) !== JSON.stringify(st.config.settings?.[key]))) {
      throw new Error('Les réglages ont été modifiés ailleurs depuis l’ouverture de cet écran. Actualisez pour les relire avant d’enregistrer.');
    }
    const settings = { ...current.settings, ...st.draft, migApp: false }; // migApp : champ conservé pour relire les anciennes configurations.
    acceptResult(await call('configuration.save', { settings, expectedRevision: current.revision }));
  }

  const sctx = {
    ctx, demo, call, run, feedback, emit, dirty, saveDraft, acceptResult, setConfig,
    st,
    on: (fn) => { listeners.add(fn); return () => listeners.delete(fn); },
    go: (id, opts) => show(id, opts),
    reload: () => load(),
    /** Chemin choisi par la fenêtre Windows, ou chemin fictif du catalogue en démonstration. */
    choose: async (kind, asset) => demo ? st.catalog?.assets?.[asset] : (await call('session.choosePath', { kind }))?.path,
  };

  // ----- En-tête : pastilles et tuiles d'état -----

  function setTile(id, value, sub, tone) {
    const t = main.querySelector(`[data-pulse="${id}"]`);
    if (!t) return;
    t.removeAttribute('aria-busy');
    t.dataset.tone = tone;
    paint(t);
    t.querySelector('.pr-pulse-v').textContent = value;
    t.querySelector('.pr-pulse-s').textContent = sub;
  }
  function setBadge(id, text, tone = 'off', label = 'À voir') {
    const b = main.querySelector(`[data-badge="${id}"]`);
    if (!b) return;
    if (!text) { b.hidden = true; return; }
    b.hidden = false; b.textContent = text; b.dataset.tone = tone; paint(b); b.setAttribute('aria-label', label);
  }
  function paintTiles() {
    const p = ctx.presentation?.get?.() ?? { theme: 'system', motion: 'system', navCollapsed: false };
    setTile('appearance', THEME_LABELS[p.theme] ?? 'Suivre Windows', `${MOTION_LABELS[p.motion] ?? 'animations riches'} · menu ${p.navCollapsed ? 'replié' : 'déplié'}`, 'accent');
    if (st.config) {
      const s = { ...st.config.settings, ...(st.draft ?? {}) };
      setTile('operations', s.encrypted ? 'Chiffrées' : 'Non chiffrées', `VSS ${s.vss ? 'activé' : 'désactivé'} · doublons${COLON}${POLICY_SHORT[s.conflictPolicy] ?? 'conserver les deux'}`, dirty() ? 'warn' : 'ok');
    } else if (st.errors.config) setTile('operations', '—', st.errors.config, 'warn');
    if (st.errors.common) setTile('common', '—', st.errors.common, 'warn');
    else if (st.common) {
      setTile('common', st.common.hasSelection ? `${st.common.appData?.length ?? 0} dossier(s) · ${st.common.registryKeys?.length ?? 0} clé(s)` : 'Aucun choix commun',
        st.common.hasSelection ? 'pour les profils sans sélection mémorisée' : 'les recommandations s’appliquent si elles sont activées', 'info');
    }
    if (st.errors.update) setTile('updates', '—', st.errors.update, 'warn');
    else if (st.update) {
      const u = st.update;
      setTile('updates', `Version ${u.currentVersion}`, u.error ? `${u.action} — ${u.error}` : u.action, u.error ? 'alert' : u.canInstall ? 'warn' : 'ok');
    }
    setBadge('operations', dirty() ? '•' : '', 'warn', 'Modifications non enregistrées');
    setBadge('diffusion', st.config?.bootstrap || st.config?.warnings?.length ? '!' : '', st.config?.bootstrap ? 'info' : 'warn',
      st.config?.bootstrap ? 'Configuration fournie à examiner' : 'Avertissement de configuration');
    // Un retour arrière possible n'est pas une alerte : seuls une erreur et une installation en attente sont signalés.
    setBadge('mises-a-jour', st.update?.error ? '!' : st.update?.canInstall ? '•' : '', st.update?.error ? 'alert' : 'warn', 'Mise à jour à voir');
    paintChips();
  }
  let chipsKey = null;
  function paintChips() {
    const version = st.update?.currentVersion;
    if (`${version}` === chipsKey) return; // Sinon les puces rejoueraient leur entrée à chaque changement.
    chipsKey = `${version}`;
    const slot = main.querySelector('[data-slot="chips"]');
    slot.innerHTML = [
      `<span class="chip ${demo ? 'info' : 'ok'}" data-j="0">${glyph(demo ? 'sparkles' : 'monitor')}${demo ? 'Démonstration' : 'Ce poste'}</span>`,
      version ? `<span class="chip" data-j="1">${glyph('logo')}FILPRO ${esc(version)}</span>` : '',
      `<span class="chip" data-j="2">${glyph('lock')}Aucune recherche sur Internet</span>`,
    ].join('');
    paint(slot);
  }
  // Avertissements de l'hôte pour cette session (lecture seule, autre compte…), sous l'en-tête.
  const warnings = ctx.session?.capabilityWarnings ?? [];
  if (warnings.length) main.querySelector('[data-slot="notices"]').innerHTML = `<div class="callout warn pr-session-notice"><div><strong>Disponibilité de cette session</strong><ul>${warnings.map((warning) => `<li>${esc(warning)}</li>`).join('')}</ul></div></div>`;

  // ----- Rail des sections -----

  function movePill(item) {
    if (!item) return;
    pill.style.setProperty('--y', `${item.offsetTop}px`);
    pill.style.setProperty('--h', `${item.offsetHeight}px`);
    pill.classList.add('on');
  }
  function markNav(id) {
    nav.querySelectorAll('.pr-nav-item').forEach((b) => {
      const on = b.dataset.section === id;
      b.setAttribute('aria-current', on ? 'page' : 'false');
      if (on) movePill(b);
    });
  }
  function writeAddress(id) { history.replaceState(null, '', `#/parametres?section=${encodeURIComponent(id)}`); }

  async function show(id, { focus = false, setting = null, first = false } = {}) {
    const section = SECTIONS.find((s) => s.id === id) ?? SECTIONS[0];
    // Même section (double clic, recherche) : rien à remonter, et la section en cours d'affichage n'est pas annulée.
    if (st.section === section.id && !first) { if (setting) highlight(setting); return; }
    const my = ++st.token;
    st.section = section.id;
    markNav(section.id);
    writeAddress(section.id);
    const leaving = host.firstElementChild;
    if (leaving && !motionReduced()) {
      leaving.classList.add('pr-leave');
      await new Promise((r) => setTimeout(r, 140));
    }
    if (my !== st.token || !st.alive) return;
    try { st.mounted?.destroy?.(); } catch (err) { console.error(err); }
    st.mounted = null;
    feedback('');
    const el = document.createElement('div');
    el.className = 'pr-section pr-enter';
    el.dataset.section = section.id;
    host.replaceChildren(el);
    st.mounted = section.mod.mount(el, { ...sctx, section }) ?? null;
    paint(el);
    // Changement de section depuis le bas de l'écran : la nouvelle section commence à son titre, pas au milieu.
    const above = main.querySelector('.pr-layout').getBoundingClientRect().top - main.getBoundingClientRect().top;
    if (!first && !setting && above < 0) main.scrollBy({ top: above - 16, behavior: motionReduced() ? 'auto' : 'smooth' });
    if (setting) setTimeout(() => highlight(setting), 420);
    if (focus) content.focus({ preventScroll: true });
    announce(`Section ${section.label}`);
  }
  function highlight(setting) {
    const target = host.querySelector(`[data-setting="${CSS.escape(setting)}"]`);
    if (!target) return;
    target.scrollIntoView({ block: 'center', behavior: motionReduced() ? 'auto' : 'smooth' });
    target.classList.remove('pr-flash');
    void target.offsetWidth;
    target.classList.add('pr-flash');
    const control = target.querySelector('[role="radio"][aria-checked="true"]:not([disabled])')
      ?? [...target.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled])')].find((el) => el.getClientRects().length > 0);
    control?.focus({ preventScroll: true });
  }

  // ----- Recherche d'un réglage -----

  const search = main.querySelector('#pr-search');
  const list = main.querySelector('#pr-search-list');
  let hits = [];
  let active = -1;
  function renderHits() {
    const q = fold(search.value.trim());
    hits = q ? INDEX.filter((x) => (demo || x.setting !== 'scenario') && q.split(/\s+/).every((w) => x.words.includes(w))).slice(0, 8) : []; // Les scénarios n'existent qu'en démonstration.
    active = hits.length ? 0 : -1;
    list.innerHTML = hits.length
      ? hits.map((h, n) => {
        const s = SECTIONS.find((x) => x.id === h.section);
        return `<li role="option" id="pr-hit-${n}" aria-selected="${n === active}" data-hit="${n}">${glyph(s.icon)}<span class="pr-hit-label">${esc(h.label)}</span><span class="pr-hit-sec">${esc(s.label)}</span></li>`;
      }).join('')
      : (q ? '<li class="pr-hit-none" role="option" aria-disabled="true">Aucun réglage ne correspond.</li>' : '');
    list.hidden = !q;
    search.setAttribute('aria-expanded', String(!!q));
    if (active >= 0) search.setAttribute('aria-activedescendant', `pr-hit-${active}`); else search.removeAttribute('aria-activedescendant');
  }
  function choose(n) {
    const h = hits[n];
    if (!h) return;
    search.value = '';
    renderHits();
    show(h.section, { setting: h.setting });
  }
  search.addEventListener('input', renderHits);
  search.addEventListener('focus', () => { if (search.value.trim() && list.hidden) renderHits(); });
  search.addEventListener('keydown', (e) => {
    // Liste fermée (après une sortie du champ) : les flèches et Entrée la rouvrent sans rien choisir.
    if (['ArrowDown', 'ArrowUp', 'Enter'].includes(e.key) && list.hidden) {
      if (search.value.trim()) { e.preventDefault(); renderHits(); }
      return;
    }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (!hits.length) return;
      e.preventDefault();
      active = (active + (e.key === 'ArrowDown' ? 1 : -1) + hits.length) % hits.length;
      list.querySelectorAll('[data-hit]').forEach((li) => li.setAttribute('aria-selected', String(Number(li.dataset.hit) === active)));
      search.setAttribute('aria-activedescendant', `pr-hit-${active}`);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      choose(active);
    } else if (e.key === 'Escape' && search.value) {
      e.stopPropagation();
      search.value = '';
      renderHits();
    }
  });
  list.addEventListener('pointerdown', (e) => {
    const li = e.target instanceof Element ? e.target.closest('[data-hit]') : null;
    if (li) { e.preventDefault(); choose(Number(li.dataset.hit)); }
  });
  search.addEventListener('blur', () => setTimeout(() => { if (st.alive && !search.matches(':focus')) { list.hidden = true; search.setAttribute('aria-expanded', 'false'); search.removeAttribute('aria-activedescendant'); } }, 120));
  // « / » place le curseur dans la recherche des réglages (hors champs de saisie).
  const onKey = (e) => {
    if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
    const t = e.target;
    if (t instanceof HTMLElement && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
    if (t instanceof Element && t.closest('[role="dialog"], dialog')) return; // Une fenêtre ouverte garde le clavier.
    e.preventDefault();
    search.focus();
  };
  document.addEventListener('keydown', onKey);

  // ----- Événements -----

  const onClick = (e) => {
    const t = e.target instanceof Element ? e.target : null;
    const item = t?.closest('.pr-nav-item');
    if (item) { show(item.dataset.section); return; }
    const tile = t?.closest('[data-go-section]');
    if (tile && main.contains(tile)) { show(tile.dataset.goSection); return; }
    if (t?.closest('[data-refresh]')) refresh();
  };
  // Relit tout l'écran ; des réglages de sauvegarde non enregistrés ne sont abandonnés qu'après accord.
  async function refresh() {
    if (st.busy) return;
    if (dirty() && !await confirmDialog({ title: 'Actualiser les réglages', message: 'Les changements non enregistrés seront abandonnés.', confirmLabel: 'Actualiser' })) return;
    if (st.alive) load();
  }
  const onNavKey = (e) => {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(e.key)) return;
    const items = [...nav.querySelectorAll('.pr-nav-item')];
    const at = items.indexOf(document.activeElement);
    if (at < 0) return;
    e.preventDefault();
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? items.length - 1 : (at + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    items[next].focus();
  };
  main.addEventListener('click', onClick);
  nav.addEventListener('keydown', onNavKey);
  const onPresentation = () => { paintTiles(); st.mounted?.presentation?.(); };
  window.addEventListener('filpro:presentation', onPresentation);
  const ro = 'ResizeObserver' in window ? new ResizeObserver(() => { markNav(st.section); syncSegments(main); }) : null;
  ro?.observe(nav);

  // ----- Lecture -----

  async function load() {
    if (st.busy) return; st.busy = true; feedback(''); emit();
    const results = await Promise.allSettled([call('configuration.read', {}), call('updates.read', {}), demo ? call('demonstration.catalog', {}) : Promise.resolve(null),
      call('sauvegarder.readCommonSelection', {}), call('updates.source', {})]);
    if (!st.alive) return;
    const [config, update, catalog, common, source] = results;
    st.errors = {};
    if (config.status === 'fulfilled') { st.config = config.value; st.draft = null; } else { st.errors.config = errorText(config.reason); feedback(st.errors.config, true); }
    if (update.status === 'fulfilled') st.update = update.value; else st.errors.update = errorText(update.reason);
    if (demo) { if (catalog.status === 'fulfilled') st.catalog = catalog.value; else feedback(errorText(catalog.reason), true); }
    if (common.status === 'fulfilled') st.common = common.value; else st.errors.common = errorText(common.reason);
    if (source.status === 'fulfilled') st.source = source.value; else feedback(errorText(source.reason), true);
    st.offer = null;
    st.sourceDraft = null; // Après une relecture, la source mémorisée (ou fictive) remplace la saisie, quelle que soit la section ouverte.
    st.busy = false;
    emit();
    st.mounted?.loaded?.();
  }
  // Progression de la préparation, de l'installation ou du retour arrière : partagée par l'en-tête, le rail et la section.
  const unlisten = onEvent('updates.changed', (value) => { if (st.alive && value) { st.update = value; emit(); } });
  const leave = ctx.guardLeave?.(() => !dirty() || confirmDialog({ title: 'Réglages non enregistrés', message: 'Quitter cette page abandonne les changements non enregistrés. Les réglages déjà sauvegardés restent conservés.', confirmLabel: 'Quitter sans enregistrer' }));

  // ----- Démarrage -----

  paintTiles();
  show(SECTIONS.some((s) => s.id === params.section) ? params.section : 'apparence', { first: true });
  load();

  return () => {
    st.alive = false;
    st.token++;
    listeners.clear();
    stopReveal();
    leave?.();
    unlisten?.();
    ro?.disconnect();
    document.removeEventListener('keydown', onKey);
    window.removeEventListener('filpro:presentation', onPresentation);
    main.removeEventListener('click', onClick);
    nav.removeEventListener('keydown', onNavKey);
    try { st.mounted?.destroy?.(); } catch (err) { console.error(err); }
  };
}
