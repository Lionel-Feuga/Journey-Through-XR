# Obsidienne — double sabre à émetteurs orbitaux

Reconstruction 3D réalisée dans Blender à partir de l'image fournie. La référence ne définit ni les cotes ni les faces cachées : cette version interprète ces détails pour créer une géométrie exploitable en jeu. Les attaches des émetteurs sont adaptées en chariots sur rail, pour permettre le mouvement demandé.

## Fichiers

- `Obsidienne.blend` : modèle détaillé éditable, matériaux, éclairage et caméra de présentation. Lames masquées au démarrage ; réactiver leur visibilité et leur rendu pour les afficher. La collection STUDIO ne fait pas partie des exports.
- `Obsidienne.glb` : modèle détaillé avec matériaux PBR et lames visibles, pour inspection dans une visionneuse compatible.
- `Unity/Obsidienne/Models/Obsidienne.fbx` : 64 000 triangles, six maillages.
- `Unity/Obsidienne/Models/Obsidienne_Low.fbx` : 22 128 triangles, même hiérarchie et mêmes pivots. Variante décimée pour commencer l'intégration VR. Le budget final dépendra du casque et de la scène.
- `Unity/Obsidienne/Scripts` : contrôleur de rotation et adaptateur d'entrée XR.
- `Unity/Obsidienne/Editor` : outil de génération du prefab, des matériaux et du collider de prise.
- `Previews` : rendus réels du modèle dans Blender, verrouillé puis avec émetteurs décalés et lames actives.
- `Source/build_saber.py` : source reproductible du modèle. À lancer dans un processus Blender vierge ; le script remplace la scène courante et régénère les livrables dans ce dossier.
- `model_report.json`, `validation_model.json` : mesures et vérifications des deux FBX après réimport.

## Échelle et construction

Unité : mètre. Garde de 50 cm de diamètre, poignée centrale d'environ 39 cm, corps complet d'environ 67 cm, chacune des lames de 85 cm. Diamètre de la prise : environ 5 cm. Dimensions choisies faute de cotes dans la référence ; à ajuster selon l'ergonomie du projet.

La garde et la poignée sont fixes. Les émetteurs et les points de départ des lames partagent un unique objet `Rotor`, au centre de la garde. Cela garantit leur opposition à 180°. Chaque lame reste indépendante pour son allumage. Il n'y a pas de squelette : il s'agit d'objets rigides.

```text
Obsidienne
├── Guard
├── Grip
├── Axis_Normal       repère de l'axe de rotation
├── Axis_Locked       repère d'alignement sur la poignée
├── Grab_Anchor       centre de prise
└── Rotor
    ├── Emitter_A
    ├── Emitter_B
    ├── Socket_A
    │   ├── Blade_A
    │   └── Tip_A
    └── Socket_B
        ├── Blade_B
        └── Tip_B
```

Le plan de la garde est XY dans Blender, sa normale est Z. Les scripts utilisent les repères exportés pour retrouver les axes après conversion FBX. Conserver ces repères et une échelle uniforme.

## Installation Unity

1. Copier le dossier `Unity/Obsidienne` dans `Assets` du projet. Attendre la compilation.
2. Lancer **Tools > Obsidienne > Create VR prefab**. L'outil utilise le FBX allégé. **Create detailed prefab** utilise le modèle détaillé. Chaque exécution crée un prefab au nom unique ; les matériaux existants sont conservés.
3. Placer le prefab généré dans la scène. Le générateur prend en charge les matériaux **Built-in** ou **URP**. Pour HDRP, créer des matériaux appropriés et les réassigner. Les lames sont émissives ; activer un bloom dans le projet si un halo est souhaité.
4. Ajouter le composant de saisie de ton système VR, par exemple **XR Grab Interactable**. Le prefab possède déjà un Rigidbody cinématique et un CapsuleCollider sur la poignée. Affecter `Grab_Anchor` comme point de prise, puis ajuster son orientation pour ta manette.
5. Dans `ObsidienneXRInput`, choisir la main et affecter le Transform réellement suivi de cette manette à `Tracked Controller`.
6. Relier l'événement de prise à **ObsidienneXRInput.Grab()**, et l'événement de lâcher à **Release()**. Sans prise déclarée, l'entrée est volontairement inactive. Pour un sabre directement parenté à la main, appeler `Grab()` quand il est équipé et `Release()` quand il est rangé.
7. Par défaut, le bouton primaire allume/éteint la lame A et le secondaire la lame B. La gâchette d'index maintenue déverrouille le rotor. Désactiver `Use Face Buttons` pour réutiliser les méthodes `SetBladeA`, `SetBladeB`, `ToggleBladeA`, `ToggleBladeB` depuis ton système existant.

