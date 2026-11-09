# FILPRO V2 — état de livraison et historique

## 9 novembre 2026 — 2.0.14 : avertissement du dossier modifiable retiré, inventaire non bloqué

Retours de David sur un poste réel (9 novembre 2026), après la 2.0.13 :

- **Dossier de FILPRO modifiable** : le bandeau jaune « Le compte ouvert sur ce poste peut
  modifier le dossier de FILPRO… » est retiré, à sa demande. La ligne grave correspondante
  disparaît aussi de la fenêtre « FILPRO — Confirmation Windows ». Le contrôle des droits
  qui l’alimentait n’existe plus : la capacité `packModifiable` et l’événement
  `pack.exposure` sont supprimés. Le conseil de placement reste affiché seulement quand
  l’affichage ne démarre pas.
- **Inventaire partiel à tort** : une clé sous `HKCU\Software`, ou un dossier AppData, dont
  le nom ne peut pas s’écrire dans une règle USMT (`[`, `]`, `/`, `*`, `?`, `"`, `<`, `>`, `|`,
  `:`, caractère de contrôle, nom d’appareil réservé, fin par une espace ou un point)
  rendait tout l’inventaire partiel. Les choix du profil ne pouvaient plus être
  enregistrés, et la préparation de sa sauvegarde était refusée. Un tel élément ne peut
  jamais être retenu, et la sauvegarde ne prend que les éléments cochés : il est
  maintenant tu, sans bloquer le profil. Les autres motifs (accès refusé, lien non suivi,
  bornes de nombre ou de durée, élément disparu) rendent toujours l’inventaire partiel.
- **Source de mise à jour vide** : « Mémoriser cette source » ou « Rechercher et vérifier
  l’offre » avec un chemin vide faisait passer la mise à jour en échec (« Action arrêtée ;
  consulter le journal avant de recommencer »). La page demande maintenant le chemin près
  du champ, sans appeler l’hôte. L’hôte refuse un chemin vide avant le verrou de mise à
  jour, et l’état de la mise à jour ne change pas.

Poussé sur `master` (`074c503`, `c478b88`, `8654aed`). Sur la demande de David
(« go tout terminer et tout corriger », 9 novembre 2026), la version passe à 2.0.14 pour
la CI Windows, la recette VM, le pack et la publication. Contrôles faits sur le Mac :
compilation sans avertissement ; 25 tests web ; suites `SituationAdmissionTests`,
`NativeWindowPolicyTests`, `LoadedRegistryDiscoveryTests`, `SoftwareFileDiscoveryTests`,
`ConfirmationSheetsTests`, `BridgeTests`, `UpdateSourcePreferenceTests`,
`UpdateTransactionTests` et `SimulationWorldTests` vertes. Les échecs propres à macOS de
`NativeConfirmationTests`, `SessionAccessTests` et `ConfirmationBindingTests` sont les
mêmes qu’avant, moins les deux tests retirés.

