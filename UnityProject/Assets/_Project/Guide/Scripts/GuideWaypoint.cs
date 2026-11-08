using JourneyThroughXR.Core;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Guide
{
    public class GuideWaypoint : MonoBehaviour
    {
        // Réglages
        [Tooltip("Étape du parcours : le robot s'y arrête et attend le visiteur.")]
        [SerializeField] private bool isStop;

        [Tooltip("Activité à terminer avant que le robot reparte (optionnel).")]
        [SerializeField] private Activity waitForActivity;

        [Tooltip("Pause en secondes, une fois le visiteur arrivé et le dialogue terminé, avant de repartir.")]
        [SerializeField] private float pauseAtStop = 3f;

        [Header("Dialogue")]
        [Tooltip("Phrases dites par le robot une fois arrêté à cette étape, l'une après l'autre.")]
        [TextArea(2, 4)]
        [SerializeField] private string[] dialogue;

        [Tooltip("Délai en secondes entre l'arrêt du robot et le début du dialogue.")]
        [SerializeField] private float dialogueDelay = 0.5f;

        [Header("Événements")]
        public UnityEvent onRobotArrived = new UnityEvent();
        public UnityEvent onVisitorJoined = new UnityEvent();
        public UnityEvent onRobotLeft = new UnityEvent();

        // Accès
        public bool IsStop
        {
            get => isStop;
            set => isStop = value;
        }

        public Activity WaitForActivity => waitForActivity;
        public float PauseAtStop => pauseAtStop;
        public string[] Dialogue => dialogue;
        public bool HasDialogue => dialogue != null && dialogue.Length > 0;
        public float DialogueDelay => dialogueDelay;

        public void SetDialogue(string[] lines) => dialogue = lines;

        // Aide visuelle
        private void OnDrawGizmos()
        {
            Gizmos.color = isStop ? new Color(1f, 0.6f, 0f) : new Color(0.2f, 0.8f, 1f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, isStop ? 0.25f : 0.12f);
        }
    }
}
