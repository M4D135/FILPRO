# Traçabilité du kit DOMENIA repris dans `v2/web/`

## État courant — 28 octobre 2026

Le tableau ci-dessous conserve l'inventaire de la copie initiale. Le manifeste
`v2/web/ui-kit.lock.json` décrit maintenant les octets courants de 28 fichiers
communs et leur référence Domenia
`75778d1df7777e601fe9179d2d075e19d7d9644c`.
Les animations existantes et le préfixe de stockage FILPRO sont des adaptations
explicites ; ils ne sont pas remplacés par le contrôle de provenance.
Les règles de retour à la ligne des identifiants longs dans `components.css`
et `shell.css` sont reprises de cette référence. Aucun parcours métier ne change.
Voir `v2/docs/UI-KIT.md` pour les commandes de vérification sans réseau.

L’identité FILPRO 2.0.2 ajoute le seul glyphe `paths.filpro` à `web/js/icons.js` ;
les autres glyphes restent inchangés. Cette adaptation de marque est documentée
dans `v2/assets/branding/README.md`, hors des 28 fichiers déclarés par le kit.

## Inventaire de la copie initiale

Le design FILPRO **réutilise le kit DOMENIA à l'identique** : les fichiers ci-dessous sont des copies **octet pour octet**
des fichiers de la référence `_reference/domenia/v2/web/` (commit DOMENIA `77805257d2868b34aac9c97b0d523f50e8b1e3f8`).
Aucun n'a été réécrit ni « adapté » en bloc. Empreintes SHA-256 de contrôle.

## CSS — système de design (copié verbatim)

| Fichier | SHA-256 |
|---|---|
| `web/css/tokens.css` | `15dd927dec2acca6dc1d882dd41475180b37a278379ed7fba82fb0e0d4a4fc10` |
| `web/css/base.css` | `c4b0516545eec64978cda9c92a115bc5cf058da567b6521d0119fedabe84450f` |
| `web/css/layout.css` | `8bfdcad89c6cef3a550b280848d7e49681a5cba6c41bf0da7c65fbc320262a4b` |
| `web/css/motion.css` | `a1b1c3e74e6f45010b9f713afe1e168efc24fb7cefd29965e01773eb2fff7c51` |
| `web/css/components.css` | `94de8131c915d3c5d137bef3c13eb6b3453485708743603739833d70f93f4a82` |
| `web/css/kit.css` | `645b1a070f78fde5303305cd25ca891d0b7a81eba25e87d48df97f48e8e38347` |
| `web/css/shell.css` | `7a40a534b890b3b024e8d5e985c85159b240d4b7525651b2ebb85fcc6d25704d` |

## JS — composants génériques du kit (copiés verbatim)

