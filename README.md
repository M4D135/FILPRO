# FILPRO 2

FILPRO accompagne la sauvegarde, la restauration et la réparation des profils
Windows : fichiers personnels, AppData et registre HKCU. Il ne sauvegarde ni ne
restaure la machine (HKLM, image système ou MigApp général).

## Télécharger

La dernière version est sur la page
[Releases](https://github.com/M4D135/FILPRO/releases/latest) :

| Archive | Contenu |
| --- | --- |
| `FILPRO-<version>-win-x64-PROD-LIGHT.zip` (à partir de 2.0.12) | Application et outils de migration, sans moteur WebView2. FILPRO utilise le runtime Microsoft Edge WebView2 Evergreen x64 installé pour la machine, sinon le pack WebView2 du [Socle Windows](https://github.com/M4D135/socle-windows/releases/tag/webview2-156.0.4314.42) extrait dans le dossier `wad`. |
| `FILPRO-<version>-win-x64-PROD.zip` (jusqu’à 2.0.11) | Ancienne édition avec WebView2 embarqué, plus publiée. |
| `SHA256SUMS` | Empreintes SHA-256 des archives disponibles. |

Pour télécharger les sources d’une version, ouvrir son tag dans la release ou choisir **Source code (zip)** sur GitHub.

## Utiliser FILPRO

FILPRO est portable : aucun installateur.

Extraire toute l’archive choisie dans un dossier vide, garder **`FILPRO.exe` et le dossier `wad`
ensemble**, puis ouvrir `FILPRO.exe` sur le poste Windows x64 concerné. La
connexion utilise la session Windows courante et la validation UAC. La
démonstration s’ouvre depuis les Paramètres ; elle reste isolée des données
réelles.

Le périmètre de qualification retenu est Windows 11 x64. Windows 10 reste
non qualifié.

## Sources et documentation

- [`v2/`](v2/) : sources C# .NET, interface web locale, tests et outils de
  fabrication.
- [Architecture](v2/ARCHITECTURE.md), [état de livraison](v2/REPRISE.md) et
  [validation](v2/docs/VALIDATION.md), avec les preuves et limites de chaque
  version.
- [Guide d’utilisation](v2/guide/UTILISER-FILPRO.txt) et
  [diffusion/reconstruction](v2/guide/DIFFUSER-ET-RECONSTRUIRE.txt).

## FILPRO 1 (historique)

FILPRO 1.0 n’est plus développé. Le dépôt public a été recréé le 9 novembre 2026
à partir de FILPRO 2 : les anciennes branches, les anciens tags et leur historique
n’y sont plus disponibles. Les copies locales des sources, packs, guides et
passations V1 sont conservées séparément.
