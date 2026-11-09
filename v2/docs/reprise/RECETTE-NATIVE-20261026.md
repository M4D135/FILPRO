# Recette native du candidat 2.0.0 — 26 octobre 2026

Source applicative publiée et distribuée : `01917331d49b3e728cef121b406d2661b5653e5c`.
Les packs PROD et DEV sont assemblés ; aucune Release GitHub finale n'est publiée.
La qualification concerne les scénarios critiques ci-dessous. Elle ne constitue
pas une déclaration de parité V1 complète ni une matrice de tous les environnements.

## Connexion et présentation

La session Windows courante est affichée avant la composition métier. Le bouton
ouvre le même EXE avec le verbe Windows `runas` lorsque le jeton n'est pas élevé.
Le relais ne remplace jamais le contrôle effectif des droits. Aucun mot de passe
n'est reçu par la page. La fenêtre reprend le kit local, sans slogan ni ascenseur.
Après erreur, annulation ou changement de dimensions, un signal fermé demande une
mesure native fixe ; la hauteur augmente dans les limites de l'écran courant.

La recette Windows a révélé un double nettoyage après `Close()` puis `Dispose()`.
Le correctif rend la libération idempotente. Le parcours corrigé login → accueil
Windows → Paramètres → démonstration → session normale passe sur FILPRO-W11-TEST :
22 profils natifs, six fictifs, puis les mêmes 22 natifs. Démonstration et animations
sont dans Paramètres ; le badge « Poste réel » est absent du nouveau shell.
La technique et les corrections ont été transmises à DOMENIA2 et PESTO2 à la demande de David.

Limite explicite : LABO\Administrateur possède déjà un jeton élevé dans cette VM,
même avec une tâche interactive demandée au niveau limité. Aucun dialogue UAC de
consentement ou de refus n'a donc été exercé nativement. Les états erreur/annulation
et le recalcul sont couverts par le harnais navigateur, pas présentés comme un essai UAC Windows.

## Preuves et réutilisation des octets

Le socle avait passé 330 contrats sur Windows, puis les corrections ciblées AppData
et catalogue USMT ont passé respectivement 36 et 46 contrats Mac et Windows.
Le lot d'entrée ajoute trois contrats de politique de démarrage et neuf de simulation
sur Mac et Windows ; la politique est revérifiée après ajout du signal de mesure.
Seize contrôles de connexion navigateur et treize de Paramètres avec vrai pont C# passent.
Ces comptes se recouvrent ; aucune somme de tests distincts n'est inventée.

Les moteurs Core, Platform et Bridge du candidat sont identiques octet pour octet
aux assemblages des trois harnais ayant exécuté les scénarios natifs. Neuf comparaisons
confirment leur présence dans le nouvel EXE. La simulation modifiée est également
identique à celle des neuf contrats Windows. L'hôte et ses ressources modifiés ont
leur propre parcours natif, décrit ci-dessus. Le suffixe informatif du binaire conserve
le parent Git présent au moment de sa compilation ; le code compilé correspond au
commit 0191733, et la version produit/manifestes/offres est 2.0.0.

Sur deux profils locaux jetables désactivés : capture ScanState chiffrée, vérification,
restauration fichiers et registre VLC, politiques sauvegardé/actuel/coexistence,
sauvegardes préalables chiffrées, mauvaise clé sans écriture, annulation sans perte,
ruches déchargées, récupération indépendante et magasins sources préservés passent.
Le scénario natif de sauvegarde fictive a confirmé l'isolation ; le nouveau parcours
Paramètres confirme l'entrée et la sortie du fournisseur de démonstration.

## Mise à jour, retour arrière et interruption

La transaction `7430bf09e2a54a599a1af27b5445906f` a installé réellement 2.0.0 depuis
1.9.9, obtenu l'accusé de santé de l'interface puis atteint Committed. Le retour arrière
a rétabli les 530 fichiers de 1.9.9, en conservant la copie remplacée et les réglages.

La transaction `2bb14dfa9e3d4935adb8200eb074a286` utilise le candidat 0191733.
Le harnais a interrompu uniquement son processus de mise à jour à AwaitingHealth,
après lancement de la nouvelle application. Cette dernière a réellement affiché
l'accueil et produit son accusé de santé. Le journal reste inachevé, comme attendu.
Le bouton de retour arrière a ensuite restauré 1.9.9 et son accueil natif.
Les 530 fichiers restaurés et les 533 fichiers du candidat conservé ont été vérifiés
par SHA-256 ; configuration et source de mise à jour sont strictement identiques
avant, pendant et après ce scénario. Aucun accusé de santé n'a été fabriqué.

