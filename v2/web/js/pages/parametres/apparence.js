// Paramètres › Apparence : thème avec aperçus, animations avec démonstration, menu replié et raccourcis. Ce sont les
// préférences de la barre supérieure : la coque les applique et les enregistre tout de suite (ctx.presentation), comme dans
// Domenia. Aucune n'attend « Enregistrer les réglages ».
import { esc, glyph, card, row, segmented, toggle, notes, sectionHead, paint, syncSegments, radioKeys, rovingTabs, pressRadio, NBSP } from './common.js';
import { reveal } from '../../ui.js';

const THEMES = [
  { id: 'light', label: 'Clair', sub: 'Fond lumineux, contrastes doux' },
  { id: 'dark', label: 'Sombre', sub: 'Fond profond, reposant le soir' },
  { id: 'contrast', label: 'Contraste élevé', sub: 'Cadres nets, couleurs franches' },
  { id: 'system', label: 'Suivre Windows', sub: 'Clair, sombre ou contraste selon Windows' },
];
const MOTIONS = [
  { value: 'full', label: 'Riches', icon: 'sparkles', tip: 'Fond vivant, entrées en cascade, graphiques qui se tracent' },
  { value: 'reduced', label: 'Réduites', icon: 'pause', tip: 'Chaque écran s’affiche directement dans son état final' },
  { value: 'system', label: 'Windows', icon: 'monitor', tip: 'Réduites seulement si Windows le demande' },
];
const KEYS = [
  ['Ctrl B', 'Replier ou déplier le menu'],
  ['Alt 1 à 9', 'Aller à un écran du menu'],
  ['/', 'Rechercher un réglage (cet écran)'],
  ['Échap', 'Fermer une fenêtre de dialogue'],
];

function themeCard(t, current) {
  const on = t.id === current;
  return `<button type="button" class="pr-theme" role="radio" aria-checked="${on}" tabindex="${on ? 0 : -1}" data-theme-choice="${t.id}">
    <span class="pr-theme-art" data-art="${t.id}" aria-hidden="true">
      <span class="pr-mini-nav"><i></i><i></i><i></i></span>
      <span class="pr-mini-main"><span class="pr-mini-hero"></span><span class="pr-mini-row"><i></i><i></i><i></i></span><span class="pr-mini-line"></span></span>
    </span>
    <span class="pr-theme-text"><b>${esc(t.label)}</b><span>${esc(t.sub)}</span></span>
    <span class="pr-theme-check" aria-hidden="true">${glyph('check')}</span>
  </button>`;
}

function markup(p) {
  const theme = card({
    id: 'theme', title: 'Thème', sub: 'Appliqué à toute la fenêtre de FILPRO, sans rien relire', iconName: 'sun', cls: 'pr-wide',
    body: `<div class="pr-themes" role="radiogroup" aria-label="Thème" data-setting="theme">${THEMES.map((t) => themeCard(t, p.theme)).join('')}</div>
      <p class="pr-hint">${glyph('info')}<span>Avec « Suivre Windows », le contraste élevé de Windows bascule de lui-même sur le thème contrasté. Les mêmes choix restent dans la barre supérieure.</span></p>`,
  });
  const motion = card({
    id: 'motion', title: 'Animations', sub: 'Mouvement des écrans et des graphiques', iconName: 'sparkles',
    body: `<div data-setting="motion">${segmented('motion', 'Animations', MOTIONS, p.motion)}</div>
      <div class="pr-motion-demo" aria-hidden="true"><span class="pr-orbit"><i></i><i></i><i></i></span><span class="pr-bars"><i></i><i></i><i></i><i></i><i></i></span><span class="pr-glint"></span></div>
      <p class="pr-motion-state" data-slot="motion-state"></p>`,
  });
  const layout = card({
    id: 'layout', title: 'Menu et raccourcis', sub: 'Disposition de la fenêtre et clavier', iconName: 'panel',
    body: row({ label: 'Menu replié en icônes', desc: `Garde le menu étroit. Sur une fenêtre étroite, il se replie de lui-même le temps qu’il faut, sans changer ce choix. Ctrl${NBSP}B.`, control: toggle('nav', 'Menu replié en icônes', p.navCollapsed), setting: 'nav' })
      + `<ul class="pr-keys">${KEYS.map(([k, v], n) => `<li data-j="${n}"><span class="pr-kbds">${k.split('  ').map((x) => `<kbd>${esc(x)}</kbd>`).join('')}</span><span>${esc(v)}</span></li>`).join('')}</ul>`,
  });
  return `${sectionHead({ title: 'Apparence', lead: 'Comme vous préférez travailler : couleurs, mouvement et menu. Appliqué tout de suite, mémorisé sur ce poste.', icon: 'sparkles' })}
    <div class="pr-grid">${theme}${motion}${layout}
      <div class="pr-footnote reveal">${notes([
        'Thème, animations et menu sont enregistrés dans les réglages de FILPRO dès qu’ils changent, comme depuis la barre supérieure.',
        'En démonstration, ces choix restent dans la démonstration : ils ne modifient pas les réglages réels.',
      ])}</div>
    </div>`;
}

