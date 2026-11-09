# FILPRO V2 — périmètre de travail

## Reprise du 9 novembre 2026

David demande de terminer les points restants après la publication de la 2.0.15 :
jonctions AppData, documentation et validation. Le dépôt GitHub a été recréé le
9 novembre ; le clone actif est `/Users/david/Projects/FILPRO`. Les références
`v1` et `v1-final` citées ci-dessous sont historiques et n'existent plus dans le
dépôt public. Ne jamais repousser les anciennes branches ni les anciens tags.
Préserver les copies locales d'archives. Les règles de périmètre profils et de
qualification des octets livrés restent applicables.

## Décision de David — 28 octobre 2026, après la release 2.0.2

La V2 devient la branche `master` ; la V1 passe en historique. Elle est conservée
intacte dans la branche `v1` et le tag `v1-final` (`42f5137`). La racine de
`master` ne contient plus que `README.md`, `.gitignore`, `.github/` et `v2/`.
Cette décision remplace les consignes ci-dessous sur `wad/`, `packs/`, `DOC/` en
lecture seule et sur la branche `v2`. Les règles de périmètre profils restent.

## Priorité actuelle de David — 28 octobre 2026

Finir FILPRO2 uniquement. L’accord de famille d’icônes avec DOMENIA, GLPI et
PESTO est acquis ; aucun nouveau chantier transverse. DEX reste en pause et les
jeux sont exclus. Travail Codex et agents autorisé explicitement par David.

Terminer et pousser le code, puis exécuter et vérifier les tests Windows GitHub
sur ce commit. Cette consigne rétablit GitHub Actions après la dispense liée à
la facturation. Les nouvelles recettes VM viendront à la toute fin ; ne pas les
anticiper. Un push de sources ne qualifie pas un pack PROD : conserver les gardes
et les preuves existantes, sans réattribuer le reçu 2.0.1 à la version 2.0.2.

## Décision prioritaire de David — 26 octobre 2026

FILPRO touche uniquement les profils : fichiers personnels, AppData et registre HKCU.
Aucune extension de sauvegarde/restauration machine, image système, HKLM ou paramètres
Windows partagés. L'accord envisagé pour une protection complète a été révoqué.
Retirer l'option MigApp générale ; préserver la lecture des anciens magasins et la
récupération indépendante. Ne pas lever les refus de restauration hors profil.
Finaliser l'existant avec des contrôles ciblés, sans grosse fonctionnalité ni matrice
de tests supplémentaire. Continuer sans GitHub Actions, bloqué côté facturation.

Demande David du 23 octobre 2026 : faire FILPRO V2 comme DOMENIA V2, même type d’interface, en confiant le développement au Spark pour économiser les tokens.

- Cible : C# .NET 10 autonome Windows x64, WebView2 fixe, HTML/CSS/JS en modules ES natifs. Réutiliser le kit de DOMENIA ; ressources locales uniquement.
- Modifier uniquement `v2/`. `wad/`, `packs/`, `DOC/` et les références sont en lecture seule. Préserver la V1 et ses magasins. Aucun format WAD custom : distribution normale.
- Stockages utilisateur : local, USB, partage LAN ; aucun connecteur cloud/OneDrive. Aucun transfert réseau implicite.
- Parité réelle avec FILPRO V1 à inventorier : profils et sessions, analyser/réparer ensemble, sauvegarder/restaurer, logiciels (fichiers+registre+modèles communs), suivi des tâches, récupération, journal, paramètres, démonstration.
- Préserver les invariants de restauration prouvés : confirmation avant préparation, sauvegarde préalable, politiques de conflits par portée, multi-profils, clés de chiffrement, hives déchargées dans finally, reprise et annulation honnêtes. Un arrêt demandé n’est pas un arrêt achevé.
- Le pont UI est typé et fermé ; aucune commande arbitraire ou PowerShell libre depuis le JavaScript. Moteur asynchrone, progression par opération et sélection automatique de la tâche courante.
- Simulation explicite, fournisseur séparé sans action système réelle. Aucune donnée fictive montrée comme résultat réel. Les fonctions manquantes indiquent clairement leur indisponibilité.
- Aucun commit/push/publication par le worker. Aucun accès VM, AD, réseau métier, service, runtime, autre projet ou installation de dépendance. Les références DOMENIA ne sont pas à modifier.
- Code, brouillons et états du Spark sont à relire indépendamment avant intégration. Aucun statut « testé » ou « terminé » sans preuve. Pas de longues suites répétées ; écrire les contrôles utiles, compilation/validation Windows ensuite.
- Un lot court à la fois. Arrêter sur un rapport READY et transmettre les limites ainsi que les fichiers changés. Aucun sous-agent cloud ; aucune substitution de modèle.

## Priorité David — 25 octobre 2026, ordre de travail

FULL SPARK, focus développement jusqu’à terminer les vrais exécutants. Ordre demandé : développement complet, commit/push des sources V2 par le coordinateur, puis tests et corrections, enfin packs validés. Ne lancer aucune suite ni VM pendant le développement. Le worker reste limité à v2 sans commit/push direct. Ne jamais qualifier un brouillon ou fabriquer un reçu ; garder les manques explicites.

## Extension demandée le 25 octobre : mises à jour et démonstration
Lire docs/specifications/updates-demo-20261025/ADAPTATION-FILPRO.md. La spécification utilisateur complète est jointe dans ce dossier. Les lots principaux continuent leur objectif limité ; une file séparée traitera les manques de cette extension après leur fin. Ne pas déclarer tout le développement terminé sans cette extension. FULL SPARK, commit/push sources par coordinateur puis tests/corrections puis packs validés.

## Tests VM — dernière consigne David
Uniquement les scénarios critiques après développement et commit/push sources : démarrage, sauvegarde/restauration multi-profils fichiers+registre/intégrité/conflits/déchargement ruches, annulation/échec sans perte, chiffrement si livré, isolation démo et mise à jour/rollback/reprise sur installation jetable. Pas de matrice exhaustive ni captures systématiques. Tests ciblés hors VM lorsque suffisants, aucune qualification globale inventée. Le worker continue le développement sans lancer de tests VM.

## Export/import de configuration demandé par David
Inclure docs/specifications/updates-demo-20261025/BOOTSTRAP-CONFIG.md dans la cible : export/import manuels et bootstrap automatique au premier lancement dans AppData, sans écraser une configuration existante. La file extension planifiera ces micro-lots ; ne pas dupliquer le worker actif.

Précision prioritaire : si une configuration existe au premier lancement d’une nouvelle version, proposer son remplacement par la configuration fournie (aperçu, sauvegarde atomique, choix garder/appliquer mémorisé). Ne plus simplement ignorer le bootstrap existant. Lire la dernière section BOOTSTRAP-CONFIG.md.

Dernier parcours configuration demandé : bouton « Définir comme configuration par défaut » dans Paramètres, écrit les réglages partageables dans le dossier PROD pour le diffuser directement. Lire la dernière section BOOTSTRAP-CONFIG.md ; préserver intégrité des binaires, profils existants et isolation démo. Export/import restent complémentaires.
