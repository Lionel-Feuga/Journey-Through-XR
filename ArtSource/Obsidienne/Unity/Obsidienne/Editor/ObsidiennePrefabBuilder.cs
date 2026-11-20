using System.IO;
using UnityEditor;
using UnityEngine;

namespace ObsidienneVR.Editor
{
    public static class ObsidiennePrefabBuilder
    {
        private const string BasePath = "Assets/Obsidienne";
        [MenuItem("Tools/Obsidienne/Create VR prefab")]
        public static void Build() { BuildModel("Obsidienne_Low.fbx"); }
        [MenuItem("Tools/Obsidienne/Create detailed prefab")]
        public static void BuildDetailed() { BuildModel("Obsidienne.fbx"); }
        private static void BuildModel(string file)
        {
            string modelPath = BasePath + "/Models/" + file;
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (!importer) { Debug.LogError("Copy the Obsidienne folder into Assets first."); return; }
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Directory.CreateDirectory(BasePath + "/Materials");
            Directory.CreateDirectory(BasePath + "/Prefabs");
            AssetDatabase.Refresh();
            var root = new GameObject("Obsidienne_VR");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                var c = root.AddComponent<ObsidienneController>();
                c.rotor = Find(instance.transform,"Rotor");
                c.axisNormal = Find(instance.transform,"Axis_Normal");
                c.axisLocked = Find(instance.transform,"Axis_Locked");
                c.bladeA = Find(instance.transform,"Blade_A").gameObject;
                c.bladeB = Find(instance.transform,"Blade_B").gameObject;
                c.bladeA.SetActive(false);c.bladeB.SetActive(false);
                var input=root.AddComponent<ObsidienneXRInput>();input.saber=c;
                var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                var grip=new GameObject("GripCollider");grip.transform.SetParent(root.transform,false);
                grip.transform.position=Find(instance.transform,"Grab_Anchor").position;
                Vector3 direction=(c.axisLocked.position-c.rotor.position).normalized;
                grip.transform.rotation=Quaternion.FromToRotation(Vector3.up,direction);
                var capsule=grip.AddComponent<CapsuleCollider>();capsule.direction=1;capsule.height=.39f;capsule.radius=.027f;
                // Materials are regenerated for the project's render pipeline.
                foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var mats=renderer.sharedMaterials;
                    for(int i=0;i<mats.Length;i++)
                    {
                        string name=mats[i] ? mats[i].name : "Obsidian_Matte";
                        string path=BasePath+"/Materials/"+name+".mat";
                        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!material)
                        {
                            bool srp=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                            Shader shader=Shader.Find(srp ? "Universal Render Pipeline/Lit" : "Standard");
                            if(!shader) throw new System.InvalidOperationException("Use Built-in or URP, or provide custom materials for your pipeline.");
                            material=new Material(shader);material.name=name;
                            Color color=new Color(.012f,.015f,.019f);
                            float metallic=.7f,smoothness=.56f;
                            if(name.Contains("Carbon")) {color=new Color(.009f,.012f,.016f);metallic=.05f;smoothness=.25f;}
                            if(name.Contains("Graphite")) {color=new Color(.036f,.042f,.050f);metallic=.8f;smoothness=.68f;}
                            if(name.Contains("Black")) color=new Color(.004f,.006f,.008f);
                            bool emissive=name.Contains("Cyan");
                            if(emissive) {color=new Color(.015f,.65f,.85f);metallic=0;smoothness=.4f;}
                            material.color=color;
                            material.SetFloat("_Metallic",metallic);
                            if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",smoothness);
                            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",smoothness);
                            if(emissive) {material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",color*(name.Contains("Blade")?6f:3f));}
                            AssetDatabase.CreateAsset(material,path);
                        }
                        mats[i]=material;
                    }
                    renderer.sharedMaterials=mats;
                }
                string prefabPath=AssetDatabase.GenerateUniqueAssetPath(BasePath+"/Prefabs/Obsidienne_VR.prefab");
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                AssetDatabase.SaveAssets();Selection.activeObject=prefab;
                Debug.Log("Obsidienne prefab created. Assign trackedController in the scene, then bind Grab/Release on your XR grab component.");
            }
            finally {Object.DestroyImmediate(root);}
        }
        private static Transform Find(Transform root,string name)
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;
            throw new System.InvalidOperationException("Missing exported node: "+name);
        }
    }
}
