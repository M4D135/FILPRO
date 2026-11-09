# Identité FILPRO

Identité du 28 octobre 2026, dans la famille visuelle GLPI validée par David : cartes
de profil blanches, léger relief, accent turquoise, fond carré **opaque bleu pastel**
selon la dernière demande de David. `filpro-geometry-study.svg` est l’étude originale dessinée par coordonnées.
Le maître couleur final `final-brand-master.png` a ensuite été affiné avec l’outil
intégré **image_gen**, à partir de cette étude et des références GLPI/DOMENIA.
Un second appel a demandé de changer uniquement le fond, avec le premier maître
comme seule image de référence. Le prompt exact est `imagegen-pastel-prompt.txt` ;
`generation.json` relie son entrée et sa sortie et conserve intégralement la première
génération, son prompt `imagegen-prompt.txt` et ses trois références. L’ancien maître
est conservé dans Git au commit `49c984ebc20cb667ccec96d518b965eca34637b4`.
Ce bitmap généré n’est présenté ni comme le rendu exact du SVG, ni comme une preuve
d’identité de tous les pixels du motif entre les deux générations.
Les couleurs du manifeste sont des références de famille,
pas une affirmation de couleurs plates exactes dans l’image générée.

Le PNG pastel fourni est conservé à l’octet (1254×1254). Les PNG de 16, 20, 24, 32, 40, 48, 64,
128 et 256 px forment `src/Filpro.App/Assets/filpro.ico` ; un PNG 1024 px complète
la diffusion. Tous dérivent du bitmap final, avec un filtre Lanczos, sans coins
arrondis ajoutés. Les PNG des logotypes juxtaposent l’icône et les chemins géométriques
`filpro-wordmark*.svg` ; aucune police n’est requise, aucun texte n’entre dans l’icône.
L’ICO est incorporé à la ressource Windows de l’EXE et à une ressource
managée dont chaque fenêtre possède sa propre copie libérée à la fermeture.

`filpro-monochrome.svg` et `filpro-icon-body.txt` définissent le glyphe 24 px utilisé
par les seuls badges de marque du login et du shell. Il emploie `currentColor`, sans
ID ni filtre, et conserve leurs dimensions, couleurs, contraste et animations.
L’action « Sauvegarder » garde son disque. L’ajout de `paths.filpro` est une adaptation
produit explicite du fichier DOMENIA `web/js/icons.js` ; les autres glyphes sont conservés.

Pour régénérer avec une installation **existante** de Sharp (aucun téléchargement) :

```sh
node v2/tools/branding/render.mjs
node v2/tools/branding/render.mjs --check
```

Si nécessaire, `FILPRO_SHARP_MODULE` désigne le module Sharp déjà installé. Les
versions de rendu sont enregistrées dans le manifeste ; cette commande ne relance
jamais image_gen. Le maître et le prompt sont vérifiés avant dérivation. Le contrôle recalcule les
PNG/ICO, exige leur opacité et compare tous les octets ; il vérifie aussi le glyphe
inline. Un nouveau binaire nécessite ses propres empreintes et observations Windows :
ces assets ne modifient ni les archives ni les preuves de FILPRO 2.0.1.
