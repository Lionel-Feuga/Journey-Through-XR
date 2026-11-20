using UnityEngine;
using UnityEngine.XR;

namespace ObsidienneVR
{
    /// <summary>Unity XR input adapter; no dependency on a specific XR Interaction Toolkit version.</summary>
    public sealed class ObsidienneXRInput : MonoBehaviour
    {
        public ObsidienneController saber;
        public XRNode hand = XRNode.RightHand;
        [Tooltip("Tracked hand/controller Transform from your XR rig, assigned per scene.")]
        public Transform trackedController;
        [Tooltip("Optional: map primary/secondary buttons to blade A/B ignition.")]
        public bool useFaceButtons = true;
        [Range(0,1)] public float triggerPressPoint = 0.75f;
        [SerializeField] private bool held;
        private bool previousA, previousB;
        private bool hadValidInput;

        // Bind Select Entered -> Grab, Select Exited -> Release on your grab interactable.
        public void Grab() { SetHeld(true); }
        public void Release() { SetHeld(false); }
        public void SetHeld(bool value)
        {
            held = value; hadValidInput = false; previousA = previousB = false;
            if (!held && saber) saber.SetTrackingActive(false);
        }
        private void Update()
        {
            if (!saber) return;
            InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
            bool valid = held && trackedController && device.isValid &&
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
            saber.rotationSource = trackedController;
            saber.SetTrackingActive(valid);
            if (!valid) { hadValidInput = false; return; }
            bool pressed;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out float trigger)) pressed = trigger >= triggerPressPoint;
            else { device.TryGetFeatureValue(CommonUsages.triggerButton, out pressed); }
            saber.SetTriggerHeld(pressed);
            device.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
            device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
            if (useFaceButtons && hadValidInput)
            {
                if (a && !previousA) saber.ToggleBladeA();
                if (b && !previousB) saber.ToggleBladeB();
            }
            previousA = a; previousB = b; hadValidInput = true;
        }
        private void OnDisable() { if (saber) saber.SetTrackingActive(false); hadValidInput = false; }
    }
}
