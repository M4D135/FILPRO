# Assemblage V2 : EXE et dossier `wad`

`Build-Input.ps1` publie l'application Windows x64 autonome dans un nouveau dossier
hors dépôt, puis appelle `prepare_input.py`. Ce dernier copie intégralement les
distributions fournies et vérifie leur structure et leurs empreintes. USMT est requis
dans les deux éditions. Seul PROD-LIGHT est publié : il n'embarque aucun moteur
et utilise le runtime Microsoft Edge WebView2 Evergreen x64 installé pour la
machine, sinon le pack WebView2 commun du Socle Windows extrait dans `wad`
([webview2-156.0.4314.42](https://github.com/M4D135/socle-windows/releases/tag/webview2-156.0.4314.42)), reconnu par
`WebViewSocle` contre son index compilé. PROD, qui embarque WebView2 fixe, n'est
plus publié. Les sources de la
version sont disponibles au tag Git et doivent correspondre au commit demandé.
Une source modifiée exige une nouvelle révision. Les paramètres et commandes sont
détaillés dans `v2/guide/DIFFUSER-ET-RECONSTRUIRE.txt`.

Le publish embarque `wad/support/DEBLOQUER_TOUT.cmd`, son script PowerShell,
les guides et les notices. Le déblocage est explicite, refuse les liens et retire
uniquement le marquage de provenance Windows. Ce support est exigé dans les deux éditions.

`create_pack.py` utilise uniquement la bibliothèque standard Python. Il lit un
dossier d'assemblage neuf contenant exactement `FILPRO.exe` et `wad/`, puis crée
un **nouveau** dossier de résultat hors du dépôt. Il ne publie rien, n'écrase aucun
pack existant et refuse les entrées `packs/` et `wad/` de la V1.

Le dossier doit déjà contenir l'application Windows x64 autonome, l'interface dans
`wad/web/` et USMT x64 dans
`wad/USMT/` (`scanstate.exe`, `loadstate.exe`, `usmtutils.exe`, `MigApp.xml` ; le
contenu du répertoire amd64 est placé directement dans `wad/USMT`). Tous les EXE
exigés sont contrôlés comme PE x64. La préparation des dépendances est distincte
de cet outil. Pour PROD, fournir `-WebView2` à `Build-Input.ps1` pour copier le moteur
dans `wad/runtime/webview2/`. Pour PROD-LIGHT, utiliser `-Flavor PROD-LIGHT` et omettre
`-WebView2`. L’entrée légère exclut `wad/runtime/`. La version demandée doit correspondre à celle compilée dans
`ProductInfo.Version` ; l'hôte refuse une divergence au démarrage.

```powershell
python v2/tools/packaging/create_pack.py --input C:\Temp\FILPRO-light-input `
  --output C:\Temp\FILPRO-light-resultat --version 2.0.15 --flavor PROD-LIGHT `
  --source-commit <SHA_GIT_EXACT> --windows-qualification C:\Preuves\light\windows-qualification.json
```

Le résultat contient un dossier extractible `FILPRO-<version>-win-x64-<édition>/`, un ZIP
normal, `SHA256SUMS` et un petit reçu d'assemblage. Dans le dossier extractible et
dans le ZIP, la racine reste **`FILPRO.exe` + `wad/`**. Aucun format WAD custom.
Le nom distribué est `FILPRO-<version>-win-x64-PROD-LIGHT.zip`. Un ZIP
`FILPRO-<version>-win-x64-PROD.zip` n'est plus publié ; l’assistant choisit toujours
l’édition installée.

## Manifeste et configuration distribuable

`wad/pack-manifest.json` inventorie chaque fichier avec taille et SHA-256. Il porte
l'application, la version SemVer, `win-x64`, l’édition et le commit source.
Les chemins ambigus Windows, doublons insensibles à la casse, liens, traversées,
fichiers à la demande, tailles excessives et fichiers racine supplémentaires sont
refusés. La copie finale est comparée aux empreintes avant création du ZIP.

Le manifeste ne s'inventorie pas lui-même. L'unique autre exception est
`wad/config/deployment-defaults.json`, optionnelle et validée par le schéma fermé de
configuration FILPRO : thème, mouvement et préférences booléennes, aucune donnée
personnelle, aucun chemin, script, compte ou secret. Le bouton de définition des
défauts peut modifier cette option sans recalculer les hashes des binaires. Les
sauvegardes préalables restent dans AppData. Après ce changement, le ZIP et sa somme
externe doivent être régénérés ; l'outil attend un assemblage neuf sans manifeste.

SHA-256 vérifie des octets ; **aucune signature ni authenticité cryptographique de
l'éditeur n'est établie**. Les sources GitHub/partage doivent être de confiance.

## Garde PROD supplémentaire, sans modification de la garde source

Les deux éditions exigent `--windows-qualification <recu.json>`. Pour PROD-LIGHT,
le reçu doit aussi porter `"flavor": "PROD-LIGHT"` et couvrir les octets de l’édition
légère démarrée avec Evergreen ; le démarrage sur le moteur du Socle est consigné à
part dans `docs/VALIDATION.md`. Les rapports hors
Windows et la CI ciblée dont `fullWindowsQualification` vaut `false` sont refusés.
Cette garde d'assemblage ne remplace pas `garde_poussee.py`, restée inchangée.

Le reçu doit provenir des scénarios **réellement exécutés** sur Windows, être un
objet fermé avec les champs suivants :

| Champ | Valeur exigée |
| --- | --- |
| `schemaVersion`, `application`, `platform` | `1`, `FILPRO`, `windows-x64` |
| `version`, `sourceCommit` | Version et commit exacts demandés |
| `status`, `fullWindowsQualification` | `passed`, `true` |
| `files` | Liste triée par chemin des `{path,size,sha256}` exactement assemblés, hors manifeste et option de défauts |
| `checks` | Tous les contrôles ci-dessous à `true` |
| `evidence` | Liste non vide de `{path,sha256}`, chemins relatifs au dossier du reçu, fichiers présents et hashes conformes |

Les contrôles requis sont `startup`, `backupRestoreMultipleProfiles`,
`filesRegistryIntegrityConflicts`, `hiveUnloadGuaranteed`,
`cancelAndFailurePreserveData`, `demoIsolation`, `updateRollbackRecovery` et
`encryption`. Le chiffrement étant proposé par le produit, il reste un contrôle
requis ; le désactiver dans une distribution demanderait une revue de cette garde.

Ce format décrit un contrat de preuve, **pas un reçu vierge à compléter en succès**.
Aucune qualification Windows complète ni aucun pack PROD prêt n'est créé par ce
lot de sources. La vérification du démarrage WebView2, des accès Windows, des
verrous multi-instance et des échanges d'EXE exige encore l'exécution critique
sur une installation jetable Windows.
