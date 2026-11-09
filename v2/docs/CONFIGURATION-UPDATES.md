# Configuration partageable et mises à jour V2

Ce document décrit les services présents dans les sources. La présence du code et
des tests ne constitue pas une qualification Windows : le démarrage WebView2,
l'échange d'EXE et les verrous multi-instance doivent être exercés sur Windows.
Les reçus de validation indiquent séparément les contrôles effectivement exécutés.

## Stockages distincts

La source de mise à jour choisie et son dossier sont mémorisés séparément dans
`update-source.json`. Cette lecture n'ouvre pas le partage. Le chemin reste exclu
des réglages partageables. L'interface permet de le mémoriser sans recherche, puis
le retrouve après redémarrage. Sans fichier, la source proposée est un dossier à
renseigner. Un fichier écrit par une version antérieure avec l'ancienne source
GitHub Releases (`kind` 0, sans dossier) est lu de la même façon ; ce type n'est
plus enregistrable. En démonstration, même cette préférence reste fictive.

Un défaut de configuration fourni invalide n'empêche plus l'ouverture de l'UI :
l'inventaire et les binaires sont entièrement vérifiés, l'option est ignorée et
un avertissement est montré au démarrage et dans Paramètres. Cette tolérance est
réservée au démarrage ; extraction, installation, copie de pack et accusé de santé
restent stricts. Un fichier utilisateur illisible est conservé sans reset.

## Patches des modèles communs

L'export des modèles est distinct de l'export de préférences. Le document fermé
`schemaVersion: 1`, `application: FILPRO`, `kind: software-template-patch`, `selection`
reprend le codec de sélection validé, sans options d'exécution actives. Les clés
stables sont la portée et le chemin relatif normalisé ; un nouveau chemin est
un ajout, jamais une suppression ou un renommage implicite. Taille maximale :
1 Mio, limites de sélections existantes conservées. Aucun script n'est exécuté.

Le JSON et son compagnon `.sha256` sont relus à l'aperçu puis à l'application.
Le partage mémorisé peut être consulté explicitement pour `FILPRO-modeles.json`.
L'aperçu affiche ajouts, identiques et conflits. Par défaut les conflits gardent
la valeur locale ; le remplacement demande un choix explicite. Les absents sont
conservés. Une révision locale ou une source modifiée rend l'aperçu périmé.

La sauvegarde, les valeurs fusionnées et les 128 dernières empreintes appliquées
utilisent le document commun privé et une publication atomique. Les documents
communs V2 antérieurs restent lisibles. Réappliquer un patch déjà enregistré
n'écrit rien ; modifier explicitement le modèle local remet cet historique à zéro.
Les sélections personnelles et les profils restent intacts. La démonstration
utilise le même codec et la même fusion, avec un état entièrement en mémoire.

La composition fournit `%LOCALAPPDATA%\FILPRO` à `LocalConfigurationService` et
`LocalUpdateService`. Les fichiers V2 sont `configuration-v2.json`, les sauvegardes
dans `configuration-backups/` et les transactions sous `updates/` pour PROD ou
`updates-light/` pour PROD-LIGHT. Les anciens
`filpro.prefs.json`, sélections par profil, sauvegardes et magasins V1 ne sont jamais
réécrits par ces services. Un fichier V2 illisible ou de version inconnue est refusé
et conservé ; il ne déclenche pas une réinitialisation.

La politique commune `StoragePathPolicy` accepte les chemins absolus locaux, USB et
UNC. URI, espaces de noms de périphériques, traversées, ADS, noms réservés Windows,
OneDrive, dossiers de synchronisation connus et UNC WebDAV sont refusés. Les
adaptateurs relisent les attributs des fichiers et de leurs ancêtres pour refuser
liens/jonctions et données à la demande. L'acceptation native de répertoires
temporaires sur Linux sert aux contrôles isolés, pas à valider Windows.

## Export, import et défauts de pack

Le format portable est un JSON fermé `schemaVersion: 1`, `application: FILPRO`,
`kind: shared-configuration`, `settings`. Les seuls champs admis sont :

| Réglages | Valeurs |
| --- | --- |
| `theme` | `system`, `light`, `dark`, `contrast` |
| `motion` | `system`, `full`, `reduced` |
| `conflictPolicy` | `keepBoth`, `keepCurrent`, `useSaved` |
| `navigationCollapsed`, `autoSelectCurrentTask`, `notifyOnCompletion`, `recommendedSelection`, `migApp`, `vss`, `continueOnError`, `encrypted` | Booléens |

Aucun chemin de poste ou de stockage, nom de compte, SID, historique, modèle
personnalisé contenant des noms/chemins, sélection personnelle, clé ou secret n'est
exporté. Les modèles et sélections existants continuent d'appartenir à leurs
magasins ; importer les réglages ne les efface pas. Les clés de chiffrement ne sont
jamais un réglage partageable ; `encrypted` représente uniquement une préférence.

