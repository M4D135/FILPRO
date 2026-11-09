# FILPRO V2 — finalisation des sources du 26 octobre 2026

## État et provenance

Ce document décrit le dernier lot de sources et ses limites. **Il ne déclare pas
FILPRO V2 prêt à distribuer.** La version reste `2.0.0-dev`, la qualification
Windows n'est pas acquise et aucun pack PROD/DEV final ni Release V2 finale n'est
annoncé.

Les travaux privés Spark effectivement récupérés s'arrêtent au **lot 004A**, publié
dans `5818fd17d1fab5b63868d3ecc7a4c4893bbc115b`. Aucun lot privé ultérieur n'est
présenté comme transféré. Les développements de reprise coordonnés après
`716812cd30939da569f04cb5e2d7c731a5fa1e55` sont distincts de cet apport Spark.

Le dernier état distant publié avant ce lot est
[`564f86cf1bb8ff5992e47187086a8c1db2313371`](https://github.com/M4D135/FILPRO/commit/564f86cf1bb8ff5992e47187086a8c1db2313371),
après `00039d6` et `984f937`. Le présent lot est identifié par le commit contenant
ce document ; le reçu de garde fourni séparément porte son SHA exact et ses compteurs.

Le socle demeure le véritable kit C#/.NET, WebView2 et HTML/CSS/JS de
`M4D135/domenia-d120`, branche `v2`, avec la provenance conservée dans
[KIT-PROVENANCE](KIT-PROVENANCE.md). Les guides `SPARK-*` et les rapports des
premiers lots restent des instantanés historiques ; leurs états anciens ne sont
pas réécrits pour leur attribuer les développements suivants.

## Changements présents et périmètre autorisé

| Parcours | Code et raccordement actuels | Limite déterminante |
| --- | --- | --- |
| Catalogue d'un magasin | Inspection courte, confirmation distincte, travail `Catalog`, suivi puis relecture par le moteur. | Une inspection ou un catalogue antérieur ne prouve pas à lui seul l'intégrité actuelle du contenu. |
| Fichiers V1 | Métadonnées reconnues, préparation confirmée du catalogue et de son empreinte, récupération indépendante dans une nouvelle destination. | L'empreinte est mesurée maintenant ; aucune empreinte historique de capture absente n'est inventée. |
| Restauration V1 | Contrôles de compatibilité et motifs de refus présents. | **LoadState systématiquement bloqué**, même après catalogage ou lecture d'une configuration hashée. |
| Magasin V2 non compressé | Index JSONL SHA-256, comparaison de l'ensemble exact des fichiers, copie ou application conditionnée aux contrôles. | Bornes explicites et qualification Windows encore absente. |
| Magasin avec MigApp | Capture explicite et avertissement sur le périmètre supplémentaire ; récupération indépendante possible. | **Restauration native bloquée** : des paramètres machine peuvent dépasser la sauvegarde préalable des profils. |
| Reçu de réparation | Choix d'un reçu, confirmation, authentification et relecture des données conservées, réconciliation des étapes. | Reçu natif émis dans le dossier privé FILPRO, clé locale et sauvegardes liées nécessaires ; pas de réparation Windows qualifiée. |
| Démonstration | Même pont C#, magasins et reçus fictifs, catalogage différé, reprise, erreur/annulation/partiel. | Mémoire de la génération courante, routes fictives dédiées, refus des écritures natives même avec arguments forgés. |

### Catalogage explicite et relecture

Le bouton **« Préparer le catalogue »** n'est proposé que lorsque le catalogue
manque. La boîte de confirmation annonce la lecture longue et demande l'accusé
de capacité avant de démarrer. Le pont appelle `recuperer.catalog` pour le moteur
poste, ou `demonstration.catalogStore` pour le service fictif ; un argument de la
page ne permet pas de changer de fournisseur.

Le moteur utilise `/verify:all` pour un magasin compressé et l'indexation ou la
vérification SHA-256 pour les fichiers non compressés. Un catalogue neuf n'est
publié dans le cache de session qu'à l'issue du travail ; une annulation ou un
échec n'en publie pas. Un catalogue dont les bornes empêchent une lecture complète
reste explicitement partiel. Les métadonnées et fichiers de l'ancien magasin V1
ne sont pas réécrits.

Le résultat du travail désigne le magasin, pas le chemin interne du cache. Depuis
le suivi, le retour à Récupérer porte uniquement l'identifiant de l'opération.
L'interface relit son type, son état final et son résultat côté C#, puis demande
une nouvelle inspection du magasin. Le dernier magasin choisi reste en mémoire
de la génération courante. Le plan de récupération V1 est lié au SHA-256 mesuré
pendant la préparation ; le contrôle du contenu est refait avant extraction ou
copie. La lecture courte conserve `PayloadVerified=false` pour le moteur natif.

Sources principales : [catalogage](../../src/Filpro.Platform/Execution/WindowsFilproExecutionService.Catalog.cs),
[récupération](../../src/Filpro.Platform/Execution/WindowsFilproExecutionService.Recovery.cs),
[page Récupérer](../../web/js/pages/recuperer.js), [suivi](../../web/js/pages/suivi.js).

### Pourquoi LoadState V1 reste fermé

Le lecteur contrôle les empreintes des règles, les variantes reconnues de
sélection/MigDocs/exclusions, la configuration de profil éventuelle, les identités
source et les informations de version USMT. Un SID ou un chemin absent n'est
jamais emprunté à la cible pour fabriquer une preuve de capture.

Ces contrôles sont nécessaires mais insuffisants. La V1 ne fournit pas de
**référence exhaustive vérifiable des composants Windows capturés**. Une
configuration XML hashée peut être vide ou incomplète ; un catalogue de fichiers
ne décrit pas tous les composants système et de registre. Leur concordance ne
prouve donc pas que LoadState resterait dans le périmètre protégé des profils.
Un bloqueur inconditionnel est conservé. La présence du code de restauration
V1, de ses vérifications source/règles/configuration/version USMT ou d'un catalogue
complet n'autorise pas son exécution.

Pour les captures V2, la liste produite par le `/genconfig` natif de la source est
restreinte afin de désactiver ses composants Windows, puis configuration, règles
et contenu sont liés par leurs empreintes. Le périmètre admis est celui des
magasins réellement produits par FILPRO et conservés intacts. Les hashes
établissent la cohérence des octets, **pas l'authenticité d'un manifeste et d'un
magasin intégralement forgés**. Aucun protocole de signature ou de confiance entre
machines n'est ajouté ici. Une configuration artificielle de test ne démontre
pas le comportement du vrai `/genconfig` Windows.

MigApp possède son propre refus de restauration native (`restore.migapp`) et son
avertissement : ses éventuelles écritures machine ne sont pas couvertes par le
préalable des profils. La récupération indépendante de fichiers ne lève aucun de
ces deux refus.

Sources : [lecteur des magasins](../../src/Filpro.Platform/Execution/WindowsBackupStoreReader.cs),
[admission de l'exécutant](../../src/Filpro.Platform/Execution/WindowsFilproExecutionService.cs),
[capture et restauration V2](../../src/Filpro.Platform/Execution/WindowsFilproExecutionService.BackupRestore.cs).

### Non-compression : intégrité bornée, format ordinaire

`PlainStoreIndexCodec` et `PlainStoreVerifier` encadrent un index JSONL des fichiers
USMT ordinaires, sans archive WAD personnalisée. Chemins, tailles et SHA-256 sont
contrôlés avec l'en-tête et la fin de l'index. Avant utilisation, l'ensemble exact
des fichiers est comparé à l'index ; ajout, absence, changement, ambiguïté, lien
non admis ou index tronqué entraînent un refus. Les entrées contrôlées restent
tenues pendant l'opération. Après récupération, les fichiers copiés sont comparés
aux tailles et empreintes attendues.

Chaque arborescence USMT vérifiée est limitée à **100 000 fichiers, 16 384 dossiers
et une profondeur de 128** ; dans un magasin V2, cette vérification s'effectue par
profil. L'index est limité à **128 Mio**. Un dépassement bloque le contrôle
d'intégrité ; il ne produit pas un succès sur un sous-ensemble silencieux.
Le catalogue proposé à l'interface peut, lui, être explicitement partiel.

Pour V1 non compressé, l'index est créé dans l'état privé FILPRO après confirmation.
Il décrit l'état alors mesuré du magasin inchangé et permet la récupération de
fichiers ; il ne rend pas la restauration de profils admissible. Ce chemin ne
prétend pas avoir vérifié un magasin non compressé par la commande USMT `/verify`.

Sources : [codec de l'index](../../src/Filpro.Core/Execution/PlainStoreIndexCodec.cs)
et [vérificateur](../../src/Filpro.Platform/Execution/PlainStoreVerifier.cs).

### Reprise depuis un reçu de réparation

Dans Analyser, **« Reprendre depuis un reçu de réparation »** ouvre le sélecteur
natif en mode poste. Le chemin est présenté avant confirmation. Le moteur accepte
uniquement un reçu émis dans son dossier privé d'opérations, authentifié avec sa
clé locale et cohérent avec les sauvegardes, dossiers, étapes et identités
conservés. Le retour arrière relit les sessions et distingue les modifications
déjà possibles d'un arrêt avant mutation. Il conserve les dossiers et preuves
nécessaires en cas d'état partiel ; une annulation demandée n'est pas présentée
comme un retour arrière achevé.

Dans la démonstration, aucun sélecteur natif n'est appelé : la liste contient les
seuls reçus fictifs connus de cette génération. Après un retour arrière abouti,
le reçu consommé quitte la liste. Un chemin extérieur ou issu d'une session
précédente ne peut pas déclencher une opération réelle.

Sources : [réparation et reçus](../../src/Filpro.Platform/Execution/WindowsFilproExecutionService.Repair.cs),
[page Analyser](../../web/js/pages/analyser.js), [monde fictif](../../src/Filpro.Simulation/SimulationWorld.cs).

## Preuves portables du dernier lot et rattachement final

| Contrôle | État et portée |
| --- | --- |
| Instantané gelé et commit final | Commit contenant ce document ; SHA exact et inventaire des sources dans le reçu de garde externe. Le commit précédent cité plus haut ne désigne pas ce lot. |
| Compilation finale | `final-build-03` sous Linux : **six projets, aucun avertissement, aucune erreur**. Journal : `/tmp/filpro-integration-20261026/final-build-03.log`. |
| Contrôles C# ciblés retenus | **79 contrôles distincts réussis, aucun échec ni ignoré restant dans la sélection** ; dernières passes utiles agrégées, détails ci-dessous. |
| UI `ui-catalog-receipt-03` | **19 contrôles réussis, aucune erreur JavaScript non interceptée** ; Chromium `153.0.8010.12` sous Linux, véritables DLL Bridge/Simulation. Rapport : `/tmp/filpro-integration-20261026/ui-catalog-receipt-03/report.json`. |
| Windows | **Non qualifié**. Le [run 40286842500](https://github.com/M4D135/FILPRO/actions/runs/40286842500) et son [job 121452389301](https://github.com/M4D135/FILPRO/actions/runs/40286842500/job/121452389301) sont bloqués par la facturation GitHub Actions ; aucune étape exécutée. |
| Distribution | Aucun pack PROD qualifié, aucun pack final ni Release V2 finale. |
| Garde finale des sources gelées | Résultat, compteurs et SHA exact consignés dans le reçu externe de la garde d’origine, sans qualification Windows. |

Le total C# comprend les dix suites réussies de `final-targeted-01` (70 contrôles),
les six contrôles Legacy réussis dans `final-targeted-03` et les trois contrôles
Cleanup réussis dans `final-targeted-02`. Les `report.json` et journaux demeurent
dans `/tmp/filpro-integration-20261026/` sous ces trois dossiers. Les tentatives
01 et 02 gardent leur échec Legacy intermédiaire ; seule la passe corrigée 03 est
retenue pour cette suite. Il ne s'agit donc ni d'une passe unique ni d'un cumul
des répétitions. Ces contrôles exécutés sous Linux ne sont pas une qualification
Windows et ne couvrent pas toute la recette métier réelle.

Le premier essai UI a révélé une règle CSS qui annulait l'effet de `hidden`. La
correction ciblée dans les styles FILPRO conserve le masquage réel des contrôles.
Le troisième essai, après recompilation du harnais sur les dernières DLL et
extension finale de la règle `hidden`, prouve le parcours fictif de
confirmation/catalogue/relecture et de reprise par reçu, ainsi que ses refus
natifs. Les exécutants Windows Platform ne sont pas chargés par ce harnais :
leurs opérations réelles restent hors de cette preuve.

Les contrôles antérieurs du parcours général et des dispositions à 820×540 et
1280×800 restent attachés à leurs lots dans [VALIDATION](../VALIDATION.md). Aucun
résultat portable ne qualifie WinForms/WebView2, les profils réels, USMT, les
handles Win32 ou l'intégrité d'une restauration Windows. Les tests restants
doivent se limiter aux scénarios Windows/VM critiques déjà définis dans ce guide.

## Livraison et suite

La distribution reste **`FILPRO.exe` + dossier de ressources `wad`**, avec un ZIP
standard éventuel pour transport et aucune archive WAD personnalisée. Les données
de profils restent limitées au stockage local, USB ou UNC ; aucun cloud/OneDrive
n'est ajouté. Le jeton manuel GitHub facultatif reste en mémoire de session,
sans export ni persistance ; son transport contrôlé sur réponses fictives ne
prouve pas une mise à jour depuis une Release privée réelle.

Les gardes de publication et les packs V1 restent préservés. Les packs PROD/DEV
finaux et leurs guides de distribution ne doivent être actualisés comme prêts
qu'après qualification de leurs octets réels. L'état courant demeure détaillé
dans [ARCHITECTURE](../../ARCHITECTURE.md), [PARITE](../PARITE.json),
[VALIDATION](../VALIDATION.md) et [Configuration/mises à jour](../CONFIGURATION-UPDATES.md).
