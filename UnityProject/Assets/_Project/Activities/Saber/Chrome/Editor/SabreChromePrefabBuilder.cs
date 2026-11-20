using System.IO;
using JourneyThroughXR.Activities;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities.SaberTools
{
    /// <summary>
    /// Outil du sabre chromé : Tools > Sabre Chrome > Créer les prefabs.
    /// Crée les matériaux métalliques et deux prefabs (détaillé et allégé), prêts à poser dans une scène.
    /// </summary>
    public static class SabreChromePrefabBuilder
    {
        private const string BasePath = "Assets/_Project/Activities/Saber/Chrome";
        private const string SaberMaterials = "Assets/_Project/Activities/Saber/Shaders/";

        // Matériaux de la poignée : mot repère dans le nom importé, nom du fichier, couleur (linéaire), métal, poli.
        private static readonly (string key, string file, Color color, float metallic, float smoothness)[] Surfaces =
        {
            ("polies",   "Chrome_Poli",    new Color(0.72f, 0.75f, 0.76f), 1.00f, 0.86f),
            ("Chrome",   "Chrome_Satine",  new Color(0.58f, 0.61f, 0.62f), 0.93f, 0.77f),
            ("Laiton",   "Laiton",         new Color(0.40f, 0.27f, 0.09f), 0.86f, 0.72f),
            ("Acier",    "Acier_Sombre",   new Color(0.10f, 0.115f, 0.12f), 0.85f, 0.73f),
            ("rainures", "Fond_Rainures",  new Color(0.003f, 0.004f, 0.004f), 0.40f, 0.73f),
            ("Polym",    "Polymere_Noir",  new Color(0.004f, 0.005f, 0.006f), 0.05f, 0.57f),
            ("Commande", "Commande_Rouge", new Color(0.48f, 0.008f, 0.004f), 0.30f, 0.71f),
        };

        [MenuItem("Tools/Sabre Chrome/Créer les prefabs")]
        public static void Build()
        {
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

            int built = 0;
            if (BuildOne("Sabre_Chrome", bladeMaterial, glowMaterial, trailMaterial)) built++;
            if (BuildOne("Sabre_Chrome_Low", bladeMaterial, glowMaterial, trailMaterial)) built++;
            AssetDatabase.SaveAssets();
            Debug.Log("Sabre chromé : " + built + " prefab(s) créé(s) dans " + BasePath + "/Prefabs.");
        }

        private static bool BuildOne(string modelName, Material bladeMaterial, Material glowMaterial, Material trailMaterial)
        {
            string modelPath = BasePath + "/Models/" + modelName + ".fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (!model)
            {
                Debug.LogError("Modèle introuvable : " + modelPath);
                return false;
            }

            var root = new GameObject(modelName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                Transform hilt = Find(instance.transform, "Saber_Hilt");
                Transform blade = Find(instance.transform, "Saber_Blade");
                Transform glow = Find(instance.transform, "Saber_Glow");

                // Matériaux
                var hiltRenderer = hilt.GetComponent<Renderer>();
                var slots = hiltRenderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i] = SurfaceMaterial(slots[i] ? slots[i].name : "");
                }
                hiltRenderer.sharedMaterials = slots;
                blade.GetComponent<Renderer>().sharedMaterial = bladeMaterial;
                glow.GetComponent<Renderer>().sharedMaterial = glowMaterial;

                // Collision de la poignée, créée avant LaserSaber pour qu'il n'ajoute pas la sienne.
                Bounds bounds = hilt.GetComponent<MeshFilter>().sharedMesh.bounds;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = 0.02f;
                capsule.center = new Vector3(0f, bounds.center.y, 0f);
                capsule.height = bounds.size.y;

                // Sabre : saisie, lame, halo, traînée. Ajoute aussi XR Grab Interactable et le Rigidbody.
                var saber = root.AddComponent<LaserSaber>();
                var saberData = new SerializedObject(saber);
                saberData.FindProperty("blade").objectReferenceValue = blade;
                saberData.FindProperty("glow").objectReferenceValue = glow;
                saberData.FindProperty("trailMaterial").objectReferenceValue = trailMaterial;
                saberData.ApplyModifiedPropertiesWithoutUndo();

                var grab = root.GetComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.useDynamicAttach = false;
                grab.farAttachMode = InteractableFarAttachMode.Near;

                PrefabUtility.SaveAsPrefabAsset(root, BasePath + "/Prefabs/" + modelName + ".prefab");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material SurfaceMaterial(string importedName)
        {
            var surface = Surfaces[1];
            foreach (var candidate in Surfaces)
            {
                if (importedName.Contains(candidate.key))
                {
                    surface = candidate;
                    break;
                }
            }

            string path = BasePath + "/Materials/" + surface.file + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new System.InvalidOperationException("Shader URP Lit introuvable.");
            material = new Material(shader) { name = surface.file };
            // Les couleurs viennent de Blender en valeurs linéaires ; Unity attend des valeurs sRGB.
            material.SetColor("_BaseColor", surface.color.gamma);
            material.SetFloat("_Metallic", surface.metallic);
            material.SetFloat("_Smoothness", surface.smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            throw new System.InvalidOperationException("Objet manquant dans le modèle : " + name);
        }
    }
}
