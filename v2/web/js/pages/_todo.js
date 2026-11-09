// Écran générique des familles non portées dans les lots 001-002 : annonce honnête de l'indisponibilité, familles et
// références V1 concernées, aucune fonction simulée présentée comme réelle.
import { icon } from '../icons.js';
import { esc, applyVars, reveal } from '../ui.js';

// Familles V1 et principaux modules source (lecture seule sous wad/). Le détail écran par écran vient dans un lot ultérieur.
const FAMILY = {
  suivi: {
    label: 'Suivi en direct',
    icon: 'activity',
    modules: ['FILPRO.Jobs.ps1', 'FILPRO.Progress.ps1', 'FILPRO.Worker.ps1', 'FILPRO.Scheduling.ps1'],
    note: 'Suivi des tâches en cours : progression par opération, arrêt demandé distingué d’un arrêt achevé.',
  },
  recuperer: {
    label: 'Récupérer des fichiers',
    icon: 'folder',
    modules: ['FILPRO.Recovery.ps1', 'FILPRO.Recovery.Plan.ps1', 'FILPRO.FileVault.ps1'],
    note: 'Récupération de fichiers à partir des magasins et des sauvegardes.',
  },
  journal: {
    label: 'Journal',
    icon: 'clock',
    modules: ['FILPRO.Reports.ps1', 'FILPRO.Support.ps1', 'FILPRO.Reviews.ps1'],
    note: 'Journal des opérations et des incidents de la session.',
  },
  parametres: {
    label: 'Paramètres',
    icon: 'settings',
    modules: ['FILPRO.Configuration.ps1', 'FILPRO.Policy.ps1', 'FILPRO.RestorePolicy.ps1', 'FILPRO.Theme.ps1'],
    note: 'Réglages, politiques de restauration, apparence et stockage (local, USB, partage LAN).',
  },
};

function layout(page) {
  const fam = FAMILY[page.id] ?? { label: page.label, icon: page.icon, modules: [], note: 'Famille à inventorier écran par écran.' };
  const modules = fam.modules.length
    ? fam.modules.map((m, n) => `<li data-j="${n}"><span class="mk" aria-hidden="true"></span><span><span class="t mono">${esc(m)}</span></span></li>`).join('')
    : `<li><span class="mk" aria-hidden="true"></span><span><span class="t muted">Aucun module source listé pour cette famille dans ce lot.</span></span></li>`;
  return `<div class="page todo-page">
    <header class="page-header reveal">
      <div class="head-row"><span class="page-icon">${icon(fam.icon)}</span>
        <div class="titles"><h1 class="gradient-text">${esc(fam.label)}</h1>
          <p class="subtitle">FILPRO 2 · famille non portée dans les lots 001-002</p></div></div>
      <div class="actions">
        <button class="btn primary" type="button" data-act="home">${icon('home')}Retour à l’accueil</button>
      </div>
    </header>
    <section class="card todo-hero reveal spot">
      <div class="empty">
        <div class="art">${icon(fam.icon)}</div>
        <h2>Fonction non disponible dans ce lot</h2>
        <p>${esc(fam.note)} Cet écran n’exécute encore aucune action réelle : rien n’est simulé en faisant croire à un
          résultat. Le portage reprendra les fonctions de FILPRO V1 listées ci-dessous, puis les améliorations prévues.</p>
        <span class="chip info">${icon('info')}Indisponible volontairement</span>
      </div>
    </section>
    <section class="card reveal spot">
      <div class="card-head"><span class="card-icon">${icon('folder')}</span><h3>Modules source V1 (lecture seule)</h3></div>
      <ul class="feature-list cascade">${modules}</ul>
    </section>
  </div>`;
}

export function render(main, ctx) {
  main.innerHTML = layout(ctx.page);
  applyVars(main);
  const stop = reveal(main);
  main.querySelector('[data-act="home"]').addEventListener('click', () => ctx.go('accueil'));
  return () => stop();
}
