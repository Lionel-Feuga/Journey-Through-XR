using System.Text;
using JourneyThroughXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace JourneyThroughXR.UI
{
    public class EndScreen : MonoBehaviour
    {
        // Réglages
        [Header("Interface")]
        [Tooltip("Canvas de l'écran de fin, masqué au lancement.")]
        [SerializeField] private GameObject panel;

        [Tooltip("Texte du récapitulatif des activités.")]
        [SerializeField] private TMP_Text summaryText;

        [Header("Placement")]
        [Tooltip("Distance en mètres devant le visiteur.")]
        [SerializeField] private float distance = 1.2f;

        [Tooltip("Décalage vertical par rapport aux yeux.")]
        [SerializeField] private float heightOffset = -0.15f;

        [Header("Continuer à explorer")]
        [Tooltip("Réaffiche l'écran quand le visiteur revient au point de fin.")]
        [SerializeField] private bool reopenWhenBack = true;

        [Tooltip("Distance à partir de laquelle le visiteur est considéré comme parti.")]
        [SerializeField] private float leaveDistance = 4f;

        [Tooltip("Distance à laquelle l'écran réapparaît à son retour.")]
        [SerializeField] private float returnDistance = 2f;

        [Header("Événements")]
        public UnityEvent onShown = new UnityEvent();
        public UnityEvent onContinue = new UnityEvent();

        // État
        private Transform visitor;
        private Vector3 anchor;
        private bool armed;
        private bool visitorLeft;

        // Cycle de vie
        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void Update()
        {
            if (!armed || !reopenWhenBack || panel == null || panel.activeSelf || !FindVisitor()) return;

            float horizontal = HorizontalDistance(visitor.position, anchor);
            if (!visitorLeft)
            {
                if (horizontal > leaveDistance) visitorLeft = true;
            }
            else if (horizontal < returnDistance)
            {
                Show();
            }
        }

        // Affichage
        public void Show()
        {
            if (panel == null || !FindVisitor()) return;

            PlaceInFrontOfVisitor();
            RefreshSummary();
            panel.SetActive(true);

            anchor = visitor.position;
            armed = true;
            visitorLeft = false;
            onShown.Invoke();
        }

        private void PlaceInFrontOfVisitor()
        {
            Vector3 forward = visitor.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 position = visitor.position + forward * distance;
            position.y = visitor.position.y + heightOffset;
            panel.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        private void RefreshSummary()
        {
            if (summaryText == null) return;

            var text = new StringBuilder();
            if (ProgressManager.TotalCount == 0)
            {
                text.Append("Merci d'avoir suivi la visite d'Epitech Nice !");
            }
            else
            {
                text.AppendLine($"Activités réalisées : {ProgressManager.CompletedCount} / {ProgressManager.TotalCount}");
                text.AppendLine();
                foreach (var activity in ProgressManager.Activities)
                {
                    string status = activity.IsCompleted
                        ? "<color=#5CC276>terminée</color>"
                        : "<color=#9AA89F>non faite</color>";
                    text.AppendLine($"{activity.DisplayName} : {status}");
                }
            }
            summaryText.text = text.ToString();
        }

        // Boutons
        public void ContinueExploring()
        {
            if (panel != null) panel.SetActive(false);
            onContinue.Invoke();
        }

        public void Replay()
        {
            ProgressManager.ResetAll();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Visiteur
        private bool FindVisitor()
        {
            if (visitor != null) return true;
            var mainCamera = Camera.main;
            if (mainCamera == null) return false;
            visitor = mainCamera.transform;
            return true;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // Menus de test
        [ContextMenu("Test : Afficher")] private void DebugShow() => Show();
    }
}
