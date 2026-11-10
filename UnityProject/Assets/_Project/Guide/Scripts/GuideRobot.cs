using System;
using System.Collections;
using System.Collections.Generic;
using JourneyThroughXR.Core;
using JourneyThroughXR.UI;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Guide
{
    public enum GuideState
    {
        Idle,
        Moving,
        WaitingForVisitor,
        AtStop,
        Finished
    }

    public class GuideRobot : MonoBehaviour
    {
        // Réglages
        [Header("Parcours")]
        [SerializeField] private GuideRoute route;

        [Tooltip("Démarre seul au lancement. Sinon, appeler StartGuiding (ex. depuis l'ascenseur).")]
        [SerializeField] private bool startOnPlay;

        [Header("Déplacement")]
        [Tooltip("Vitesse en mètres par seconde.")]
        [SerializeField] private float speed = 1.2f;

        [Tooltip("Vitesse de rotation en degrés par seconde.")]
        [SerializeField] private float turnSpeed = 240f;

        [Header("Visiteur")]
        [Tooltip("Distance à laquelle le visiteur est considéré comme arrivé à une étape.")]
        [SerializeField] private float joinDistance = 2.5f;

        [Tooltip("Distance maximale d'avance sur le visiteur avant de l'attendre.")]
        [SerializeField] private float maxLead = 5f;

        [Header("Dialogue")]
        [Tooltip("Bulle de dialogue. Vide : celle du robot est trouvée seule.")]
        [SerializeField] private GuideSpeechBubble speech;

        [Tooltip("Le robot commence à parler quand le visiteur est à moins de cette distance.")]
        [SerializeField] private float talkDistance = 3f;

        [Tooltip("Fenêtre de tutoriel des activités. Vide : celle de la scène est trouvée seule.")]
        [SerializeField] private TutorialWindow tutorial;

        [Header("Apparence")]
        [SerializeField] private Transform visual;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float hoverAmplitude = 0.05f;
        [SerializeField] private Color bodyColor = new Color(0.92f, 0.93f, 0.95f);
        [SerializeField] private Color accentColor = new Color(0.95f, 0.15f, 0.45f);

        [Header("Événements")]
        public UnityEvent onStarted = new UnityEvent();
        public UnityEvent onFinished = new UnityEvent();

        public event Action<GuideWaypoint> StopReached;

        // État
        public GuideState State { get; private set; } = GuideState.Idle;
        public GuideWaypoint CurrentStop { get; private set; }
        public GuideRoute Route => route;

        private Transform visitor;
        private Coroutine running;

        // Cycle de vie
        private void Awake()
        {
            if (speech == null) speech = GetComponent<GuideSpeechBubble>();
            if (tutorial == null) tutorial = FindAnyObjectByType<TutorialWindow>();
            ApplyColors();
        }

        private void Start()
        {
            PlaceAtFirstPoint();
            if (startOnPlay) StartGuiding();
        }

        private void Update()
        {
            Hover();
            if (State == GuideState.Idle || State == GuideState.Finished) FaceVisitor();
        }

        // Commandes
        public void StartGuiding()
        {
            if (running != null || route == null) return;
            onStarted.Invoke();
            running = StartCoroutine(Run());
        }

        public void StopGuiding()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            State = GuideState.Idle;
        }

        // Parcours
        private IEnumerator Run()
        {
            IReadOnlyList<GuideWaypoint> points = route.Waypoints;

            for (int i = 0; i < points.Count; i++)
            {
                GuideWaypoint waypoint = points[i];
                if (i > 0) yield return MoveTo(waypoint.transform.position);
                if (!waypoint.IsStop) continue;

                CurrentStop = waypoint;
                waypoint.onRobotArrived.Invoke();
                StopReached?.Invoke(waypoint);

                State = GuideState.AtStop;
                bool spoke = waypoint.HasDialogue && speech != null;
                if (spoke) yield return Speak(waypoint);

                State = GuideState.WaitingForVisitor;
                while (!spoke && !VisitorWithin(joinDistance))
                {
                    FaceVisitor();
                    yield return null;
                }
                waypoint.onVisitorJoined.Invoke();

                State = GuideState.AtStop;
                if (waypoint.WaitForActivity != null)
                {
                    yield return ShowTutorial(waypoint.WaitForActivity);
                    if (waypoint.ConfirmWithRobot && speech != null)
                        yield return WaitForConfirmation(waypoint, waypoint.WaitForActivity);
                    while (!waypoint.WaitForActivity.IsCompleted)
                    {
                        FaceVisitor();
                        yield return null;
                    }
                }

                yield return Wait(waypoint.PauseAtStop);
                waypoint.onRobotLeft.Invoke();
            }

            State = GuideState.Finished;
            running = null;
            onFinished.Invoke();
        }

        private IEnumerator Speak(GuideWaypoint waypoint)
        {
            while (!VisitorWithin(talkDistance))
            {
                FaceVisitor();
                yield return null;
            }

            yield return Wait(waypoint.DialogueDelay);
            speech.SayLines(waypoint.Dialogue);
            while (speech.IsSpeaking)
            {
                FaceVisitor();
                yield return null;
            }
        }

        private IEnumerator ShowTutorial(Activity activity)
        {
            if (activity.State != ActivityState.NotStarted) yield break;

            if (tutorial == null || string.IsNullOrWhiteSpace(activity.TutorialText))
            {
                activity.StartActivity();
                yield break;
            }

            tutorial.Show(activity, transform);
            while (tutorial.IsOpen)
            {
                FaceVisitor();
                yield return null;
            }
        }

        private IEnumerator WaitForConfirmation(GuideWaypoint waypoint, Activity activity)
        {
            speech.SayLines(waypoint.ReleaseLines);
            while (speech.IsSpeaking)
            {
                FaceVisitor();
                yield return null;
            }

            bool visitorLeft = false;
            bool asking = false;
            float start = Time.time;

            while (!activity.IsCompleted)
            {
                FaceVisitor();
                bool near = VisitorWithin(talkDistance);
                if (!near) visitorLeft = true;

                if (asking)
                {
                    if (!near)
                    {
                        speech.Hide();
                        asking = false;
                    }
                    else if (!speech.IsSpeaking)
                    {
                        if (speech.LastAnswer)
                        {
                            activity.CompleteActivity();
                            break;
                        }

                        asking = false;
                        visitorLeft = false;
                        speech.SayLines(waypoint.NotYetLines);
                        while (speech.IsSpeaking)
                        {
                            FaceVisitor();
                            yield return null;
                        }
                        start = Time.time;
                    }
                }
                else if (near && (visitorLeft || Time.time - start >= waypoint.ConfirmDelay))
                {
                    string question = waypoint.ConfirmLines != null && waypoint.ConfirmLines.Length > 0
                        ? waypoint.ConfirmLines[0] : "On passe à la suite ?";
                    speech.Ask(question, waypoint.YesLabel, waypoint.NoLabel);
                    asking = true;
                }

                yield return null;
            }
        }

        private IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                FaceVisitor();
                yield return null;
            }
        }

        private IEnumerator MoveTo(Vector3 target)
        {
            target.y = transform.position.y;

            while ((target - transform.position).sqrMagnitude > 0.0025f)
            {
                if (!VisitorWithin(maxLead))
                {
                    State = GuideState.WaitingForVisitor;
                    FaceVisitor();
                    yield return null;
                    continue;
                }

                State = GuideState.Moving;
                Vector3 direction = target - transform.position;
                TurnTowards(direction);
                transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
                yield return null;
            }
        }

        // Orientation
        private void FaceVisitor()
        {
            if (!FindVisitor()) return;
            TurnTowards(visitor.position - transform.position);
        }

        private void TurnTowards(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            Quaternion goal = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, turnSpeed * Time.deltaTime);
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

        private bool VisitorWithin(float distance)
        {
            if (!FindVisitor()) return true;
            Vector3 offset = visitor.position - transform.position;
            offset.y = 0f;
            return offset.magnitude <= distance;
        }

        // Placement
        private void PlaceAtFirstPoint()
        {
            if (route == null) return;
            var points = route.Waypoints;
            if (points.Count == 0) return;
            transform.position = points[0].transform.position;
        }

        // Apparence
        private void Hover()
        {
            if (visual == null) return;
            float offset = hoverHeight + Mathf.Sin(Time.time * 2f) * hoverAmplitude;
            visual.localPosition = new Vector3(0f, offset, 0f);
        }

        private void OnValidate() => ApplyColors();

        private void ApplyColors()
        {
            if (visual == null) return;
            var block = new MaterialPropertyBlock();
            foreach (var meshRenderer in visual.GetComponentsInChildren<Renderer>())
            {
                string partName = meshRenderer.name;
                bool accent = StartsWithAny(partName, "Oeil", "Pupille", "Antenne", "Point");
                bool body = StartsWithAny(partName, "Corps", "Tete", "Bras");
                if (!accent && !body) continue;
                Color color = accent ? accentColor : bodyColor;
                meshRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                meshRenderer.SetPropertyBlock(block);
            }
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (value.StartsWith(prefix)) return true;
            return false;
        }

        [ContextMenu("Créer le corps en primitives")]
        private void CreateBody()
        {
            if (visual != null)
            {
                Debug.LogWarning("[Robot] Le corps existe déjà.", this);
                return;
            }

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(visual.gameObject, "Créer le robot");
            UnityEditor.Undo.RecordObject(this, "Créer le robot");
#endif
            AddPart(PrimitiveType.Capsule, "Corps", new Vector3(0f, 0.55f, 0f), new Vector3(0.45f, 0.4f, 0.45f));
            AddPart(PrimitiveType.Sphere, "Tete", new Vector3(0f, 1.1f, 0f), new Vector3(0.4f, 0.4f, 0.4f));
            AddPart(PrimitiveType.Cube, "Oeil_G", new Vector3(-0.08f, 1.13f, 0.18f), new Vector3(0.06f, 0.08f, 0.04f));
            AddPart(PrimitiveType.Cube, "Oeil_D", new Vector3(0.08f, 1.13f, 0.18f), new Vector3(0.06f, 0.08f, 0.04f));
            AddPart(PrimitiveType.Cylinder, "Antenne", new Vector3(0f, 1.38f, 0f), new Vector3(0.03f, 0.08f, 0.03f));
            ApplyColors();
        }

        private void AddPart(PrimitiveType type, string partName, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;
            DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(visual, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
        }

        // Menus de test
        [ContextMenu("Test : Démarrer le guidage")] private void DebugStart() => StartGuiding();
    }
}
