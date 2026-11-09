using JourneyThroughXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace JourneyThroughXR.UI
{
    public class TutorialWindow : MonoBehaviour
    {
        // Réglages
        [Header("Interface")]
        [Tooltip("Canvas de la fenêtre, masqué au lancement.")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Button confirmButton;

        [Header("Placement")]
        [Tooltip("Distance en mètres devant le visiteur.")]
        [SerializeField] private float distance = 1.2f;

        [Tooltip("Décalage vertical par rapport aux yeux.")]
        [SerializeField] private float heightOffset = -0.15f;

        [Tooltip("Angle en degrés entre le robot et la fenêtre, vus par le visiteur, pour que le robot ne la cache pas.")]
        [SerializeField] private float sideAngle = 40f;

        [Header("Commande")]
        [Tooltip("Bouton qui valide la fenêtre, comme le bouton « Compris ».")]
        [SerializeField] private InputActionProperty confirmAction = new InputActionProperty(
            new InputAction("Compris", InputActionType.Button, "<XRController>{RightHand}/primaryButton"));

        [Header("Événements")]
        public UnityEvent onShown = new UnityEvent();
        public UnityEvent onConfirmed = new UnityEvent();

        // État
        public bool IsOpen => panel != null && panel.activeSelf;
        public Activity CurrentActivity { get; private set; }

        private Transform visitor;
        private int shownFrame;

        // Cycle de vie
        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
        }

        private void OnEnable()
        {
            if (confirmAction.reference == null) confirmAction.action?.Enable();
        }

        private void OnDisable()
        {
            if (confirmAction.reference == null) confirmAction.action?.Disable();
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == shownFrame) return;
            var action = confirmAction.action;
            if (action != null && action.WasPressedThisFrame()) Confirm();
        }

        // Commandes
        public void Show(Activity activity) => Show(activity, null);

        public void Show(Activity activity, Transform avoid)
        {
            if (activity == null) return;
            CurrentActivity = activity;
            Open(activity.DisplayName, activity.TutorialText, avoid);
        }

        public void ShowText(string title, string body)
        {
            CurrentActivity = null;
            Open(title, body, null);
        }

        public void Confirm()
        {
            if (!IsOpen) return;
            panel.SetActive(false);

            Activity activity = CurrentActivity;
            CurrentActivity = null;
            if (activity != null) activity.StartActivity();
            onConfirmed.Invoke();
        }

        // Affichage
        private void Open(string title, string body, Transform avoid)
        {
            if (panel == null) return;
            if (titleText != null) titleText.text = title;
            if (bodyText != null) bodyText.text = body;

            PlaceInFrontOfVisitor(avoid);
            panel.SetActive(true);
            shownFrame = Time.frameCount;
            onShown.Invoke();
        }

        private void PlaceInFrontOfVisitor(Transform avoid)
        {
            if (!FindVisitor()) return;

            Vector3 forward = Flat(visitor.forward);
            if (avoid != null)
            {
                Vector3 toAvoid = Flat(avoid.position - visitor.position);
                float side = Vector3.SignedAngle(toAvoid, forward, Vector3.up) < 0f ? -1f : 1f;
                forward = Quaternion.AngleAxis(sideAngle * side, Vector3.up) * toAvoid;
            }

            panel.transform.position = visitor.position + forward * distance + Vector3.up * heightOffset;
            panel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
        }

        private bool FindVisitor()
        {
            if (visitor != null) return true;
            var mainCamera = Camera.main;
            if (mainCamera == null) return false;
            visitor = mainCamera.transform;
            return true;
        }

        // Outils de mise en place
        [ContextMenu("Créer la fenêtre")]
        private void CreateWindow()
        {
            if (panel != null)
            {
                Debug.LogWarning("[Tutoriel] La fenêtre existe déjà.", this);
                return;
            }

            var canvasObject = new GameObject("Fenetre", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(600f, 380f);
            canvasRect.localScale = Vector3.one * 0.001f;

            var background = AddImage(canvasObject.transform, "Fond", new Color32(0x1B, 0x22, 0x1D, 0xE6), "UI/Skin/UISprite.psd");
            Stretch(background.rectTransform);

            titleText = AddText(canvasObject.transform, "Titre", "Activité", 40f, new Vector2(0f, 140f), new Vector2(540f, 70f));
            titleText.fontStyle = FontStyles.Bold;

            bodyText = AddText(canvasObject.transform, "Consignes", "Consignes de l'activité.", 26f, new Vector2(0f, 10f), new Vector2(540f, 180f));
            bodyText.alignment = TextAlignmentOptions.TopLeft;
            bodyText.enableAutoSizing = true;
            bodyText.fontSizeMin = 18f;
            bodyText.fontSizeMax = 26f;

            var buttonImage = AddImage(canvasObject.transform, "Bouton_Compris", new Color32(0xFF, 0xFF, 0xFF, 0xFF), "UI/Skin/UISprite.psd");
            Place(buttonImage.rectTransform, new Vector2(0f, -140f), new Vector2(240f, 70f));
            buttonImage.raycastTarget = true;
            confirmButton = buttonImage.gameObject.AddComponent<Button>();

            var circle = AddImage(buttonImage.transform, "Bouton_A", new Color32(0x5C, 0xC2, 0x76, 0xFF), "UI/Skin/Knob.psd");
            Place(circle.rectTransform, new Vector2(-75f, 0f), new Vector2(46f, 46f));
            var letter = AddText(circle.transform, "Lettre", "A", 30f, Vector2.zero, new Vector2(46f, 46f));
            letter.fontStyle = FontStyles.Bold;
            letter.color = new Color32(0x1B, 0x22, 0x1D, 0xFF);

            var label = AddText(buttonImage.transform, "Texte", "Compris", 30f, new Vector2(25f, 0f), new Vector2(150f, 60f));
            label.color = new Color32(0x1B, 0x22, 0x1D, 0xFF);

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(canvasObject, "Créer la fenêtre de tutoriel");
            UnityEditor.Undo.RecordObject(this, "Créer la fenêtre de tutoriel");
#endif
            panel = canvasObject;
        }

        private static Image AddImage(Transform parent, string objectName, Color color, string builtinSprite)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
#if UNITY_EDITOR
            image.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>(builtinSprite);
            image.type = builtinSprite.Contains("Knob") ? Image.Type.Simple : Image.Type.Sliced;
#endif
            return image;
        }

        private static TextMeshProUGUI AddText(Transform parent, string objectName, string content, float size, Vector2 position, Vector2 dimensions)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            Place((RectTransform)textObject.transform, position, dimensions);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 dimensions)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Menus de test
        [ContextMenu("Test : Afficher")]
        private void DebugShow() => ShowText("Activité test",
            "Voici une fenêtre de tutoriel.\nAppuyez sur A ou visez le bouton « Compris » pour la fermer.");
    }
}
