# Journey Through XR

Visite immersive du 3e étage d'Epitech Nice sur Meta Quest, avec un robot guide et des mini-jeux.
Unity + Blender, équipe de 4.

## 1. Première installation (une seule fois)

1. **Accès** : accepter l'invitation GitHub au dépôt (reçue par e-mail ou sur github.com).
2. **Installer** [Git](https://git-scm.com/downloads) (Git LFS est inclus sous Windows), puis dans un terminal :
   ```bash
   git config --global user.name "Prénom Nom"
   git config --global user.email "email-du-compte-github"
   git lfs install
   ```
3. **Installer Unity Hub**, puis l'éditeur **6000.3.10f1** (Installs > Install Editor ; s'il n'est pas listé : onglet *Archive*).
   Cocher le module **Android Build Support** avec **OpenJDK** et **Android SDK & NDK Tools**.
4. **Cloner** dans un chemin court, hors OneDrive / Google Drive (ex. `C:\dev`) :
   ```bash
   cd C:\dev
   git clone https://github.com/Lionel-Feuga/Journey-Through-XR.git journey_through_xr
   ```
   Vérifier que `docs/page1.png` s'ouvre comme une image. Sinon : `git lfs pull`.
5. **Ouvrir** : Unity Hub > *Add* > *Add project from disk* > dossier `journey_through_xr/UnityProject`.
   Choisir la version 6000.3.10f1 si Unity la demande. La première ouverture est longue (5 à 15 min) : c'est normal.
6. Optionnel : [Blender 5.2.2 LTS](https://www.blender.org/download/) pour modifier `ArtSource/`.

## 2. Lancer

Ouvrir la scène `Assets/_Project/Scenes/Etage_R3` (et non `Assets/Scenes/SampleScene`).

- **Dans l'éditeur, sans casque** : Play, puis clavier et souris grâce au XR Device Simulator.
- **Sur le Quest** :
  1. Le mode développeur doit être activé sur le casque (déjà fait sur le casque de l'équipe).
  2. Brancher le casque en USB, puis accepter « Autoriser le débogage USB » dans le casque.
  3. `File > Build Profiles` > Android > Run Device = le casque > **Build And Run**.
  4. Enregistrer l'APK dans `UnityProject/Builds/` (ignoré par Git).

## 3. Où ranger quoi

```text
docs/                         sujet, plan R+3, storyboard
ArtSource/                    fichiers .blend (sources Blender)
UnityProject/Assets/_Project/
  Activities/<Activité>/      une activité = un dossier + sa scène de test + un prefab
  Core/ XR/ Guide/ UI/        systèmes communs (joueur, robot, tutoriels)
  Environment/                bâtiment (Etage_R3.fbx)
  Scenes/                     scènes principales
```

Ne pas toucher aux dossiers gérés par Unity : `Samples/`, `Settings/`, `XR/`, `XRI/`.

## 4. Travailler en équipe

1. Partir de `main` à jour : `git checkout main` puis `git pull`.
2. Créer une branche : `git checkout -b feature/ma-tache`.
3. Commiter souvent, puis `git push -u origin feature/ma-tache`.
4. Ouvrir une **pull request** vers `main`, la faire relire par un autre membre, puis **Squash and merge**.

**Règles**
- Avant chaque commit, lire `git status` : jamais de `Library/`, `Temp/`, `.utmp/`, `Builds/` ni d'APK.
- Créer, déplacer et renommer les assets **dans Unity**, jamais dans l'Explorateur (sinon les `.meta` sont perdus).
- Une seule personne à la fois sur une scène partagée (`Etage_R3`) ou sur un même `.blend`. Prévenir l'équipe avant.
- Chacun développe son activité dans **sa propre scène de test**, puis la livre sous forme de prefab.
- Les versions Unity, les packages et les réglages XR ne changent qu'après accord de l'équipe.
