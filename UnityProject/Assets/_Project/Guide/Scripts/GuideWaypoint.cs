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

        [Tooltip("Pause en secondes, une fois le visiteur arrivé, avant de repartir.")]
        [SerializeField] private float pauseAtStop = 3f;

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

        // Aide visuelle
        private void OnDrawGizmos()
        {
            Gizmos.color = isStop ? new Color(1f, 0.6f, 0f) : new Color(0.2f, 0.8f, 1f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, isStop ? 0.25f : 0.12f);
        }
    }
}