L'import commence par un aperçu lié aux octets du fichier, à la révision actuelle
et à un identifiant expirant après quinze minutes. Une confirmation explicite est
requise. Les champs absents conservent leur valeur actuelle. Une source modifiée ou
un état concurrent rend l'aperçu périmé. Avant remplacement, le document est validé
en mémoire, une sauvegarde réelle est écrite et vérifiée, puis le temporaire du
même dossier remplace atomiquement le fichier. Les verrous empêchent deux instances
FILPRO de publier simultanément leurs réglages.

Au premier lancement sans configuration, des défauts valides du pack initialisent
automatiquement l'état V2. Si une configuration V2 ou des préférences V1 existent,
les défauts d'un nouveau pack sont proposés avec aperçu et choix garder/appliquer.
Le choix confirmé est mémorisé dans la même écriture que les réglages. Annuler ou
échouer ne mémorise pas un succès. L'identité du choix combine le hash du manifeste
et celui des défauts : changer les défauts d'un pack de même version produit un
nouveau choix. Les 128 dernières décisions sont conservées.

**Définir comme configuration par défaut** écrit directement dans
`wad/config/deployment-defaults.json` du pack PROD courant, après aperçu,
confirmation et sauvegarde hors du pack. Une distribution en lecture seule échoue
explicitement et invite à copier le pack complet vers un emplacement modifiable,
puis à lancer cette copie. Aucune écriture de substitution dans AppData n'est
présentée comme une modification du pack.

## Intégrité de la distribution

La racine contient seulement `FILPRO.exe` et `wad/`. Le manifeste
`wad/pack-manifest.json` porte version SemVer, commit source, architecture
`win-x64`, édition et inventaire `{path,size,sha256}`. Les distributions PROD et
PROD-LIGHT contiennent l’application, son interface locale et USMT x64. Seul
PROD-LIGHT est publié. PROD embarque WebView2 fixe. Le manifeste PROD-LIGHT exclut
`wad/runtime` ; FILPRO y utilise WebView2 Evergreen x64 installé pour la machine,
sinon le moteur du pack commun du Socle Windows extrait dans `wad/runtime/webview2`.
Ce dossier n'est admis que s'il reproduit exactement, fichier par fichier, taille et
SHA-256, un index compilé dans FILPRO (`webview2-156.0.4314.42.files.json`) ; un
fichier modifié, manquant ou en trop est refusé en le nommant. La préparation d'une
mise à jour recopie et revérifie ce Socle dans la nouvelle version ; la version
retirée garde sa copie pour le retour arrière. USMT se trouve dans
`wad/USMT/` : `scanstate.exe`, `loadstate.exe`, `usmtutils.exe`, `MigApp.xml`, sans
sous-dossier `amd64` intermédiaire. La vérification annonce son avancement en
fichiers et octets. Sous Windows, le handle ouvert doit aussi désigner un fichier
à un seul lien ; un hardlink est refusé. Ce contrôle natif ne se déduit pas d'un
test Linux.

Le manifeste ne se hash pas lui-même. Une seule option mutable est admise hors de
l'inventaire binaire : `wad/config/deployment-defaults.json`, obligatoirement
validée par le codec fermé. Aucun autre fichier inconnu, script ou exécutable ne
bénéficie de cette exception. Modifier les défauts ne modifie donc pas les hashes
binaires. Une archive redistribuée doit ensuite recevoir sa nouvelle somme externe.

**Les hashes prouvent l'intégrité des octets, pas une signature de l'éditeur.** Aucun
certificat ni clé d'éditeur approuvé n'est inventé. Le contexte de confiance de la
source doit être choisi explicitement par l'opérateur.

## Sources de mises à jour

`updates.inspect` est une action explicite ; le démarrage et la lecture d'état ne
lisent aucune source. FILPRO n'interroge aucun service en ligne : la source GitHub
Releases, son jeton de session et les opérations `updates.githubSession` et
`updates.setGitHubToken` ont été retirés. Les sources sont :

- Dossier local/USB/UNC lu sans écriture : comparaison SemVer des ZIP stables de
  l’édition installée (`FILPRO-<version>-win-x64-PROD-LIGHT.zip` pour PROD-LIGHT),
  dernière version compatible et somme exacte dans `SHA256SUMS`. Une ambiguïté ou
  une erreur ne provoque aucun repli silencieux.
- ZIP manuel avec SHA-256 fourni explicitement ou le fichier voisin `SHA256SUMS`.

