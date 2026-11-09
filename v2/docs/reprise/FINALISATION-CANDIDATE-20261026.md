# Candidate FILPRO 2.0.0 — 26 octobre 2026

Ce lot prolonge `46a3999` et ne modifie que `v2/`. Le reçu externe identifie le
commit final : aucun SHA futur n'est inventé dans les sources. V1, ses packs,
les références et la garde de publication restent inchangés.

## Changements

- Journal privé persistant, bilan initial avant exécution et bilan final avant
  annonce du résultat. Une interruption sans bilan reste explicitement inconnue.
  Mille bilans maximum, sans suppression automatique. Les travaux actifs de la
  session restent distincts des anciens bilans pour fermer ou changer de mode.
- Verrou machine entre sessions administrateur et refus d'écriture si une ruche
  FILPRO résiduelle est observée ; aucune adoption ni décharge d'une ruche étrangère.
- Démarrage réactif, avertissement visible sur les défauts optionnels invalides.
  Les empreintes binaires restent obligatoires ; aucune validation de santé d'une
  mise à jour avec configuration invalide.
- Source de mise à jour mémorisée localement sans jeton ni accès réseau implicite.
  Patches de modèles logiciels avec aperçu, ajouts, conflits conservés par défaut,
  sauvegarde préalable et rejeu idempotent ; sélections des profils préservées.
- Guides, notices, déblocage volontaire et reconstruction DEV. Les annexes de
  `dotnet publish` sont rangées dans `wad/DEV` pour DEV et omises de PROD ; les
  entrées de compilation ne sont pas modifiées. Un fichier inconnu provoque un refus.

## Contrôles réellement exécutés

- Mac ARM64, SDK .NET 10.0.401 : compilation croisée des six projets ; publication
  autonome de l'application et du lanceur Windows x64. Zéro erreur/avertissement
  sur les compilations retenues. Les **103 contrats C# ciblés passent** après les
  dernières corrections, avec données fictives et fichiers temporaires.
- Hôte de laboratoire Windows x64 10.0.26200 : **43 suites, 330 contrôles réussis,
  zéro échec, zéro ignoré**. Exécutables autonomes compilés sur Mac. Lecture du HKU
  courant, hardlinks, verrous et remplacement atomique sont natifs ; les scénarios
  métier des contrats utilisent des façades et des données temporaires.
- La première passe Windows comportait sept échecs dans quatre suites. Ils ont
  révélé trois causes corrigées : `CreateFile` masquait l'absence derrière une
  IOException générique ; READ_ATTRIBUTES seul ne bloquait pas le renommage d'un
  dossier ; MoveFileEx ne remplaçait pas un bilan encore ouvert par un lecteur.
  Les exceptions natives sont distinguées, FILE_LIST_DIRECTORY active le verrou
  de partage et ReplaceFile conserve l'ancien instantané pour le lecteur.
- Chrome macOS avec le vrai pont C# et SimulationWorld : **10 contrôles** sur
  configuration initiale, modèles fictifs, rejeu, source mémorisée, journal et
  petite fenêtre ; aucune exception JavaScript ni erreur du pont sur ce parcours.
- **7 tests Python d'assemblage**, **31 tests de la garde originale**, syntaxe de
  **45 modules JavaScript et 3 fichiers Python** ; scripts PowerShell analysés sur
  Windows sans erreur. Un préassemblage réel de **335 fichiers** respecte EXE+wad.
- Le cache initial de WebView2 a ensuite été refusé : les locales manquaient.
  Le candidat DEV utilise la distribution Fixed Version x64 **155.0.4283.62**
  téléchargée depuis Microsoft, extraite complète (256 fichiers). SHA-256 du CAB :
  `cf54aa717a0ebd05b40e87620724583cb6eb0a5b8b64764cc11e42822f68bf6c`.
  Signature du lanceur `Valid` contrôlée dans la VM. USMT complet issu du dossier
  amd64 existant ; aucun runtime système copié.

## Correction issue de la première recette native

Les profils fraîchement créés par Windows contiennent les jonctions de compatibilité
`AppData/Local/Application Data` et `Temporary Internet Files` (`History` était déjà
exclu). Leur présence rendait tout l'inventaire incomplet et bloquait le plan avant
ScanState. Ces deux noms sont maintenant exclus de la découverte générique sous
Local. Une sélection explicite reste soumise aux contrôles de liens ; un lien inconnu
reste bloquant. Aucun lien n'est parcouru et le refus des inventaires partiels demeure.

Le cas de régression et les contrats voisins passent : **36 contrôles ciblés sur Mac,
36 sur Windows**. Dans FILPRO-W11-TEST, les inventaires natifs des deux profils jetables
sont complets et la préparation USMT a démarré. Ce constat ne qualifie pas encore la
sauvegarde ni la restauration complète.

La capture réelle du premier profil a ensuite terminé avec un code USMT nul.
`usmtutils /verify:all` a aussi renvoyé zéro, mais FILPRO refusait son rapport : le
lecteur attendait le marqueur artificiel `CATALOG`, tandis que USMT 10.0.26100
énumère `catalog.mig`, `MIGSTATE.DAT` et `CACHE.IMG` avec leurs états. Le catalogue
peut précéder le cache. Le lecteur reconnaît désormais ces trois métadonnées,
vérifie leur état sans les proposer comme fichiers récupérables et refuse les
doublons, corruptions et métadonnées inconnues. Le code retour du processus reste
contrôlé séparément. La [référence Microsoft sur /verify](https://learn.microsoft.com/en-us/windows/deployment/usmt/verify-the-condition-of-a-compressed-migration-store)
confirme que `all` comprend les fichiers et le catalogue du magasin.

Le rapport natif observé est reproduit dans le test de régression : **46 contrôles
ciblés passent sur Mac et 46 sur Windows**. La reprise complète en VM demeure
nécessaire ; le magasin de l'essai refusé n'est pas promu manuellement en réussite.

Les logs des premières tentatives et des reprises sont conservés séparément.
Les outils de test de l'interface n'exécutent ni WebView2 ni USMT natifs.

## Conditions de livraison encore distinctes

Le nouveau run GitHub et la garde doivent porter le commit exact. Les scénarios
VM critiques suivent la publication des sources, selon l'ordre demandé : démarrage,
multi-profils fichiers/registre/conflits/intégrité, ruches, annulation/échec,
chiffrement, isolation démo et mise à jour/rollback/reprise sur installation jetable.
Aucune réussite de ces scénarios n'est annoncée ici. PROD exige leurs preuves sur
les octets exacts ; un DEV candidat ne vaut pas qualification de production.

La restauration native des magasins V1 reste refusée sans preuve exhaustive
excluant les composants Windows. MigApp reste refusé à la restauration tant que
le préalable ne couvre pas les paramètres machine. La récupération indépendante
reste disponible ; ces refus ne sont pas levés par les tests de contrats.
