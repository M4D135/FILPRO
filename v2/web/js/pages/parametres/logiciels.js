// Paramètres › Logiciels communs : choix de logiciels partagés par les profils sans sélection mémorisée, et modèles à
// partager (export, import d'un patch, dossier de diffusion). Aucun profil Windows n'est modifié ici.
import { esc, glyph, card, notes, sectionHead, paint, button } from './common.js';
import { reveal } from '../../ui.js';
import { confirmDialog, openDialog } from '../../kit/index.js';

function markup() {
  return `${sectionHead({ title: 'Logiciels communs', lead: 'Les logiciels et clés HKCU choisis une fois pour tous les profils qui n’ont pas de sélection à eux, et les modèles pour les partager.', icon: 'object', tone: 'info' })}
    <div class="pr-grid">
      ${card({ id: 'common', title: 'Choix communs', sub: 'Pour les profils sans sélection mémorisée', iconName: 'object', toneName: 'info', cls: 'pr-wide',
    body: `<div data-setting="common"><p class="pr-lead-text" data-common-status>Lecture des choix communs…</p>
        <div class="actions-row"><a class="btn" href="#/sauvegarder">${glyph('users')}<span>Configurer depuis un profil</span></a>${button('clear-common', 'Effacer les choix communs', 'trash', { kind: 'ghost' })}</div></div>` })}
      ${card({ id: 'templates', title: 'Modèle à partager', sub: 'JSON et sa somme .sha256, à garder ensemble', iconName: 'layers',
    body: `<div data-setting="templates"><p>Un modèle partage ces choix sans modifier les profils. À l’import, l’aperçu garde les entrées absentes et propose un choix explicite pour chaque conflit.</p>
        <div class="actions-row">${button('template-export', 'Exporter le modèle', 'download')}${button('template-import', 'Importer un patch', 'file')}</div></div>` })}
      ${card({ id: 'source', title: 'Depuis le dossier de diffusion', sub: 'Le patch déposé avec les mises à jour', iconName: 'folder',
    body: `<p>Cherche un patch de modèles dans le dossier de diffusion mémorisé pour les mises à jour, puis montre l’aperçu avant tout ajout.</p>
        <div class="actions-row">${button('template-source', 'Chercher dans le dossier de diffusion', 'search')}</div>` })}
      <div class="pr-footnote reveal">${notes([
        'Une liste vide est un choix explicite : aucun logiciel commun.',
        'Le modèle contient les chemins relatifs de logiciels et les clés HKCU sélectionnées ; aucun compte, profil, clé de chiffrement ou consentement.',
      ])}</div>
    </div>`;
}

export function mount(host, sctx) {
  const { st, call, run, feedback } = sctx;
  host.innerHTML = markup();
  paint(host);
  const stopReveal = reveal(host);

  const sync = () => {
    const common = st.common;
    // Une lecture ratée passe avant la valeur précédente, pour ne pas la présenter comme fraîche.
    host.querySelector('[data-common-status]').textContent = st.errors.common ?? (common
      ? common.hasSelection
        ? `${common.appData?.length ?? 0} dossier(s) et ${common.registryKeys?.length ?? 0} clé(s) constituent les choix communs. Une liste vide est un choix explicite.`
        : 'Aucun choix commun enregistré. Les recommandations s’appliquent lorsqu’elles sont activées et qu’aucun choix du profil n’existe.'
      : 'Lecture des choix communs…');
    host.querySelector('[data-act="clear-common"]').disabled = st.busy || common?.hasSelection !== true;
    for (const key of ['template-export', 'template-import', 'template-source']) host.querySelector(`[data-act="${key}"]`).disabled = st.busy || !common;
  };

  const showCommon = (value) => { st.common = value; sctx.emit(); };
  const reviewPatch = async (preview) => {
    const names = { add: 'Ajout', identical: 'Identique', conflict: 'Conflit local' };
    const hasConflicts = preview.changes.some((item) => item.disposition === 'conflict');
    const dialog = openDialog({ title: 'Examiner le patch de modèles', icon: 'object', size: 'lg',
      subtitle: preview.demonstration ? 'Démonstration — données fictives' : 'Aucun profil Windows ne sera modifié',
      body: `<p>${esc(preview.notice)}</p><p class="mono">SHA-256 : ${esc(preview.sha256)}</p><div class="workflow-table-wrap"><table class="workflow-table"><thead><tr><th>Portée et chemin</th><th>Local</th><th>Proposé</th><th>État</th></tr></thead><tbody>${preview.changes.map((item) => `<tr><th>${esc(item.scope)} · ${esc(item.relativePath)}</th><td>${esc(item.before ?? '—')}</td><td>${esc(item.after)}</td><td>${esc(names[item.disposition] ?? item.disposition)}</td></tr>`).join('')}</tbody></table></div>`,
      actions: [{ id: 'cancel', label: 'Annuler', safe: true }, { id: 'keep', label: hasConflicts ? 'Ajouter et garder mes conflits' : 'Appliquer le patch', primary: true }, ...(hasConflicts ? [{ id: 'replace', label: 'Appliquer aussi aux conflits' }] : [])],
    });
    const choice = await dialog.result;
    if (!st.alive || !['keep', 'replace'].includes(choice)) return;
    const result = await call('sauvegarder.applyCommonPatch', { previewId: preview.id, replaceConflicts: choice === 'replace', confirmed: true });
    if (st.alive) { showCommon(result.state); feedback([result.notice, result.backupPath ? `Sauvegarde : ${result.backupPath}` : ''].filter(Boolean).join(' ')); }
  };

  const onClick = (e) => {
    const btn = e.target instanceof Element ? e.target.closest('[data-act]') : null;
    if (!btn || btn.disabled) return;
    const act = btn.dataset.act;
    if (act === 'template-export') run(async () => {
      const destination = await sctx.choose('saveConfiguration', 'exportConfigurationPath'); if (!destination) return;
      if (!await confirmDialog({ title: 'Exporter les choix communs', message: 'Le JSON et sa somme .sha256 contiendront les chemins relatifs de logiciels et les clés HKCU sélectionnées. Aucun compte, profil, clé de chiffrement ou consentement ne sera inclus.', confirmLabel: 'Exporter ce modèle' })) return;
      const result = await call('sauvegarder.exportCommonPatch', { destination, confirmed: true }); feedback(`${result.notice} ${result.writtenPath ?? ''}`);
    });
    else if (act === 'template-import') run(async () => {
      const source = await sctx.choose('openConfiguration', 'importConfigurationPath'); if (source) await reviewPatch(await call('sauvegarder.previewCommonPatch', { source }));
    });
    else if (act === 'template-source') run(async () => reviewPatch(await call('sauvegarder.previewCommonPatchFromSource', {})));
    else if (act === 'clear-common') run(async () => {
      if (!await confirmDialog({ title: 'Effacer les choix communs', message: 'Les sélections enregistrées séparément pour chaque profil seront conservées. Les profils sans historique reprendront les recommandations activées.', confirmLabel: 'Effacer les choix communs' })) return;
      showCommon(await call('sauvegarder.clearCommonSelection', { confirmed: true }));
      feedback('Choix communs effacés ; sélections propres aux profils conservées.');
    });
  };
  host.addEventListener('click', onClick);
  const off = sctx.on(sync);
  sync();
  return { destroy() { off(); stopReveal(); host.removeEventListener('click', onClick); } };
}
