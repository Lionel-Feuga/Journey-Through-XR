using UnityEngine;
using UnityEngine.UI;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Icône du mode assis : croix rouge (désactivé) ou coche verte (activé).
    /// Dessinée avec de simples barres d'interface : pas besoin de police ni d'image.
    /// Créée par les menus des boutons (Height Choice Panel, Wrist Height Toggle).
    /// </summary>
    public class SeatedIcon : MonoBehaviour
    {
        [SerializeField] private GameObject cross;
        [SerializeField] private GameObject check;

        /// Échelle de l'icône à sa création (0,8 : réglée par Lionel dans le casque).
        public const float DefaultScale = 0.8f;

        public static readonly Color CrossColor = new Color32(0xD9, 0x30, 0x30, 0xFF);
        public static readonly Color CheckColor = new Color32(0x2E, 0xB8, 0x4F, 0xFF);

        public void Set(bool seated)
        {
            if (cross != null) cross.SetActive(!seated);
            if (check != null) check.SetActive(seated);
        }

        /// Crée l'icône (carré de « size » unités d'interface) sous « parent », centrée sur « center ».
        public static SeatedIcon Create(Transform parent, Vector2 center, float size)
        {
            var go = new GameObject("Icone_Assis", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = new Vector2(size, size);
            rect.localScale = new Vector3(DefaultScale, DefaultScale, 1f);

            float t = size * 0.16f; // épaisseur des traits

            var cross = Group("Croix", go.transform);
            Bar(cross, new Vector2(-0.38f, -0.38f) * size, new Vector2(0.38f, 0.38f) * size, t, CrossColor);
            Bar(cross, new Vector2(-0.38f, 0.38f) * size, new Vector2(0.38f, -0.38f) * size, t, CrossColor);

            var check = Group("Coche", go.transform);
            Vector2 corner = new Vector2(-0.12f, -0.32f) * size;
            Bar(check, new Vector2(-0.42f, 0.02f) * size, corner, t, CheckColor);
            Bar(check, corner, new Vector2(0.42f, 0.38f) * size, t, CheckColor);

            var icon = go.AddComponent<SeatedIcon>();
            icon.cross = cross.gameObject;
            icon.check = check.gameObject;
            icon.Set(false);
            return icon;
        }

        private static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            return go.transform;
        }

        // Une barre d'un point à l'autre, bouts arrondis par le recouvrement des deux barres.
        private static void Bar(Transform parent, Vector2 from, Vector2 to, float thickness, Color color)
        {
            var go = new GameObject("Trait", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            Vector2 d = to - from;
            rect.anchoredPosition = (from + to) * 0.5f;
            rect.sizeDelta = new Vector2(d.magnitude + thickness * 0.5f, thickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }
}