Publiée le 9 novembre 2026 : [v2.0.14](https://github.com/M4D135/FILPRO/releases/tag/v2.0.14)
(identifiant `439078646`, tag sur `9f8ca177`), ZIP `6cf2385b…` et `SHA256SUMS`, vérifiés
par l’API et un nouveau téléchargement. CI Windows `42602008887` verte (57 suites, 459
contrôles). Recette VM `F214FIXA` validée et reçu émis ; le détail est dans
`docs/VALIDATION.md`. Reste ouvert : un lien ou une jonction sous AppData rend toujours
l’inventaire partiel.

Après la publication, `4b34195` ajoute le test des noms de dossiers AppData non
représentables ; il échoue avec l’ancien comportement. Constat en l’écrivant, par
lecture du code et non reproduit sur Windows : `Path.GetFullPath` retire sous Windows le
point ou l’espace final d’un nom. Un dossier AppData nommé ainsi est donc contrôlé sous
un autre nom, introuvable, et rend l’inventaire partiel (« devenu absent »). Non corrigé.

## 9 novembre 2026 — dépôt GitHub recréé sur un seul commit

Décision de David (9 novembre 2026) : ne garder que la dernière version. M4D135/FILPRO
est recréé, public, à partir d’un seul commit, `e66757d1`. Son arbre reprend `8fe99e5f`
avec 11 remplacements de `CNAMTS\Administrateur` par `LABO\Administrateur` : 10 dans
`docs/VALIDATION.md` et 1 dans `docs/reprise/RECETTE-NATIVE-20261026.md`. La release
[v2.0.13](https://github.com/M4D135/FILPRO/releases/tag/v2.0.13) est la seule :
identifiant `438824426`, tag sur `e66757d1`, mêmes fichiers (ZIP `ad430349…` et
`SHA256SUMS`).

Les commits et les runs Actions cités dans ce document et dans `docs/` n’existent plus
sur GitHub, y compris `36e0d035` et le run `42529299194`, ni les releases antérieures à
la 2.0.13. Le pack 2.0.13 déclare `sourceCommit` `36e0d035`. Les sources de ce commit et
celles de `e66757d1` ne diffèrent que par trois documents absents du pack : `REPRISE.md`,
`docs/VALIDATION.md` et `docs/reprise/RECETTE-NATIVE-20261026.md`. Le reçu de
qualification, le `validation.json` de la CI et les preuves de recette restent privés,
hors du dépôt.

Tout travail repart d’un clone neuf. Sur le Mac, l’URL de push de l’ancien clone est
neutralisée. Ne jamais repousser une ancienne branche ni un ancien tag.

## 9 novembre 2026 — recette VM et publication de la 2.0.13 `36e0d035`

La CI Windows du commit de version est verte (`42529299194` : 57 suites, 460/460). La
recette VM complète du pack LIGHT est jouée sur une fixture neuve (run `F213FIXA`) :
natif, complément, Socle, démarrage, fichier en trop, démonstration, mise à jour réelle
0.0.1 → 2.0.13 depuis un dossier avec retour arrière, et reprise de l’ouvrier. Le détail
est dans `docs/VALIDATION.md`. Le validateur de recette exigeait encore la suite CI
`GitHubUpdateTransportTests`, supprimée avec la source GitHub. Il demande maintenant
`UpdateSourcePreferenceTests` et `ExecutionBridgeTests`, et tous les groupes passent. Le
reçu de qualification PROD-LIGHT est émis pour `36e0d035`.

Release publique [v2.0.13](https://github.com/M4D135/FILPRO/releases/tag/v2.0.13),
publiée sur décision de David (9 novembre 2026) d’abord sur `36e0d035` (identifiant
`438821123`), puis à l’identique sur `e66757d1` (identifiant `438824426`) à la
recréation du dépôt :

- `FILPRO-2.0.13-win-x64-PROD-LIGHT.zip`, 53 202 174 octets, SHA-256
  `ad430349589b67cc75152395394cf455f0b7722c1422e1369885ec66a810c250`.
  Ses 279 fichiers sont identiques à ceux du pack recetté ; EXE `ee9d3efa…`.
- `SHA256SUMS`, 103 octets.

Une 2.0.12 dont la source est GitHub Releases se voit proposer cette version. Après la
mise à jour, sa source devient « Dossier » : il faut la renseigner dans Paramètres.

## 8 novembre 2026, nuit — mise à jour intégrée sans GitHub, non publiée

Demande de David (8 novembre 2026) : FILPRO ne cherche plus ses mises à jour sur
GitHub. La source GitHub Releases, son client HTTP, le jeton de session et les routes
`updates.githubSession` et `updates.setGitHubToken` sont retirés. Restent le dossier
local/UNC avec `SHA256SUMS` et le ZIP manuel. Un ancien choix GitHub enregistré
(`kind` 0, sans dossier) est relu comme un dossier à renseigner.

Poussé sur master (`602d340`). La réécriture d’historique prévue pour GLPI est
annulée (David, 8 novembre 2026). Le dépôt est toutefois recréé le 9 novembre (voir
plus haut) : ces SHA ne sont plus publiés. CI Windows verte
(`42528798328` : 57 suites, 460/460). Sur décision de David, la version passe à 2.0.13
pour la recette VM et le pack ; la publication attend son accord.

## 8 novembre 2026, soir — recette VM du pack LIGHT `f256df62`

La CI Windows du commit final est verte (`42513511350` : 58 suites, 475/475). La recette
VM complète du pack LIGHT est jouée sur une fixture neuve : natif, complément, Socle,
démarrage, fichier en trop, démonstration, mise à jour réelle 0.0.1 → 2.0.12 avec
retour arrière, et reprise de l’ouvrier. Le détail est dans `docs/VALIDATION.md`. Le seul
groupe d’abord refusé venait d’un horodatage du kit de recette. Sur décision de David,
le validateur l’accepte maintenant, et tous les groupes passent. Le reçu de
qualification PROD-LIGHT est émis pour `f256df62`.

Release publique [v2.0.12](https://github.com/M4D135/FILPRO/releases/tag/v2.0.12),
identifiant `438528416`, tag et cible sur `f256df62`, publiée sur décision de David
(8 novembre 2026) :

- `FILPRO-2.0.12-win-x64-PROD-LIGHT.zip`, 53 212 681 octets, SHA-256
  `2c08bb31612980b56a298e4acf2762cbb76b436dbbf34a59d99c553f17803eae`.
  Ses 279 fichiers sont identiques à ceux du pack recetté ; EXE `cb58bb8c…`.
- `SHA256SUMS`, 103 octets.

La 2.0.11 PROD ne cherche que des packs `-win-x64-PROD.zip` : elle ne propose pas
cette version. On installe le pack PROD-LIGHT dans un nouveau dossier.

## 8 novembre 2026 — 2.0.12 en PROD-LIGHT seul, moteur du Socle Windows

Décision de David (8 novembre 2026), commune à Pesto 2, FILPRO 2, Domenia 2 et
GLPI : chaque outil ne publie plus que son pack LIGHT. Le moteur WebView2 fixe est
dans un pack additionnel commun, publié une seule fois : release
[webview2-156.0.4314.42](https://github.com/M4D135/socle-windows/releases/tag/webview2-156.0.4314.42)
de `M4D135/socle-windows` (ZIP `42dc4b13…`, index par fichier `a846d378…`).

FILPRO choisit d’abord Evergreen enregistré pour la machine, puis le Socle extrait
dans `wad/runtime/webview2`, admis seulement s’il reproduit exactement l’index
compilé (`src/Filpro.Platform/Updates/Socle/`). Sinon le message propose Evergreen
ou le lien du Socle, sans renvoyer vers un pack PROD. La préparation d’une mise à
jour LIGHT recopie et revérifie le Socle ; la version retirée garde sa copie. Les
index connus ne sont jamais retirés. Une autre version du moteur passe par une
nouvelle release du Socle, jamais par une copie dans ce dépôt. La qualification
PROD 2.0.12 est abandonnée ; la recette VM porte sur le pack LIGHT du commit final.

Quatre défauts des revues précédentes, corrigés avant ce commit final à la
demande de David (8 novembre 2026) :

- un nom de fichier contenant `[`, `]` ou `%` ne refuse plus la sauvegarde. Seuls
  un dossier de premier niveau à inclure et un élément à écarter doivent s’écrire
  dans une règle USMT ; leur refus nomme le chemin à renommer ;
- un dossier dont le marqueur `filpro-backup.json` dépasserait 259 caractères
  n’arrête plus la sauvegarde : ce n’est pas un magasin FILPRO ;
- Win32 5 de `RegLoadAppKeyW` ne monte plus la copie, puisque la racine resterait
  illisible ; une sonde HKU en accès refusé vaut montage présent et reste déchargeable ;
- chaque montage possédé tient `Global\FILPRO_V2_<guid>`, dont le propriétaire est
  Administrateurs. La lecture d’inventaire vivante d’une autre session n’est plus
  prise pour un résidu bloquant ; un nom disparu entre deux relevés non plus.

Les parties Windows (marqueur Global, homonyme d’un autre propriétaire, dossier de
249 caractères) ne sont exercées que par les contrats Windows.

## 2 novembre 2026 — préparation de PROD-LIGHT 2.0.12

La fabrication propose deux éditions de production. PROD-LIGHT utilise le runtime
WebView2 Evergreen x64 installé pour la machine et distribue les mêmes outils
USMT que PROD. Les mises à jour conservent l’édition installée. Les sources se
téléchargent depuis le tag GitHub de la version. Le dernier pack qualifié à ce
stade est le PROD 2.0.11 ; chaque édition 2.0.12 exige son propre reçu Windows.

## État du 31 octobre 2026 — 2.0.11 publiée, source `78c5de7c`

FILPRO 2.0.11 corrige quatre points sans changer l'interface : l'accord de la fenêtre
de confirmation est refusé une fois l'échéance de deux minutes atteinte ; l'accusé de
santé d'une mise à jour est lu même si la nouvelle version se ferme aussitôt ; un
`SHA256SUMS` non UTF-8 est refusé clairement ; la sortie du mode modal est garantie
après la fenêtre. EXE :
`3776ea2a9b43ecf824a4df2b3e9e80887b27b0d337999f1bd168a24069e9d4b4`. Run GitHub Windows
`41189660081` : 55 suites, 440 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST : mise à jour réelle 2.0.10 → 2.0.11
et retour arrière décidés dans la fenêtre ; 17 étapes natives (6 USMT réelles), 4
phases complémentaires, fichier en trop refusé, démonstration sans effet, reprise
`RollingBack`. Reçu Windows `status=passed`, SHA-256
`e449a263278eb4694169e99579338f77801f7a1454e425314074ac71d42af78e` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.11](https://github.com/M4D135/FILPRO/releases/tag/v2.0.11),
identifiant `429878550`, tag et cible sur `78c5de7c` :

- `FILPRO-2.0.11-win-x64-PROD.zip`, 364 728 570 octets, SHA-256
  `523652a19c146fb9b737462ee1c1574099faaf364c32abb3a0a677a7e7e565d7`.
- `FILPRO-2.0.11-win-x64-DEV.zip`, 368 924 799 octets, SHA-256
  `69b99cfa4d72f7f225373d4b38b06d8da36e00813fd6423eacb1875f81abea68`.

Reste à faire : les images de la vraie fenêtre en thèmes sombre et contraste
n'ont pas pu être prises sur le pack DEV (réglages par défaut réservés au PROD) ; à
reprendre sur une instance PROD. Points du poste réel inchangés.

## État du 30 octobre 2026 — 2.0.10 publiée, source `71de3c58`

FILPRO 2.0.10 donne à la fenêtre « FILPRO — Confirmation Windows » l'aspect de FILPRO :
logo FILPRO (barre de titre et en-tête), filet du dégradé de marque, polices Segoe UI
Variable (Cascadia Mono pour l'empreinte), valeurs dans des cadres arrondis, bouton
d'accord au dégradé une fois armé et gris lisible désarmé, Annuler en contour avec
anneau du focus, barre de titre aux couleurs (Windows 11) ; palette fermée compilée
(clair, sombre, contraste), thème lu sans effet de bord en une seconde, contraste élevé
de Windows en couleurs système ; armement, recouvrement, focus, clavier et refus
inchangés. EXE :
`9f32b414df1e8373c7d235a23ccb88b596b460077e4d06d3d4f347252d33db8c`. Run GitHub Windows
`41028767432` : 55 suites, 439 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST : mise à jour depuis la version publiée
et retour arrière décidés dans la fenêtre ; 17 étapes natives (6 USMT réelles), 4
phases complémentaires, démarrage sans écran d'entrée, Paramètres « Installée :
2.0.10 », fichier en trop refusé, démonstration sans destination. Reçu Windows
`status=passed`, SHA-256
`fcc2f37ac18a4272d5b9f329f4dd12f977ee28061611535f71deef87d5fe4b1d` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.10](https://github.com/M4D135/FILPRO/releases/tag/v2.0.10),
identifiant `428967911`, tag et cible sur `71de3c58` :

- `FILPRO-2.0.10-win-x64-PROD.zip`, 364 728 411 octets, SHA-256
  `86b30f5aabb43580d68b5bca139c6094edb628678d8ffacdfb8fc1ee10015e29`.
- `FILPRO-2.0.10-win-x64-DEV.zip`, 368 921 161 octets, SHA-256
  `40f21a48f7896c0023cfbcf1ccb6f8055a9859554d46c59b9579f166a064a0f3`.

Reste à confirmer sur le poste réel : l'aspect de la fenêtre en usage réel, thèmes
sombre et contraste compris, et les points déjà listés pour les versions précédentes.

## État du 30 octobre 2026 — 2.0.9 publiée, source `5e8fd5e0`

FILPRO 2.0.9 protège les sessions où la page tourne sous un autre compte que FILPRO
élevé (utilisateur standard, administrateur saisi dans l'UAC) ou sous un compte
indéterminé : mises à jour refusées, verrou de présence à sens unique (session Windows
verrouillée, déconnectée ou fermée, 15 min sans saisie, veille de plus de 2 min, 30 min
après l'ouverture, 3 refus en 10 min, case « Verrouiller FILPRO après le lancement »
cochée par défaut), sélecteur de fichiers fermé seul après 2 minutes et avertissement
quand le compte de la session peut modifier FILPRO ; sous le même compte, rien ne
change. EXE :
`2e2be930a9fc5f829c5ecdbea79bca55ce44e24d244a9ef7f9aa1c36842a95c9`. Run GitHub Windows
`40989228485` : 433 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST : mise à jour 2.0.8 → 2.0.9 et retour
arrière décidés dans la fenêtre ; en session « autre compte », la page masque les mises
à jour et signale le dossier modifiable, le sélecteur se ferme seul, le compte de la
session ne peut pas accepter la fenêtre, l'accord avec la case cochée verrouille FILPRO
sans arrêter la sauvegarde ni son suivi, une relance déverrouille et Win+L verrouille.
Reçu Windows `status=passed`, SHA-256
`f662e0f7e930af035847db32c733f3171ec207be7334274daa8428755a8fbad9` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.9](https://github.com/M4D135/FILPRO/releases/tag/v2.0.9),
identifiant `428624519`, tag et cible sur `5e8fd5e0` :

- `FILPRO-2.0.9-win-x64-PROD.zip`, 364 714 515 octets, SHA-256
  `a4e8c6188d5072ab18fdbdb20b05c6d0ddaad96306e6bf79d68351800c18afae`.
- `FILPRO-2.0.9-win-x64-DEV.zip`, 368 890 510 octets, SHA-256
  `a53d12872691223be80d892939738de3c234d321f4a3595460198812cea8bc2b`.

Reste à confirmer sur le poste réel : la fenêtre au clavier et à la souris, y compris
dans une session standard avec un administrateur saisi dans l'UAC (2.0.7, 2.0.8 et
2.0.9), et les verrous de présence non constatés sur la VM (couverts par les tests) :
inactivité, veille, âge de 30 min, 3 refus en 10 min, déconnexion, fermeture de session
et maintien du verrou après le déverrouillage de la session Windows.

## État du 30 octobre 2026 — 2.0.8 publiée, source `df83c2f0`

FILPRO 2.0.8 fait décider les dix opérations privilégiées dans une fenêtre Windows de
FILPRO élevé, « FILPRO — Confirmation Windows », et non plus dans la page, qui
s'exécute sous le compte de la session : fiche construite par FILPRO avec l'origine de
chaque information et des points d'attention, « Annuler » par défaut, acceptation
armée après 1,5 s et désactivée si la fenêtre est inactive ou recouverte, annulation
après 2 minutes sans réponse ; reçu de réparation et catalogue liés à leur empreinte ; startup hooks .NET
et variables `WEBVIEW2_*` neutralisés au démarrage. EXE :
`172aefc60dd022acf4341fb8ad8e59b4cf80683c86aca14702329d2cfeaa97cc`. Run GitHub Windows
`40965653416` : 408 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST : retour arrière de mise à jour décidé
dans la fenêtre ; un programme non élevé de la session n'a pu ni la piloter ni
l'accepter ; en session « autre compte », la fenêtre l'indique, le compte de la
session ne peut pas l'accepter et Échap la refuse sans rien lancer. Reçu Windows
`status=passed`, SHA-256
`bd3d5327cce28f9ddea3cbf0ac9043ef6f9b7259fa6aaac91f75bb032fcf5655` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.8](https://github.com/M4D135/FILPRO/releases/tag/v2.0.8),
identifiant `428523711`, tag et cible sur `df83c2f0` :

- `FILPRO-2.0.8-win-x64-PROD.zip`, 364 705 317 octets, SHA-256
  `1784388a3b5942429b2241a790cdb537eb382c82c32369f8d5521edc2ec39360`.
- `FILPRO-2.0.8-win-x64-DEV.zip`, 368 850 961 octets, SHA-256
  `b6b93dc1c47be36956e2231703ba47050ccb7fe8ec5c37e69a1ecff3d7c7e657`.

Reste à confirmer sur le poste réel : la fenêtre au clavier et à la souris, y compris
dans une session standard avec un administrateur saisi dans l'UAC (2.0.7 et 2.0.8).
Suite prévue, 2.0.9 : verrou de présence, refus des mises à jour sous un autre compte,
annulation automatique du sélecteur, case « Verrouiller FILPRO après le lancement » et
avertissement pour un pack modifiable par le compte de la session.

## État du 29 octobre 2026 — 2.0.7 publiée, source `1e459f30`

FILPRO 2.0.7 explique un affichage qui ne démarre pas quand FILPRO est élevé avec un
autre compte que celui de la session : les droits effectifs de ce compte sur le moteur
et l'interface du pack sont évalués par Authz, l'élément illisible est nommé avec le
conseil de placement (`C:\Program Files\FILPRO`), sinon un démarrage qui dépasse 25 s
est signalé ; rien n'interrompt le démarrage, et FILPRO se ferme désormais pendant un
démarrage bloqué. EXE :
`281e1f20060b480fbe76c03cfcf6ecef16e693f863f1d3f0095b60bc87dd5271`. Run GitHub
Windows `40903450388` : 357 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST, plus le croisement de comptes : moteur
lisible, accueil sans avertissement ; moteur illisible, dossier nommé et fermeture
possible. Reçu Windows `status=passed`, SHA-256
`e24060012e0e8091a25a0acd9bffa31f7f89113ff483119b928c3d4128131656` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.7](https://github.com/M4D135/FILPRO/releases/tag/v2.0.7),
identifiant `428130691`, tag et cible sur `1e459f30` :

- `FILPRO-2.0.7-win-x64-PROD.zip`, 364 658 417 octets,
  SHA-256 `582abafe5fb607b1f7ba8f226e1dc6da10cb0c183e7fbf0ce0e1116a7b9e205f`.
- `FILPRO-2.0.7-win-x64-DEV.zip`, 368 715 678 octets,
  SHA-256 `dd49c26a717fa75eaa5649bd540fd9f901fa454e8c4a351c5633b011dbd19f4a`.

Reste à confirmer sur le poste réel : ouverture avec le compte administrateur saisi
dans l'UAC, pack extrait dans `C:\Program Files\FILPRO`. L'avertissement des 25 s
n'a pas été observé en réel (couvert par les tests).

## État du 29 octobre 2026 — 2.0.6 publiée, source `141f3ca3`

FILPRO 2.0.6 durcit le dossier d'affichage de 2.0.5 (utilisateur standard, UAC d'un
autre administrateur) : listes d'accès lues brutes sur le dossier ouvert, bits
génériques convertis, lecture seule pour tout autre compte, dossier final accepté
seulement avec la protection exacte posée par FILPRO ; un dossier créé par la 2.0.5
reste accepté. Le conseil d'échec désigne un dossier que le compte de la session peut
lire sans le modifier (`C:\Program Files\FILPRO`). EXE :
`c0713a379932c47a8bbbb82d7bebd46b8b46bd5f96a616ca14ece12a59190f79`. Run GitHub
Windows `40865532403` : 353 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST, plus le croisement de comptes : la
2.0.5 publiée crée le dossier, la 2.0.6 le réutilise et ouvre l'accueil, puis refuse le
même dossier doté d'un GENERIC_WRITE héritable. Reçu Windows `status=passed`, SHA-256
`bb66c89eaccb467872839854fc4c2e23670c2e7f38ce6032bff7930ca584be7a` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.6](https://github.com/M4D135/FILPRO/releases/tag/v2.0.6),
identifiant `427845713`, tag et cible sur `141f3ca3` :

- `FILPRO-2.0.6-win-x64-PROD.zip`, 364 653 876 octets,
  SHA-256 `1e6a52c2c81fe75b705fc12b376094fecb3c6ea88b1b97e3879df0abe338e89d`.
- `FILPRO-2.0.6-win-x64-DEV.zip`, 368 699 688 octets,
  SHA-256 `7fcd7ed2a81a43b727ff5d61e4e735193597e452a1fb0d9bee6a5919c681711e`.

Reste à faire : si le moteur du pack est illisible pour le compte de la session,
Windows affiche son propre refus et FILPRO reste sur l'initialisation (2.0.5 idem) ;
à confirmer sur le poste réel avec le pack extrait dans `C:\Program Files\FILPRO`.

## État du 29 octobre 2026 — 2.0.5 publiée, source `6292fff9`

FILPRO 2.0.5 corrige le démarrage quand un utilisateur standard ouvre FILPRO en
saisissant un autre compte administrateur dans l'UAC (« Création du répertoire de
données impossible ») : WebView2 tournant sous le compte de la session, son profil
d'affichage est alors placé sous ProgramData, dans un dossier réservé à SYSTEM, aux
Administrateurs et à ce compte. EXE :
`b39010148f5d0fb5af168e3b2553049cf65e1703d5d8b2112dbf3954a644753a`. Run GitHub
Windows `40843957062` : 346 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST : défaut reproduit avec la 2.0.4 et
corrigé avec la 2.0.5 (session standard, UAC d'un autre administrateur), **17 étapes
natives**, **4 phases complémentaires**, démarrage élevé direct, refus nommé avec
`desktop.ini`, démonstration sans effet, mise à jour réelle 2.0.4 → 2.0.5 puis retour
arrière dans la même transaction, reprise par le worker 2.0.5 d'un état
`RollingBack` préparé. Reçu Windows `status=passed`, SHA-256
`296c468c344ec62c6863d2cb9bb6e33c87b52751aae7d5b3bff077daa0e3f080` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.5](https://github.com/M4D135/FILPRO/releases/tag/v2.0.5),
identifiant `427684182`, tag et cible sur `6292fff9` :

- `FILPRO-2.0.5-win-x64-PROD.zip`, 364 649 740 octets,
  SHA-256 `23cd2f512e6fe45a4b86addb7f288b732d5eb69a626a6225df95d23a898a803f`.
- `FILPRO-2.0.5-win-x64-DEV.zip`, 368 688 480 octets,
  SHA-256 `026ae219fd4680c795f39797d8b3f386886f515387f3477f4fa9c727ab590097`.

Reste à confirmer sur le poste réel : ouverture de la 2.0.5 avec le compte
administrateur saisi dans l'UAC, FILPRO extrait dans un dossier lisible par le
compte standard (par exemple `C:\FILPRO`). Limite connue : l'affichage tourne alors
sous le compte standard ; ne saisir un administrateur que dans la session d'un
utilisateur de confiance.

## État du 29 octobre 2026 — 2.0.4 publiée, source `e981b00d`

FILPRO 2.0.4 nomme l'élément en trop quand le pack est extrait au milieu d'autres
fichiers (par exemple sur le Bureau) au lieu d'accuser le manifeste ; le refus est
inchangé. L'interface suit DOMENIA (icônes immobiles, pastilles et encadrés sans
collision) et le repli du menu est harmonisé avec DOMENIA, PESTO et GLPI (libellé
visible menu déplié, icône seule replié, toujours en bas). EXE :
`41d2b2a410d035d5935187aea4f08a9f808b11a5057159a1a6ed2362fb763f67`. Run GitHub
Windows `40823090614` : 342 contrôles réussis.

Recette VM complète réussie sur FILPRO-W11-TEST : **17 étapes natives**, **4 phases
complémentaires**, démarrage élevé direct, refus nommé avec `desktop.ini` à côté de
l'EXE, démonstration sans effet, mise à jour réelle 2.0.3 → 2.0.4 puis retour
arrière dans la même transaction, reprise par le worker 2.0.4 d'un état
`RollingBack` préparé. Reçu Windows `status=passed`, SHA-256
`7b2f1b26e21f32f1518bc8d1e7761d28f8055692746869f95c7a730ffe264708` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.4](https://github.com/M4D135/FILPRO/releases/tag/v2.0.4),
identifiant `427538279`, tag et cible sur `e981b00d` :

- `FILPRO-2.0.4-win-x64-PROD.zip`, 364 644 153 octets,
  SHA-256 `8f955b69a309376daefcdc3bc5434eafaba980088dbc6cc44d54f5e9ba7e74eb`.
- `FILPRO-2.0.4-win-x64-DEV.zip`, 368 673 389 octets,
  SHA-256 `7be431b803016e9abe3bbeaddfd33c45409c0212c7169e7a3b2e7fbd2335bd63`.

Reste à confirmer sur un poste réel : lancement normal sans le lien « Aller au
contenu » visible, et le cas d'origine (zip extrait sur le Bureau) avec le PROD.

## État du 28 octobre 2026 — 2.0.3 publiée, source `395bb12c`

FILPRO 2.0.3 corrige l'ouverture sur un poste où un utilisateur standard fournit
à l'UAC un autre compte administrateur. L'UAC est demandée d'emblée ; FILPRO
s'ouvre ensuite directement sous le compte fourni. Si `%LOCALAPPDATA%\FILPRO` de
ce compte est inutilisable, le processus élevé emploie
`ProgramData\FILPRO-V2\Users\<SID>`, protégé comme dans FILPRO 1 ; sélections et
copies privées de ruches suivent ce dossier. EXE :
`1811bcda64401215bdcbf1bd5e99d84cf02cc5a0f428d2dfde663f4690f66c18`. Run GitHub
Windows `40725809121` : 341 contrôles réussis.

Recette VM réussie sur FILPRO-W11-TEST : parcours UAC (annulation, autre
administrateur, dossier de secours), **17 étapes natives** et **4 phases
complémentaires**, démarrage élevé direct et démonstration sans effet, mise à
jour réelle 2.0.2 → 2.0.3 jusqu'à `Committed` puis retour arrière dans la même
transaction, reprise par le worker 2.0.3 d'un état `RollingBack` préparé (pas une
coupure électrique). Reçu Windows `status=passed`, SHA-256
`c787df707030819b9f26ae6b4b8e060046b1a655b56b5cd7225d8f0f74f768b3` ; détails dans
[VALIDATION](docs/VALIDATION.md).

Release publique [v2.0.3](https://github.com/M4D135/FILPRO/releases/tag/v2.0.3),
identifiant `427087235`, tag et cible sur `395bb12c` :

- `FILPRO-2.0.3-win-x64-PROD.zip`, 364 642 673 octets,
  SHA-256 `005461ee0d0df8bfc800736b8892777fe0815557941db31a3397ec6478f66926`.
- `FILPRO-2.0.3-win-x64-DEV.zip`, 368 667 322 octets,
  SHA-256 `2e1773f8fb8ff6913a7d75ec87a7d7c31b8b52e89e0d3b0629ff778de3357f5d`.

Le probe réel du transport GitHub a réussi sans installation. Les tâches
FILPRO-203 sont retirées et les réglages de test restaurés ; le profil chargé du
compte de test supprimé `FTADM203` disparaîtra au redémarrage de la VM. L'erreur
Edge exacte du poste d'origine n'a pas été reproduite en VM : la confirmation sur
ce poste reste à faire. Un commit documentaire ultérieur ne change ni la source
`395bb12c` des binaires, ni le tag, ni la qualification.

## État du 28 octobre 2026 — 2.0.2 publiée, source `625af6f2`

Les binaires Windows x64 de FILPRO 2.0.2 portent la source
`625af6f280f69a64cebda9836c30b0f8d4c45ebb`. EXE :
`08d74bb8015dd6e2dc6b4c6b17b7599b2e981876cd1d01098f3413ea06c90f73`.
Le run GitHub Windows `40701216007` a réussi 335 contrôles ; il ne vaut pas
recette VM.

La recette VM fraîche de cette source a réussi sur FILPRO-W11-TEST :
**17 étapes natives** et **4 phases complémentaires** sur les deux profils
jetables. Le démarrage, les Paramètres et l'aller-retour de démonstration sont
vérifiés, avec un état réel inchangé. La mise à jour réelle 2.0.1 → 2.0.2 est
allée jusqu'à `Committed`, puis son retour arrière vers 2.0.1 dans la même
transaction. Deux invocations réelles du worker 2.0.2 ont terminé, puis confirmé
sans changement, un état `RollingBack` préparé ; ce n'est pas une coupure
électrique réelle. Aucune preuve 2.0.1 n'est réutilisée.

Le reçu Windows de cette source est `status=passed` ; il établit les huit
conditions critiques décrites dans [VALIDATION](docs/VALIDATION.md). Son SHA-256 est
`a3b4f6f3dd25b805949aa9a65268c87e1ce0fde2819487bc66834220dd0f2647`.
Trois défauts des seuls scripts de laboratoire ont été corrigés avant
exécution, ou après un refus conservé ; le produit n'a pas changé.

Release publique [v2.0.2](https://github.com/M4D135/FILPRO/releases/tag/v2.0.2),
identifiant `426860370`, tag et cible sur `625af6f2` :

- `FILPRO-2.0.2-win-x64-PROD.zip`, 364 634 319 octets,
  SHA-256 `a752efa9483cc8ad332c7f5f65f20bc2e43acdbc4a9d80a6a305d99fa229a36d`.
- `FILPRO-2.0.2-win-x64-DEV.zip`, 368 649 864 octets,
  SHA-256 `2d33de965c3b7cb9291296473b0c2cc1e95e432c0d8c4d2e32fcef2216b3042d`.

Le probe réel du transport GitHub a réussi, sans installation : version 2.0.2,
source exacte, taille et SHA-256 du PROD téléchargé identiques. La release
2.0.1 et ses trois assets restent inchangés.

Fin des essais vérifiée : les sept tâches FILPRO-202 sont retirées et
`update-source.json` a retrouvé ses octets d'avant les tests, après copie de
sécurité ; `configuration-v2.json` était déjà d'origine. Un préflight en lecture
seule, après nettoyage, confirme deux profils déchargés, aucune ruche, aucun
processus FILPRO/USMT et aucun profil `.old-filpro-*`. Les racines de test, la
baseline 2.0.1 et toutes les preuves sont conservées.

Le périmètre reste les profils : fichiers personnels, AppData et HKCU. MigApp
général et la restauration machine restent refusés ; aucune parité V1 exhaustive
ni nouvelle recette UAC n'est revendiquée pour 2.0.2. Le complément UAC natif
2.0.1 ci-dessous garde ses propres limites. Un commit documentaire ultérieur ne
change ni la source `625af6f2` des binaires, ni le tag, ni la qualification.

## Lot 2.0.2 — identité et cohérence visuelle

La version des sources passe à 2.0.2 pour distinguer ce lot de la release 2.0.1
publiée. L’icône des cartes de profils est intégrée à l’EXE, à la connexion et
à la fenêtre principale ; les badges du kit gardent un glyphe monochrome adapté
au contraste. Le maître couleur, le logo, les tailles Windows et leur provenance
sont conservés dans `v2/assets/branding/`.

Deux protections DOMENIA permettent aux identifiants longs de rester contenus et
sélectionnables. Le comparateur commun embarqué vérifie hors ligne les 28 fichiers
du kit déclarés ; ses limites figurent dans [UI-KIT](docs/UI-KIT.md). Le moteur de
profils, les confirmations et les animations validées restent inchangés.

Ordre demandé par David : code terminé et poussé, puis tests Windows GitHub,
recette VM à la toute fin. Ce lot doit recevoir ses propres preuves avant
publication. Les preuves et archives 2.0.1 ci-dessous restent liées à leur source ;
elles ne qualifient pas automatiquement le nouveau binaire.

## État du 28 octobre 2026 — 2.0.1, source `8fc09056`

Les binaires Windows x64 de FILPRO 2.0.1 portent la source
`8fc09056e6627d7055494e79f1b821fd6aa37d87`. Le look et les animations validés par
David sont conservés. Les corrections portent sur l'estimation USMT non
compressée, la fermeture des handles de ruche avant renommage de profil,
la conservation des erreurs dans le journal technique et l'attente du verrou
du worker, passée de 15 secondes à 2 minutes.

Les preuves de cette source établissent **30 contrôles C# ciblés**, **17 étapes
natives Windows** sur deux profils jetables et **4 phases complémentaires**
réussis. Le démarrage, l'aller-retour de démonstration avec état réel inchangé,
la mise à jour 2.0.0 → 2.0.1 et son retour à 2.0.0 sont vérifiés. Les deux packs
et les réglages sont conservés. Deux invocations réelles du worker 2.0.1 ont
aussi terminé puis confirmé sans changement un état `RollingBack` préparé
après déplacement final ; ce n'est pas une coupure électrique réelle.

Le reçu Windows du candidat est `status=passed`, limité aux scénarios critiques
décrits dans [VALIDATION](docs/VALIDATION.md). Son SHA-256 est
`3ec0f02ccb918a9c659e4297e37e3ffd87ad4637e06eca9fd85f0b5ec61f96c3`.
Les archives assemblées sont :

- `FILPRO-2.0.1-win-x64-PROD.zip`, 364 258 477 octets,
  SHA-256 `17703be14cd05a4543704ebf7b8d911b3f76c414fa3b819c8f736a69a709ebe1`.
- `FILPRO-2.0.1-win-x64-DEV.zip`, 365 431 884 octets,
  SHA-256 `bd24d9f14e642844069e9238ae2f17a1a60496fcc2fbca9f23e6dd186c5c9651`.

David a autorisé explicitement la diffusion publique le 28 octobre 2026.
Release publique [v2.0.1](https://github.com/M4D135/FILPRO/releases/tag/v2.0.1)
publiée, identifiant `426611047`, tag et cible sur `8fc09056` ; les trois assets
PROD, DEV et `SHA256SUMS` ont été vérifiés.
Le probe réel du transport GitHub a réussi, sans installation : version 2.0.1,
source exacte, PROD téléchargé de 364 258 477 octets et SHA-256 identique à celui
indiqué ci-dessus.
Fin des essais vérifiée : neuf tâches de test et les deux tâches de nettoyage
retirées, réglages initiaux retrouvés, signatures des profils et état réel
identiques au snapshot préalable. Les sauvegardes sont conservées ; le processus
était déjà absent, aucun processus n’a été fermé par le nettoyage. Un premier
essai du script de laboratoire a échoué sur un argument nul PowerShell ; son
journal est conservé. Le script corrigé a réussi, sans changement du produit.
Le dernier constat ne relève plus de processus FILPRO ni de tâche FILPRO-201 ; la session
Administrateur reste active.

### Complément UAC natif après publication — 28 octobre 2026

L’annulation et l’acceptation de la demande Windows réelle sont maintenant
exercées sur le même EXE 2.0.1, source `8fc09056`, sans changement de binaire
ni du reçu PROD. L’annulation conserve la fenêtre Connexion, le même processus
non élevé et ses deux boutons actifs. L’acceptation démarre un nouveau processus
élevé sur le même chemin, avec `--poste --filpro-elevated`, puis affiche l’accueil.

La session utilise l’Administrateur intégré du domaine. Le lancement de test
emploie `runas /trustlevel:0x20000` : `TokenElevation=false`, type `Default`,
intégrité RID 12288. Ce n’est pas une recette de jeton UAC lié ou de compte
standard à intégrité moyenne. L’invite réelle demande des identifiants sur le
bureau sécurisé ; elle n’est pas un simple consentement Oui/Non. Aucun compte
ni stratégie UAC n’a été modifié. Les preuves et limites sont détaillées dans
[VALIDATION](docs/VALIDATION.md).

Nettoyage UAC confirmé : les cinq tâches temporaires sont retirées, aucun
processus FILPRO ne reste, et les profils, réglages et opérations sont strictement
identiques au relevé préalable, hors horodatage. Les empreintes de l’EXE et du
manifeste sont conservées ; les stratégies UAC sont inchangées. Le pack de test
et les preuves sont préservés, sans fermeture forcée ; la connexion SSH dédiée
a été fermée. Ce complément reste distinct du reçu PROD original inchangé.

Pour lancer la version PROD, extraire toute l'archive puis ouvrir `FILPRO.exe`,
en gardant le dossier `wad` à côté. Le commit documentaire ultérieur ne change
pas la source `8fc09056` des binaires, du tag ni de leur qualification.

FILPRO reste limité aux profils : fichiers personnels, AppData et HKCU. MigApp
général et restauration native V1 restent refusés ; aucune parité V1 exhaustive
n'est revendiquée. La récupération indépendante et les magasins V1 chiffrés
sans clé ne sont pas retestés par cette reprise. Le complément UAC ci-dessus
conserve ses limites de jeton ; GitHub Actions écarté sur décision de David. Le contrôle du verrou
ne démontre pas la cause historique d'un échec à 15 secondes.

Les sections suivantes conservent les archives à leur date ; elles ne décrivent
pas l'état 2.0.1 ci-dessus. La release 2.0.0 et ses preuves sont conservées.


## Archive de recette native — candidat 0191733

Packs PROD et DEV assemblés avec le reçu des scénarios Windows critiques ; aucune
Release finale. Connexion, aller-retour de démonstration dans les
Paramètres et reprise après interruption réelle du worker sont vérifiés. Les copies
et réglages sont préservés. Lire [le bilan et ses limites](docs/reprise/RECETTE-NATIVE-20261026.md).
La session testée est déjà élevée : refus UAC natif non exercé. GitHub Actions reste
bloqué par la facturation. Les gardes V1/MigApp et la parité incomplète restent explicites.
Les sections suivantes décrivent leurs états historiques, sans remplacer les preuves.

## Entrée Windows et présentation — 26 octobre 2026

David a demandé une entrée V2 avec la session Windows courante et l'UAC, sans
mot de passe applicatif, slogan ni défilement. Le nouvel hôte de connexion est
séparé de la composition métier. Démonstration et animations se trouvent uniquement
dans Paramètres ; le badge « Poste réel » a été retiré de l'interface.
Lire [le lot et sa validation](docs/reprise/ENTREE-WINDOWS-20261026.md).

Sur les octets de `58f2267`, les scénarios natifs multi-profils, registre, conflits,
chiffrement, annulation, récupération indépendante et isolation démo ont passé.
La mise à jour réelle 1.9.9 → 2.0.0 et son retour à 1.9.9 ont également passé,
avec les deux copies vérifiées et les réglages conservés. L'interruption de
l'assistant reste à tester ; le nouvel écran d'entrée demande sa propre validation
Windows. Les anciens bilans ci-dessous conservent leur périmètre historique.

## Candidate 2.0.0 — code et contrats validés, recette VM restante

Reprise sur `46a3999` : journal persistant, configuration optionnelle invalide
signalée au démarrage, verrou machine, source de mise à jour mémorisée, patches de
modèles logiciels, guides, support et reconstruction DEV. Les tests Windows ont
révélé puis validé trois corrections d'E/S natives : absence de document,
verrouillage des dossiers et remplacement atomique avec lecteur concurrent.

**330 contrôles C# Windows, 103 contrôles C# Mac, 10 contrôles d'interface,
7 tests d'assemblage et 31 tests de la garde passent.** Lire la portée et les
limites dans [le rapport courant](docs/reprise/FINALISATION-CANDIDATE-20261026.md).
La publication, la recette VM et les packs PROD restent des étapes distinctes.
La restauration native V1 et MigApp reste bloquée pour les raisons documentées.

## Reprise Codex — retour arrière après interruption, 26 octobre 2026

Le clone propre a été avancé sans réinitialisation vers `origin/v2` au commit
`9e790cd163e7d320449123067661856d19c11eed`. Le premier lot suivant corrige la
reprise d'une mise à jour lorsque l'ancienne installation est déjà remise en
place mais que le journal n'a pas enregistré la fin : action proposée dans les
paramètres, contrôle sous verrou, version et octets vérifiés, finalisation
idempotente sans nouveau déplacement. Voir le [rapport du lot](docs/reprise/REPRISE-ROLLBACK-20261026.md)
et les résultats distincts Mac/Windows dans [VALIDATION](docs/VALIDATION.md).
La V2 reste à qualifier sur ses parcours métier et ses packs réels.

## État historique — intégration du 26 octobre 2026

Les sources Spark **jusqu’au lot 004A seulement** sont présentes sur `v2` depuis
`5818fd17d1fab5b63868d3ecc7a4c4893bbc115b`. Aucun autre développement privé Spark
n’est présenté comme récupéré. La reprise coordonnée part de `716812c` et conserve
le kit DOMENIA documenté dans [KIT-PROVENANCE](docs/reprise/KIT-PROVENANCE.md).
Le dernier état distant publié avant le présent lot est
`564f86cf1bb8ff5992e47187086a8c1db2313371`, après `00039d6` et `984f937`.
Le présent lot est identifié par le commit contenant ce document ; le reçu de
garde fourni séparément porte son SHA exact et ses compteurs.

Les exécutants C# Windows, le pont, les sept écrans, la configuration, les
mises à jour transactionnelles et le monde fictif interactif sont maintenant
implémentés et raccordés. Leur présence dans les sources ne constitue pas une
qualification sous Windows. Lire la [finalisation des sources](docs/reprise/FINALISATION-SOURCES-20261026.md),
l’[architecture actuelle](ARCHITECTURE.md), [la portée des validations](docs/VALIDATION.md)
et l’[inventaire de parité](docs/PARITE.json). Le premier compte rendu
[d’intégration](docs/reprise/INTEGRATION-MOTEURS-20261026.md) reste un état historique du lot concerné.

Les changements de ce dernier lot sont présents dans les sources :

- **Catalogage confirmé et récupération indépendante** des fichiers V1 : une tâche
  longue distincte prépare le catalogue, puis le suivi et la page Récupérer relisent
  son résultat côté moteur. Le SHA-256 mesuré décrit l’état actuel du contenu V1,
  sans inventer une empreinte historique absente.
- **Restauration native V1 toujours bloquée** : les règles, la configuration, les
  identités source et la version USMT sont contrôlées, mais ni un XML hashé ni le
  catalogue de fichiers ne prouvent exhaustivement l’exclusion des composants
  Windows capturés. Le catalogage ne lève pas ce refus de LoadState.
- **Magasins non compressés V2** : index SHA-256 et comparaison de l’ensemble exact
  des fichiers, avec au plus 100 000 fichiers, 16 384 dossiers et une profondeur
  de 128 par arborescence USMT vérifiée. Le chemin V1 permet la récupération de
  fichiers après indexation confirmée, sans autoriser LoadState.
- **Reprise depuis un reçu de réparation** : choix explicite, confirmation et
  revalidation du reçu et des données préservées ; en démonstration, seuls les
  reçus fictifs de la session sont proposés.
- **MigApp** reste une option explicite de capture. La restauration native de ces
  magasins est refusée avec avertissement, car les paramètres machine éventuels
  ne sont pas couverts par le préalable des profils ; la récupération indépendante
  reste disponible.

Le client GitHub accepte un jeton manuel facultatif limité à la session, sans
persistance, export ni transmission aux hôtes de redirection. Les contrôles du
transport utilisent des réponses fictives ; aucune Release réelle du dépôt privé
ni mise à jour réseau de bout en bout n’a été testée.

Les résultats portables antérieurs restent rattachés à leurs instantanés dans
[VALIDATION](docs/VALIDATION.md). Les preuves finales de compilation, contrôles C#
et parcours ciblés du dernier lot sont consignées séparément, sans transférer
automatiquement une réussite à des modifications ultérieures. Le runner Windows
de GitHub n’a exécuté aucune étape à cause de la restriction de facturation du
compte. Aucun pack PROD/DEV final, aucune Release et aucun remplacement des packs
V1 ne sont autorisés par ces preuves.

## Historique des sources Spark transférées — lots 001 à 004A

Les sections historiques qui suivent décrivent les lots au moment de leur transfert. Un rapport
Spark READY indique des sources écrites ; il ne constitue pas une validation Windows.

Reprise du développement de FILPRO V2 sur le modèle de DOMENIA V2, confiée au Spark local via OpenCode natif. Le Mac est
coordinateur. Ce fichier tient à jour l'état de la reprise ; `docs/PARITE.json` porte l'inventaire structuré des familles
et routes. Les consignes `SPARK-LOT-*`, les rapports `SPARK-READY-*` des lots 001,
002, 003A à 003E et 004A, ainsi que l'état initial `BASELINE.json`, sont conservés
dans [l'historique Git au commit f4e8892](https://github.com/M4D135/FILPRO/tree/f4e8892ccd5ed80551cbdd4b895af857b9ae7de1/v2/docs/reprise).
Ces documents de travail ont été retirés de la branche courante le 1er novembre
2026 ; les rapports de qualification et de provenance restent dans le dépôt.

## Cadre

- Périmètre d'écriture : **`v2/` uniquement**. `wad/`, `packs/`, `DOC/` et `_reference/` sont en **lecture seule**.
- Références : kit DOMENIA V2 (`_reference/domenia/v2/`, commit `77805257`) et FILPRO V1 (`wad/`). Aucune référence modifiée.
- Cible : C# .NET 10 autonome Windows x64, WebView2 fixe (même version que DOMENIA), HTML/CSS/JS en modules ES natifs,
  ressources locales uniquement. Aucune dépendance téléchargée, aucun runtime système copié.
- Le worker ne fait **aucun commit/push**. Compilation et validation **sous Windows** dans un lot ultérieur.

## Routes et familles (résumé honnête — détail dans `docs/PARITE.json`)

| Route | Libellé | Section | Statut lots 001-003A | Modules source V1 (lecture seule) |
|---|---|---|---|---|
| `accueil` | Accueil | Pilotage | **simulé** (fournisseur de démonstration) | `FILPRO.Experience.ps1`, `FILPRO.Insight.ps1`, `FILPRO.Workspace.ps1` |
| `suivi` | Suivi en direct | Pilotage | structure (indisponible volontairement) | `FILPRO.Jobs.ps1`, `FILPRO.Progress.ps1`, `FILPRO.Worker.ps1`, `FILPRO.Scheduling.ps1` |
| `journal` | Journal | Pilotage | structure (indisponible volontairement) | `FILPRO.Reports.ps1`, `FILPRO.Support.ps1`, `FILPRO.Reviews.ps1` |
| `analyser` | Analyser / réparer | Opérations | **simulé / natif en lecture seule** ; moteur de réparation absent | `FILPRO.Profiles.ps1`, `FILPRO.Insight.ps1`, `FILPRO.Selections.ps1`, `FILPRO.Catalogue.ps1` |
| `sauvegarder` | Sauvegarder (Sauvegarder / Restaurer) | Opérations | structure + sélection, aperçu des conflits, **contrat typé du plan multi-profils validé par fonction pure** et **contrat typé du plan de restauration validé par fonction pure (correspondances, portées/conflits, sauvegarde préalable, anciens magasins)** ; **plan de sauvegarde branché sur le pont (`sauvegarder.plan`) et confirmation typée (`sauvegarder.confirmPlan`, refusée faute de moteur)** ; **plan de restauration branché sur le pont (`sauvegarder.restorePlan`, conflits par portée relus) et confirmation typée (`sauvegarder.confirmRestorePlan`, refusée faute de moteur)** ; **construction sûre des arguments USMT depuis le plan par fonction pure (`UsmtArguments.Build`, liste fermée de jetons pour `ArgumentList`, jamais de shell)** ; moteurs d'écriture absents | `FILPRO.FileVault.ps1`, `FILPRO.FileApply.ps1`, `FILPRO.Planning.ps1`, `FILPRO.Destination.ps1`, `FILPRO.RestorePolicy.ps1`, `FILPRO.Usmt.ps1`, `FILPRO.Safeguards.ps1` |
| `recuperer` | Récupérer des fichiers | Opérations | structure (indisponible volontairement) | `FILPRO.Recovery.ps1`, `FILPRO.Recovery.Plan.ps1`, `FILPRO.FileVault.ps1` |
| `parametres` | Paramètres | (bas) | structure (indisponible volontairement) | `FILPRO.Configuration.ps1`, `FILPRO.Policy.ps1`, `FILPRO.RestorePolicy.ps1`, `FILPRO.Theme.ps1` |

Familles V1 à inventorier écran par écran (statut détaillé dans `docs/PARITE.json`) : profils et sessions,
analyser/réparer ensemble, sauvegarder/restaurer, logiciels (fichiers + registre + modèles communs), suivi des tâches,
récupération, journal, paramètres, démonstration.

## Invariants de restauration à préserver (prouvés en V1)

Confirmation avant préparation · sauvegarde préalable avant restauration · politiques de conflits par portée ·
multi-profils · clés de chiffrement jamais journalisées · hives du registre déchargées dans un `finally` · reprise et
annulation honnêtes. **Un arrêt demandé n'est pas un arrêt achevé.** Ces invariants sont déjà énoncés dans l'écran
Sauvegarder et dans le contrat du pont ; leur exécution réelle viendra avec le moteur.

## Décisions du lot 001

1. **Mode par défaut = démonstration.** En l'absence de choix explicite, l'application retombe honnêtement en démonstration
   (`ModeResolver`). `--simulation` / `FILPRO_SIMULATION=1` forcent la démonstration ;
   `FILPRO_SIMULATION=0` demande le poste réel. Depuis le lot 002, les lecteurs poste réels sont câblés en lecture seule ;
   les opérations d'écriture restent refusées (`write.unavailable`).
2. **Pont fermé.** Seules les opérations déclarées sont appelables. En démonstration : `Write` refusée
   (`simulation.readOnly`), `NetworkRead` refusée sauf marquée servie par le fournisseur fictif. Aucune commande
   arbitraire ni PowerShell libre depuis le JS.
3. **WebView2 verrouillé** sur `https://filpro.local/`, CSP `default-src 'self'` + `connect-src 'none'`, navigation et
   nouvelles fenêtres bloquées, DevTools/contextuel coupés en PROD, profil de données séparé en démonstration.
4. **Kit DOMENIA repris verbatim** (CSS design system + composants génériques + `ui.js`/`icons.js`/`bridge.js`/`router.js`),
   avec 2 lignes adaptées seulement (préfixe de stockage, titre). Traçabilité complète dans
   `docs/reprise/KIT-PROVENANCE.md`.
5. **Aucune donnée fictive présentée comme réelle** : bandeau de démonstration permanent, puce de mode, cartes marquées
   « démonstration », écrans non portés qui annoncent leur indisponibilité.

## Lot 002 — Profils et sélection des logiciels (24 octobre 2026)

Portage des lecteurs et de la sélection, en **lecture seule uniquement** (aucune écriture système dans ce lot).

- **Contrats typés** dans `Filpro.Core/Poste/` et `Filpro.Core/Software/` : `ProfileCandidate` (SID, compte, chargement
  tri-état, sessions), `WindowsSession`/`WindowsSessionInventory`/`SessionEvidence` (WTS), `SoftwareItem`/`SoftwareGroup`/
  `SoftwareSizeSummary`/`SoftwareInventory` (fichiers + registre), `LastSelection`, `RestorePolicy`/`RestoreScope`.
- **Logique pure** reprise de la V1 et testable sans OS : `SessionEvidenceReader` (dérivation WTS + tri), `SoftwareCatalog`
  (identité logiciel, regroupement fichiers+registre, recommandations ensemble, mesure des tailles honnête),
  `RestoreConflictPolicies` (correctif des conflits : coexistence par défaut, décision obligatoire, refus des chevauchements).
- **Fournisseurs séparés** (`IFilproPosteSource`) : natif Windows `Filpro.Platform/WindowsPosteSource` (P/Invoke borné
  advapi32/wtsapi32/kernel32 + `System.Security.Principal.Windows`, aucune dépendance ajoutée, **aucun montage de ruche**,
  jeton WTS jamais exposé) et fictif `Filpro.Simulation/SimulationPosteSource`. Ce que le natif n'établit pas (chargement,
  registre d'un autre compte, dernière sélection) reste **inconnu**, jamais inventé.
- **Écrans branchés** : `analyser.js` (profils, sessions, sélection groupée, dernière sélection) et `sauvegarder.js`
  (sélection à sauvegarder + aperçu des politiques de conflit). Le moteur de réparation/sauvegarde/restauration reste absent.
- **Modules du pont** : `AnalyserModule` (`analyser.profiles|software|lastSelection`) et `SauvegarderModule`
  (`sauvegarder.profiles|software|conflictPolicies`), tous en `NetworkRead` servis en démonstration.

## Micro-lot 003A — contrat du plan de sauvegarde multi-profils (25 octobre 2026)

Le contrat typé et **une seule fonction pure** de création/validation du plan, sans moteur et sans écran.

- **Contrats** dans `Filpro.Core/Backup/BackupPlanModels.cs` : `BackupPlanRequest`, `BackupProfileSelection` (SID, compte,
  chemin, chargement tri-état, fichiers + clés retenus), `BackupPlan`, `BackupSpaceRequirement`/`BackupSpaceState`,
  `BackupExclusion`/`BackupExclusionKind`/`BackupExclusionOrigin`.
- **Fonction pure** `BackupPlan.Create(request, planId, createdUtc)` (`Filpro.Core/Backup/BackupPlan.cs`) : aucune E/S,
  aucune horloge, aucun GUID, aucun registre, aucune sonde de support. Elle refuse la **sélection vide**, le compte non
  explicite ou dupliqué, le SID mal formé, le profil **Entra**, le profil **chargé sans VSS**, le chargement inconnu hors
  démonstration, la **destination** relative ou placée dans un profil source, et la **clé de chiffrement** absente, dans un
  profil source, dans le magasin ou combinée à la non-compression (règles V1 : `Get-FilproBackupAccounts`,
  `Assert-FilproExplicitAccounts`, `Get-FilproBackupPreflight`, `Get-FilproCryptoArguments`).
- **Taille inconnue explicite** : `Space.State = Inconnu`, octets et horodatage de mesure à `null`,
  `HasUnknownSpace` et `RequiresManualSpaceAcknowledgement` à `true` — la copie reste bloquée tant que le moteur n'a pas
  mesuré (aucun seuil inventé, comme en V1).
- **Exclusions annoncées** : celles déclarées par l'opérateur (justifiées, non dupliquées, jamais présentées comme des
  règles) plus les limites constantes du plan (flux nommés, applications, ruches, dossiers vides, magasins voisins).
- **Clé** : le plan ne porte que son chemin ; le contenu n'est ni lu, ni affiché, ni journalisé.
- **Hors périmètre de ce micro-lot** : aucun module du pont, aucun écran, aucun moteur, aucune mesure réelle, aucune
  empreinte/validation de fraîcheur du plan (le `planId` et l'horodatage viennent de l'appelant).

## Micro-lot 003B — contrat du plan de restauration (25 octobre 2026)

Le contrat typé et **une seule fonction pure** de validation du plan de restauration, sans moteur et sans écran.

- **Contrats** dans `Filpro.Core/Restore/RestorePlanModels.cs` : `RestorePlanRequest`, `RestoreStoreInfo` (magasin source,
  `Ready`/`Legacy`/`Partial`/`Simulated`/`Encrypted`, comptes du manifeste, poste source, version USMT),
  `RestoreAccountMapping`, `RestoreSourceProfile`, `RestoreTargetProfile` (SID, compte, chemin, chargement tri-état),
  `PriorBackupRequirement` + `PriorBackupState`, `RestoreMappingResolution`, `RestoreIdentityPair`, `RestorePlanReview`.
- **Fonction pure** `RestorePlan.Validate(request, planId, createdUtc)` (`Filpro.Core/Restore/RestorePlan.cs`) : aucune
  E/S, aucune horloge, aucun GUID, aucune résolution de compte, aucun registre. Elle **réutilise
  `RestoreConflictPolicies`** pour les portées et les politiques de conflits (aucune seconde implémentation des
  chevauchements). Codes de refus stables : `restore.plan`, `restore.selection`, `restore.mapping`, `restore.target`,
  `restore.store`, `restore.prior` (+ `conflict.policy` repris de `RestoreConflictPolicies`).
- **Correspondances de comptes** (règles V1 `Resolve-FilproAccountMappings`) : cible par défaut = source, refus de la
  **correspondance en double**, refus de la **fusion** de plusieurs comptes vers une même destination, refus d'une
  correspondance portant sur un **compte non sélectionné**, comptes source et cible explicites et uniques, identité
  **Entra** refusée.
- **Profils cibles** (règles V1 `Assert-FilproRestoreAccounts`) : SID de destination en `S-1-5-21-*` exigé, profil cible
  **chargé refusé**, chargement **inconnu** refusé hors démonstration (averti en démonstration), le **compte qui exécute
  FILPRO** ne peut pas être une destination, cible hors correspondance refusée.
- **Sauvegarde préalable explicite** (règles V1 `New-FilproDestinationSnapshot`, `Get-FilproBackupEvidence`) : exigée dès
  qu'un profil cible existe, dossier **hors du magasin à restaurer** et **hors des profils de destination**, clé
  indépendante requise hors démonstration ; le plan **ne déclare jamais** la sauvegarde préalable vérifiée (laissé au
  moteur) et distingue `Declaree` (non contrôlée) de `Identifiee` (dernier contrôle natif = information enregistrée).
- **Compatibilité anciens magasins explicite** (règles V1 `Get-FilproUsmtStoreInfo`, `New-FilproRestorePlan`) : un
  magasin **ancien** (`Legacy`) exige la reconnaissance explicite des limites ; un ancien magasin **ne fournit ni SID ni
  chemin source** — le plan refuse qu'on emprunte ceux de la cible. Magasin non `Ready` refusé ; **mélange
  simulation/réel** refusé ; compte source **absent du manifeste** refusé ; magasin **partiel** exige reconnaissance ;
  magasin **chiffré** exige le chemin de sa clé (contenu jamais porté ni journalisé).
- **Taille inconnue explicite** : `HasUnknownSpace` et `RequiresManualSpaceAcknowledgement` à `true` (aucune mesure,
  aucun seuil inventé, comme en V1).
- **Hors périmètre de ce micro-lot** : aucun module du pont, aucun écran, aucun moteur, aucune exécution USMT/LoadState,
  aucune résolution réelle de compte ni contrôle de droits/plateforme/USMT (appartiennent au moteur).

## Micro-lot 003C — persistance de la sélection logiciels (25 octobre 2026)

Une **fonction de sauvegarde atomique** de la sélection (fichiers + registre + modèles communs) et un **lecteur symétrique
minimal**, sans moteur, sans écran, sans pont, et **sans aucune modification de profil Windows**.

- **Codec pur** `Filpro.Core/Software/SoftwareSelectionCodec.cs` : contrats `SelectionPreferences` (options mémorisées,
  reprises des `Options` V1) et `SoftwareSelectionDocument` (schéma, nature, horodatage fourni par l'appelant, fichiers,
  clés, préférences). `Encode` canonise (tri par portée puis chemin, dédoublonnage, assainissement des libellés), valide la
  **forme fermée** puis écrit un JSON canonique via `Utf8JsonWriter` ; `Decode` relit et revalide via `JsonDocument`
  (aucune sérialisation par réflexion, aucune dépendance ajoutée). Règles V1 reprises (`Test-FilproRelativePath`,
  `Test-FilproRegistryRelativePath`, `Assert-FilproLastBackupSelection`) : traversée `..`, chemin absolu, lettre de lecteur,
  nom d'appareil réservé, deux-points dans une clé, propriété interdite/absente, doublon, borne de 500 éléments refusés.
  **Aucun E/S dans `Filpro.Core`** : le codec ne manipule que des octets en mémoire.
- **Persistance** `Filpro.Platform/SoftwareSelectionStore.cs` : `Save` est la seule écriture — sérialise et valide
  **entièrement en mémoire avant toute écriture**, puis publie par **remplacement atomique** (fichier temporaire exclusif
  `FileMode.CreateNew`/`FileShare.None`, `Flush(flushToDisk: true)`, `File.Move` avec reprise sur conflit de partage
  32/33), fidèle à `Write-FilproAtomicJson` (V1) ; refus des liens/jonctions (`Assert-FilproNoReparsePath`), longueur de
  chemin bornée à 259, création du dossier parent, nettoyage du temporaire en `finally` sans transformer un enregistrement
  publié en échec. `Read` est le lecteur symétrique : **absent, illisible ou de forme refusée → `null`, jamais d'exception**.
- **Préférences préservées** : `Save` sans préférences explicites **conserve le bloc `preferences` déjà présent** dans le
  document (plutôt que de le réinitialiser) ; un appel explicite ou `preserveExistingPreferences: false` reprend la main.
- **Honnêteté et invariants** : le **SID n'est jamais sérialisé** (il sert de clé de stockage côté fournisseur poste) ;
  aucune taille, recommandation ni état « connu » inventé (le fournisseur les recalcule) ; l'écriture **ne touche aucun
  profil Windows, aucune ruche, aucun autre fichier** — uniquement le document confié.
- **Hors périmètre de ce micro-lot** : aucun module du pont, aucun écran, aucun branchement de `WindowsPosteSource` ni de
  `IFilproPosteSource` (le magasin reste à câbler côté moteur) ; aucune protection ACL (`Protect-FilproFile` V1) ni
  journalisation, laissées au moteur ; aucune mesure réelle.

## Micro-lot 003D — parcours de confirmation du plan de sauvegarde (25 octobre 2026)

Branchement du plan de sauvegarde sur le pont **typé** et sur l'écran Sauvegarder, avec **confirmation avant toute
préparation longue**, erreurs et indisponibilités honnêtes. Aucune sauvegarde exécutée, aucune écriture, aucune mesure.

- **Correction de cohérence (CS0101 du lot 003A)** : le contrat `record BackupPlan` est **renommé `BackupPlanReview`**
  (`Filpro.Core/Backup/BackupPlanModels.cs`), comme `RestorePlanReview` en 003B, pour lever le doublon de nom avec la
  fabrique pure `static class BackupPlan`. `BackupPlan.Create` renvoie désormais `BackupPlanReview`. Sans ce renommage,
  `Filpro.Core` (donc le pont qui branche le plan) ne compilait pas. Logique de la fonction pure inchangée.
- **Nouvelles opérations du pont** (`Filpro.Bridge/Operations/SauvegarderModule.cs`) :
  - `sauvegarder.plan` — classe `NetworkRead`, **servie en démonstration**. Relit les profils et la sélection cochés chez
    le fournisseur poste (le JavaScript ne fournit que les SID retenus, la destination et les options), puis appelle la
    **fonction pure** `BackupPlan.Create` (le pont fournit `planId` en 32 hex et `createdUtc` depuis l'horloge injectée).
    Renvoie l'**aperçu canonisé** : profils, destination, chiffrement, **taille explicitement inconnue**, exclusions
    annoncées, avertissements, mentions d'honnêteté. **Aucune préparation longue, aucune exécution.**
  - `sauvegarder.confirmPlan` — classe **`Write`**. La **garde d'admission la refuse avant son gestionnaire** dans les deux
    modes (`simulation.readOnly` en démonstration, `write.unavailable` en poste réel) : rien n'est préparé ni exécuté. Son
    gestionnaire exige l'**accusé de capacité** (`acknowledgeCapacity`) puis refuse par `feature.unavailable` — seconde
    barrière explicite tant que le moteur d'écriture est absent. La confirmation est donc **demandée avant** toute
    préparation longue, et l'**indisponibilité est honnête côté serveur**, pas seulement côté interface.
- **Aides d'arguments** (`Filpro.Bridge/BridgeArguments.cs`) : `GetStringArray` (tableau obligatoire de textes non vides,
  **borné** à `BackupPlan.MaxProfiles` pour éviter toute boucle longue avant confirmation) et `GetOptionalBoolean`.
- **Écran Sauvegarder** (`web/js/pages/sauvegarder.js`) : carte « Destination et options » (destination absolue, VSS,
  chiffrement + fichier clé), bouton **« Préparer une sauvegarde »** qui appelle `sauvegarder.plan` puis ouvre un dialogue
  de **confirmation** rendant le plan (profils, destination, taille inconnue, exclusions, avertissements) avec **case
  d'accusé de capacité obligatoire** — le bouton « Confirmer la préparation » reste désactivé tant qu'elle n'est pas
  cochée. La confirmation appelle `sauvegarder.confirmPlan` et **affiche le refus honnête** du pont. L'onglet Restaurer
  garde son dialogue de préparation (moteur absent).
- **Prévisualisation** (`web/js/mock-poste.js`, `web/js/mock.js`) : miroir de `BackupPlan.Create` (mêmes formes camelCase,
  taille inconnue, exclusions annoncées, mêmes refus) et refus de `sauvegarder.confirmPlan` en `simulation.readOnly`.
- **Hors périmètre de ce micro-lot** : aucune exécution de sauvegarde, aucune mesure réelle, aucune empreinte/validation de
  fraîcheur du plan à la confirmation (le `planId` est fourni par le pont, non re-contrôlé faute de moteur), aucune
  sélection multi-profils à cases multiples (le plan est préparé pour le profil sélectionné), aucun branchement du plan de
  restauration à l'écran Restaurer.

## Micro-lot 003E — parcours de confirmation du plan de restauration (25 octobre 2026)

Branchement du plan de restauration sur le pont **typé** et sur l'écran Restaurer, avec **confirmation avant toute
préparation longue** et **conflits par portée relus**. Aucune restauration exécutée, aucune écriture, aucune mesure.

- **Nouvelles opérations du pont** (`Filpro.Bridge/Operations/SauvegarderModule.cs`) :
  - `sauvegarder.restorePlan` — classe `NetworkRead`, **servie en démonstration**. Les **profils cibles sont relus chez le
    fournisseur poste** (le JavaScript ne fournit que les SID : compte, chemin et chargement tri-état viennent du
    fournisseur, un SID inconnu est refusé `restore.target`) et les **portées de conflits sont relues** chez le même
    fournisseur pour **un seul** profil cible (la préparation ne lit pas les groupes de tous les profils avant
    confirmation). Le pont fournit `planId` (32 hex) et `createdUtc` (horloge injectée), puis appelle la **fonction pure**
    `RestorePlan.Validate` et renvoie un `RestorePlanPreview` : plan canonisé + déclaration du magasin + limites.
    **Aucune préparation longue, aucune exécution.**
  - `sauvegarder.confirmRestorePlan` — classe **`Write`**. La **garde d'admission la refuse avant son gestionnaire** dans
    les deux modes (`simulation.readOnly`, `write.unavailable`) : rien n'est préparé ni exécuté. Son gestionnaire exige
    l'identifiant de plan (`restore.plan`), puis **trois accusés** — capacité (`restore.plan`, besoin d'espace inconnu),
    **sauvegarde préalable** (`restore.prior`, le plan ne la déclare jamais vérifiée) et **revue des correspondances de
    comptes** (`restore.target`) — et refuse par `feature.unavailable` : seconde barrière explicite tant que le moteur est
    absent.
  - Le pont **ne pose jamais l'exploitabilité du magasin** : `RestoreStoreInfo.Ready` vient d'une case cochée par
    l'opérateur (obligatoire à l'appel) et le `Status` porte la mention « aucun manifeste relu par FILPRO dans ce lot ».
    Faute de lecteur de manifeste, `SourceProfiles` reste `null` : **aucun SID ni chemin source n'est emprunté à la cible**
    (règle V1), la revue des identités ne montre que les comptes.
- **Nouveau contrat d'aperçu** (`Filpro.Bridge/RestorePlanPreview.cs`) : `RestorePlanPreview(Plan, Store, ConflictNotice,
  Limitations)` et `RestoreStoreDeclaration` — l'aperçu rendu à l'écran sépare ce qui vient du **plan canonisé** de ce qui
  n'est **que déclaré** (magasin) ou **lu chez le fournisseur poste** (portées de conflits, monde fictif en démonstration).
- **Aides d'arguments** (`Filpro.Bridge/BridgeArguments.cs`) : `GetOptionalStringArray` (tableau optionnel de textes non
  vides, borné, vide admis — une absence explicite n'est pas une valeur inventée) et `GetObjectArray` (tableau d'objets
  **fermés** borné, pour les correspondances et les décisions de conflit).
- **Conflits par portée** : aucune seconde implémentation — les décisions (`keepBoth` / `keepCurrent` / `useSaved`) sont
  validées par `RestoreConflictPolicies` **depuis la fonction pure** (une décision par groupe affiché, chevauchements de
  portées refusés `conflict.policy`, résumé `ConflictSummary` repris de la V1).
- **Écran Restaurer** (`web/js/pages/sauvegarder.js`) : magasin déclaré (chemin, prêt, ancien/partiel avec **reconnaissances
  obligatoires**, chiffré + fichier clé, USMT, poste source, comptes du manifeste), comptes source et correspondance
  facultative, **sélection du profil cible relu** (SID, chemin, chargement), sauvegarde préalable (dossier, clé, identifiée
  ou déclarée) et **sélecteur de politique par portée** pour chaque groupe de réglages. « Préparer une restauration »
  appelle `sauvegarder.restorePlan` puis ouvre le dialogue de confirmation (correspondances, cible, préalable jamais
  déclarée vérifié, résumé des conflits, taille inconnue, limites) avec **trois cases d'accusé obligatoires** conditionnant
  le bouton ; la confirmation appelle `sauvegarder.confirmRestorePlan` et **affiche le refus honnête** du pont.
- **Prévisualisation** (`web/js/mock-poste.js`, `web/js/mock.js`) : miroir de `RestorePlan.Validate` (mêmes formes camelCase,
  mêmes codes `restore.*` et `conflict.policy`, taille inconnue, magasin déclaré) et refus de `sauvegarder.confirmRestorePlan`
  en `simulation.readOnly`.
- **Étiquettes des politiques** : la table d'étiquettes est rendue insensible à la casse, pour qu'une décision
  `keepCurrent` ou `useSaved` n'affiche plus l'étiquette de la coexistence par défaut (bug d'affichage de l'aperçu 003D).
- **Hors périmètre de ce micro-lot** : aucune exécution de restauration ni LoadState, aucun lecteur de manifeste du magasin
  (état **déclaré**, jamais relu), aucune mesure réelle, aucune empreinte/validation de fraîcheur du plan à la confirmation,
  aucun contrôle du compte exécutant FILPRO (non exposé par le fournisseur poste), pas de cases multi-profils côté écran.

## Micro-lot 004A — construction sûre des arguments USMT à partir du plan typé (25 octobre 2026)

**Une seule fonction C#** de construction des arguments USMT à partir du plan validé, sans shell ni commande libre, fidèle à
la V1 (`Get-FilproUsmtArguments`, `Get-FilproCryptoArguments`, `Assert-FilproExplicitAccounts`,
`Resolve-FilproAccountMappings`). Aucun processus lancé, aucune E/S, aucune exécution USMT.

- **Contrats** dans `Filpro.Core/Usmt/UsmtModels.cs` : `UsmtAction` (`Scan`/`Estimate`/`Load`), `UsmtAccountMapping`,
  `UsmtInvocation` (exécutable attendu, **liste fermée des jetons**, chemins de journaux, mention d'honnêteté) et
  `UsmtArgumentRequest`. Les adaptateurs `UsmtArgumentRequest.ForBackup` / `ForRestore` tirent du **plan validé** les
  comptes, les options (non-compression, poursuite après erreur, VSS, chiffrement), le chemin de la clé et les
  correspondances ; le moteur ne fournit que les chemins d'exécution (magasin, dossier des journaux, règles, configuration,
  fichier d'estimation).
- **Fonction pure** `UsmtArguments.Build(request)` (`Filpro.Core/Usmt/UsmtArguments.cs`) : aucune E/S, aucune horloge, aucun
  GUID, aucun processus, **aucun shell**. Elle renvoie une **liste de jetons** à passer telle quelle à
  `ProcessStartInfo.ArgumentList` — jamais une chaîne de commande libre : un chemin avec espace reste **un seul jeton**,
  aucun caractère n'est interprété. Ordre et gardes de la V1 reproduits : magasin d'abord, `/i:` des règles, `/config:`,
  `/nocompress`, `/c`, `/v:5`, `/l:` et `/progress:` **obligatoirement hors du magasin**, puis pour Scan/Estimate
  `/localonly`, `/ue:*`, `/efs:abort`, un `/ui:` par compte et `/vsc` seulement si VSS demandée ; pour Load `/ue:*`, un
  `/ui:` par compte, un `/ue:` par compte exclu et un `/mu:` par correspondance modifiée ; `/p:` exigé pour l'estimation ;
  `/listfiles:` pour la sauvegarde seule ; chiffrement borné (`/encrypt:AES_256` en Scan, `/decrypt:AES_256` en Load,
  `/keyfile:` — jamais avec la non-compression, clé hors du lot) ; **jamais de `/o`** (un magasin n'écrase jamais un autre).
- **Gardes et codes de refus stables** : `usmt.plan` (action inconnue, journaux dans le magasin, estimation sans fichier),
  `usmt.selection` (sélection vide, compte non explicite ou dupliqué, compte à la fois inclus et exclu, compte exclu non
  explicite, correspondance sur compte non sélectionné / en double / **fusion détectée sur l'ensemble des comptes** comme
  `Resolve-FilproAccountMappings`), `usmt.store` (magasin, journaux, règles, configuration, estimation non absolus ou trop
  nombreux), `usmt.crypto` (non-compression incompatible, clé absente ou dans le lot).
- **Sûreté** : l'exécutable n'est jamais résolu depuis une variable d'environnement (le moteur le prend dans le dossier
  USMT vérifié de la distribution, comme `Get-UsmtRoot` V1) ; la clé n'est portée que par son **chemin**, jamais son
  contenu ; existence, contenu, droits de la clé, disponibilité USMT et contrôle de plateforme restent à la charge du
  moteur. La mention du plan rappelle qu'**un arrêt demandé n'est pas un arrêt achevé**.
- **Hors périmètre de ce micro-lot** : aucune exécution de scanstate/loadstate/usmtutils, aucun branchement au pont ni à un
  écran, aucune résolution réelle de compte ni traduction SID (appartiennent au moteur), aucun `usmtutils /verify` (relève
  du moteur), aucune mesure. La non-compression et la poursuite après erreur d'une restauration dépendent du manifeste du
  magasin : le moteur les renseigne (le plan ne les porte pas).

## Prochain lot recommandé

- **Lecteur de magasin** (`IFilproStoreSource` ou extension du fournisseur poste) : relire le manifeste d'un magasin FILPRO
  (comptes, SID/chemins sources, statut d'exploitabilité, version USMT, marque `Legacy`/`Partial`/`Encrypted`) pour que
  `sauvegarder.restorePlan` ne repose plus sur une déclaration de l'opérateur — en démonstration, magasin fictif séparé ;
  côté natif, lecture seule bornée, aucune exécution USMT.
- **Objet d'approbation lié à l'empreinte du plan** à la confirmation (sauvegarde et restauration) : magasin de plans côté
  moteur, recontrôle de la fraîcheur et de l'empreinte, accusés adossés à un état réellement contrôlé.
- Compléter le parcours de confirmation du plan de sauvegarde quand le moteur existe : sélection multi-profils à cases,
  recontrôle de la **fraîcheur et de l'empreinte** du plan à la confirmation (le `planId` fourni par le pont est à
  re-valider), mesure réelle du besoin d'espace levant l'accusé de capacité.
- Exposer le **compte qui exécute FILPRO** au pont pour que la fonction pure refuse cette destination (garde V1 déjà écrite
  dans `RestorePlan.Validate`, paramètre `CurrentAccountSid` encore `null`).
- Moteur poste réel minimal en écriture : analyse/réparation avec sauvegarde préalable, plan confirmé, reçu et relecture.
- Suivi en direct (progression par opération, arrêt demandé ≠ arrêt achevé) et journal réel.
- Brancher réellement Sauvegarder/Restaurer sur le moteur (montage/démontage des hives dans un `finally`, clés jamais
   journalisées). La construction des arguments USMT est déjà écrite et testée (`UsmtArguments.Build`, micro-lot 004A) : il
   reste à l'appeler depuis le moteur, résoudre l'exécutable depuis le dossier USMT vérifié, passer la liste à
   `ProcessStartInfo.ArgumentList` (jamais de shell) et interpréter le code de retour et les journaux USMT.
- Catalogue PARITE écran par écran (fonctions V1 → statut) pour les familles restantes.

## Vérifications à faire sous Windows (hors lots 001-004A)

- `dotnet build Filpro.slnx -c Release` puis `dotnet run --project src/Filpro.App -- --simulation`.
- Vérifier le verrouillage WebView2 (aucune requête hors `filpro.local`, DevTools/contextuel coupés en Release).
- Vérifier le pont : une opération inconnue est refusée et tracée ; aucune écriture système en démonstration.
- Vérifier le parcours de confirmation du plan (démonstration) : « Préparer une sauvegarde » affiche l'aperçu canonisé
  (taille inconnue, exclusions annoncées) ; un profil chargé sans VSS est refusé (`backup.selection`) ; la confirmation est
  **refusée par la garde d'admission** (`simulation.readOnly`) sans rien exécuter ; la case d'accusé de capacité conditionne
  le bouton « Confirmer la préparation ».
- Vérifier le parcours de confirmation de la **restauration** (démonstration) : sans case « magasin déclaré prêt », la
  préparation est refusée ; un profil cible chargé est refusé (`restore.target`) ; une destination existante sans dossier de
  sauvegarde préalable est refusée (`restore.prior`) ; un compte absent du manifeste déclaré est refusé (`restore.store`) ;
  une décision de conflit qui ne couvre pas tous les groupes est refusée (`conflict.policy`) ; l'aperçu affiche la revue des
  identités **sans SID source** et le besoin d'espace inconnu ; les **trois accusés** conditionnent le bouton
  « Confirmer la préparation » ; la confirmation est **refusée par la garde d'admission** (`simulation.readOnly`) sans rien
  exécuter.
- Vérifier le lecteur natif (`FILPRO_SIMULATION=0`) : profils/SID/compte, sessions WTS, chargement tri-état, AppData lus,
  **registre d'un autre compte non lu** (annoncé), **aucune écriture ni montage de ruche** ; `sauvegarder.confirmPlan` et
  `sauvegarder.confirmRestorePlan` y sont refusées en `write.unavailable`.
- Vérifier la construction des arguments USMT (`UsmtArgumentsTests`, fonction pure) : ordre des jetons conforme à la V1,
   journaux refusés dans le magasin, `/p:` exigé à l'estimation, chiffrement refusé avec la non-compression et clé refusée
   dans le lot, **aucun jeton `/o`**, un chemin avec espace tenu comme **un seul jeton** (preuve qu'aucune chaîne shell
   n'est produite). Lors du branchement du moteur, passer la liste à `ProcessStartInfo.ArgumentList` et résoudre l'exécutable
   depuis le dossier USMT vérifié, jamais depuis une variable d'environnement.
- Exécuter `Filpro.Tests` (lanceur maison) : `SessionEvidenceTests`, `SoftwareCatalogTests`, `ConflictPolicyTests`,
   `PosteSourceTests`, `BackupPlanTests`, `SauvegarderPlanTests`, `SauvegarderRestorePlanTests`, `RestorePlanTests`,
   `SoftwareSelectionCodecTests`, `SoftwareSelectionStoreTests`, `UsmtArgumentsTests`, plus les tests du lot 001. Vérifier que
  `SoftwareSelectionStoreTests` n'écrit que dans un dossier temporaire et qu'un échec de sérialisation laisse le document
  précédent intact (aucun fichier `.tmp` résiduel).
