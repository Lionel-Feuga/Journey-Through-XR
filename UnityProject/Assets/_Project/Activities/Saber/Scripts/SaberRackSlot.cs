using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Garde un sabre sur son emplacement du râtelier.
    /// Posé : le sabre est figé (il ne tombe pas du râtelier).
    /// Saisi : il redevient un objet physique normal.
    /// Lâché : il revient tout seul à sa place après quelques secondes.
    /// À poser sur le même objet que LaserSaber. La place est celle du sabre au lancement.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LaserSaber))]
    public class SaberRackSlot : MonoBehaviour
    {
        [Tooltip("Secondes entre le moment où le sabre est lâché et son retour sur le râtelier.")]
        [SerializeField] private float returnDelay = 3f;

        private LaserSaber saber;
        private XRGrabInteractable grab;
        private Rigidbody body;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool docked;
        private float releasedAt = -1f;

        public bool IsDocked => docked;

        private void Awake()
        {
            saber = GetComponent<LaserSaber>();
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            homePosition = transform.position;
            homeRotation = transform.rotation;

            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        private void Start()
        {
            Dock();
        }

        private void OnDestroy()
        {
            if (grab == null) return;
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            docked = false;
            releasedAt = -1f;
            body.constraints = RigidbodyConstraints.None;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            body.constraints = RigidbodyConstraints.None;
            releasedAt = Time.time;
        }

        private void Update()
        {
            if (docked || grab.isSelected) return;

            // LaserSaber peut déjà l'avoir ramené (bouton A / X, chute) : on le fige simplement.
            bool atHome = (transform.position - homePosition).sqrMagnitude < 0.000001f
                          && Quaternion.Angle(transform.rotation, homeRotation) < 0.1f;
            if (atHome)
            {
                Dock();
                return;
            }

            if (releasedAt >= 0f && Time.time - releasedAt >= returnDelay)
            {
                saber.Respawn();
                Dock();
            }
        }

        private void Dock()
        {
            docked = true;
            releasedAt = -1f;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.constraints = RigidbodyConstraints.FreezeAll;
        }
    }
}
