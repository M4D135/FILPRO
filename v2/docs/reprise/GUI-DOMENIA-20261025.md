# FILPRO V2 — raccordement du véritable shell DOMENIA

## Références et périmètre

Audit initial du lot sur FILPRO `4d1d61d58662499884ef1c3c80186105d9a2983e`.
La référence accessible est **M4D135/domenia-d120**, branche `v2`, examinée au commit
[`9b1c0333c34020ad88c1448874b91eff884d5406`](https://github.com/M4D135/domenia-d120/tree/9b1c0333c34020ad88c1448874b91eff884d5406/v2).
Le kit déjà importé dans FILPRO conserve sa provenance d'origine
[`77805257d2868b34aac9c97b0d523f50e8b1e3f8`](https://github.com/M4D135/domenia-d120/tree/77805257d2868b34aac9c97b0d523f50e8b1e3f8/v2),
documentée dans `KIT-PROVENANCE.md` ; ce lot n'est pas une réimportation globale.

Comparaison des identifiants de blobs Git sur ces deux références : les sept feuilles `web/css/*.css` d'origine,
les vingt modules de `web/js/kit/` autres que `util.js`, `web/js/ui.js` et `web/js/icons.js` sont identiques
dans FILPRO et dans les deux commits DOMENIA. Les adaptations de marque de `router.js` et `kit/util.js` restent
distinctes des copies intégrales. Le pont DOMENIA a évolué depuis la référence initiale ; il n'est pas remplacé
par ce lot de présentation.

Le socle natif présent dans FILPRO est C# `net10.0-windows`, `win-x64`, hôte WinForms et WebView2
`1.0.4258.31`, avec modules HTML/CSS/JS locaux et pont typé. Une ressemblance visuelle ne vaut pas
validation de ce socle sur Windows.

## Écarts corrigés dans ce lot

- Les préférences d'animations existaient mais aucune commande n'appelait `setMotion` : le menu de la barre
  supérieure expose maintenant les choix riches, réduites et suivant Windows, avec sélection radio du kit,
  état visible et nom accessible. Les réglages restent des préférences locales d'interface.
- Le thème système n'était évalué qu'au chargement : les changements de couleurs, de contraste forcé et de
  préférence de mouvement sont maintenant suivis pendant la session.
- Le repli changeait directement la grille et conservait le nom « Replier » : reprise de l'animation de colonne
  DOMENIA, nom « Déplier/Replier », état ARIA et position de l'indicateur recalculés. Les animations concurrentes
  sont arrêtées proprement ; aucune largeur n'est interpolée à chaque image.
- Les infobulles CSS du rail pouvaient être coupées par son défilement : reprise du placement flottant DOMENIA
  dans un élément extérieur au rail, pour `data-tip` et les motifs de désactivation. Les contrôles conservent
  leurs noms accessibles ; aucun catalogue métier DOMENIA n'est importé.
- Les règles CSS de faible hauteur n'étaient pas activées : reprise de `heightPlan` (940/700/560 px, largeur
  étroite sous 1280 px), repli temporaire indépendant de la préférence enregistrée, adaptation des espaces et
  lien clavier « Aller au contenu ». Le rail imposé sous 900 px suit le CSS du kit.

## Sources effectivement réutilisées

| Élément | Source DOMENIA au commit audité | Adaptation FILPRO |
|---|---|---|
| Repli animé, libellé de commande | [`web/js/app.js`](https://github.com/M4D135/domenia-d120/blob/9b1c0333c34020ad88c1448874b91eff884d5406/v2/web/js/app.js), `slideColumn`, `paintToggle` | `web/js/shell/presentation.js`, sans politique AD ni panneau d'aide |
| Dispositions et contraste Windows | [`web/js/shell/environment.js`](https://github.com/M4D135/domenia-d120/blob/9b1c0333c34020ad88c1448874b91eff884d5406/v2/web/js/shell/environment.js), `heightPlan`, `watchForcedColors` | Même seuils ; état d'affichage temporaire, aucune commande de fenêtre native ajoutée |
| Infobulles flottantes | [`web/js/shell/tips.js`](https://github.com/M4D135/domenia-d120/blob/9b1c0333c34020ad88c1448874b91eff884d5406/v2/web/js/shell/tips.js), placement, délais 350/100 ms et durée 20 s | Partie `data-tip` uniquement ; texte créé par `textContent`, fermeture au défilement, redimensionnement et changement de page |
| Choix des animations | [`web/js/kit/menu.js`](https://github.com/M4D135/domenia-d120/blob/9b1c0333c34020ad88c1448874b91eff884d5406/v2/web/js/kit/menu.js) déjà importé | `openMenu` et ses éléments radio, raccordés dans `app.js` |

Fichiers du raccordement : `web/js/app.js`, `web/js/shell/navigation.js`, nouveau
`web/js/shell/presentation.js` et nouvelle feuille additive `web/css/filpro-shell.css`.
Cette feuille est incluse dans `web/index.html`. Les sept CSS et les composants génériques importés
restent inchangés.

## Accueil et écrans existants

La feuille propre à l'accueil, `web/css/pages/accueil.css`, utilise le chargement de styles par écran du
routeur DOMENIA. Les quatre indicateurs avaient chacun une seule colonne sur douze, ce qui coupait leurs
textes. Ils occupent maintenant quatre, deux ou une tuile par ligne selon la largeur ; les sections sont
espacées et les liens principaux ouvrent les écrans existants de préparation et de profils.

Le nombre de profils suivis correspond au total affiché, avec le nombre actif en complément. Les tâches
dont l'arrêt est seulement demandé restent comptées comme actives. Ces nombres sont explicitement fictifs ;
ils ne sont pas une mesure d'activité du poste. L'accueil refuse un résultat qui n'est pas marqué comme
démonstration, tant que son fournisseur natif n'est pas disponible.

L'actualisation conserve un gestionnaire unique et bloque les requêtes concurrentes. Réessayer reconstruit
la vue après un échec, et une réponse reçue après un changement de page ne peut pas remplacer le nouvel
écran. Dans Analyser, les cartes de logiciels ajoutées après le premier rendu utilisent l'entrée autonome
`pop` du kit : elles ne restent plus invisibles en attendant un observateur `reveal` qui ne les avait pas vues.

## Validation et limites

La comparaison de contenu confirme que
les écarts de `router.js` et `kit/util.js` avec DOMENIA sont exactement les deux substitutions de marque
déjà documentées. Les contrôles de syntaxe JavaScript et `git diff --check` portent sur les sources.

Douze contrôles d'interface ont été exécutés avec succès dans **Chromium 153.0.8010.12 sur Linux**, avec les
fichiers web locaux exacts et le fournisseur fictif existant, via un harnais de messages :

- accueil lisible et compteurs cohérents ; repli animé, commande basse, infobulle et préférence mémorisée ;
- thèmes clair/sombre/contraste et changement du thème système sans rechargement ;
- réglage des animations au clavier, retour du focus, persistance et suivi de la préférence système ;
- fenêtre 1024 × 680, cartes de logiciels visibles et navigation sauvegarde/retour ;
- trois actualisations produisant trois requêtes, échec puis Réessayer, refus de données non fictives ;
- réponse tardive sans remplacement de la page active ; absence d'erreur JS/CSP, ressource manquante ou
  requête externe.

Le rapport conserve l'environnement, les observations, quatre captures et les empreintes SHA-256 de tous
les fichiers web avant et après les contrôles. La garde source exige leur correspondance avec le commit
candidat. Les huit contrôles Node de sélection sont exécutés dans cette passe de garde. Le backend C#,
les projets, tests et outils .NET, le workflow et toutes les racines V1 restent identiques au parent :
aucune nouvelle compilation ni suite C# n'est comptabilisée pour ce lot de présentation.

**Ni exécution Windows, ni qualification du rendu WebView2 par ce lot.** Les captures sont des observations
du rendu web dans Chromium Linux. L'essai Windows GitHub précédent n'a démarré aucune étape, GitHub ayant
signalé un problème de paiement du compte ou de plafond de dépenses
([exécution](https://github.com/M4D135/FILPRO/actions/runs/40230326902)).

Ces animations sont des effets de présentation. Le lot n'ajoute ni exécuteur de sauvegarde/restauration,
ni progression métier, estimation de durée ou notification de succès inventée. Le suivi d'opérations,
la démonstration métier complète, les réglages partageables du pack et leur persistance native restent
des travaux distincts. La ligne d'attente sous la barre supérieure n'est pas une mesure de progression
d'une opération ; son raccordement aux appels réels reste à traiter.

L'hôte attend encore `web/` et `runtime/webview2/` à côté de l'EXE. Le raccordement final au dossier ordinaire
`wad/`, les replis de démarrage si l'interface manque et la qualification de fenêtre/DPI sous Windows relèvent
des prochains lots natifs et de distribution. Aucun pack PROD/DEV ni Release V2 n'est qualifié par ce travail.
