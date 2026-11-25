using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Poste de travail de l'open-space 309 : bureau, chaise, écran, unité centrale, clavier, souris.
    /// L'objet porteur est posé au pied du mur, au milieu du bord arrière du bureau ;
    /// son axe bleu (Z) part du mur vers la pièce, côté visiteur.
    /// Menu du composant : « Créer le poste de travail ».
    /// </summary>
    public class ComputerDesk : MonoBehaviour
    {
        // Réglages
        [Tooltip("Surface de l'écran, où s'affichera le contenu de l'activité.")]
        [SerializeField] private Transform screenSurface;

        [Tooltip("Chaise du poste.")]
        [SerializeField] private ComputerSeat seat;

        [Header("Matériaux (créés par le menu)")]
        [SerializeField] private Material deskMaterial;
        [SerializeField] private Material metalMaterial;
        [SerializeField] private Material plasticMaterial;
        [SerializeField] private Material fabricMaterial;
        [SerializeField] private Material screenMaterial;
        [SerializeField] private Material ledMaterial;

        public Transform ScreenSurface => screenSurface;
        public ComputerSeat Seat => seat;

        // Dimensions (mètres)
        private const float DeskWidth = 1.4f;
        private const float DeskDepth = 0.7f;
        private const float DeskHeight = 0.75f;
        private const float DeskTop = 0.03f;
        private const float DeskBack = 0.03f;      // jour entre le mur et le bureau

        private const float MonitorWidth = 0.62f;
        private const float MonitorHeight = 0.37f;
        private const float MonitorCenterY = 1.06f;
        private const float MonitorZ = 0.22f;

        private const float ChairZ = 1.0f;
        private const float EyeHeight = 1.18f;
        private const float EyeDistance = 0.6f;    // des yeux à l'écran

        private static readonly string[] GeneratedNames =
            { "Bureau", "Ecran", "Unite_Centrale", "Clavier", "Souris", "Chaise", "Point_Vue", "Point_Lever", "Indication_Se_Lever" };

#if UNITY_EDITOR
        // Construction
        [ContextMenu("Créer le poste de travail")]
        private void Build()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Créer le poste de travail");
            RemoveGenerated();
            CreateMaterials();

            float topY = DeskHeight - DeskTop * 0.5f;
            float deskZ = DeskBack + DeskDepth * 0.5f;

            // Bureau
            var desk = Group("Bureau", transform);
            Box("Plateau", desk, new Vector3(0f, topY, deskZ), new Vector3(DeskWidth, DeskTop, DeskDepth), deskMaterial);
            float legX = DeskWidth * 0.5f - 0.05f;
            float legZ = DeskDepth * 0.5f - 0.05f;
            float legH = DeskHeight - DeskTop;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * legX;
                float z = deskZ + (i < 2 ? -1f : 1f) * legZ;
                Box($"Pied_{i + 1}", desk, new Vector3(x, legH * 0.5f, z), new Vector3(0.05f, legH, 0.05f), metalMaterial);
            }
            Box("Voile", desk, new Vector3(0f, legH - 0.15f, DeskBack + 0.06f), new Vector3(DeskWidth - 0.1f, 0.3f, 0.015f), metalMaterial);

            // Écran
            var monitor = Group("Ecran", transform);
            Box("Pied_Socle", monitor, new Vector3(0f, DeskHeight + 0.008f, MonitorZ), new Vector3(0.24f, 0.016f, 0.18f), plasticMaterial);
            float neckBottom = DeskHeight + 0.016f;
            float neckTop = MonitorCenterY - 0.05f;
            Box("Pied_Bras", monitor, new Vector3(0f, (neckBottom + neckTop) * 0.5f, MonitorZ - 0.03f), new Vector3(0.05f, neckTop - neckBottom, 0.025f), plasticMaterial);
            Box("Coque", monitor, new Vector3(0f, MonitorCenterY, MonitorZ), new Vector3(MonitorWidth, MonitorHeight, 0.035f), plasticMaterial);
            // Dalle écartée de 1 cm de la coque : le Quest départage mal les profondeurs trop proches.
            var panel = Box("Dalle", monitor, new Vector3(0f, MonitorCenterY + 0.01f, MonitorZ + 0.0275f), new Vector3(MonitorWidth - 0.03f, MonitorHeight - 0.05f, 0.002f), screenMaterial);
            DestroyImmediate(panel.GetComponent<Collider>());
            var surface = new GameObject("Surface_Ecran").transform;
            surface.SetParent(monitor, false);
            surface.localPosition = new Vector3(0f, MonitorCenterY + 0.01f, MonitorZ + 0.0375f);
            surface.localRotation = Quaternion.identity; // axe bleu vers le visiteur
            screenSurface = surface;

            // Unité centrale, sous le bureau à droite
            var tower = Group("Unite_Centrale", transform);
            Box("Boitier", tower, new Vector3(0.48f, 0.235f, DeskBack + 0.3f), new Vector3(0.2f, 0.45f, 0.44f), plasticMaterial);
            var led = Box("Voyant", tower, new Vector3(0.48f, 0.42f, DeskBack + 0.3f + 0.225f), new Vector3(0.02f, 0.02f, 0.01f), ledMaterial);
            DestroyImmediate(led.GetComponent<Collider>());

            // Clavier et souris
            var keyboard = Group("Clavier", transform);
            Box("Clavier", keyboard, new Vector3(-0.03f, DeskHeight + 0.01f, 0.52f), new Vector3(0.44f, 0.02f, 0.14f), plasticMaterial);
            var mouse = Group("Souris", transform);
            Box("Souris", mouse, new Vector3(0.3f, DeskHeight + 0.015f, 0.52f), new Vector3(0.06f, 0.03f, 0.1f), plasticMaterial);

            // Chaise, tournée vers le bureau
            var chair = Group("Chaise", transform);
            chair.localPosition = new Vector3(0f, 0f, ChairZ);
            Box("Croix_1", chair, new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.03f, 0.06f), metalMaterial);
            Box("Croix_2", chair, new Vector3(0f, 0.04f, 0f), new Vector3(0.06f, 0.03f, 0.6f), metalMaterial);
            var pole = Primitive(PrimitiveType.Cylinder, "Verin", chair, metalMaterial);
            pole.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            pole.transform.localScale = new Vector3(0.05f, 0.2f, 0.05f);
            Box("Assise", chair, new Vector3(0f, 0.47f, 0f), new Vector3(0.46f, 0.07f, 0.46f), fabricMaterial);
            Box("Dossier", chair, new Vector3(0f, 0.8f, 0.22f), new Vector3(0.44f, 0.5f, 0.05f), fabricMaterial);

            var sitHint = Label("Indication_Asseoir", chair, new Vector3(0f, 1.25f, 0f), "S'asseoir\n<size=60%>Visez la chaise + gâchette</size>");
            sitHint.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // lisible depuis la pièce (tourné vers le visiteur au lancement)

            chair.gameObject.AddComponent<XRSimpleInteractable>();
            seat = chair.gameObject.AddComponent<ComputerSeat>();

            // Points de vue
            var eye = new GameObject("Point_Vue").transform;
            eye.SetParent(transform, false);
            eye.localPosition = new Vector3(0f, EyeHeight, MonitorZ + EyeDistance);
            eye.localRotation = Quaternion.Euler(0f, 180f, 0f); // regarde l'écran

            var stand = new GameObject("Point_Lever").transform;
            stand.SetParent(transform, false);
            stand.localPosition = new Vector3(0.8f, 0f, ChairZ + 0.15f);

            var standHint = Label("Indication_Se_Lever", transform, new Vector3(0f, DeskHeight + 0.12f, MonitorZ + 0.15f), "<size=70%>Joystick gauche : se lever</size>");
            standHint.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // lisible une fois assis

            seat.Configure(eye, stand, sitHint, standHint);
            UnityEditor.EditorUtility.SetDirty(seat);

            // Programme affiché sur l'écran (créé au lancement).
            var program = GetComponent<ComputerProgram>();
            if (program == null) program = UnityEditor.Undo.AddComponent<ComputerProgram>(gameObject);
            program.ConfigureScreen(screenSurface);
            UnityEditor.EditorUtility.SetDirty(program);
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[Poste] Poste de travail créé. Enregistrer la scène (Ctrl+S).", this);
        }

        private void RemoveGenerated()
        {
            foreach (var n in GeneratedNames)
            {
                var child = transform.Find(n);
                while (child != null)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(child.gameObject);
                    child = transform.Find(n);
                }
            }
        }

        private void CreateMaterials()
        {
            deskMaterial = LoadOrCreate("Poste_Bureau", new Color(0.78f, 0.66f, 0.5f), 0f, 0.35f, false);
            metalMaterial = LoadOrCreate("Poste_Metal", new Color(0.25f, 0.26f, 0.28f), 0.8f, 0.5f, false);
            plasticMaterial = LoadOrCreate("Poste_Plastique", new Color(0.08f, 0.08f, 0.09f), 0f, 0.45f, false);
            fabricMaterial = LoadOrCreate("Poste_Tissu", new Color(0.12f, 0.2f, 0.42f), 0f, 0.1f, false);
            screenMaterial = LoadOrCreate("Poste_Dalle", new Color(0.03f, 0.05f, 0.1f), 0f, 0f, true);
            ledMaterial = LoadOrCreate("Poste_Voyant", new Color(0.2f, 0.6f, 1f), 0f, 0f, true);
        }

        private static Material LoadOrCreate(string name, Color color, float metallic, float smoothness, bool unlit)
        {
            const string folder = "Assets/_Project/Activities/Computers/Materials";
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                material = new Material(shader);
                UnityEditor.AssetDatabase.CreateAsset(material, path);
            }
            // Couleurs enregistrées dans le matériau lui-même (pas de bloc de propriétés, perdu au rechargement).
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            UnityEditor.EditorUtility.SetDirty(material);
            UnityEditor.AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            UnityEditor.AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        // Aides
        private static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, name);
            return go.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = Primitive(PrimitiveType.Cube, name, parent, material);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            return go;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static TMP_Text Label(string name, Transform parent, Vector3 position, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 0.6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = new Vector2(1.2f, 0.4f);
            go.SetActive(false);
            return tmp;
        }
#endif
    }
}