Le ZIP et `SHA256SUMS` d'une version publiée se téléchargent hors de FILPRO, puis
se déposent dans le dossier de diffusion. La copie du ZIP commence uniquement après
confirmation de préparation ; l'inspection lit déjà le nom, la taille et les sommes. Le ZIP est copié
localement, contrôlé, puis extrait dans un dossier neuf. Traversées,
chemins spéciaux, doublons Windows, liens, entrées étrangères au manifeste,
préversions/DEV, limites de taille et ratios de compression excessifs sont refusés.
Tous les fichiers extraits sont rehashés. Une offre expirée, une version différente
du manifeste ou une version identique/inférieure n'est pas installée.

## Transaction et retour arrière

Le service prépare un dossier adjacent à l'installation, distinct des données
AppData. Cette adjacency permet l'échange de répertoires sur un même volume. Le
remplacement exige une installation locale ou USB modifiable ; une application
lancée depuis UNC ou un support en lecture seule peut continuer à fonctionner mais
ne remplace pas sa propre installation depuis cette session.

Après confirmation d'installation et fin des travaux en cours, une copie vérifiée
du même EXE et de ses ressources démarre en mode worker hors du dossier remplacé.
Elle attend la fermeture de toutes les instances via le verrou de l'installation.
Aucun processus n'est tué. Un journal durable décrit chaque étape et ses hashes :
préparé, attente de fermeture, ancienne copie déplacée, nouvelle copie en place,
attente de santé, puis succès confirmé.

L'ancienne installation est conservée. La nouvelle instance reçoit un défi propre
à cette transaction ; la composition ne l'acquitte qu'après intégrité vérifiée et
interface réellement prête. La vérification initiale de l'hôte est partagée avec
la lecture de configuration pour éviter un second hash complet inutile ; les
nouveaux changements du pack et l'accusé de santé conservent leur vérification
complète. La disponibilité de la page est relue juste avant l'accusé. La fenêtre
de démarrage est bornée à dix minutes pour les supports lents, puis l'échec est
explicite. Un processus créé ne suffit pas à déclarer le succès.
Un échec de démarrage ou l'absence d'accusé déclenche le retour arrière. Si un
verrou ou un support empêche ce retour, les deux copies et le journal restent
présents, et l'état est un échec récupérable.

Un retour arrière confirmé ne réécrit pas les réglages AppData. La version retirée
reste dans `failed/`, la version précédente est revalidée avant rétablissement. Une
transaction interrompue apparaît dans la lecture d'état et peut reprendre son
retour arrière. Si la coupure a eu lieu pendant l'échange et que le dossier normal
manque, la copie `worker/FILPRO.exe` du dossier adjacent conservé permet la reprise
avec les arguments fermés `--filpro-update-rollback <id> <installation> <AppData>`.
Il faut conserver ce dossier et le reçu ; aucune suppression automatique des
copies n'est effectuée.

Si le retour arrière a déjà remis l'ancienne installation en place mais que la
dernière écriture du journal a été interrompue, Paramètres propose encore la
reprise. Le worker prend le verrou exclusif de l'installation puis vérifie la
version, le manifeste, tous les fichiers et les défauts attendus de cette copie.
Il finalise seulement alors le journal, sans déplacer une seconde fois le pack.
Une version différente, une modification des défauts, une entrée ambiguë, un
verrou occupé ou une annulation empêchent d'annoncer cette réussite. Le nouveau
pack retiré reste conservé. Cette réconciliation est commune au parcours normal
et au worker de secours ; aucune branche ne finalise hors du verrou.

Les services de démonstration implémentent les mêmes interfaces uniquement en
mémoire. Ils n'appellent pas ces adaptateurs fichiers et ne lancent pas le worker
natif.

## Contrôles et limites de preuve

Les suites ciblées `ConfigurationCodecTests`, `ConfigurationStoreTests`,
`StoragePathPolicyTests`, `PackIntegrityTests` et `UpdateTransactionTests` utilisent
des octets fictifs et des dossiers temporaires. Aucun faux EXE de test n'est lancé.
Elles couvrent notamment formats fermés, conservation concurrente, sauvegardes,
bootstraps, exceptions d'intégrité, ZIP hostiles et retours arrière de copies de
dossiers avec lanceur simulé. Leur résultat doit être lu dans le reçu du commit
effectivement contrôlé.

`UpdateSourcePreferenceTests` contrôle aussi la lecture d'un ancien choix GitHub
Releases et le refus de ce type à l'enregistrement et à l'inspection ;
`ExecutionBridgeTests` vérifie que le pont ne connaît plus les routes GitHub.

Ces contrôles ne qualifient pas WebView2, `File.Replace`/verrous Windows et UNC,
les permissions multi-utilisateur, le remplacement d'un EXE chargé, les erreurs de
support physiques. La garde PROD de
`tools/packaging/create_pack.py` exige une preuve Windows complète des mêmes octets
avant assemblage. Aucun rapport Linux ou succès d'une suite partielle n'y suffit.
