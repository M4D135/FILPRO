// Confirmation Windows (2.0.8) : en Poste, une opération privilégiée attend la décision prise dans la fenêtre native de
// l'hôte élevé, jamais dans la page. Les dialogues de la page gardent leur contenu et leurs accusés, mais leur bouton
// principal devient « Continuer » ; ceux qui ne faisaient que redire l'opération sont sautés. Aucun accès au DOM ici :
// les écrans et la coque passent leur session et leurs éléments.
import { icon } from './icons.js';

export const CONFIRMATION_WINDOW = 'FILPRO — Confirmation Windows';
export const CONTINUE_LABEL = 'Continuer';
export const DECISION_LINE = `La décision se prend ensuite dans la fenêtre Windows « ${CONFIRMATION_WINDOW} ».`;
export const PENDING_TEXT = `Confirmation Windows en attente : répondez dans la fenêtre « ${CONFIRMATION_WINDOW} ».`;
// Textes fixes de l'hôte (2.0.9), quand l'affichage tourne sous un autre compte que FILPRO élevé.
export const PRESENCE_LOCKED_TEXT = 'FILPRO est verrouillé dans cette session : les opérations en cours continuent. Fermez puis relancez FILPRO avec vos identifiants pour en lancer d’autres.';
export const CROSS_ACCOUNT_UPDATES_TEXT = 'Les mises à jour de FILPRO ne sont pas proposées quand FILPRO est élevé avec un autre compte que celui de la session. Mettez FILPRO à jour depuis une session du compte administrateur.';
export const PACK_MODIFIABLE_TEXT = 'Le compte ouvert sur ce poste peut modifier le dossier de FILPRO : le programme a pu être remplacé avant son lancement. Placez FILPRO dans un dossier que ce compte peut lire sans pouvoir le modifier, par exemple C:\\Program Files\\FILPRO.';

// Capacité annoncée par l'hôte dans session.info ; absente ou fausse en démonstration et dans la prévisualisation.
export function nativeConfirmation(session) {
  return session?.demonstration === false && session?.capabilities?.nativeConfirmation === true;
}

// Bouton principal d'un dialogue de la page qui précède la fenêtre Windows.
export function primaryLabel(session, label) {
  return nativeConfirmation(session) ? CONTINUE_LABEL : label;
}

// Ligne ajoutée à ce dialogue (texte fixe, sans donnée de la page).
export function decisionLine(session) {
  return nativeConfirmation(session) ? `<div class="callout info">${icon('shield')}<div><p>${DECISION_LINE}</p></div></div>` : '';
}

// Confirmation qui ne fait que redire l'opération : la fenêtre Windows la présente avec les valeurs détenues par l'hôte.
export async function confirmUnlessNative(session, ask) {
  return nativeConfirmation(session) ? true : ask();
}

// Indications de l'hôte en Poste ; l'admission du pont applique de toute façon ces règles à chaque demande.
export function presenceLocked(session) {
  return session?.demonstration === false && session?.capabilities?.presenceLocked === true;
}

export function packModifiable(session) {
  return session?.demonstration === false && session?.capabilities?.packModifiable === true;
}

// Mises à jour du code de FILPRO : seulement quand l'hôte ne les déclare pas indisponibles.
export function codeUpdates(session) {
  return session?.capabilities?.codeUpdates !== false;
}

// Bandeau permanent, en Poste seulement : verrouillé à l'ouverture (session.info) ou ensuite (presence.changed, redit
// par l'hôte quand la page est prête). Le verrou ne se lève jamais.
export function bindPresenceBanner(banner, subscribe, session) {
  let locked = false;
  const show = () => { banner.innerHTML = locked ? `${icon('lock')}<span>${PRESENCE_LOCKED_TEXT}</span>` : ''; banner.hidden = !locked; };
  locked = presenceLocked(session); show();
  return subscribe('presence.changed', (data) => { if (session?.demonstration === false && data?.locked === true) { locked = true; show(); } });
}

// Bandeau permanent, en Poste seulement : dossier de FILPRO modifiable par le compte de la session (avertissement,
// jamais un refus), connu à l'ouverture ou annoncé ensuite par l'hôte.
export function bindPackBanner(banner, subscribe, session) {
  let exposed = false;
  const show = () => { banner.innerHTML = exposed ? `${icon('alert')}<span>${PACK_MODIFIABLE_TEXT}</span>` : ''; banner.hidden = !exposed; };
  exposed = packModifiable(session); show();
  return subscribe('pack.exposure', (data) => { if (session?.demonstration === false && data?.modifiable === true) { exposed = true; show(); } });
}

// Bandeau non bloquant, sans détail ni autorité : il signale seulement qu'une fenêtre Windows attend une réponse.
export function bindConfirmationBanner(banner, subscribe) {
  return subscribe('confirmation.changed', (data) => {
    const pending = data?.pending === true;
    banner.innerHTML = pending ? `${icon('shield')}<span>${PENDING_TEXT}</span>` : '';
    banner.hidden = !pending;
  });
}
