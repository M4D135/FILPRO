# Intégration FILPRO V2 — 26 octobre 2026

## Provenance et limite de cette reprise

Base GitHub : branche `v2`, commit `716812cd30939da569f04cb5e2d7c731a5fa1e55`.
Les lots Spark jusqu’au 004A sont accessibles depuis `5818fd1`. Aucun travail
privé supplémentaire n’est présenté comme récupéré. Le kit DOMENIA reste celui
inventorié dans [KIT-PROVENANCE](KIT-PROVENANCE.md) ; ses fichiers de référence
ne sont pas réécrits. Les modifications de ce lot restent dans `v2/`.

## Code raccordé

| Domaine | Comportement implémenté | Limite de qualification |
| --- | --- | --- |
| Hôte | C#/.NET 10, WinForms/WebView2 fixe, ressources locales, validation du pack avant admission du pont | Compilation croisée ; fenêtre Windows non démarrée |
| Sauvegarde | Multi-profils, choix cohérents fichiers et registre, USMT par liste d’arguments, règles et manifestes hashés, progression et reçus | ScanState réel non exécuté |
| Restauration | Inspection du magasin, mappings relus, choix de conflits, sauvegarde préalable, revalidation du plan et des sources | LoadState et résultat métier non qualifiés |
| Réparation | Préparation avec sauvegarde préalable, profil d’origine conservé, reçu durable et retour arrière | Profil Windows réel non modifié ; connexion de l’utilisateur puis restauration restent des étapes explicites |
| Récupération | Sélection issue du catalogue contrôlé, extraction vers un nouveau dossier, résultat distinct d’une restauration | Extraction USMT non qualifiée |
| Ruches hors ligne | Copie privée, lecture AppKey, repli RegLoadKey possédé, fermeture des handles et déchargement dans le nettoyage | Façades contrôlées ; privilèges et déchargement Win32 non exécutés |
| Configuration | Schéma fermé, export/import avec aperçu, bootstrap, défauts du pack, sauvegarde et remplacement atomique | Contrats et fichiers temporaires contrôlés ; verrouillage Windows séparé |
| Mise à jour | GitHub public/dossier/UNC/ZIP explicites, validation stricte, préparation, worker transactionnel, santé au démarrage, rollback | Aucun remplacement réel d’EXE ni téléchargement de Release FILPRO vérifié |
| Démonstration | Monde C# en mémoire, sept pages, sauvegarde/restauration/réparation/récupération/configuration/update, erreurs et annulation | Parcours Chromium/Linux ; aucune qualification WebView2 native |

Les capacités dépendent du mode, de Windows, des droits, du pack vérifié et des
outils USMT. Un lancement depuis les sources sans manifeste n’active pas les
écritures natives. Un pack PROD valide demande le contexte poste par défaut ;
DEV et les sources démarrent en démonstration. `--simulation` reste prioritaire.

## Protections concrètes

- Les plans sont conservés côté C#, bornés, expirent et sont consommés une seule
  fois. Leur empreinte interdit la modification des collections après revue.
- Une découverte incomplète empêche la préparation d’un plan amputé ; les
  derniers choix restent mémorisés et l’interface propose de relire l’inventaire.
- L’annulation attend le nettoyage. La fermeture et le changement de mode
  bloquent les nouveaux démarrages et attendent les opérations déjà admises.
- Les échecs et résultats partiels restent distincts d’une réussite. Une erreur
  de nettoyage de ruche laisse un reçu, bloque les écritures suivantes et ne
  provoque jamais le déchargement d’une ruche appartenant à un autre processus.
- Les opérations réelles ne sont pas disponibles depuis le fournisseur fictif,
  y compris par appel direct des routes natives. Les générations de session
  empêchent de reprendre un plan d’une ancienne démonstration.
- Les chemins locaux/USB/UNC sont contrôlés ; les parents sensibles sont tenus
  ouverts, les points de réanalyse et fichiers à plusieurs liens sont refusés.
  Ces contrôles Win32 doivent encore être exercés sur Windows.
- Les règles MigApp sont optionnelles et leur portée supplémentaire est
  annoncée avant préparation. Un catalogue incomplet n’est pas présenté comme
  une énumération exhaustive des données capturées.

Les réglages exportables sont une liste fermée, sans identité, SID, chemins
personnels, clé de chiffrement ou historique. Le fichier mutable
`wad/config/deployment-defaults.json` est validé séparément : les hashes des
binaires restent inchangés. Voir [Configuration et mises à jour](../CONFIGURATION-UPDATES.md).

## Contrôles effectivement exécutés pour l’intégration

- `build-03` : compilation croisée Linux des six projets, zéro avertissement,
  zéro erreur.
- `targeted-03` : 99 contrôles C# réussis, zéro échec, zéro ignoré. Données
  fictives, fichiers temporaires et façades uniquement.
- `ui-04` : 25 contrôles de parcours réussis dans Chromium/Linux relié au vrai
  `BridgeDispatcher` C# et à `SimulationWorld`, sans fournisseur JavaScript
  fictif utilisé pour répondre. Sauvegarde de deux profils, restauration du
  résultat, réparation/rollback, extraction, configuration, update/rollback,
  échec, annulation et refus d’une écriture native sont exercés.

Ces preuves sont liées à l’instantané de travail conservé avec leurs journaux.
Le rattachement à un commit et la garde de publication sont des étapes
séparées ; un commit postérieur ne reprend pas automatiquement ces preuves.
Les suites natives Windows restent exclues de la preuve Linux.

## Écarts encore explicites dans ce lot

1. Les magasins V1 sont reconnus et leurs règles hashées, mais leur exécution
   n’est pas encore admise. Leur conversion ne doit pas inventer un SID source,
   des données absentes ou une intégrité qui n’a pas été vérifiée.
2. Les magasins non compressés sont refusés tant que leur catalogue et leurs
   preuves d’intégrité ne sont pas pris en charge.
3. Le client GitHub public ne peut pas lire le dépôt FILPRO privé sans un
   mécanisme d’authentification explicite. Aucune Release n’était publiée lors
   du contrôle du dépôt ; le transfert par dossier ou ZIP reste distinct.
4. La validation Windows est bloquée au niveau du compte GitHub Actions.
   Run `40286842500`, job `121452389301`, aucune étape exécutée. Le message
   GitHub indique paiement récent en échec ou plafond de dépenses atteint.
5. Aucun pack PROD/DEV final ni Release n’est produit. Le générateur de packs
   refuse PROD sans reçu Windows lié aux octets exacts et aux scénarios critiques.

La recette critique Windows est définie dans [VALIDATION](../VALIDATION.md).
Les packs V1, leur publication et leurs gardes sont conservés.
