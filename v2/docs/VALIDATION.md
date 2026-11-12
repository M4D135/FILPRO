# Validation FILPRO V2

## Recette du 12 novembre 2026 — 2.0.18 PROD-LIGHT, source `6c8980f1`

Le candidat provient de `6c8980f1d02ac71ab09021d7e8663215fc290887` : ZIP
`c30d39ab7ca73f2df490bf46eab528b9a105838ece55084419c033b53535f492`, 286 fichiers,
EXE `7a8e43563c0c6a41c53f7784402524570196160ab330424a7c1a86ec7264844b`.
La base jetable 0.0.1 est compilée depuis `27fee58bc032d84a0568f83c96639a3d4393007d`
(même code, version 0.0.1) ; elle reste privée (ZIP
`6af484a00c269e3e08a12b97216ed9d988c0b7a38d5c0ba89c85782cf84b9e58`, EXE `6ccbd06a…`).
CI Windows [`43196906324`](https://github.com/M4D135/FILPRO/actions/runs/43196906324)
verte : 57 suites, 469 contrôles réussis, aucun ignoré, commit exact prouvé,
page Evergreen observée.

Le run `F218FIXA` utilise un disque de recouvrement neuf sur la base Windows 11 Pro
(10.0.26100), sans carte réseau, en groupe de travail ; le coordinateur du banc a défini
le domaine (XML `084bfb56`), créé le disque et démarré la VM. Prévol lié
`b377a167…`. Écarts d'outillage sans effet sur le produit : la session de profil A a été
fermée par le redémarrage suivant au lieu d'une déconnexion (guillemets de l'aide QGA,
corrigée) ; le kit natif a été compilé avec le SDK .NET 10.0.401 du Mac, le chemin par
défaut du générateur n'existant plus.

- Natif : 17 étapes réussies, codes de sortie tous nuls.
- Complément : plain, tamper, repair et rollback réussis ; prévols conformes.
- Socle : Evergreen 140 prioritaire, Socle 156 en repli, absence des deux moteurs
  signalée avec le lien du Socle, `fr.pak` altéré refusé ; valeur Evergreen rétablie.
- Interface : accueil WebView et filiation Evergreen observés ; `desktop.ini` refusé
  avec son nom.
- Nouvel écran Paramètres dans l'application Windows réelle : en-tête et quatre tuiles,
  rail des six sections (ouverture de Démonstration, de Mises à jour et de Configuration
  à diffuser), carte des scénarios en démonstration (issue, compteur, liens), section
  Mises à jour hors démonstration avant et après la mise à jour (« Version 2.0.18 —
  Nouveau démarrage confirmé »). Aucun bandeau de compte ouvert ni d'inventaire partiel.
- Démonstration : sauvegarde simulée de deux profils terminée ;
  `filpro.demoStateIdentical` avant/après.
- Mise à jour réelle 0.0.1 → 2.0.18 puis retour arrière : transaction
  `3cb9dabfdfe34826a7f65160c08897ea`, trois confirmations Windows observées, états
  Committed et RolledBack vérifiés, EXE candidat puis EXE de base relancés.
- Reprise : état `RollingBack` injecté dans une copie dédiée, véritable worker 2.0.18
  exécuté, `passed`. Test déterministe, pas une coupure électrique réelle.

Les huit groupes passent le validateur sans assouplissement. Reçu privé
`windows-qualification.json` : `passed`, PROD-LIGHT, qualification complète dans le
périmètre Windows 11, 286 fichiers et 124 références de preuve. SHA-256 :
`69e92f6774f1919a8dbb78ca1598ce73ff3e8fa8a5629982529cca90dc13dbe5`.

Observation supplémentaire, après la collecte des preuves : sur la base 0.0.1 revenue,
« Définir comme configuration par défaut » (confirmation Windows) écrit
`wad\config\deployment-defaults.json` (431 octets, `ebb4f96a…`). Une seconde mise à jour
vers 2.0.18 (transaction `a11e52a13f674da1904d7dcadeb3a64b`) laisse ce fichier identique
dans la nouvelle version, octet pour octet. Au démarrage, FILPRO présente la
« Configuration fournie avec ce pack » ; « Garder mes réglages » ferme l'aperçu et
revient à l'accueil.

Pack PROD-LIGHT construit depuis l'entrée du candidat : mêmes 287 entrées, manifeste
compris, ZIP `498b320014368b9bd584c9ea52f2b694de17d98c96ac88ca243650705b790dbc`,
53 229 738 octets.

