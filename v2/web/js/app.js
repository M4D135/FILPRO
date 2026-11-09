// Coque FILPRO 2 : navigation, barre supérieure, bandeau de démonstration, thème, notifications et routeur.
// Réutilise le kit DOMENIA (ui.js, kit/, CSS) sans le réécrire. Les écrans sont dans js/pages/.
import { call, onEvent } from './bridge.js';
import { icon } from './icons.js';
import {
  esc, initLive, initPointerEffects, motionReduced, toast, announce,
} from './ui.js';
import { createRouter } from './router.js';
import { menuOpen, openMenu, setLayoutStore, confirmDialog } from './kit/index.js';
import { sessionInfo, acceptSession, invalidateSession } from './session-state.js';
import { decideBootstrap, configurationChanged } from './configuration-ui.js';
import { startOperationEvents, refreshOperations, observeOperations, isTerminal, jobState } from './operations.js';
import { bindConfirmationBanner, bindPresenceBanner } from './native-confirmation.js';
import { isTextEntry, modalOpen } from './kit/util.js';
import {
  PAGES, THEMES, MOTIONS, loadPrefs, savePrefs, resolvedTheme, navMarkup, topbarMarkup,
} from './shell/navigation.js';
import { installNavigation, installTips, watchWindowsPresentation } from './shell/presentation.js';

const root = document.documentElement;
const prefs = { theme: 'system', motion: 'system', navCollapsed: false };
let navigation = null;
let themeTransition = null;
let configuration = null;
let preferenceQueue = Promise.resolve();

function persistPrefs() {
  // Une démo ne réécrit jamais les préférences du contexte normal, même si l'utilisateur essaie tous les thèmes.
  if (!configuration) return;
  const presentation = { theme: prefs.theme, motion: prefs.motion, navigationCollapsed: prefs.navCollapsed };
  preferenceQueue = preferenceQueue.then(async () => {
    const current = await call('configuration.read', {});
    const saved = await call('configuration.save', { settings: { ...current.settings, ...presentation }, expectedRevision: current.revision });
    configuration = saved.state;
    if (sessionInfo().demonstration === false) savePrefs({ theme: saved.state.settings.theme, motion: saved.state.settings.motion, navCollapsed: saved.state.settings.navigationCollapsed });
  }).catch(async (err) => {
    try { applyConfiguration(await call('configuration.read', {})); } catch { if (configuration) applyConfiguration(configuration); }
    toast(err?.message ?? 'Préférence non enregistrée.', { tone: 'warn' });
  });
}

// ---------- Thème et motion ----------

