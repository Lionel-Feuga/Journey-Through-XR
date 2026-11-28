using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Bouton « Mode assis : » sous la carte du poignet, à droite, avec une croix rouge (debout) ou une coche verte (assis).
    /// Carte ouverte (grip gauche maintenu), viser le bouton avec la manette droite et appuyer sur la gâchette.
    /// Utile si le visiteur se lève ou s'assoit en cours de visite.
    /// À poser sur l'objet qui porte « Wrist Map ». Menu du composant : « Créer le bouton Mode assis ».
    /// </summary>
    public class WristHeightToggle : MonoBehaviour
    {
        // Réglages
        [Tooltip("Canvas de la carte (objet « Carte »). Vide : trouvé seul sous cet objet.")]
        [SerializeField] private GameObject mapRoot;

        [Tooltip("Bouton créé par le menu.")]
        [SerializeField] private Button button;

        [Tooltip("Texte du bouton.")]
        [SerializeField] private TMP_Text label;

        [Tooltip("Croix ou coche du bouton.")]
        [SerializeField] private SeatedIcon icon;

        [Tooltip("Hauteur du joueur. Vide : trouvée seule dans la scène.")]
        [SerializeField] private PlayerHeight playerHeight;

        private CanvasGroup mapGroup;
        private CanvasGroup ownGroup;

        private void Awake()
        {
            if (mapRoot == null)
            {
                var t = transform.Find("Carte");
                if (t != null) mapRoot = t.gameObject;
            }
            if (playerHeight == null) playerHeight = FindAnyObjectByType<PlayerHeight>();
            if (mapRoot != null) mapGroup = mapRoot.GetComponent<CanvasGroup>();
            if (button != null)
            {
                ownGroup = button.GetComponentInParent<CanvasGroup>();
                button.onClick.AddListener(OnClick);
            }
            if (playerHeight == null) Debug.LogWarning("[Hauteur] Pas de Player Height dans la scène : l'ajouter sur le XR Origin.", this);
        }

        private void OnEnable()
        {
            if (playerHeight != null) playerHeight.Changed += OnChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (playerHeight != null) playerHeight.Changed -= OnChanged;
        }

        // Le bouton suit le fondu de la carte (son propre groupe ignore celui de la carte pour rester cliquable).
        private void LateUpdate()
        {
            if (ownGroup != null && mapGroup != null) ownGroup.alpha = mapGroup.alpha;
        }

        private void OnClick()
        {
            if (playerHeight != null) playerHeight.Toggle();
        }

        private void OnChanged(bool seated) => Refresh();

        private void Refresh()
        {
            bool seated = playerHeight != null && playerHeight.IsSeated;
            if (icon != null) icon.Set(seated);
        }

#if UNITY_EDITOR
        [ContextMenu("Créer le bouton Mode assis")]
        private void CreateButton()
        {
            if (mapRoot == null)
            {
                var t = transform.Find("Carte");
                if (t != null) mapRoot = t.gameObject;
            }
            if (mapRoot == null)
            {
                Debug.LogWarning("[Hauteur] Pas de carte « Carte » sous cet objet : créer d'abord la carte (menu du Wrist Map).", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Créer le bouton Mode assis");
            var old = mapRoot.transform.Find("Bouton_Hauteur");
            if (old != null) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);

            // Canvas à part, sous la carte : il a son propre raycaster pour être cliquable avec le rayon.
            var go = new GameObject("Bouton_Hauteur", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup),
                typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(mapRoot.transform, false);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Bouton_Hauteur");
            var group = go.GetComponent<CanvasGroup>();
            group.ignoreParentGroups = true; // la carte n'est pas cliquable, ce bouton oui
            group.interactable = true;
            group.blocksRaycasts = true;
            var rect = (RectTransform)go.transform;
            // Sous la carte, aligné sur son bord droit : la manette droite qui vise ne cache pas le texte.
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(0f, -30f);
            rect.sizeDelta = new Vector2(330f, 90f);

            var bg = new GameObject("Fond", typeof(RectTransform), typeof(Image), typeof(Button));
            bg.transform.SetParent(go.transform, false);
            var bgRect = (RectTransform)bg.transform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
            var image = bg.GetComponent<Image>();
            image.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = new Color32(0x0A, 0x1E, 0x2D, 0xE6);
            button = bg.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.75f, 0.9f, 1f);
            colors.pressedColor = new Color(0.55f, 0.75f, 0.9f);
            button.colors = colors;

            var textGo = new GameObject("Texte", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(bg.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 6f);
            textRect.offsetMax = new Vector2(-80f, -6f); // place pour l'icône à droite
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableAutoSizing = false;
            tmp.fontSize = 42f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            tmp.text = "Mode assis :";
            label = tmp;
            icon = SeatedIcon.Create(bg.transform, new Vector2(330f / 2f - 45f, 0f), 50f);

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[Hauteur] Bouton Assis / Debout créé sous la carte. Enregistrer la scène (Ctrl+S).", this);
        }
#endif
    }
}
