using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace JourneyThroughXR.Guide
{
    public class GuideSpeechBubble : MonoBehaviour
    {
        // Réglages
        [Header("Bulle")]
        [SerializeField] private GameObject bubble;
        [SerializeField] private TMP_Text label;

        [Tooltip("Symbole du bouton A, affiché quand la phrase est entièrement écrite.")]
        [SerializeField] private GameObject nextPrompt;

        [Tooltip("La bulle se tourne toujours vers le visiteur.")]
        [SerializeField] private bool faceVisitor = true;

        [Header("Rythme")]
        [Tooltip("Secondes par caractère pour l'effet machine à écrire. 0 : texte affiché d'un coup.")]
        [SerializeField] private float typingDelay = 0.03f;

        [Header("Commande")]
        [Tooltip("Bouton qui affiche la phrase en entier, puis passe à la suivante.")]
        [SerializeField] private InputActionProperty nextAction = new InputActionProperty(
            new InputAction("Dialogue suivant", InputActionType.Button, "<XRController>{RightHand}/primaryButton"));

        [Header("Événements")]
        public UnityEvent onDialogueStarted = new UnityEvent();
        public UnityEvent onDialogueFinished = new UnityEvent();

        // État
        public bool IsSpeaking => speaking != null;

        private Transform visitor;
        private Coroutine speaking;

        // Cycle de vie
        private void Awake() => SetVisible(false);

        private void OnEnable()
        {
            if (nextAction.reference == null) nextAction.action?.Enable();
        }

        private void OnDisable()
        {
            if (nextAction.reference == null) nextAction.action?.Disable();
        }

        private void LateUpdate()
        {
            if (bubble == null || !bubble.activeSelf) return;
            if (faceVisitor) FaceVisitor();
            if (nextPrompt != null && nextPrompt.activeSelf)
                nextPrompt.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 6f));
        }

        // Commandes
        public void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            SayLines(new[] { text });
        }

        public void SayLines(IList<string> lines)
        {
            if (lines == null || lines.Count == 0 || label == null) return;
            if (speaking != null) StopCoroutine(speaking);
            speaking = StartCoroutine(Speak(lines));
        }

        public void Hide()
        {
            if (speaking != null) StopCoroutine(speaking);
            speaking = null;
            SetVisible(false);
        }

        // Dialogue
        private IEnumerator Speak(IList<string> lines)
        {
            onDialogueStarted.Invoke();
            SetVisible(true);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                SetPrompt(false);
                label.text = line;
                yield return TypeLine();

                SetPrompt(true);
                yield return null;
                while (!NextPressed()) yield return null;
                yield return null;
            }

            SetVisible(false);
            speaking = null;
            onDialogueFinished.Invoke();
        }

        private IEnumerator TypeLine()
        {
            label.ForceMeshUpdate();
            int total = label.textInfo.characterCount;
            label.maxVisibleCharacters = typingDelay > 0f ? 0 : total;

            float elapsed = 0f;
            while (label.maxVisibleCharacters < total)
            {
                yield return null;
                if (NextPressed())
                {
                    label.maxVisibleCharacters = total;
                    break;
                }
                elapsed += Time.deltaTime;
                label.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(elapsed / typingDelay));
            }
        }

        private bool NextPressed()
        {
            var action = nextAction.action;
            return action != null && action.WasPressedThisFrame();
        }

        // Affichage
        private void SetVisible(bool visible)
        {
            if (bubble != null) bubble.SetActive(visible);
            if (!visible) SetPrompt(false);
        }

        private void SetPrompt(bool visible)
        {
            if (nextPrompt != null) nextPrompt.SetActive(visible);
        }

        private void FaceVisitor()
        {
            if (visitor == null)
            {
                var mainCamera = Camera.main;
                if (mainCamera == null) return;
                visitor = mainCamera.transform;
            }

            Vector3 direction = bubble.transform.position - visitor.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            bubble.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        // Outils de mise en place
        [ContextMenu("Créer la bulle")]
        private void CreateBubble()
        {
            if (bubble != null)
            {
                Debug.LogWarning("[Bulle] La bulle existe déjà.", this);
                return;
            }

            var canvasObject = new GameObject("Bulle", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(520f, 170f);
            canvasRect.localPosition = new Vector3(0f, 1.85f, 0f);
            canvasRect.localScale = Vector3.one * 0.001f;

            var background = new GameObject("Fond", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            Stretch((RectTransform)background.transform, 0f);
            var image = background.GetComponent<Image>();
            image.color = new Color32(0x1B, 0x22, 0x1D, 0xE6);
            image.raycastTarget = false;
#if UNITY_EDITOR
            image.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
#endif

            var textObject = new GameObject("Texte", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);
            Stretch((RectTransform)textObject.transform, 24f);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = "Bonjour et bienvenue à Epitech Nice !";
            text.fontSize = 30f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18f;
            text.fontSizeMax = 30f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(canvasObject, "Créer la bulle");
            UnityEditor.Undo.RecordObject(this, "Créer la bulle");
#endif
            bubble = canvasObject;
            label = text;
            CreatePrompt();
        }

        [ContextMenu("Créer le symbole du bouton A")]
        private void CreatePrompt()
        {
            if (bubble == null)
            {
                Debug.LogWarning("[Bulle] Créer d'abord la bulle.", this);
                return;
            }
            if (nextPrompt != null)
            {
                Debug.LogWarning("[Bulle] Le symbole du bouton A existe déjà.", this);
                return;
            }

            var promptObject = new GameObject("Bouton_A", typeof(RectTransform), typeof(Image));
            promptObject.transform.SetParent(bubble.transform, false);
            var promptRect = (RectTransform)promptObject.transform;
            promptRect.anchorMin = new Vector2(1f, 0f);
            promptRect.anchorMax = new Vector2(1f, 0f);
            promptRect.pivot = new Vector2(0.5f, 0.5f);
            promptRect.sizeDelta = new Vector2(46f, 46f);
            promptRect.anchoredPosition = new Vector2(-30f, 30f);
            var circle = promptObject.GetComponent<Image>();
            circle.color = new Color32(0x5C, 0xC2, 0x76, 0xFF);
            circle.raycastTarget = false;
#if UNITY_EDITOR
            circle.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
#endif

            var letterObject = new GameObject("Lettre", typeof(RectTransform), typeof(TextMeshProUGUI));
            letterObject.transform.SetParent(promptObject.transform, false);
            Stretch((RectTransform)letterObject.transform, 0f);
            var letter = letterObject.GetComponent<TextMeshProUGUI>();
            letter.text = "A";
            letter.fontSize = 30f;
            letter.fontStyle = FontStyles.Bold;
            letter.alignment = TextAlignmentOptions.Center;
            letter.color = new Color32(0x1B, 0x22, 0x1D, 0xFF);
            letter.raycastTarget = false;

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(promptObject, "Créer le symbole du bouton A");
            UnityEditor.Undo.RecordObject(this, "Créer le symbole du bouton A");
#endif
            nextPrompt = promptObject;
        }

        private static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        [ContextMenu("Remplir les dialogues des étapes")]
        private void FillStopDialogues()
        {
            var robot = GetComponent<GuideRobot>();
            if (robot == null || robot.Route == null)
            {
                Debug.LogWarning("[Bulle] Le robot ou son parcours est introuvable.", this);
                return;
            }

            int filled = 0;
            foreach (var waypoint in robot.Route.Waypoints)
            {
                if (!waypoint.IsStop || waypoint.HasDialogue) continue;
                if (!DefaultDialogues.TryGetValue(waypoint.name, out string[] lines)) continue;
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(waypoint, "Remplir les dialogues");
#endif
                waypoint.SetDialogue(lines);
                filled++;
            }

            Debug.Log($"[Bulle] {filled} étapes remplies. Les étapes qui avaient déjà un dialogue n'ont pas été modifiées.", this);
        }

        // Dialogues par défaut
        private static readonly Dictionary<string, string[]> DefaultDialogues = new Dictionary<string, string[]>
        {
            { "Accueil", new[] {
                "Bonjour et bienvenue à Epitech Nice !",
                "Je suis votre guide. Suivez-moi, je vous fais visiter l'étage." } },
            { "PC", new[] {
                "Voici l'open-space. Les étudiants y travaillent sur leurs projets.",
                "Approchez-vous de l'ordinateur pour essayer l'activité." } },
            { "Dessin", new[] {
                "Bienvenue dans la salle 307.",
                "Ici, place à la créativité : essayez de dessiner !" } },
            { "Nettoyage", new[] {
                "Un petit détour par les sanitaires...",
                "Même ici, il y a du travail : à vous de faire briller les lieux !" } },
            { "Voiture_RC", new[] {
                "Salle 305 : place au pilotage !",
                "Prenez les commandes de la voiture radiocommandée." } },
            { "Sabre_Echecs", new[] {
                "Dernière étape : la salle 301.",
                "Sabre laser ou partie d'échecs, à vous de choisir !" } },
            { "Fin", new[] {
                "La visite est terminée. Merci de m'avoir suivi !" } }
        };
    }
}
