# Reprise GitHub — compilation et sélections

## Sources effectivement reçues

Branche `v2`, commit de transfert `5818fd17d1fab5b63868d3ecc7a4c4893bbc115b` :
142 fichiers sous `v2/`, couvrant les lots Spark 001, 002, 003A à 003E et 004A.
Les octets récupérés ont été comparés à leurs identifiants de blobs Git. Le message
du transfert dit explicitement « non compilé, non testé Windows ».

Le dépôt DOMENIA accessible est `M4D135/domenia-d120` ; sa branche `v2` a été examinée
au commit `9b1c0333c34020ad88c1448874b91eff884d5406`. FILPRO contient le véritable kit
de sa V2, dont la provenance au commit `77805257` est décrite dans
[KIT-PROVENANCE.md](KIT-PROVENANCE.md). Ce lot conserve les composants et le CSS
importés ; il modifie les pages propres à FILPRO et leur fournisseur fictif.

## Premier lot de reprise

- Correction des erreurs de compilation des brouillons C# et du double réglage DPI.
  Les six projets compilent avec le SDK 10.0.401, depuis Linux en ciblant Windows x64.
  Cela ne prouve pas leur démarrage sous Windows.
- Sauvegarde : plusieurs profils peuvent être inclus dans **un plan** ; le profil
  affiché pour modifier ses logiciels est distinct des profils inclus.
- `sauvegarder.saveSelection` accepte seulement un SID et des identifiants de
  groupes. Le fournisseur relit l'inventaire ; les fichiers et clés du groupe
  sont mémorisés ensemble. Aucun chemin fourni librement par JavaScript n'est écrit.
- Derniers choix distincts par SID, sélection vide conservée, recommandations
  appliquées seulement en l'absence de choix précédents, catégories de modèles
  communs conservées. En mode poste, le stockage de préférences est prévu dans
  `%LOCALAPPDATA%\FILPRO\Selections`, avec remplacement atomique et lecture bornée.
  En démonstration, les choix restent dans la mémoire du fournisseur fictif.
- La préservation des préférences refuse un document existant illisible, invalide
  ou d'une version inconnue avant toute création de temporaire. Une absence réelle
  permet seule de reprendre les valeurs par défaut ; les remplacements explicitement
  demandés restent possibles.
- Un inventaire incomplet refuse l'enregistrement avant toute mutation. Dans ce
  premier lot, le lecteur natif ne lisait pas encore le registre : le parcours
  Windows d'édition de sélection était donc explicitement bloqué. Le lot du
  26 octobre décrit ci-dessous ajoute la lecture des ruches déjà chargées ; les
  contrôles sur fichiers temporaires ne valident toujours pas le parcours Windows.
- Brouillons protégés lors de la navigation interne ; réponse tardive d'un autre
  profil ignorée ; préparation refusée si un choix inclus reste non enregistré.
- Le pont refuse un fournisseur d'un autre mode avant lecture ou écriture. Le plan
  de restauration reçoit le SID du compte exécutant et le refuse comme cible ;
  le profil des conflits doit appartenir aux cibles retenues.
- Mentions de sauvegarde préalable corrigées : une déclaration d'opérateur ne
  prouve ni un contrôle natif, ni l'intégrité actuelle du magasin.

## Présent, vérifiable et encore manquant

| Domaine | État du code après ce lot | Limite matérielle |
| --- | --- | --- |
| Hôte et kit | WinForms/WebView2, pont fermé, kit DOMENIA, navigation repliable | Fenêtre et runtime fixe non exécutés sous Windows |
| Profils | Lecteurs Windows et fournisseur fictif séparés | Lectures WTS/registre natif non qualifiées ici |
| Sélections | Catalogue V1 précis, découverte AppData et HKU des ruches déjà chargées, règles, persistance, pont et interface raccordés | Ruches hors ligne non lues ; édition refusée si inventaire incomplet ; lecteur natif non qualifié sous Windows |
| Sauvegarde | Plan multi-profils, validation pure, confirmation, arguments USMT | Aucun ScanState, aucun moteur de copie, aucune mesure ou progression réelle |
| Restauration | Plan, conflits, correspondances et protections de préparation | Aucun lecteur de manifeste, aucun LoadState ; interface encore limitée à une cible et une correspondance |
| Réparation et ruches | Handles de lecture du registre possédés et fermés ; aucun montage de ruche | Aucun exécutant de réparation ; cycle de montage/déchargement hors ligne absent |
| Configuration du pack | Spécification disponible | Export/import complet, défaut PROD, bootstrap, proposition mémorisée et intégrité du pack non implémentés |
| Mises à jour | Spécification disponible | GitHub Releases/local/UNC/ZIP, transaction et retour arrière non implémentés |
| Démonstration | Lectures et choix fictifs interactifs, écritures système refusées | Parcours d'exécution, progression et résultats simulés complets absents |
| Distribution | Architecture EXE + dossier de ressources prévue | Aucun pack V2 prêt ; packs V1 conservés |