La « gâchette du haut » est interprétée comme la gâchette d'index. L'adaptateur utilise les entrées standard `UnityEngine.XR` ; un fournisseur XR actif et configuré dans le projet est nécessaire. Il ne configure pas OpenXR ni le rig à ta place. Pour une autre affectation, appeler `SetTriggerHeld(bool)` depuis ton gestionnaire d'entrées et désactiver l'adaptateur fourni. Appeler également `SetTrackingActive(bool)` et renseigner `rotationSource`.

## Comportement livré

| État / action | Résultat |
|---|---|
| Départ | Socles verrouillés dans l'axe de la poignée ; deux lames éteintes. |
| Au moins une lame active + gâchette maintenue 0,45 s | Déverrouillage des deux socles. |
| Déverrouillé | La ligne reliant les socles rejoint progressivement l'horizontale terrestre dans le plan de la garde. |
| Garde à plat | Position angulaire conservée, puisqu'aucun diamètre horizontal n'est privilégié. |
| Rotation rapide du poignet | Départ à vitesse constante dans le sens du geste. |
| Mouvement dans le même sens pendant la rotation | La rotation continue. |
| Geste rapide dans le sens inverse | Retour progressif au plus proche alignement avec la poignée, puis verrouillage. |
| Deux lames éteintes, lâcher ou perte de suivi | Retour et verrouillage. |

Le retour choisit 0° ou 180° : les deux socles peuvent échanger leur extrémité, tout en restant alignés avec la poignée. Le rotor est animé par le code, sans simulation physique de roulement. La vitesse constante est mesurée relativement à la garde.

## Réglage du geste

Le geste correspond à une rotation sur place de la main et de l'avant-bras, comme pour tourner une clé. Le déplacement de la main n'entre pas dans la détection.

- `Controller Roll Axis` : axe local du Transform suivi correspondant à la rotation de l'avant-bras. Valeur initiale `(0, 0, 1)` ; ajuster selon l'orientation du contrôleur. Il est indépendant de l'axe du rotor.
- `Invert Gesture` : inverser le sens si nécessaire après essai en main.
- `Gesture Threshold` : 220°/s pour déclencher un geste.
- `Spin Speed` : 1 080°/s, soit trois tours par seconde. Le geste donne le sens ; son amplitude ne modifie pas la vitesse.
- `Balance Speed` : 150°/s. `Relock Speed` : 540°/s.
- Un repos bref du poignet (0,10 s sous 65°/s) arme la détection ; une temporisation de 0,30 s suit le lancement. Cela limite les arrêts causés par le rebond immédiat du geste. Relâcher la gâchette entre deux déverrouillages.

Ces valeurs sont une base de réglage, pas une calibration validée sur casque.

## Limites de cette livraison

Les matériaux sont uniformes, avec relief géométrique et UV disponibles ; aucune texture de micro-rayures ou normal map n'est livrée. La version allégée est une décimation du modèle détaillé, pas une retopologie manuelle. Six maillages et plusieurs matériaux impliquent plusieurs appels de rendu ; mesurer le coût sur le matériel cible avant de figer le budget.

Le collider fourni sert à la prise de la poignée. Les collisions de garde, les dégâts des lames, les sons, les vibrations et les traînées lumineuses ne sont pas implémentés. Pour des lames très rapides, le système de combat devra échantillonner le balayage entre les poses ; un simple collider discret peut manquer un contact.

La fidélité et la lisibilité ont été contrôlées par rendu Blender. Les pivots, l'échelle, les UV et l'opposition des émetteurs ont été contrôlés sur les FBX réimportés, sur 72 positions par tour. Aucun essai avec une manette ou un casque physique n'a été réalisé.

Les trois scripts compilent sans erreur avec Roslyn et les assemblies réelles de Unity 6000.3.10f1. Le lancement de l'éditeur en batch a été bloqué par son service de licence : l'import Unity, la génération effective du prefab et l'exécution du comportement n'ont pas pu être validés dans l'éditeur. Voir `validation_unity.txt`.

## Références techniques

- [Export FBX Blender](https://docs.blender.org/api/dev/bpy.ops.export_scene.html)
- [Entrées XR Unity](https://docs.unity.com/en-us/engine/6000.0/manual/input/xr_input)
- [Conversion quaternion/axe-angle Unity](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/quaternion/toangleaxis)
