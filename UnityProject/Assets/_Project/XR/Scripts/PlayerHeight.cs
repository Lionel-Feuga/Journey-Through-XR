using System;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Mode assis ou debout.
    /// Debout : la vue est à la vraie hauteur du casque.
    /// Assis : la vue est montée pour que les yeux soient à « Seated Eye Height » (1,60 m, une personne de 1,70 m).
    /// La hauteur du casque est mesurée au moment du choix : il suffit d'être assis normalement quand on appuie.
    /// Au lancement, le visiteur est toujours debout (vraie hauteur du casque).
    /// À poser sur l'objet « XR Origin (XR Rig) ».
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerHeight : MonoBehaviour
    {
        // Réglages
        [Tooltip("Hauteur des yeux voulue en mode assis (m). 1,60 m = une personne de 1,70 m.")]
        [SerializeField, Range(1.2f, 1.9f)] private float seatedEyeHeight = 1.6f;

        [Tooltip("Objet qui porte la caméra (Camera Offset). Vide : trouvé seul sur le XR Origin.")]
        [SerializeField] private Transform cameraOffset;

        [Tooltip("Caméra du casque. Vide : trouvée seule sur le XR Origin.")]
        [SerializeField] private Transform head;

        [Header("Mesure (lecture seule, pendant le Play)")]
        [Tooltip("Hauteur des yeux dans le jeu, au-dessus du sol virtuel (m). Debout, elle doit valoir ta taille moins 10 à 12 cm.")]
        [SerializeField] private float eyeHeightInGame;

        [Tooltip("Hauteur réelle du casque vue par le Quest (m), sans le mode assis. Si elle est fausse, c'est le sol du Quest qui est mal réglé.")]
        [SerializeField] private float headsetHeight;

        // État
        public event Action<bool> Changed;
        public bool IsSeated { get; private set; }
        public float EyeHeightInGame => eyeHeightInGame;
        public float HeadsetHeight => headsetHeight;
        /// Objet touché par le rayon vertical sous les yeux (diagnostic).
        public string BelowEyesName { get; private set; } = "";
        private float extraHeight;
        private XROrigin origin;

        private void Awake()
        {
            origin = GetComponent<XROrigin>();
            if (cameraOffset == null && origin != null && origin.CameraFloorOffsetObject != null)
                cameraOffset = origin.CameraFloorOffsetObject.transform;
            if (head == null && origin != null && origin.Camera != null)
                head = origin.Camera.transform;
            if (cameraOffset == null) Debug.LogWarning("[Hauteur] Camera Offset introuvable : remplir le champ.", this);
        }

        // Le XR Origin remet parfois la hauteur à zéro (recentrage du casque) : on la réimpose chaque image.
        private void LateUpdate()
        {
            if (IsSeated) Apply();
            if (head != null)
            {
                // Distance réelle jusqu'au sol du bâtiment, sous les yeux.
                bool hitSomething = Physics.Raycast(head.position, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore);
                eyeHeightInGame = hitSomething ? hit.distance : head.position.y - transform.position.y;
                BelowEyesName = hitSomething ? hit.collider.name : "";
                headsetHeight = HeadHeight();
            }
        }

        // Commandes (boutons)
        public void SetSeated()
        {
            float measured = HeadHeight();
            extraHeight = Mathf.Max(0f, seatedEyeHeight - measured);
            IsSeated = true;
            Apply();
            Debug.Log($"[Hauteur] Mode assis : casque à {measured:0.00} m, vue montée de {extraHeight:0.00} m.", this);
            Changed?.Invoke(true);
        }

        public void SetStanding()
        {
            extraHeight = 0f;
            IsSeated = false;
            Apply();
            Debug.Log("[Hauteur] Mode debout : vraie hauteur du casque.", this);
            Changed?.Invoke(false);
        }

        public void Toggle()
        {
            if (IsSeated) SetStanding();
            else SetSeated();
        }

        // Interne
        /// Hauteur réelle du casque au-dessus du sol réel (sans le décalage du mode assis).
        private float HeadHeight()
        {
            if (head == null) return seatedEyeHeight;
            return BaseOffset() + head.localPosition.y;
        }

        /// Hauteur que le XR Origin donne lui-même au Camera Offset : 0 au sol (casque réel),
        /// « Camera Y Offset » sinon (simulateur, ancien mode).
        private float BaseOffset()
        {
            if (origin == null) return 0f;
            return origin.CurrentTrackingOriginMode == UnityEngine.XR.TrackingOriginModeFlags.Floor ? 0f : origin.CameraYOffset;
        }

        private void Apply()
        {
            if (cameraOffset == null) return;
            Vector3 p = cameraOffset.localPosition;
            float wanted = BaseOffset() + (IsSeated ? extraHeight : 0f);
            if (Mathf.Abs(p.y - wanted) > 0.0001f)
            {
                p.y = wanted;
                cameraOffset.localPosition = p;
            }
        }

        // Menus de test
        [ContextMenu("Test : Assis")] private void DebugSeated() => SetSeated();
        [ContextMenu("Test : Debout")] private void DebugStanding() => SetStanding();
    }
}
