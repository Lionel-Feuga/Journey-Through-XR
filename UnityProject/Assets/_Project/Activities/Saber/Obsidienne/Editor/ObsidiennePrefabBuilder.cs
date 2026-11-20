using System.IO;
using JourneyThroughXR.Activities;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ObsidienneVR.Editor
{
    /// <summary>
    /// Génère le prefab du sabre Obsidienne : Tools > Obsidienne > Créer le prefab.
    /// Adapté de l'outil livré avec le modèle : le prefab utilise LaserSaber (saisie, lames, halo, traînée)
    /// et ObsidienneSaberLink à la place de l'adaptateur d'entrée d'origine.
    /// </summary>
    public static class ObsidiennePrefabBuilder
    {
        private const string BasePath = "Assets/_Project/Activities/Saber/Obsidienne";
        private const string SaberMaterials = "Assets/_Project/Activities/Saber/Shaders/";

        // Le modèle livré a une garde de 50 cm et une poignée de 5 cm de diamètre. À 0,62, la garde fait 31 cm
        // et la poignée 3,2 cm de diamètre pour 24 cm de long, comme les autres sabres.
        private const float ModelScale = 0.62f;

        [MenuItem("Tools/Obsidienne/Créer le prefab")]
        public static void Build()
        {
            string modelPath = BasePath + "/Models/Obsidienne_Low.fbx";
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (!importer) { Debug.LogError("Modèle introuvable : " + modelPath); return; }
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var bladeMaterial = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterials + "SaberBlade.mat");
            var glowMaterial = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterials + "SaberGlow.mat");
            var trailMaterial = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterials + "SaberTrail.mat");
            if (!bladeMaterial || !glowMaterial || !trailMaterial)
            {
                Debug.LogError("Matériaux SaberBlade, SaberGlow ou SaberTrail introuvables dans " + SaberMaterials);
                return;
            }

            Directory.CreateDirectory(BasePath + "/Materials");
            Directory.CreateDirectory(BasePath + "/Prefabs");
            AssetDatabase.Refresh();

            var root = new GameObject("Obsidienne");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                Transform rotor = Find(instance.transform, "Rotor");
                Transform axisNormal = Find(instance.transform, "Axis_Normal");
                Transform axisLocked = Find(instance.transform, "Axis_Locked");
                Transform bladeA = Find(instance.transform, "Blade_A");
                Transform bladeB = Find(instance.transform, "Blade_B");
                Transform anchor = Find(instance.transform, "Grab_Anchor");

                // Poignée et garde réduites ; les lames gardent leur longueur et leur épaisseur d'origine.
                instance.transform.localScale = Vector3.one * ModelScale;
                bladeA.localScale = Vector3.one / ModelScale;
                bladeB.localScale = Vector3.one / ModelScale;

                Vector3 gripDirection = (axisLocked.position - rotor.position).normalized;
                Vector3 normalDirection = (axisNormal.position - rotor.position).normalized;

                // Collisions, créées avant LaserSaber pour qu'il n'ajoute pas la sienne : la poignée, puis la garde.
                var grip = new GameObject("GripCollider");
                grip.transform.SetParent(root.transform, false);
                grip.transform.position = anchor.position;
                grip.transform.rotation = Quaternion.FromToRotation(Vector3.up, gripDirection);
                var capsule = grip.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.height = 0.39f * ModelScale;
                capsule.radius = 0.027f * ModelScale;

                var guard = new GameObject("GuardCollider");
                guard.transform.SetParent(root.transform, false);
                guard.transform.position = rotor.position;
                guard.transform.rotation = Quaternion.LookRotation(normalDirection, gripDirection);
                var disc = guard.AddComponent<BoxCollider>();
                disc.size = new Vector3(0.5f, 0.5f, 0.035f) * ModelScale;

                // Point de prise : la poignée verticale dans le poing, la garde face à soi (son axe dans celui de l'avant-bras).
                var attach = new GameObject("Attach");
                attach.transform.SetParent(root.transform, false);
                attach.transform.position = anchor.position;
                attach.transform.rotation = Quaternion.LookRotation(-normalDirection, gripDirection);

                // Rotor
                var controller = root.AddComponent<ObsidienneController>();
                controller.rotor = rotor;
                controller.axisNormal = axisNormal;
                controller.axisLocked = axisLocked;
                controller.invertGesture = true;

                // Sabre : saisie, lames, halo, traînée. Ajoute aussi XR Grab Interactable et le Rigidbody.
                var saber = root.AddComponent<LaserSaber>();
                var saberData = new SerializedObject(saber);
                saberData.FindProperty("blade").objectReferenceValue = bladeA;
                saberData.FindProperty("blade2").objectReferenceValue = bladeB;
                saberData.FindProperty("autoGlowMaterial").objectReferenceValue = glowMaterial;
                saberData.FindProperty("trailMaterial").objectReferenceValue = trailMaterial;
                saberData.FindProperty("lightOffset").vector3Value = Vector3.zero;
                saberData.ApplyModifiedPropertiesWithoutUndo();

                var grab = root.GetComponent<XRGrabInteractable>();
                grab.attachTransform = attach.transform;
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.useDynamicAttach = false;
                grab.farAttachMode = InteractableFarAttachMode.Near;

                root.AddComponent<ObsidienneSaberLink>();

                // Matériaux de la poignée et de la garde, créés pour le pipeline du projet. Les lames prennent ceux des autres sabres.
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.transform == bladeA || renderer.transform == bladeB)
                    {
                        renderer.sharedMaterial = bladeMaterial;
                        continue;
                    }

                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string name = mats[i] ? mats[i].name : "Obsidian_Matte";
                        string path = BasePath + "/Materials/" + name + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!material)
                        {
                            bool srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                            Shader shader = Shader.Find(srp ? "Universal Render Pipeline/Lit" : "Standard");
                            if (!shader) throw new System.InvalidOperationException("Pipeline de rendu non pris en charge : Built-in ou URP attendu.");
                            material = new Material(shader);
                            material.name = name;
                            Color color = new Color(.012f, .015f, .019f);
                            float metallic = .7f, smoothness = .56f;
                            if (name.Contains("Carbon")) { color = new Color(.009f, .012f, .016f); metallic = .05f; smoothness = .25f; }
                            if (name.Contains("Graphite")) { color = new Color(.036f, .042f, .050f); metallic = .8f; smoothness = .68f; }
                            if (name.Contains("Black")) color = new Color(.004f, .006f, .008f);
                            bool emissive = name.Contains("Cyan");
                            if (emissive) { color = new Color(.015f, .65f, .85f); metallic = 0; smoothness = .4f; }
                            material.color = color;
                            material.SetFloat("_Metallic", metallic);
                            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
                            if (emissive) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 3f); }
                            AssetDatabase.CreateAsset(material, path);
                        }
                        mats[i] = material;
                    }
                    renderer.sharedMaterials = mats;
                }

                // Même chemin à chaque génération : le prefab existant est remplacé, et ses exemplaires dans les scènes suivent.
                string prefabPath = BasePath + "/Prefabs/Obsidienne.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = prefab;
                Debug.Log("Prefab Obsidienne créé : " + prefabPath + ". Glissez-le dans la scène.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            throw new System.InvalidOperationException("Objet exporté manquant : " + name);
        }
    }
}
