// Paramètres › Démonstration : ouvrir, quitter ou réinitialiser le monde fictif, et choisir l'issue de la prochaine opération.
// La démonstration est un fournisseur séparé, sans action système réelle ; ses données restent en mémoire.
import { esc, glyph, card, notes, sectionHead, paint } from './common.js';
import { reveal } from '../../ui.js';

function markup(demo) {
  const switcher = card({ id: 'demo', title: demo ? 'Démonstration ouverte' : 'Essayer FILPRO sans risque', sub: demo ? 'Données fictives, en mémoire' : 'Profils et sauvegardes fictifs', iconName: 'play', toneName: 'accent', cls: 'pr-wide',
    body: `<div data-setting="demo"><p>Explorez FILPRO avec des profils et des sauvegardes fictifs. Les essais restent en mémoire et ne modifient aucune donnée Windows.</p>
      <div class="actions-row">${demo
    ? `<button class="btn primary" type="button" data-demo-action="session.demoExit">${glyph('power')}<span>Quitter la démonstration</span></button><button class="btn" type="button" data-demo-action="session.demoReset">${glyph('refresh')}<span>Réinitialiser les données fictives</span></button>`
    : `<button class="btn primary" type="button" data-demo-action="session.demoEnter">${glyph('play')}<span>Ouvrir la démonstration</span></button>`}</div></div>` });
  const scenarios = demo ? card({ id: 'scenario', title: 'Essayer les scénarios', sub: 'Issue de la prochaine opération fictive', iconName: 'sparkles', cls: 'pr-wide',
    body: `<div data-setting="scenario"><p>La prochaine sauvegarde, restauration, réparation ou récupération peut réussir, produire un résultat partiel ou échouer volontairement. Pour essayer l’annulation, utilisez le suivi pendant une action.</p>
      <div class="pr-form"><label class="pr-field">Issue de la prochaine opération<select class="input" data-scenario></select></label></div>
      <p class="muted" data-demo-count></p>
      <div class="actions-row"><a class="btn" href="#/sauvegarder">${glyph('disk')}<span>Sauvegarder / restaurer</span></a><a class="btn" href="#/analyser">${glyph('search')}<span>Analyser / réparer</span></a><a class="btn" href="#/recuperer">${glyph('lifebuoy')}<span>Récupérer des fichiers</span></a></div></div>` }) : '';
  return `${sectionHead({ title: 'Démonstration', lead: 'Un monde fictif pour découvrir les écrans, montrer FILPRO ou essayer un échec sans toucher à un profil.', icon: 'play', tone: 'accent' })}
    <div class="pr-grid">${switcher}${scenarios}
      <div class="pr-footnote reveal">${notes([
        'Aucune donnée fictive n’est montrée comme un résultat réel : chaque écran de la démonstration le signale.',
        'Les réglages modifiés pendant la démonstration restent dans la démonstration.',
      ])}</div>
    </div>`;
}

export function mount(host, sctx) {
  const { st, call, run, feedback, demo, ctx } = sctx;
  host.innerHTML = markup(demo);
  paint(host);
  const stopReveal = reveal(host);
  const scenario = host.querySelector('[data-scenario]');

  let shownCatalog;
  const paintCatalog = () => {
    const catalog = st.catalog;
    if (!scenario || !catalog || catalog === shownCatalog) return;
    shownCatalog = catalog;
    scenario.innerHTML = catalog.scenarios.map((choice) => `<option value="${esc(choice.id)}">${esc(choice.label)}</option>`).join('');
    scenario.value = catalog.selectedScenario;
    host.querySelector('[data-demo-count]').textContent = `${catalog.stores.length} magasins et ${catalog.destinations.length} destinations fictifs. Aucun accès aux services réels.`;
  };
  const sync = () => {
    paintCatalog();
    if (scenario) scenario.disabled = st.busy || !st.catalog;
    host.querySelectorAll('[data-demo-action]').forEach((btn) => {
      btn.disabled = st.busy || typeof ctx.switchDemonstration !== 'function' || ctx.session?.demonstration == null;
    });
  };

  const onClick = (e) => {
    const btn = e.target instanceof Element ? e.target.closest('[data-demo-action]') : null;
    if (btn && !btn.disabled) run(() => ctx.switchDemonstration(btn.dataset.demoAction));
  };
  const onChange = (e) => {
    if (e.target !== scenario) return;
    run(async () => {
      try { st.catalog = await call('demonstration.scenario', { outcome: scenario.value }); feedback('Scénario fictif sélectionné pour la prochaine opération.'); }
      finally { if (st.alive) { shownCatalog = null; sctx.emit(); scenario.value = st.catalog?.selectedScenario ?? 'succeeded'; } }
    });
  };
  host.addEventListener('click', onClick);
  host.addEventListener('change', onChange);
  const off = sctx.on(sync);
  sync();
  return {
    loaded() { shownCatalog = null; sync(); },
    destroy() {
      off(); stopReveal();
      host.removeEventListener('click', onClick);
      host.removeEventListener('change', onChange);
    },
  };
}
