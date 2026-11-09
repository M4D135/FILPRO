// Point d'entrée du mock de prévisualisation, importé par bridge.js quand aucun hôte WebView2 n'est présent.
// Il n'émule que les opérations réellement déclarées côté C# (accueil.*, session.*, analyser.*, sauvegarder.*) ; toute autre
// opération renvoie la même erreur que le pont (operation.unknown), afin que l'interface ne laisse jamais croire à un résultat réel.
import { profilesInventory, softwareInventory, lastSelection, saveSelection, conflictPreview, confirmPlan, confirmRestorePlan, readCommonSelection, setCommonSelection, clearCommonSelection } from './mock-poste.js';
import { worldCall } from './mock-world.js';
export { setEventHandler } from './mock-world.js';

function bridgeError(code, message) {
  return Object.assign(new Error(message), { code });
}

export async function mockCall(op, args = {}) {
  const worldResult = await worldCall(op, args);
  if (worldResult !== undefined) return worldResult;
  // Refus local des mêmes règles que le pont : identifiant absent ou opération hors contrat.
  switch (op) {
    case 'analyser.profiles':
    case 'sauvegarder.profiles':
      return Promise.resolve(profilesInventory());
    case 'analyser.software':
    case 'sauvegarder.software':
      return Promise.resolve(softwareInventory(String(args?.sid ?? '')));
    case 'analyser.lastSelection':
    case 'sauvegarder.lastSelection':
      return Promise.resolve(lastSelection(String(args?.sid ?? '')));
    case 'sauvegarder.saveSelection':
      return saveSelection(args);
    case 'sauvegarder.conflictPolicies':
      return Promise.resolve(conflictPreview(String(args?.sid ?? '')));
    case 'sauvegarder.readCommonSelection':
      if (!args || Object.keys(args).length) throw bridgeError('arguments.invalid','Aucun argument attendu.');
      return readCommonSelection();
    case 'sauvegarder.setCommonSelection':
      if (!args || Object.keys(args).some((key)=>!['sid','groupIds'].includes(key))) throw bridgeError('arguments.invalid','Arguments hors contrat.');
      return setCommonSelection(args.sid,args.groupIds);
    case 'sauvegarder.clearCommonSelection':
      if (!args || Object.keys(args).some((key)=>key!=='confirmed') || args.confirmed!==true) throw bridgeError('arguments.invalid','Confirmation requise.');
      return clearCommonSelection();
    case 'sauvegarder.confirmPlan':
      return confirmPlan();
    case 'sauvegarder.confirmRestorePlan':
      return confirmRestorePlan();
    case 'analyser.confirmRepair':
    case 'analyser.rollbackRepair':
    case 'recuperer.confirmPlan':
    case 'updates.install':
    case 'updates.rollback':
    case 'session.choosePath':
      throw bridgeError('simulation.readOnly','Action native refusée en démonstration.');
    default:
      return Promise.reject(bridgeError('operation.unknown', 'Opération inconnue : rien n’a été exécuté.'));
  }
}
