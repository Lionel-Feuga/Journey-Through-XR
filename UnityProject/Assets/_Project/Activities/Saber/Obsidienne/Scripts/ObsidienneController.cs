using UnityEngine;

namespace ObsidienneVR
{
    /// <summary>Visual rotor controller. All angles/speeds are degrees and degrees/second.</summary>
    public sealed class ObsidienneController : MonoBehaviour
    {
        public enum RotorState { Locked, Balancing, Spinning, Returning }
        [Header("Model references (assigned by the prefab builder)")]
        public Transform rotor;
        public Transform axisNormal;
        public Transform axisLocked;
        public GameObject bladeA;
        public GameObject bladeB;
        [Header("Tracked controller, NOT the rotating blade assembly")]
        public Transform rotationSource;
        [Tooltip("Local axis along the forearm/controller. Calibrate for your grip pose.")]
        public Vector3 controllerRollAxis = Vector3.forward;
        public bool invertGesture;
        [Header("Tuning")]
        [Min(0.1f)] public float unlockHoldSeconds = 0.45f;
        [Min(1f)] public float gestureThreshold = 220f;
        [Min(0f)] public float neutralThreshold = 65f;
        [Min(0f)] public float neutralSeconds = 0.10f;
        [Min(0f)] public float gestureCooldown = 0.30f;
        [Min(1f)] public float spinSpeed = 1080f;
        [Tooltip("Raideur du rappel vers l'horizontale. Plus haut : les socles reviennent plus vite.")]
        [Min(0f)] public float balanceStiffness = 45f;
        [Tooltip("Frein du balancement. Plus bas : les socles oscillent plus longtemps avant de se poser.")]
        [Min(0f)] public float balanceDamping = 5f;
        [Tooltip("Accélération de la mise en rotation, en degrés par seconde au carré.")]
        [Min(1f)] public float spinAcceleration = 3600f;
        [Min(1f)] public float relockSpeed = 540f;
        [SerializeField] private RotorState state = RotorState.Locked;
        [SerializeField] private bool bladeAOn, bladeBOn;
        public RotorState State => state;
        public float Angle => angle;

        private Transform frame;
        private Quaternion restRotation, lastRotation;
        private Vector3 normal, lockedDirection;
        private float angle, returnAngle, spinDirection, holdTime, neutralTime, cooldown;
        private float velocity;   // vitesse angulaire courante du rotor, en degrés par seconde
        private bool initialized, tracked, haveLastRotation, triggerHeld, holdConsumed, armed;

        private void Awake() { Initialize(); ApplyBlades(); }
        private bool Initialize()
        {
            if (initialized) return true;
            if (!rotor || !rotor.parent || !axisNormal || !axisLocked) return false;
            frame = rotor.parent;
            normal = frame.InverseTransformDirection(axisNormal.position - rotor.position).normalized;
            lockedDirection = Vector3.ProjectOnPlane(
                frame.InverseTransformDirection(axisLocked.position - rotor.position), normal).normalized;
            if (normal.sqrMagnitude < 0.5f || lockedDirection.sqrMagnitude < 0.5f) return false;
            restRotation = rotor.localRotation;
            initialized = true;
            return true;
        }

        /// <summary>Call only while held and tracking is valid. False safely relocks.</summary>
        public void SetTrackingActive(bool active)
        {
            if (tracked == active) return;
            tracked = active;
            haveLastRotation = false; armed = false; neutralTime = 0;
            holdTime = 0; holdConsumed = false; triggerHeld = false;
            if (!active) RequestLock();
        }
        /// <summary>Bind the upper trigger to this input, normally through the XR adapter.</summary>
        public void SetTriggerHeld(bool pressed)
        {
            triggerHeld = pressed;
            if (!pressed) { holdTime = 0; holdConsumed = false; }
        }
        public void SetBladeA(bool on) { bladeAOn = on; ApplyBlades(); }
        public void SetBladeB(bool on) { bladeBOn = on; ApplyBlades(); }
        public void ToggleBladeA() { SetBladeA(!bladeAOn); }
        public void ToggleBladeB() { SetBladeB(!bladeBOn); }
        private void ApplyBlades()
        {
            if (bladeA) bladeA.SetActive(bladeAOn);
            if (bladeB) bladeB.SetActive(bladeBOn);
            if (!bladeAOn && !bladeBOn) RequestLock();
        }
        public void RequestLock()
        {
            if (!Initialize() || state == RotorState.Locked || state == RotorState.Returning) return;
            // 0 and 180 both align the two opposing emitters with the handle.
            returnAngle = Mathf.Round(angle / 180f) * 180f;
            state = RotorState.Returning; armed = false; neutralTime = 0; holdTime = 0;
            holdConsumed = true; // a release is required before a new unlock
        }
        private void OnDisable()
        {
            tracked = false; triggerHeld = false; haveLastRotation = false;
            armed = false; neutralTime = holdTime = cooldown = 0; holdConsumed = false;
            if (!initialized) return;
            angle = 0; state = RotorState.Locked; rotor.localRotation = restRotation;
        }