Les cases de profils inclus, la destination et les options d'une opération ne sont
pas encore mémorisées à la réouverture de la page. La protection de navigation
interne ne constitue pas une preuve de fermeture sûre de WebView2 en cours d'action.

## Contrôles et suite

### Lot découverte des logiciels — 26 octobre 2026

Le [lecteur de logiciels](LECTURE-LOGICIELS-20261026.md) utilise les recettes précises
V1, les chemins mémorisés et les modèles communs validés. Il distingue l'absence
des accès refusés et signale les découvertes bornées ou interrompues comme partielles.
Le registre est lu uniquement sous le SID demandé dans `HKEY_USERS`, si la ruche est
déjà chargée par Windows. Les handles ouverts par FILPRO sont fermés ; la ruche
possédée par Windows n'est jamais déchargée par ce lecteur.

Le code autorise la mémorisation existante seulement lorsque les deux inventaires
sont complets. Ce raccordement ne constitue pas une validation Windows et n'active
aucun moteur de sauvegarde, restauration ou réparation. Les profils hors ligne
restent bloqués faute de lecteur de ruche adapté. Les limites de cohérence et le
comportement hérité des recommandations groupées sont détaillés dans la note du lot.

### Lot interface DOMENIA

Le [raccordement du shell et de l'accueil](GUI-DOMENIA-20261025.md) complète la reprise du vrai kit :
menu d'animations utilisable, suivi des préférences système, repli animé avec commande basse et libellé
cohérent, infobulles flottantes, dispositions de faible hauteur et grille d'accueil corrigée. Les cartes
de logiciels chargées après le premier rendu sont visibles. L'actualisation, la reprise après erreur et
les réponses tardives de l'accueil ont été contrôlées.

Douze contrôles web ont réussi dans Chromium Linux, sur des données fictives. Les empreintes des sources,
observations et captures sont conservées pour la garde du commit. Ce lot ne modifie pas le backend C# et
ne fournit aucune preuve d'exécution Windows/WebView2. Le code de progression métier, les vrais exécutants,
la configuration distribuable, les mises à jour et les packs restent dans l'état indiqué dans le tableau.

La compilation croisée et les contrôles C#/JavaScript portent sur les contrats,
les données fictives et des fichiers temporaires. Les contrôles du jeton Windows
et du verrou exclusif de partage sont réservés à Windows ; aucun résultat Linux
ne les remplace. Les journaux et reçus doivent
indiquer `windows_qualified=false`, le commit exact et les empreintes des sources.
[VALIDATION.md](../VALIDATION.md) décrit le script Windows et le workflow dédié
`.github/workflows/filpro-v2.yml`, activé à la demande de validation via GitHub.
Son rapport doit confirmer le SHA, le résultat et la portée de chaque exécution ;
un workflow présent ne constitue pas, à lui seul, une qualification Windows.

Priorité suivante : qualifier la lecture native ciblée, compléter les ruches hors
ligne avec un cycle de ressources sûr, puis relier les vrais exécutants aux plans et à une relecture
de manifeste. Préserver la sauvegarde préalable, les choix par portée, l'annulation
et les échecs sans perte avant d'ouvrir une opération `Write`. La configuration,
les mises à jour transactionnelles et la démonstration complète restent des lots
nécessaires avant qualification et packs PROD/DEV.

Si le Spark contient des lots postérieurs à 004A non publiés, transférer leurs sources,
ressources, migrations de schéma et états de lot avant de remplacer ces composants.
Leur récupération ne peut pas être déduite du seul transfert des lots présents.
