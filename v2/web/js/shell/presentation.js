// Adaptations ciblées du vrai shell DOMENIA V2, M4D135/domenia-d120 à 9b1c0333c34020ad88c1448874b91eff884d5406 :
// app.js (slideColumn, paintToggle), shell/environment.js (heightPlan), shell/tips.js (placement et délais).
// Voir docs/reprise/GUI-DOMENIA-20261025.md. Aucun accès au pont, aux profils, au disque ou au réseau.
import { cssMs, motionReduced } from '../ui.js';

/** Seuils DOMENIA ; les adaptations à la fenêtre ne remplacent jamais une préférence enregistrée. */
export function heightPlan(height, width) {
  const short = height > 0 && height < 940;
  const low = height > 0 && height < 700;
  const tiny = height > 0 && height < 560;
  const narrow = width > 0 && width < 1280;
  return { short, low, tiny, narrow, foldNavigation: narrow || (short && width > 0 && width < 1200) };
}

export function watchWindowsPresentation({ onThemeChange, onMotionChange }) {
  const dark = matchMedia('(prefers-color-scheme: dark)');
  const contrast = matchMedia('(forced-colors: active), (prefers-contrast: more)');
  const forced = matchMedia('(forced-colors: active)');
  const motion = matchMedia('(prefers-reduced-motion: reduce)');
  const followTheme = () => {
    document.documentElement.toggleAttribute('data-forced', forced.matches);
    onThemeChange();
  };
  [dark, contrast, forced].forEach((query) => query.addEventListener('change', followTheme));
  motion.addEventListener('change', onMotionChange);
  followTheme();
  return () => {
    [dark, contrast, forced].forEach((query) => query.removeEventListener('change', followTheme));
    motion.removeEventListener('change', onMotionChange);
  };
}

export function installNavigation({ shell, nav, toggle, readCollapsed, saveCollapsed, onLayout }) {
  const moving = new Map();
  let autoFold = false;
  let autoReleased = false;
  let knownPreference = Boolean(readCollapsed());
  let frame = 0;

  const finishSlides = () => {
    moving.forEach((animation) => animation.cancel());
    moving.clear();
  };
  const slideColumn = (change) => {
    const movers = [...shell.children].filter((el) => el !== nav && !el.hidden);
    // Une mesure avant/après le changement de grille ; seules les transformations sont animées.
    // Le rectangle courant permet aussi de repartir de la position visible après des clics rapides.
    const before = movers.map((el) => el.getBoundingClientRect().left);
    finishSlides();
    change();
    if (motionReduced() || typeof Element.prototype.animate !== 'function') return;
    const duration = cssMs('--t-slow', 520);
    movers.forEach((el, n) => {
      const dx = before[n] - el.getBoundingClientRect().left;
      if (Math.abs(dx) < 1) return;
      const animation = el.animate([{ transform: `translateX(${dx}px)` }, { transform: 'translateX(0)' }], {
        duration, easing: 'cubic-bezier(.16, 1, .3, 1)',
      });
      moving.set(el, animation);
      const done = () => { if (moving.get(el) === animation) moving.delete(el); };
      animation.finished.then(done, done);
    });
  };
  const collapsed = () => shell.classList.contains('nav-collapsed');
  // Le pied (icône de repli) ne prend un fond que lorsque des entrées défilent réellement dessous.
  const shadeFoot = () => nav.classList.toggle('nav-scrolls', nav.scrollHeight > nav.clientHeight + 1);
  const paintToggle = () => {
    // L'adaptation FILPRO permet aussi le dépliage dans une fenêtre étroite.
    const folded = collapsed();
    const text = folded ? 'Déplier le menu' : 'Replier le menu';
    toggle.setAttribute('aria-expanded', String(!folded));
    toggle.setAttribute('aria-label', text);
    toggle.querySelector('.nav-label').textContent = text;
    toggle.dataset.tip = `${text} (Ctrl B)`;
    shadeFoot();
    onLayout();
  };
  const setCollapsed = (on, animate = false) => {
    const change = () => shell.classList.toggle('nav-collapsed', on);
    if (on !== collapsed() && animate) slideColumn(change); else change();
    paintToggle();
  };
  const applyLayout = () => {
    frame = 0;
    finishSlides();
    const plan = heightPlan(innerHeight, innerWidth);
    for (const key of ['short', 'low', 'tiny', 'narrow']) {
      document.documentElement.toggleAttribute(`data-${key}`, plan[key]);
    }
    if (plan.foldNavigation && !readCollapsed() && !autoReleased) {
      autoFold = true;
      setCollapsed(true);
    } else if (!plan.foldNavigation && autoFold) {
      autoFold = false;
      setCollapsed(readCollapsed());
    } else paintToggle();
  };
  const onResize = () => { if (!frame) frame = requestAnimationFrame(applyLayout); };
  const toggleNav = () => {
    const next = !collapsed();
    autoFold = false;
    autoReleased = true;
    knownPreference = next;
    saveCollapsed(next);
    setCollapsed(next, true);
  };
  toggle.addEventListener('click', toggleNav);
  window.addEventListener('resize', onResize);
  const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(() => { shadeFoot(); onLayout(); }) : null;
  observer?.observe(nav);
  setCollapsed(readCollapsed());
  applyLayout();
  return {
    toggle: toggleNav,
    applyPreference: () => {
      const next = Boolean(readCollapsed());
      // Une notification de configuration inchangée ne doit pas annuler le repli automatique lié à la fenêtre.
      if (next === knownPreference) { applyLayout(); return; }
      knownPreference = next;
      autoFold = false;
      autoReleased = true;
      setCollapsed(next, true);
    },
    settleMotion: () => { if (motionReduced()) finishSlides(); },
    stop() {
      finishSlides();
      cancelAnimationFrame(frame);
      observer?.disconnect();
      toggle.removeEventListener('click', toggleNav);
      window.removeEventListener('resize', onResize);
    },
  };
}

