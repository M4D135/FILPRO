# Découverte des logiciels — 26 octobre 2026

## Ce que ce lot ajoute

Le fournisseur Windows découvre les dossiers précis des logiciels et les clés du
profil demandé, puis applique les choix mémorisés et le regroupement fichiers +
registre existants. Il n'exécute aucune sauvegarde, restauration ou réparation.
La mémorisation des préférences FILPRO reste une opération distincte, demandée
par l'opérateur et refusée si l'un des inventaires est incomplet.

Les sources V1 de référence sont celles du commit
`63b89fe704aed7a363cce42fcdfc5d9112f94807` : `wad/FILPRO.ps1`,
`wad/FILPRO.Profiles.ps1` et les associations déjà portées dans `SoftwareCatalog`.
Les recettes décrivent des chemins à rechercher ; elles ne prouvent ni présence,
ni capacité de sauvegarde d'un logiciel.

### Catalogue et choix conservés

- Les 29 recettes AppData reprennent les chemins, noms et recommandations V1.
  Les 12 recettes explicites de registre sont complétées par 12 clés des
  associations V1 existantes. Ces ajouts n'ont pas de recommandation propre.
- Les racines génériques d'éditeurs, les dossiers de bruit et les parents de
  recettes précises ne sont pas ajoutés en bloc. Par exemple, la recette Chrome
  cible `Local\Google\Chrome\User Data`, sans proposer tout `Local\Google`.
- Les chemins de la dernière sélection et des modèles communs sont validés par
  le codec existant puis relus. Un élément non constaté présent n'est pas inventé.
  Le type `Modele` des ajouts explicites est conservé.
- Sans choix mémorisé, les recommandations existantes s'appliquent au logiciel
  entier. Avec un document mémorisé, y compris vide, les choix précis sont
  conservés ; aucun logiciel n'est réactivé par sa seule recommandation.
- Les domaines V1 comme « Bureautique » restent des métadonnées séparées du type
  de données V2. Aucun schéma de sélection n'est étendu dans ce lot.

Les trois marqueurs V1 `NotTested` (SAP GUI, Citrix, AutoComplete Outlook) sont
conservés dans `RequiresQualification`. Leur recommandation propre reste à
`false`. Le regroupement Office hérité peut toutefois sélectionner AutoComplete
avec un autre membre Office recommandé : `Recommend=false` n'est pas un veto
sur le groupe. Ce comportement est documenté et contrôlé ; il ne qualifie pas
la recette. Aucune recette, marquée ou non, n'est déclarée validée sous Windows
par ce portage.

## Lecture de fichiers

`WindowsSoftwareFileReader` recherche le catalogue et les chemins supplémentaires,
puis seulement le premier niveau de `AppData\Local` et `AppData\Roaming`.
Chaque chemin est relatif à sa portée validée. Les attributs des composants sont
contrôlés du volume ou partage vers le dossier : les liens et jonctions observés
sont refusés. Un accès refusé ou une erreur de lecture rend l'inventaire partiel,
au lieu d'être assimilé à un dossier absent par `Directory.Exists`.

L'énumération générique est bornée par portée à 1 000 entrées et quatre secondes
entre appels système. Une limite atteinte, une entrée devenue absente ou une
énumération interrompue ne valent pas réussite complète. Aucune taille n'est
mesurée ni déduite de la présence d'un dossier.

Ce lecteur utilise des chemins et des vérifications d'attributs. Il ne crée pas
de snapshot et ne verrouille pas l'arborescence contre une substitution concurrente
entre deux appels. La complétude décrit le périmètre de découverte annoncé à cet
instant, pas la cohérence d'une future copie. Le futur moteur devra revalider les
sources avec ses propres protections.

## Registre déjà chargé et ressources

`WindowsLoadedRegistryReader` ouvre uniquement `HKEY_USERS\<SID>` pour un SID de
profil validé, via `RegOpenKeyExW` et `KEY_READ`. Il cherche les recettes précises,
les chemins supplémentaires validés et le premier niveau générique de `Software`.
La façade n'expose aucune commande d'écriture, de chargement ou de déchargement.
Les handles natifs restent privés et possédés par des `SafeRegistryHandle`.

Les codes Win32 d'absence sont distingués des autres erreurs. Une ruche non
chargée produit explicitement un résultat incomplet : aucun `NTUSER.DAT` n'est
ouvert ou monté en remplacement. Une clé refusée, un lien, une erreur d'énumération
ou une limite atteinte rendent aussi le résultat partiel. Les composants sont
ouverts avec `REG_OPTION_OPEN_LINK` et un lien observé n'est pas suivi.

Les sous-clés et la racine ouvertes par FILPRO sont disposées après succès,
erreur ou annulation. La ruche déjà chargée appartient à Windows : ce lecteur
ferme ses handles, il ne décharge pas la ruche du compte. Le cycle de montage,
lecture et déchargement d'une copie de ruche hors ligne reste à implémenter et
à qualifier séparément.

La revue a aussi corrigé une constante du lecteur de profils existant : `HKU`
utilisait la valeur de `HKEY_CURRENT_CONFIG`. Elle désigne maintenant
`HKEY_USERS` (`0x80000003`), afin que le constat de profil chargé interroge la bonne
racine. Le contrôle Windows dédié doit confirmer l'ouverture réelle du propre SID.

L'annulation est coopérative entre appels de lecture et propagée après nettoyage.
Elle ne force pas l'interruption d'un appel système bloqué ; aucune durée maximale
absolue n'est promise.

Références API : [RegOpenKeyExW](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regopenkeyexw)
et [clés prédéfinies Windows](https://learn.microsoft.com/en-us/windows/win32/sysinfo/predefined-keys).

## Vérification et limites de livraison

Contrôles de développement du 26 octobre : les six projets compilent avec le SDK
.NET 10.0.401, sans avertissement ni erreur, depuis Linux en ciblant Windows x64.
Les 24 nouveaux tests portables (huit par suite ci-dessous) réussissent, sans
test ignoré. Ces résultats ne prouvent ni un démarrage WebView2, ni une exécution
des API natives. La garde finale rattache ses propres résultats au commit exact
et à l'inventaire des sources ; aucun reçu historique n'est réutilisé comme succès
de ce lot.

Les suites ciblées sont `SoftwareDiscoveryCatalogTests`,
`SoftwareFileDiscoveryTests` et `LoadedRegistryDiscoveryTests`. Les deux lecteurs
utilisent des façades injectées pour contrôler notamment les erreurs, les bornes,
les chemins invalides, l'annulation et la fermeture des ressources. Ces doubles
de test ne valident pas les API Windows.

`LoadedRegistryWindowsTests` est une suite séparée, ajoutée au script Windows.
Elle doit ouvrir réellement la ruche du compte exécutant, sans écriture ni
montage. Une absence inattendue est un échec ; cette suite n'est pas sélectionnée
dans une preuve Linux. Voir [VALIDATION.md](../VALIDATION.md) pour les preuves
exigées avant toute déclaration de réussite Windows.

Ce lot ne fournit ni exécutants métier, ni lecteur de manifeste de restauration,
ni configuration de pack distribuable, ni mise à jour transactionnelle, ni
démonstration complète d'exécution. Aucun pack PROD/DEV V2, tag ou Release n'est
produit ; les ressources et packs V1 restent inchangés.
