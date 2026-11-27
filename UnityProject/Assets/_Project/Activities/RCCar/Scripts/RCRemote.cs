using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// La télécommande de la voiture RC (A3). Grip : la prendre (un second appui la lâche).
    /// Prise de près ou de loin, elle vient toujours dans la main : c'est ce script qui la place sur la manette.
    /// Gâchette (index) de la main qui la tient : accélère, plus ou moins selon l'appui. Relâchée : la voiture freine.
    /// Le voyant passe à l'orange avant un virage pris trop vite, au rouge quand la voiture va sortir.
    /// Lâchée, elle revient sur la table.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class RCRemote : MonoBehaviour
    {
        // Réglages
        [SerializeField] private RCCar car;

        [Tooltip("Voyant sur le dessus de la télécommande.")]
        [SerializeField] private MeshRenderer led;

        [Header("Dans la main (réglable pendant le Play)")]
        [Tooltip("Position par rapport à la manette (m). X : droite, Y : haut, Z : avant.")]
        [SerializeField] private Vector3 handOffset = new Vector3(0f, -0.02f, 0.04f);

        [Tooltip("Rotation par rapport à la manette (degrés).")]
        [SerializeField] private Vector3 handRotation = Vector3.zero;

        [Header("Gâchette de secours")]
        [Tooltip("Lue seulement si la manette ne donne pas sa gâchette directement.")]
        [SerializeField] private InputActionProperty rightTrigger = new InputActionProperty(
            new InputAction("Gâchette droite", InputActionType.Value, "<XRController>{RightHand}/trigger"));

        [SerializeField] private InputActionProperty leftTrigger = new InputActionProperty(
            new InputAction("Gâchette gauche", InputActionType.Value, "<XRController>{LeftHand}/trigger"));

        [Header("Vibrations")]
        [SerializeField] private bool haptics = true;

        // État
        public event Action<RCRemote> Grabbed;
        public bool IsHeld => holder != null;
        public float Trigger { get; private set; }

        private static readonly Color Green = new Color(0.2f, 0.9f, 0.3f);
        private static readonly Color Orange = new Color(1f, 0.6f, 0.05f);
        private static readonly Color Red = new Color(1f, 0.1f, 0.1f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private XRGrabInteractable grab;
        private Transform holder;
        private XRBaseInputInteractor hand;
        private bool restoreTrigger;
        private XRBaseInputInteractor.InputTriggerType previousTrigger;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private MaterialPropertyBlock block;
        private int lastWarning = -1;
        private float nextRumble;

        public void Configure(RCCar target, MeshRenderer light)
        {
            car = target;
            led = light;
        }

        // Cycle de vie
        private void Awake()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            block = new MaterialPropertyBlock();
            grab = GetComponent<XRGrabInteractable>();
            // Le toolkit ne déplace plus la télécommande : elle suit la manette (voir FollowHand).
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = false;
            grab.farAttachMode = InteractableFarAttachMode.Near;
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
            if (car == null) car = transform.parent != null ? transform.parent.GetComponentInChildren<RCCar>() : null;
            SetLed(0);
        }

        private void OnEnable()
        {
            Application.onBeforeRender += FollowHand;
            rightTrigger.action?.Enable();
            leftTrigger.action?.Enable();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= FollowHand;
            rightTrigger.action?.Disable();
            leftTrigger.action?.Disable();
        }

        private void OnDestroy()
        {
            if (grab == null) return;
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }

        private void Update()
        {
            Trigger = IsHeld ? ReadTrigger() : 0f;
            if (car != null)
            {
                car.Throttle = Trigger;
                SetLed(IsHeld ? car.Warning : 0);
                Rumble();
            }
            FollowHand();
        }

        private void LateUpdate() => FollowHand();

        // Gâchette
        private float ReadTrigger()
        {
            float value = 0f;
            if (hand != null)
            {
                try { value = hand.activateInput.ReadValue(); }
                catch (Exception) { value = 0f; }
            }
            if (value <= 0.001f)
            {
                bool left = hand != null && hand.handedness == InteractorHandedness.Left;
                var action = left ? leftTrigger.action : rightTrigger.action;
                if (action != null) value = action.ReadValue<float>();
            }
            return Mathf.Clamp01(value);
        }

        // Voyant
        private void SetLed(int warning)
        {
            if (led == null || warning == lastWarning) return;
            lastWarning = warning;
            led.GetPropertyBlock(block);
            block.SetColor(BaseColor, warning == 2 ? Red : warning == 1 ? Orange : Green);
            led.SetPropertyBlock(block);
        }

        // Vibrations : le moteur ronronne avec la vitesse, une sortie de piste secoue fort.
        private void Rumble()
        {
            if (!haptics || !IsHeld || Time.time < nextRumble) return;
            nextRumble = Time.time + 0.1f;
            if (car.IsCrashed) return;
            float amount = car.MaxSpeed > 0f ? car.Speed / car.MaxSpeed : 0f;
            if (amount > 0.05f) Pulse(0.05f + 0.2f * amount, 0.1f);
        }

        public void Pulse(float amplitude, float duration)
        {
            if (!haptics || hand == null) return;
            var node = hand.handedness == InteractorHandedness.Left ? XRNode.LeftHand : XRNode.RightHand;
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid) device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }

        // Dans la main
        private void FollowHand()
        {
            if (holder == null) return;
            transform.SetPositionAndRotation(holder.TransformPoint(handOffset), holder.rotation * Quaternion.Euler(handRotation));
        }

        // Prise et retour
        private void OnGrabbed(SelectEnterEventArgs args)
        {
            holder = args.interactorObject.transform;
            hand = args.interactorObject as XRBaseInputInteractor;
            // Prise à bascule : la télécommande reste en main jusqu'au prochain appui sur le grip.
            if (hand != null)
            {
                previousTrigger = hand.selectActionTrigger;
                restoreTrigger = true;
                hand.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.Toggle;
            }
            FollowHand();
            Grabbed?.Invoke(this);
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (hand != null && restoreTrigger) hand.selectActionTrigger = previousTrigger;
            restoreTrigger = false;
            hand = null;
            holder = null;
            Trigger = 0f;
            if (car != null) car.Throttle = 0f;
            SetLed(0);
            ReturnHome();
        }

        public void ReturnHome() => transform.SetPositionAndRotation(startPosition, startRotation);
    }
}
