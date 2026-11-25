using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Chaise du poste de travail. Viser la chaise et appuyer sur la gâchette : la vue se place
    /// au niveau de l'écran (point « Point_Vue »), même si le visiteur reste debout.
    /// Pousser le joystick gauche (ou viser la chaise et appuyer de nouveau) : il se relève à côté.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class ComputerSeat : MonoBehaviour
    {
        // Réglages
        [Header("Points")]
        [Tooltip("Position et direction des yeux une fois assis (devant l'écran).")]
        [SerializeField] private Transform eyePoint;

        [Tooltip("Endroit où le visiteur se retrouve en se levant.")]
        [SerializeField] private Transform standPoint;

        [Header("Indications")]
        [Tooltip("Texte affiché au-dessus de la chaise quand on la vise.")]
        [SerializeField] private TMP_Text sitHint;

        [Tooltip("Texte affiché sous l'écran une fois assis.")]
        [SerializeField] private TMP_Text standHint;

        [Header("Hauteur des yeux")]
        [Tooltip("Si la tête reste plus loin que cet écart (en m) de la hauteur prévue, la vue est recalée (le visiteur s'est vraiment assis ou relevé).")]
        [SerializeField] private float recalibrateThreshold = 0.2f;

        [Tooltip("Durée en secondes de l'écart avant de recaler la hauteur.")]
        [SerializeField] private float recalibrateDelay = 1f;

        [Header("Se lever")]
        [Tooltip("Joystick qui fait se lever (celui du déplacement).")]
        [SerializeField] private InputActionProperty standAction = new InputActionProperty(
            new InputAction("Se lever", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}", expectedControlType: "Vector2"));

        [Tooltip("Inclinaison du joystick (0 à 1) qui fait se lever.")]
        [SerializeField] private float standStickThreshold = 0.6f;

        [Tooltip("Délai en secondes après s'être assis pendant lequel le joystick est ignoré.")]
        [SerializeField] private float standIgnoreDelay = 0.6f;

        [Header("Événements")]
        public UnityEvent onSat = new UnityEvent();
        public UnityEvent onStood = new UnityEvent();

        // État
        public bool IsSeated { get; private set; }
        public Transform EyePoint => eyePoint;

        private XRSimpleInteractable interactable;
        private XROrigin origin;
        private float savedOriginY;
        private float seatedTime;
        private float offHeightTime;
        private readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
        private CharacterController disabledController;

        // Cycle de vie
        private void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            SetHint(sitHint, false);
            SetHint(standHint, false);
        }

        private void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            if (standAction.reference == null) standAction.action?.Enable();
        }

        private void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            if (standAction.reference == null) standAction.action?.Disable();
            if (IsSeated) StandUp();
        }

        private void Update()
        {
            FaceVisitor(sitHint);
            if (!IsSeated || origin == null) return;

            // Se lever au joystick.
            if (Time.time - seatedTime > standIgnoreDelay)
            {
                var action = standAction.action;
                if (action != null && action.ReadValue<Vector2>().magnitude > standStickThreshold)
                {
                    StandUp();
                    return;
                }
            }

            // Recaler la hauteur si le visiteur s'est assis ou relevé pour de vrai.
            var cam = origin.Camera.transform;
            float gap = eyePoint.position.y - cam.position.y;
            if (Mathf.Abs(gap) > recalibrateThreshold)
            {
                offHeightTime += Time.deltaTime;
                if (offHeightTime > recalibrateDelay)
                {
                    origin.Origin.transform.position += Vector3.up * gap;
                    offHeightTime = 0f;
                }
            }
            else offHeightTime = 0f;
        }

        // Interaction
        private void OnSelected(SelectEnterEventArgs args)
        {
            // Les manettes sont en mode « Toggle » : on relâche tout de suite la chaise.
            if (interactable.interactionManager != null)
                interactable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)interactable);

            if (IsSeated) StandUp();
            else SitDown();
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => SetHint(sitHint, !IsSeated);

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (!interactable.isHovered) SetHint(sitHint, false);
        }

        // Commandes
        [ContextMenu("Test : S'asseoir")]
        public void SitDown()
        {
            if (IsSeated || eyePoint == null) return;
            origin = FindAnyObjectByType<XROrigin>();
            if (origin == null || origin.Camera == null)
            {
                Debug.LogWarning("[Poste] XR Origin introuvable : impossible de s'asseoir.", this);
                return;
            }

            savedOriginY = origin.Origin.transform.position.y;
            FreezeLocomotion(true);

            // Regarder l'écran, puis placer les yeux au point de vue.
            Vector3 forward = Vector3.ProjectOnPlane(eyePoint.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f) origin.MatchOriginUpCameraForward(Vector3.up, forward.normalized);
            origin.MoveCameraToWorldLocation(eyePoint.position);

            IsSeated = true;
            seatedTime = Time.time;
            offHeightTime = 0f;
            SetHint(sitHint, false);
            SetHint(standHint, true);
            onSat.Invoke();
        }

        [ContextMenu("Test : Se lever")]
        public void StandUp()
        {
            if (!IsSeated) return;
            IsSeated = false;

            if (origin != null && origin.Camera != null)
            {
                var originTransform = origin.Origin.transform;
                Vector3 p = originTransform.position;
                originTransform.position = new Vector3(p.x, savedOriginY, p.z);

                if (standPoint != null)
                {
                    Vector3 cam = origin.Camera.transform.position;
                    origin.MoveCameraToWorldLocation(new Vector3(standPoint.position.x, cam.y, standPoint.position.z));
                }
            }

            FreezeLocomotion(false);
            SetHint(standHint, false);
            onStood.Invoke();
        }

        // Déplacements coupés pendant qu'on est assis (gravité, joystick, téléportation, rotation).
        private void FreezeLocomotion(bool freeze)
        {
            if (freeze)
            {
                disabledBehaviours.Clear();
                foreach (var provider in FindObjectsByType<LocomotionProvider>(FindObjectsSortMode.None))
                    Disable(provider);
                foreach (var body in FindObjectsByType<XRBodyTransformer>(FindObjectsSortMode.None))
                    Disable(body);

                var controller = origin.Origin.GetComponent<CharacterController>();
                if (controller != null && controller.enabled)
                {
                    controller.enabled = false;
                    disabledController = controller;
                }
            }
            else
            {
                if (disabledController != null) disabledController.enabled = true;
                disabledController = null;
                foreach (var behaviour in disabledBehaviours)
                    if (behaviour != null) behaviour.enabled = true;
                disabledBehaviours.Clear();
            }
        }

        private void Disable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled) return;
            behaviour.enabled = false;
            disabledBehaviours.Add(behaviour);
        }

        // L'indication au-dessus de la chaise se tourne vers le visiteur.
        private static void FaceVisitor(TMP_Text hint)
        {
            if (hint == null || !hint.gameObject.activeInHierarchy) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = hint.transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) hint.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private static void SetHint(TMP_Text hint, bool visible)
        {
            if (hint != null) hint.gameObject.SetActive(visible);
        }

#if UNITY_EDITOR
        internal void Configure(Transform eye, Transform stand, TMP_Text sit, TMP_Text standText)
        {
            eyePoint = eye;
            standPoint = stand;
            sitHint = sit;
            standHint = standText;
        }
#endif
    }
}