function applyTheme() {
  root.dataset.theme = resolvedTheme(prefs.theme);
  root.dataset.themePref = prefs.theme;
  document.querySelectorAll('.theme-switch button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.set === prefs.theme)));
}

function applyMotion() {
  root.dataset.motion = prefs.motion;
  if (motionReduced()) themeTransition?.skipTransition();
  navigation?.settleMotion();
}

function setTheme(id) {
  if (!THEMES.some((t) => t.id === id) || prefs.theme === id) return;
  prefs.theme = id;
  persistPrefs();
  themeTransition?.skipTransition();
  if (document.startViewTransition && !motionReduced()) {
    root.classList.add('theme-swap');
    const vt = document.startViewTransition(applyTheme);
    themeTransition = vt;
    const finish = () => {
      if (themeTransition !== vt) return;
      themeTransition = null;
      root.classList.remove('theme-swap');
    };
    vt.finished.then(finish, finish);
  } else {
    applyTheme();
  }
  announce(THEMES.find((t) => t.id === id)?.label ?? 'Thème');
}

// ---------- Coque ----------

function shellMarkup() {
  return `<a class="skip-link" href="#main">Aller au contenu</a><div class="shell filpro-shell">
    ${navMarkup()}
    ${topbarMarkup()}
    <div class="card sim-banner" role="status" hidden></div>
    <div class="host-banners">
      <div class="card sim-banner" data-confirmation-banner data-tone="warn" role="status" hidden></div>
      <div class="card sim-banner" data-presence-banner data-tone="warn" role="status" hidden></div>
    </div>
    <div class="operation-strip" hidden><span aria-hidden="true">${icon('activity')}</span><div><strong data-operation-title></strong><small data-operation-action></small></div><progress class="operation-progress" max="100" aria-label="Opération courante"></progress><button class="btn sm" type="button" data-open-operation>Voir le suivi</button></div>
    <main class="main" id="main" tabindex="-1"></main>
  </div>`;
}

function mountShell() {
  const app = document.getElementById('app');
  app.classList.remove('boot');
  app.innerHTML = shellMarkup();
  // Ligne de progression sous la barre supérieure pendant une requête.
  const top = app.querySelector('.topbar');
  top.insertAdjacentHTML('beforeend', '<span class="busy-line" aria-hidden="true"></span>');
  // Halo qui suit la souris et région des toasts (une seule fois).
  if (!document.querySelector('.cursor-halo')) {
    document.body.insertAdjacentHTML('beforeend', '<div class="cursor-halo" aria-hidden="true"></div><div class="toasts" aria-live="polite"></div>');
  }
}

// ---------- Repli de stockage des dispositions (préférences locales) ----------

function installLayoutStore() {
  let cache = {};
  if (sessionInfo().demonstration === false) {
    try { cache = JSON.parse(localStorage.getItem('filpro.layout.v1') ?? '{}') ?? {}; } catch { cache = {}; }
  }
  setLayoutStore({
    read: (key) => cache[key],
    write: (key, value) => {
      cache[key] = value;
      if (sessionInfo().demonstration === false) {
        try { localStorage.setItem('filpro.layout.v1', JSON.stringify(cache)); } catch { /* session seulement */ }
      }
    },
  });
}

// ---------- Mode et bandeau de démonstration ----------

async function loadSession() {
  const chip = document.querySelector('.mode-chip');
  const banner = document.querySelector('.sim-banner');
  try {
    const info = await call('session.info', {});
    acceptSession(info);
    if (chip) {
      chip.hidden = info?.demonstration !== true;
      chip.querySelector('.mode-label').textContent = 'Démonstration';
      chip.dataset.mode = 'preview';
    }
    if (banner) {
      if (info?.demonstration) {
        banner.hidden = false;
        banner.innerHTML = `${icon('info')}<span><strong>Démonstration — données fictives.</strong> Les actions restent en mémoire, sans accès aux profils ni sauvegardes réels.</span>`;
      } else {
        banner.hidden = true;
      }
    }
    document.documentElement.dataset.demonstration = String(info.demonstration);
  } catch (err) {
    console.error(err);
    if (chip) {
      chip.hidden = false;
      chip.querySelector('.mode-label').textContent = 'Indisponible';
      chip.dataset.mode = 'preview';
    }
  }
}

async function switchDemonstration(operation) {
  const messages = {
    'session.demoEnter': ['Ouvrir la démonstration', 'FILPRO ouvre un monde fictif isolé. Les opérations actives doivent être terminées et les saisies non enregistrées de cet écran seront abandonnées. Vos réglages et données réels sont conservés.'],
    'session.demoExit': ['Quitter la démonstration', 'Revenir au contexte normal. Les données fictives ne sont pas transférées vers vos profils ou sauvegardes.'],
    'session.demoReset': ['Réinitialiser la démonstration', 'Recréer uniquement les données fictives de cette session. Les changements effectués dans la démonstration seront perdus.'],
  };
  const [title, message] = messages[operation];
  if (!await confirmDialog({ title, message, confirmLabel: title, icon: 'sparkles' })) return;
  try {
    const result = await call(operation, { confirmed: true });
    if (!result?.restarting) throw new Error('Le changement de contexte n’a pas été confirmé par l’hôte.');
    invalidateSession();
    toast('Le contexte est recréé…', { tone: 'info' });
    // L'hôte natif recrée ses fournisseurs et sa page. Une prévisualisation n'a que sa mémoire JavaScript à recréer.
    if (!window.chrome?.webview) location.reload();
  } catch (err) {
    toast(err?.message ?? 'Le changement de contexte a été refusé.', { tone: 'alert' });
  }
}

// ---------- Routeur ----------

let router = null;

function context(page) {
  return {
    page,
    toast,
    session: sessionInfo(),
    settings: configuration?.settings ?? {},
    switchDemonstration,
    go: (id, params) => router?.go(id, params),
    openPalette: () => openMenu(document.querySelector('.nav-item[data-id="parametres"]'), PAGES.map((entry) => ({ label: entry.label, run: () => router?.go(entry.id) })), { label: 'Aller à un écran' }),
  };
}

function applyConfiguration(state) {
  if (!state?.settings || state.demonstration !== sessionInfo().demonstration) return;
  configuration = state;
  const value = state.settings;
  if (THEMES.some((theme) => theme.id === value.theme)) prefs.theme = value.theme;
  if (MOTIONS.some((motion) => motion.id === value.motion)) prefs.motion = value.motion;
  prefs.navCollapsed = value.navigationCollapsed === true;
  if (sessionInfo().demonstration === false) savePrefs(prefs);
  applyTheme(); applyMotion();
  // Le repli visible et le réglage distribué restent cohérents sans forcer un nouveau mouvement à chaque notification.
  const shell = document.querySelector('.shell');
  if (navigation && shell.classList.contains('nav-collapsed') !== prefs.navCollapsed) navigation.applyPreference();
}

function installOperations() {
  startOperationEvents();
  let displayed = null;
  const strip = document.querySelector('.operation-strip');
  strip.querySelector('[data-open-operation]').addEventListener('click', () => { if (displayed) router.go('suivi', { operation: displayed }); });
  observeOperations((jobs, changed, previous) => {
    const current = jobs.find((job) => !isTerminal(job));
    displayed = current?.id ?? null;
    strip.hidden = !current;
    if (current) {
      strip.querySelector('[data-operation-title]').textContent = current.title;
      strip.querySelector('[data-operation-action]').textContent = current.cancellationRequested ? 'Arrêt demandé — nettoyage en cours' : current.currentAction;
      const progress = strip.querySelector('progress');
      if (Number.isFinite(current.progressPercent)) progress.value = current.progressPercent; else progress.removeAttribute('value');
    }
    if (changed && previous && !isTerminal(previous) && isTerminal(changed) && configuration?.settings?.notifyOnCompletion !== false) {
      toast(`${changed.demonstration ? 'Démonstration · ' : ''}${changed.title} : ${jobState(changed).label.toLowerCase()}.`, { tone: jobState(changed).tone, life: 8000 });
      announce(`${changed.title} : ${jobState(changed).label}`, { assertive: changed.state === 'failed' });
    }
  });
  refreshOperations().catch((err) => console.warn('Suivi indisponible', err?.code));
  onEvent('session.closeRequested', async () => {
    if (!await confirmDialog({ title: 'Fermer FILPRO pendant une opération', message: 'FILPRO demande l’arrêt puis attend la fin du nettoyage avant de fermer. Attendez le bilan si vous souhaitez conserver l’opération en cours.', confirmLabel: 'Demander l’arrêt et fermer', icon: 'pause' })) return;
    toast('Arrêt demandé. FILPRO attend la fermeture des ressources avant de quitter.', { tone: 'info', life: 12000 });
    try { await call('session.confirmClose', { confirmed: true }); }
    catch (err) { toast(err?.message ?? 'La fermeture n’a pas pu être confirmée.', { tone: 'alert' }); }
  });
}

function notifyReady() {
  const generation = sessionInfo().generation;
  if (!generation) return;
  let sent = false;
  const observer = new MutationObserver(() => {
    const main = document.getElementById('main');
    if (sent || !document.documentElement.dataset.ready || !main?.firstElementChild || main.querySelector('.error-card') || sessionInfo().generation !== generation) return;
    sent = true; observer.disconnect();
    call('session.ready', { generation }).catch((err) => console.warn('Accusé de démarrage refusé', err?.code));
  });
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-ready'] });
}

function wireNav() {
  const shell = document.querySelector('.shell');
  const nav = document.querySelector('.nav');
  const toggle = document.querySelector('.nav-toggle');
  navigation = installNavigation({
    shell, nav, toggle,
    readCollapsed: () => prefs.navCollapsed,
    saveCollapsed: (collapsed) => { prefs.navCollapsed = collapsed; persistPrefs(); },
    onLayout: () => router?.updatePill(),
  });
  installTips(nav);

  document.querySelectorAll('.theme-switch button').forEach((b) => b.addEventListener('click', () => setTheme(b.dataset.set)));
  document.querySelector('.skip-link')?.addEventListener('click', (e) => {
    e.preventDefault();
    document.getElementById('main')?.focus({ preventScroll: true });
  });
  watchWindowsPresentation({
    onThemeChange: () => { if (prefs.theme === 'system') applyTheme(); },
    onMotionChange: () => { if (prefs.motion === 'system') applyMotion(); },
  });
}

function wireShortcuts() {
  document.addEventListener('keydown', (e) => {
    if (e.defaultPrevented || e.isComposing || modalOpen() || menuOpen() || isTextEntry(e.target)) return;
    if (e.ctrlKey && !e.altKey && !e.metaKey && (e.key === 'b' || e.key === 'B')) {
      e.preventDefault();
      if (!e.repeat) navigation?.toggle();
      return;
    }
    if (e.altKey && !e.ctrlKey && !e.metaKey && /^[1-9]$/.test(e.key)) {
      const page = PAGES[Number(e.key) - 1];
      if (page) {
        e.preventDefault();
        router?.go(page.id);
      }
    }
  });
}

// ---------- Démarrage ----------

async function boot() {
  mountShell();
  await loadSession();
  if (sessionInfo().demonstration === false) Object.assign(prefs, loadPrefs());
  try { applyConfiguration(await call('configuration.read', {})); } catch (err) { console.warn('Réglages indisponibles', err?.code); }
  installLayoutStore();
  applyTheme();
  applyMotion();
  initLive();
  initPointerEffects();
  for (const warning of new Set(configuration?.warnings || [])) toast(warning, { tone: 'warn' });
  // Les bannières de la coque restent les toasts du kit (basicToast) pour ce lot : aucun setToastImpl personnalisé.

  const main = document.getElementById('main');
  const nav = document.querySelector('.nav');
  router = createRouter({
    main,
    nav,
    pages: PAGES,
    context,
    defaultId: 'accueil',
    isBusy: () => document.querySelector('.busy-line')?.classList.contains('on') ?? false,
  });

  wireNav();
  wireShortcuts();
  window.addEventListener('filpro:configuration', (event) => applyConfiguration(event.detail));
  installOperations();
  // Même rangée que le bandeau de démonstration : la fenêtre Windows n'existe qu'en Poste, jamais en démonstration.
  bindConfirmationBanner(document.querySelector('[data-confirmation-banner]'), onEvent);
  // Autre compte : FILPRO verrouillé.
  bindPresenceBanner(document.querySelector('[data-presence-banner]'), onEvent, sessionInfo());
  notifyReady();
  router.start();
  if (configuration?.bootstrap) {
    try { const result = await decideBootstrap(configuration.bootstrap); if (result) { applyConfiguration(result.state); configurationChanged(result.state); } }
    catch (err) { toast(err?.message ?? 'La configuration fournie n’a pas été appliquée.', { tone: 'warn' }); }
  }
}

boot();
