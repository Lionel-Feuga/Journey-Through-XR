using JourneyThroughXR.Guide;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace JourneyThroughXR.UI
{
    public class WristMap : MonoBehaviour
    {
        // Réglages
        [Header("Interface")]
        [Tooltip("Canvas de la carte, masqué tant que le bouton n'est pas appuyé.")]
        [SerializeField] private GameObject mapRoot;
        [SerializeField] private RectTransform mapArea;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private RectTransform robotMarker;

        [Tooltip("Image du plan de l'étage.")]
        [SerializeField] private Texture2D mapTexture;

        [Header("Suivi")]
        [Tooltip("Manette gauche. Vide : l'objet « Left Controller » est trouvé seul.")]
        [SerializeField] private Transform hand;

        [Tooltip("Robot affiché sur la carte. Vide : le robot de la scène est trouvé seul.")]
        [SerializeField] private Transform robot;

        [Header("Placement")]
        [Tooltip("Hauteur de la carte au-dessus de la manette, en mètres.")]
        [SerializeField] private float heightAboveHand = 0.13f;

        [Header("Correspondance plan / monde")]
        [Tooltip("Coordonnées monde (x, z) du coin bas gauche de l'image.")]
        [SerializeField] private Vector2 worldMin = new Vector2(-40.343f, -19.386f);

        [Tooltip("Coordonnées monde (x, z) du coin haut droit de l'image.")]
        [SerializeField] private Vector2 worldMax = new Vector2(0.823f, 0.396f);

        [Header("Commande")]
        [Tooltip("Bouton qui affiche la carte : gâchette inférieure (grip) de la manette gauche.")]
        [SerializeField] private InputActionProperty showAction = new InputActionProperty(
            new InputAction("Carte", InputActionType.Button, "<XRController>{LeftHand}/gripPressed"));

        [Tooltip("Coché : un appui ouvre, un autre ferme. Décoché : la carte reste visible tant que le bouton est maintenu.")]
        [SerializeField] private bool toggle;

        [Header("Animation")]
        [Tooltip("Durée d'ouverture et de fermeture en secondes.")]
        [SerializeField] private float openDuration = 0.15f;

        // État
        public bool IsOpen => wanted;

        private Transform visitor;
        private Vector3 baseScale = Vector3.one;
        private CanvasGroup group;
        private float openAmount;
        private bool wanted;
        private bool forced;

        // Cycle de vie
        private void Awake()
        {
            if (mapRoot != null)
            {
                baseScale = mapRoot.transform.localScale;
                group = mapRoot.GetComponent<CanvasGroup>();
                mapRoot.SetActive(false);
            }
            if (hand == null)
            {
                var found = GameObject.Find("Left Controller");
                if (found != null) hand = found.transform;
            }
            if (robot == null)
            {
                var guide = FindAnyObjectByType<GuideRobot>();
                if (guide != null) robot = guide.transform;
            }
        }

        private void OnEnable()
        {
            if (showAction.reference == null) showAction.action?.Enable();
        }

        private void OnDisable()
        {
            if (showAction.reference == null) showAction.action?.Disable();
        }

        private void LateUpdate()
        {
            if (mapRoot == null || hand == null) return;

            ReadInput();
            float step = openDuration > 0f ? Time.deltaTime / openDuration : 1f;
            openAmount = Mathf.MoveTowards(openAmount, wanted ? 1f : 0f, step);

            if (openAmount <= 0f)
            {
                if (mapRoot.activeSelf) mapRoot.SetActive(false);
                return;
            }

            if (!mapRoot.activeSelf) mapRoot.SetActive(true);
            Place();
            Animate();
            UpdateMarkers();
        }

        // Commandes
        public void Show() => forced = wanted = true;
        public void Hide() => forced = wanted = false;

        private void ReadInput()
        {
            var action = showAction.action;
            if (action == null) return;
            if (toggle)
            {
                if (action.WasPressedThisFrame()) wanted = !wanted;
            }
            else
            {
                wanted = forced || action.IsPressed();
            }
        }

        // Affichage
        private void Place()
        {
            Vector3 position = hand.position + Vector3.up * heightAboveHand;
            mapRoot.transform.position = position;
            if (FindVisitor())
            {
                Vector3 direction = position - visitor.position;
                if (direction.sqrMagnitude > 0.0001f)
                    mapRoot.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }

        private void Animate()
        {
            float ease = Mathf.SmoothStep(0f, 1f, openAmount);
            mapRoot.transform.localScale = new Vector3(baseScale.x, baseScale.y * ease, baseScale.z);
            if (group != null)
            {
                float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 12f, 0f);
                group.alpha = ease * flicker;
            }
        }

        private void UpdateMarkers()
        {
            if (mapArea == null) return;

            if (playerMarker != null && FindVisitor())
            {
                playerMarker.anchoredPosition = ToMap(visitor.position);
                Vector3 forward = visitor.forward;
                float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                playerMarker.localEulerAngles = new Vector3(0f, 0f, -heading);
            }

            if (robotMarker != null)
            {
                robotMarker.gameObject.SetActive(robot != null);
                if (robot != null) robotMarker.anchoredPosition = ToMap(robot.position);
            }
        }

        private Vector2 ToMap(Vector3 world)
        {
            float u = Mathf.InverseLerp(worldMin.x, worldMax.x, world.x);
            float v = Mathf.InverseLerp(worldMin.y, worldMax.y, world.z);
            Rect rect = mapArea.rect;
            return new Vector2((u - 0.5f) * rect.width, (v - 0.5f) * rect.height);
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
        [ContextMenu("Créer la carte")]
        private void CreateMap()
        {
            if (mapRoot != null)
            {
                Debug.LogWarning("[Carte] La carte existe déjà.", this);
                return;
            }

            var canvasObject = new GameObject("Carte", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasGroup = canvasObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(1024f, 512f);
            canvasRect.localScale = Vector3.one * 0.00028f;

            var background = AddImage(canvasObject.transform, "Fond", new Color32(0x0A, 0x1E, 0x2D, 0x8C), "UI/Skin/UISprite.psd");
            Stretch(background.rectTransform, -16f);

            var planObject = new GameObject("Plan", typeof(RectTransform), typeof(RawImage));
            planObject.transform.SetParent(canvasObject.transform, false);
            var plan = planObject.GetComponent<RawImage>();
            plan.texture = mapTexture;
            plan.raycastTarget = false;
            Stretch(plan.rectTransform, 0f);

            var robotImage = AddImage(planObject.transform, "Robot", new Color32(0xF2, 0x26, 0x73, 0xFF), "UI/Skin/Knob.psd");
            robotImage.rectTransform.sizeDelta = new Vector2(26f, 26f);

            var playerImage = AddImage(planObject.transform, "Visiteur", new Color32(0xFF, 0xFF, 0xFF, 0xFF), "UI/Skin/Knob.psd");
            playerImage.rectTransform.sizeDelta = new Vector2(30f, 30f);
            var nose = AddImage(playerImage.transform, "Direction", new Color32(0xFF, 0xFF, 0xFF, 0xFF), "UI/Skin/UISprite.psd");
            nose.rectTransform.sizeDelta = new Vector2(8f, 24f);
            nose.rectTransform.anchoredPosition = new Vector2(0f, 22f);

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(canvasObject, "Créer la carte");
            UnityEditor.Undo.RecordObject(this, "Créer la carte");
#endif
            mapRoot = canvasObject;
            mapArea = plan.rectTransform;
            playerMarker = playerImage.rectTransform;
            robotMarker = robotImage.rectTransform;
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

        private static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        // Menus de test
        [ContextMenu("Test : Afficher ou masquer")]
        private void DebugToggle()
        {
            if (wanted) Hide();
            else Show();
        }
    }
}
