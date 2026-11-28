using UnityEngine;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Empêche la tête du visiteur de passer à travers les murs quand il avance physiquement
    /// (en marchant ou en se penchant dans sa pièce réelle).
    /// Le Character Controller bloque déjà le joystick et la téléportation, mais pas le casque lui-même.
    /// Ici, si la tête touche un mur, tout le XR Origin recule d'autant : le visiteur glisse le long du mur
    /// au lieu de le traverser. Les déplacements par joystick ou téléportation ne sont pas gênés.
    /// À poser sur l'objet « XR Origin (XR Rig) ».
    /// </summary>
    [DefaultExecutionOrder(1000)] // après le suivi du casque et les déplacements
    public class HeadWallBlocker : MonoBehaviour
    {
        // Réglages
        [Tooltip("Caméra du casque. Vide : la Main Camera sous ce XR Origin.")]
        [SerializeField] private Transform head;

        [Tooltip("Rayon de la tête (m) : distance minimale gardée entre les yeux et le mur.")]
        [SerializeField, Range(0.05f, 0.4f)] private float headRadius = 0.15f;

        [Tooltip("Couches qui bloquent la tête (murs, portes, mobilier). Par défaut : tout.")]
        [SerializeField] private LayerMask blockingLayers = ~0;

        [Tooltip("Décoché : les objets avec un Rigidbody (éponge, télécommande, sabres...) ne bloquent jamais la tête.")]
        [SerializeField] private bool blockOnMovingObjects = false;

        [Tooltip("Au-delà de ce saut de la tête en une image (m), on ne repousse pas : casque retiré, remis, ou suivi perdu.")]
        [SerializeField] private float maxHeadJump = 0.5f;

        [Tooltip("Coché : un message apparaît dans la Console à chaque fois que la tête est repoussée (pour régler).")]
        [SerializeField] private bool debugLog = false;

        // État
        private CharacterController body;
        private Vector3 lastHeadLocal; // dernière place sûre de la tête, dans le repère du XR Origin
        private bool initialized;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private Collider lastBlocker;
        private Vector3 lastBlockPoint;

        // Dernier blocage, pour l'affichage de diagnostic (Height Debug Readout).
        public string LastBlockName { get; private set; } = "";
        public float LastBlockTime { get; private set; } = float.NegativeInfinity;
        public float LastBlockHeight { get; private set; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            if (head == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) head = cam.transform;
            }
            if (head == null) Debug.LogWarning("[Tête] Pas de caméra trouvée sous le XR Origin : remplir le champ Head.", this);
        }

        private void OnEnable() => initialized = false;
        private void OnApplicationPause(bool paused) { if (paused) initialized = false; }

        private void LateUpdate()
        {
            if (head == null) return;

            // Casque retiré (application en pause ou sans focus) : on ne fait rien, et on repart de zéro au retour.
            if (!Application.isFocused)
            {
                initialized = false;
                return;
            }

            if (!initialized)
            {
                lastHeadLocal = transform.InverseTransformPoint(head.position);
                initialized = true;
                return;
            }

            // Ce que le joystick, la rotation ou la téléportation ont déplacé : on le suit sans le tester
            // (le Character Controller et le rayon de téléportation s'en chargent déjà).
            Vector3 safeHead = transform.TransformPoint(lastHeadLocal);

            Vector3 current = head.position;
            lastBlocker = null;

            // Saut de suivi (casque remis, recentrage) : pas un vrai pas, on se recale sans repousser.
            Vector3 jump = current - safeHead;
            jump.y = 0f;
            if (jump.magnitude > maxHeadJump)
            {
                lastHeadLocal = transform.InverseTransformPoint(current);
                if (debugLog) Debug.Log($"[Tête] Saut de suivi de {jump.magnitude:0.00} m ignoré.", this);
                return;
            }

            Vector3 target = Resolve(safeHead, current);
            Vector3 push = target - current;
            push.y = 0f;

            if (push.sqrMagnitude > 0.000001f)
            {
                MoveRig(push);
                LastBlockName = lastBlocker != null ? lastBlocker.name : "?";
                LastBlockTime = Time.time;
                LastBlockHeight = lastBlockPoint.y;
                if (debugLog)
                    Debug.Log($"[Tête] Repoussée de {push.magnitude * 100f:0.0} cm par « {(lastBlocker != null ? lastBlocker.name : "?")} », "
                              + $"yeux à {head.position.y - transform.position.y:0.00} m du sol.", lastBlocker != null ? (Object)lastBlocker : this);
            }

            lastHeadLocal = transform.InverseTransformPoint(head.position);
        }

        /// Où la tête peut aller entre « from » (dernière place sûre) et « to » (place réelle) :
        /// on avance jusqu'au mur, puis on glisse le long de lui (deux fois, pour les coins).
        private Vector3 Resolve(Vector3 from, Vector3 to)
        {
            Vector3 position = from;
            Vector3 move = to - from;
            float vertical = move.y;
            move.y = 0f;

            for (int pass = 0; pass < 3; pass++)
            {
                float length = move.magnitude;
                if (length < 0.0001f) break;
                Vector3 dir = move / length;

                if (!Cast(position, dir, length, out RaycastHit hit))
                {
                    position += move;
                    move = Vector3.zero;
                    break;
                }

                const float skin = 0.01f;
                float allowed = Mathf.Max(0f, hit.distance - skin);
                position += dir * allowed;

                // Le reste du mouvement glisse le long du mur.
                Vector3 normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude < 0.0001f) { move = Vector3.zero; break; }
                normal.Normalize();
                Vector3 rest = dir * (length - allowed);
                move = rest - Vector3.Dot(rest, normal) * normal;
            }

            position.y += vertical;
            return position;
        }

        private bool Cast(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(origin, headRadius, dir, hits, distance, blockingLayers,
                QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.collider == null || !Blocks(h.collider)) continue;
                // Distance 0 : la sphère partait déjà dans l'objet, on l'ignore pour ne pas rester coincé.
                if (h.distance <= 0f) continue;
                if (h.distance < bestDistance)
                {
                    bestDistance = h.distance;
                    best = h;
                    found = true;
                }
            }
            if (found)
            {
                lastBlocker = best.collider;
                lastBlockPoint = best.point;
            }
            return found;
        }

        private bool Blocks(Collider c)
        {
            if (c.transform.IsChildOf(transform)) return false; // le joueur lui-même, ses manettes
            if (!blockOnMovingObjects && c.attachedRigidbody != null) return false;
            return true;
        }

        private void MoveRig(Vector3 delta)
        {
            // Déplacement horizontal seulement : la hauteur du joueur n'est jamais touchée par ce script.
            delta.y = 0f;
            transform.position += delta;
            if (body != null) Physics.SyncTransforms();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (head == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(head.position, headRadius);
        }
#endif
    }
}
