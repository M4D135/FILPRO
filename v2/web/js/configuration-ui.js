import { call } from './bridge.js';
import { esc } from './ui.js';
import { openDialog } from './kit/index.js';
import { sessionInfo } from './session-state.js';
import { primaryLabel, decisionLine } from './native-confirmation.js';

export const SETTING_LABELS = {
  theme: 'Thème', motion: 'Animations', navigationCollapsed: 'Navigation repliée', autoSelectCurrentTask: 'Sélectionner la tâche en cours',
  notifyOnCompletion: 'Notification de fin', recommendedSelection: 'Réglages recommandés', migApp: 'Ancien réglage MigApp (désactivé)',
  vss: 'Instantané VSS', continueOnError: 'Continuer après une erreur', encrypted: 'Chiffrement', conflictPolicy: 'Doublons à la restauration',
};
const VALUES = { true: 'Oui', false: 'Non', True: 'Oui', False: 'Non', system: 'Suivre Windows', light: 'Clair', dark: 'Sombre', contrast: 'Contraste élevé', full: 'Riches', reduced: 'Réduites', keepBoth: 'Conserver les deux fichiers · registre actuel', keepCurrent: 'Garder l’actuel', useSaved: 'Rétablir le sauvegardé' };
export function configurationChanged(state) {
  if (state?.settings) window.dispatchEvent(new CustomEvent('filpro:configuration', { detail: state }));
}
export async function reviewConfiguration(preview, { bootstrap = false } = {}) {
  const rows = (preview.changes || []).map((change) => `<tr><th scope="row">${esc(SETTING_LABELS[change.key] ?? change.key)}</th><td>${esc(VALUES[change.before] ?? change.before)}</td><td>${esc(VALUES[change.after] ?? change.after)}</td><td>${change.disposition === 'preserve' ? 'Conservé' : change.before === change.after ? 'Identique' : 'Remplacé'}</td></tr>`).join('');
  const defaults = preview.kind === 'defaults';
  // Seule l'écriture des défauts du pack passe par la fenêtre Windows ; l'import et le premier lancement restent ici.
  const session = defaults ? sessionInfo() : null;
  const dialog = openDialog({ title: bootstrap ? 'Configuration fournie avec ce pack' : defaults ? 'Définir la configuration par défaut' : 'Importer cette configuration',
    subtitle: preview.demonstration ? 'Démonstration — aucun fichier réel modifié' : 'Réglages partageables uniquement', icon: 'settings', size: 'lg',
    body: `<p>${esc(preview.notice)}</p><p class="mono">${esc(preview.sourceLabel)}</p><div class="workflow-table-wrap"><table class="workflow-table"><thead><tr><th>Réglage</th><th>Actuel</th><th>Proposé</th><th>Décision</th></tr></thead><tbody>${rows}</tbody></table></div><p class="muted">Une sauvegarde préalable et un contrôle de fraîcheur protègent les réglages existants. Aucun secret, profil ou historique n’est exporté.</p>${decisionLine(session)}`,
    actions: [{ id: bootstrap ? 'keep' : 'cancel', label: bootstrap ? 'Garder mes réglages' : 'Annuler', safe: true }, { id: 'apply', label: primaryLabel(session, defaults ? 'Définir ces valeurs par défaut' : 'Appliquer la configuration'), primary: true }],
  });
  return dialog.result;
}
export async function decideBootstrap(preview) {
  const choice = await reviewConfiguration(preview, { bootstrap: true });
  if (choice !== 'keep' && choice !== 'apply') return null;
  const result = await call('configuration.decideBootstrap', { previewId: preview.id, apply: choice === 'apply', confirmed: true });
  configurationChanged(result.state);
  return result;
}
