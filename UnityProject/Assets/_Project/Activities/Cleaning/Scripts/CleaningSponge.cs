using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// L'éponge des sanitaires. Grip : la prendre (un second appui la lâche).
    /// Prise de près ou de loin, elle vient toujours dans la main : c'est ce script qui la place
    /// sur la manette, quel que soit le réglage du rayon. Pour frotter, il faut donc s'approcher.
    /// Lâchée, elle revient sur son support (le bord du lavabo).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class CleaningSponge : MonoBehaviour
    {
        // Réglages
        [Tooltip("Zones sales que cette éponge peut nettoyer.")]
        [SerializeField] private DirtSpot[] spots;

        [Tooltip("Endroit où l'éponge revient quand on la lâche. Vide : là où l'éponge est posée dans la scène.")]
        [SerializeField] private Transform home;

        [Tooltip("Coché : l'éponge revient là où elle est posée dans la scène (on la déplace dans l'éditeur, son support suit). Décoché : sur « Home ».")]
        [SerializeField] private bool homeIsStartPose = true;

        [Tooltip("Face de l'éponge qui frotte (son centre). Vide : le centre de l'éponge.")]
        [SerializeField] private Transform scrubPoint;

        [Header("Dans la main (réglable pendant le Play)")]
        [Tooltip("Position de l'éponge par rapport à la manette (m). X : droite, Y : haut, Z : avant.")]
        [SerializeField] private Vector3 handOffset = new Vector3(0f, -0.02f, 0.03f);

        [Tooltip("Rotation de l'éponge par rapport à la manette (degrés). Z = 180 : retournée, côté vert foncé vers le bas.")]
        [SerializeField] private Vector3 handRotation = new Vector3(0f, 0f, 180f);

        // État
        public event Action<CleaningSponge> Grabbed;
        public bool IsHeld => holder != null;

        private XRGrabInteractable grab;
        private Transform holder;
        private Vector3 startPosition;
        private Quaternion startRotation;

        public void Configure(DirtSpot[] roomSpots, Transform homePoint, Transform scrub)
        {
            spots = roomSpots;
            home = homePoint;
            scrubPoint = scrub;
        }

        public void SetSpots(DirtSpot[] roomSpots) => spots = roomSpots;

        // Cycle de vie
        private void Awake()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            grab = GetComponent<XRGrabInteractable>();
            // Le toolkit ne déplace plus l'éponge : elle suit la manette (voir FollowHand).
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = false;
            grab.farAttachMode = InteractableFarAttachMode.Near;
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
            ReturnHome();
        }

        private void OnEnable() => Application.onBeforeRender += FollowHand;
        private void OnDisable() => Application.onBeforeRender -= FollowHand;

        private void OnDestroy()
        {
            if (grab == null) return;
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }

        private void Update()
        {
            if (!IsHeld) return;
            FollowHand();
            if (spots == null) return;
            Vector3 p = scrubPoint != null ? scrubPoint.position : transform.position;
            foreach (var spot in spots)
                if (spot != null) spot.RubWith(p);
        }

        private void LateUpdate() => FollowHand();

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
            FollowHand();
            Grabbed?.Invoke(this);
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            holder = null;
            ReturnHome();
        }

        public void ReturnHome()
        {
            if (homeIsStartPose || home == null) transform.SetPositionAndRotation(startPosition, startRotation);
            else transform.SetPositionAndRotation(home.position, home.rotation);
        }
    }
}
