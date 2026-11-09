# Parcours GUI et monde de démonstration — 26 octobre 2026

## Périmètre réel de ce lot

Les pages de FILPRO utilisent le shell, les composants et les feuilles du kit DOMENIA déjà présents dans V2. Ce lot ajoute des raccordements dans `web/js`, une feuille `web/css/filpro-workflows.css` et les services en mémoire sous `Filpro.Simulation`. Il ne modifie pas les sept feuilles du kit ni `web/js/kit` ou `ui.js`. La provenance du kit est détaillée dans `GUI-DOMENIA-20261025.md` ; aucun dépôt DOMENIA n’est modifié.

Les pages de navigation possèdent désormais leur module : Accueil, Suivi, Journal, Analyser/réparer, Sauvegarder/restaurer, Récupérer et Paramètres. La présence d’un bouton ne donne pas une capacité native : les écrans consultent les capacités de `session.info`, et le pont contrôle à nouveau le fournisseur et le mode.

## Raccordements

| Parcours | Sources et comportement |
| --- | --- |
| Sauvegarde | Sélection de plusieurs SID ; brouillons et enregistrement séparés par profil. Les fichiers et les clés d’un groupe sont retenus ensemble. Une découverte incomplète interdit de remplacer les choix mémorisés. `MigApp` est visible dans les options et rappelé dans la confirmation avec son périmètre supplémentaire. |
| Choix communs | Bouton « Utiliser comme choix communs », avec confirmation, à partir d’une sélection complète et enregistrée. Les sélections explicites par profil restent prioritaires, y compris une sélection vide. Effacement séparé dans Paramètres. Les choix communs ne sont pas des réglages partageables du pack. |
| Restauration | Inspection du magasin avant les correspondances. Plusieurs sources et plusieurs cibles explicites, sans fusion silencieuse. Les politiques proposées viennent des groupes présents dans le magasin inspecté. Les profils chargés, avec session ouverte ou déconnectée, ou dont les sessions sont inconnues restent indisponibles. Confirmation des identités, de la capacité et de la sauvegarde préalable. |
| Réparation | Plan comportant le profil, le dossier préservé et la sauvegarde préalable ; confirmation avant préparation. Le reçu peut servir à demander un retour arrière depuis le suivi. Une réparation préparée ne signifie pas qu’une nouvelle session Windows a été ouverte. |
| Récupération | Catalogue inspecté, choix explicite de fichiers, nouveau dossier, revue avant préparation. Le pont et le service décident de la disponibilité des fichiers et du plan. |
| Suivi et Journal | États fournis par `operations.*` et `operations.changed`. Action courante, durée, progression et estimation lorsqu’elles existent. Une estimation absente est affichée comme non estimée. Un arrêt demandé reste en cours jusqu’au bilan après nettoyage. Les erreurs et résultats partiels sont distincts d’une réussite. |
| Paramètres | Lecture/enregistrement avec révision ; export/import manuels ; aperçu puis bouton de configuration par défaut ; choix garder/appliquer pour le bootstrap. Les champs sont ceux du contrat fermé de configuration. |
| Mise à jour | Source GitHub Releases, dossier ou ZIP. Inspection, préparation, installation et retour arrière nécessitent leurs actions explicites. Les erreurs d’accès à GitHub ne sont pas transformées en « à jour ». Le ZIP demande une empreinte externe saisie ou le fichier `SHA256SUMS` voisin, conformément au service. |

Une confirmation d’exécution renvoie une opération suivie. Elle n’affiche pas un succès métier. Les nouvelles tâches sont sélectionnées selon le réglage correspondant ; la bande de suivi reste visible au-dessus de la page. Les notifications de fin de ce lot sont les toasts et annonces accessibles du kit.

## Frontière de démonstration

`SimulationWorld` fournit le catalogue, l’accueil, le poste fictif et l’exécution ; `SimulationConfigurationService` et `SimulationUpdateService` conservent leurs documents, aperçus et transactions en mémoire. Aucun de ces services ne dépend de `Filpro.Platform`. Le cycle de vie des tâches réutilise `ExecutionJobCoordinator`, partagé avec le contrat d’exécution ; le nettoyage se termine avant la publication de l’annulation ou du bilan.

Le catalogue initial contient six profils, quatre destinations et soixante magasins associés à ces profils. Les chemins sont des libellés fictifs sous `FILPRO-DEMO`, avec un partage `example.invalid`. Ils ne sont pas ouverts. Les actions mettent à jour le monde en mémoire uniquement après leur phase finale ; une erreur ou une annulation avant validation conserve l’état précédent. Le catalogue expose l’état fictif des profils après restauration ou préparation de réparation. Le retour arrière d’une réparation rétablit l’état mémorisé dans son reçu fictif.

Les routes de démarrage de démonstration sont distinctes : `demonstration.backup`, `restore`, `repair`, `recover`, `repairRollback`, `updateInstall` et `updateRollback`. Les routes d’écriture natives restent refusées en démonstration. Aucun champ transmis par la page ne sélectionne le fournisseur réel. Les changements de mode et la réinitialisation demandent une confirmation et une nouvelle session ; l’hôte refuse cette transition pendant une opération active.

Le client mémorise la génération de session, écarte les réponses devenues étrangères à ce contexte et garde les dispositions de démonstration en mémoire. Il ne lit ni n’écrit les préférences locales du contexte normal depuis la démonstration. Le mock navigateur existant reste une prévisualisation sans hôte, avec ses propres gardes : ses résultats ne constituent pas une preuve d’intégration du service C#.

## Santé du démarrage et fermeture

`session.ready` est demandé après validation de la génération, installation des gestionnaires du shell, des événements et de la fermeture, puis montage réel du module de page. Le routeur doit constater deux périodes sans chargement ni zone occupée. Une carte d’erreur de page empêche cet accusé. Aucun `finally` ne signale artificiellement un démarrage réussi.

`session.closeRequested` ouvre une confirmation. `session.confirmClose` demande l’arrêt et laisse l’hôte attendre le nettoyage. L’interface affiche l’arrêt en cours et n’annonce pas une annulation déjà terminée.

## Vérification et limites de preuve

Contrôles réalisés dans ce lot : syntaxe JavaScript ciblée ; tests Node des sélections multi-profils, des frontières de prévisualisation, de la génération de session et de l’ordre des états client. Le test client de comparaison d’états a été corrigé pour comparer les données et non l’identité de l’objet, puis relancé. Ces contrôles ne valident pas Windows ou WebView2.

`SimulationWorldTests.cs` ajoute des contrôles du catalogue cohérent, des chemins et plans étrangers refusés, de l’accord avant préparation, du nettoyage après annulation, des sélections séparées et du chiffrement fictif, de l’échec, de la réparation et de son retour arrière, ainsi que des réglages et mises à jour isolés. Ils sont remis au coordinateur pour compilation et exécution ciblées ; ce document ne les déclare pas réussis.

L’intégration du navigateur au vrai pont C# et au vrai `SimulationWorld` doit être distinguée du mock JavaScript. Une exécution de ce harnais sous Chromium/Linux reste une vérification des parcours et des contrats ; elle ne prouve ni les opérations Windows, ni le contrôle des ruches, ni la migration USMT, ni les transactions de mise à jour d’un EXE Windows. Les gardes de publication et la validation Windows critique restent applicables.
