# Reprise du retour arrière après interruption

Base distante vérifiée : `9e790cd163e7d320449123067661856d19c11eed`, branche `v2`.
Les modifications de ce lot restent sous `v2/`. V1, packs, kit DOMENIA, workflow
et garde source sont inchangés. Version applicative conservée : `2.0.0-dev`.

## Défaut et correction

Après le dernier déplacement du retour arrière, l'ancienne version peut déjà
être à sa place alors que le journal indique encore `RollingBack` ou `Failed`.
Le service ne proposait plus la reprise faute de dossier `previous`, et la
transaction commune échouait en cherchant ce dossier. Le worker possédait un
cas spécial qui finalisait le journal sans verrou exclusif d'installation et
sans comparer explicitement la version attendue.

La reprise passe désormais par `UpdateDirectoryTransaction` dans tous les cas.
Le verrou, l'intégrité, la version et les défauts de l'ancienne copie sont
contrôlés avant de finaliser le journal. Si elle a déjà été rétablie, aucun
nouveau déplacement n'est fait. Un réessai finalisé ne réécrit pas le journal.
Les copies retirées restent conservées ; une incohérence échoue explicitement.
`LocalUpdateService` propose cette reprise, puis relit la transaction après
acquisition du verrou du worker avant toute décision de lancement.

Le bouton et le pont existants restent raccordés : `updates.read` expose
`CanRollback`, Paramètres affiche l'action, la confirmation appelle
`updates.rollback`, puis l'hôte ferme proprement la session avant le worker.
Le service d'état ne transforme pas une simple existence de dossier en succès.

## Vérification exécutée

- Deux régressions reproduites sur la base, puis corrigées : absence de reprise
  dans l'état renvoyé aux paramètres et échec de finalisation après déplacement.
- Six projets compilés pour Windows x64 sur macOS avec SDK 10.0.401, sans
  avertissement ni erreur ; 15 tests de transaction réussis sur macOS ARM64.
- 24 tests réussis sur Windows x64 10.0.26200.0 : transactions (15), intégrité
  des packs (8), refus des hardlinks sur un vrai handle Windows (1).
- Huit frontières de déplacement/journal sont exercées dans le test dédié,
  plus finalisation répétée, verrou indisponible, annulation, version différente,
  défauts/binaires modifiés et fichier ambigu à la place de `previous`.
- 31 tests de la garde de publication d'origine réussis, sans modifier la garde.

Les preuves brutes du travail sont conservées dans le dossier de session
`work/filpro-rollback/`. Côté Windows :
`C:\ADCC-Lab\validation\filpro-v2-rollback-20261026-codex`.
Les inventaires et journaux sont livrés séparément du code. Aucun rapport
de qualification PROD ni reçu de publication fictif n'est créé.

## Contrôles Windows restant nécessaires pour ce parcours

Sur deux copies réelles d'une installation de laboratoire qualifiée :

1. Préparer une mise à jour, garder une deuxième instance ouverte et vérifier
   que le worker attend sa fermeture sans arrêter une opération active.
2. Interrompre le worker après remise en place de l'ancienne version, avant
   le journal final. Relancer l'ancienne installation, confirmer la reprise
   proposée et vérifier version, défauts, état final et conservation du nouveau pack.
3. Répéter avec une copie modifiée : aucun succès ni écrasement. Conserver les
   deux copies et le journal. Tester aussi le lancement du worker conservé si
   le dossier d'installation manque après une interruption antérieure.

Les tests automatiques utilisent des fichiers fictifs et un lanceur substitué.
Ils ne qualifient pas le démarrage WebView2, la santé d'un vrai nouveau pack,
le remplacement d'un EXE ouvert ou une coupure électrique. Les refus LoadState
V1 et MigApp restent en vigueur ; aucun pack final ou Release n'est produit.
