# FILPRO V2 — architecture implémentée et limites de qualification

État des sources relu le **26 octobre 2026**, version `2.0.0-dev`. Les services de sauvegarde, restauration, préparation de réparation, récupération, configuration et mise à jour sont maintenant présents et raccordés au pont. Les sept pages utilisent ces contrats ; le fournisseur de démonstration les simule dans un monde C# en mémoire.

**La présence du code et les contrôles portables ne constituent pas une qualification Windows.** Le run GitHub Actions Windows est bloqué par la facturation avant toute étape. Aucun pack PROD n'est qualifié, aucune parité V1 complète n'est déclarée. La récupération indépendante des fichiers V1 et les magasins V2 non compressés ont des chemins d'exécution implémentés ; la restauration native V1 et celle des magasins avec MigApp restent bloquées.

Ce document décrit le code à la date indiquée ci-dessus ; [REPRISE](REPRISE.md) porte l'état de livraison courant. Les consignes et comptes rendus `SPARK-*` sont consultables dans [l'historique Git](https://github.com/M4D135/FILPRO/tree/f4e8892ccd5ed80551cbdd4b895af857b9ae7de1/v2/docs/reprise) : leurs mentions « moteur absent » ou « registre hors ligne absent » ne décrivent plus cette architecture. La matrice [PARITE.json](docs/PARITE.json) sépare les parcours présents de leurs limites ; [VALIDATION](docs/VALIDATION.md) conserve les preuves et leur rattachement exact aux sources.

## 1. Socle et provenance

L'hôte est une application **C#/.NET 10 autonome Windows x64**, WinForms et WebView2. Le projet cible `net10.0-windows`, publie `FILPRO.exe` en fichier unique avec le runtime .NET et utilise le SDK WebView2 `1.0.4258.31`. Les ressources web sont des modules HTML/CSS/JS locaux, sans chaîne de compilation JavaScript ni dépendance distante à l'exécution.

Le véritable socle DOMENIA est celui de [M4D135/domenia-d120](https://github.com/M4D135/domenia-d120), branche `v2`, relue au commit `9b1c0333c34020ad88c1448874b91eff884d5406`. La provenance du kit importé reste le commit `77805257d2868b34aac9c97b0d523f50e8b1e3f8`, décrit dans [KIT-PROVENANCE](docs/reprise/KIT-PROVENANCE.md). Les CSS et modules du kit de référence restent conservés ; la coque et les pages FILPRO ajoutent leurs modules et feuilles séparées.

Les sources Spark **jusqu'au lot 004A seulement** sont présentes depuis `5818fd17d1fab5b63868d3ecc7a4c4893bbc115b`. Le lot d'intégration part de `716812cd30939da569f04cb5e2d7c731a5fa1e55`. Les développements de reprise ultérieurs sont distingués de ce transfert ; aucun autre travail privé Spark n'est présenté comme récupéré. Le dernier état distant publié avant ce lot est `564f86cf1bb8ff5992e47187086a8c1db2313371`, après `00039d6` et `984f937`. Le rattachement final du présent lot sera consigné dans [Finalisation des sources](docs/reprise/FINALISATION-SOURCES-20261026.md). Les sources V1 sous `wad/`, leurs packs et leurs gardes restent préservés.

## 2. Répartition des responsabilités

| Emplacement sous `v2/` | Responsabilité actuelle |
| --- | --- |
| `src/Filpro.App/` | Hôte WinForms/WebView2, composition des services par mode, validation du pack, cycle de session et fermeture, événements, sélecteurs natifs de chemins, lancement du worker de mise à jour. |
| `src/Filpro.Core/` | DTO et interfaces, règles pures, codecs fermés, plans, choix de logiciels, politique des chemins, coordination asynchrone des travaux. Aucune dépendance à WebView2 ou à l'exécutant Windows. |
| `src/Filpro.Bridge/` | Messages et arguments fermés, admission par capacité et mode, plans conservés côté moteur, routes de lecture/confirmation, événements. |
| `src/Filpro.Platform/` | Lecteurs Windows, persistance locale, copie privée des ruches, exécution USMT, reçus et réparation, intégrité des packs et transactions de mise à jour. |
| `src/Filpro.Simulation/` | Implémentations des mêmes interfaces avec profils, magasins, reçus, configuration et mises à jour fictifs en mémoire ; référence Core seule. |
| `web/` | Kit DOMENIA, coque, pages, dialogues, sélection, suivi et traitement des événements. |
| `tests/Filpro.Tests/`, `tests/web/` | Contrôles ciblés des contrats, règles, façades, transactions et comportements web ; tests Windows séparés. |
| `tools/packaging/` | Génération d'une nouvelle distribution et de son ZIP standard ; garde PROD exigeant la preuve Windows sur les fichiers exacts. |

Il n'existe pas de projet d'updater indépendant : le worker transactionnel est un mode du même exécutable vérifié, recopié hors de l'installation avant son remplacement.

## 3. Composition, capacités et pont

`Composition` crée un ensemble de services immuable pour une session. Le mode démonstration reçoit `SimulationWorld`, ses services de configuration/mise à jour et un fournisseur de choix communs en mémoire. Le mode poste reçoit les lecteurs Windows, la configuration locale, les choix communs locaux et le service de mise à jour. Le service d'exécution natif n'est enregistré qu'après validation du manifeste du pack et des outils USMT.

Les capacités ne découlent donc pas seulement de la présence d'un bouton. Elles dépendent du mode, de Windows, des droits administrateur, des ressources vérifiées et de l'absence d'un nettoyage de ruche non confirmé. Un lancement depuis les sources sans manifeste permet la consultation mais n'active pas les écritures natives. L'hôte utilise `asInvoker` : aucune élévation silencieuse n'est ajoutée. Le contexte poste est le défaut d'un pack PROD valide ; DEV et les sources utilisent la démonstration par défaut, avec les options de mode explicites prévues par `ModeResolver`.

Le pont refuse les opérations et champs inconnus. Il ne reçoit ni commande shell, ni script PowerShell libre, ni nom d'exécutable arbitraire. La composition choisit le fournisseur ; un argument JavaScript ne peut pas sélectionner un moteur natif.

Les plans de sauvegarde, restauration, réparation et récupération sont conservés dans `ConfirmedPlanStore` côté C#. Ils sont bornés, expirent, sont protégés contre une modification après revue et ne peuvent être consommés qu'une fois. La confirmation fournit l'identifiant du plan et les accusés nécessaires, pas un remplacement de son contenu. Les magasins et les cibles sont relus ; une simple case « prêt » ne fait plus autorité sur l'exploitabilité d'un magasin.

### Confirmation Windows des opérations privilégiées (2.0.8)

Le moteur WebView2 s'exécute sous le compte de la session, à intégrité moyenne : tout programme de cette session peut piloter la page. La page ne fait donc plus que demander. Les dix opérations de `PrivilegePolicy` (sauvegarde, restauration, réparation et son retour arrière, catalogue, récupération, réglages par défaut du pack, préparation, installation et retour arrière d'une mise à jour) n'ont d'effet en mode poste qu'après un accord donné dans la fenêtre « FILPRO — Confirmation Windows ». C'est une fenêtre WinForms modale du processus élevé, hors de WebView2 : Windows (UIPI) empêche un programme non élevé de la cliquer, de lui envoyer des touches ou de la piloter.

- La liste est fermée et fixée dans le code. Au démarrage, une écriture absente de la liste ou un nom périmé arrête FILPRO ; aucun réglage, aucune variable et aucun argument de la page ne la modifie.
- Chaque gestionnaire garde ses contrôles, réserve l'unique fenêtre avant de consommer quoi que ce soit, prend son bail, puis retire le plan, relit le reçu ou le magasin, ou retrouve l'offre, l'aperçu et la transaction conservés par l'hôte. Ces étapes refusent avant toute fenêtre.
- `ConfirmationSheets` construit la fenêtre à partir des seuls objets détenus par l'hôte ; chaque information porte son origine (FILPRO, magasin, interface). Le texte libre de la page n'y figure jamais et les caractères invisibles ou de contrôle sont rendus visibles et signalés.
- Ce qui est montré est ce qui s'exécute : même instance de plan, reçu de réparation lié par son SHA-256 jusqu'au travail de retour arrière, catalogue lié à l'empreinte du manifeste, offre, aperçu et transaction retrouvés par identifiant côté hôte.
- Seul un clic sur le bouton d'accord autorise l'opération. Annuler a le focus ; Entrée, Échap, Alt+F4 et la fermeture refusent. Le bouton reste désactivé 1,5 s après chaque activation (3 s si la demande est arrivée quand FILPRO n'était pas au premier plan), quand la fenêtre perd le focus et tant qu'une fenêtre d'un autre programme la recouvre. Sans réponse en 2 minutes, l'opération est refusée. Une seule fenêtre native à la fois, sélecteur de fichiers compris ; 5 s d'attente après un refus.
- En démonstration, aucune fenêtre : les écritures restent refusées à l'admission.
- Au démarrage, les startup hooks .NET sont désactivés et les variables `WEBVIEW2_*` sont retirées du processus : sous le même compte, elles chargeraient du code dans FILPRO élevé ou remplaceraient le moteur et son dossier.

Limites connues : une personne au clavier de la session peut cliquer la fenêtre ; sous le même compte, l'UAC n'est pas une frontière de sécurité (variables `CORECLR_*` lues avant FILPRO, données de `%LOCALAPPDATA%\FILPRO` modifiables sans élévation) ; un pack modifiable par la session permet de remplacer FILPRO.exe avant l'UAC, les binaires n'étant pas signés ; la page peut afficher une fausse progression.

### Session d'un autre compte (2.0.9)

Quand l'affichage tourne sous un autre compte que FILPRO élevé (utilisateur standard, administrateur saisi dans l'UAC), ou sous un compte indéterminé, des protections s'ajoutent à la fenêtre de confirmation ; sous le même compte, rien ne change.

- La situation est établie quand l'affichage démarre, d'après le propriétaire du processus navigateur de WebView2 ; absente ou indéterminée, elle compte comme un autre compte. Le journal l'écrit sur une ligne : « … : autre compte ; mises à jour indisponibles, verrouillage de présence actif. »
- Les mises à jour du code de FILPRO (recherche de l'offre, source mémorisée, préparation, installation, retour arrière) sont refusées à l'admission (`session.crossAccount`) : le compte de la session ne choisit pas la source des prochaines mises à jour du compte élevé. La page masque la section et renvoie vers une session administrateur.
- Un verrou de présence, unique pour le processus élevé et conservé à travers la démonstration, se ferme dans un seul sens : session Windows verrouillée, déconnectée ou fermée, y compris pendant le démarrage de FILPRO (appliqué dès que la situation est connue) et d'après l'état de la session relu toutes les 10 s ; 15 minutes sans saisie ; veille ou relevés interrompus plus de 2 minutes ; 30 minutes après le lancement élevé ; 3 refus ou absences de réponse en 10 minutes ; ou accord donné avec la case « Verrouiller FILPRO après le lancement (suivi seulement) », cochée par défaut dans la fenêtre des opérations sur le poste : le verrou est posé dès l'accord, avant le lancement, et tient même si le lancement échoue. Les durées suivent le compteur de Windows, veille comprise, jamais l'heure du poste. Verrouillé, seuls la lecture locale, le suivi, l'annulation et la fermeture restent admis (`presence.locked`) : ni démonstration, ni sélecteur de fichiers, ni nouvelle opération ; un accord donné après le verrouillage ne lance rien. La fenêtre en attente est refusée, un sélecteur ouvert est fermé, le titre porte « — verrouillé » et la page affiche un bandeau. Seule une relance, avec une nouvelle UAC, déverrouille.
- Un sélecteur de fichiers resté ouvert se ferme seul après 2 minutes, comme Annuler.
- Si le compte de la session peut modifier FILPRO.exe, le dossier de FILPRO ou wad, ou renommer, supprimer ou redéfinir les droits de l'un des dossiers parents jusqu'à la racine du volume (droits effectifs évalués par Authz ; volume FAT ou exFAT compris), FILPRO l'écrit au journal, l'annonce dans la page et ajoute un point d'attention grave à chaque fenêtre de confirmation. C'est un avertissement, jamais un refus.

Limites connues : une personne qui prend le clavier avant le verrouillage peut accepter une fenêtre ou parcourir le poste avec le sélecteur élevé ; seule une revérification d'identité, non prévue, fermerait ce cas. L'inactivité suit la dernière saisie de toute la session.

### Aspect de la fenêtre de confirmation (2.0.10)

La fenêtre reprend l'aspect de la page sans en dépendre : logo FILPRO dans la barre de titre et l'en-tête, filet du dégradé de marque, polices Segoe UI Variable (à défaut Segoe UI ; Cascadia Mono ou Consolas pour l'empreinte), valeurs dans des cadres arrondis, bouton d'accord au dégradé de marque une fois armé et gris lisible tant qu'il est désactivé, Annuler en contour, anneau du focus toujours visible sur le bouton qui a le focus.

- Les couleurs viennent d'une table fermée compilée dans FILPRO (`ConfirmationTheme` : jetons clair, sombre et contraste de `tokens.css`) ; aucune couleur ne vient de la page. Seul le nom du thème est lu, par FILPRO élevé, dans ses propres réglages (liste fermée `system`, `light`, `dark`, `contrast`), sans effet sur les réglages ni sur leurs aperçus, avec une attente bornée à une seconde, sinon `system` ; `system` suit le mode des applications de Windows du compte élevé, qui peut différer de celui de la session sous un autre compte. Chaque palette garde un contraste WCAG AA pour les textes et 3:1 pour le contour des cases et du bouton Annuler, la case cochée et l'anneau du focus (`ConfirmationThemeTests`).
- En contraste élevé de Windows, la fenêtre garde les couleurs et les contrôles système de 2.0.9.
- Boutons et cases restent des contrôles Windows dessinés par FILPRO : noms, AutomationId, clavier, UI Automation, UIPI, délai d'armement, recouvrement et refus inchangés. Sur Windows 11, la barre de titre prend les couleurs de la palette, mode sombre compris ; ailleurs, un attribut refusé laisse la barre système.

Le coordinateur autorise un travail métier actif, publie des instantanés et attend le délégué ainsi que son nettoyage lors d'une annulation. `CancellationRequested`, `Cancelled`, `Partial`, `Failed` et `Succeeded` représentent des situations distinctes. La progression et l'estimation restent nulles quand aucune mesure n'est disponible.

## 4. Profils, logiciels et ruches

`WindowsPosteSource` lit les profils/SID, le compte courant et les sessions WTS. Les états inconnus sont conservés. Une restauration ou réparation refuse le compte courant, une session active **ou déconnectée**, une lecture de sessions incomplète et toute ruche dont le déchargement n'est pas établi. Les identités et les chemins sont relus avant mutation.

`SoftwareDiscoveryCatalog` conserve les recettes V1 avec leurs racines précises, recommandations et métadonnées de qualification. La catégorie source V1 décrit un domaine applicatif ; elle reste distincte de `SoftwareCategory`, qui décrit un type de données. Une recette marquée `RequiresQualification` n'est pas validée par sa seule présence dans ce catalogue. Le regroupement Office hérité peut sélectionner une recette individuellement non recommandée ; ce comportement est documenté, sans prétendre avoir qualifié son contenu.

Les lecteurs distinguent absence, accès refusé, erreur et découverte incomplète. Ils refusent les liens ou traversées non admis et bornent l'inventaire. Les racines génériques trop larges ne remplacent pas les recettes explicites. Les choix logiciels associent les fichiers **et** les clés de registre d'un même groupe. Le navigateur enregistre des identifiants de groupes ; le moteur reconstruit les éléments depuis l'inventaire connu.

Les derniers choix sont séparés par profil, y compris une sélection explicitement vide. Ils ont priorité sur les choix communs, puis sur les recommandations configurées. Un inventaire incomplet reste visible, mais ne peut pas écraser une sélection mémorisée ni produire un plan silencieusement amputé. Les choix communs ne sont pas inclus dans l'export des réglages partageables.

### Lecture hors ligne et propriété des handles

Pour une ruche déjà chargée, le lecteur ouvre le SID sous HKU en lecture et ferme ses propres handles ; il ne décharge pas la ruche Windows de l'utilisateur.

Pour un profil hors ligne admissible, le code copie `NTUSER.DAT` et les journaux présents dans un emplacement privé. La lecture porte sur cette copie, d'abord par `RegLoadAppKeyW` avec droits de lecture et portée processus. Le repli Windows utilise `RegLoadKeyW` uniquement sur cette copie et sous un tag privé possédé par FILPRO, quand `RegLoadAppKeyW` la refuse avec Win32 87 ou 1009. Windows 11 26100 rend 1009 pour tout `NTUSER.DAT` déjà ouvert en session, à cause du descripteur de `SystemCertificates\Root\ProtectedRoots` ; ce repli y devient donc le chemin normal. Win32 5 reste un échec : `RegLoadAppKeyW` demande la même lecture que l'ouverture du montage, qui serait refusée elle aussi. Il ne monte pas la ruche originale sous un faux SID et ne s'attribue pas un montage étranger. Chaque montage tient un événement nommé `Global\FILPRO_V2_<guid>` du chargement au déchargement confirmé. Au démarrage d'une opération, un tag `FILPRO_V2_` sous HKU n'est un résidu bloquant que si aucun processus vivant ne tient ce marqueur ; la lecture d'inventaire d'une autre session n'arrête donc plus les écritures.

`OfflineUserHiveManager` et `OwnedCopiedHiveMounts` organisent la fermeture des enfants avant la racine, le déchargement du montage possédé et la vérification de sa disparition. Les privilèges temporaires sont rétablis. Une erreur de nettoyage laisse une preuve durable et bloque les mutations suivantes. Ce mécanisme est implémenté et contrôlé par façades ; **la décharge effective Win32 après exception ou annulation reste à qualifier sous Windows**.

## 5. Exécution métier actuelle

### Sauvegarde et restauration

`WindowsFilproExecutionService` utilise `UsmtArguments` et `ProcessStartInfo.ArgumentList`, jamais un shell. Les règles générées, sélections par profil, catalogues et manifestes sont contrôlés. Le magasin V2 porte `filpro-backup.json` et des états par profil. Dans le profil, un fichier dont le nom contient `[`, `]` ou `%` reste couvert par le joker de son dossier ; seuls un dossier de premier niveau à inclure et un élément à écarter (lien, cloud, chemin trop long, autre magasin) doivent s'écrire dans une règle, et leur refus nomme le chemin à renommer avant toute copie. Un dossier dont le marqueur `filpro-backup.json` dépasserait 259 caractères n'est pas un magasin FILPRO. La préparation longue, l'estimation ScanState et la copie commencent après confirmation explicite.

La sauvegarde crée un nouveau magasin, vérifie l'espace, exécute USMT, contrôle le magasin produit et conserve les avertissements, journaux et résultats partiels. Le chiffrement utilise un fichier clé hors du magasin. VSS doit être choisi explicitement pour un profil chargé ; sa présence ne démontre pas à elle seule la cohérence transactionnelle de toutes les applications.

Pour une capture V2, la configuration issue du `/genconfig` natif de la source est restreinte afin de désactiver ses composants Windows ; configuration, règles et contenu sont ensuite liés par leurs empreintes. Le périmètre admis est celui des magasins réellement produits par FILPRO et conservés intacts. Ces empreintes prouvent la concordance des octets avec le manifeste, pas l'authenticité d'un ensemble manifeste et magasin intégralement forgé. Aucun protocole de signature ou de confiance entre machines n'est ajouté par ce lot.

MigApp est une option explicite de sauvegarde. Elle ajoute les réglages d'applications reconnues par USMT au périmètre des groupes FILPRO cochés. L'interface l'annonce avant confirmation et le manifeste conserve ce choix ; un catalogue incomplet n'est pas présenté comme une énumération exhaustive des données capturées. **La restauration native d'un magasin contenant MigApp est refusée** (`restore.migapp`) : ces règles peuvent modifier des paramètres machine que la sauvegarde préalable des profils ne couvre pas. L'inspection conserve cet avertissement ; la récupération indépendante des fichiers reste distincte.

La restauration inspecte les métadonnées du magasin, lie le plan à son identité et à son empreinte, valide les correspondances entre profils et les politiques de conflit, puis crée et vérifie une sauvegarde préalable des cibles. LoadState ne démarre pas si cette protection échoue. Si une interruption survient après une écriture possible, le résultat reste partiel avec reçu et sauvegarde préalable conservés : le programme ne prétend pas annuler intégralement les effets de LoadState.

### Anciens magasins V1 et magasins non compressés

`WindowsBackupStoreReader` reconnaît les métadonnées V1 sans fabriquer les identités, chemins ou empreintes absents. Les règles XML sont lues avec un parseur fermé et comparées à leurs SHA-256 ; les variantes admissibles de règles de sélection, MigDocs, exclusions et configuration sont distinguées des règles non reconnues. Les identités source et la version USMT restent des informations à établir depuis le magasin, jamais depuis le profil cible.

**LoadState reste systématiquement bloqué pour les magasins V1.** Un catalogue de fichiers et un XML de configuration hashé ne donnent pas la référence exhaustive des composants Windows que ScanState a pu capturer. Ils ne prouvent donc pas l'exclusion de paramètres système. Le code de compatibilité, les contrôles des règles/configuration/source/version USMT et la présence d'une méthode de restauration V1 ne constituent pas une autorisation d'atteindre LoadState. Lever ce refus nécessiterait une preuve supplémentaire de ce périmètre, pas seulement la réussite du catalogage.

Les magasins V2 non compressés utilisent les fichiers USMT ordinaires et un index JSONL SHA-256 (`PlainStoreIndexCodec`, `PlainStoreVerifier`). L'index enregistre les chemins, tailles et empreintes, avec un en-tête et une fin contrôlés. Avant application ou copie, le lecteur vérifie l'ensemble exact des fichiers, leurs empreintes et l'absence d'élément supplémentaire ; les entrées sont maintenues ouvertes pendant leur utilisation. Les liens ou chemins ambigus, index tronqués, doublons et dépassements de bornes sont refusés. Ce chemin ne prétend pas exécuter `/verify` sur un format que cette commande ne vérifie pas.

La borne est **100 000 fichiers, 16 384 dossiers et une profondeur de 128 par arborescence USMT vérifiée** (par profil dans un magasin V2) ; l'index est limité à 128 Mio. Le dépassement est un refus explicite, pas une validation partielle de l'intégrité. Pour un magasin V1 non compressé, l'index est établi après confirmation dans l'état privé de FILPRO, sans modifier l'ancien magasin ; il prouve son état mesuré lors de cette préparation, pas un hash historique de capture. Ces chemins restent à qualifier avec le vrai USMT et les garanties de handles Windows.

### Préparation de réparation et retour arrière

La réparation n'efface pas directement le profil. Après sauvegarde préalable vérifiée, le service conserve la ruche et les valeurs ProfileList nécessaires, renomme le dossier d'origine et détache seulement l'entrée visée. Un reçu décrit les étapes et permet un retour arrière contrôlé.

Le succès signifie **ancien profil conservé et recréation attendue**, pas profil déjà recréé ni données déjà restaurées. Une connexion volontaire ultérieure et la restauration constituent des étapes explicites. Le retour arrière vérifie son reçu et préserve les conflits éventuels ; il ne détruit pas un nouveau profil créé entre-temps pour masquer une difficulté.

L'écran Analyser permet aussi **« Reprendre depuis un reçu de réparation »**. En mode poste, un sélecteur natif choisit le reçu puis une confirmation précède sa relecture par le moteur. Le reçu doit appartenir au dossier privé des opérations FILPRO et sa clé locale d'authentification doit encore être disponible. La structure fermée, cette authentification, les empreintes des sauvegardes, les dossiers conservés et les sessions sont revérifiés ; un chemin choisi dans l'interface n'autorise pas à contourner ces contrôles. Les étapes de réconciliation distinguent les modifications déjà possibles d'un arrêt avant mutation. En démonstration, seule la liste des reçus fictifs encore connus de la génération courante est proposée.

### Récupération de fichiers

L'inspection courte lit les métadonnées et le catalogue disponible sans lancer une vérification longue cachée. Si le magasin n'a pas de catalogue, **« Préparer le catalogue »** demande confirmation et revue de capacité, puis démarre un travail `Catalog` : `/verify:all` pour un magasin compressé, indexation ou vérification SHA-256 pour les fichiers non compressés. Le catalogue préparé n'est publié dans le cache de session qu'après ce travail ; échec ou annulation ne publient pas un catalogue neuf. Un catalogue borné peut rester explicitement partiel.

Le suivi renvoie vers Récupérer avec l'identifiant du travail. La page relit ce travail côté moteur, vérifie son type et son état final, puis relit le magasin désigné par son résultat. Le dernier magasin reste une préférence en mémoire de la génération courante. Ni l'URL ni une déclaration du navigateur ne prouvent son intégrité. Pour V1, le plan est également lié à l'empreinte du contenu mesurée lors du catalogage. Une inspection suivante n'affirme pas que ce contenu est toujours vérifié : le contrôle est refait avant l'opération.

La récupération sélectionne des identifiants d'entrées du catalogue des magasins V2, ou V1 après cette préparation. Elle extrait les fichiers compressés ou copie les fichiers non compressés vérifiés dans un nouveau dossier, sans écraser une destination existante ni effectuer une restauration de compte ou de registre. Elle reste utilisable indépendamment des refus de restauration V1/MigApp. Un échec après création de sortie conserve les fichiers et un reçu partiel.

Les empreintes calculées sur les fichiers extraits servent au contrôle de leur copie. Lorsque le catalogue USMT ne contient pas le hash individuel d'origine, elles ne sont pas présentées comme une preuve d'intégrité individuelle remontant au fichier source. Pour les fichiers non compressés, le résultat est comparé au SHA-256 de l'index avant et après copie, dans les limites de provenance de cet index.

## 6. Interface et démonstration

Les routes `accueil`, `analyser`, `sauvegarder`, `suivi`, `recuperer`, `journal` et `parametres` disposent de pages raccordées. La coque reprend les composants, thèmes et mouvements DOMENIA avec navigation repliable, commande de repli en bas, modes d'animation système/complet/réduit et navigation clavier. Les styles FILPRO restent séparés du kit d'origine.

Le suivi affiche l'action courante, la progression et la durée lorsqu'elles sont connues, sélectionne la tâche en cours selon le réglage et notifie sa fin dans l'interface. Une notification système Windows externe n'est pas une capacité qualifiée de ce lot. Le journal représente les travaux de la session ; il ne remplace pas tout l'historique ou les rapports V1. L'Accueil natif n'invente pas d'index global de sauvegardes : une disponibilité absente est rendue « non inventorié », pas zéro sauvegarde prouvé.

`SimulationWorld` implémente les mêmes interfaces métier sans dépendance à Platform. Profils, magasins, choix, reçus, configuration et transactions sont fictifs et restent en mémoire. Les scénarios permettent réussite, résultat partiel, erreur et annulation, y compris préparation de réparation/retour arrière et mise à jour/retour arrière.

Les routes natives de classe `Write` restent refusées en démonstration, même avec des arguments forgés. Les démarrages fictifs passent par les routes `demonstration.*` et exigent un service de démonstration cohérent avec le mode. Entrer, sortir ou réinitialiser recrée toute la session après confirmation et sans opération active. Sa génération opaque empêche de reprendre un ancien plan ou une réponse tardive. Le profil de stockage WebView2 de démonstration est distinct du profil natif ; le service fictif ne lit ni n'écrit les données des profils Windows.

Le mock JavaScript reste un outil de prévisualisation. La preuve portable du parcours intégré utilise **le vrai pont C# et SimulationWorld**, reliés à Chromium/Linux par un harnais externe ; elle ne démontre ni l'hôte WebView2 ni les moteurs Windows.

## 7. Configuration et mises à jour

Les contrats et détails sont décrits dans [Configuration et mises à jour](docs/CONFIGURATION-UPDATES.md).

La configuration de travail est initialisée sous `%LOCALAPPDATA%\FILPRO`. Le codec d'export partageable est fermé : thème, animations et options prévues, sans identité, SID, chemin personnel, clé, secret ou historique. Les choix logiciels par profil et les modèles communs ne deviennent pas des données de déploiement.

L'import manuel présente un aperçu lié au contenu, à la révision courante et à une expiration. L'application sauvegarde et vérifie l'existant avant un remplacement atomique. Une configuration invalide n'est pas silencieusement effacée. Lorsqu'un nouveau pack fournit des défauts et qu'une configuration existe, le choix garder/appliquer est mémorisé ; une annulation ou une écriture échouée ne vaut pas décision réussie.

« Définir comme configuration par défaut » écrit, après aperçu et confirmation, `wad/config/deployment-defaults.json` dans le pack PROD courant. Ce fichier mutable est validé séparément ; il ne permet pas de contourner le contrôle des autres fichiers. Un pack non inscriptible provoque un refus, pas une écriture ailleurs présentée comme un succès.

Les mises à jour sont demandées explicitement depuis un dossier local/USB/UNC ou un ZIP manuel. L'ouverture de l'application et la lecture de l'état ne lisent aucune source. Les ZIP et dossiers sont contrôlés contre une empreinte attendue, un manifeste fermé et un inventaire exact ; tailles, chemins, liens, entrées étrangères et versions sont bornés. SHA256 prouve la concordance avec la référence fournie, pas l'identité d'un éditeur signé.

La source GitHub Releases, son client HTTP et son jeton de session ont été retirés après la 2.0.12 : FILPRO n'interroge plus aucun service en ligne pour ses mises à jour. Un ancien choix GitHub enregistré est relu comme un dossier à renseigner.

Préparer, installer et revenir en arrière sont des actions distinctes et confirmées. L'installation transactionnelle garde l'ancienne version, journalise les étapes, attend la fermeture des instances sans les tuer et exige une preuve de santé de la nouvelle version. Celle-ci suppose le pack vérifié et le rendu réel de l'interface avec `session.ready` ; démarrer un processus ne suffit pas. Un échec de démarrage déclenche le retour arrière prévu ; si celui-ci est empêché, les versions et le journal restent disponibles pour reprise. Les réglages AppData ne sont pas réécrits par un rollback de binaires. Une installation exécutée depuis UNC n'est pas remplaçable sur place par ce mécanisme.

## 8. Distribution et confinement

La distribution est classique : **`FILPRO.exe` et le dossier `wad`**. Le dossier contient notamment `web/`, `USMT/`, `config/` et `pack-manifest.json`, plus `runtime/webview2/` quand le pack WebView2 du Socle y est extrait. Les outils USMT x64 et `MigApp.xml` sont directement sous `wad/USMT/`. Un ZIP standard sert éventuellement au transfert ; **aucune archive WAD custom n'est créée**.

Seul PROD-LIGHT est publié ; son manifeste n'a jamais de `runtime/`. FILPRO y choisit d'abord WebView2 Evergreen enregistré pour la machine, puis le moteur fixe du pack commun du Socle Windows extrait dans `wad/runtime/webview2`. Ce dossier n'est admis que s'il reproduit exactement un index compilé (`WebViewSocle`, chemins, tailles et SHA-256) ; la liste des index connus ne fait que s'allonger, car une mise à jour recopie le Socle installé et la version suivante doit le relire. Les packs PROD et DEV, qui embarquent leur moteur dans le manifeste, restent lisibles mais ne sont plus publiés. Le lancement de développement peut utiliser un runtime système disponible lorsque le dossier fixe manque. La création d'un pack ne télécharge ni n'invente les binaires requis.

L'hôte sert les ressources locales à `https://filpro.local/`, bloque les navigations externes, nouvelles fenêtres et permissions non autorisées. Les services de stockage acceptent les emplacements locaux, USB et UNC contrôlés ; ils refusent les URI, chemins ambigus et racines cloud connues, dont OneDrive. Le téléchargement explicite de mises à jour n'est pas un stockage cloud de profils.

Le générateur [packaging](tools/packaging/README.md) travaille dans une nouvelle sortie, ne remplace pas les packs V1 et refuse un PROD sans preuve Windows complète liée à la version, au commit et aux octets exacts. Les gardes de publication demeurent en place.

## 9. Preuves disponibles et étapes encore nécessaires

La compilation croisée des projets, les contrôles C# ciblés sur données fictives/fichiers temporaires/façades, les contrôles JavaScript et le parcours Chromium/Linux relié au pont C# ont été exécutés. Les résultats, journaux et instantanés concernés figurent dans [l'intégration](docs/reprise/INTEGRATION-MOTEURS-20261026.md) et [VALIDATION](docs/VALIDATION.md). Aucun nombre de contrôles ni résultat d'un ancien instantané n'est utilisé ici comme certification du suivant.

La tentative Windows [run 40286842500](https://github.com/M4D135/FILPRO/actions/runs/40286842500), [job 121452389301](https://github.com/M4D135/FILPRO/actions/runs/40286842500/job/121452389301), est bloquée au niveau du compte GitHub Actions : paiement en échec ou plafond de dépenses atteint. **Aucune étape de ce job n'a été exécutée.** Ce blocage ne constitue ni un échec métier observé ni une validation Windows.

Les validations critiques restantes portent sur le démarrage du vrai hôte, les profils Windows multiples, USMT et le chiffrement, les données et conflits après restauration, le déchargement des ruches, l'annulation et les défaillances sans perte, l'isolation de la démonstration et la transaction de mise à jour avec retour arrière/reprise. Elles devront porter sur une installation jetable et les sources exactes retenues, conformément à [VALIDATION](docs/VALIDATION.md).

La restauration de profils depuis V1 reste fermée et sa parité n'est pas acquise. La récupération V1, les magasins non compressés et la reprise depuis reçu possèdent du code raccordé, mais leur exécution native complète reste à qualifier. Les preuves du dernier lot sont rattachées séparément dans [Finalisation des sources](docs/reprise/FINALISATION-SOURCES-20261026.md) ; celles d'un instantané antérieur ne certifient pas les modifications suivantes. Aucun pack PROD qualifié ni Release V2 finale n'est produit par ce lot.
