using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Une zone sale (lavabo, WC ou mur) : une texture de saleté posée à plat sur la surface.
    /// Comme une gomme : l'éponge efface la saleté là où elle passe, un peu plus à chaque passage.
    /// Axe bleu (Z) de l'objet : la normale de la surface, vers le visiteur.
    /// La saleté est générée au lancement (motif irrégulier, propre à chaque zone).
    /// </summary>
    public class DirtSpot : MonoBehaviour
    {
        // Réglages
        [Tooltip("Nom dit par le robot dans les aides (ex. « le lavabo (mur de la porte) »).")]
        [SerializeField] private string hintName = "la tache";

        [Tooltip("Nom court affiché sur le compteur (ex. Lavabo).")]
        [SerializeField] private string shortName = "Tache";

        [Tooltip("Demi-largeur (X) et demi-hauteur (Y) de la zone, en mètres.")]
        [SerializeField] private Vector2 halfSize = new Vector2(0.2f, 0.15f);

        [Tooltip("Quad qui affiche la saleté (créé par le menu de la pièce).")]
        [SerializeField] private Renderer dirtRenderer;

        [Header("Effacement")]
        [Tooltip("Rayon de l'éponge (m) : surface effacée autour de son centre.")]
        [SerializeField] private float brushRadius = 0.055f;

        [Tooltip("Force d'effacement : plus c'est haut, moins il faut de passages (environ 3 passages à 6).")]
        [SerializeField] private float rubStrength = 6f;

        [Tooltip("Distance (m) devant ou derrière la surface où l'éponge compte comme posée dessus.")]
        [SerializeField] private float contactDepth = 0.09f;

        [Tooltip("Part de saleté restante (0 à 1) sous laquelle la zone est déclarée propre (le reste disparaît).")]
        [SerializeField, Range(0f, 0.3f)] private float doneBelow = 0.06f;

        [Tooltip("Résolution de la texture (pixels par mètre).")]
        [SerializeField] private int pixelsPerMeter = 220;

        [Header("Aspect")]
        [SerializeField] private Color lightDirt = new Color(0.55f, 0.45f, 0.3f);
        [SerializeField] private Color darkDirt = new Color(0.25f, 0.18f, 0.1f);
        [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f);

        [Tooltip("Texte « Propre ! » affiché quelques secondes une fois la zone nettoyée.")]
        [SerializeField] private TMP_Text cleanLabel;

        [Header("Événements")]
        public UnityEvent onCleaned = new UnityEvent();

        // État
        public event Action<DirtSpot> Rubbed;
        public event Action<DirtSpot> Cleaned;

        public string HintName => hintName;
        public string ShortName => shortName;
        public bool IsClean { get; private set; }
        public bool Highlighted { get; private set; }
        /// Part de saleté restante, de 1 (sale) à 0 (propre).
        public float Remaining => initialTotal > 0f ? currentTotal / initialTotal : 0f;

        private const float AlphaCutoff = 0.12f;

        private Texture2D texture;
        private Material material;
        private float[] dirt;
        private float[] initialDirt;
        private Color32[] pixels;
        private int width, height;
        private float initialTotal, currentTotal;
        private Vector3 lastLocal;
        private bool wasInContact;
        private bool dirty;
        private float cleanLabelUntil;

        // Configuration par le menu
        public void Configure(string shortLabel, string hint, Vector2 half, Renderer quad, TMP_Text label)
        {
            shortName = shortLabel;
            hintName = hint;
            halfSize = half;
            dirtRenderer = quad;
            cleanLabel = label;
        }

        public void SetRenderer(Renderer quad) => dirtRenderer = quad;
        public Vector2 HalfSize => halfSize;

        // Cycle de vie
        private void Awake()
        {
            // Anciennes versions : plus de nettoyage à distance, on coupe le collider de visée.
            var aim = GetComponent<Collider>();
            if (aim != null) aim.enabled = false;
            foreach (var b in GetComponents<Behaviour>())
                if (b != this && b.GetType().Name == "XRSimpleInteractable") b.enabled = false;

            if (cleanLabel != null) cleanLabel.gameObject.SetActive(false);
            CreateTexture();
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (material != null) Destroy(material);
        }

        private void Update()
        {
            if (cleanLabel != null && cleanLabel.gameObject.activeSelf)
            {
                if (Time.time > cleanLabelUntil) cleanLabel.gameObject.SetActive(false);
                else FaceCamera(cleanLabel.transform);
            }
            UpdateHighlight();
        }

        private void LateUpdate()
        {
            if (!dirty || texture == null) return;
            dirty = false;
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        // Saleté
        private void CreateTexture()
        {
            if (dirtRenderer == null) return;
            width = Mathf.Clamp(Mathf.RoundToInt(halfSize.x * 2f * pixelsPerMeter), 16, 256);
            height = Mathf.Clamp(Mathf.RoundToInt(halfSize.y * 2f * pixelsPerMeter), 16, 256);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Salete_" + name
            };
            dirt = new float[width * height];
            pixels = new Color32[width * height];

            // Motif irrégulier : bruit sur plusieurs échelles, plus dense au centre de la zone.
            int seed = ((transform.position * 100f).GetHashCode() & 0x7fffffff) % 1000;
            float ox = seed * 1.37f, oy = seed * 2.11f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                float mx = u * halfSize.x * 2f, my = v * halfSize.y * 2f; // en mètres
                float n = 0.55f * Mathf.PerlinNoise(ox + mx * 9f, oy + my * 9f)
                        + 0.3f * Mathf.PerlinNoise(ox + mx * 25f + 40f, oy + my * 25f + 40f)
                        + 0.15f * Mathf.PerlinNoise(ox + mx * 70f + 90f, oy + my * 70f + 90f);
                float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
                float edge = Mathf.Clamp01(1.15f - Mathf.Sqrt(dx * dx + dy * dy)); // ovale qui s'estompe vers les bords
                float d = Mathf.Clamp01((n - 0.35f) * 2.2f) * Mathf.Clamp01(edge * 2.5f);
                dirt[y * width + x] = d;
            }
            initialDirt = (float[])dirt.Clone();
            initialTotal = 0f;
            for (int i = 0; i < dirt.Length; i++)
            {
                initialTotal += Visible(dirt[i]);
                pixels[i] = ToColor(dirt[i]);
            }
            currentTotal = initialTotal;
            texture.SetPixels32(pixels);
            texture.Apply(false);

            material = dirtRenderer.material; // copie propre à cette zone
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
        }

        private static float Visible(float d) => d > AlphaCutoff ? d - AlphaCutoff : 0f;

        private Color32 ToColor(float d)
        {
            Color c = Color.Lerp(lightDirt, darkDirt, d);
            c.a = d;
            return c;
        }

        // Frottement : appelé chaque image par l'éponge tenue en main.
        public void RubWith(Vector3 spongeWorld)
        {
            if (IsClean || dirt == null) return;
            Vector3 local = transform.InverseTransformPoint(spongeWorld);
            bool inContact = Mathf.Abs(local.z) <= contactDepth
                             && Mathf.Abs(local.x) <= halfSize.x + brushRadius
                             && Mathf.Abs(local.y) <= halfSize.y + brushRadius;
            if (!inContact)
            {
                wasInContact = false;
                return;
            }
            if (!wasInContact)
            {
                wasInContact = true;
                lastLocal = local;
                return;
            }

            float moved = new Vector2(local.x - lastLocal.x, local.y - lastLocal.y).magnitude;
            lastLocal = local;
            if (moved < 0.0005f || moved > 0.2f) return; // immobile, ou saut de suivi

            // Position de l'éponge dans la texture (via le quad, pour suivre son orientation).
            Vector3 q = dirtRenderer.transform.InverseTransformPoint(spongeWorld);
            float cu = (q.x + 0.5f) * width, cv = (q.y + 0.5f) * height;
            float rx = brushRadius / (halfSize.x * 2f) * width;
            float ry = brushRadius / (halfSize.y * 2f) * height;
            float strength = moved * rubStrength;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cu - rx)), x1 = Mathf.Min(width - 1, Mathf.CeilToInt(cu + rx));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cv - ry)), y1 = Mathf.Min(height - 1, Mathf.CeilToInt(cv + ry));
            bool changed = false;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cu) / rx, dy = (y + 0.5f - cv) / ry;
                float r2 = dx * dx + dy * dy;
                if (r2 > 1f) continue;
                int i = y * width + x;
                float before = dirt[i];
                if (before <= 0f) continue;
                float after = Mathf.Max(0f, before - strength * (1f - 0.6f * r2)); // plus fort au centre de l'éponge
                dirt[i] = after;
                currentTotal -= Visible(before) - Visible(after);
                pixels[i] = ToColor(after);
                changed = true;
            }

            if (!changed) return;
            dirty = true;
            Rubbed?.Invoke(this);
            if (Remaining <= doneBelow) CleanNow();
        }

        public void CleanNow()
        {
            if (IsClean) return;
            if (dirt != null)
            {
                for (int i = 0; i < dirt.Length; i++)
                {
                    dirt[i] = 0f;
                    pixels[i] = ToColor(0f);
                }
                currentTotal = 0f;
                dirty = true;
            }
            MarkClean();
        }

        public void SetHighlighted(bool on) => Highlighted = on;

        public void ResetDirt()
        {
            IsClean = false;
            Highlighted = false;
            wasInContact = false;
            if (dirt != null)
            {
                Array.Copy(initialDirt, dirt, dirt.Length);
                for (int i = 0; i < dirt.Length; i++) pixels[i] = ToColor(dirt[i]);
                currentTotal = initialTotal;
                dirty = true;
            }
            if (dirtRenderer != null) dirtRenderer.enabled = true;
            if (cleanLabel != null) cleanLabel.gameObject.SetActive(false);
        }

        // État interne
        private void MarkClean()
        {
            IsClean = true;
            Highlighted = false;
            if (cleanLabel != null)
            {
                cleanLabel.gameObject.SetActive(true);
                cleanLabelUntil = Time.time + 2.5f;
            }
            Cleaned?.Invoke(this);
            onCleaned.Invoke();
        }

        private void UpdateHighlight()
        {
            if (material == null) return;
            float t = Highlighted && !IsClean ? 0.5f + 0.5f * Mathf.Sin(Time.time * 5f) : 0f;
            Color c = Color.Lerp(Color.white, highlightColor * 1.6f, t * 0.7f);
            material.SetColor("_BaseColor", c);
        }

        private static void FaceCamera(Transform t)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = t.position - cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfSize.x * 2f, halfSize.y * 2f, contactDepth * 2f));
        }
#endif
    }
}