| Fichier | SHA-256 |
|---|---|
| `web/js/kit/breadcrumb.js` | `febe30b2f9363f1140b81f827a268f07593a239d5068dc3ad558abdd5beb8b36` |
| `web/js/kit/commandbar.js` | `04a903fb55a871fd7c7ad18a4b0355b1da3952f702fa2185cb578e819c2cf958` |
| `web/js/kit/copy.js` | `d40f6e06d70b936c14a2309720ffeb591e5e1c3d90becd3472b96c0c354768cf` |
| `web/js/kit/dialog.js` | `195383765fa9ed5e73cece0e2ee6517200d9903d89b0feae7405f91e5e7e2b24` |
| `web/js/kit/drawer.js` | `4b093aaff0c0b18fa95139bce63c0327373c1d07b2f248a4f0de66695669257c` |
| `web/js/kit/filterbar.js` | `2abbaf13c077732e7cf8538bb0f402cec4f2e7d453691c0946ef8ac2432725df` |
| `web/js/kit/form.js` | `ea674360e8d7b7ba19e9e1065e82e2c2eb249499ab159258b81c3427f344876f` |
| `web/js/kit/freshness.js` | `b2556bdc6b839d20b7664b53c3d10a2dd1f209418e72d14cccea2af64ac15ba7` |
| `web/js/kit/gauge.js` | `0297813e83b110470309bf548f4168f3da49daa965db02b8ba1e5bcd6cd4bed0` |
| `web/js/kit/glyphs.js` | `a0d9f12798055bf12fa2330d8da0d2e0466060b3a8146842a2d306bead0d928b` |
| `web/js/kit/grid.js` | `ae83951457ee2b70c6103b2824a50c3ee23070cd5dd39a1ecff1ac192fef218c` |
| `web/js/kit/index.js` | `fc149a622eb71315ccd37dbc95b4ad64275e073a773da35bb2ef6e7a5d7f438a` |
| `web/js/kit/infinite.js` | `fc49cba716e79fb83829f7a1232e45950b31d31a14c4bac5bd7ac9655a13273d` |
| `web/js/kit/kv.js` | `15019f23ad40013e16efa799139d4025988d7bf008868e73a67cc2e9c807a635` |
| `web/js/kit/menu.js` | `221294cac95778f32dd03907494720f94ebc79a957c46bd9bd668890f65ab301` |
| `web/js/kit/split.js` | `e8f14ddc3394fe15a3c02ed0ace47f54cb8cfcaf52ff219cd011fa665d4bf4c5` |
| `web/js/kit/status.js` | `a1418a04996e23b2eb8f5f2e5988bd776ca99732b8ae05611ea9c2d9639b71e7` |
| `web/js/kit/tabs.js` | `0412fd9e12c5a5fdb0692d8d09a33d0d307d3d369ddda27200a97fc4d1b524df` |
| `web/js/kit/timeline.js` | `edabcae6705f2055d1643814f1ef1f67288a849f3d5fe68f7e462405edee2ca7` |
| `web/js/kit/tree.js` | `283acff5871d465e322092869fb6619a3cc0e22d8ffff843db7768aee020fe4d` |
| `web/js/kit/util.js` | `9ccc2d0225ef7a05c6f9f8e2b408f807f5dc52fa021378bae908fc75efcc4f48` *(1 ligne modifiée, voir ci-dessous)* |

## JS — aides partagées (copiées verbatim)

| Fichier | SHA-256 |
|---|---|
| `web/js/ui.js` | `a281f5fe669f3f9b80056f7e55e48dec5b7c8128cf0ef8893d603ad4d4ae7fd3` |
| `web/js/icons.js` | `0622bb19439b2e92320e208419e57c4188be3a400a13401d0ffc5d98bf097295` |
| `web/js/bridge.js` | `1d6c53011667959fbdd5a436cc628f85264bd36f83fd8439658b67dd7a7757b0` |
| `web/js/router.js` | `de020c05f2caffb50e34dcff2f5775382017d85aa67a5a2db3b8f7f707b5737b` *(1 ligne modifiée, voir ci-dessous)* |

## Seules modifications locales (2 lignes, hors comportement)

1. `web/js/kit/util.js` : préfixe de stockage des dispositions `domenia.kit.v1:` → `filpro.kit.v1:` (clé de localStorage
   propre à FILPRO ; aucune donnée métier ni secret n'y est stocké).
2. `web/js/router.js` : titre de document `· Domenia` → `· FILPRO`.

Les commentaires internes des fichiers copiés qui citent « Domenia 1 » sont **conservés** : ils documentent l'origine du kit
(parité DOMENIA 1) et ne sont pas des consignes.

## Fichiers écrits pour FILPRO (non copiés)

`web/index.html`, `web/js/boot.js`, `web/js/app.js`, `web/js/shell/navigation.js`, `web/js/pages/accueil.js`,
`web/js/pages/sauvegarder.js`, `web/js/pages/_todo.js`, `web/js/mock.js`,
[`web/js/mock-accueil.js` (historique)](https://github.com/M4D135/FILPRO/blob/f4e8892ccd5ed80551cbdd4b895af857b9ae7de1/v2/web/js/mock-accueil.js).
Ce dernier module, remplacé par `mock-world.js` et sans import restant, a été retiré
le 1er novembre 2026. Les modules de cet inventaire n'utilisent que les
exports publics du kit (`ui.js`, `kit/index.js`, `icons.js`, `router.js`, `bridge.js`) et n'ont pas à être relus comme une
adaptation de DOMENIA.
