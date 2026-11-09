# Entrée Windows et commandes de présentation

Décisions de David du 26 octobre : connexion avec la session Windows courante et
validation UAC, même niveau graphique que DOMENIA V2 et Pesto V2, aucun slogan ni
ascenseur dans la fenêtre. Les commandes de démonstration et d'animations se trouvent
uniquement dans Paramètres. Le badge « Poste réel » est retiré ; la démonstration
active reste identifiée.

## Implémentation

`WelcomeForm` vérifie les ressources du pack puis affiche `login.html` avec le kit
local. Aucun `Composition`, service de profils ou pont métier n'est créé à ce stade.
Le canal d'entrée accepte seulement `info`, `open`, `close` et `layout`, avec un objet fermé
et borné. L'identité affichée provient du jeton Windows courant.

L'action d'ouverture utilise le verbe Windows `runas` pour le même EXE et des
arguments fixes. Windows gère l'UAC ; FILPRO ne reçoit aucun mot de passe. Un refus
1223 garde la fenêtre ouverte. Le relais contrôle les droits administrateur actifs
avant de poursuivre ; son argument seul ne donne aucun droit. Une session déjà
élevée passe directement à l'application après le clic. Les démarrages de santé
de mise à jour restent soumis aux contrôles du journal, du jeton de santé et du
pack ; ils exigent aussi un processus élevé. La démonstration explicite reste
isolée et ne demande pas d'élévation.

La composition graphique utilise les tokens et SVG existants. Deux panneaux sur
la fenêtre normale ; sous 720 px, une bande de marque compacte remplace le panneau
illustré. Les espacements s'adaptent à la hauteur, les comptes longs reviennent à la
ligne et le message UAC dispose d'une place stable. Les boutons restent accessibles
au clavier. Le compte, l'action, les erreurs et le pied de page tiennent à 640 × 440
sans défilement. Après un changement de message, de compte ou de taille, la page
demande une nouvelle mesure native sans transmettre de dimension. L’hôte mesure
le contenu avec un script local fixe et agrandit la fenêtre si nécessaire, dans
les limites de l’écran courant et en tenant compte de son échelle. Le recalcul
ne réduit pas la fenêtre et ne tourne pas en boucle. Les thèmes Windows clair, sombre et contraste élevé sont suivis.

Le compteur final de la simulation est aussi corrigé : une opération réussie termine
toutes ses étapes, au lieu d'afficher 9/10 sous un résultat à 100 %. Les résultats
partiels ne sont pas reclassés comme réussis.

## Vérification disponible à ce lot

- Compilation de l'hôte et de la simulation, zéro erreur et avertissement.
- Mac ARM64 et Windows x64 natif : trois contrats de politique d'entrée et neuf contrats SimulationWorld
  passent. Le test de réussite contrôle le compteur final.
- Chrome macOS : seize contrôles de connexion (rendu et hôte simulé), treize contrôles
  des paramètres reliés au vrai pont C# ; aucune erreur JavaScript sur les passes retenues.
- La connexion native et l'UAC de ce nouvel hôte restent à vérifier dans la VM.
  Les tests du navigateur ne sont pas présentés comme une preuve UAC.

Les preuves natives déjà obtenues sur `58f2267` restent conservées séparément :
sauvegarde/restauration de deux profils avec fichiers et registre, trois politiques
de conflits, sauvegardes préalables chiffrées, mauvaise clé sans écriture, annulation,
ruches déchargées, récupération indépendante et isolation de la démo.
La mise à jour 1.9.9 → 2.0.0 a confirmé son démarrage puis le rollback a rétabli
les 530 fichiers de 1.9.9 ; les réglages sont strictement identiques. La reprise
après interruption reste ouverte. Aucun reçu PROD n'est créé avec ce lot.

GitHub Actions au commit `58f2267` n'a exécuté aucune étape : le diagnostic de
facturation/plafond provient du run 40327549545. Les validations locales et Windows
restent distinctes de cette CI bloquée.
