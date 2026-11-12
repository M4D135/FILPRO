// Paramètres › Configuration à diffuser : configuration fournie avec le dossier (aperçu pour garder ses réglages ou appliquer
// ceux du dossier), définition de la configuration par défaut du dossier, export et import des réglages partageables.
import { esc, glyph, card, notes, sectionHead, paint, button } from './common.js';
import { reveal } from '../../ui.js';
import { confirmDialog } from '../../kit/index.js';
import { reviewConfiguration, decideBootstrap } from '../../configuration-ui.js';

function markup() {
  return `${sectionHead({ title: 'Configuration à diffuser', lead: 'Les réglages de ce poste deviennent ceux du dossier de FILPRO, ou passent d’un poste à l’autre par un fichier. Aucun secret, compte, SID, clé de chiffrement ou historique n’y figure.', icon: 'layers', tone: 'accent' })}
    <div data-slot="bootstrap"></div>
    <div class="pr-grid">
      ${card({ id: 'defaults', title: 'Configuration par défaut du dossier', sub: 'wad\\config\\deployment-defaults.json', iconName: 'folder', toneName: 'accent', cls: 'pr-wide',
    body: `<div data-setting="defaults"><p>Configurez ce poste, puis définissez ses réglages par défaut. Un nouvel utilisateur les recevra au premier lancement ; un utilisateur existant choisira de les appliquer ou de garder les siens.</p>
        <div class="actions-row"><button class="btn primary" type="button" data-act="defaults">${glyph('check')}<span>Définir comme configuration par défaut</span></button></div>
        <p class="pr-hint" data-slot="dirty-hint" hidden>${glyph('info')}<span>Enregistrez d’abord les réglages modifiés dans Sauvegarde et restauration.</span></p></div>` })}
      ${card({ id: 'share', title: 'Exporter ou importer', sub: 'Réglages partageables uniquement', iconName: 'copy',
    body: `<div data-setting="share"><p>L’export écrit un fichier de réglages autorisés. L’import montre chaque changement avant de l’appliquer, avec une copie préalable des réglages actuels.</p>
        <div class="actions-row">${button('export', 'Exporter la configuration', 'download')}${button('import', 'Importer une configuration', 'file')}</div></div>` })}
      ${card({ id: 'update', title: 'Après une mise à jour', sub: 'Le fichier suit la nouvelle version', iconName: 'refresh',
    body: notes([
      'Une mise à jour garde la configuration par défaut définie dans ce dossier.',
      'Un pack qui en apporte une autre est refusé : retirer d’abord celle du dossier pour prendre celle du pack.',
      'Pour diffuser le dossier en ZIP, régénérer le ZIP et sa somme SHA-256.',
    ]) })}
      <div class="pr-footnote reveal">${notes([
        '« Définir comme configuration par défaut » écrit dans le dossier de FILPRO, qui se diffuse tel quel. Le manifeste des binaires reste intact.',
        'Une configuration fournie invalide est ignorée et signalée ; les binaires restent vérifiés.',
      ])}</div>
    </div>`;
}

export function mount(host, sctx) {
  const { st, call, run, demo } = sctx;
  host.innerHTML = markup();
  paint(host);
  const stopReveal = reveal(host);

  let shownBootstrap;
  const paintBootstrap = () => {
    const state = st.config;
    const key = state ? `${state.revision}|${state.bootstrap?.id ?? ''}|${(state.warnings || []).join('|')}` : '';
    if (key === shownBootstrap) return;
    shownBootstrap = key;
    const slot = host.querySelector('[data-slot="bootstrap"]');
    slot.innerHTML = state ? [...new Set(state.warnings || [])].map((warning) => `<p class="callout warn" role="status">${esc(warning)}</p>`).join('')
      + (state.bootstrap ? `<div class="callout info pr-bootstrap"><p>Ce dossier propose une configuration. Vous pouvez examiner les changements et garder vos réglages.</p><button class="btn" type="button" data-act="review-bootstrap">Examiner la configuration fournie</button></div>` : '') : '';
  };
  const sync = () => {
    paintBootstrap();
    const dirty = sctx.dirty();
    for (const key of ['defaults', 'export', 'import']) host.querySelector(`[data-act="${key}"]`).disabled = st.busy || !st.config || dirty;
    const review = host.querySelector('[data-act="review-bootstrap"]');
    if (review) review.disabled = st.busy;
    host.querySelector('[data-slot="dirty-hint"]').hidden = !dirty;
  };

  const onClick = (e) => {
    const btn = e.target instanceof Element ? e.target.closest('[data-act]') : null;
    if (!btn || btn.disabled) return;
    const act = btn.dataset.act;
    if (act === 'review-bootstrap') run(async () => { const result = await decideBootstrap(st.config.bootstrap); if (result) sctx.acceptResult(result); });
    else if (act === 'defaults') run(async () => {
      const preview = await call('configuration.previewDefaults', {});
      if (st.alive && await reviewConfiguration(preview) === 'apply') sctx.acceptResult(await call('configuration.applyDefaults', { previewId: preview.id, confirmed: true }));
    });
    else if (act === 'export') run(async () => {
      const destinationPath = await sctx.choose('saveConfiguration', 'exportConfigurationPath');
      if (destinationPath && await confirmDialog({ title: 'Exporter les réglages partageables', message: `Destination ${demo ? 'fictive' : 'choisie'} : ${destinationPath}. Aucun secret ou profil ne sera inclus.`, confirmLabel: 'Exporter' })) sctx.acceptResult(await call('configuration.export', { destinationPath, confirmed: true }));
    });
    else if (act === 'import') run(async () => {
      const sourcePath = await sctx.choose('openConfiguration', 'importConfigurationPath'); if (!sourcePath) return;
      const preview = await call('configuration.previewImport', { sourcePath });
      if (st.alive && await reviewConfiguration(preview) === 'apply') sctx.acceptResult(await call('configuration.applyImport', { previewId: preview.id, confirmed: true }));
    });
  };
  host.addEventListener('click', onClick);
  const off = sctx.on(sync);
  sync();
  return { destroy() { off(); stopReveal(); host.removeEventListener('click', onClick); } };
}
