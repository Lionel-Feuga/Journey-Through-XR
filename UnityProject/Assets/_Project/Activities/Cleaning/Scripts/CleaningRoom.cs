using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Un sanitaire à nettoyer : lavabo, WC, mur sale, éponge et compteur « Nettoyage 1/3 »
    /// affiché sur le miroir du lavabo (il ne gêne pas le passage).
    /// L'objet porteur est posé au sol, au milieu de la porte d'entrée, Rotation 0 / 0 / 0
    /// (le visiteur entre en allant vers -Z).
    /// Menu du composant : « Créer le sanitaire ».
    /// </summary>
    public class CleaningRoom : MonoBehaviour
    {
        public enum Layout { Mixte, PMR }

        // Réglages
        [Tooltip("Disposition à créer : sanitaire mixte (lavabo + 2 cabines) ou PMR.")]
        [SerializeField] private Layout layout = Layout.Mixte;

        [Tooltip("Zones sales, dans l'ordre conseillé (lavabo, WC, mur).")]
        [SerializeField] private DirtSpot[] spots;

        [SerializeField] private CleaningSponge sponge;

        [Tooltip("Texte du panneau mural (« Nettoyage 1/3 »).")]
        [SerializeField] private TMP_Text counterText;

        [Header("Matériaux (créés par le menu)")]
        [SerializeField] private Material ceramicMaterial;
        [SerializeField] private Material basinMaterial;
        [SerializeField] private Material cabinetMaterial;
        [SerializeField] private Material chromeMaterial;
        [SerializeField] private Material mirrorMaterial;
        [SerializeField] private Material dirtMaterial;
        [SerializeField] private Material spongeMaterial;
        [SerializeField] private Material scrubMaterial;

        // État
        public event Action<CleaningRoom> Progressed;
        public event Action<CleaningRoom> SpotCleaned;
        public event Action<CleaningRoom> Completed;
        public event Action<CleaningRoom> SpongeGrabbed;

        public DirtSpot[] Spots => spots;
        public CleaningSponge Sponge => sponge;
        public int Total => spots != null ? spots.Length : 0;
        public bool IsComplete => Total > 0 && CleanedCount == Total;
        public Vector3 Center => counterText != null ? counterText.transform.position : transform.position;

        public int CleanedCount
        {
            get
            {
                int n = 0;
                if (spots != null) foreach (var s in spots) if (s != null && s.IsClean) n++;
                return n;
            }
        }

        private string hintLine = "";
        private bool finished;

        // Cycle de vie
        private void Awake() => AutoWire();

        /// Retrouve seul les zones, l'éponge et le compteur parmi les enfants si les champs sont vides
        /// (ex. composant réinitialisé ou ajouté de nouveau).
        private void AutoWire()
        {
            bool missing = spots == null || spots.Length == 0;
            if (!missing) foreach (var s in spots) if (s == null) missing = true;
            if (missing)
            {
                var found = new List<DirtSpot>();
                foreach (var path in new[] { "Lavabo/Salete_Lavabo", "WC/Salete_WC", "Mur_Sale/Salete_Mur" })
                {
                    var t = transform.Find(path);
                    var spot = t != null ? t.GetComponent<DirtSpot>() : null;
                    if (spot != null) found.Add(spot);
                }
                foreach (var spot in GetComponentsInChildren<DirtSpot>(true))
                    if (!found.Contains(spot)) found.Add(spot);
                spots = found.ToArray();
                Debug.Log($"[Sanitaire] {name} : {spots.Length} zones sales retrouvées automatiquement.", this);
            }

            if (sponge == null) sponge = GetComponentInChildren<CleaningSponge>(true);
            if (sponge != null) sponge.SetSpots(spots);

            if (counterText == null)
            {
                var t = transform.Find("Lavabo/Compteur/Texte");
                if (t == null) t = transform.Find("Compteur/Texte");
                if (t != null) counterText = t.GetComponent<TMP_Text>();
            }
        }

        private void OnEnable()
        {
            if (spots != null)
                foreach (var s in spots)
                {
                    if (s == null) continue;
                    s.Rubbed += OnRubbed;
                    s.Cleaned += OnCleaned;
                }
            if (sponge != null) sponge.Grabbed += OnSpongeGrabbed;
            RefreshPanel();
        }

        private void OnDisable()
        {
            if (spots != null)
                foreach (var s in spots)
                {
                    if (s == null) continue;
                    s.Rubbed -= OnRubbed;
                    s.Cleaned -= OnCleaned;
                }
            if (sponge != null) sponge.Grabbed -= OnSpongeGrabbed;
        }

        // Commandes (appelées par l'activité)
        public DirtSpot NextSpot()
        {
            if (spots == null) return null;
            foreach (var s in spots) if (s != null && !s.IsClean) return s;
            return null;
        }

        public void SetHint(string text)
        {
            hintLine = text ?? "";
            RefreshPanel();
        }

        public void HighlightOnly(DirtSpot target)
        {
            if (spots == null) return;
            foreach (var s in spots) if (s != null) s.SetHighlighted(s == target);
        }

        public void ResetRoom()
        {
            finished = false;
            hintLine = "";
            if (spots != null) foreach (var s in spots) if (s != null) s.ResetDirt();
            if (sponge != null) sponge.ReturnHome();
            RefreshPanel();
        }

        // Événements
        private void OnRubbed(DirtSpot spot) => Progressed?.Invoke(this);
        private void OnSpongeGrabbed(CleaningSponge s)
        {
            hintLine = "";
            RefreshPanel();
            SpongeGrabbed?.Invoke(this);
            Progressed?.Invoke(this);
        }

        private void OnCleaned(DirtSpot spot)
        {
            spot.SetHighlighted(false);
            hintLine = "";
            RefreshPanel();
            Progressed?.Invoke(this);
            SpotCleaned?.Invoke(this);
            if (!finished && IsComplete)
            {
                finished = true;
                if (sponge != null && !sponge.IsHeld) sponge.ReturnHome();
                Completed?.Invoke(this);
            }
        }

        // Panneau
        private void RefreshPanel()
        {
            if (counterText == null) return;
            int done = CleanedCount;
            var sb = new StringBuilder();
            sb.Append("<b>Nettoyage ").Append(done).Append('/').Append(Total).Append("</b>\n");

            if (Total > 0 && done == Total)
            {
                sb.Append("<color=#1E7A3A>Bravo, tout brille !</color>");
            }
            else
            {
                sb.Append("<size=55%>");
                if (!string.IsNullOrEmpty(hintLine))
                {
                    sb.Append("<color=#A04A00>").Append(hintLine).Append("</color>");
                }
                else
                {
                    sb.Append("Reste : ");
                    bool first = true;
                    foreach (var s in spots)
                    {
                        if (s == null || s.IsClean) continue;
                        if (!first) sb.Append(", ");
                        sb.Append(s.ShortName);
                        first = false;
                    }
                }
                sb.Append("</size>");
            }
            counterText.text = sb.ToString();
        }

#if UNITY_EDITOR
        // Disposition (mètres, repère de l'objet porteur : milieu de la porte, au sol, -Z vers l'intérieur)
        private struct Fixture
        {
            public Vector3 position;
            public float yaw;          // l'axe bleu de l'élément part du mur vers la pièce
            public string shortName;
            public string hint;
            public float spongeX;      // lavabo : côté du plan où est posée l'éponge
        }

        private void GetLayout(out Fixture sink, out Fixture toilet, out Fixture wall, out Fixture panel)
        {
            if (layout == Layout.Mixte)
            {
                // Positions relevées dans l'école par Lionel (26 nov.).
                sink = new Fixture { position = new Vector3(-1.17f, 0f, 0.01f), yaw = 180f, shortName = "Lavabo",
                    hint = "le lavabo (mur de la porte, à droite en entrant)", spongeX = -0.26f };
                toilet = new Fixture { position = new Vector3(-0.6f, 0f, -3.28f), yaw = 0f, shortName = "WC",
                    hint = "le WC de la cabine de gauche (ouvrez sa porte)" };
                wall = new Fixture { position = new Vector3(0.61f, 0f, -0.99f), yaw = -90f, shortName = "Mur",
                    hint = "le mur de gauche en entrant" };
                // Panneau de secours si le lavabo manque : mur entre les deux portes de cabine.
                panel = new Fixture { position = new Vector3(-0.685f, 0f, -1.99f), yaw = 0f };
            }
            else
            {
                sink = new Fixture { position = new Vector3(1f, 0f, -0.01f), yaw = 180f, shortName = "Lavabo",
                    hint = "le lavabo (mur de la porte, à gauche en entrant)", spongeX = 0.26f };
                toilet = new Fixture { position = new Vector3(-0.11f, 0f, -3.26f), yaw = 0f, shortName = "WC",
                    hint = "le WC au fond de la pièce" };
                wall = new Fixture { position = new Vector3(-0.57f, 0f, -1.99f), yaw = 90f, shortName = "Mur",
                    hint = "le mur de droite en entrant" };
                // Panneau de secours si le lavabo manque : mur du fond, au-dessus du WC.
                panel = new Fixture { position = new Vector3(-0.11f, 0f, -3.26f), yaw = 0f };
            }
        }

        private static readonly string[] GeneratedNames = { "Lavabo", "WC", "Mur_Sale", "Eponge", "Place_Eponge", "Compteur" };

        [ContextMenu("Créer le sanitaire")]
        private void Build()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Créer le sanitaire");
            RemoveGenerated();
            CreateMaterials();
            GetLayout(out var sinkFx, out var toiletFx, out var wallFx, out var panelFx);
            var random = new System.Random(layout == Layout.Mixte ? 11 : 23);

            // Lavabo
            var sink = Group("Lavabo", transform, sinkFx);
            Box("Meuble", sink, new Vector3(0f, 0.4f, 0.225f), new Vector3(0.6f, 0.8f, 0.45f), cabinetMaterial);
            Box("Plan", sink, new Vector3(0f, 0.825f, 0.235f), new Vector3(0.62f, 0.05f, 0.47f), ceramicMaterial);
            NoCollider(Box("Vasque", sink, new Vector3(0f, 0.855f, 0.25f), new Vector3(0.42f, 0.01f, 0.3f), basinMaterial));
            var tap = Primitive(PrimitiveType.Cylinder, "Robinet", sink, chromeMaterial);
            tap.transform.localPosition = new Vector3(0f, 0.9f, 0.06f);
            tap.transform.localScale = new Vector3(0.03f, 0.05f, 0.03f);
            Box("Bec", sink, new Vector3(0f, 0.945f, 0.11f), new Vector3(0.025f, 0.025f, 0.12f), chromeMaterial);
            NoCollider(Box("Miroir", sink, new Vector3(0f, 1.45f, 0.01f), new Vector3(0.55f, 0.7f, 0.01f), mirrorMaterial));
            var sinkSpot = Spot(sink, "Salete_Lavabo", new Vector3(0f, 0.862f, 0.25f), Quaternion.Euler(-90f, 0f, 0f),
                new Vector2(0.18f, 0.12f), 7, 0.05f, 0.1f, sinkFx, new Vector3(0f, 1.15f, 0.3f), random);

            // Place de l'éponge, sur le plan à côté de la vasque
            var home = new GameObject("Place_Eponge").transform;
            home.SetParent(transform, false);
            home.position = sink.TransformPoint(new Vector3(sinkFx.spongeX, 0.873f, 0.1f));
            home.rotation = sink.rotation * Quaternion.Euler(0f, 90f, 0f);
            UnityEditor.Undo.RegisterCreatedObjectUndo(home.gameObject, "Place_Eponge");

            // WC
            var toilet = Group("WC", transform, toiletFx);
            Box("Reservoir", toilet, new Vector3(0f, 0.62f, 0.095f), new Vector3(0.4f, 0.38f, 0.17f), ceramicMaterial);
            Box("Pied", toilet, new Vector3(0f, 0.19f, 0.38f), new Vector3(0.3f, 0.38f, 0.42f), ceramicMaterial);
            Box("Cuvette", toilet, new Vector3(0f, 0.4f, 0.4f), new Vector3(0.38f, 0.06f, 0.5f), ceramicMaterial);
            // Abattant fermé : la saleté est sur le dessus de la cuvette.
            Box("Abattant", toilet, LidPosition, LidSize, ceramicMaterial);
            var toiletSpot = Spot(toilet, "Salete_WC", new Vector3(0f, LidTop + 0.001f, 0.42f), Quaternion.Euler(-90f, 0f, 0f),
                new Vector2(0.11f, 0.14f), 6, 0.05f, 0.09f, toiletFx, new Vector3(0f, 0.8f, 0.5f), random);

            // Mur sale
            var wall = Group("Mur_Sale", transform, wallFx);
            var wallSpot = Spot(wall, "Salete_Mur", new Vector3(0f, 1.25f, 0.004f), Quaternion.identity,
                new Vector2(0.28f, 0.22f), 9, 0.06f, 0.14f, wallFx, new Vector3(0f, 1.65f, 0.25f), random);

            spots = new[] { sinkSpot, toiletSpot, wallSpot };

            // Éponge
            var spongeGo = new GameObject("Eponge");
            spongeGo.transform.SetParent(transform, false);
            spongeGo.transform.SetPositionAndRotation(home.position, home.rotation);
            UnityEditor.Undo.RegisterCreatedObjectUndo(spongeGo, "Eponge");
            var col = spongeGo.AddComponent<BoxCollider>();
            col.size = new Vector3(0.12f, 0.045f, 0.08f);
            NoCollider(Box("Mousse", spongeGo.transform, new Vector3(0f, -0.0075f, 0f), new Vector3(0.12f, 0.03f, 0.08f), spongeMaterial));
            NoCollider(Box("Grattoir", spongeGo.transform, new Vector3(0f, 0.015f, 0f), new Vector3(0.12f, 0.015f, 0.08f), scrubMaterial));
            var body = spongeGo.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var grab = spongeGo.AddComponent<XRGrabInteractable>();
            grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = false;
            grab.farAttachMode = UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Near;
            sponge = spongeGo.AddComponent<CleaningSponge>();
            sponge.Configure(spots, home, null);
            UnityEditor.EditorUtility.SetDirty(sponge);

            // Compteur
            CreatePanel(panelFx);

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Sanitaire] {layout} créé. Enregistrer la scène (Ctrl+S).", this);
        }

        // Panneau affiché sur le miroir du lavabo (texte foncé sur le miroir clair).
        private const float MirrorCenterY = 1.45f;
        private static readonly Color PanelTextColor = new Color(0.1f, 0.13f, 0.2f);

        private void CreatePanel(Fixture fallback)
        {
            Transform root;
            var sink = transform.Find("Lavabo");
            if (sink != null)
            {
                root = new GameObject("Compteur").transform;
                root.SetParent(sink, false);
                UnityEditor.Undo.RegisterCreatedObjectUndo(root.gameObject, "Compteur");
            }
            else
            {
                root = Group("Compteur", transform, fallback);
            }

            // Miroir : 0,55 x 0,7 m, face avant à 1,5 cm du mur. Texte 1 cm devant.
            counterText = Label("Texte", root, new Vector3(0f, MirrorCenterY, 0.025f), "", 0.3f, new Vector2(0.48f, 0.6f));
            counterText.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // lisible depuis la pièce
            counterText.color = PanelTextColor;
            counterText.enableAutoSizing = true;
            counterText.fontSizeMin = 0.1f;
            counterText.fontSizeMax = 0.3f;
            counterText.gameObject.SetActive(true);
            RefreshPanel();
            UnityEditor.EditorUtility.SetDirty(counterText);
        }

        // Remplace l'ancien panneau par le panneau sur le miroir, sans toucher au reste.
        [ContextMenu("Placer le panneau sur le miroir")]
        private void RebuildPanel()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Placer le panneau sur le miroir");
            foreach (var parent in new[] { transform, transform.Find("Lavabo") })
            {
                if (parent == null) continue;
                var old = parent.Find("Compteur");
                while (old != null)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);
                    old = parent.Find("Compteur");
                }
            }
            CreateMaterials();
            GetLayout(out _, out _, out _, out var panelFx);
            CreatePanel(panelFx);
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[Sanitaire] Panneau placé sur le miroir. Enregistrer la scène (Ctrl+S).", this);
        }

        private static readonly Vector3 LidPosition = new Vector3(0f, 0.445f, 0.42f);
        private static readonly Vector3 LidSize = new Vector3(0.38f, 0.03f, 0.46f);
        private const float LidTop = 0.46f;

        // Ferme les WC d'un sanitaire déjà créé, sans toucher au reste (positions, éponge, textes).
        [ContextMenu("Fermer les WC (sans eau)")]
        private void CloseToilet()
        {
            var toilet = transform.Find("WC");
            if (toilet == null)
            {
                Debug.LogWarning("[Sanitaire] Pas d'objet « WC » sous ce sanitaire.", this);
                return;
            }
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(toilet.gameObject, "Fermer les WC");

            var water = toilet.Find("Eau");
            if (water != null) UnityEditor.Undo.DestroyObjectImmediate(water.gameObject);

            var lid = toilet.Find("Abattant");
            if (lid != null)
            {
                lid.localPosition = LidPosition;
                lid.localRotation = Quaternion.identity;
                lid.localScale = LidSize;
            }

            var spot = toilet.Find("Salete_WC");
            if (spot != null) spot.localPosition = new Vector3(0f, LidTop + 0.001f, 0.42f);

            UnityEditor.EditorUtility.SetDirty(toilet.gameObject);
            Debug.Log("[Sanitaire] WC fermés. Enregistrer la scène (Ctrl+S).", this);
        }

        private DirtSpot Spot(Transform parent, string name, Vector3 position, Quaternion rotation, Vector2 half,
            int count, float minSize, float maxSize, Fixture fx, Vector3 labelPos, System.Random random)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, name);

            var quad = CreateDirtQuad(go.transform, half);
            var label = Label("Propre", parent, labelPos, "<color=#5CC276>Propre !</color>", 0.5f, new Vector2(0.8f, 0.25f));

            var spot = go.AddComponent<DirtSpot>();
            spot.Configure(fx.shortName, fx.hint, half, quad, label);
            UnityEditor.EditorUtility.SetDirty(spot);
            return spot;
        }

        // Quad à plat sur la surface, 6 mm devant elle ; la texture de saleté est créée au lancement.
        private Renderer CreateDirtQuad(Transform spot, Vector2 half)
        {
            var quad = Primitive(PrimitiveType.Quad, "Salete", spot, dirtMaterial);
            NoCollider(quad);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face visible vers le visiteur
            quad.transform.localScale = new Vector3(half.x * 2f, half.y * 2f, 1f);
            quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return quad.GetComponent<MeshRenderer>();
        }

        // Convertit un sanitaire déjà créé : taches rondes remplacées par la saleté effaçable. Rien d'autre ne bouge.
        [ContextMenu("Passer à la saleté effaçable")]
        private void UpgradeDirt()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Saleté effaçable");
            CreateMaterials();
            int count = 0;
            foreach (var spot in GetComponentsInChildren<DirtSpot>(true))
            {
                for (int i = spot.transform.childCount - 1; i >= 0; i--)
                {
                    var child = spot.transform.GetChild(i);
                    if (child.name.StartsWith("Tache_") || child.name == "Salete")
                        UnityEditor.Undo.DestroyObjectImmediate(child.gameObject);
                }
                var quad = CreateDirtQuad(spot.transform, spot.HalfSize);
                UnityEditor.Undo.RegisterCreatedObjectUndo(quad.gameObject, "Salete");
                UnityEditor.Undo.RecordObject(spot, "Saleté effaçable");
                spot.SetRenderer(quad);
                UnityEditor.EditorUtility.SetDirty(spot);
                count++;
            }
            Debug.Log($"[Sanitaire] {name} : {count} zones passées à la saleté effaçable. Enregistrer la scène (Ctrl+S).", this);
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
            ceramicMaterial = LoadOrCreate("Sanitaire_Ceramique", new Color(0.95f, 0.95f, 0.93f), 0f, 0.85f, false);
            basinMaterial = LoadOrCreate("Sanitaire_Vasque", new Color(0.82f, 0.84f, 0.86f), 0f, 0.9f, false);
            cabinetMaterial = LoadOrCreate("Sanitaire_Meuble", new Color(0.45f, 0.47f, 0.5f), 0f, 0.3f, false);
            chromeMaterial = LoadOrCreate("Sanitaire_Chrome", new Color(0.8f, 0.8f, 0.82f), 1f, 0.85f, false);
            mirrorMaterial = LoadOrCreate("Sanitaire_Miroir", new Color(0.62f, 0.72f, 0.78f), 0f, 0f, true);
            dirtMaterial = LoadOrCreate("Sanitaire_Salete", Color.white, 0f, 0.1f, false);
            // Découpe alpha : les endroits effacés deviennent invisibles (pas de transparence, plus sûr sur Quest).
            dirtMaterial.SetFloat("_AlphaClip", 1f);
            dirtMaterial.SetFloat("_Cutoff", 0.12f);
            dirtMaterial.EnableKeyword("_ALPHATEST_ON");
            dirtMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            UnityEditor.EditorUtility.SetDirty(dirtMaterial);
            UnityEditor.AssetDatabase.SaveAssets();
            spongeMaterial = LoadOrCreate("Eponge_Mousse", new Color(1f, 0.82f, 0.2f), 0f, 0.1f, false);
            scrubMaterial = LoadOrCreate("Eponge_Grattoir", new Color(0.2f, 0.6f, 0.3f), 0f, 0.1f, false);
        }

        private static Material LoadOrCreate(string name, Color color, float metallic, float smoothness, bool unlit)
        {
            const string folder = "Assets/_Project/Activities/Cleaning/Materials";
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                material = new Material(shader);
                UnityEditor.AssetDatabase.CreateAsset(material, path);
            }
            // Couleurs enregistrées dans le matériau lui-même (un bloc de propriétés serait perdu au rechargement).
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
        private static Transform Group(string name, Transform parent, Fixture fx)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = fx.position;
            go.transform.localRotation = Quaternion.Euler(0f, fx.yaw, 0f);
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

        private static void NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
        }

        private static TMP_Text Label(string name, Transform parent, Vector3 position, string text, float size, Vector2 box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.rectTransform.sizeDelta = box;
            go.SetActive(false);
            return tmp;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position - transform.forward * 1.5f);
            Gizmos.DrawWireSphere(transform.position, 0.1f);
        }
#endif
    }
}
