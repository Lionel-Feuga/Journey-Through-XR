using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Building
{
    public class ElevatorStart : MonoBehaviour
    {
        // Réglages
        [Header("Cabine")]
        [Tooltip("Repère de la cabine de départ (Ascenseur_1).")]
        [SerializeField] private Transform cabinAnchor;

        [Tooltip("Porte de la cabine de départ.")]
        [SerializeField] private ElevatorDoor door;

        [Tooltip("Joueur à placer dans la cabine (XR Origin).")]
        [SerializeField] private Transform player;

        [Header("Démarrage")]
        [Tooltip("Panneau avec le bouton Démarrer, masqué après l'appui.")]
        [SerializeField] private GameObject startPanel;

        [Tooltip("Durée de la montée en secondes, avant l'ouverture des portes.")]
        [SerializeField] private float rideDuration = 0.5f;

        [Header("Vue de départ")]
        [Tooltip("Tourne le joueur vers la porte au lancement, quelle que soit l'orientation réelle du casque.")]
        [SerializeField] private bool alignViewOnStart = true;

        [Tooltip("Délai en secondes avant l'alignement, le temps que le suivi du casque démarre.")]
        [SerializeField] private float alignDelay = 0.3f;

        [Header("Événements")]
        public UnityEvent onStartPressed = new UnityEvent();
        public UnityEvent onArrived = new UnityEvent();

        // État
        public bool HasStarted { get; private set; }

        // Lancement
        private void Start()
        {
            if (alignViewOnStart) StartCoroutine(AlignViewWhenTracked());
        }

        private IEnumerator AlignViewWhenTracked()
        {
            yield return new WaitForSeconds(alignDelay);
            AlignView();
        }

        // Alignement de la vue
        public void AlignView()
        {
            var origin = player != null ? player.GetComponent<XROrigin>() : null;
            if (origin == null || origin.Camera == null) return;

            Vector3 cameraForward = origin.Camera.transform.forward;
            cameraForward.y = 0f;
            Vector3 targetForward = transform.forward;
            targetForward.y = 0f;
            if (cameraForward.sqrMagnitude < 0.0001f) return;

            origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(cameraForward, targetForward, Vector3.up));

            Vector3 target = transform.position;
            target.y = origin.Camera.transform.position.y;
            origin.MoveCameraToWorldLocation(target);
            Physics.SyncTransforms();
        }

        // Démarrage
        public void StartVisit()
        {
            if (HasStarted) return;
            HasStarted = true;
            if (startPanel != null) startPanel.SetActive(false);
            onStartPressed.Invoke();
            StartCoroutine(Ride());
        }

        private IEnumerator Ride()
        {
            yield return new WaitForSeconds(rideDuration);
            if (door != null) door.Open(transform.position);
            onArrived.Invoke();
        }

        // Menus de mise en place
        [ContextMenu("Aligner sur la cabine")]
        private void AlignOnCabin()
        {
            if (cabinAnchor == null || door == null)
            {
                Debug.LogWarning("[Ascenseur] Renseigner Cabin Anchor et Door d'abord.", this);
                return;
            }

            Vector3 position = cabinAnchor.position;
            Vector3 doorCenter = door.GetComponentInChildren<Renderer>().bounds.center;
            Vector3 forward = doorCenter - position;
            forward.y = 0f;

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(transform, "Aligner sur la cabine");
#endif
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        [ContextMenu("Placer le joueur ici")]
        private void PlacePlayer()
        {
            if (player == null)
            {
                Debug.LogWarning("[Ascenseur] Renseigner Player d'abord.", this);
                return;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(player, "Placer le joueur");
#endif
            player.SetPositionAndRotation(transform.position, transform.rotation);
        }

        // Menus de test
        [ContextMenu("Test : Démarrer")] private void DebugStart() => StartVisit();
        [ContextMenu("Test : Aligner la vue")] private void DebugAlignView() => AlignView();
    }
}
