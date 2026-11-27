using System;
using UnityEngine;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// La voiture du circuit RC (A3). Elle suit la piste comme une voiture de slot car :
    /// la gâchette de la télécommande accélère, la relâcher freine.
    /// Trop vite dans un virage : elle s'envole et retombe à côté. Trop lente dans le looping : elle tombe.
    /// Après une sortie, elle est reposée sur la piste au début de la portion d'avant.
    /// Aide (Assisted) : la voiture ralentit seule avant les virages et garde assez d'élan dans le looping.
    /// </summary>
    public class RCCar : MonoBehaviour
    {
        public enum CrashKind { Curve, Loop }

        // Réglages
        [SerializeField] private RCTrack track;

        [Tooltip("Roues (tournent avec la vitesse).")]
        [SerializeField] private Transform[] wheels;

        [Tooltip("Décalage de côté sur la piste (m, + = voie de droite).")]
        [SerializeField] private float lane = 0.04f;

        [Header("Moteur (réglable pendant le Play)")]
        [Tooltip("Accélération gâchette à fond (m/s²).")]
        [SerializeField] private float acceleration = 3.5f;

        [Tooltip("Vitesse maximale en ligne droite (m/s).")]
        [SerializeField] private float maxSpeed = 3.2f;

        [Tooltip("Freinage quand la gâchette est relâchée (m/s²).")]
        [SerializeField] private float brake = 3f;

        [Tooltip("Gravité de la piste (m/s²). Plus haut : looping et pont plus durs.")]
        [SerializeField] private float trackGravity = 4f;

        [Header("Difficulté")]
        [Tooltip("Vitesse au-delà de laquelle la voiture sort dans un virage (m/s).")]
        [SerializeField] private float curveCrashSpeed = 1.9f;

        [Tooltip("Tours à faire.")]
        [SerializeField] private int laps = 3;

        [Header("Sortie de piste")]
        [Tooltip("Durée de la chute avant que la voiture soit reposée (s).")]
        [SerializeField] private float crashTime = 1.6f;

        // État
        public event Action<RCCar, RCTrack.Piece, CrashKind> Crashed;
        public event Action<RCCar, int> LapCompleted;

        public RCTrack Track => track;
        public int Laps => laps;
        public int Lap { get; private set; }
        public bool Finished => Lap >= laps;
        public float Speed { get; private set; }
        public float MaxSpeed => maxSpeed;
        public float Throttle { get; set; }
        public bool Assisted { get; set; }
        public bool IsCrashed => crashTimer > 0f;
        public int Crashes { get; private set; }
        /// Distance parcourue sur la piste depuis le dernier respawn ou reset, toujours croissante.
        public float Travelled { get; private set; }
        /// 0 : tout va bien, 1 : attention (virage proche), 2 : trop vite.
        public int Warning { get; private set; }

        private float distance;
        private int lapCounter;   // peut redescendre si la voiture est reposée avant la ligne
        private float crashTimer;
        private Vector3 flyVelocity;
        private Vector3 flySpin;
        private float floorY;
        private int respawnPiece;
        private float blinkTimer;
        private Renderer[] renderers;

        public void Configure(RCTrack circuit, Transform[] wheelPivots)
        {
            track = circuit;
            wheels = wheelPivots;
        }

        // Cycle de vie
        private void Awake()
        {
            if (track == null) track = GetComponentInParent<RCTrack>();
            renderers = GetComponentsInChildren<Renderer>();
        }

        private void Start()
        {
            if (track == null) return;
            track.EnsureBuilt();
            ResetCar();
        }

        private void Update()
        {
            if (track == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (crashTimer > 0f)
            {
                Fly(dt);
                return;
            }

            Drive(dt);
            Place();
            Blink(dt);
        }

        // Conduite
        private void Drive(float dt)
        {
            float throttle = Mathf.Clamp01(Throttle);
            float drag = maxSpeed > 0f ? acceleration / maxSpeed : 1f;
            float a = throttle * acceleration - drag * Speed - trackGravity * track.SlopeAt(distance);
            if (throttle < 0.05f) a -= brake;
            float v = Mathf.Max(0f, Speed + a * dt);

            var piece = track.PieceAt(distance);
            float allowed = AllowedSpeed(out float toCurve);

            if (Assisted)
            {
                // Freinage automatique avant les virages, élan garanti dans le looping.
                v = Mathf.Min(v, allowed * 0.92f);
                if (piece.kind == RCTrack.PieceKind.Loop) v = Mathf.Max(v, LoopSafeSpeed(piece) + 0.25f);
            }

            Speed = v;
            Warning = v > allowed * 0.97f ? 2 : (v > allowed * 0.8f && toCurve < 0.6f ? 1 : 0);

            distance += v * dt;
            Travelled += v * dt;

            // Tour terminé
            if (distance >= track.Length)
            {
                distance -= track.Length;
                lapCounter++;
                if (lapCounter > Lap && Lap < laps)
                {
                    Lap = lapCounter;
                    LapCompleted?.Invoke(this, Lap);
                }
            }

            // Sorties de piste (plus de sortie une fois les tours faits)
            if (Finished) return;
            piece = track.PieceAt(distance);
            if (piece.kind == RCTrack.PieceKind.Curve && v > curveCrashSpeed)
            {
                Crash(piece, CrashKind.Curve);
            }
            else if (piece.kind == RCTrack.PieceKind.Loop)
            {
                track.Evaluate(distance, out _, out _, out var up);
                bool upsideDown = up.y < 0f;
                float progress = (distance - piece.start) / piece.length;
                if ((upsideDown && v * v < trackGravity * piece.radius * -up.y) || (v < 0.05f && progress > 0.08f))
                    Crash(piece, CrashKind.Loop);
            }
        }

        /// Vitesse maximale sans sortie à cet endroit, en tenant compte du freinage avant le prochain virage.
        private float AllowedSpeed(out float toCurve)
        {
            toCurve = track.DistanceToNextCurve(distance, 2f, out _);
            if (toCurve == float.MaxValue) return maxSpeed;
            float safe = curveCrashSpeed;
            return Mathf.Min(maxSpeed, Mathf.Sqrt(safe * safe + 2f * brake * toCurve));
        }

        private float LoopSafeSpeed(RCTrack.Piece loop)
        {
            // Vitesse qui passe le haut du looping, avec la perte d'élan de la montée.
            float top = trackGravity * loop.radius;
            float progress = Mathf.Clamp01((track.Wrap(distance) - loop.start) / loop.length);
            float height = loop.radius * (1f - Mathf.Cos(progress * 2f * Mathf.PI));
            float needed = top + 2f * trackGravity * (2f * loop.radius - height);
            return Mathf.Sqrt(Mathf.Max(0f, needed));
        }

        private void Place()
        {
            track.EvaluateWorld(distance, lane, out var pos, out var rot, out _, out _);
            transform.SetPositionAndRotation(pos, rot);
            if (wheels == null) return;
            float spin = Speed * Time.deltaTime / 0.012f * Mathf.Rad2Deg;
            foreach (var w in wheels) if (w != null) w.Rotate(Vector3.right, spin, Space.Self);
        }

        // Sortie de piste
        private void Crash(RCTrack.Piece piece, CrashKind kind)
        {
            Crashes++;
            track.EvaluateWorld(distance, lane, out _, out _, out var fwd, out var up);
            Vector3 outward = Vector3.Cross(up, fwd).normalized; // virages à gauche : on sort par la droite
            if (kind == CrashKind.Curve)
                flyVelocity = fwd * Speed * 0.8f + outward * Speed * 0.6f + Vector3.up * 0.8f;
            else
                flyVelocity = fwd * Speed * 0.3f + Vector3.up * 0.2f;
            flySpin = new Vector3(UnityEngine.Random.Range(-540f, 540f), UnityEngine.Random.Range(-360f, 360f), UnityEngine.Random.Range(-540f, 540f));
            floorY = track.transform.position.y + 0.02f;
            respawnPiece = piece.respawnPiece;
            crashTimer = crashTime;
            Speed = 0f;
            Warning = 2;
            Crashed?.Invoke(this, piece, kind);
        }

        private void Fly(float dt)
        {
            crashTimer -= dt;
            if (transform.position.y > floorY)
            {
                flyVelocity += Physics.gravity * dt;
                transform.position += flyVelocity * dt;
                transform.Rotate(flySpin * dt, Space.Self);
                // Retombe sur la table si elle est dessous
                float tableTop = track.transform.TransformPoint(new Vector3(0f, track.TableHeight, 0f)).y + 0.01f;
                Vector3 local = track.transform.InverseTransformPoint(transform.position);
                bool overTable = Mathf.Abs(local.x) < 1.5f && local.z > -1f && local.z < 0.8f;
                float ground = overTable ? tableTop : floorY;
                if (transform.position.y < ground && flyVelocity.y < 0f)
                {
                    var p = transform.position;
                    p.y = ground;
                    transform.position = p;
                    flyVelocity = new Vector3(flyVelocity.x * 0.3f, 0f, flyVelocity.z * 0.3f);
                    flySpin *= 0.3f;
                    floorY = ground;
                }
            }
            if (crashTimer <= 0f) Respawn();
        }

        private void Respawn()
        {
            crashTimer = 0f;
            var pieces = track.Pieces;
            int index = Mathf.Clamp(respawnPiece, 0, pieces.Count - 1);
            float target = pieces[index].start + 0.02f;
            // Reposée avant la ligne de départ : le tour sera recompté en la repassant.
            if (target > distance) lapCounter--;
            distance = target;
            Speed = 0f;
            Warning = 0;
            blinkTimer = 0.8f;
            Place();
        }

        private void Blink(float dt)
        {
            if (blinkTimer <= 0f || renderers == null) return;
            blinkTimer -= dt;
            bool visible = blinkTimer <= 0f || Mathf.Repeat(blinkTimer, 0.16f) > 0.08f;
            foreach (var r in renderers) if (r != null) r.enabled = visible;
        }

        // Remise à zéro
        public void ResetCar()
        {
            Lap = 0;
            lapCounter = 0;
            Crashes = 0;
            Speed = 0f;
            Travelled = 0f;
            crashTimer = 0f;
            distance = 0.03f;
            Warning = 0;
            if (renderers != null) foreach (var r in renderers) if (r != null) r.enabled = true;
            if (track != null) Place();
        }

        [ContextMenu("Test : Sortie de piste")]
        private void DebugCrash()
        {
            if (track != null && Application.isPlaying) Crash(track.Pieces[1], CrashKind.Curve);
        }
    }
}