/** Partie data-tip du vrai shell DOMENIA, sans son catalogue d'aide ni ses dépendances métier. */
export function installTips(nav) {
  const root = document.documentElement;
  const tip = document.createElement('div');
  tip.className = 'flytip filpro-flytip';
  // Les commandes gardent leur propre nom accessible ; ne pas le faire annoncer une deuxième fois.
  tip.setAttribute('aria-hidden', 'true');
  document.body.appendChild(tip);
  root.classList.add('js-tips');
  let owner = null;
  let timer = 0;
  let lifeTimer = 0;

  const hide = () => {
    clearTimeout(timer);
    clearTimeout(lifeTimer);
    owner = null;
    tip.classList.remove('on');
  };
  const wanted = (el) => !(el.closest('.nav-item') && nav.getBoundingClientRect().width > 120);
  const place = (el) => {
    const r = el.getBoundingClientRect();
    const w = tip.offsetWidth;
    const h = tip.offsetHeight;
    let x;
    let y;
    if (el.closest('.nav')) { x = r.right + 10; y = r.top + r.height / 2 - h / 2; }
    else if (r.top < 140) { x = r.left + r.width / 2 - w / 2; y = r.bottom + 8; }
    else { x = r.left + r.width / 2 - w / 2; y = r.top - h - 8; }
    tip.style.setProperty('--tx', `${Math.round(Math.max(8, Math.min(innerWidth - w - 8, x)))}px`);
    tip.style.setProperty('--ty', `${Math.round(Math.max(8, Math.min(innerHeight - h - 8, y)))}px`);
  };
  const show = (el) => {
    if (!el.isConnected || !wanted(el)) { hide(); return; }
    const box = el.getBoundingClientRect();
    if (!box.width && !box.height) { hide(); return; }
    const disabled = el.getAttribute('aria-disabled') === 'true' || el.disabled === true;
    const text = el.dataset.tip ?? '';
    const reason = disabled ? (el.dataset.reason ?? '') : '';
    if (!text && !reason) { hide(); return; }
    const wasOn = tip.classList.contains('on');
    owner = el;
    const label = document.createElement('span');
    label.className = 'flytip-text';
    label.textContent = text || reason;
    tip.replaceChildren(label);
    if (text && reason && text !== reason) {
      const detail = document.createElement('span');
      detail.className = 'flytip-reason';
      detail.textContent = reason;
      tip.appendChild(detail);
    }
    tip.classList.toggle('wide', text.length > 80 || !!reason);
    if (!wasOn) tip.classList.add('jump');
    place(el);
    if (!wasOn) { void tip.offsetWidth; tip.classList.remove('jump'); }
    tip.classList.add('on');
    clearTimeout(lifeTimer);
    lifeTimer = setTimeout(hide, 20_000);
  };
  const target = (e) => e.target instanceof Element ? e.target.closest('[data-tip], [data-reason]') : null;
  const pointer = (e) => {
    if (e.pointerType === 'touch') return;
    const el = target(e);
    if (el === owner) return;
    clearTimeout(timer);
    if (!el) { hide(); return; }
    timer = setTimeout(() => show(el), tip.classList.contains('on') ? 100 : 350);
  };
  const focus = (e) => {
    clearTimeout(timer);
    const el = target(e);
    if (el?.matches(':focus-visible')) show(el); else hide();
  };
  const escape = (e) => { if (e.key === 'Escape') hide(); };
  document.addEventListener('pointerover', pointer);
  document.addEventListener('focusin', focus);
  document.addEventListener('keydown', escape);
  const dismiss = ['pointerdown', 'focusout', 'wheel', 'scroll'];
  dismiss.forEach((name) => document.addEventListener(name, hide, { passive: true, capture: true }));
  const windowDismiss = ['blur', 'resize', 'hashchange'];
  windowDismiss.forEach((name) => window.addEventListener(name, hide));
  root.addEventListener('pointerleave', hide);
  return () => {
    hide();
    tip.remove();
    root.classList.remove('js-tips');
    document.removeEventListener('pointerover', pointer);
    document.removeEventListener('focusin', focus);
    document.removeEventListener('keydown', escape);
    dismiss.forEach((name) => document.removeEventListener(name, hide, true));
    windowDismiss.forEach((name) => window.removeEventListener(name, hide));
    root.removeEventListener('pointerleave', hide);
  };
}