export function mount(host, sctx) {
  const presentation = sctx.ctx.presentation;
  const read = () => presentation?.get?.() ?? { theme: 'system', motion: 'system', navCollapsed: false };
  let p = read();

  const motionState = () => {
    const el = host.querySelector('[data-slot="motion-state"]');
    if (!el) return;
    const windowsReduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const m = document.documentElement.dataset.motion;
    const effective = m === 'reduced' || (m === 'system' && windowsReduced) ? 'réduites' : 'riches';
    el.innerHTML = `${glyph(effective === 'riches' ? 'zap' : 'pause')}<span>Animations ${effective} en ce moment · Windows ${windowsReduced ? 'demande' : 'ne demande pas'} des animations réduites.</span>`;
    host.querySelector('.pr-motion-demo')?.classList.toggle('still', effective !== 'riches');
  };

  host.innerHTML = markup(p);
  paint(host);
  const stopReveal = reveal(host);
  requestAnimationFrame(() => syncSegments(host));
  motionState();
  if (!presentation) host.querySelectorAll('[data-theme-choice], .pr-seg button, [data-toggle]').forEach((el) => { el.disabled = true; });

  const sync = () => {
    p = read();
    const themes = host.querySelector('.pr-themes');
    if (themes) pressRadio(themes, p.theme);
    const group = host.querySelector('[data-seg="motion"]');
    if (group && group.querySelector('[aria-checked="true"]')?.dataset.value !== p.motion) pressRadio(group, p.motion);
    const nav = host.querySelector('[data-toggle="nav"]');
    if (nav) nav.checked = p.navCollapsed;
    setTimeout(motionState, 30);
  };

  const onClick = (e) => {
    const t = e.target instanceof Element ? e.target : null;
    const theme = t?.closest('[data-theme-choice]');
    if (theme && !theme.disabled) {
      presentation.setTheme(theme.dataset.themeChoice);
      pressRadio(host.querySelector('.pr-themes'), theme.dataset.themeChoice);
      theme.classList.remove('pr-pop');
      void theme.offsetWidth;
      theme.classList.add('pr-pop');
      return;
    }
    const seg = t?.closest('.pr-seg button[data-value]');
    if (seg && !seg.disabled) {
      presentation.setMotion(seg.dataset.value);
      pressRadio(seg.closest('.pr-seg'), seg.dataset.value);
      setTimeout(motionState, 30);
    }
  };
  const onChange = (e) => {
    const input = e.target instanceof HTMLInputElement ? e.target : null;
    if (input?.dataset.toggle === 'nav' && !input.disabled) presentation.setNavCollapsed(input.checked);
  };
  const onKey = (e) => { radioKeys(e); };
  host.addEventListener('click', onClick);
  host.addEventListener('change', onChange);
  host.addEventListener('keydown', onKey);
  rovingTabs(host.querySelector('.pr-themes'));

  return {
    presentation: sync,
    destroy() {
      stopReveal();
      host.removeEventListener('click', onClick);
      host.removeEventListener('change', onChange);
      host.removeEventListener('keydown', onKey);
    },
  };
}
