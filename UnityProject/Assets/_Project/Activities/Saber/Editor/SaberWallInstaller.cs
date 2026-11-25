using System.Collections.Generic;
using JourneyThroughXR.Activities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace JourneyThroughXR.Activities.SaberTools
{
    /// <summary>
    /// Tools > Sabres > Installer le mur des sabres (Local 301)
    ///
    /// Dans la scène Etage_R3 ouverte :
    /// - ajoute un mur dans le Local 301, partant du gros poteau vers la porte double ;
    /// - accroche les cinq sabres sur sa face côté porte (face sud du plan) ;
    /// - ajoute un Global Volume avec le Bloom des sabres et active le post-traitement de la caméra ;
    /// - ajoute une Reflection Probe calculée pour la salle (reflets des métaux) ;
    /// - ajoute la gestion de la pénombre (SaberAmbiance) sur « Sabres_301 » ;
    /// - baisse un peu la lumière générale de l'étage (soleil et ambiante à 0,8).
    ///
    /// Relancer le menu remplace proprement ce qu'il avait créé (objets « Sabres_301 », « Reflet_Local_301 »).
    /// Les prefabs des sabres sont seulement instanciés : leurs réglages ne sont pas touchés.
    /// </summary>
    public static class SaberWallInstaller
    {
        private const string SceneName = "Etage_R3";
        private const string RootName = "Sabres_301";
        private const string ProbeName = "Reflet_Local_301";
        private const string VolumeName = "Global Volume";
        private const string SaberRoot = "Assets/_Project/Activities/Saber/";
        private const string SceneDataFolder = "Assets/_Project/Scenes/Etage_R3";
        private const string WallMaterialFolder = "Assets/_Project/Activities/Saber/Mur/Materials";

        // Sabres dans l'ordre d'accrochage, de gauche à droite face au mur.
        private static readonly string[] SaberPrefabs =
        {
            "Chrome/Prefabs/Sabre_Chrome_Low.prefab",
            "Aureole/Prefabs/Aureole_Light.prefab",
            "OrChrome/Prefabs/Sabre_Or_Chrome.prefab",
            "SithIndustriel/Prefabs/Sabre_Sith_Industriel.prefab",
            "VadorESB/Prefabs/Sabre_Vador_ESB.prefab",
        };

        // Repères de la maquette (coordonnées du FBX Etage_R3, en mètres : x, z).
        // Ils servent à convertir les positions du plan en positions de la scène, quelle que soit la façon dont la maquette est placée.
        private static readonly (string name, Vector2 fbx)[] Anchors =
        {
            ("Local_301", new Vector2(5.7382f, -14.7557f)),
            ("Porte_Salle_301_A", new Vector2(11.0417f, -14.7255f)),
            ("Porte_Salle_301_B", new Vector2(11.0417f, -13.2915f)),
        };

        // Mur (coordonnées du plan) : du gros poteau (x 6,8 ; z -12,2) vers la porte double.
        private const float WallStartX = 6.95f;   // un peu dans le poteau pour ne pas laisser de jour
        private const float WallEndX = 9.30f;
        private const float WallZ = -12.2f;
        private const float WallThickness = 0.10f;
        private const float WallHeight = 3.0f;    // jusqu'au plafond

        // Accrochage
        private const float SaberSpacing = 0.38f;
        private const float HiltBottomHeight = 1.0f;  // bas des poignées, au-dessus du sol
        private const float HiltDistanceFromWall = 0.09f;

        // Salle 301 (coordonnées du plan) pour la Reflection Probe.
        private static readonly Vector2 RoomMin = new Vector2(0.4f, -18.8f);
        private static readonly Vector2 RoomMax = new Vector2(10.85f, -8.5f);

        private static readonly Vector3 BloomValues = new Vector3(0.5f, 1.6f, 0.39f); // seuil, intensité, diffusion

        [MenuItem("Tools/Sabres/Installer le mur des sabres (Local 301)")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                EditorUtility.DisplayDialog("Mur des sabres",
                    $"Ouvre d'abord la scène {SceneName} (Assets/_Project/Scenes/{SceneName}).", "OK");
                return;
            }

            if (!TryBuildPlanToScene(scene, out var map, out string missing))
            {
                EditorUtility.DisplayDialog("Mur des sabres",
                    $"Repère introuvable dans la scène : {missing}.\nLa maquette Etage_R3 doit être présente avec ses objets d'origine.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Installer le mur des sabres");
            int undoGroup = Undo.GetCurrentGroup();

            RemoveExisting(scene, RootName);
            RemoveExisting(scene, ProbeName);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Mur des sabres");
            SceneManager.MoveGameObjectToScene(root, scene);

            var sabers = BuildWallAndSabers(scene, root.transform, map);
            // Pénombre à l'approche ou à la saisie d'un sabre (voir SaberAmbiance).
            Undo.AddComponent<SaberAmbiance>(root);
            SetupBloom(scene);
            SetupFloorLighting(scene);
            BakeRoomProbe(scene, map, sabers);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Sabres] Mur installé dans le Local 301 avec {sabers.Count} sabres. Enregistre la scène (Ctrl+S).");
        }

        // ---------------------------------------------------------------- Mur et sabres

        private static List<GameObject> BuildWallAndSabers(Scene scene, Transform root, PlanToScene map)
        {
            float floorY = map.FloorY;

            // Axes du mur dans la scène.
            Vector3 along = map.Direction(new Vector2(1f, 0f));      // le long du mur (x du plan)
            Vector3 normal = map.Direction(new Vector2(0f, -1f));    // face côté porte (z décroissant du plan)
            Quaternion facing = Quaternion.LookRotation(normal, Vector3.up);

            Vector3 wallMid = map.Point(new Vector2((WallStartX + WallEndX) * 0.5f, WallZ), floorY);
            float wallLength = WallEndX - WallStartX;

            // Mur, même matériau que les cloisons de la maquette.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Mur_Sabres";
            wall.transform.SetParent(root, false);
            wall.transform.SetPositionAndRotation(wallMid + Vector3.up * (WallHeight * 0.5f), facing);
            wall.transform.localScale = new Vector3(wallLength, WallHeight, WallThickness);
            wall.isStatic = true;
            var partitionMaterial = FindMaterialOf(scene, "Cloisons");
            if (partitionMaterial != null) wall.GetComponent<MeshRenderer>().sharedMaterial = partitionMaterial;

            // Planche de présentation et supports.
            var board = LoadOrCreateMaterial("Mur_Planche", new Color(0.12f, 0.12f, 0.13f), 0.2f, 0.35f);
            var metal = LoadOrCreateMaterial("Mur_Support", new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.6f);

            Vector3 wallFace = wallMid + normal * (WallThickness * 0.5f);
            float boardWidth = SaberSpacing * (SaberPrefabs.Length - 1) + 0.4f;

            var plank = Primitive(PrimitiveType.Cube, "Planche", root, board);
            Vector3 plankCenter = wallFace + normal * 0.0125f;
            plankCenter.y = floorY + HiltBottomHeight + 0.15f;
            plank.transform.SetPositionAndRotation(plankCenter, facing);
            plank.transform.localScale = new Vector3(boardWidth, 0.6f, 0.025f);
            plank.isStatic = true;

            var sabers = new List<GameObject>();
            for (int i = 0; i < SaberPrefabs.Length; i++)
            {
                string path = SaberRoot + SaberPrefabs[i];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"[Sabres] Prefab introuvable : {path}");
                    continue;
                }

                float offset = (i - (SaberPrefabs.Length - 1) * 0.5f) * SaberSpacing;
                Vector3 slot = wallFace + normal * HiltDistanceFromWall - along * offset;
                slot.y = floorY + HiltBottomHeight;

                // Support : coupelle sous la poignée + bras jusqu'à la planche.
                var cup = Primitive(PrimitiveType.Cylinder, $"Support_{i + 1}", root, metal);
                cup.transform.SetPositionAndRotation(slot - Vector3.up * 0.008f, facing);
                cup.transform.localScale = new Vector3(0.07f, 0.008f, 0.07f);
                cup.isStatic = true;

                var arm = Primitive(PrimitiveType.Cube, $"Bras_{i + 1}", root, metal);
                Vector3 armCenter = wallFace + normal * (HiltDistanceFromWall * 0.5f) - along * offset;
                armCenter.y = slot.y - 0.008f;
                arm.transform.SetPositionAndRotation(armCenter, facing);
                arm.transform.localScale = new Vector3(0.02f, 0.012f, HiltDistanceFromWall);
                arm.isStatic = true;

                // Sabre, posé debout, émetteur vers le haut.
                var saber = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(saber, "Sabre");
                saber.transform.SetParent(root, true);
                saber.transform.rotation = facing;
                float bottomLocal = HiltBottomLocal(saber);
                saber.transform.position = slot - saber.transform.up * bottomLocal * saber.transform.lossyScale.y;

                if (saber.GetComponent<SaberRackSlot>() == null) Undo.AddComponent<SaberRackSlot>(saber);
                sabers.Add(saber);
            }

            return sabers;
        }

        /// <summary>Bas de la poignée en coordonnées locales (axe Y du sabre), d'après sa capsule de collision.</summary>
        private static float HiltBottomLocal(GameObject saber)
        {
            var capsule = saber.GetComponent<CapsuleCollider>();
            if (capsule != null && capsule.direction == 1)
                return capsule.center.y - capsule.height * 0.5f;

            var box = saber.GetComponent<BoxCollider>();
            if (box != null) return box.center.y - box.size.y * 0.5f;

            Debug.LogWarning($"[Sabres] {saber.name} : pas de capsule verticale, le bas de la poignée est estimé.");
            return -0.15f;
        }

        // ---------------------------------------------------------------- Bloom

        private static void SetupBloom(Scene scene)
        {
            Volume volume = null;
            foreach (var v in FindAll<Volume>(scene))
            {
                if (v.isGlobal) { volume = v; break; }
            }

            if (volume == null)
            {
                var go = new GameObject(VolumeName);
                Undo.RegisterCreatedObjectUndo(go, "Global Volume");
                SceneManager.MoveGameObjectToScene(go, scene);
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
            }

            Undo.RecordObject(volume, "Bloom");
            if (volume.sharedProfile == null)
            {
                EnsureFolder(SceneDataFolder);
                string profilePath = SceneDataFolder + "/Etage_R3 Volume Profile.asset";
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, profilePath);
                }
                volume.sharedProfile = profile;
            }

            var p = volume.sharedProfile;
            if (!p.TryGet(out Bloom bloom))
            {
                bloom = p.Add<Bloom>(false);
                AssetDatabase.AddObjectToAsset(bloom, p);
            }
            bloom.active = true;
            bloom.threshold.Override(BloomValues.x);
            bloom.intensity.Override(BloomValues.y);
            bloom.scatter.Override(BloomValues.z);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();

            // Le Bloom n'apparaît que si la caméra du casque a le post-traitement activé.
            foreach (var cam in FindAll<Camera>(scene))
            {
                if (!cam.CompareTag("MainCamera")) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "Post-traitement");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
            }
        }

        // ---------------------------------------------------------------- Lumière de l'étage

        // Lumière générale de l'étage, un peu baissée (d'origine : 1 et 1).
        private const float FloorSunIntensity = 0.8f;
        private const float FloorAmbientIntensity = 0.8f;

        private static void SetupFloorLighting(Scene scene)
        {
            foreach (var light in FindAll<Light>(scene))
            {
                if (light.type != LightType.Directional) continue;
                Undo.RecordObject(light, "Lumière de l'étage");
                light.intensity = FloorSunIntensity;
            }
            // RenderSettings appartient à la scène active (Etage_R3) : enregistré avec elle.
            RenderSettings.ambientIntensity = FloorAmbientIntensity;
        }

        // ---------------------------------------------------------------- Reflets

        private static void BakeRoomProbe(Scene scene, PlanToScene map, List<GameObject> sabers)
        {
            var center2 = (RoomMin + RoomMax) * 0.5f;
            Vector3 center = map.Point(center2, map.FloorY) + Vector3.up * 1.5f;
            Vector3 sx = map.Vector(new Vector2(RoomMax.x - RoomMin.x, 0f));
            Vector3 sz = map.Vector(new Vector2(0f, RoomMax.y - RoomMin.y));
            var size = new Vector3(Mathf.Abs(sx.x) + Mathf.Abs(sz.x), 3f, Mathf.Abs(sx.z) + Mathf.Abs(sz.z));

            var go = new GameObject(ProbeName);
            Undo.RegisterCreatedObjectUndo(go, "Reflection Probe");
            SceneManager.MoveGameObjectToScene(go, scene);
            // Point de capture à hauteur des yeux, près du mur des sabres.
            Vector3 eye = map.Point(new Vector2((WallStartX + WallEndX) * 0.5f, WallZ - 1.5f), map.FloorY) + Vector3.up * 1.5f;
            go.transform.position = eye;

            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.renderDynamicObjects = true;
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = true;
            probe.center = center - eye;
            probe.size = size;

            // Les sabres ne doivent pas se refléter eux-mêmes.
            foreach (var s in sabers) s.SetActive(false);
            try
            {
                EnsureFolder(SceneDataFolder);
                string path = SceneDataFolder + "/Reflet_Local_301.exr";
                if (Lightmapping.BakeReflectionProbe(probe, path))
                {
                    AssetDatabase.ImportAsset(path);
                    probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                }
                else
                {
                    Debug.LogWarning("[Sabres] Le calcul de la Reflection Probe a échoué : relance le menu, ou clique sur Bake dans son Inspector.");
                }
            }
            finally
            {
                foreach (var s in sabers) s.SetActive(true);
            }
        }

        // ---------------------------------------------------------------- Conversion plan → scène

        private sealed class PlanToScene
        {
            // q = M p + t, sur le plan horizontal (x, z).
            private readonly float m00, m01, m10, m11;
            private readonly Vector2 t;
            public float FloorY { get; }

            public PlanToScene(Vector2[] p, Vector2[] q, float floorY)
            {
                Vector2 p1 = p[1] - p[0], p2 = p[2] - p[0];
                Vector2 q1 = q[1] - q[0], q2 = q[2] - q[0];
                float det = p1.x * p2.y - p2.x * p1.y;
                // M = Q · P⁻¹
                float i00 = p2.y / det, i01 = -p2.x / det, i10 = -p1.y / det, i11 = p1.x / det;
                m00 = q1.x * i00 + q2.x * i10; m01 = q1.x * i01 + q2.x * i11;
                m10 = q1.y * i00 + q2.y * i10; m11 = q1.y * i01 + q2.y * i11;
                t = q[0] - Apply(p[0]);
                FloorY = floorY;
            }

            private Vector2 Apply(Vector2 v) => new Vector2(m00 * v.x + m01 * v.y, m10 * v.x + m11 * v.y);

            public Vector3 Point(Vector2 plan, float y)
            {
                var q = Apply(plan) + t;
                return new Vector3(q.x, y, q.y);
            }

            public Vector3 Vector(Vector2 plan)
            {
                var q = Apply(plan);
                return new Vector3(q.x, 0f, q.y);
            }

            public Vector3 Direction(Vector2 plan) => Vector(plan).normalized;
        }

        private static bool TryBuildPlanToScene(Scene scene, out PlanToScene map, out string missing)
        {
            map = null;
            missing = null;
            var p = new Vector2[Anchors.Length];
            var q = new Vector2[Anchors.Length];
            float floorY = 0f;
            for (int i = 0; i < Anchors.Length; i++)
            {
                var tr = FindByName(scene, Anchors[i].name);
                if (tr == null) { missing = Anchors[i].name; return false; }
                p[i] = Anchors[i].fbx;
                q[i] = new Vector2(tr.position.x, tr.position.z);
                if (i == 0) floorY = tr.position.y;
            }
            map = new PlanToScene(p, q, floorY);
            return true;
        }

        // ---------------------------------------------------------------- Outils

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static Material LoadOrCreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            EnsureFolder(WallMaterialFolder);
            string path = $"{WallMaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material FindMaterialOf(Scene scene, string objectName)
        {
            var tr = FindByName(scene, objectName);
            var renderer = tr != null ? tr.GetComponent<Renderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }

        private static void RemoveExisting(Scene scene, string name)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == name) Undo.DestroyObjectImmediate(go);
            }
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == name) return tr;
                }
            }
            return null;
        }

        private static IEnumerable<T> FindAll<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<T>(true)) yield return c;
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
