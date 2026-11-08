using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Building
{
    public enum DoorSwing
    {
        AwayFromVisitor,
        Positive,
        Negative
    }

    [DisallowMultipleComponent]
    public class Door : MonoBehaviour
    {
        // Réglages
        [Header("Ouverture")]
        [Tooltip("Angle d'ouverture en degrés.")]
        [SerializeField] private float openAngle = 95f;

        [Tooltip("Sens d'ouverture. AwayFromVisitor : la porte s'écarte toujours du visiteur.")]
        [SerializeField] private DoorSwing swing = DoorSwing.AwayFromVisitor;

        [Tooltip("Vitesse de rotation en degrés par seconde.")]
        [SerializeField] private float speed = 200f;

        [Header("Ouverture automatique")]
        [SerializeField] private bool autoOpen = true;

        [Tooltip("Distance au sol, en mètres, entre le visiteur et le milieu de la porte.")]
        [SerializeField] private float openDistance = 1.6f;

        [Tooltip("Temps en secondes avant de refermer une fois le visiteur parti.")]
        [SerializeField] private float closeDelay = 1f;

        [Tooltip("Porte verrouillée : elle reste fermée.")]
        [SerializeField] private bool locked;

        [Header("Événements")]
        public UnityEvent onOpened = new UnityEvent();
        public UnityEvent onClosed = new UnityEvent();

        // État
        private Quaternion closedRotation;
        private Vector3 closedCenter;
        private Transform visitor;
        private float currentAngle;
        private float targetAngle;
        private float lastNearTime = float.NegativeInfinity;

        public bool IsOpen => !Mathf.Approximately(targetAngle, 0f);

        public bool Locked
        {
            get => locked;
            set
            {
                locked = value;
                if (locked) Close();
            }
        }

        // Initialisation
        private void Awake()
        {
            closedRotation = transform.rotation;
            var meshRenderer = GetComponentInChildren<Renderer>();
            closedCenter = meshRenderer != null ? meshRenderer.bounds.center : transform.position;
        }

        // Boucle
        private void Update()
        {
            if (autoOpen) UpdateAutoOpen();
            Animate();
        }

        private void UpdateAutoOpen()
        {
            bool near = FindNearestOpener(out Vector3 openerPosition) && !locked;

            if (near)
            {
                lastNearTime = Time.time;
                if (!IsOpen) targetAngle = ChooseAngle(openerPosition);
            }
            else if (IsOpen && Time.time - lastNearTime > closeDelay)
            {
                targetAngle = 0f;
            }
        }

        private void Animate()
        {
            if (Mathf.Approximately(currentAngle, targetAngle)) return;

            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, speed * Time.deltaTime);
            transform.rotation = Quaternion.AngleAxis(currentAngle, Vector3.up) * closedRotation;

            if (Mathf.Approximately(currentAngle, targetAngle))
            {
                if (IsOpen) onOpened.Invoke();
                else onClosed.Invoke();
            }
        }

        // Commandes
        public void Open()
        {
            if (locked || IsOpen) return;
            targetAngle = FindVisitor() ? ChooseAngle(visitor.position) : SignedAngle(1f);
            lastNearTime = Time.time;
        }

        public void Close() => targetAngle = 0f;

        public void Lock() => Locked = true;
        public void Unlock() => Locked = false;

        // Calculs
        private bool FindVisitor()
        {
            if (visitor != null) return true;
            var mainCamera = Camera.main;
            if (mainCamera == null) return false;
            visitor = mainCamera.transform;
            return true;
        }

        private bool FindNearestOpener(out Vector3 position)
        {
            position = default;
            float best = openDistance;
            bool found = false;

            if (FindVisitor())
            {
                float distance = HorizontalDistance(visitor.position, closedCenter);
                if (distance < best)
                {
                    best = distance;
                    position = visitor.position;
                    found = true;
                }
            }

            foreach (var opener in DoorOpener.All)
            {
                float distance = HorizontalDistance(opener.transform.position, closedCenter);
                if (distance < best)
                {
                    best = distance;
                    position = opener.transform.position;
                    found = true;
                }
            }

            return found;
        }

        private float ChooseAngle(Vector3 visitorPosition)
        {
            switch (swing)
            {
                case DoorSwing.Positive: return SignedAngle(1f);
                case DoorSwing.Negative: return SignedAngle(-1f);
            }

            Vector3 hinge = transform.position;
            Vector3 offset = closedCenter - hinge;
            Vector3 positiveEnd = hinge + Quaternion.AngleAxis(openAngle, Vector3.up) * offset;
            Vector3 negativeEnd = hinge + Quaternion.AngleAxis(-openAngle, Vector3.up) * offset;

            return HorizontalDistance(positiveEnd, visitorPosition) >= HorizontalDistance(negativeEnd, visitorPosition)
                ? SignedAngle(1f)
                : SignedAngle(-1f);
        }

        private float SignedAngle(float sign) => Mathf.Abs(openAngle) * sign;

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // Menus de test
        [ContextMenu("Test : Ouvrir")] private void DebugOpen() => Open();
        [ContextMenu("Test : Fermer")] private void DebugClose() => Close();

        // Aide visuelle
        private void OnDrawGizmosSelected()
        {
            var meshRenderer = GetComponentInChildren<Renderer>();
            Vector3 center = Application.isPlaying ? closedCenter : (meshRenderer != null ? meshRenderer.bounds.center : transform.position);
            Gizmos.color = locked ? Color.red : Color.green;
            Gizmos.DrawWireSphere(new Vector3(center.x, transform.position.y, center.z), openDistance);
        }
    }
}
