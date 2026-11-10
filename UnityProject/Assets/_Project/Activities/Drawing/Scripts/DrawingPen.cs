using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public class DrawingPen : MonoBehaviour
    {
        // Réglages
        [SerializeField] private DrawingBoard board;

        [Tooltip("Pointe du stylet : c'est elle qui touche l'écran.")]
        [SerializeField] private Transform tip;

        [Tooltip("Partie colorée de la pointe, qui prend la couleur choisie.")]
        [SerializeField] private Renderer tipRenderer;

        [Tooltip("Support où le stylet revient quand on le lâche.")]
        [SerializeField] private Transform home;

        [Header("Pointeur")]
        [Tooltip("Distance maximale du pointeur laser pour choisir un outil à distance.")]
        [SerializeField] private float pointerRange = 4f;
        [SerializeField] private float pointerWidth = 0.003f;

        // État
        private XRGrabInteractable grab;
        private MaterialPropertyBlock block;
        private Color shownColor;
        private bool touching;
        private bool triggerHeld;
        private bool triggerPressedThisFrame;
        private LineRenderer pointer;

        // Cycle de vie
        private void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            grab.selectExited.AddListener(OnReleased);
            grab.activated.AddListener(OnTriggerPressed);
            grab.deactivated.AddListener(OnTriggerReleased);
            block = new MaterialPropertyBlock();
            CreatePointer();
            ReturnHome();
        }

        private void OnDestroy()
        {
            if (grab == null) return;
            grab.selectExited.RemoveListener(OnReleased);
            grab.activated.RemoveListener(OnTriggerPressed);
            grab.deactivated.RemoveListener(OnTriggerReleased);
        }

        private void Update()
        {
            if (board == null || tip == null) return;
            UpdateTipColor();

            bool pressed = triggerPressedThisFrame;
            triggerPressedThisFrame = false;

            if (!grab.isSelected)
            {
                StopTouching();
                ShowPointer(false, Vector3.zero);
                return;
            }

            bool inContact = board.TryGetContact(tip.position, out Vector3 local);
            if (!touching && !inContact)
            {
                bool aiming = board.RaycastBoard(tip.position, tip.forward, pointerRange, out Vector3 hit, out BoardButton button);
                ShowPointer(aiming, hit);
                if (pressed && button != null) board.PressButton(button);
                return;
            }

            ShowPointer(false, Vector3.zero);
            if (!triggerHeld)
            {
                StopTouching();
                return;
            }

            if (inContact)
            {
                if (!touching)
                {
                    touching = true;
                    board.BeginTouch(local);
                }
                else
                {
                    board.MoveTouch(local);
                }
            }
            else
            {
                StopTouching();
            }
        }

        // Contact
        private void StopTouching()
        {
            if (!touching) return;
            touching = false;
            board.EndTouch();
        }

        // Gâchette
        private void OnTriggerPressed(ActivateEventArgs args)
        {
            triggerHeld = true;
            triggerPressedThisFrame = true;
        }
        private void OnTriggerReleased(DeactivateEventArgs args) => triggerHeld = false;

        // Retour au support
        private void OnReleased(SelectExitEventArgs args)
        {
            triggerHeld = false;
            StopTouching();
            ReturnHome();
        }

        private void ReturnHome()
        {
            if (home == null) return;
            transform.SetPositionAndRotation(home.position, home.rotation);
        }

        // Pointeur
        private void CreatePointer()
        {
            if (board == null || tip == null) return;
            var pointerObject = new GameObject("Pointeur");
            pointerObject.transform.SetParent(transform, false);
            pointer = pointerObject.AddComponent<LineRenderer>();
            pointer.useWorldSpace = true;
            pointer.sharedMaterial = board.LineMaterial;
            pointer.widthMultiplier = pointerWidth;
            pointer.positionCount = 2;
            pointer.numCapVertices = 2;
            pointer.sortingOrder = 32000;
            pointer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pointer.receiveShadows = false;
            pointer.enabled = false;
        }

        private void ShowPointer(bool visible, Vector3 hit)
        {
            if (pointer == null) return;
            pointer.enabled = visible;
            if (!visible) return;

            Color start = board.CurrentColor;
            Color end = start;
            end.a = 0.2f;
            pointer.startColor = start;
            pointer.endColor = end;
            pointer.SetPosition(0, tip.position);
            pointer.SetPosition(1, hit);
        }

        // Apparence
        private void UpdateTipColor()
        {
            if (tipRenderer == null || board.CurrentColor == shownColor) return;
            shownColor = board.CurrentColor;
            tipRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", shownColor);
            block.SetColor("_Color", shownColor);
            tipRenderer.SetPropertyBlock(block);
        }
    }
}
