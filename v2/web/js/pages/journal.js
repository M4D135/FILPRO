import { esc, errorText, applyVars, reveal } from '../ui.js';
import { icon } from '../icons.js';
import { timeline, bindTimeline } from '../kit/index.js';
import { observeOperations, refreshOperations, jobState } from '../operations.js';

export function render(main) {
  main.innerHTML = `<div class="page workflow-page"><header class="page-header reveal"><div class="head-row"><span class="page-icon">${icon('clock')}</span><div class="titles"><h1>Journal des opérations</h1><p class="subtitle">Bilans locaux conservés entre les sessions. En démonstration, seul le scénario fictif courant est affiché.</p></div></div><button class="btn" type="button" data-refresh>${icon('refresh')}Actualiser</button></header>
    <section class="card reveal"><label class="workflow-search">Rechercher une opération<input class="input" type="search" data-search placeholder="Titre, action, état…"></label><div data-error></div><div data-journal></div></section></div>`;
  applyVars(main); const stopReveal = reveal(main); let active = true; let jobs = [];
  const renderJournal = () => {
    const query = main.querySelector('[data-search]').value.toLocaleLowerCase('fr-FR');
    const events = jobs.filter((job) => `${job.title} ${job.currentAction} ${job.error?.message || ''} ${jobState(job).label}`.toLocaleLowerCase('fr-FR').includes(query)).map((job) => ({
      when: job.finishedAt || job.startedAt, title: `${job.title} · ${jobState(job).label}`, text: job.error?.message || job.currentAction,
      tone: jobState(job).tone, icon: 'activity', meta: job.demonstration ? 'Démonstration — données fictives' : 'Poste réel',
      html: `<a href="#/suivi?operation=${encodeURIComponent(job.id)}">Ouvrir le bilan</a>${(job.warnings || []).length ? `<ul>${job.warnings.map((warning) => `<li>${esc(warning)}</li>`).join('')}</ul>` : ''}`,
    }));
    main.querySelector('[data-journal]').innerHTML = timeline(events, { empty: 'Aucune opération ne correspond à cette recherche.' });
    bindTimeline(main);
  };
  const stop = observeOperations((list) => { if (active) { jobs = list; renderJournal(); } });
  main.querySelector('[data-search]').addEventListener('input', renderJournal);
  const load = async () => {
    try { await refreshOperations(); if (active) main.querySelector('[data-error]').innerHTML = ''; }
    catch (err) { if (active) main.querySelector('[data-error]').innerHTML = `<p class="callout alert" role="alert">${esc(errorText(err))}</p>`; }
  };
  main.querySelector('[data-refresh]').addEventListener('click', load); load();
  return () => { active = false; stop(); stopReveal(); };
}