Publiée le 12 novembre 2026 sur le « go publish » de David :
[v2.0.18](https://github.com/M4D135/FILPRO/releases/tag/v2.0.18), release `443009751`,
tag sur `6c8980f1`. Le ZIP et `SHA256SUMS` sont vérifiés de trois façons : empreinte
donnée par l'API, nouveau téléchargement contrôlé par `shasum -c`, notes identiques au
fichier source.

## Recette du 10 novembre 2026 — 2.0.17 portable PROD-LIGHT, source `8fdadaa1`

Le candidat provient de `8fdadaa14d347c4fc6a0d27a67debbc75eafd5d1` : ZIP
`4c5f32b83e15dba0a425538a5a7caf387d57e5768ee13c6064150ab046d22b9e`, 278 fichiers,
EXE `36bd266f7415f21dd7aac8bb0eb3efa8a36e7a040d185437ce608c7afe60b344`.
La base jetable 0.0.1 est compilée séparément depuis
`388c4fce0ffe3b6f6e408d7c777aa63315dafb24` ; elle reste privée (ZIP
`2db1f2c496de95feacfacd8b4c6b8506e1ff484eb5e66f05bc0bedd616776db7`).
CI Windows [`42775193124`](https://github.com/M4D135/FILPRO/actions/runs/42775193124)
verte : 57 suites, 465 contrôles réussis, aucun ignoré, page Evergreen observée.

Le run `F217FIXA` utilise une nouvelle fixture Windows 11 Pro (10.0.26100),
isolée sans carte réseau, en groupe de travail. Son identité, ses comptes et
sa session opérateur sont liés au prévol réellement observé. Le disque et les
preuves de la recette précédente restent conservés. La préparation a effectué
un redémarrage Windows normal. Un ReTrim du volume de test a terminé avec code 0 ;
son observation a été reprise après expiration du délai du contrôleur, sans
relancer l'opération.

- Natif : 17 étapes réussies ; annulation, chiffrement, trois politiques de
  restauration, refus de mauvaise clé et retour à l'état initial.
- Complément : plain, tamper, repair et rollback réussis ; prévol final conforme.
- Socle : Evergreen 140 prioritaire, Socle 156 en repli, absence des deux moteurs
  clairement signalée avec le lien du Socle, `fr.pak` altéré refusé. La valeur de
  registre Evergreen est rétablie et comparée.
- Interface : accueil WebView et filiation Evergreen réelle observés ;
  `desktop.ini` explicitement refusé dans la copie contenant cet intrus.
- Nouvelle interface : huit observations dans l'application Windows réelle
  vérifient les deux blocs fermés initialement, le compteur visible, l'ouverture
  du bon profil par « Voir les logiciels », le choix supplémentaire et son état
  non enregistré, la conservation de ces informations au repli, l'ouverture
  indépendante des doublons, leur explication et la conservation du choix au
  dépliage. Cinq captures UIA brutes et six images sont conservées séparément.
- Démonstration : sauvegarde simulée de deux profils terminée ; comparaison
  `filpro.demoStateIdentical` avant/après, réglages, profils et ruches conservés.
- Mise à jour réelle 0.0.1 → 2.0.17 puis retour arrière : transaction
  `c315cdc25b1840fa9e04ea130d750986`, trois confirmations Windows observées,
  états Committed et RolledBack vérifiés, accueils redémarrés observés et réglages
  inchangés. Le premier dialogue de préparation a expiré sans effet ; l'absence
  de transaction a été vérifiée avant une nouvelle confirmation. Les traces
  restent conservées ; la mise à jour n'a été exécutée qu'une fois.
- Reprise : état `RollingBack` injecté après le déplacement final dans une copie
  dédiée, véritable worker 2.0.17 exécuté deux fois avec code 0. Journal,
  inventaires, réglages et transaction originale inchangés. Ce test déterministe
  n'est pas une coupure électrique réelle.

Les huit groupes passent le validateur sans assouplissement. Le reçu privé
`windows-qualification.json` est `passed`, PROD-LIGHT, qualification complète dans
le périmètre Windows 11 : 278 fichiers et 124 références de preuve. SHA-256 :
`f248ba0435abf30aa2bc30b8bba775c03e15d14e20530076749639d83e9c67e6`.
Les preuves brutes, journaux privés et captures supplémentaires restent au banc.

Le ZIP portable publié [v2.0.17](https://github.com/M4D135/FILPRO/releases/tag/v2.0.17)
(release `440148999`, tag sur `8fdadaa14d347c4fc6a0d27a67debbc75eafd5d1`)
a exactement les mêmes 279 entrées que le candidat, manifeste compris :
`9112d269e53dfda8c6ba4b57f55bfcb897032ea31352426ded0b8cf85ae3b901`,
53 198 757 octets. L'API, `SHA256SUMS` et un nouveau téléchargement sont vérifiés.
Il s'agit d'une archive à extraire, sans installateur.

Le banc est clôturé : réglages d'origine rétablis, dix tâches propres à cette
recette retirées, connexion automatique désactivée, arrêt Windows normal observé.
Disques et preuves conservés. Qualification limitée à Windows 11 x64 sur décision
de David ; Windows 10 reste non testé et non qualifié.

## Recette du 9 novembre 2026 — 2.0.16 PROD-LIGHT, source `525d177a`

Cette recette porte sur le candidat LIGHT de
`525d177ab808b6de49d349b77abe9496a3fe74e3` (ZIP
`f53972d9811e02ec6c854e4d35dadaf9b1b4d6f603e438b970ae373044f1c0c9`, 278 fichiers,
EXE `76dd88ae0db570507bd1bb5fe4dbeea245913b231b98aafce66eb4c36a6ad9de`).
La base jetable 0.0.1 est compilée séparément depuis `d91fdebafb767a3bc01e70d298668139ec7059e9`
(ZIP `681fc1b459377e05e5d56eae3ed4bd61e64886c9d0e4a23ca343395f4f095f40`).
La CI Windows `42695591772` est verte : 57 suites, 465 contrôles réussis,
aucun ignoré, page Evergreen observée.

Le run `F216FIXA` utilise une fixture Windows 11 (10.0.26100) isolée, hors réseau,
en groupe de travail. La préparation du banc a rencontré un redémarrage Windows
bloqué avant les tests FILPRO. Une reprise à froid du même domaine a été effectuée ;
le volume NTFS, les identités, les marqueurs, les profils désactivés et les ruches
déchargées ont été contrôlés ensuite. Le prévol accepté est lié au kit et à la
session opérateur réelle. Aucun essai natif n'a été rejoué pour cet incident.

- Natif : 17 étapes réussies ; annulation, chiffrement, trois politiques de
  restauration, refus de mauvaise clé et retour à l'état initial.
- Complément : plain, tamper, repair et rollback réussis ; prévol final conforme.
- Socle : quatre scénarios réussis : Evergreen 140 prioritaire, Socle 156 en repli,
  absence des deux moteurs signalée avec le lien du Socle, `fr.pak` altéré refusé ;
  valeur de registre Evergreen rétablie et comparée.
- Interface, Evergreen, fichier intrus : accueil WebView observé avec filiation Evergreen réelle ;
  `desktop.ini` explicitement refusé dans une copie du pack.
- Démonstration : sauvegarde simulée de deux profils terminée ; comparaison
  avant/après `filpro.demoStateIdentical`, réglages, profils et ruches conservés.
- Mise à jour réelle 0.0.1 → 2.0.16 et retour arrière : transaction `b661f54db11746d1828d26e1f393060c`,
  confirmations Windows observées, états Committed et RolledBack vérifiés.
- Reprise de l'ouvrier : état `RollingBack` injecté après le déplacement final,
  véritable worker 2.0.16 exécuté deux fois, idempotence vérifiée, inventaires et
  journal conservés ; transaction originale et réglages inchangés. La coupure de
  courant pendant une mise à jour n'est pas exercée ; l'incident de préparation
  du banc ne constitue pas ce test.

Les huit groupes ont été validés sans modification du validateur. Le reçu privé
`windows-qualification.json` est `passed`, PROD-LIGHT, qualification Windows complète
dans le périmètre Windows 11 : 278 fichiers et 124 références de preuve. Son SHA-256
est `d32532fe95310da0ef50dcf08b95559e20d134b6721d77deb44906990c4a4302`.
Les archives de preuves brutes et les confirmations expirées restent conservées.
Un relevé complet de l’interface redémarrée a suivi une observation trop précoce.
Deux confirmations expirées sans effet ont été reprises avec une fenêtre fraîche.
Les tentatives précédentes restent archivées ; aucun essai natif ni aucune
installation de mise à jour n’a été rejoué.

Le pack publié `v2.0.16` (release et tag retirés de GitHub le 12 novembre 2026)
(release `439729889`, tag sur `525d177ab808b6de49d349b77abe9496a3fe74e3`)
a exactement le même contenu que le candidat, manifeste compris : ZIP
`888a6553e50fa9f6f434bd117430283b68be5382081c73c40287f57e64faf213`,
53 197 160 octets. Les empreintes de l’API et d’un nouveau téléchargement sont comparées.

Les cas de découverte AppData sont couverts par les 20 contrôles Windows de
`SoftwareFileDiscoveryTests`, notamment un lien symbolique réellement créé :
exclusion des liens génériques sans bloquer les voisins, refus maintenu pour les
chemins explicites, mémorisés, du catalogue et les racines liées. Cette matrice
n'est pas rejouée manuellement dans la recette VM.
Qualification limitée à Windows 11 x64 sur décision explicite de David ;
Windows 10 n'a pas été testé et reste non qualifié.

## Recette du 9 novembre 2026 — 2.0.15 PROD-LIGHT, source `c87c677a`

Cette recette porte sur le pack LIGHT de `c87c677ade4749bda3e4acd29aac3add604fc807`
(ZIP `0f7072eb219c71fc6edbee2761eab8351e40186712f3edc4b40962bdd66359c2`, 278 fichiers,
EXE `00acb615c9d27587a2621eb95b85ab8cea50f7bd9422062533889c1e5e7e2f2a`). Base de mise à
jour : LIGHT 0.0.1 de la même source (ZIP `ca52d3fc…`, EXE `ef9a8939…`). Le run GitHub
Windows `42673322691` compte 57 suites et 463 contrôles réussis, avec la page Evergreen
observée. Son premier essai n’a lancé aucun job pendant une panne de GitHub Actions ; le
second est vert.

Recette VM (run `F215FIXA`) sur la fixture Windows 11 recréée de zéro (KVM, groupe de
travail, hors réseau), compte opérateur local :

- **Natif** : 17 étapes, dont 6 confirmations. Les 4 phases complémentaires sont
  conformes.
- **Socle** : Evergreen 140 retenu en premier ; Socle retenu sans Evergreen ; message
  avec le lien du Socle sans l’un ni l’autre ; Socle altéré (`fr.pak`) refusé.
- **Démarrage** : la page est observée par le collecteur Evergreen.
- **Fichier en trop** : 278 fichiers plus `desktop.ini` ; refus nommé.
- **Démonstration** : `filpro.demoStateIdentical` avant/après, 2 profils.
- **Mise à jour réelle** 0.0.1 → 2.0.15 depuis un dossier local avec `SHA256SUMS` :
  - source mémorisée, réglages relevés ensuite ;
  - transaction `94aff50769aa4ad29a725c4f5fa81541`, d’abord `Committed` (EXE du
    candidat en cours d’exécution), puis `RolledBack` par le retour arrière demandé dans
    l’interface de la 2.0.15 (EXE de la base rétabli) ;
  - confirmations natives acceptées dans la vraie fenêtre.
- **Reprise de l’ouvrier** : état RollingBack après le déplacement final, ouvrier
  candidat appelé deux fois, journal et inventaires conservés. La coupure de courant
  n’est pas exercée.

Les 8 groupes sont validés sans adaptation du validateur. Le reçu
`windows-qualification.json` est émis (`passed`, PROD-LIGHT, 278 fichiers, 124 preuves)
et reste privé. Le pack publié (ZIP `4e846357e51448d4fe7b4f698fd9fe34ddaa4b7532777940ee662829efb054d3`)
a le même contenu que le candidat recetté.

Non rejoué dans la VM : les profils de la fixture n’ont aucun dossier AppData au nom fini
par un point ou une espace, au nom d’appareil ou au nom non représentable. Ces cas sont
couverts par la CI Windows (`SoftwareFileDiscoveryTests`, voir la section 2.0.14). Le cas
Windows 10 (noms d’appareil convertis en `\\.\NUL`) n’est vérifié que par lecture : la
CI et la fixture tournent sous Windows 11 (10.0.26100).

## Recette du 9 novembre 2026 — 2.0.14 PROD-LIGHT, source `9f8ca177`

Cette recette porte sur le pack LIGHT de `9f8ca1775773342bbbcec3a20af6834f69728cd5`
(ZIP `831d3b6f2b161683e2163f13b29bdc7e58e96922b086864df206cfd355be9002`, 278 fichiers,
EXE `87a141655c09ddcaa624134926fb2a59830716569e8f07013ed0fe3bd172afae`). Base de mise à
jour : LIGHT 0.0.1 de `d1a8e3cd` (ZIP `1279a803…`, EXE `d047b16f…`). Le run GitHub
Windows `42602008887` compte 57 suites et 459 contrôles réussis, avec la page Evergreen
observée. Par rapport à la 2.0.13, deux tests sont partis avec l’avertissement du dossier
modifiable et un test couvre le chemin de mise à jour vide.

Recette VM (run `F214FIXA`) sur la fixture Windows 11 recréée de zéro (KVM, groupe de
travail, hors réseau), compte opérateur local :

- **Natif** : 17 étapes, dont 6 confirmations. Les 4 phases complémentaires sont
  conformes.
- **Socle** : Evergreen 140 retenu en premier ; Socle retenu sans Evergreen ; message
  avec le lien du Socle sans l’un ni l’autre ; Socle altéré (`fr.pak`) refusé.
- **Démarrage** : la page est observée par le collecteur Evergreen. Ni l’accueil ni
  Paramètres n’affichent l’avertissement du dossier modifiable.
- **Fichier en trop** : 278 fichiers plus `desktop.ini` ; refus nommé.
- **Démonstration** : `filpro.demoStateIdentical` avant/après, 2 profils.
- **Mise à jour réelle** 0.0.1 → 2.0.14 depuis un dossier local avec `SHA256SUMS` :
  - source mémorisée du premier essai, réglages relevés ensuite ;
  - transaction `13847e1764ba4cd595983016e26657e4`, d’abord `Committed`, puis
    `RolledBack` par le retour arrière demandé dans l’interface de la 2.0.14 ;
  - confirmations natives acceptées dans la vraie fenêtre.
- **Reprise de l’ouvrier** : état RollingBack après le déplacement final, ouvrier
  candidat appelé deux fois, journal et inventaires conservés. La coupure de courant
  n’est pas exercée.

Les 8 groupes sont validés sans adaptation du validateur. Le reçu
`windows-qualification.json` est émis (`passed`, PROD-LIGHT, 278 fichiers, 124 preuves)
et reste privé. Le pack publié (ZIP `6cf2385b516d49555b8e9326c255f2807457e08f0e8d2cf9f19dec61b22c4efd`)
a le même contenu que le candidat recetté.

Non rejoué dans la VM : les profils de la fixture n’ont aucun nom non représentable, et
le chemin vide n’a pas été essayé. Ces deux corrections sont couvertes par la CI Windows
(`LoadedRegistryDiscoveryTests.NomGeneriqueNonRepresentableEstTaiSansRendreLInventairePartiel`,
`UpdateSourcePreferenceTests.CheminVideRefuseSansMettreLaMiseAJourEnEchec`) et par les
tests web. Le même traitement côté dossiers AppData (`WindowsSoftwareFileReader`)
n’avait pas de test dédié à la publication ; il est couvert depuis `4b34195`
(`SoftwareFileDiscoveryTests.NomDeDossierNonRepresentableEstTuSansRendreLInventairePartiel`,
CI Windows `42641217661`, 460 contrôles). Les noms finis par un point ou une espace, les
noms d’appareil et les liens au nom non représentable sont corrigés et couverts depuis
`30231eb` (`NomFiniParUnPointOuUneEspaceEstTuSansRendreLInventairePartiel`,
`NomDAppareilReserveEstTuSansInterrompreLaPortee`,
`LienAuNomNonRepresentableEstTuSansRendreLInventairePartiel` ; CI Windows `42648655102`,
463 contrôles). Non rejoué dans la VM, ni sous Windows 10.

## Recette du 9 novembre 2026 — 2.0.13 PROD-LIGHT, source `36e0d035`

Cette recette porte sur le pack LIGHT de `36e0d0353ab084b83a0cf37096d0efb03618a1d2`
(ZIP `0219b32b00216544d6d50c8d96da62473843a75634de4f3daf745024b79048e1`, 278 fichiers,
EXE `ee9d3efa87416aa587d42367cb450298caa16621deefa338e204804de676843b`). Base de mise à
jour : LIGHT 0.0.1 de `cf9dc42e` (ZIP `1b215132…`, EXE `272aa91d…`). Le run GitHub
Windows `42529299194` compte 57 suites et 460 contrôles réussis, avec la page Evergreen
observée.

Depuis la recréation du dépôt (9 novembre 2026), `36e0d035` et le run `42529299194` ne
sont plus sur GitHub. `e66757d1`, base du dépôt recréé, a les mêmes sources à trois
documents près (voir `REPRISE.md`).

Recette VM sur une fixture Windows 11 neuve (KVM, groupe de travail, hors réseau),
compte opérateur local :

- **Natif** : 17 étapes, dont 6 confirmations. Les 4 phases complémentaires sont
  conformes.
- **Socle** : Evergreen 140 retenu en premier ; Socle retenu sans Evergreen ; message
  avec le lien du Socle sans l’un ni l’autre ; Socle altéré (`fr.pak`) refusé.
- **Démarrage** : la page est observée par le collecteur Evergreen.
- **Fichier en trop** : 278 fichiers plus `desktop.ini` ; refus nommé.
- **Démonstration** : `filpro.demoStateIdentical` avant/après, 2 profils.
- **Mise à jour réelle** 0.0.1 → 2.0.13 depuis un dossier local avec `SHA256SUMS` :
  - « Dossier local ou partage UNC » est la source proposée par défaut ;
  - transaction `ec8c7f1146e84a5dbdc241e999398f1e`, d’abord `Committed`, puis
    `RolledBack` par le retour arrière demandé dans l’interface ;
  - confirmations natives acceptées dans la vraie fenêtre ;
  - réglages relevés après la mémorisation de la source, puis conservés.
- **Reprise de l’ouvrier** : état RollingBack après le déplacement final, ouvrier
  candidat appelé deux fois, journal et inventaires conservés. La coupure de courant
  n’est pas exercée.

Le validateur de recette a d’abord refusé `packageAndCI` (« Windows contract suites
incomplete ») : sa liste de suites CI exigées nommait `GitHubUpdateTransportTests`,
supprimée par `602d340` avec la source GitHub. Le refus de `updateRollbackRecovery` en
découlait. La liste nomme maintenant `UpdateSourcePreferenceTests` et
`ExecutionBridgeTests`, qui couvrent la préférence de source et les routes du pont ; les
autres suites exigées sont inchangées. Cette adaptation du kit a été faite pendant la
recette et signalée à David avant la publication.

Après cette adaptation, les 8 groupes sont validés. Le reçu `windows-qualification.json`
est émis (`passed`, PROD-LIGHT, 278 fichiers) et reste privé.

Observation, non corrigée : « Mémoriser cette source » avec un chemin vide est refusé
(« Préférence de mise à jour invalide… ») et n’écrit rien, mais le refus s’affiche dans
le bandeau général « Action arrêtée ; consulter le journal avant de recommencer ».

## Recette du 8 novembre 2026 — 2.0.12 PROD-LIGHT, source `f256df62`

Cette recette porte sur le pack LIGHT de `f256df621ec57fd8d277749192fd8279a9f90193`
(ZIP `d0eb2f1c51363bdc498417b89dd04f1828d93204fa48627ac390bc297547a64d`, 278 fichiers,
EXE `cb58bb8c4da115ec58069b80fe0b84b18a29e07842516e19c47710abe67c0011`). Base de mise à
jour : LIGHT 0.0.1 de `96327647` (ZIP `e9264ef4…`, EXE `05609913…`). Le run GitHub
Windows `42513511350` compte 58 suites et 475 contrôles réussis, avec la page Evergreen
observée.

`f256df6` ne change que la CI : en démonstration, la fenêtre de configuration du pack
s’ouvre au démarrage et rend la coque inerte. Le contrôle Evergreen accepte donc son
sous-titre comme preuve de la même page.

Recette VM sur une fixture Windows 11 neuve (KVM, groupe de travail, hors réseau),
compte opérateur local :

- **Natif** : 17 étapes, dont 6 confirmations. Les 4 phases complémentaires sont
  conformes.
- **Socle** : Evergreen 140 retenu en premier ; Socle retenu sans Evergreen ; message
  avec le lien du Socle sans l’un ni l’autre ; Socle altéré (`fr.pak`) refusé.
- **Démarrage** : la page est observée par le collecteur Evergreen.
- **Fichier en trop** : 278 fichiers plus `desktop.ini` ; refus nommé.
- **Démonstration** : sauvegarde fictive de 2 profils terminée ;
  `filpro.demoStateIdentical` avant/après.
- **Mise à jour réelle** 0.0.1 → 2.0.12 depuis un dossier local avec `SHA256SUMS` :
  - transaction `ec96617c9a494ba9bde99dadcbdb67ad`, d’abord `Committed`, puis
    `RolledBack` par le retour arrière demandé dans l’interface ;
  - confirmations natives acceptées dans la vraie fenêtre ;
  - EXE relevé après chaque redémarrage.
- **Reprise de l’ouvrier** : état RollingBack après le déplacement final, ouvrier
  candidat appelé deux fois, journal et inventaires conservés. La coupure de courant
  n’est pas exercée.

Le validateur de recette a d’abord validé tous les groupes sauf
`nativeIndexAndScopes` (« invalid ISO timestamp »). Le script de préparation native du
kit écrit `endedUtc` avec `[DateTime]::UtcNow`, que `ConvertTo-Json` de Windows
PowerShell 5.1 sérialise en `\/Date(ms)\/`. C’est un défaut du kit, pas du candidat.

David a choisi d’adapter le validateur (8 novembre 2026). Seul le `endedUtc` des étapes
natives convertit maintenant la forme exacte `/Date(ms)/` en instant UTC. Toute autre
valeur reste soumise au contrôle ISO et à la fenêtre du run (20 cas testés).

Après cette adaptation, les 8 groupes sont validés. Le reçu `windows-qualification.json`
est émis (`passed`, PROD-LIGHT, 278 fichiers) et reste privé.

## Recette du 31 octobre 2026 — 2.0.11, source `78c5de7c`

Cette recette porte sur `78c5de7cc60827933beb4a84238eabf34717ba06`. EXE Windows x64 :
`3776ea2a9b43ecf824a4df2b3e9e80887b27b0d337999f1bd168a24069e9d4b4`. L'inventaire
vérifie 535 fichiers PROD et 917 fichiers DEV. Le run GitHub Windows `41189660081`
compte 55 suites et 440 contrôles réussis ; les mêmes 55 suites (440 contrôles) ont
aussi réussi sur la VM avant le push.

Changements 2.0.11 (corrections, sans changement d'interface) :

- fenêtre de confirmation : l'accord exige aussi que l'échéance de deux minutes ne
  soit pas atteinte (`NativeWindowPolicy.CanApprove`) ; seul le refus reste possible
  ensuite ;
- worker de mise à jour : la sortie du processus est relevée avant de chercher
  l'accusé de santé, qui est donc lu même après une fermeture rapide ;
- `SHA256SUMS` non UTF-8 : refus explicite (`DecoderFallbackException` traitée) ;
- après la fenêtre, un échec du nettoyage n'empêche plus la sortie du mode modal
  (`LeaveNativeModal` en `finally`).

- **Recette VM** sur FILPRO-W11-TEST, même compte `LABO\Administrateur` : 17
  étapes natives (code de sortie 0), dont 6 exécutions USMT réelles ; 4 phases
  complémentaires (`SUPPLEMENT_OK`).
- **Démarrage**, **fichier en trop** (918 fichiers plus `desktop.ini`, seul le refus
  nommé) et **démonstration** (profils et réglages inchangés) : conformes.
- **Mise à jour réelle** depuis la 2.0.10 publiée : transaction
  `9838b967b1c342bda938eb546f8ef93d`, `Committed` puis `RolledBack`. Décisions au
  journal (`SameAccount`) : `updates.prepare` acceptée (`DDAE60B821F2`) et
  `updates.install` acceptée (`9357B2B0CF7E`) dans la fenêtre de la 2.0.10 ; dans la
  fenêtre de la 2.0.11, `updates.rollback` refusée puis acceptée (`EF0BB58669FD`).
  Un premier clic d'accord envoyé pendant que la fenêtre était inactive a été ignoré,
  comme prévu.
- **Sonde UIPI** pendant la fenêtre 2.0.11 : RID 8192, toutes les tentatives
  refusées, la fenêtre reste affichée.
- **Reprise par le worker 2.0.11** : transaction jetable
  `3cd33c0aed4b47709695deb1d8e52a34`, état `RollingBack`,
  `fixtureStateSimulated=true`.
- **Images de la vraie fenêtre par thème (nouvelle étape 10b)** : non obtenues. Le
  thème bascule bien (`system` → `light` → `system`), mais « Définir comme
  configuration par défaut » est refusé sur le pack DEV (« Le type du pack ne
  correspond pas à la distribution attendue. ») et n'ouvre aucune fenêtre, comme en
  2.0.10. Le garde est conservé ; preuves sous `theme-evidence/`.

Constats du banc : avant la recette, avec l'accord de David, le cache Windows Update
et la corbeille de `C:` ont été vidés et les racines `U:\FILPRO-UPDATE-204` à `208`
(et leurs `-Recovery`) supprimées ; une vérification d'espace (étape -1) précède
désormais la recette. La valeur d'inventaire `acd66dad…` a été reliée à la main comme
depuis 2.0.4 (relue sur la VM).

Le reçu `delivery/qualified-211/windows-qualification.json` porte `status=passed` et
les huit contrôles. SHA-256 :
`e449a263278eb4694169e99579338f77801f7a1454e425314074ac71d42af78e`.
`validation-summary.json` : SHA-256
`ced17d7ecd8ec54d9218b460c0af4c7d164a1b0b59c3371ed840321c7cabdde4`. L'export des
preuves (84 fichiers, `FILPRO-2.0.11-windows-evidence.zip`, 78 079 octets, SHA-256
`ad66995722fb5fce45bfd1aa1041ebddab661f6e28f579744af312a9148fd13d`) a été vérifié
entrée par entrée contre son index. Le PROD officiel est identique à la fixture (536
entrées, manifeste `ecf91897…`).

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.11-win-x64-PROD.zip` | 364 728 570 | `523652a19c146fb9b737462ee1c1574099faaf364c32abb3a0a677a7e7e565d7` |
| `FILPRO-2.0.11-win-x64-DEV.zip` | 368 924 799 | `69b99cfa4d72f7f225373d4b38b06d8da36e00813fd6423eacb1875f81abea68` |

Release publique [v2.0.11](https://github.com/M4D135/FILPRO/releases/tag/v2.0.11),
identifiant `429878550`, tag et cible sur `78c5de7c`, avec `SHA256SUMS` (193 octets,
SHA-256 `a191cf12118f70802c1ee2ce7a13effc1f9e04285f76ad3b8fd34150a2521e90`). Le probe
réel de téléchargement GitHub depuis la 2.0.10 est conforme.

Nettoyage : `cleanup-result.json` (SHA-256
`464ecc0645cd1ca52ef1c600f1f45eb0bdb0540ab9cc4cb4378ed42be5a1d781`) retire les tâches
FILPRO-211 (Probe comprise) et restaure les réglages d'avant recette
(`update-source.json` restauré). Les 13 fichiers de transfert de l'hôte ont été
retirés. Les dossiers `U:\FILPRO-V2-211`, `U:\FILPRO-TESTS-211`,
`U:\FILPRO-UPDATE-211`, `U:\FILPRO-UPDATE-211-Recovery` et la base 2.0.10 restent sur
le disque de test comme pièces.

## Recette du 30 octobre 2026 — 2.0.10, source `71de3c58`

Cette recette porte sur `71de3c585aac07820b7c7a2bb9d0b277513f6f09`. EXE Windows x64 :
`9f32b414df1e8373c7d235a23ccb88b596b460077e4d06d3d4f347252d33db8c`. Les quatre DLL des
harnais sont retrouvées octet pour octet dans cet EXE. L'inventaire vérifie 535
fichiers PROD et 917 fichiers DEV. Le run GitHub Windows `41028767432` compte 55
suites et 439 contrôles réussis ; les mêmes suites ont aussi réussi sur la VM avant
le push.

Changement 2.0.10 : la fenêtre « FILPRO — Confirmation Windows » prend l'aspect de
FILPRO.

- la fenêtre prend le logo FILPRO (barre de titre et en-tête), le filet du dégradé
  de marque, les polices Segoe UI Variable (Cascadia Mono pour l'empreinte) et des
  valeurs dans des cadres arrondis ;
- le bouton d'accord est au dégradé une fois armé et gris lisible désarmé, Annuler
  est en contour avec anneau du focus, la barre de titre est aux couleurs (Windows
  11) ;
- la palette fermée compilée (clair, sombre, contraste) est appliquée ; seul le
  nom du thème est lu, sans effet de bord, dans les réglages de FILPRO, attente
  bornée à une seconde ; contraste élevé de Windows : couleurs système.
- l'armement, le recouvrement, le focus, le clavier et les refus sont inchangés ;
  nouvelle suite `ConfirmationThemeTests` (contraste WCAG) et test de lecture du
  thème sans effet (`ConfigurationStoreTests`).

Une revue Spark et une revue par agents (interrompue, un angle terminé) ont fait
corriger avant le commit : lecture du thème par `ReadAsync` qui pouvait évincer un
aperçu en attente (remplacée par une lecture sans effet), annulation pendant la
lecture, affichage tombé pendant la lecture, polices illisibles, icône de l'en-tête
libérée en cas d'échec, contour d'Annuler à 3:1, gardes de dessin et réglage Windows
illisible.

- **Recette VM** sur FILPRO-W11-TEST, même compte `LABO\Administrateur` : 17
  étapes natives, dont 6 exécutions USMT réelles ; 4 phases complémentaires ;
  `plain` refuse dans la confirmation (`confirmation.refused`), puis pendant la pause
  (`confirmation.busy`), puis au rejeu du plan refusé (`plan.expired`).
- **Démarrage** : accueil sans écran d'entrée ; Paramètres « Installée : 2.0.10 ».
  **Fichier en trop** : copie de 918 fichiers (917 et le manifeste) plus `desktop.ini`,
  seul le refus nommé. **Démonstration** : sauvegarde fictive de deux profils
  terminée ; profils et réglages identiques, aucune destination créée.
- **Mise à jour réelle** depuis la version publiée : premier cycle (transaction
  `21cbe1e2f5fc4d838f3524ed0bbef0a3`) jusqu'à `RolledBack`, mais preuves non capturées
  car l'instantané des réglages avait été omis ; second cycle complet (transaction
  `d63b9ad9a52244318049d022e628c2a9`), `Committed` puis `RolledBack`, captures et
  health liés. Décisions au journal (`SameAccount`) : `updates.prepare` acceptée
  (`0683CEE45202`) et `updates.install` acceptée (`A4B4D7E9E2F5`) dans la fenêtre de
  la version précédente ; dans la nouvelle fenêtre de la 2.0.10, `updates.rollback`
  refusée puis acceptée (`28F4D7391E59`) ; au premier cycle, même chose (refusée
  `6E42F740652D`, 43 727 ms, puis acceptée, 84 709 ms).
- **Sonde UIPI** pendant la fenêtre 2.0.10 (premier cycle) : RID 8192, fenêtre et
  bouton trouvés ; UI Automation ne voit pas le bouton ; `BM_CLICK` et
  `WM_KEYDOWN`/`WM_KEYUP` refusés (erreur 5) ; `SendInput` sans effet ; la fenêtre
  reste ouverte. Un premier lancement avait été refusé par la sonde elle-même
  (empreinte de l'EXE précédente encore épinglée, corrigée).
- **Capture de la vraie fenêtre** 2.0.10 (thème clair, retour arrière) :
  `rollback-window-210.png` ; aperçus des thèmes clair, sombre, contraste et repli
  système par un programme d'aperçu compilant le même code (`preview/captures-1`).
- **Reprise par le worker 2.0.10** : transaction jetable
  `90d05a6e41b0486e91b8f30d86e83f2d`, état `RollingBack`, deux invocations,
  `fixtureStateSimulated=true`.

Constats du banc : `C:` plein (0 Mo) pendant le second cycle ; le cache Windows Update
a été vidé avec l'accord de David (3,9 Go libérés). `U:` plein pendant la reprise ;
les racines de mise à jour 2.0.1 à 2.0.3 ont été supprimées avec l'accord de David
(16,8 Go). La première reprise en échec (disque) est conservée sous
`recovery-run-failed-diskfull` et `FILPRO-UPDATE-210-Recovery-failed-diskfull`. La
valeur d'inventaire `acd66dad…` a été reliée à la main comme depuis 2.0.4. Un essai de
fenêtre réelle en thème sombre n'a pas abouti (aucune fenêtre ouverte par « Définir
comme configuration par défaut »).

Limites : la fenêtre réelle 2.0.10 a été vue en thème clair seulement ; les thèmes
sombre, contraste et le contraste élevé de Windows ont été vus par l'aperçu. La lecture
de la page, `CORECLR_*` et `LOCALAPPDATA` restent des risques acceptés.

Le reçu `delivery/qualified-210/windows-qualification.json` porte `status=passed` et
les huit contrôles. SHA-256 :
`fcc2f37ac18a4272d5b9f329f4dd12f977ee28061611535f71deef87d5fe4b1d`.
`validation-summary.json` : SHA-256
`ced17d7ecd8ec54d9218b460c0af4c7d164a1b0b59c3371ed840321c7cabdde4`. L'export des
preuves (84 fichiers, `FILPRO-2.0.10-windows-evidence.zip`, 77 299 octets, SHA-256
`9531e486154e6f41d341536a598dd311b0a804062f6d6f1a081cf2c25735f484`) a été vérifié
entrée par entrée contre son index. Le PROD officiel
est identique à la fixture (536 entrées, manifeste `d3c0886d…`).

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.10-win-x64-PROD.zip` | 364 728 411 | `86b30f5aabb43580d68b5bca139c6094edb628678d8ffacdfb8fc1ee10015e29` |
| `FILPRO-2.0.10-win-x64-DEV.zip` | 368 921 161 | `40f21a48f7896c0023cfbcf1ccb6f8055a9859554d46c59b9579f166a064a0f3` |

Release publique [v2.0.10](https://github.com/M4D135/FILPRO/releases/tag/v2.0.10),
identifiant `428967911`, tag et cible sur `71de3c58`, avec `SHA256SUMS` (193
octets, SHA-256
`5a587aaaa2c06ed3255423504660f4d3211fb6b05685ae0d1f14278edf28a949`). Le probe réel
de téléchargement GitHub depuis la version publiée est conforme.

Nettoyage : `cleanup-result.json` (SHA-256
`a70628a8faa4b49ce65def7bbb939ff6e1feb236a9de4e4fe996acab1693345b`) retire les tâches
FILPRO-210 et restaure les réglages d'avant recette (`update-source.json` restauré).
Les 14 fichiers de transfert de l'hôte ont été retirés. Les dossiers
`U:\FILPRO-V2-210`, `U:\FILPRO-TESTS-210`, `U:\FILPRO-UPDATE-210`,
`U:\FILPRO-UPDATE-210-Recovery`, `U:\FILPRO-PREVIEW-210` et la base de production
précédente restent sur le disque de test comme pièces.

## Recette du 30 octobre 2026 — 2.0.9, source `5e8fd5e0`

Cette recette porte sur `5e8fd5e0ac1996c5b100e0125afe2844c5b80930`. EXE Windows x64 :
`2e2be930a9fc5f829c5ecdbea79bca55ce44e24d244a9ef7f9aa1c36842a95c9`. Les quatre DLL des
harnais sont retrouvées octet pour octet dans cet EXE. L'inventaire vérifie 371
fichiers source, 535 fichiers PROD et 914 fichiers DEV. Le run GitHub Windows
`40989228485` (54 suites, 433 contrôles réussis) précède cette recette ; les mêmes
suites ont aussi réussi sur la VM.

Changement 2.0.9 : quand l'affichage WebView2 tourne sous un autre compte que FILPRO
élevé, ou sous un compte indéterminé, FILPRO ajoute les protections suivantes ; sous
le même compte, rien ne change par rapport à 2.0.8, et la démonstration garde ses
règles d'admission.

- les mises à jour sont refusées (`session.crossAccount`) : recherche
  (`updates.inspect`), source mémorisée (`updates.saveSource`), jeton GitHub
  (`updates.setGitHubToken`), préparation, installation et retour arrière ; la page
  remplace les commandes de la section Mises à jour par une explication (le titre et
  la version installée restent) ; le journal porte « Affichage sous <session>, FILPRO
  élevé sous <élevé> : autre compte ; mises à jour indisponibles, verrouillage de
  présence actif. » (« situation indéterminée » au lieu d'« autre compte » sous un
  compte indéterminé) ;
- un verrou de présence à sens unique, armé une fois la situation établie, se ferme
  sur : session Windows verrouillée, déconnectée ou fermée (notifications WTS,
  conservées pendant le démarrage, et état de la session relu toutes les 10 s) ;
  15 min sans saisie ; veille ou relevés interrompus plus de 2 min ; 30 min après le
  lancement élevé ; 3 refus ou absences de réponse en 10 min ; accord donné avec la
  case « Verrouiller FILPRO après le lancement (suivi seulement) », cochée par défaut et
  proposée pour les opérations système. Ce dernier verrou est posé dès l'accord, avant
  la libération de la fenêtre et avant le lancement, et reste si le lancement échoue.
  Les durées suivent un compteur monotone ; seul l'âge du processus élevé au
  démarrage de FILPRO se lit une fois sur l'horloge du poste, en UTC ;
- verrouillé, FILPRO n'admet plus que la lecture locale, le suivi, l'annulation et la
  fermeture ; la démonstration et tout le reste sont refusés (`presence.locked`) ;
  titre « FILPRO — verrouillé », bandeau dans la page, avertissement au journal,
  fenêtre en attente refusée, sélecteur de fichiers fermé ; il faut relancer FILPRO
  avec une nouvelle UAC ;
- un sélecteur de fichiers ouvert depuis la page se ferme seul après 2 minutes ;
- hors partage réseau (non évalué), quand le compte de la session peut modifier
  FILPRO.exe, son dossier, `wad` ou un dossier ancêtre (en supprimer un élément, le
  renommer, changer ses autorisations ou son propriétaire ; une racine de volume ne se
  renomme pas), FILPRO avertit sans refuser : bandeau, journal et point d'attention
  grave dans chaque fenêtre de confirmation.

Deux tours de revue adversariale ont fait corriger avant le commit : des bandeaux
superposés dans la même cellule de la page (empilés désormais), le bandeau de verrou
affiché en démonstration, des événements `pack.exposure` et `presence.changed` perdus
avant l'abonnement de la page (rejoués à `session.ready`), un guide qui présentait ces
protections comme générales, un dossier parent contrôlé pour la seule suppression
d'élément (désormais tous les ancêtres, avec renommage, autorisations et
propriétaire), des délais mesurés sur l'heure du poste puis un âge du processus
calculé en heure locale (désormais compteur monotone et UTC), un verrou après
lancement posé après le départ de l'opération et évitable par un échec de lancement
provoqué, un Win+L ou une déconnexion pendant le démarrage ignorés (désormais
appliqués à l'armement), `updates.saveSource` encore admis sous un autre compte, une
veille rapportée comme « 15 minutes sans saisie » (motif propre et règle documentée),
une racine de volume signalée pour un renommage impossible, deux commentaires
inexacts et la marge des bandeaux en fenêtre basse. Trois constats ont été écartés
comme non fondés.

- **Fenêtre réelle, même compte** : mise à jour 2.0.8 publiée → 2.0.9 ;
  `updates.prepare` acceptée (empreinte `03D6EF01478A`, 82 828 ms) et
  `updates.install` acceptée (`2B0B38A7532F`, 33 569 ms) dans la fenêtre de la 2.0.8 ;
  dans celle de la 2.0.9, `updates.rollback` refusée (`C649D8FD4097`, 36 169 ms, rien
  de lancé) puis acceptée (12 531 ms) ; situation `SameAccount`.
- **Sonde UIPI, même compte** : `LABO\Administrateur` ramené à l'intégrité moyenne
  (RID 8192) pendant que la fenêtre attendait. UI Automation ne voit pas le bouton
  d'acceptation ; `BM_CLICK` et `WM_KEYDOWN`/`WM_KEYUP` postés : refus, erreur 5 ;
  `SendInput` accepté par l'API (2 événements) sans effet ; la fenêtre reste ouverte.
- **Croisement de comptes**, comptes jetables `FTSTD209` (standard, session console)
  et `FTADM209` (saisi dans l'UAC), copie DEV sous `U:\FILPRO-REPRO-209`, dossier que
  les utilisateurs authentifiés peuvent modifier ; page pilotée sous `FTSTD209` par UI
  Automation, fenêtre au seul clavier de la VM (heures UTC) :
  - 03:00:33, « … : autre compte ; mises à jour indisponibles, verrouillage de
    présence actif. », puis l'avertissement du dossier modifiable ; la page montre le
    bandeau du dossier et remplace les commandes de Mises à jour par leur explication ;
  - un sélecteur ouvert depuis la page se ferme seul : 03:04:14, « Sélecteur de
    fichiers fermé automatiquement après 2 minutes » ;
  - la fenêtre de sauvegarde affiche « Compte élevé : FILPRO-W11\FTADM209 · Affichage :
    FILPRO-W11\FTSTD209 (autre compte) », la case de verrouillage cochée et le point
    grave du dossier modifiable ; les bandeaux de la page s'empilent ; la sonde lancée
    sous `FTSTD209` (RID 8192) obtient les mêmes refus et la fenêtre reste ouverte ;
  - Échap : « refusée (empreinte 69D04A8C0440, situation CrossAccount, 26194 ms) » ;
    une fenêtre laissée sans réponse par le banc se ferme à 120 s (« sans réponse »)
    et compte comme un refus ; rien n'est lancé ;
  - accord au clavier (Maj+Tab, Espace) avec la case cochée : « acceptée (empreinte
    812FB30B48E9, situation CrossAccount, 13921 ms) » à 03:09:34 et, dans la même
    seconde, « FILPRO verrouillé dans cette session (verrouillage demandé au lancement
    d’une opération) » ; titre « FILPRO — verrouillé ». La sauvegarde se termine
    (100 %, 2 min 23 s) et son suivi reste lisible ; `session.demoEnter`,
    `accueil.summary` (« Accueil indisponible » avec le texte du verrou),
    `sauvegarder.profiles` (« Préparer une sauvegarde » reste désactivé) et
    `session.choosePath` (aucun sélecteur) sont refusés (`presence.locked`) ;
  - fermeture par Alt+F4, relance avec une nouvelle UAC (03:15:13) : situation et
    avertissement de nouveau journalisés, aucun verrou ; Win+L : 03:17:28, « FILPRO
    verrouillé dans cette session (session Windows verrouillée) » ; pendant que la
    session Windows reste verrouillée, la page pilotée par le compte de la session
    montre le titre verrouillé et le bandeau, et la démonstration est refusée.

  Constat manuel hors des huit contrôles du validateur ; pièces sous
  `final-20261030-209/repro/`, `repro-evidence/` et dans les captures
  `repro-209-*.png` (`CONSTAT-209.txt` ; journal de `FTADM209`, 20 lignes, SHA-256
  `64a1d0f4aeacad6e0117be564b7259c579394a4c585de95de77711c352c0e970`).
- **17 étapes natives réussies**, dont 6 exécutions USMT réelles (sauvegarde chiffrée
  de deux profils, annulation, restaurations saved/current/both et clé erronée),
  chacune précédée de sa fiche (`native-confirmations-<phase>.json`) et de
  « Confirmation Windows acceptée » au journal ; dans ce banc, le harnais répond à la
  place de la fenêtre. Sous le même compte, aucune des six fiches ne propose la case
  de verrouillage (`offerLockAfterStart=false`). **4 phases complémentaires**
  (`plain`, `tamper`, `repair`, `rollback`) ; `plain` refuse dans la confirmation
  (`confirmation.refused`), puis pendant la pause (`confirmation.busy`), puis au
  rejeu du plan refusé (`plan.expired`), sans job ni destination.
- **Démarrage** sur l'installation DEV sous `LABO\Administrateur` : accueil sans
  écran d'entrée ; Paramètres « Installée : 2.0.9 ». **Fichier en trop** : copie du
  pack DEV (914 fichiers et le manifeste) plus `desktop.ini`, seul le refus nommé.
  **Démonstration** : sauvegarde fictive de deux profils terminée ; profils, réglages
  et opérations identiques avant et après, aucune destination créée.
- **Mise à jour réelle et retour arrière** : transaction
  `eb8680213aca4664909e8e800d96a963`, 2.0.8 publiée → 2.0.9 jusqu'à `Committed`, puis
  `RolledBack` dans la même transaction après la fenêtre.
- **Reprise par le worker 2.0.9** : transaction jetable
  `23751ab5b59a49f08bea02249d3dce14`, état `RollingBack` reconstruit, deux invocations
  réelles, la seconde sans changement. `fixtureStateSimulated=true`.

Constats du banc : à chaque lancement, la première lecture UI Automation de la page
était vide et la suivante complète. Une fenêtre est restée sans réponse : le banc a
appelé son outil clavier depuis le mauvais dossier et le délai a expiré ; elle a
compté comme un refus, comme prévu. Le déverrouillage de la session Windows de
`FTSTD209` n'a pas abouti : deux saisies du mot de passe sur l'écran de verrouillage
(disposition US, puis AZERTY) ont été refusées par Windows, alors que la même saisie
AZERTY avait ouvert la session ; arrêt volontaire pour ne pas approcher du seuil de
verrouillage du compte. Le nettoyage contrôlé a d'abord échoué, faute du script
`Complete-TestCleanup209.ps1` sur la VM : sortie prématurée conservée à part, script
transféré, nouvelle exécution réussie ; l'empreinte d'inventaire d'avant tests a été
reliée à la main, `bind_step.py` refusant `acd66dad…` comme valeur d'un ancien run,
comme pour 2.0.4 à 2.0.8.

Limites : la fenêtre réelle de la 2.0.9 a été ouverte, sous le même compte, pour le
retour arrière d'une mise à jour (préparation et installation acceptées dans celle de
la 2.0.8), et sous un autre compte pour la sauvegarde ; les autres fiches sont
couvertes par `ConfirmationSheetsTests` et, pour les six exécutions USMT, par les
fiches enregistrées du harnais. Seuls deux motifs de verrouillage ont été constatés en
direct (accord avec la case cochée, Win+L) ; le maintien du verrou après
déverrouillage de la session, l'inactivité, la veille, l'âge de 30 min, les 3 refus,
la déconnexion et la fermeture de session sont couverts par `OperatorPresenceTests`,
`NativeConfirmationTests`, `SituationAdmissionTests` et `NativeWindowPolicyTests`
(notifications WTS de déconnexion et de fermeture, état relu de la session,
déverrouillage sans effet). Le refus des mises à jour par le pont sous un autre compte
est couvert par les tests ; la recette a constaté les commandes masquées. Aucune
revérification d'identité n'est prévue : une personne qui prend le clavier avant le
verrouillage peut encore accepter une fenêtre ou parcourir le poste avec le sélecteur
élevé. La lecture de la page, `CORECLR_*` et `LOCALAPPDATA` restent des risques
acceptés.

Le reçu `delivery/qualified-209/windows-qualification.json` porte `status=passed` et
les huit contrôles critiques. SHA-256 :
`f662e0f7e930af035847db32c733f3171ec207be7334274daa8428755a8fbad9`.
`validation-summary.json` : SHA-256
`c06dc96f8a895a5f8e074d308a8a7ed7966dc9e2d40bc8c5b4985ec1aff1c41e`. L'export des
preuves (84 fichiers, `FILPRO-2.0.9-windows-evidence.zip`, 76 342 octets, SHA-256
`4a2228bc9dee2340fff6d45fd59c2d7d571ed3094b2395375cfe4502731afe59`) a été vérifié
entrée par entrée contre son index. Le PROD officiel a les mêmes 536 entrées (535
fichiers et le manifeste), octet pour octet, que la fixture privée qualifiée, et le
même manifeste (`be8735a4…`).

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.9-win-x64-PROD.zip` | 364 714 515 | `a4e8c6188d5072ab18fdbdb20b05c6d0ddaad96306e6bf79d68351800c18afae` |
| `FILPRO-2.0.9-win-x64-DEV.zip` | 368 890 510 | `a53d12872691223be80d892939738de3c234d321f4a3595460198812cea8bc2b` |

La release publique [v2.0.9](https://github.com/M4D135/FILPRO/releases/tag/v2.0.9),
identifiant `428624519`, a sa cible et son tag sur `5e8fd5e0`, avec `SHA256SUMS` (191
octets, SHA-256 `5c3a4381344240c8737101f0b1328c00f21488158f42342cb02d3ccc281ab426`).
Le probe réel de téléchargement GitHub depuis la 2.0.8, sans installation, confirme
version, source, taille et SHA-256 du PROD ; compilé d'emblée avec `-r osx-arm64
-p:PlatformTarget=ARM64`.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`2f72ff2ee6715ff360c1b00f3fa5d158d034b24c6fb63a6bcdde6044e5d2e096`) retire les tâches
FILPRO-209 et restaure les réglages d'avant recette. Les comptes `FTSTD209` et
`FTADM209`, leurs profils, leur dossier d'affichage sous ProgramData, la stratégie
OOBE de leur première ouverture, leurs tâches de pilotage, leurs mots de passe
temporaires et les 19 fichiers de transfert de l'hôte ont été supprimés ; aucune tâche
FILPRO-209 ni processus de ces comptes ne reste. Les dossiers `U:\FILPRO-REPRO-209`,
`U:\FILPRO-TESTS-209`, `U:\FILPRO-UPDATE-209`, `U:\FILPRO-UPDATE-209-Recovery`,
`U:\FILPRO-V2-209` et la base `U:\FILPRO-V2-208-PROD` restent sur le disque de test
comme pièces. Il reste environ 1,6 Go libres sur `C:` de la VM.

## Recette du 30 octobre 2026 — 2.0.8, source `df83c2f0`

Cette recette porte sur `df83c2f0bf1ff70982a41d5950503a3c11474c31`. EXE Windows x64 :
`172aefc60dd022acf4341fb8ad8e59b4cf80683c86aca14702329d2cfeaa97cc`. Les quatre DLL des
harnais sont retrouvées octet pour octet dans cet EXE. L'inventaire vérifie 366
fichiers source, 535 fichiers PROD et 909 fichiers DEV. Le run GitHub Windows
`40965653416` (52 suites, 408 contrôles réussis) précède cette recette ; les mêmes
suites ont aussi réussi sur la VM.

Changement 2.0.8 : les dix opérations privilégiées (sauvegarde, restauration,
réparation de profil et son retour arrière, ouverture d'un catalogue et récupération
de fichiers, réglages par défaut du pack, préparation, installation et retour arrière
d'une mise à jour) se décident dans « FILPRO — Confirmation Windows », fenêtre modale
de FILPRO élevé. La page, qui s'exécute sous le compte de la session, ne fait plus que
demander ; la table fermée `PrivilegePolicy` est vérifiée à la composition.

- la fiche est construite par FILPRO : chaque fait porte son origine (FILPRO, magasin,
  interface), les caractères de contrôle, bidirectionnels ou de largeur nulle
  s'affichent par leur code (⟨U+202E⟩…), un texte de plus de 2 000 caractères est
  tronqué ; les points d'attention (destination amovible, réseau, absente ou inconnue,
  sauvegarde non chiffrée, empreinte saisie, magasin partiel…) sont calculés par
  FILPRO ; l'empreinte affichée est celle du plan confirmé ;
- « Annuler » a le focus et aucun bouton n'est par défaut ; l'acceptation s'arme 1,5 s
  après l'activation (3 s si FILPRO n'était pas au premier plan) et reste désactivée
  tant que la fenêtre est inactive ou recouverte par la fenêtre d'un autre programme ;
  Échap ou la croix refusent, 120 s sans réponse annulent ; une seule fenêtre à la
  fois, suivie de 5 s de pause (`confirmation.busy`) ; le plan doit rester valable au
  moins 2 min 30 au moment de l'affichage ;
- le reçu de réparation est lié par son SHA-256 au départ et à la première lecture du
  job ; le catalogue est lié à l'empreinte du manifeste examiné ; offres, aperçus et
  transactions sont tenus par FILPRO ;
- chaque décision est journalisée : « Confirmation Windows <opération> : acceptée,
  refusée ou sans réponse (empreinte, situation SameAccount, CrossAccount ou Unknown,
  durée) » ; la situation vient du propriétaire du processus d'affichage ;
- dans la page, le bouton devient « Continuer » et un bandeau signale une confirmation
  en attente ; au démarrage, les startup hooks .NET sont désactivés
  (`StartupHookSupport=false`) et les variables `WEBVIEW2_*` retirées de
  l'environnement.

Les revues adversariales de l'intégration ont fait corriger avant le commit : une
demande pendant la pause rendue comme indisponible, un reçu de réparation relu après
le départ, l'examen du retour arrière sans contrôle d'emplacement, une fiche des
défauts qui répétait la valeur, des fins de ligne prises pour des caractères cachés,
des textes du magasin présentés comme venant de FILPRO, une provenance expirée au bout
de 15 min, la fraîcheur du plan contrôlée après la fenêtre, un lecteur inconnu classé
local, un magasin partiel non signalé, une réduction de la fenêtre principale qui
terminait la décision, la fermeture du propriétaire mal verrouillée et un débordement
de mise en page.

- **Fenêtre réelle, même compte** : retour arrière de la mise à jour
  (`updates.rollback`, empreinte `980A09785115`, `SameAccount`). Un refus et cinq
  absences de réponse (2 min chacune, rien de lancé) pendant la mise au point du
  pilote, puis l'acceptation (85 s) ; la transaction passe alors à `RolledBack`.
- **Sonde UIPI, même compte** : `LABO\Administrateur` ramené à l'intégrité moyenne
  (RID 8192) par un jeton restreint, pendant que la fenêtre attendait. Fenêtre et
  bouton trouvés ; UI Automation ne voit pas le bouton d'acceptation ; `BM_CLICK` et
  `WM_KEYDOWN`/`WM_KEYUP` postés : refus, erreur 5 ; `SendInput` accepté par l'API (2
  événements) sans effet ; la fenêtre reste ouverte.
- **Croisement de comptes**, comptes jetables `FTSTD208` (standard, session ouverte)
  et `FTADM208` (saisi dans l'UAC) : le journal porte « Affichage sous
  FILPRO-W11\FTSTD208, FILPRO élevé sous FILPRO-W11\FTADM208 : autre compte. ». Sous
  `FTSTD208`, UI Automation lit et pilote la page (risque accepté) jusqu'à une
  sauvegarde de son profil avec VSS ; la fenêtre s'ouvre avec « Compte élevé :
  FILPRO-W11\FTADM208 · Affichage : FILPRO-W11\FTSTD208 (autre compte) ». La même
  sonde lancée sous `FTSTD208` (RID 8192) obtient les mêmes refus et la fenêtre reste
  ouverte. Échap au clavier de la VM : « refusée (empreinte CE728C8B40E1, situation
  CrossAccount, 27591 ms) », la page affiche « Opération refusée … rien n'a été
  lancé » et la destination n'existe pas. Constat manuel hors des huit contrôles du
  validateur ; pièces sous `final-20261029-208/repro/` et `repro-evidence/`.
- **17 étapes natives réussies**, dont 6 exécutions USMT réelles (sauvegarde chiffrée
  de deux profils, annulation, restaurations saved/current/both et clé erronée),
  chacune précédée de sa fiche (`native-confirmations-<phase>.json`) et de
  « Confirmation Windows acceptée » au journal ; dans ce banc, le harnais répond à la
  place de la fenêtre. **4 phases complémentaires** (`plain`, `tamper`, `repair`,
  `rollback`) avec préflight final ; `plain` refuse dans la confirmation
  (`confirmation.refused`), puis pendant la pause (`confirmation.busy`), puis sur un
  plan expiré (`plan.expired`), sans job ni destination.
- **Démarrage** sur l'installation DEV sous `LABO\Administrateur` : accueil sans
  écran d'entrée ; « Replier le menu » ; Paramètres « Installée : 2.0.8 ». **Fichier
  en trop** : copie de 910 fichiers, seul le refus nommé (`desktop.ini`).
  **Démonstration** : sauvegarde fictive de deux profils terminée, profils et réglages
  identiques avant et après.
- **Mise à jour réelle et retour arrière** : transaction
  `ed026ef2b2164042bdc2ad832d0be8c5`, 2.0.7 publiée → 2.0.8 jusqu'à `Committed`, puis
  `RolledBack` dans la même transaction après la fenêtre.
- **Reprise par le worker 2.0.8** : transaction jetable
  `b25a4b0961a14538a0771fc59c22422d`, état `RollingBack` reconstruit, deux invocations
  réelles, la seconde sans changement. `fixtureStateSimulated=true`.

Constats du banc : UI Automation range la fenêtre de confirmation sous la fenêtre
principale qui la possède ; une console ouverte par la recette la recouvrait et
désactivait l'acceptation, comme prévu : pilote et sondes passent par `conhost.exe
--headless` ; le pilote élevé active la fenêtre avant d'accepter. `runas
/trustlevel:0x20000` laisse l'administrateur intégré à l'intégrité haute : la sonde
part d'une copie restreinte du jeton ramenée à Medium, et sa classe est compilée à
l'avance, PowerShell ne pouvant pas lancer le compilateur à ce niveau.

Limites : la fenêtre réelle a été ouverte pour deux des dix opérations (retour arrière
de mise à jour, sauvegarde). Les autres fiches sont couvertes par
`ConfirmationSheetsTests` et, pour les six exécutions USMT, par les fiches
enregistrées du harnais. En session « autre compte », seul le refus a été exercé. La
lecture de la page, `CORECLR_*` et `LOCALAPPDATA` restent des risques acceptés ; le
verrou de présence, le refus des mises à jour sous un autre compte et les autres
protections « autre compte » sont prévus en 2.0.9.

Le reçu `delivery/qualified-208/windows-qualification.json` porte `status=passed` et
les huit contrôles critiques. SHA-256 :
`bd3d5327cce28f9ddea3cbf0ac9043ef6f9b7259fa6aaac91f75bb032fcf5655`.
`validation-summary.json` : SHA-256
`c80fc5804753d3fa38fbd42179b7d95863682588d1b6ecb5ea4caa7336645fb4`. L'export des
preuves (84 fichiers, `FILPRO-2.0.8-windows-evidence.zip`, 74 952 octets, SHA-256
`3ddda78c622bbd3b5822d03dd934337457233d70edee7fddb8ded32e4185c129`) a été vérifié
entrée par entrée contre son index. Le PROD officiel a les mêmes 535 entrées, octet
pour octet, que la fixture privée qualifiée, et le même manifeste (`d442bcee…`).

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.8-win-x64-PROD.zip` | 364 705 317 | `1784388a3b5942429b2241a790cdb537eb382c82c32369f8d5521edc2ec39360` |
| `FILPRO-2.0.8-win-x64-DEV.zip` | 368 850 961 | `b6b93dc1c47be36956e2231703ba47050ccb7fe8ec5c37e69a1ecff3d7c7e657` |

La release publique [v2.0.8](https://github.com/M4D135/FILPRO/releases/tag/v2.0.8),
identifiant `428523711`, a sa cible et son tag sur `df83c2f0`, avec `SHA256SUMS` (191
octets, SHA-256 `32455506f94bf1c0a237499674b3c01218bdc4d06ac1a8b212b3640b6d0e03d8`).
Le probe réel de téléchargement GitHub depuis la 2.0.7, sans installation, confirme
version, source, taille et SHA-256 du PROD. Il doit être compilé avec `-r osx-arm64
-p:PlatformTarget=ARM64` : sans ces propriétés globales, Core et Platform sortent en
win-x64 et le probe ne les charge pas (première tentative conservée à part).

Nettoyage réussi : `cleanup-result.json` (SHA-256
`459679b97add3826f0a294adfa557bb5c760947fbfa566f7f38d913f6a0c7eaf`) retire les tâches
FILPRO-208 et restaure les réglages d'avant recette. Les comptes `FTSTD208` et
`FTADM208`, leurs profils, leur dossier d'affichage sous ProgramData, la stratégie
OOBE de leur première ouverture, leurs tâches de pilotage et leurs mots de passe
temporaires sur l'hôte ont été supprimés ; aucune tâche FILPRO-208 ni processus ne
reste. Les dossiers `U:\FILPRO-REPRO-208`, `U:\FILPRO-TESTS-208`,
`U:\FILPRO-UPDATE-208`, `U:\FILPRO-UPDATE-208-Recovery` et `U:\FILPRO-V2-208` restent
sur le disque de test comme pièces. Il reste environ 2 Go libres sur `C:` de la VM.

## Recette du 29 octobre 2026 — 2.0.7, source `1e459f30`

Cette recette porte sur `1e459f304c1246c0e74342eeca91dc9125f54d19`.
EXE Windows x64 : `281e1f20060b480fbe76c03cfcf6ecef16e693f863f1d3f0095b60bc87dd5271`.
Les quatre DLL des harnais sont retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 349 fichiers source, 534 fichiers PROD et 891 fichiers DEV (deux
sources ajoutées). Le run GitHub Windows `40903450388` (47 suites, 357 contrôles
réussis, aucun ignoré, dont les quatre `SessionAccessTests`) précède cette recette.
Avant le commit, `WebViewDataLocationTests`, `UserDataLocationTests` et
`SessionAccessTests` ont aussi tourné sur la VM, élevé, en session interactive : 18
sur 18, sans test ignoré.

Changement 2.0.7 : élevé avec un autre compte que celui de la session, WebView2 lance
son moteur par l'Explorateur sous le compte de la session ; si ce compte ne peut pas
lire le pack, Windows affiche son propre refus et la création de l'environnement ne
rend jamais la main : FILPRO restait sur « Initialisation de l'interface… » et ne
pouvait plus être fermé. Désormais :

- les droits effectifs du compte de la session sur le dossier du moteur, son
  exécutable, le dossier de l'interface et sa page d'accueil sont évalués par Authz,
  avec les groupes d'une session ouverte (Tout le monde, INTERACTIVE, utilisateurs
  authentifiés, LOCAL, console, bureau à distance, cette organisation) ; un pack sur
  un partage réseau, une entrée de refus visant un groupe d'ouverture incertain ou
  tout échec d'API laissent le résultat indéterminé ;
- si un élément est illisible et que le démarrage est toujours en cours 5 s plus
  tard, l'écran le nomme avec le conseil de placement ; sinon un démarrage encore en
  cours au bout de 25 s est signalé avec ce conseil. Rien n'interrompt ni ne refuse
  le démarrage ;
- la fermeture n'attend plus un démarrage bloqué ; le texte d'état n'interprète plus
  « & » comme raccourci clavier.

Deux revues adversariales indépendantes ont précédé le commit. La première (trois
angles, un contradicteur par constat) a relevé, dans la première version, la
fermeture impossible pendant un démarrage bloqué, l'absence d'INTERACTIVE dans le
contexte Authz, les partages réseau, un minuteur démarré trop tard, quatre contextes
Authz au lieu d'un et des tests trop faibles : tout est corrigé. La seconde a relevé
le « & » du texte d'état, corrigé ; elle a écarté le refus visant NETWORK, déjà
couvert par la règle élargie, et le test ignoré sur un domaine injoignable.

- **Croisement de comptes**, comptes jetables `FTSTD207` (standard, session ouverte)
  et `FTADM207` (saisi dans l'UAC) : moteur lisible, la 2.0.7 ouvre l'accueil sans
  avertissement au journal. Lecture et exécution refusées à `FTSTD207` sur
  `wad\runtime` : Windows signale l'accès refusé à `msedgewebview2.exe` ; 5 s après la
  préparation du profil, le journal porte « Le compte de la session FILPRO-W11\FTSTD207
  ne peut pas lire U:\FILPRO-REPRO-207\2.0.7-DEV\wad\runtime\webview2 » et l'écran
  nomme ce dossier avec le conseil de placement ; Alt+F4 ferme alors FILPRO (aucun
  processus restant). Seul `msedge.dll` refusé : WebView2 échoue aussitôt
  (`CO_E_SERVER_EXEC_FAILURE`) et le message d'échec porte le compte et le conseil.
  L'avertissement des 25 s n'a pas été observé en réel ; il est couvert par les tests
  de `SessionAccess.WatchAsync`. Constat manuel hors des huit contrôles du validateur ;
  pièces sous `final-20261029-207/repro/`.
- **17 étapes natives réussies**, dont 6 exécutions USMT réelles, et **4 phases
  complémentaires** (`plain`, `tamper`, `repair`, `rollback`) avec préflight final.
- **Démarrage** sur l'installation DEV sous `LABO\Administrateur` : accueil sans
  écran d'entrée ; « Replier le menu » ; Paramètres « Installée : 2.0.7 ».
  **Fichier en trop** : copie de 892 fichiers, seul le refus nommé (`desktop.ini`).
  **Démonstration** : sauvegarde fictive de deux profils terminée, profils et réglages
  identiques avant et après.
- **Mise à jour réelle et retour arrière** : transaction
  `6088943fe56444d180883dedd568a586`, 2.0.6 publiée → 2.0.7 jusqu'à `Committed`, puis
  `RolledBack` dans la même transaction ; chaque version rouvre directement l'accueil.
- **Reprise par le worker 2.0.7** : transaction jetable
  `3d0a617c174a4851aa68882aa20199e3`, état `RollingBack` reconstruit, deux invocations
  réelles, la seconde sans changement. `fixtureStateSimulated=true`.

Le reçu `delivery/qualified-207/windows-qualification.json` porte `status=passed`
et les huit contrôles critiques. SHA-256 :
`e24060012e0e8091a25a0acd9bffa31f7f89113ff483119b928c3d4128131656`.
`validation-summary.json` : SHA-256
`3134f557023148703ade5319a43abcea79c99f16086ccf53f4a992045c6593f8`.
L'export des preuves (78 fichiers, `FILPRO-2.0.7-windows-evidence.zip`, 63 707
octets, SHA-256 `9e6d938c2a85fb7b399a94f7a2a6d2eae809359c44a36666c13c3d9c6c1e7eca`)
a été vérifié entrée par entrée contre son index. Le PROD officiel a les mêmes 535
entrées, octet pour octet, que la fixture privée qualifiée.

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.7-win-x64-PROD.zip` | 364 658 417 | `582abafe5fb607b1f7ba8f226e1dc6da10cb0c183e7fbf0ce0e1116a7b9e205f` |
| `FILPRO-2.0.7-win-x64-DEV.zip` | 368 715 678 | `dd49c26a717fa75eaa5649bd540fd9f901fa454e8c4a351c5633b011dbd19f4a` |

La release publique
[v2.0.7](https://github.com/M4D135/FILPRO/releases/tag/v2.0.7), identifiant
`428130691`, a sa cible et son tag sur `1e459f30`, avec `SHA256SUMS` (191 octets,
SHA-256 `195fd09c3909d9acc87fa2951df485225ea71e735932615cc778f2277f528d25`). Le
probe réel de téléchargement GitHub, sans installation, confirme version, source,
taille et SHA-256 du PROD.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`338b14682b67c76f3175e228e0b6b3700456bae9c7e096e4a8c2a30507a3f641`) retire les
tâches FILPRO-207 et restaure les réglages d'avant recette ; aucune tâche FILPRO-207 ni
processus ne reste. Les comptes `FTSTD207` et `FTADM207`, leurs profils, leur dossier
d'affichage sous ProgramData, la stratégie OOBE de leur première ouverture et leurs
mots de passe temporaires sur l'hôte ont été supprimés. Les dossiers
`U:\FILPRO-REPRO-207` et `U:\FILPRO-TESTS-207` restent sur le disque de test comme
pièces.

## Recette du 29 octobre 2026 — 2.0.6, source `141f3ca3`

Cette recette porte sur `141f3ca31ce2f9809f31b51102b3e3243d1b291a`.
EXE Windows x64 : `c0713a379932c47a8bbbb82d7bebd46b8b46bd5f96a616ca14ece12a59190f79`.
Les quatre DLL des harnais sont retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 347 fichiers source, 534 fichiers PROD et 889 fichiers DEV.
Le run GitHub Windows `40865532403` (46 suites, 353 contrôles réussis, dont les dix
`WebViewDataLocationTests` et les quatre `UserDataLocationTests`) précède cette
recette. Avant le commit, ces deux suites ont aussi tourné sur la VM (Windows 11 x64,
élevé, session interactive de l'Explorateur) : 14 sur 14.

Changement 2.0.6, durcissement du dossier d'affichage de 2.0.5 :

- listes d'accès lues brutes (`GetSecurityInfo`) sur le handle épinglé, sans
  relecture par chemin ; liste absente, ou entrée d'un autre type qu'autorisation ou
  refus simples, refusée ; une entrée conditionnelle compte comme accordée ;
- bits génériques convertis en droits de fichier ; hors SYSTEM et Administrateurs, un
  autre compte ne peut que lire, pour le dossier final, ses parents et le dossier de
  secours des données ;
- dossier final exact : ni refus explicite ni entrée héritée ; le compte de la session
  tient Modification et Synchronize, transmis au dossier et à tout son contenu, sans
  droit sur les autorisations ni le propriétaire ;
- compte de la session propre à chaque fenêtre, rendu seulement après une préparation
  réussie ; nom de profil vérifié dans les deux emplacements ; le conseil d'échec
  désigne un dossier lisible mais non modifiable par ce compte
  (`C:\Program Files\FILPRO`), car FILPRO s'exécute toujours élevé.

Deux revues adversariales indépendantes ont précédé le commit. La première (quatre
angles, un contradicteur par constat) a confirmé les bits génériques, Synchronize, le
compte de session statique et les entrées que .NET masque ; elle a écarté la course
sur un dossier final transformé en point de montage, sans gain pour le compte de la
session. La seconde n'a relevé qu'un test trop faible, corrigé avant le commit.

- **Croisement de comptes**, comptes jetables `FTSTD206` (standard, session ouverte)
  et `FTADM206` (saisi dans l'UAC) : la 2.0.5 publiée crée
  `…\WebView2\<SID FTSTD206>\<SID FTADM206>\WebView2`
  (`D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;<session>)`) et ouvre
  l'accueil ; la 2.0.6 réutilise ce dossier (même chemin journalisé) et ouvre l'accueil,
  le moteur du pack tournant sous `FTSTD206`. Après
  `icacls … /grant *S-1-1-0:(OI)(CI)(IO)(GW)`, la 2.0.6 refuse le dossier (« Écriture
  accordée à un compte non autorisé (S-1-1-0) ») et n'affiche que le message générique.
  Constat manuel hors des huit contrôles du validateur ; pièces sous
  `final-20261029-206/repro/`.
- **Limite observée** : moteur du pack rendu illisible au compte de la session (refus
  de lecture sur `wad\runtime`), Windows affiche son propre message d'accès refusé à
  `msedgewebview2.exe`, lancé par l'Explorateur sous ce compte, et FILPRO reste sur
  « Initialisation de l'interface… » : le conseil n'est pas atteint. Même
  comportement en 2.0.5 ; à traiter à part.
- **17 étapes natives réussies**, dont 6 exécutions USMT réelles, et **4 phases
  complémentaires** (`plain`, `tamper`, `repair`, `rollback`) avec préflight final.
- **Démarrage** sur l'installation DEV sous `LABO\Administrateur` : accueil rendu
  dans le même processus, sans écran d'entrée ; « Replier le menu » ; Paramètres
  « Installée : 2.0.6 ». **Fichier en trop** : seul le refus nommé (`desktop.ini`).
  **Démonstration** : sauvegarde fictive de deux profils terminée, profils et réglages
  identiques avant et après.
- **Mise à jour réelle et retour arrière** : transaction
  `9f681e34851f4716bfa3d56e7af91f0c`, 2.0.5 publiée → 2.0.6 jusqu'à `Committed`, puis
  `RolledBack` dans la même transaction ; chaque version rouvre directement l'accueil.
- **Reprise par le worker 2.0.6** : transaction jetable
  `e0a35e9e4ab240a4ad8a2134113dcb85`, état `RollingBack` reconstruit, deux invocations
  réelles, la seconde sans changement. `fixtureStateSimulated=true`.

Le reçu `delivery/qualified-206/windows-qualification.json` porte `status=passed`
et les huit contrôles critiques. SHA-256 :
`bb66c89eaccb467872839854fc4c2e23670c2e7f38ce6032bff7930ca584be7a`.
L'export des preuves (78 fichiers, `FILPRO-2.0.6-windows-evidence.zip`, 62 828
octets, SHA-256 `20296576e28832cd84a25518cac2bbfd9bdbd24265cfa2a15168281688e554be`)
a été vérifié entrée par entrée contre son index. Le PROD officiel a les mêmes 535
entrées, octet pour octet, que la fixture privée qualifiée.

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.6-win-x64-PROD.zip` | 364 653 876 | `1e6a52c2c81fe75b705fc12b376094fecb3c6ea88b1b97e3879df0abe338e89d` |
| `FILPRO-2.0.6-win-x64-DEV.zip` | 368 699 688 | `7fcd7ed2a81a43b727ff5d61e4e735193597e452a1fb0d9bee6a5919c681711e` |

La release publique
[v2.0.6](https://github.com/M4D135/FILPRO/releases/tag/v2.0.6), identifiant
`427845713`, a sa cible et son tag sur `141f3ca3`, avec `SHA256SUMS` (191 octets,
SHA-256 `5fb94939b32ea703f00f0a1ba52947899887ebf8cede0a6b1741b611def1e0a1`). Le
probe réel de téléchargement GitHub, sans installation, confirme version, source,
taille et SHA-256 du PROD.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`7837360b6b22676b12d46f13b917fb0d5b93a6ae8c6ae4db9a890b683feae3dc`) retire les
tâches FILPRO-206 et restaure les réglages d'avant recette ; aucune tâche FILPRO-206 ni
processus ne reste. Les comptes `FTSTD206` et `FTADM206`, leurs profils, leur dossier
d'affichage sous ProgramData et la stratégie OOBE posée pour leur première ouverture
ont été supprimés, ainsi que leurs mots de passe temporaires sur l'hôte. Les dossiers
`U:\FILPRO-REPRO-206` et `U:\FILPRO-TESTS-206` restent sur le disque de test comme
pièces.

## Recette du 29 octobre 2026 — 2.0.5, source `6292fff9`

Cette recette porte sur `6292fff9b79b08e37d6782f0b0072094e370949b`.
EXE Windows x64 : `b39010148f5d0fb5af168e3b2553049cf65e1703d5d8b2112dbf3954a644753a`.
Les quatre DLL des harnais sont retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 347 fichiers source, 534 fichiers PROD et 889 fichiers DEV.
Le run GitHub Windows `40843957062` (46 suites, 346 contrôles réussis, dont les
quatre `WebViewDataLocationTests`) précède cette recette.

Changement 2.0.5 : quand un utilisateur standard ouvre FILPRO en saisissant un autre
compte administrateur dans l'UAC, WebView2 lance ses processus sous le compte de la
session (comportement documenté, WebView2Feedback #932), qui ne peut pas écrire dans
le profil de l'administrateur : « Création du répertoire de données impossible ».
Dans ce seul cas, le profil d'affichage est créé sous
`C:\ProgramData\FILPRO-V2\WebView2\<SID session>\<SID FILPRO>\<profil>` : parents
réservés à SYSTEM et aux Administrateurs, dossier final avec en plus Modification
pour le compte de la session, sans droit de changer autorisations ni propriétaire ;
un dossier existant à protection différente est refusé, jamais réparé. Le journal,
les réglages et les opérations restent dans le profil de l'administrateur.

- **Reproduction et correction**, comptes jetables `FTSTD205` (standard, session
  ouverte) et `FTADM205` (administrateur saisi dans l'UAC) : la 2.0.4 publiée affiche
  l'erreur du poste réel (`C:\Users\FTADM205\…\EBWebView`), msedgewebview2 tournant
  sous `FTSTD205` ; la 2.0.5 (même EXE que le PROD publié) ouvre l'accueil et
  journalise « Affichage WebView2 exécuté par Windows sous le compte de la session
  FILPRO-W11\FTSTD205 (FILPRO élevé sous FILPRO-W11\FTADM205) » ; ACL relues
  conformes. Constat manuel hors des huit contrôles du validateur ; pièces sous
  `final-20261029-205/repro/`. Lancé normalement, sans tâche planifiée, le lien
  « Aller au contenu » n'est pas visible au démarrage (observation 2.0.4 levée).
- **17 étapes natives réussies**, dont 6 exécutions USMT réelles, sur les profils
  jetables F2QA/F2QB (`native-evidence/profiles-205-suite.json`), et **4 phases
  complémentaires** (`plain`, `tamper`, `repair`, `rollback`) avec préflight final.
- **Démarrage** sur l'installation DEV sous `LABO\Administrateur` (session et
  FILPRO sous le même compte : aucun déplacement du profil) : accueil rendu dans le
  même processus, sans écran d'entrée ; « Replier le menu » ; Paramètres
  « Installée : 2.0.5 ».
- **Fichier en trop**, non-régression : copie vérifiée du pack DEV installé (889
  fichiers + manifeste) avec `desktop.ini` à côté de `FILPRO.exe` ; seul le refus
  nommé s'affiche, sans « Manifeste de pack refusé ».
- **Démonstration** : sauvegarde fictive de deux profils terminée, empreintes des
  profils et réglages identiques avant et après.
- **Mise à jour réelle et retour arrière** : transaction
  `149c4da41c234105b29eabf52d405ca3`, 2.0.4 publiée → 2.0.5 jusqu'à `Committed`,
  la 2.0.5 ouvre directement l'accueil ; retour arrière dans la même transaction
  jusqu'à `RolledBack`, la 2.0.4 rouvre directement l'accueil.
- **Reprise par le worker 2.0.5** : transaction jetable
  `db2e5078ac4f4c5b9bb31fca883b8164`, état `RollingBack` reconstruit, deux
  invocations réelles, la seconde sans changement. `fixtureStateSimulated=true` :
  pas une coupure électrique réelle.

Le reçu `delivery/qualified-205/windows-qualification.json` porte `status=passed`
et les huit contrôles critiques. SHA-256 :
`296c468c344ec62c6863d2cb9bb6e33c87b52751aae7d5b3bff077daa0e3f080`.
`validation-summary.json` : SHA-256
`7f5b4320caa4e0f20668d1216b15be6ca4569a694a24d8831fbbc590a223b0b4`.

Une revue indépendante du correctif (Spark) a avancé deux défauts vérifiables, réfutés
avant publication : la structure `WTS_PROCESS_INFO_EXW` déclarée fait bien 64 octets
sur x64 (énumération réelle dans la VM : 48 processus sur 48 relus avec PID et nom),
et le dossier d'affichage de la démonstration n'est jamais écrit par le processus
élevé (il sert seulement à un message de journal). Durcissements possibles, non
bloquants : refuser aussi les ACE `Deny` et exiger l'héritage exact sur un dossier
final existant, ne renseigner le compte de session qu'après préparation réussie,
tests plus stricts.

Limite : dans ce cas, l'affichage tourne sous le compte standard de la session, qui
peut agir sur ses propres processus et donc sur l'interface de FILPRO élevé ; les
opérations restent celles du pont typé. Ne saisir un compte administrateur que dans
la session d'un utilisateur de confiance. Une confirmation native des opérations
privilégiées fermerait ce point.

L'export des preuves (78 fichiers, `FILPRO-2.0.5-windows-evidence.zip`, 61 917
octets, SHA-256 `2b9e9b5054caf3ccc714b06c57496daed31206cd083aec29dc304698658adb66`)
a été vérifié entrée par entrée contre son index. Le PROD officiel a les mêmes 535
entrées, octet pour octet, que la fixture privée qualifiée.

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.5-win-x64-PROD.zip` | 364 649 740 | `23cd2f512e6fe45a4b86addb7f288b732d5eb69a626a6225df95d23a898a803f` |
| `FILPRO-2.0.5-win-x64-DEV.zip` | 368 688 480 | `026ae219fd4680c795f39797d8b3f386886f515387f3477f4fa9c727ab590097` |

La release publique
[v2.0.5](https://github.com/M4D135/FILPRO/releases/tag/v2.0.5), identifiant
`427684182`, a sa cible et son tag sur `6292fff9`, avec `SHA256SUMS` (191 octets,
SHA-256 `1c74367965f1eb59a3b84b3eecc27d85620c9b532d2c6fc0d6053fa265d05e88`). Le
probe réel de téléchargement GitHub, sans installation, confirme version, source,
taille et SHA-256 du PROD.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`bc1c9bdb2a68d57c34fa5fce2380bf53d5c5e470d24a67497e513bf5ab84e9c8`) retire les
tâches FILPRO-205 et restaure les réglages d'avant recette ; le contrôle après
nettoyage confirme aucune tâche FILPRO-205 ni processus. Les comptes `FTSTD205` et
`FTADM205` (profils, dossiers, entrées ProfileList), leur dossier d'affichage sous
ProgramData et la stratégie OOBE posée pour leur première ouverture ont été
supprimés ; leurs mots de passe temporaires ont été effacés de l'hôte.

## Recette du 29 octobre 2026 — 2.0.4, source `e981b00d`

Cette recette porte sur `e981b00de23681753fd1249a12cdd54d95dc7e3b`.
EXE Windows x64 : `41d2b2a410d035d5935187aea4f08a9f808b11a5057159a1a6ed2362fb763f67`.
Les quatre DLL des harnais sont retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 345 fichiers source, 534 fichiers PROD et 887 fichiers DEV.
Le run GitHub Windows `40823090614` (45 suites, 342 contrôles réussis, dont
`PackIntegrityTests.PackExtraitParmiDAutresFichiersNommeLeFichierEnTrop`) précède
cette recette.

Changements 2.0.4 : un élément étranger à côté de `FILPRO.exe` est refusé en le
nommant (« Fichier non prévu dans le dossier de FILPRO : <nom>. Extraire le zip
complet dans un dossier vide réservé à FILPRO. ») au lieu de « Manifeste de pack
refusé » ; interface alignée sur DOMENIA (icônes immobiles, pastilles et encadrés
sans collision) ; repli du menu harmonisé avec DOMENIA, PESTO et GLPI (décision du
29 octobre) : libellé « Replier le menu » visible menu déplié, icône seule menu
replié, toujours visible en bas.

Un premier passage sur le candidat `a45a7bb` (icône seule) a été arrêté après la
décision sur le libellé ; ses preuves sont conservées (VM :
`U:\FILPRO-V2-204-a45a7bb`, `U:\FILPRO-UPDATE-204-a45a7bb` ; Mac :
`final-20261029-204/`). La recette ci-dessous a été entièrement refaite sur
`e981b00`. Pièces locales : `work/filpro-finalisation/resume-20261028/final-20261029-204b/`.

- **17 étapes natives réussies**, dont 6 exécutions USMT réelles, sur les profils
  jetables F2QA/F2QB (`native-evidence/profiles-204-suite.json`), et **4 phases
  complémentaires** (`plain`, `tamper`, `repair`, `rollback`) avec préflight final.
- **Démarrage** sur l'installation DEV : première observation pendant la
  vérification du pack (« Vérification du pack… »), puis l'accueil rendu dans le
  même processus, sans écran d'entrée ; libellé « Replier le menu » en bas du menu
  déplié. Paramètres « Installée : 2.0.4 ».
- **Fichier en trop** : copie vérifiée du pack DEV installé (887 fichiers +
  manifeste, même EXE que PROD) avec `desktop.ini` à côté de `FILPRO.exe` ; FILPRO
  affiche le seul refus nommé, sans « Manifeste de pack refusé »
  (`ui-stray-file.json`, `stray-evidence/stray-prepare-result.json`, tous deux
  contrôlés par le validateur). Le cas exact d'un PROD extrait sur le Bureau n'a
  pas été rejoué tel quel ; l'EXE et la règle sont identiques.
- **Démonstration** : sauvegarde fictive de deux profils terminée, empreintes des
  profils identiques avant et après.
- **Mise à jour réelle et retour arrière** : transaction
  `ecb82916f44b48dca0c00cb657a4d2ee`, 2.0.3 publiée → 2.0.4 jusqu'à `Committed`,
  la 2.0.4 ouvre directement l'accueil ; retour arrière dans la même transaction
  jusqu'à `RolledBack`, la 2.0.3 rouvre directement l'accueil.
- **Reprise par le worker 2.0.4** : transaction jetable
  `f6078ed13f8e4f47af882147f0b7a723`, état `RollingBack` reconstruit, deux
  invocations réelles, la seconde sans changement. `fixtureStateSimulated=true` :
  pas une coupure électrique réelle.

Le reçu `delivery/qualified-204/windows-qualification.json` porte `status=passed`
et les huit contrôles critiques. SHA-256 :
`7b2f1b26e21f32f1518bc8d1e7761d28f8055692746869f95c7a730ffe264708`.
`validation-summary.json` : SHA-256
`528ee9af54103a1a3d2a148ff6dacbd8a4ade47589392b714acb83fd9a50dd63`.

Avant le reçu, une revue indépendante a relevé que le contrôle du fichier en trop
ne vérifiait pas la préparation du dossier et acceptait un nom plus long ; le
validateur lit désormais le rapport de préparation et exige le nom exact, et le
texte du reçu précise qu'il s'agit d'une copie du pack DEV. Le menu replié, les
pastilles et les encadrés ont été vérifiés dans un navigateur (7 écrans, 4 tailles
de fenêtre) et par la CI, pas par la recette VM.

L'export des preuves (78 fichiers, `FILPRO-2.0.4-windows-evidence.zip`, 60 999
octets, SHA-256 `aa92003c24105d490cdc6849c0ae06fb7d98d5d484d96cb329bfd96bab1af05a`)
a été vérifié entrée par entrée contre son index.

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.4-win-x64-PROD.zip` | 364 644 153 | `8f955b69a309376daefcdc3bc5434eafaba980088dbc6cc44d54f5e9ba7e74eb` |
| `FILPRO-2.0.4-win-x64-DEV.zip` | 368 673 389 | `7be431b803016e9abe3bbeaddfd33c45409c0212c7169e7a3b2e7fbd2335bd63` |

La release publique
[v2.0.4](https://github.com/M4D135/FILPRO/releases/tag/v2.0.4), identifiant
`427538279`, a sa cible et son tag sur `e981b00d`, avec `SHA256SUMS` (191 octets,
SHA-256 `413e8e2aef14875c87cdd4631dccb759150f96d2986f360d7a78087c14c4d836`). Le
probe réel de téléchargement GitHub, sans installation, confirme version, source,
taille et SHA-256 du PROD.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`daaa2d2326ce19ab02d4e091c8ed4c4976a341728874fa6d92f177a73a58e4d7`) retire les
tâches FILPRO-204 et restaure les réglages d'avant recette ; le préflight après
nettoyage confirme deux profils déchargés, aucune ruche et aucun processus. Le
profil du compte de test UAC `FTADM203`, n'étant plus chargé, a été supprimé
(profil, dossier et entrée ProfileList) sans redémarrage.

Observation : dans la VM, lancé par tâche planifiée, le lien d'accessibilité
« Aller au contenu » est focalisé et visible au démarrage ; il ne l'est pas sur les
captures d'un lancement normal. À confirmer sur un poste réel.

## Recette du 28 octobre 2026 — 2.0.3, source `395bb12c`

Cette recette porte sur `395bb12c541b32655109732895cec79117611f30`.
EXE Windows x64 : `1811bcda64401215bdcbf1bd5e99d84cf02cc5a0f428d2dfde663f4690f66c18`.
Les quatre DLL des harnais sont retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 345 fichiers source, 534 fichiers PROD et 887 fichiers DEV.
Le run GitHub Windows `40725809121` (45 suites, 341 contrôles réussis, dont
`StartupPolicyTests` et `UserDataLocationTests`) précède cette recette.

Changement de comportement vérifié : un lancement déjà élevé ouvre directement la
fenêtre principale « FILPRO », sans écran « FILPRO · Connexion ».

Les pièces locales sont conservées hors du dépôt, sous
`work/filpro-finalisation/resume-20261028/final-20261028-203/` :

- **Parcours UAC** (avant la recette complète, même EXE, comptes jetables
  supprimés ensuite) : la demande UAC précède toute fenêtre FILPRO ; une
  annulation affiche l'écran d'entrée avec son message et des boutons actifs ;
  une acceptation avec un autre compte administrateur ouvre directement
  l'accueil sous ce compte, WebView2 fonctionnel ; un dossier local rendu
  inutilisable bascule vers `ProgramData\FILPRO-V2\Users\<SID>`, propriétaire
  Administrateurs, héritage coupé, SYSTEM et Administrateurs seuls. Pièce :
  `recette-uac-autre-admin-203.json`. L'erreur Edge exacte du poste d'origine n'a
  pas été reproduite.
- **17 étapes natives réussies**, dont 6 exécutions USMT réelles (annulation,
  sauvegarde chiffrée, restaurations saved/current/both, mauvaise clé refusée),
  sur les profils jetables F2QA/F2QB. Les 17 codes de sortie sont nuls dans
  `native-evidence/profiles-203-suite.json`.
- **4 phases complémentaires réussies** : `plain`, `tamper`, `repair`, `rollback`,
  avec préflight final passé.
- **Démarrage et démonstration** sur l'installation DEV : la première capture
  montre la fenêtre « FILPRO » en initialisation, la suivante l'accueil rendu, dans
  le même processus ; aucune n'affiche l'écran d'entrée. Paramètres « Installée :
  2.0.3 » sans option MigApp générale. La sauvegarde fictive de deux profils s'est
  terminée ; `demo-before.json` et `demo-after.json` sont identiques hors horodatage.
- **Mise à jour réelle et retour arrière** : transaction
  `4b8e112a26bf40e99d6269c07a1a2bf4`, 2.0.2 publiée → 2.0.3. Elle a atteint
  `Committed` ; la 2.0.3 installée ouvre directement l'accueil. Retour arrière
  dans la même transaction jusqu'à `RolledBack`, la 2.0.2 réaffiche son écran
  d'entrée ; `health.json` reste identique octet pour octet.
- **Reprise par le worker 2.0.3** : transaction jetable
  `16c97cc212884353a802c717a6cb763a`, état `RollingBack` reconstruit après le
  déplacement final, deux invocations réelles, la seconde sans changement.
  `fixtureStateSimulated=true` : pas une coupure électrique réelle.

Le reçu `delivery/qualified-203/windows-qualification.json` porte `status=passed`
et les huit contrôles critiques. SHA-256 :
`c787df707030819b9f26ae6b4b8e060046b1a655b56b5cd7225d8f0f74f768b3`.
`validation-summary.json` : SHA-256
`ca40a3b17df2731320f0350e755a8573440565eb17227d2c15cb468fe54e2a5f`.

Avant publication, une revue indépendante en lecture seule a relevé que le reçu
décrivait la « première observation » comme la fenêtre principale rendue, alors
que la première capture montrait cette fenêtre en initialisation. Le validateur
contrôle désormais aussi cette capture (même processus, antérieure, sans écran
d'entrée), la formulation est corrigée ; le reçu et le PROD ont été régénérés,
les sorties précédentes conservées dans `superseded-before-review/`.

L'export des preuves (78 fichiers, `FILPRO-2.0.3-windows-evidence.zip`, 58 973
octets, SHA-256 `0b788d37ef814cd775d2035293aa31186d57c025b66a7415b1fd6821095bb684`)
a été vérifié entrée par entrée contre son index.

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.3-win-x64-PROD.zip` | 364 642 673 | `005461ee0d0df8bfc800736b8892777fe0815557941db31a3397ec6478f66926` |
| `FILPRO-2.0.3-win-x64-DEV.zip` | 368 667 322 | `2e1773f8fb8ff6913a7d75ec87a7d7c31b8b52e89e0d3b0629ff778de3357f5d` |

La release publique
[v2.0.3](https://github.com/M4D135/FILPRO/releases/tag/v2.0.3), identifiant
`427087235`, a sa cible et son tag sur `395bb12c`, avec `SHA256SUMS` (191 octets,
SHA-256 `28036b06f8bfacaf051a21f593542bd7dba5e62c885704ff86039514817b4356`). Le
probe réel de téléchargement GitHub, sans installation, confirme version, source,
taille et SHA-256 du PROD.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`ed978ce21a00ffe68f6267880fd84eb55ad2527c2b8186d0422e0c608a087558`) retire les
sept tâches FILPRO-203 et restaure `update-source.json` après copie de sécurité ;
`configuration-v2.json` était déjà d'origine. `preflight-after-cleanup.json`
confirme deux profils déchargés, aucune ruche et aucun processus. Le profil du
compte de test UAC supprimé `FTADM203` reste chargé par Windows (dossier
`C:\Users\FTADM203`) jusqu'au prochain redémarrage de la VM ; il n'a pas été
forcé.

Limite connue : les informations de débogage de l'EXE contiennent les chemins de
compilation du poste de build ; aucune donnée secrète n'y figure.

## Recette du 28 octobre 2026 — 2.0.2, source `625af6f2`

Cette recette porte sur `625af6f280f69a64cebda9836c30b0f8d4c45ebb`.
EXE Windows x64 : `08d74bb8015dd6e2dc6b4c6b17b7599b2e981876cd1d01098f3413ea06c90f73`.
Les quatre DLL des harnais sont retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 343 fichiers source, 534 fichiers PROD et 885 fichiers DEV.
Le run GitHub Windows `40701216007` (43 suites, 335 contrôles réussis) précède
cette recette ; ses indicateurs GUI, USMT, profils réels et release restaient faux.

Les pièces locales sont conservées hors du dépôt, sous
`work/filpro-finalisation/resume-20261028/final-20261028-202/` :

- **17 étapes natives réussies** sur FILPRO-W11-TEST, invité FILPRO-W11, profils
  jetables F2QA/F2QB : marqueurs d'origine, annulation USMT puis vérification,
  sauvegarde chiffrée, restaurations saved/current/both, mauvaise clé refusée,
  remise à l'origine. Les 17 codes de sortie sont nuls dans
  `native-evidence/profiles-202-suite.json` ; `suite-exit.txt` vaut 0.
- **4 phases complémentaires réussies** : `plain`, `tamper`, `repair`, `rollback`.
  Les préflights avant le supplément et après les suites sont passés. Pièces :
  `supplement-evidence/`.
- **Démarrage et démonstration** sur l'installation DEV du candidat : connexion
  « FILPRO 2.0.2 », accueil, Paramètres « Installée : 2.0.2 » sans option MigApp
  générale. La sauvegarde fictive de deux profils s'est terminée, puis le mode
  normal est revenu. `demo-before.json` et `demo-after.json` encadrent cette
  opération : leur état réel comparé est identique.
- **Mise à jour réelle et retour arrière** : transaction
  `2a0eac4537bf45e08d8039b100aa6200`, 2.0.1 → 2.0.2. Elle a atteint `Committed`
  après l'acquittement de santé réel, puis `RolledBack` vers 2.0.1 depuis
  l'interface 2.0.2. Les preuves `Committed` sont collectées avant le retour
  arrière ; `health.json` reste identique octet pour octet. L'installation 2.0.1
  publiée d'origine est revérifiée inchangée (533 fichiers).
- **Reprise par le worker 2.0.2** : transaction jetable distincte
  `b788777205844a20b879f6d85048ed07`, état `RollingBack` reconstruit après le
  déplacement final. Deux invocations réelles du candidat réussissent ; la seconde
  conserve le journal et les inventaires. `fixtureStateSimulated=true` : ce n'est
  pas une coupure électrique réelle. Pièces : `recovery-evidence/` (résultat,
  trois journaux, neuf inventaires).

Le reçu `delivery/qualified-202/windows-qualification.json` porte `status=passed`
et les huit contrôles critiques. SHA-256 :
`a3b4f6f3dd25b805949aa9a65268c87e1ce0fde2819487bc66834220dd0f2647`.
`validation-summary.json` (SHA-256
`8afddb5f7aa61e80f96688a6bf2863ce8e668399e8e4ed272641d1304fe3743b`) distingue
les résultats natifs, UI, démo, transaction et reprise. Ce périmètre critique
ne vaut ni matrice exhaustive, ni parité V1, ni nouvelle recette UAC.

Défauts des seuls scripts de laboratoire, corrigés sans changer le produit,
originaux et refus conservés :

- le préflight natif lisait `NTUSER.DAT`, fichier caché, sans `-Force` ;
- PowerShell 5.1 comptait la liste JSON des deux profils comme un seul élément.
  L'énumération est désormais explicite, et un contrôle nom/chemin par profil
  a été ajouté ;
- une revue adverse, avant exécution, a montré que le `createdUtc` du journal de
  reprise différait de sa réécriture par System.Text.Json quand ses derniers
  chiffres étaient nuls. Le format est aligné, sans assouplir de garde.

Le premier préflight échoué n'avait muté aucun profil. L'export des preuves
(78 fichiers, `FILPRO-2.0.2-windows-evidence.zip`, 58 073 octets, SHA-256
`b4490de3d7df8901e4dbb95b251eea07c837a8f40be87195739deadc98a50bdf`) a été vérifié
entrée par entrée contre son index, après normalisation des seuls séparateurs
antislash.

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.2-win-x64-PROD.zip` | 364 634 319 | `a752efa9483cc8ad332c7f5f65f20bc2e43acdbc4a9d80a6a305d99fa229a36d` |
| `FILPRO-2.0.2-win-x64-DEV.zip` | 368 649 864 | `2d33de965c3b7cb9291296473b0c2cc1e95e432c0d8c4d2e32fcef2216b3042d` |

Le PROD officiel a le même manifeste (`7c1c89fd…`) et les mêmes 535 contenus que
la fixture privée réellement mise à jour en VM ; seul l'horodatage ZIP du
manifeste diffère. La release publique
[v2.0.2](https://github.com/M4D135/FILPRO/releases/tag/v2.0.2), identifiant
`426860370`, a sa cible et son tag sur `625af6f2`. `publish-public-result.json`
confirme les deux archives et `SHA256SUMS` (191 octets, SHA-256
`e0b69db07b51b292d38e786028c2f766a12c1e704db22eeac98702fee6feaf35`). Le probe réel
de téléchargement GitHub a terminé avec le code 0, sans installation ;
`github-transport-final.json` confirme version, source, taille et SHA-256 du PROD.
La release 2.0.1 reste intacte.

Nettoyage réussi : `cleanup-result.json` (SHA-256
`33ecbf9674a89d82aa1da48f631b494eb89b53d87af072b3fe6191a7e945fa96`) retire les
sept tâches FILPRO-202 et restaure `update-source.json` après copie de sécurité ;
`configuration-v2.json` était déjà d'origine. `preflight-after-cleanup.json`
confirme deux profils désactivés et déchargés, aucune ruche et aucun processus.
Les installations, sauvegardes, racines de test et preuves sont conservées. Les
autres tâches FILPRO hors de ce lot n'ont pas été modifiées.

## Recette du 28 octobre 2026 — 2.0.1, source `8fc09056`

Cette recette porte sur `8fc09056e6627d7055494e79f1b821fd6aa37d87`.
EXE Windows x64 : `0763d0baa52390c092a05f9a09a23a9499beba75b262f3c9cfdf35dfac58d5ae`.
Le DEV et l'entrée PROD partagent les mêmes octets d'exécution. Les quatre DLL
chargées par les harnais ont été retrouvées octet pour octet dans cet EXE.
L'inventaire vérifie 310 fichiers source, 533 fichiers PROD et 851 fichiers DEV.
Le texte de portée du harnais natif mentionne encore un commit historique ;
le garde du manifeste, les DLL exactes et la capture des sources établissent
le candidat `8fc09056` réellement contrôlé.

Les pièces locales citées ci-dessous sont conservées sous
`work/filpro-finalisation/resume-20261028/`, hors du dépôt :

- **30 contrôles C# ciblés distincts réussis** : ExecutionCoordinatorTests (5),
  ExecutionManifestTests (9), UpdateTransactionTests (15) et
  SimulationWorldTests.ConfigurationEtMiseAJourRestentDansLaSessionFictive (1).
  Preuves : `targeted-tests-02.json`, `ExecutionCoordinatorTests.log`,
  `SimulationUpdate-final.log`. Les tentatives intermédiaires échouées ou sans
  test sélectionné sont conservées et ne sont pas comptées comme réussites.
- **17 étapes natives réussies** sur FILPRO-W11-TEST, invité FILPRO-W11 : deux
  profils jetables, sauvegarde chiffrée, trois politiques de conflit fichiers/HKCU,
  annulation après progression USMT, mauvaise clé, arrêt des processus et
  déchargement des ruches. Les 17 codes de sortie sont nuls dans
  `native-evidence/profiles-201-suite.json` et les rapports associés.
- **4 phases complémentaires réussies sur les DLL du candidat** : `plain`,
  `tamper`, `repair`, `rollback`. Magasin non compressé créé ; altération refusée
  par `store.integrity` au stade `StoppedBeforeTargetWrite`, profil inchangé ;
  réparation puis reprise du reçu rétablissant le profil original, avec son
  empreinte antérieure, visible et déconnecté. Pièces :
  `supplement-evidence/supplement-suite.json` et `supplement-evidence/reports/`.
- **Démarrage et démonstration** : entrée Windows, accueil et Paramètres observés
  sur l'EXE du candidat ; opération fictive terminée puis retour au mode normal.
  Les snapshots réels pris avant/après encadrent cette opération et conservent
  signatures des profils, configuration et opérations ; aucun processus USMT ni
  destination fictive sur disque n'est présent au constat final. Pièces `ui-*.json`, `demo-before.json` et
  `demo-after.json` dans le dossier `evidence` du reçu ci-dessous.
- **Mise à jour réelle et retour arrière** : transaction
  `82827780d3af4f39a6040392e5fef1e0`, 2.0.0 → 2.0.1 au stade `Committed`, puis
  `RolledBack` vers 2.0.0 sur l'installation jetable. Les inventaires des deux
  packs, l'acquittement de santé enregistré et les réglages inchangés sont
  vérifiés dans `update-committed.json`, `update-rolledback.json` et les journaux
  bruts référencés. Le worker de cette transaction provient de 2.0.0 ; les essais
  suivants portent séparément sur le worker 2.0.1.
- **Reprise par le worker 2.0.1** : transaction jetable distincte
  `35bdcf0275f04c3b941e7ae1c2c393ce`, état `RollingBack` reconstruit après le
  déplacement final. Deux invocations réelles du candidat réussissent : la première
  finalise `RolledBack`, la seconde conserve le journal et tous les inventaires.
  Les trois packs inventoriés et les réglages sont préservés. Pièces dans
  `recovery-evidence/`, dont `recovery-result.json`, neuf inventaires et trois
  journaux. Il ne s'agit pas d'une coupure électrique réelle.
- **Observation supplémentaire du verrou** : dans `worker-lock-wait.json`, le
  worker candidat reste vivant pendant une détention externe de 20 007 ms puis
  sort avec le code 0 après libération ; journal inchangé, sans relancer l'UI.
  Cette durée inclut démarrage et vérification du pack : elle ne prouve ni
  20 secondes dans `WaitForLockAsync`, ni la cause de l'ancien échec à 15 secondes.

Le reçu `qualification-agent/qualified-201/windows-qualification.json` porte
`status=passed` et les huit contrôles critiques requis. SHA-256 :
`3ec0f02ccb918a9c659e4297e37e3ffd87ad4637e06eca9fd85f0b5ec61f96c3`.
Ses références de preuves et leurs empreintes sont vérifiées ;
`qualification-agent/qualified-201/evidence/validation-summary.json` distingue
les résultats natifs, UI, démo, transaction et reprise. Le champ de schéma
`fullWindowsQualification=true` désigne ce périmètre critique, pas une matrice
exhaustive ni la parité V1. Le supplément et l'observation du verrou restent
des preuves complémentaires distinctes de ce reçu.

La collecte des preuves a d'abord refusé les dossiers annexes `History`/`Hives`,
puis le format antislash des noms ZIP produit par .NET Framework. Les refus sont
conservés. La vérification locale après normalisation des seuls séparateurs
confirme exactement l'index et ses 76 fichiers, tailles et SHA-256 compris.
`windows-evidence.zip` : 54 160 octets, SHA-256
`9a4eeb569a2685691bd9f10f3e930ea3f674051fd9b7860a8343a5d5e6e95968`.
Aucun refus de collecte n'est réétiqueté comme succès d'exécution.

Archives assemblées à partir du candidat et du reçu :

| Archive | Octets | SHA-256 |
| --- | ---: | --- |
| `FILPRO-2.0.1-win-x64-PROD.zip` | 364 258 477 | `17703be14cd05a4543704ebf7b8d911b3f76c414fa3b819c8f736a69a709ebe1` |
| `FILPRO-2.0.1-win-x64-DEV.zip` | 365 431 884 | `bd24d9f14e642844069e9238ae2f17a1a60496fcc2fbca9f23e6dd186c5c9651` |

Diffusion publique explicitement autorisée par David le 28 octobre 2026.
La release publique [v2.0.1](https://github.com/M4D135/FILPRO/releases/tag/v2.0.1)
est publiée, identifiant `426611047`, cible et tag sur le commit `8fc09056` exact.
`publish-public-result.json` confirme les tailles et SHA-256 des deux archives
ci-dessus et du troisième asset `SHA256SUMS` (191 octets, SHA-256
`8a0639d791fb68ef6b7a201d8f4fd0d54398d1ae23431a346a33c11a23c89c94`).
Le probe réel de découverte puis téléchargement via GitHub a terminé avec le
code 0, sans installation. `github-transport-final.json` confirme version 2.0.1,
source `8fc09056e6627d7055494e79f1b821fd6aa37d87`, 364 258 477 octets et SHA-256
`17703be14cd05a4543704ebf7b8d911b3f76c414fa3b819c8f736a69a709ebe1`.
Nettoyage final réussi : `configuration-v2.json` était déjà identique à son
original, `update-source.json` a été restauré et les deux SHA-256 vérifiés. Les
copies de sécurité, installations et magasins sont conservés. Les neuf tâches
de test ont été retirées ; les deux tâches temporaires de nettoyage sont ensuite
confirmées absentes. Le processus de reprise était déjà absent : aucune fermeture
de processus n’est revendiquée. `cleanup-result-raw.json`, SHA-256
`e3015d118f92d1b025d315ed04dee7928bc1472fcc34224460842e6e23e7314e`,
porte le résultat ; `cleanup-final-task-removed.json` confirme le code 0 et le
retrait de Cleanup02. `cleanup-final-comparison.json` et les snapshots sous-jacents
confirment tous les champs identiques à `demo-before.json`, hors horodatage.
Le premier nettoyage a échoué avant remplacement sur l’argument nul de
`File.Replace` en PowerShell 5.1 ; journal et sauvegarde sont conservés dans les
preuves. La correction du seul script de laboratoire utilise une sauvegarde
atomique nommée à côté du fichier cible. Le premier essai reste un échec
(`cleanup-first-log.txt`, `cleanup-first-task-removed.json`) ; aucun code produit
ni binaire publié n’a été changé pour cette correction.
`vm-cleanup-final-status.json` ne relève plus de processus FILPRO ni de tâche FILPRO-201
et confirme la session Administrateur active. Le coordinateur a ensuite fermé
proprement la connexion SSH dédiée.
Un commit documentaire postérieur conserve les binaires et le tag sur `8fc09056` ;
son éventuel reçu hors VM ne remplace pas cette qualification Windows.

### Complément UAC natif après publication — 28 octobre 2026

Ce complément utilise le PROD 2.0.1 copié dans l’installation jetable
`U:\FILPRO-V2-201\uac-20261028\install`. Le script de préparation puis le
lanceur contrôlent le SHA-256 de l’EXE `0763d0baa52390c092a05f9a09a23a9499beba75b262f3c9cfdf35dfac58d5ae`
avant ouverture. La source binaire reste `8fc09056` ; ni le produit, ni les
archives publiées, ni le reçu Windows PROD n’ont été modifiés ou régénérés.
Les pièces suivantes se trouvent dans `resume-20261028/uac-20261028/`.

- **Contexte réel** : `preflight.json` relève `EnableLUA=1`,
  `ConsentPromptBehaviorAdmin=5`, `PromptOnSecureDesktop=1` et
  `FilterAdministratorToken` absent. La session console 1 est celle de
  `LABO\Administrateur`, compte intégré du domaine (SID terminé par `-500`).
  Une tâche interactive `Limited` reste élevée et son lanceur refuse avant tout
  démarrage FILPRO ; ce refus n’est pas une réussite de recette.
- **Parent restreint** : `runas /trustlevel:0x20000` crée le lanceur non élevé,
  puis le PROD. `prod-before-token.json` relève PID 684, `TokenElevation=false`,
  `TokenElevationType=Default` et `IntegrityRID=12288`. Ce niveau d’intégrité
  élevé et l’absence de jeton lié limitent la portée : aucun compte standard,
  jeton medium ou parcours UAC split-token n’est qualifié par cet essai.
- **Invite et annulation réelles** : `after-first-open-token.json` relève
  `consent.exe` dans la même session ; `first-uac-prompt.png` montre la véritable
  invite Windows d’identifiants pour FILPRO.exe sur le bureau sécurisé.
  Après annulation, `after-cancel-token.json` retrouve seulement PID 684 non
  élevé, sans `consent.exe`. `ui-after-cancel.json` confirme le message
  « L’autorisation Windows a été annulée » et les boutons Ouvrir FILPRO et
  Fermer visibles et actifs. Le parcours n’utilise pas une exception simulée.
- **Acceptation réelle** : `after-accept-token.json` relève un nouveau PID 5856,
  `TokenElevation=true`, le même chemin d’EXE et les arguments exacts
  `--poste --filpro-elevated`. L’ancien PID n’est plus présent dans ce relevé.
  `ui-elevated-home.json` lie ce processus à l’accueil et au bouton Actualiser ;
  `after-acceptance.png` confirme l’écran. Il s’agit d’une invite avec saisie
  d’identifiants, pas seulement d’un clic Oui/Non.

Une deuxième invite a été abandonnée avec retour à l’annulation ; la cause
de ce retour n’est pas établie. L’erreur de saisie du nom liée
au clavier français a été corrigée avant remise du mot de passe ; les traces
intermédiaires sont conservées et ne sont pas comptées comme acceptations.
Aucun compte ni réglage UAC n’a été modifié pour obtenir ce parcours.
Les secrets ne sont pas intégrés aux preuves documentaires.

Le contrôle local `github-agent/validate-uac-evidence-201.py` a relu treize
pièces autorisées, vérifié les liens processus/chemin/UI, l’ordre des observations
et les limites de jeton ; les deux captures ont été examinées visuellement et
leurs SHA épinglés. Son rapport `github-agent/uac-evidence-review-201.json`
constate la cohérence des preuves collectées, pas une nouvelle exécution Windows
ni une extension du reçu PROD. Quatre contrôles négatifs en mémoire refusent
un simple résultat sans processus, l’absence de consent.exe, la réutilisation
du PID parent comme enfant et l’absence du message d’annulation.

Le nettoyage UAC est confirmé dans `cleanup-result.json` : cinq tâches
FILPRO-201-UAC retirées, zéro tâche UAC restante et aucun processus FILPRO.
Les SHA-256 de l’EXE et du manifeste restent ceux du candidat ; les quatre
valeurs de stratégie UAC sont identiques au préflight, ainsi que le réglage
d’invite des utilisateurs standard. `after.json` est strictement identique à
`before.json` pour tous les champs sauf `takenUtc` : profils, réglages,
opérations et ruches restent préservés. Le pack et les preuves sont conservés.
Le coordinateur confirme la fermeture normale, sans forcekill, puis celle de
la connexion SSH dédiée ; aucun compte ni stratégie n’a été changé.

Le complément `uac-result.json` référence seize preuves dont les SHA-256 ont
été recoupés localement ; son SHA-256 est
`a105c200f0da481dbba88d913aa341bb3970c9d82909e61382c4e1886bb75b4f`.
`github-agent/uac-final-evidence-review-201.json` consigne le rapprochement des
jetons, le nettoyage, les snapshots et les seize empreintes. Il corrige toute
mention antérieure d’expiration de la deuxième invite : la cause est inconnue.
Le reçu PROD original conserve son SHA-256 `3ec0f02ccb918a9c659e4297e37e3ffd87ad4637e06eca9fd85f0b5ec61f96c3`.
Ce complément UAC reste séparé ; aucun reçu de production n’est régénéré.

Portée : profils uniquement, fichiers personnels, AppData et HKCU. Aucune parité
V1 exhaustive, image système, restauration générale HKLM/Windows partagé ou levée
des refus LoadState V1/MigApp. La récupération indépendante et les magasins V1
chiffrés sans clé ne sont pas retestés par cette reprise. Le complément UAC
ci-dessus conserve ses limites de jeton ; GitHub Actions non utilisés sur décision de David.
Les sections ci-dessous conservent les résultats historiques de leurs propres
commits ; elles ne remplacent pas les preuves du candidat 2.0.1.


## Clôture du 26 octobre 2026 — `5a7bc5a`

Le candidat `5a7bc5a5c2ce5e0beba234ca79c3c5ef0bce1877` est qualifié et publié
en release privée `v2.0.0`. Les contrôles qui bloquaient le pack PROD sont faits
sur FILPRO-W11-TEST : démarrage, sauvegarde/restauration de deux profils,
intégrité, conflits, ruches déchargées, annulation après USMT réel, chiffrement,
mauvaise clé, isolation de démonstration, mise à jour `e91f` puis retour arrière
depuis l’interface. Le premier essai `19c871` a conservé les deux paquets après
un fichier occupé ; la cause du verrou n’est pas établie. Reçu, sommes et bilan :
`Documents/Codex/2026-10-26/co/outputs/BILAN-FILPRO-5a7bc5a.txt`.

Les phrases plus bas qui disent « validation native restante », « aucun reçu PROD »
ou « packs non qualifiés » décrivent des lots antérieurs. Elles ne sont pas l’état
de `5a7bc5a`.

Reste hors de cette release, volontairement : dialogue UAC natif (session déjà
élevée), parité V1, GitHub Actions. Le complément exécuté ensuite n’est pas dans
les octets publiés. Son résultat est dans la section suivante.

## Complément du 26 octobre 2026 — après `5a7bc5a`, non republié

Exécuté seulement sur FILPRO-W11-TEST, avec la session labadmin déjà élevée.
`FILPRO.exe` publié reste `e829f8fd6ba82a282edd6fb5dc6f1290ea6e152102690e8321d891ffad537b9a`.
Les parcours non compressé, altération et réparation ont chargé des DLL
reconstruites dans le harnais, pas la release `v2.0.0`.

- Refus d’une capture MigApp : `backup.profileScope`, aucun dossier créé.
- Deux magasins V1 historiques chiffrés ont été copiés. Les `USMT.MIG` source et
  copie sont encore identiques : profil `7ac719caaa74ca24711ab990fa09c568b017fd577382608cb0c7c34328e3cc79`
  (2 154 010 octets), MigApp `21dc6f6db8d6921578109eda5a63d47b39dfd69e28bd0576c0af77cc2bef8a8e`
  (2 156 066 octets). Les originaux n’ont pas été écrits.
- Restauration de la copie profil : `restore.compatibility`, avec le texte
  « LoadState V1 est désactivé ». Restauration de la copie MigApp : `restore.migapp`.
  Aucun LoadState. La signature de F2QA est inchangée.
- Catalogue du vrai magasin : bloqué par `usmt.crypto`, faute de clé à côté du
  magasin. Ce n’est ni un catalogue ni une récupération indépendante. Aucune clé
  de démonstration n’a été essayée.
- Sauvegarde V2 non compressée de F2QA, groupe `known:vlc` : deux magasins Ready,
  vérification `FileHashesVerified`. L’EXE publié refuse l’estimation à plusieurs
  clusters (« Estimation USMT illisible »). Le moteur local retient le cluster du
  volume. L’altération du magasin le plus récent a été refusée par `store.integrity`,
  stade `StoppedBeforeTargetWrite`, profil inchangé. DLL de cette preuve :
  Core `400f448db4ebc6e36072063b95f8259ba643b7ab898cefb75d19f77a255f2f82`,
  Platform `b606cdcb4d71a5ff6b81fc5f21b89b1152ed4fa283d00f756a7f5a47380ba0c2`.
- Réparation de F2QB : le premier essai s’arrête avant renommage tant que le
  handle de `NTUSER.DAT` reste ouvert. Le correctif le ferme avant `Directory.Move`.
  Le second essai renomme le profil, puis un second processus reprend le reçu
  `61e3ee2a821a43dfb85d06660aed05b2`. Stade final `RolledBack`. L’empreinte du
  dossier correspond à celle d’avant. F2QB est revenu à `C:\Users\F2QB_68b870d`,
  déconnecté, sans dossier `.old`. DLL de cette preuve :
  Core `f4a2c82ae810e73800eec726d24e388609b072447434268957f6c6312b624721`,
  Platform `ae3aba4ac48d8185ef0b99612dc8a35cc12ad98bb0f44ce91e46d9bde4461e70`.
- Aucun processus FILPRO ou USMT restant, aucune tâche `FILPRO-Supplement`.

Restent hors résultat : parité V1, dialogue UAC natif, GitHub Actions, et la cause
du verrou `19c871`. Les sources `603231e` sont sur `origin/v2`. La garde a accepté
un reçu hors VM réel (syntaxe, guides, publication) et a rappelé que ce reçu ne
qualifie pas Windows. La release `v2.0.0` reste le binaire `5a7bc5a`. Aucun
contournement de garde.

## Candidate 2.0.0 — contrôles du 26 octobre 2026

Le [rapport courant](reprise/FINALISATION-CANDIDATE-20261026.md) décrit les 330
contrôles Windows, 103 contrôles Mac, le parcours d’interface et l’assemblage réel.
Leurs preuves et le reçu de garde externe porteront le commit exact ; aucune
qualification complète Windows ni PROD n’est déduite des contrats.

## Reprise du retour arrière — 26 octobre 2026, après `9e790cd`

Lot détaillé dans [REPRISE-ROLLBACK-20261026](reprise/REPRISE-ROLLBACK-20261026.md).
Les résultats ci-dessous portent sur les sources de ce lot, inventoriées dans
les preuves externes ; ils ne qualifient pas la distribution complète.

- macOS 26.4 ARM64, SDK .NET 10.0.401 : compilation croisée des six projets
  Windows x64, zéro avertissement et zéro erreur. Compilation du lanceur de tests
  pour exécution locale ARM64 et **15 tests UpdateTransactionTests réussis**.
- Windows x64 `10.0.26200.0`, hôte de laboratoire `DESKTOP-285EKHU` : lanceur
  autonome compilé sur le Mac, **24 tests réussis, zéro échec et zéro ignoré** :
  `UpdateTransactionTests` (15), `PackIntegrityTests` (8) et
  `StorageFileIdentityWindowsTests` (1). Les transactions déplacent de vrais
  dossiers Windows contenant des packs fictifs ; aucun EXE fictif n'est lancé.
- Deux nouveaux tests reproduisent les régressions sur les sources de `9e790cd` :
  reprise non proposée dans Paramètres et finalisation du retour arrière refusée
  lorsque `previous` a déjà été remis en place. Ils réussissent après correction.
- Garde source inchangée : ses **31 tests Python passent** sur le Mac. Cela ne
  remplace pas son reçu de publication portant sur le commit exact.

Le premier lancement distant du banc a refusé ses arguments avant tout test ;
la passe retenue utilise `Start-Process` et conserve les sorties et codes par
suite. Un premier build Mac contenait une référence erronée dans le nouveau test,
corrigée avant les passes retenues ; une publication parallèle MSBuild a échoué
avant le succès en mode séquentiel. Les journaux intermédiaires restent conservés.

La VM FILPRO a pu être démarrée, mais son accès SSH n'a pas abouti. Les tests
ci-dessus ont donc été exécutés sur l'hôte Windows, pas dans cette VM. Aucune
validation WebView2/USMT, profil réel, arrêt physique, worker remplaçant un EXE
ou mise à jour GitHub réelle n'est déduite de ces contrôles. Les packs restent
non qualifiés. Le run Actions `40298903992` est toujours en échec sans étapes ;
le motif précis de ce dernier échec n'a pas été obtenu par le connecteur.

## Dernier lot du 26 octobre 2026 — preuves rattachées séparément

Le dernier état distant publié avant ce lot est
`564f86cf1bb8ff5992e47187086a8c1db2313371`, après `00039d6` et `984f937`.
Les sources Spark effectivement transférées s'arrêtent au lot 004A ; les ajouts
de reprise ne sont pas un transfert supposé d'autres travaux privés.
Le [rapport de finalisation des sources](reprise/FINALISATION-SOURCES-20261026.md)
décrit les changements actuels et les refus qui demeurent.

| Preuve du dernier lot | Résultat et portée |
| --- | --- |
| Instantané exact et commit final | Commit contenant ce document ; SHA exact et inventaire des sources dans le reçu externe. Aucune réussite antérieure n'est transférée automatiquement. |
| Compilation après les derniers ajustements Platform | `final-build-03` sous Linux : **six projets, aucun avertissement, aucune erreur**. Journal : `/tmp/filpro-integration-20261026/final-build-03.log`. |
| Contrôles C# ciblés retenus | **79 contrôles distincts réussis, aucun échec ni ignoré restant dans la sélection**. Résultat agrégé des dernières passes utiles des suites, décrit ci-dessous ; pas une exécution Windows ni une suite générale exhaustive. |
| `ui-catalog-receipt-03` | **19 contrôles réussis, aucune erreur JavaScript non interceptée**. Chromium `153.0.8010.12` sous Linux, relié aux véritables DLL Bridge/Simulation par le harnais externe. Rapport : `/tmp/filpro-integration-20261026/ui-catalog-receipt-03/report.json`. |
| Windows / WinForms / WebView2 / USMT réel | Non qualifiés ; le run Actions connu n'a exécuté aucune étape. |
| Packs PROD/DEV finaux et Release | Aucun pack qualifié ni Release finale autorisés par ces preuves. |
| Garde finale sur les sources gelées | Résultat, compteurs et SHA exact dans le reçu externe de la garde d’origine ; portée hors Windows. |

Les 79 contrôles C# correspondent aux dix suites réussies dans
`/tmp/filpro-integration-20261026/final-targeted-01/report.json` (70 contrôles),
aux six contrôles `LegacyExecutionTests` réussis dans `final-targeted-03/report.json`
et aux trois contrôles `ExecutionCleanupTests` réussis dans
`final-targeted-02/report.json`, sous le même dossier de preuves. Les tentatives
01 et 02 conservent chacune l'échec intermédiaire de la suite Legacy ; elles ne
sont pas réétiquetées comme entièrement réussies. Cette suite a été corrigée et
relancée seule en tentative 03. Les réussites répétées ne sont pas additionnées.
Tous ces rapports indiquent Linux et `windowsQualified=false`.

Le parcours UI ciblé vérifie l'absence de catalogue sans entrées inventées, la
confirmation avant lecture longue, l'annulation de l'aperçu sans démarrage,
le suivi puis la relecture du catalogue par identifiant de travail, la reprise
depuis un reçu fictif connu et le refus d'un reçu extérieur ou d'une route native
en démonstration. Le premier essai a révélé un attribut `hidden` écrasé par les
styles ; la correction FILPRO masque effectivement les boutons et champs concernés.
Le troisième essai utilise les DLL recompilées du dernier lot et la règle finale
de masquage de tout le contenu principal FILPRO. Il vérifie ce parcours fictif
sur les sources GUI/Bridge/Simulation gelées ; il ne valide pas les exécutants
Windows Platform, absents du harnais.

Les contrôles de compatibilité doivent conserver ces distinctions :

- **V1 : LoadState refusé dans tous les cas**, même si règles et configuration
  sont hashées et si le catalogue est complet. La référence exhaustive des
  composants Windows capturés manque ; le catalogage confirmé et la récupération
  indépendante ne la remplacent pas.
- **MigApp : restauration native refusée**, avec avertissement concernant les
  paramètres machine non couverts par le préalable des profils.
- **Non compressé : index et fichiers vérifiés ensemble**, avec refus des
  ajouts, suppressions, modifications, liens/ambiguïtés et dépassements des
  bornes : 100 000 fichiers, 16 384 dossiers, profondeur 128 par arborescence USMT.
  Les façades et fichiers temporaires n'exercent pas LoadState ni les handles Windows.
- **Reçu de réparation : reprise contrôlée**, avec authentification locale,
  conservation des données et état partiel dès que des modifications restent
  possibles. Une reprise fictive ne démontre pas la réconciliation de ProfileList.
- **Magasins V2 : périmètre produit par FILPRO et conservé intact**. La génération
  native de configuration et ses exclusions doivent être vérifiées sur Windows.
  Une configuration de test vide ou partielle n'en est pas une preuve ; des hashes
  concordants ne constituent pas une signature d'un ensemble intégralement forgé.

## Preuves antérieures — intégration du 26 octobre 2026

Le lot d’intégration relie les exécutants et les écrans, décrit dans
[INTEGRATION-MOTEURS-20261026](reprise/INTEGRATION-MOTEURS-20261026.md).
Le contrôle `build-03` exécuté sous Linux compile les six projets sans
avertissement ni erreur. Les 99 contrôles C# ciblés passent sans échec ni
ignoré. Le harnais Chromium relié au véritable pont C# de démonstration
passe 25 contrôles de parcours sans erreur JavaScript non interceptée.
Ces résultats portent sur l’instantané de travail inventorié dans les preuves,
pas automatiquement sur un commit ultérieur.


Le contrôle de disposition `ui-layout-03` passe ensuite 84 contrôles à
820×540 et 1280×800 sur le candidat d’intégration : navigation repliable
accessible, absence de débordement horizontal et dialogues utilisables.
Le contrôle porte sur Chromium/Linux et sur des données fictives ; il ne
qualifie toujours pas WebView2 sous Windows.

Ces contrôles utilisent des fichiers temporaires, des façades et le monde
fictif. Ils ne démarrent ni WinForms/WebView2 natif, ni ScanState/LoadState,
ni une opération sur des profils réels. Les tests de registres, handles de
dossier et hardlinks Windows sont sélectionnés uniquement sur Windows.

Le run GitHub `40286842500` du commit `716812c` n’a lancé aucune étape Windows :
job `121452389301`, `runner_id=0`, liste des étapes vide. GitHub indique un
échec de paiement récent ou un plafond de dépenses à augmenter. La présence
d’un workflow Windows ne remplace pas une exécution. Aucun pack n’est qualifié.

Les nouvelles suites par défaut couvrent les codecs de configuration, le
bootstrap, l’intégrité, les transactions d’update, les plans à usage unique,
la synchronisation du cycle de vie, les choix communs, le pont d’exécution,
les opérations fictives, le coordinateur, les manifestes et le nettoyage
des ruches via façades. `ExecutionWindowsTests` et
`StorageFileIdentityWindowsTests` restent des contrôles natifs distincts.
La recette USMT/profils/WebView2 et remplacement d’installation reste à réaliser
sur Windows jetable ; aucun succès de façade ne vaut qualification de ces APIs.

## État et portée

Les sources initiales de la V2, jusqu'au lot Spark 004A seulement, ont été transférées au commit
`5818fd17d1fab5b63868d3ecc7a4c4893bbc115b`. Le message de ce commit précise
qu'elles n'étaient ni compilées, ni testées sous Windows. Les rapports Spark
READY décrivent le code écrit ; ils ne remplacent pas des résultats d'exécution.

Les sources de reprise corrigent les blocages de compilation identifiés :

- quatre appels incompatibles avec .NET 10 (`StartsWith(char, StringComparison)`)
  remplacés par une comparaison ordinale de caractère ;
- quatre conditions utilisant un compteur entier comme booléen dans
  `RestoreConflictPolicies`, remplacées par `Count > 0` ;
- casse des arguments nommés du constructeur `BackupPlanRequest` dans
  `UsmtArgumentsTests` (`Encrypted`, `KeyPath`, `UseVss`) ;
- déclarations DPI dupliquées retirées du manifeste : le réglage
  `ApplicationHighDpiMode=PerMonitorV2` reste porté par le projet WinForms.

Le lanceur de tests est aussi renforcé :

- un filtre vide ou inconnu échoue ;
- le filtre désigne exactement une classe ou `Classe.Methode`, sans englober
  une autre suite dont le nom contient les mêmes mots ;
- `Check.Skip` consigne un contrôle ignoré, distinct des réussites ;
- zéro contrôle réellement réussi ne peut produire un code de succès.

Seul le rapport d'un run exécuté sur Windows prouve une exécution Windows.
Une compilation croisée depuis Linux ne qualifie ni le lancement Windows,
ni WebView2, ni les opérations sur des profils. Le workflow décrit ci-dessous
conserve séparément ces niveaux de qualification.

## Compilation et contrats ciblés sous Windows

Prérequis : Windows x64, PowerShell 7.2 ou ultérieur pour l'outil de build,
et SDK .NET compatible avec `global.json` (10.0.401, correctifs de la même
bande autorisés). Ces outils ne sont pas des prérequis de la future application
autonome. Le rapport conserve la version réellement utilisée dans `dotnet-info.log`.

Depuis `v2`, sur un environnement Windows de validation :

```powershell
# Restauration depuis les caches locaux indiqués par NuGet.config.
pwsh -NoProfile -File tools/ci/Validate-Windows.ps1

# Choix explicite d'une restauration depuis NuGet officiel pour le build.
pwsh -NoProfile -File tools/ci/Validate-Windows.ps1 -AllowNetworkRestore

# Depuis une console PowerShell 7 : composants modifiés, dossier de preuves neuf.
& ./tools/ci/Validate-Windows.ps1 -AllowNetworkRestore `
  -Suite @('BridgeTests', 'SoftwareSelectionStoreTests') `
  -OutputDirectory C:\FilproValidation\lot-cible
```

Le script refuse un hôte non Windows, un processus 32 bits, une liste de suites
vide ou dupliquée, un dossier de preuves existant et tout dossier de preuves
situé dans le dépôt. Sans chemin fourni, il crée un dossier temporaire unique.
L'option réseau concerne uniquement la restauration de dépendances du build ;
`v2/NuGet.config` reste inchangé, et l'application n'acquiert aucun connecteur cloud.
Avec `-AllowNetworkRestore` uniquement, le script passe aussi
`-p:DisableTransitiveFrameworkReferenceDownloads=false` à **restore et build**.
Cet override de développement permet de restaurer WindowsDesktop et les références
transitives sur un runner neuf ; il évite le refus `NETSDK1185` lorsque le pack
`Microsoft.WindowsDesktop.App.Runtime.win-x64` n'est pas déjà fourni. La propriété
hors ligne de `Directory.Build.props` reste inchangée. Un échec de restauration
interrompt le script avant la compilation ; il n'est jamais ignoré.
Le SDK peut alors demander aussi `Microsoft.AspNetCore.App.Runtime.win-x64` comme
pack technique transitif. Son mapping est autorisé uniquement dans le fichier NuGet
de CI ; cela n'ajoute ni service ASP.NET ni connexion réseau à l'application.

Une restauration, une compilation Release de la solution, puis une exécution
par suite sélectionnée sont effectuées. Il s'agit du lanceur console `Filpro.Tests`,
**pas de `dotnet test`**. Les suites par défaut contrôlent le mode, le pont, les
plans de sauvegarde/restauration, les arguments USMT et la persistance de sélection
sur fichiers temporaires, ainsi que la lecture seule du contexte Windows courant.
Les suites de sélection et de découverte sont incluses explicitement :

| Suite | Périmètre du contrôle |
| --- | --- |
| `LastSelectionRulesTests` | Règles pures de sélection et conservation des choix |
| `PosteSelectionPersistenceTests` | Persistance par fournisseur, mode fictif distinct, documents temporaires |
| `SelectionBridgeTests` | Validation des demandes et raccordement de la sélection au pont |
| `SoftwareDiscoveryCatalogTests` | Recettes V1, recommandations, exclusions et regroupement fichiers + clés |
| `SoftwareFileDiscoveryTests` | Découverte AppData sur façade contrôlée : accès, limites, liens et annulation |
| `LoadedRegistryDiscoveryTests` | Découverte de clés sur façade contrôlée : codes d'erreur, limites et fermeture des ressources |
| `LoadedRegistryWindowsTests` | Ouverture réelle de HKU du propre SID et fermeture/réouverture en lecture seule, Windows uniquement |

Une suite attendue absente échoue par le filtre du lanceur ; elle n'est pas ignorée
silencieusement pendant une intégration en cours. Ces suites n'exécutent ni
ScanState ni LoadState.
Les trois suites de découverte sur données contrôlées peuvent être exécutées
hors Windows, sans qualifier le registre ni les chemins Windows. La suite
`LoadedRegistryWindowsTests` en est séparée et ne doit pas être sélectionnée
sur Linux : un contrôle ignoré n'est jamais compté comme réussi. Ce contrôle
natif ne monte aucune ruche, ne lit aucun profil tiers et ne prouve pas encore
le cycle de sauvegarde/restauration ou de déchargement hors ligne.
La suite du stockage inclut le refus d'un document existant illisible ou invalide
avant tout remplacement. Son contrôle de verrou exclusif est réservé à Windows,
comme celui du jeton exécutant ; ils ne font pas partie d'une preuve Linux.
Le contrôle de refus du lecteur natif hors Windows ne fait pas partie de cette
sélection : il n'est pas applicable à Windows.

Chaque exécution conserve les journaux bruts, un inventaire SHA-256 des fichiers
V2 et `validation.json`. Le rapport distingue :

- compilation Windows réussie ;
- suites réellement exécutées et leurs nombres de réussites, échecs et ignorés ;
- `loadedUserRegistryReadPassed`, vrai seulement après la réussite de la suite
  native dédiée au registre du compte courant ;
- sources inchangées pendant la validation ;
- rattachement à un commit exact, seulement si Git est disponible et l'arbre V2
  était propre au début ;
- limites explicites : GUI, USMT, profils réels, qualification complète et
  publication **non qualifiés**.

Un code zéro du processus seul ne suffit pas : la suite doit avoir au moins une
réussite, un résumé reconnu, aucun échec et aucun contrôle ignoré. Une sortie
anticipée laisse un rapport en échec, avec la phase concernée. Les résultats
portent sur les seuls fichiers et scénarios inventoriés.

## GitHub Actions : Windows distant

À la demande de validation Windows via GitHub du 25 octobre 2026, le workflow
unique est `.github/workflows/filpro-v2.yml`. Il remplace le modèle inactif
anciennement conservé sous `v2/tools/ci/`. Cette extension d'infrastructure
ne modifie ni les sources V1, ni leurs packs, ni la garde de publication.

Un push sur `v2` ou une pull request vers cette branche touchant les sources
sélectionnées déclenche un runner `windows-2025` : une vraie machine Windows
hébergée par GitHub. Le job installe le SDK du projet, compile les six projets
et exécute les suites ciblées, dont le jeton Windows et le verrou exclusif.
Les politiques de conflits sont également incluses dans la sélection par défaut.

Le checkout ne matérialise que `v2/` et ce workflow. Les actions sont épinglées,
les droits limités à la lecture, et les journaux sont conservés même après échec.
Chaque artefact porte le commit et le numéro de tentative pour préserver les
preuves précédentes. Aucun pack, tag ou Release n'est publié par ce workflow.

La présence du workflow ou le seul démarrage du job ne prouve aucune réussite.
Vérifier le SHA du run, son état final, `validation.json`, les compteurs de tests
et les journaux avant de déclarer les contrôles Windows exécutés. Un échec
de compilation, un test ignoré ou un résumé incomplet interdit le succès.

Ce premier job n'installe pas de runtime WebView2 fixe et ne lance pas la fenêtre
de l'application. Les indicateurs GUI, USMT, profils réels et livraison restent
à `false`, même si compilation et contrats Windows réussissent. Le démarrage
de l'EXE et les opérations métier sur données de laboratoire devront être
exécutés avec les exécutants maintenant raccordés ; le principe employé par
le projet GLPI sur GitHub peut alors être repris sans prétendre que ce banc existe déjà.

## Publication et gardes

La garde V1 de `passation/outils/mac/garde_poussee.py` reste intacte. Son inventaire
vise `FILPRO.exe`, `wad`, `passation/outils` et `.github` ; il ne couvre pas les
sources de `v2/`. Un ancien reçu V1 ne constitue donc pas une preuve V2.
Ne pas contourner le hook, réétiqueter un résultat ancien ni employer une écriture
API pour éluder une barrière applicable. La validation proposée ici produit des
preuves ciblées ; elle n'autorise pas à elle seule une publication ou le remplacement
des packs V1. L'intégration des sources et la qualification d'une livraison
restent deux étapes distinctes.

## Recette critique restant à exécuter sous Windows

Sur Windows jetable avec données de laboratoire : démarrage et fermeture WebView2 ;
sauvegarde/restauration multi-profils fichiers et registre ; intégrité et conflits ;
ruches déchargées ; annulation/échec sans perte ; chiffrement si livré ; isolation
de démonstration ; mise à jour, retour arrière et reprise sur copies d'installation.
Une fonctionnalité encore absente ne doit pas être remplacée par un succès simulé.

Le complément de compatibilité a été exécuté le 26 octobre 2026 sur FILPRO-W11-TEST.
Le résultat, les DLL réellement chargées et le catalogue V1 bloqué sans clé sont
dans « Complément du 26 octobre 2026 ». Ce complément ne vaut ni matrice exhaustive
ni autorisation de lever un refus pour obtenir un test vert.
