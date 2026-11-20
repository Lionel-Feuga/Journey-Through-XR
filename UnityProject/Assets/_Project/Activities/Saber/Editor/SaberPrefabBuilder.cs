using System;
using System.IO;
using JourneyThroughXR.Activities;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities.SaberTools
{
    /// <summary>
    /// Crée le prefab d'un sabre à partir de son modèle : sélectionner le FBX dans la fenêtre Project,
    /// puis Tools > Sabres > Créer le prefab du modèle sélectionné.
    ///
    /// Le modèle doit contenir Saber_Hilt et Saber_Blade (et Saber_Blade_2 pour un sabre double).
    /// À côté du FBX, un fichier « NomDuModele.materials.json » donne les matériaux de la poignée
    /// (couleur linéaire, métal, rugosité), car Unity ne reprend pas le caractère métallique d'un FBX.
    /// Le prefab reçoit la saisie, la lame, le halo et la traînée communs à tous les sabres.
    /// </summary>
    public static class SaberPrefabBuilder
    {
        private const string SaberMaterials = "Assets/_Project/Activities/Saber/Shaders/";

        [Serializable]
        private class MaterialEntry
        {
            public string name;
            public float[] color;
            public float metallic;
            public float roughness;
            public float[] emission;   // facultatif : couleur linéaire x intensité
        }

        [Serializable]
        private class TextureFiles
        {
            public string baseMap;     // couleur (sRGB)
            public string maskMap;     // R métal, A lissé (linéaire)
            public string normalMap;   // normal map tangente
        }

        [Serializable]
        private class MaterialTable
        {
            public MaterialEntry[] materials;
            public TextureFiles textures;   // facultatif : textures cuites partagées par tous les matériaux de la poignée
        }

        private class BakedTextures
        {
            public Texture2D baseMap, maskMap, normalMap;
            public bool Any => baseMap || maskMap || normalMap;
        }

        [MenuItem("Tools/Sabres/Créer le prefab du modèle sélectionné")]
        public static void BuildSelected()
        {
            int built = 0;
            foreach (var selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                if (Build(path)) built++;
            }
            AssetDatabase.SaveAssets();
            if (built == 0) Debug.LogWarning("Aucun prefab créé : sélectionnez d'abord un modèle FBX de sabre dans la fenêtre Project.");
        }

        private static bool Build(string modelPath)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var bladeMaterial = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterials + "SaberBlade.mat");
            var glowMaterial = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterials + "SaberGlow.mat");
            var trailMaterial = AssetDatabase.LoadAssetAtPath<Material>(SaberMaterials + "SaberTrail.mat");
            if (!model || !bladeMaterial || !glowMaterial || !trailMaterial)
            {
                Debug.LogError("Modèle ou matériaux SaberBlade / SaberGlow / SaberTrail introuvables pour " + modelPath);
                return false;
            }

            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            string modelFolder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            string baseFolder = modelFolder.EndsWith("/Models") ? modelFolder.Substring(0, modelFolder.Length - "/Models".Length) : modelFolder;
            MaterialTable table = ReadTable(modelFolder + "/" + modelName + ".materials.json");
            BakedTextures baked = LoadBakedTextures(table.textures, modelFolder);

            Directory.CreateDirectory(baseFolder + "/Materials");
            Directory.CreateDirectory(baseFolder + "/Prefabs");
            AssetDatabase.Refresh();

            var root = new GameObject(modelName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                Transform hilt = Find(instance.transform, "Saber_Hilt");
                Transform blade = Find(instance.transform, "Saber_Blade");
                Transform blade2 = Find(instance.transform, "Saber_Blade_2");
                Transform glow = Find(instance.transform, "Saber_Glow");
                Transform glow2 = Find(instance.transform, "Saber_Glow_2");
                if (hilt == null || blade == null)
                {
                    Debug.LogError(modelName + " : le modèle doit contenir les objets Saber_Hilt et Saber_Blade.");
                    return false;
                }

                // Matériaux : lames et halos communs à tous les sabres, le reste d'après le tableau du modèle.
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Transform part = renderer.transform;
                    if (part == blade || part == blade2)
                    {
                        renderer.sharedMaterial = bladeMaterial;
                        continue;
                    }
                    if (part == glow || part == glow2)
                    {
                        renderer.sharedMaterial = glowMaterial;
                        continue;
                    }

                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        Material made = SurfaceMaterial(table, slots[i] ? slots[i].name : "", baseFolder, modelName, baked);
                        if (made != null) slots[i] = made;
                    }
                    renderer.sharedMaterials = slots;
                }

                // Collision de la poignée, créée avant LaserSaber pour qu'il n'ajoute pas la sienne.
                Bounds bounds = hilt.GetComponent<Renderer>().bounds;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.center = new Vector3(0f, bounds.center.y, 0f);
                capsule.height = bounds.size.y;
                capsule.radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f, 0.015f, 0.025f);

                // Sabre : saisie, lames, halo, traînée. Ajoute aussi XR Grab Interactable et le Rigidbody.
                var saber = root.AddComponent<LaserSaber>();
                var saberData = new SerializedObject(saber);
                saberData.FindProperty("blade").objectReferenceValue = blade;
                saberData.FindProperty("glow").objectReferenceValue = glow;
                saberData.FindProperty("blade2").objectReferenceValue = blade2;
                saberData.FindProperty("glow2").objectReferenceValue = glow2;
                saberData.FindProperty("autoGlowMaterial").objectReferenceValue = glowMaterial;
                saberData.FindProperty("trailMaterial").objectReferenceValue = trailMaterial;
                saberData.ApplyModifiedPropertiesWithoutUndo();

                var grab = root.GetComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.useDynamicAttach = false;
                grab.farAttachMode = InteractableFarAttachMode.Near;

                string prefabPath = baseFolder + "/Prefabs/" + modelName + ".prefab";
                Selection.activeObject = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("Prefab créé : " + prefabPath);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static MaterialTable ReadTable(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("Tableau des matériaux absent (" + path + ") : la poignée garde les matériaux importés, sans effet métallique.");
                return new MaterialTable { materials = new MaterialEntry[0] };
            }
            var table = JsonUtility.FromJson<MaterialTable>(File.ReadAllText(path));
            if (table == null || table.materials == null) table = new MaterialTable { materials = new MaterialEntry[0] };
            return table;
        }

        /// <summary>Matériau URP Lit correspondant à un matériau importé, ou null s'il n'est pas dans le tableau.</summary>
        private static Material SurfaceMaterial(MaterialTable table, string importedName, string baseFolder, string modelName, BakedTextures baked)
        {
            MaterialEntry entry = null;
            foreach (var candidate in table.materials)
            {
                if (candidate != null && candidate.name == importedName) entry = candidate;
            }
            if (entry == null || entry.color == null || entry.color.Length < 3)
            {
                if (table.materials.Length > 0) Debug.LogWarning(modelName + " : matériau « " + importedName + " » absent du tableau, laissé tel quel.");
                return null;
            }

            string path = baseFolder + "/Materials/" + entry.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = material == null;
            if (isNew)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) throw new InvalidOperationException("Shader URP Lit introuvable.");
                material = new Material(shader) { name = entry.name };
            }

            // Les couleurs du tableau sont en valeurs linéaires ; Unity attend des valeurs sRGB.
            material.SetColor("_BaseColor", new Color(entry.color[0], entry.color[1], entry.color[2]).gamma);
            material.SetFloat("_Metallic", entry.metallic);
            material.SetFloat("_Smoothness", 1f - entry.roughness);
            SetEmission(material, entry.emission);
            ApplyBakedTextures(material, baked);
            if (isNew) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Textures cuites dans Blender (usure) : la couleur et le métal/lissé viennent alors des textures.</summary>
        private static BakedTextures LoadBakedTextures(TextureFiles files, string modelFolder)
        {
            var baked = new BakedTextures();
            if (files == null) return baked;
            baked.baseMap = ImportTexture(modelFolder, files.baseMap, srgb: true, normal: false);
            baked.maskMap = ImportTexture(modelFolder, files.maskMap, srgb: false, normal: false);
            baked.normalMap = ImportTexture(modelFolder, files.normalMap, srgb: false, normal: true);
            return baked;
        }

        private static Texture2D ImportTexture(string folder, string file, bool srgb, bool normal)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string path = folder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("Texture introuvable : " + path);
                return null;
            }
            bool changed = false;
            if (normal && importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; changed = true; }
            if (!normal && importer.sRGBTexture != srgb) { importer.sRGBTexture = srgb; changed = true; }
            if (!normal && !srgb && importer.alphaSource != TextureImporterAlphaSource.FromInput) { importer.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }
            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void ApplyBakedTextures(Material material, BakedTextures baked)
        {
            if (baked == null || !baked.Any) return;
            if (baked.baseMap)
            {
                material.SetTexture("_BaseMap", baked.baseMap);
                material.SetColor("_BaseColor", Color.white);
            }
            if (baked.maskMap)
            {
                material.SetTexture("_MetallicGlossMap", baked.maskMap);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_SmoothnessTextureChannel", 0f);
                material.SetFloat("_Smoothness", 1f);
                material.SetFloat("_Metallic", 1f);
            }
            if (baked.normalMap)
            {
                material.SetTexture("_BumpMap", baked.normalMap);
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_BumpScale", 1f);
            }
        }

        /// <summary>Émission URP (fenêtres lumineuses). Valeurs linéaires HDR, converties pour SetColor qui attend du sRGB.</summary>
        private static void SetEmission(Material material, float[] emission)
        {
            float peak = emission != null && emission.Length >= 3 ? Mathf.Max(emission[0], Mathf.Max(emission[1], emission[2])) : 0f;
            if (peak <= 0f)
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                return;
            }
            Color hue = new Color(emission[0] / peak, emission[1] / peak, emission[2] / peak).gamma;
            float boost = Mathf.Pow(peak, 1f / 2.2f);
            material.SetColor("_EmissionColor", new Color(hue.r * boost, hue.g * boost, hue.b * boost));
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            return null;
        }
    }
}
