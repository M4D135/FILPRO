# Kit commun Domenia — contrôle local

`v2/web/ui-kit.lock.json` fixe les octets de 28 fichiers communs de FILPRO et leur référence
Domenia `9a81bc7044364b5d9b2ca1bc36227d337e79776c` (Domenia 2.0.5, 28 octobre 2026), adoptée par FILPRO 2.0.4.
Les chemins sont relatifs à la racine du dépôt. L’adoption déclarée est partielle :
ce manifeste ne couvre ni toute l’interface ni le moteur métier.

Le kit reste une copie locale livrée avec l’application. Aucun téléchargement,
import partagé à l’exécution ou changement d’authentification n’est introduit.
Les trois adaptations sont nommées dans le manifeste. Leur empreinte locale et
celle du fichier canonique restent toutes deux obligatoires ; une adaptation
n’autorise pas une dérive libre. Depuis la 2.0.4, les animations suivent Domenia :
aucune icône ni pastille décorative ne flotte en boucle ; seuls les glyphes de marque FILPRO restent adaptés.

Les règles Domenia de retour à la ligne sont reprises dans `components.css` et
`shell.css` : un identifiant long reste entier, sélectionnable et contenu dans sa
carte ou son panneau. Les confirmations, sauvegardes et restaurations ne changent pas.

## Vérification sans réseau

Le contrôleur Python standard est embarqué dans `v2/tools/m4d-components/`,
copié à l’octet de `M4D135/m4d-admin-common` au commit
`67ae057d3f46dbb9a72562b3f444a8fd069b10c5`. Le lanceur vérifie ses deux
fichiers et leurs empreintes épinglées avant de l’exécuter en mode isolé.
Depuis la racine du projet, avec les chemins du canon adaptés :

```text
python3 -I -S v2/tools/check-ui-kit.py
python3 -I -S v2/tools/check-ui-kit.py --canonical-root <canon> --canonical-manifest <canon>/ui-kit.canonical.json
```

La première commande vérifie les fichiers locaux et marque l’amont comme non contrôlé.
La seconde vérifie en plus la copie canonique fournie. Aucun appel réseau n’est fait.
Le résultat JSON distingue conformité des octets déclarés et couverture : code
0 si cohérent, 1 si dérive, 2 si entrée invalide. Le SHA de commit déclaré ne
prouve pas à lui seul la provenance Git ; l’export du canon doit être fait depuis
ce commit exact et conservé avec sa preuve. Ces commandes ne certifient ni le
rendu visuel complet, ni Windows, ni les opérations métier.

Un changement du canon demande une comparaison explicite et une nouvelle revue
avant de mettre à jour les empreintes. Ne pas copier automatiquement les fichiers
au-dessus des adaptations du produit.

Ce contrôle s’exécute localement sans dépendre de GitHub. La consigne du
28 octobre rétablit les tests Windows GitHub après le push du code ; leur
résultat doit être vérifié sur le commit concerné, avant la recette VM finale.
