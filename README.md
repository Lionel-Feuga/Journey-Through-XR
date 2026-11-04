# Journey Through XR

Visite immersive et interactive du 3e étage d'Epitech Nice sur Meta Quest, accompagnée par un robot guide et ponctuée de mini-jeux.

Projet d'école réalisé en équipe de 4 avec Unity et Blender.

## Versions de l'équipe

Tous les membres utilisent **exactement** ces versions :

| Outil | Version |
|---|---|
| Unity | 6.3 LTS (`6000.3.10f1`) avec Android Build Support (OpenJDK, Android SDK & NDK) |
| Blender | 5.2.2 LTS |
| Git LFS | 3.x |
| Rendu / XR | URP, OpenXR, XR Interaction Toolkit |
| Casques | Meta Quest 2, puis Meta Quest 3 |

## Démarrage rapide

```bash
git lfs install
git clone https://github.com/Lionel-Feuga/Journey-Through-XR.git
```

Ouvrir ensuite le dossier `UnityProject/` depuis Unity Hub, avec la version indiquée ci-dessus.

## Organisation du dépôt

```text
docs/          Sujet, plan du R+3 et storyboard
ArtSource/     Sources Blender (.blend), hors du projet Unity
UnityProject/  Projet Unity
```

## Règles de contribution

- `main` contient uniquement du travail intégré et testé.
- Une branche courte par tâche (`feature/...`, `fix/...`, `chore/...`), puis une pull request relue par un autre membre.
- Une seule personne à la fois sur un même fichier `.blend` ou une même scène partagée.