        private void LateUpdate()
        { Tick(Time.deltaTime); }
        private void Tick(float dt)
        {
            if (!Initialize()) return;
            if (dt <= 0) return;
            cooldown = Mathf.Max(0, cooldown - dt);
            float rollSpeed = ReadRollSpeed(dt);
            if (tracked && triggerHeld && !holdConsumed && state == RotorState.Locked && (bladeAOn || bladeBOn))
            {
                holdTime += dt;
                if (holdTime >= unlockHoldSeconds)
                {
                    state = RotorState.Balancing; holdConsumed = true; velocity = 0;
                    armed = false; neutralTime = 0; cooldown = gestureCooldown;
                }
            }
            else if (state != RotorState.Locked || !tracked || !(bladeAOn || bladeBOn)) holdTime = 0;

            // Return to a quiet wrist before each gesture, to reject rebound and sustained turns.
            if (tracked && cooldown <= 0 && Mathf.Abs(rollSpeed) <= neutralThreshold)
            {
                neutralTime += dt;
                if (neutralTime >= neutralSeconds) armed = true;
            }
            else neutralTime = 0;
            if (tracked && armed && cooldown <= 0 && Mathf.Abs(rollSpeed) >= gestureThreshold)
            {
                if (state == RotorState.Balancing)
                {
                    spinDirection = Mathf.Sign(rollSpeed);
                    state = RotorState.Spinning;
                    armed = false; cooldown = gestureCooldown;
                }
                else if (state == RotorState.Spinning && Mathf.Sign(rollSpeed) != spinDirection)
                {
                    RequestLock(); cooldown = gestureCooldown;
                }
            }
            switch (state)
            {
                case RotorState.Balancing:
                    Vector3 worldNormal = frame.TransformDirection(normal);
                    Vector3 horizontal = Vector3.Cross(worldNormal, Vector3.up);
                    // A flat guard has no unique horizontal diameter: retain current orientation.
                    if (horizontal.sqrMagnitude > 0.0025f)
                    {
                        float target = Vector3.SignedAngle(lockedDirection,
                            frame.InverseTransformDirection(horizontal.normalized), normal);
                        float opposite = target + 180f;
                        if (Mathf.Abs(Mathf.DeltaAngle(angle, opposite)) < Mathf.Abs(Mathf.DeltaAngle(angle, target))) target = opposite;
                        // Rappel élastique : les socles dépassent un peu l'horizontale, se balancent, puis se posent.
                        velocity += Mathf.DeltaAngle(angle, target) * balanceStiffness * dt;
                    }
                    velocity -= velocity * Mathf.Min(1f, balanceDamping * dt);
                    velocity = Mathf.Clamp(velocity, -spinSpeed, spinSpeed);
                    angle = Mathf.Repeat(angle + velocity * dt, 360f);
                    break;
                case RotorState.Spinning:
                    // Mise en rotation progressive à partir du mouvement en cours.
                    velocity = Mathf.MoveTowards(velocity, spinDirection * spinSpeed, spinAcceleration * dt);
                    angle = Mathf.Repeat(angle + velocity * dt, 360f);
                    break;
                case RotorState.Returning:
                    angle = Mathf.MoveTowardsAngle(angle, returnAngle, relockSpeed * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(angle, returnAngle)) < 0.05f)
                    { angle = Mathf.Repeat(returnAngle, 360f); state = RotorState.Locked; velocity = 0; }
                    break;
            }
            rotor.localRotation = Quaternion.AngleAxis(angle, normal) * restRotation;
        }
        private float ReadRollSpeed(float dt)
        {
            if (!tracked || !rotationSource) { haveLastRotation = false; return 0; }
            Quaternion current = rotationSource.rotation;
            if (!haveLastRotation || dt > 0.1f)
            { lastRotation = current; haveLastRotation = true; armed = false; return 0; }
            Quaternion delta = (current * Quaternion.Inverse(lastRotation)).normalized;
            lastRotation = current;
            delta.ToAngleAxis(out float degrees, out Vector3 worldAxis);
            if (degrees > 180f) degrees -= 360f;
            if (Mathf.Abs(degrees) < 0.001f || float.IsNaN(worldAxis.x)) return 0;
            Vector3 roll = rotationSource.TransformDirection(controllerRollAxis.normalized);
            return degrees / dt * Vector3.Dot(worldAxis, roll) * (invertGesture ? -1f : 1f);
        }
        [ContextMenu("Preview/Light both blades")]
        private void LightBoth() { SetBladeA(true); SetBladeB(true); }
        [ContextMenu("Preview/Extinguish both blades")]
        private void ExtinguishBoth() { SetBladeA(false); SetBladeB(false); }
    }
}