Les ZIP de protocole étaient des fixtures privées non publiées. Le pack PROD livré
est assemblé ensuite par l'outil original avec un reçu portant sur les mêmes 533
fichiers ; son manifeste est identique à celui vérifié dans la VM.

## Distribution et limites restantes

EXE SHA-256 : `74ae44321292b34b24140e5fe95379e1ab48eaa9ebe7ddb40bc8beb87ebccc3a`.
Reçu Windows SHA-256 : `a4365a26b95b0318f4c3a99347f0511ef189bc88c2b5e5eb28e6b21de8a1c9ab`.
À la racine des packs : seulement FILPRO.exe et wad. WebView2 fixe et USMT complets,
DEBLOQUER_TOUT.cmd sous wad/support ; sources et outils supplémentaires dans DEV.
Intégrité SHA-256, sans signature d'éditeur.

- PROD : `01b4b650baab178f3b62b7dd4df12d92f425bba7482b05b1514afd380a187857` (364258124 octets).
- DEV : `90cd0378b655c657d8a4b5f16664566fb453d0fcceae417e8ccd9c4cb9c894a4` (365423056 octets).

GitHub Actions run [40360487062](https://github.com/M4D135/FILPRO/actions/runs/40360487062)
n'a exécuté aucune étape : GitHub signale un paiement en échec ou un plafond de dépenses.
Les résultats locaux ne sont pas présentés comme une CI réussie. La mise à jour native
utilise une source dossier ; les transports GitHub/ZIP manuel ont leurs contrats propres,
ce compte rendu ne prétend pas les avoir tous rejoués dans la VM.

Décision de David pendant la reprise du 26 octobre : continuer sans GitHub Actions.
Le run `40365638760`, attaché au commit documentaire `bbc16a4`, présente encore le
même refus avant toute étape. La validation Windows locale reste le parcours retenu ;
une publication GitHub ne devra pas présenter ce run comme réussi.

Les gardes restent actives : restauration native d'un magasin V1 refusée sans preuve
excluant les composants Windows ; restauration MigApp refusée sans sauvegarde préalable
couvrant les paramètres machine. La récupération indépendante des fichiers fonctionne.
La parité V1 complète et une Release V2 finale ne sont donc pas annoncées.

## Périmètre confirmé pendant la reprise

David confirme « FILPRO = profil, on touche que les profils » et annule l'extension
machine envisagée. Aucun code de sauvegarde machine n'a été intégré. L'option MigApp
générale est retirée des écrans et refusée à la création du plan comme à l'entrée du
moteur natif. Les sélections AppData/HKCU restent disponibles. Le champ de configuration
historique reste lisible pour compatibilité, sans pouvoir autoriser une capture MigApp.
Les anciens magasins V1/MigApp gardent leur parcours de récupération indépendante ;
leur restauration complète n'est pas déclarée disponible. Les preuves et packs 0191733
ci-dessus décrivent le candidat antérieur, pas cette correction de périmètre.

Les magasins, clés et copies de retour arrière restent dans le dossier de qualification
sur la seule VM FILPRO-W11-TEST. Aucun changement sur une autre VM n'est revendiqué.

## Correction d'annulation issue de la recette profils

Le candidat `1ff7870` a réussi les captures et restaurations chiffrées sur deux profils, les trois politiques de conflit et le refus d'une mauvaise clé. Son contrôle d'annulation a détecté une ruche utilisateur encore chargée après l'arrêt forcé de ScanState pendant l'initialisation. Ce candidat n'est donc pas qualifié PROD.

Le correctif attend la fin de l'étape USMT au lieu de tuer le processus. La demande d'arrêt reste visible jusqu'à la sortie native et au nettoyage ; l'attente peut être longue. Aucun déchargement arbitraire de ruche utilisateur n'est ajouté au produit. Les sept tests ciblés du coordinateur et du nettoyage passent hors VM.

La validation native manquante à l'écriture de cette note a été faite ensuite sur `5a7bc5a` : annulation après démarrage USMT réel, ruches déchargées, sans Kill. Le complément de compatibilité a suivi sur la même VM ; son résultat et ses limites sont dans `docs/VALIDATION.md`, section « Complément du 26 octobre 2026 ».
