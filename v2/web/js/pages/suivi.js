import { call } from '../bridge.js';
import { esc, errorText, formatDateTime, applyVars, reveal } from '../ui.js';
import { icon } from '../icons.js';
import { pill, confirmDialog } from '../kit/index.js';
import { observeOperations, refreshOperations, rememberOperation, followOperation, jobState, isTerminal, duration } from '../operations.js';
import { confirmUnlessNative } from '../native-confirmation.js';

export function render(main, ctx) {
  main.innerHTML = `<div class="page workflow-page"><header class="page-header reveal"><div class="head-row"><span class="page-icon">${icon('activity')}</span><div class="titles"><h1>Suivi en direct</h1><p class="subtitle">La préparation, l’action courante et le bilan restent visibles.</p></div></div><button class="btn" type="button" data-refresh>${icon('refresh')}Actualiser</button></header>
    <div data-error></div><div class="workflow-columns"><section class="card reveal"><div class="card-head"><h2>Opérations</h2><span class="meta" data-count>—</span></div><div class="operation-list" data-jobs></div></section>
    <section class="card reveal" data-detail aria-live="polite"><div class="empty"><p>Choisissez une opération pour afficher son suivi.</p></div></section></div></div>`;
  applyVars(main);
  const stopReveal = reveal(main);
  let active = true;
  let selectedId = ctx.params?.operation || null;
  let rows = [];
  const cancelling = new Set();
  const reverting = new Set();
  const detail = main.querySelector('[data-detail]');
  const list = main.querySelector('[data-jobs]');
  const renderDetail = () => {
    if (!active) return;
    const job = rows.find((item) => item.id === selectedId);
    if (!job) { detail.innerHTML = '<div class="empty"><p>Aucune opération dans cette session.</p></div>'; return; }
    const state = jobState(job);
    const progress = Number.isFinite(job.progressPercent) ? Math.max(0, Math.min(100, job.progressPercent)) : null;
    const elapsed = (Date.parse(job.finishedAt || new Date().toISOString()) - Date.parse(job.startedAt)) / 1000;
    const units = Number.isFinite(job.completedUnits) && Number.isFinite(job.totalUnits)
      ? `${job.completedUnits.toLocaleString('fr-FR')} / ${job.totalUnits.toLocaleString('fr-FR')} éléments` : 'Quantité non mesurée';
    detail.innerHTML = `<div class="card-head"><span class="card-icon">${icon('activity')}</span><h2>${esc(job.title)}</h2>${pill(state.tone, state.label)}</div>
      ${job.demonstration ? '<p class="callout info">Démonstration — cette opération modifie uniquement les données fictives.</p>' : ''}
      <div class="operation-action"><span class="eyebrow">Action courante</span><h3>${esc(job.currentAction || state.label)}</h3></div>
      <progress class="operation-progress" max="100"${progress == null ? '' : ` value="${progress}"`} aria-label="Progression de l’opération"></progress>
      <div class="operation-metrics"><span>${progress == null ? 'Progression non chiffrée' : `${Math.round(progress)} %`}</span><span>${esc(units)}</span></div>
      <dl class="workflow-facts"><div><dt>Durée écoulée</dt><dd>${esc(duration(elapsed))}</dd></div><div><dt>Durée restante</dt><dd>${isTerminal(job) ? '—' : esc(duration(job.estimatedRemainingSeconds))}</dd></div><div><dt>Début</dt><dd>${esc(formatDateTime(job.startedAt) || '—')}</dd></div>${job.finishedAt ? `<div><dt>Fin</dt><dd>${esc(formatDateTime(job.finishedAt) || '—')}</dd></div>` : ''}</dl>
      ${job.error ? `<div class="callout alert" role="alert">${icon('alert')}<p>${esc(job.error.message ?? job.error)}</p></div>` : ''}
      ${(job.warnings || []).length ? `<div class="callout warn"><ul>${job.warnings.map((warning) => `<li>${esc(warning)}</li>`).join('')}</ul></div>` : ''}
      ${job.resultPath ? `<div class="workflow-result"><span>Résultat ${job.demonstration ? 'fictif' : ''}</span><code>${esc(job.resultPath)}</code></div>` : ''}
      ${!isTerminal(job) ? `<button type="button" class="btn" data-cancel${cancelling.has(job.id) || job.cancellationRequested ? ' disabled' : ''}>${icon('pause')}${cancelling.has(job.id) || job.cancellationRequested ? 'Arrêt demandé — nettoyage en cours' : 'Demander l’arrêt'}</button><p class="muted">L’arrêt est confirmé après la fin des écritures engagées et le nettoyage.</p>` : '<a class="btn" href="#/journal">Consulter le journal</a>'}
      ${String(job.kind).toLowerCase() === 'repair' && ['succeeded','partial'].includes(String(job.state).toLowerCase()) && job.resultPath ? `<button type="button" class="btn" data-repair-rollback${reverting.has(job.id) ? ' disabled' : ''}>Examiner le retour arrière de la réparation</button>` : ''}
      ${String(job.kind).toLowerCase() === 'catalog' && ['succeeded','partial'].includes(String(job.state).toLowerCase()) && job.resultPath ? '<button type="button" class="btn" data-open-catalog>Relire le catalogue pour récupérer des fichiers</button>' : ''}`;
    detail.querySelector('[data-open-catalog]')?.addEventListener('click', () => ctx.go('recuperer', { operation: job.id }));
    detail.querySelector('[data-cancel]')?.addEventListener('click', async () => {
      if (!await confirmDialog({ title: 'Demander l’arrêt', message: 'L’opération termine sa phase sûre et libère ses ressources. Les résultats partiels restent signalés.', confirmLabel: 'Demander l’arrêt', icon: 'pause' })) return;
      cancelling.add(job.id); renderDetail();
      try { rememberOperation(await call('operations.cancel', { id: job.id, confirmed: true })); }
      catch (err) { if (active) ctx.toast(errorText(err), { tone: 'alert' }); }
      finally { cancelling.delete(job.id); if (active) renderDetail(); }
    });
    detail.querySelector('[data-repair-rollback]')?.addEventListener('click', async () => {
      if (!await confirmUnlessNative(ctx.session, () => confirmDialog({ title: 'Retour arrière de la réparation', message: `${job.demonstration ? 'Le monde fictif' : 'Le moteur'} relira le reçu et vérifiera que le profil peut revenir à son état préservé. Toute incohérence bloque l’action. Confirmez la préparation du retour arrière.`, confirmLabel: 'Confirmer le retour arrière' }))) return;
      reverting.add(job.id); renderDetail();
      try {
        const next = await call(job.demonstration ? 'demonstration.repairRollback' : 'analyser.rollbackRepair', { receiptPath: job.resultPath, confirmed: true });
        followOperation(next, ctx);
      } catch (err) { if (active) ctx.toast(errorText(err), { tone: 'alert' }); }
      finally { reverting.delete(job.id); if (active) renderDetail(); }
    });
  };
  const redraw = (jobs, changed, previous) => {
    if (!active) return;
    rows = jobs;
    // Une nouvelle action devient la sélection ; consulter une ancienne tâche reste possible après ce premier choix.
    if (changed && !previous && !isTerminal(changed)) selectedId = changed.id;
    if (!selectedId || !rows.some((job) => job.id === selectedId)) selectedId = rows.find((job) => !isTerminal(job))?.id ?? rows[0]?.id;
    main.querySelector('[data-count]').textContent = `${rows.length} opération(s)`;
    list.innerHTML = rows.map((job) => {
      const state = jobState(job);
      return `<button type="button" class="operation-row${job.id === selectedId ? ' is-active' : ''}" data-id="${esc(job.id)}" aria-pressed="${job.id === selectedId}"><span><strong>${esc(job.title)}</strong><small>${esc(job.currentAction || state.label)}</small></span>${pill(state.tone, state.label, { size: 'sm' })}</button>`;
    }).join('') || '<p class="muted">Les opérations apparaîtront ici après votre confirmation.</p>';
    renderDetail();
  };
  list.addEventListener('click', (event) => { const row = event.target.closest('[data-id]'); if (row) { selectedId = row.dataset.id; redraw(rows); } });
  const stop = observeOperations(redraw);
  const load = async () => {
    const error = main.querySelector('[data-error]');
    try { await refreshOperations(); if (active) error.innerHTML = ''; }
    catch (err) { if (active) error.innerHTML = `<div class="callout alert" role="alert">${esc(errorText(err, 'Le suivi est indisponible.'))}</div>`; }
  };
  main.querySelector('[data-refresh]').addEventListener('click', load);
  // Sert aussi à rattraper une notification perdue ; le service reste la seule source des états et des pourcentages.
  const poll = setInterval(() => { if (rows.some((job) => !isTerminal(job))) load(); }, 1500);
  load();
  return () => { active = false; clearInterval(poll); stop(); stopReveal(); };
}
