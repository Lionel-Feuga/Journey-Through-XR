using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace JourneyThroughXR.Activities
{
    /// <summary>Réglages d'apparence modifiables en jeu (voir SaberTuningMenu).</summary>
    public enum SaberSetting
    {
        BladeIntensity,
        BladeCore,
        BladeCoreIntensity,
        GlowIntensity,
        GlowFalloff,
        GlowOpacity,
        TrailIntensity,
        TrailDuration,
        LightIntensity,
        LightRange,
        Flicker,
        FlickerSpeed,
        IgniteDuration,
        HoldDuration,
        GripTilt,
        GripOffset,
        Mass,
        ThrowBoost,
        RespawnFallDistance,
        GripRoll
    }

    /// <summary>
    /// Sabre laser. Une pression sur le grip le saisit, une deuxième le lâche.
    /// Une fois en main, la gâchette l'allume, une deuxième pression l'éteint.
    /// Sabre double : première pression, la lame du haut ; deuxième, celle du bas ; troisième, extinction.
    /// Une pression maintenue sort les deux lames d'un coup (ou les éteint si elles sont déjà sorties).
    /// Retour au point de départ : touche Retour arrière, bouton A / X, ou chute hors de la scène.
    /// À poser sur la racine du modèle LaserSaber (enfants Saber_Blade et Saber_Glow).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class LaserSaber : MonoBehaviour
    {
        // Réglages
        [Header("Lame")]
        [Tooltip("Objet Saber_Blade (pivot à la base de la lame).")]
        [SerializeField] private Transform blade;

        [Tooltip("Objet Saber_Glow (halo autour de la lame).")]
        [SerializeField] private Transform glow;

        [Tooltip("Sabre double uniquement : objet Saber_Blade_2, la lame de l'autre bout.")]
        [SerializeField] private Transform blade2;

        [Tooltip("Sabre double uniquement : objet Saber_Glow_2.")]
        [SerializeField] private Transform glow2;

        [Tooltip("Pour un modèle sans halo : matériau SaberGlow. Le halo est alors créé autour de chaque lame au lancement.")]
        [SerializeField] private Material autoGlowMaterial;

        [Tooltip("Largeur du halo créé automatiquement, en multiple de l'épaisseur de la lame.")]
        [SerializeField] private float autoGlowWidth = 3.4f;

        [ColorUsage(false, true)]
        [SerializeField] private Color bladeColor = new Color(0.1f, 0.45f, 1f);

        [Header("Lumière projetée sur le décor")]
        [Tooltip("Crée une lumière ponctuelle de la couleur de la lame. C'est elle qui colore le sol et les objets proches.")]
        [SerializeField] private bool castLight = true;
        [SerializeField] private float lightIntensity = 2f;
        [SerializeField] private float lightRange = 1.3f;

        [Tooltip("Position de la lumière créée automatiquement, par rapport à la racine du sabre.")]
        [SerializeField] private Vector3 lightOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Optionnel : lumière existante à utiliser à la place de celle créée automatiquement.")]
        [SerializeField] private Light bladeLight;

        [Header("Traînée")]
        [Tooltip("Matériau créé à partir du shader SaberTrail. Vide : pas de traînée.")]
        [SerializeField] private Material trailMaterial;

        [Tooltip("Durée de vie de la traînée, en secondes. Plus long : ruban plus étendu derrière la lame.")]
        [Range(0.02f, 0.6f)]
        [SerializeField] private float trailDuration = 0.05f;

        [Header("Allumage")]
        [SerializeField] private float igniteDuration = 0.25f;

        [Tooltip("Sabre double : durée d'appui, en secondes, à partir de laquelle la pression compte comme maintenue.")]
        [SerializeField] private float holdDuration = 0.35f;

        [Header("Scintillement")]
        [Range(0f, 0.3f)]
        [SerializeField] private float flickerAmount = 0.3f;
        [SerializeField] private float flickerSpeed = 25f;

        [Header("Prise en main")]
        [Tooltip("Inclinaison de la lame vers le haut par rapport à la manette, en degrés.")]
        [Range(0f, 90f)]
        [SerializeField] private float gripTilt = 35f;

        [Tooltip("Décale la manette le long du manche, en mètres. Positif : vers l'émetteur. Négatif : vers le pommeau.")]
        [Range(-0.12f, 0.12f)]
        [SerializeField] private float gripOffset = 0.07f;

        [Tooltip("Rotation du sabre dans la main autour de l'axe de la lame, en degrés.")]
        [Range(-180f, 180f)]
        [SerializeField] private float gripRoll = 0f;

        [Header("Physique")]
        [Tooltip("Masse en kg. Joue sur les chocs avec les autres objets, pas sur la distance de lancer.")]
        [SerializeField] private float mass = 0.3f;

        [Tooltip("Multiplie la vitesse de la main au moment du lâcher. Plus haut : le sabre part plus loin.")]
        [Range(0.5f, 4f)]
        [SerializeField] private float throwBoost = 2.2f;

        [Header("Retour au point de départ")]
        [Tooltip("Le sabre revient tout seul s'il tombe de cette hauteur (en mètres) sous son point de départ.")]
        [SerializeField] private float respawnFallDistance = 5f;

        // État
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int CorePowerId = Shader.PropertyToID("_CorePower");
        private static readonly int CoreIntensityId = Shader.PropertyToID("_CoreIntensity");
        private static readonly int FalloffId = Shader.PropertyToID("_Falloff");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        // Valeurs imposées en jeu par le menu de réglage. Vide : le matériau garde la main.
        private readonly float?[] materialOverrides = new float?[7];

        private XRGrabInteractable grab;
        // Lames et halos, avec l'axe le long duquel chacun s'étire (lu sur le maillage).
        private BladePart bladePart;
        private BladePart glowPart;
        private BladePart blade2Part;
        private BladePart glow2Part;
        private bool pressStartedIgnited;
        private bool regripping;
        private MaterialPropertyBlock block;
        private bool ignited;
        private float level;
        private bool ignited2;
        private float level2;
        private bool triggerHeld;
        private bool holdHandled;
        private float triggerPressTime;
        private Transform attach;
        private Rigidbody body;

        // Traînées : une par lame.
        private const int TrailSamples = 32;
        private readonly List<BladeTrail> trails = new List<BladeTrail>();
        private Vector3 startPosition;
        private Quaternion startRotation;

        // Manettes passées en saisie « à bascule » pour ce sabre, avec leur réglage d'origine.
        private readonly Dictionary<XRBaseInputInteractor, XRBaseInputInteractor.InputTriggerType> toggledHands =
            new Dictionary<XRBaseInputInteractor, XRBaseInputInteractor.InputTriggerType>();
        private readonly List<XRBaseInputInteractor> handsToRestore = new List<XRBaseInputInteractor>();

        public bool IsIgnited => ignited || ignited2;
        public bool IsBlade1Ignited => ignited;
        public bool IsBlade2Ignited => ignited2;

        /// <summary>Manette qui tient le sabre, ou null s'il n'est pas en main.</summary>
        public Transform Holder { get; private set; }

        /// <summary>Vrai tant que la gâchette est enfoncée (sabre double uniquement).</summary>
        public bool IsTriggerHeld => triggerHeld;

        /// <summary>Vrai si une lame était déjà allumée quand la pression en cours a commencé.</summary>
        public bool PressStartedIgnited => pressStartedIgnited;

        public float HoldDuration => holdDuration;

        /// <summary>Faux si le sabre a son propre point de prise (Obsidienne) : Grip Tilt et Grip Offset sont alors sans effet.</summary>
        public bool UsesGripSettings => attach != null;

        /// <summary>
        /// Si vrai, la pression maintenue n'agit plus sur les lames dès qu'une lame est allumée :
        /// elle est réservée à un autre mécanisme (déverrouillage du rotor de l'Obsidienne).
        /// </summary>
        public bool HoldReservedWhenIgnited { get; set; }

        /// <summary>Couleur de la lame, du halo, de la traînée et de la lumière projetée.</summary>
        public Color BladeColor
        {
            get => bladeColor;
            set => bladeColor = value;
        }

        // Cycle de vie
        private void Reset()
        {
            // Pré-réglage automatique quand on ajoute le composant dans l'éditeur.
            blade = FindChild("Saber_Blade");
            glow = FindChild("Saber_Glow");
            blade2 = FindChild("Saber_Blade_2");
            glow2 = FindChild("Saber_Glow_2");

            if (GetComponentInChildren<Collider>() == null)
            {
                // Collision ajustée à la poignée du modèle (plus longue sur un sabre double).
                var capsule = gameObject.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = 0.022f;
                capsule.height = 0.28f;

                var hilt = FindChild("Saber_Hilt");
                var hiltMesh = hilt != null ? hilt.GetComponent<MeshFilter>() : null;
                if (hiltMesh != null && hiltMesh.sharedMesh != null)
                {
                    Bounds bounds = hiltMesh.sharedMesh.bounds;
                    capsule.center = new Vector3(0f, bounds.center.y, 0f);
                    capsule.height = bounds.size.y;
                }
            }

            var grabInteractable = GetComponent<XRGrabInteractable>();
            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.useDynamicAttach = false;
        }

        private void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
            grab.activated.AddListener(OnTriggerPressed);
            grab.deactivated.AddListener(OnTriggerReleased);

            block = new MaterialPropertyBlock();
            bladePart = MakePart(blade);
            blade2Part = MakePart(blade2);
            glowPart = glow != null ? MakePart(glow) : CreateGlow(bladePart);
            glow2Part = glow2 != null ? MakePart(glow2) : CreateGlow(blade2Part);

            body = GetComponent<Rigidbody>();
            body.mass = mass;
            // Évite que le sabre traverse le sol quand il va vite.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            grab.throwVelocityScale = throwBoost;

            startPosition = transform.position;
            startRotation = transform.rotation;

            CreateLight();
            CreateTrail(bladePart, false);
            CreateTrail(blade2Part, true);

            CreateAttachPoint();
            Apply();
        }

        private void OnValidate()
        {
            if (body != null) body.mass = mass;
            if (grab != null) grab.throwVelocityScale = throwBoost;

            // Permet d'ajuster la prise en main en mode Play (relâcher puis ressaisir pour voir l'effet).
            UpdateAttachPoint();
        }

        private void OnDisable()
        {
            foreach (var pair in toggledHands)
            {
                if (pair.Key != null) pair.Key.selectActionTrigger = pair.Value;
            }
            toggledHands.Clear();
        }

        private void LateUpdate()
        {
            foreach (var trail in trails) trail.Update(this);
        }

        private void OnDestroy()
        {
            foreach (var trail in trails) trail.Destroy();
            trails.Clear();
            if (grab == null) return;
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
            grab.activated.RemoveListener(OnTriggerPressed);
            grab.deactivated.RemoveListener(OnTriggerReleased);
        }

        private void Update()
        {
            RestoreReleasedHands();
            if (RespawnRequested()) Respawn();

            // Sabre double : la pression devient « maintenue » dès que la durée est atteinte, sans attendre le relâchement.
            if (triggerHeld && !holdHandled && Time.time - triggerPressTime >= holdDuration)
            {
                holdHandled = true;
                if (!(HoldReservedWhenIgnited && pressStartedIgnited))
                {
                    bool bothOut = ignited && ignited2;
                    ignited = !bothOut;
                    ignited2 = !bothOut;
                }
            }

            bool settled = Mathf.Approximately(level, ignited ? 1f : 0f) && Mathf.Approximately(level2, ignited2 ? 1f : 0f);
            if (settled && !IsIgnited) return;

            float step = igniteDuration > 0f ? Time.deltaTime / igniteDuration : 1f;
            level = Mathf.MoveTowards(level, ignited ? 1f : 0f, step);
            level2 = Mathf.MoveTowards(level2, ignited2 ? 1f : 0f, step);
            Apply();
        }

        // Réglages en jeu
        public float GetSetting(SaberSetting setting)
        {
            switch (setting)
            {
                case SaberSetting.BladeIntensity: return MaterialSetting(setting, RendererOf(bladePart), IntensityId);
                case SaberSetting.BladeCore: return MaterialSetting(setting, RendererOf(bladePart), CorePowerId);
                case SaberSetting.BladeCoreIntensity: return MaterialSetting(setting, RendererOf(bladePart), CoreIntensityId);
                case SaberSetting.GlowIntensity: return MaterialSetting(setting, RendererOf(glowPart), IntensityId);
                case SaberSetting.GlowFalloff: return MaterialSetting(setting, RendererOf(glowPart), FalloffId);
                case SaberSetting.GlowOpacity: return MaterialSetting(setting, RendererOf(glowPart), OpacityId);
                case SaberSetting.TrailIntensity: return MaterialSetting(setting, trails.Count > 0 ? trails[0].Renderer : null, IntensityId);
                case SaberSetting.TrailDuration: return trailDuration;
                case SaberSetting.LightIntensity: return lightIntensity;
                case SaberSetting.LightRange: return lightRange;
                case SaberSetting.Flicker: return flickerAmount;
                case SaberSetting.FlickerSpeed: return flickerSpeed;
                case SaberSetting.IgniteDuration: return igniteDuration;
                case SaberSetting.HoldDuration: return holdDuration;
                case SaberSetting.GripTilt: return gripTilt;
                case SaberSetting.GripOffset: return gripOffset;
                case SaberSetting.GripRoll: return gripRoll;
                case SaberSetting.Mass: return mass;
                case SaberSetting.ThrowBoost: return throwBoost;
                case SaberSetting.RespawnFallDistance: return respawnFallDistance;
                default: return 0f;
            }
        }

        public void SetSetting(SaberSetting setting, float value)
        {
            switch (setting)
            {
                case SaberSetting.TrailDuration: trailDuration = Mathf.Max(0.02f, value); break;
                case SaberSetting.LightIntensity: lightIntensity = value; break;
                case SaberSetting.LightRange: lightRange = value; break;
                case SaberSetting.Flicker: flickerAmount = value; break;
                case SaberSetting.FlickerSpeed: flickerSpeed = value; break;
                case SaberSetting.IgniteDuration: igniteDuration = Mathf.Max(0f, value); break;
                case SaberSetting.HoldDuration: holdDuration = Mathf.Max(0.1f, value); break;
                case SaberSetting.GripTilt: gripTilt = value; UpdateAttachPoint(); RefreshGrip(); break;
                case SaberSetting.GripOffset: gripOffset = value; UpdateAttachPoint(); RefreshGrip(); break;
                case SaberSetting.GripRoll: gripRoll = value; UpdateAttachPoint(); RefreshGrip(); break;
                case SaberSetting.Mass:
                    mass = Mathf.Max(0.01f, value);
                    if (body != null) body.mass = mass;
                    break;
                case SaberSetting.ThrowBoost:
                    throwBoost = value;
                    if (grab != null) grab.throwVelocityScale = throwBoost;
                    break;
                case SaberSetting.RespawnFallDistance: respawnFallDistance = value; break;
                default: materialOverrides[(int)setting] = value; break;
            }
        }

        private float MaterialSetting(SaberSetting setting, Renderer partRenderer, int propertyId)
        {
            float? forced = materialOverrides[(int)setting];
            if (forced.HasValue) return forced.Value;

            var material = partRenderer != null ? partRenderer.sharedMaterial : null;
            return material != null && material.HasProperty(propertyId) ? material.GetFloat(propertyId) : 0f;
        }

        private void ApplyOverride(SaberSetting setting, int propertyId)
        {
            float? forced = materialOverrides[(int)setting];
            if (forced.HasValue) block.SetFloat(propertyId, forced.Value);
        }

        /// <summary>
        /// Applique tout de suite un changement du point de prise à un sabre tenu en main.
        /// Le toolkit ne relit la prise qu'au moment de la saisie : on lâche et on ressaisit dans la foulée,
        /// sans éteindre les lames ni lancer le sabre.
        /// </summary>
        public void RefreshGrip()
        {
            if (grab == null || !grab.isSelected || grab.interactionManager == null) return;

            IXRSelectInteractor hand = grab.interactorsSelecting[0];
            regripping = true;
            try
            {
                grab.interactionManager.SelectExit(hand, (IXRSelectInteractable)grab);
                grab.interactionManager.SelectEnter(hand, (IXRSelectInteractable)grab);
            }
            finally
            {
                regripping = false;
            }
        }

        // Commandes
        public void Ignite()
        {
            ignited = true;
            ignited2 = blade2 != null;
        }

        public void Extinguish()
        {
            ignited = false;
            ignited2 = false;
            triggerHeld = false;
        }

        /// <summary>Ramène le sabre, éteint et immobile, à son point de départ. Sans effet tant qu'il est en main.</summary>
        public void Respawn()
        {
            if (grab.isSelected) return;

            Extinguish();
            foreach (var trail in trails) trail.Clear();
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = startPosition;
            body.rotation = startRotation;
            transform.SetPositionAndRotation(startPosition, startRotation);
        }

        private bool RespawnRequested()
        {
            if (grab.isSelected) return false;
            if (transform.position.y < startPosition.y - respawnFallDistance) return true;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame) return true;

            return PrimaryButtonPressed(UnityEngine.InputSystem.XR.XRController.rightHand)
                || PrimaryButtonPressed(UnityEngine.InputSystem.XR.XRController.leftHand);
        }

        private static bool PrimaryButtonPressed(InputDevice controller)
        {
            if (controller == null) return false;
            var button = controller.TryGetChildControl<ButtonControl>("primaryButton");
            return button != null && button.wasPressedThisFrame;
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            // Saisie à bascule : la manette garde le sabre jusqu'à la prochaine pression sur le grip.
            if (args.interactorObject is XRBaseInputInteractor hand && !toggledHands.ContainsKey(hand))
            {
                toggledHands.Add(hand, hand.selectActionTrigger);
                hand.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.Toggle;
            }
            Holder = args.interactorObject != null ? args.interactorObject.transform : null;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            // Ressaisie technique (changement de prise en cours de route) : le sabre ne quitte pas vraiment la main.
            if (regripping) return;
            Holder = null;
            Extinguish();
        }

        private void RestoreReleasedHands()
        {
            if (toggledHands.Count == 0) return;

            // On attend que le grip soit relâché avant de rendre son réglage à la manette,
            // sinon la pression qui lâche le sabre le ressaisirait aussitôt.
            handsToRestore.Clear();
            foreach (var pair in toggledHands)
            {
                var hand = pair.Key;
                if (hand == null) { handsToRestore.Add(hand); continue; }
                if (grab.isSelected && grab.interactorsSelecting.Contains(hand)) continue;
                if (hand.logicalSelectState.isPerformed) continue;
                hand.selectActionTrigger = pair.Value;
                handsToRestore.Add(hand);
            }
            foreach (var hand in handsToRestore) toggledHands.Remove(hand);
        }

        private void OnTriggerPressed(ActivateEventArgs args)
        {
            // Sabre simple : la pression allume ou éteint tout de suite.
            if (blade2 == null)
            {
                ignited = !ignited;
                return;
            }

            // Sabre double : on attend de savoir si la pression est brève ou maintenue.
            triggerHeld = true;
            pressStartedIgnited = IsIgnited;
            holdHandled = false;
            triggerPressTime = Time.time;
        }

        private void OnTriggerReleased(DeactivateEventArgs args)
        {
            if (!triggerHeld) return;
            triggerHeld = false;
            if (holdHandled) return;

            // Pression brève : la lame qui pointe vers le haut, puis l'autre, puis extinction.
            if (!ignited && !ignited2)
            {
                if (SecondBladeIsUpper()) ignited2 = true;
                else ignited = true;
            }
            else if (!(ignited && ignited2))
            {
                ignited = true;
                ignited2 = true;
            }
            else Extinguish();
        }

        /// <summary>Vrai si la seconde lame est celle qui pointe le plus vers le haut à cet instant.</summary>
        private bool SecondBladeIsUpper()
        {
            if (bladePart == null || blade2Part == null) return false;
            Vector3 first = bladePart.transform.TransformDirection(bladePart.tipLocal.normalized);
            Vector3 second = blade2Part.transform.TransformDirection(blade2Part.tipLocal.normalized);
            return second.y > first.y;
        }

        // Apparence
        private void Apply()
        {
            float flicker = 1f + (Mathf.PerlinNoise(Time.time * flickerSpeed, 0f) - 0.5f) * 2f * flickerAmount;

            ApplyTo(bladePart, true, level, flicker);
            ApplyTo(glowPart, false, level, flicker);
            ApplyTo(blade2Part, true, level2, flicker);
            ApplyTo(glow2Part, false, level2, flicker);

            if (bladeLight != null)
            {
                float lightLevel = Mathf.Max(level, level2);
                bladeLight.enabled = lightLevel > 0.001f;
                bladeLight.color = bladeColor;
                bladeLight.range = lightRange;
                bladeLight.intensity = lightIntensity * lightLevel * flicker;
            }
        }

        private void ApplyTo(BladePart part, bool isBlade, float partLevel, float flicker)
        {
            if (part == null || part.transform == null) return;
            bool visible = partLevel > 0.001f;

            // La lame s'étire le long de son propre axe, depuis sa base.
            Vector3 scale = part.baseScale;
            scale[part.axis] *= Mathf.Max(partLevel, 0.0001f);
            part.transform.localScale = scale;

            Renderer partRenderer = part.renderer;
            if (partRenderer == null) return;
            partRenderer.enabled = visible;
            partRenderer.GetPropertyBlock(block);
            block.SetColor(ColorId, bladeColor);
            block.SetFloat(FlickerId, flicker);
            if (isBlade)
            {
                ApplyOverride(SaberSetting.BladeIntensity, IntensityId);
                ApplyOverride(SaberSetting.BladeCore, CorePowerId);
                ApplyOverride(SaberSetting.BladeCoreIntensity, CoreIntensityId);
            }
            else
            {
                ApplyOverride(SaberSetting.GlowIntensity, IntensityId);
                ApplyOverride(SaberSetting.GlowFalloff, FalloffId);
                ApplyOverride(SaberSetting.GlowOpacity, OpacityId);
            }
            partRenderer.SetPropertyBlock(block);
        }

        /// <summary>Une lame ou un halo : son axe d'étirement et sa pointe sont lus sur le maillage.</summary>
        private class BladePart
        {
            public Transform transform;
            public Renderer renderer;
            public int axis = 1;
            public Vector3 baseScale = Vector3.one;
            public Vector3 tipLocal = new Vector3(0f, 0.9f, 0f);
        }

        private static Renderer RendererOf(BladePart part) => part != null ? part.renderer : null;

        private static BladePart MakePart(Transform partTransform)
        {
            if (partTransform == null) return null;

            var part = new BladePart
            {
                transform = partTransform,
                renderer = partTransform.GetComponent<Renderer>(),
                baseScale = partTransform.localScale
            };

            // L'axe de la lame est la plus grande dimension du maillage ; sa pointe est le bout le plus éloigné du pivot.
            var filter = partTransform.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds bounds = filter.sharedMesh.bounds;
                Vector3 size = bounds.size;
                part.axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);

                float near = bounds.min[part.axis];
                float far = bounds.max[part.axis];
                Vector3 tip = Vector3.zero;
                tip[part.axis] = Mathf.Abs(far) > Mathf.Abs(near) ? far : near;
                part.tipLocal = tip;
            }

            if (part.renderer != null)
            {
                part.renderer.shadowCastingMode = ShadowCastingMode.Off;
                part.renderer.receiveShadows = false;
            }
            return part;
        }

        /// <summary>Crée un halo autour d'une lame dont le modèle n'en fournit pas : même maillage, élargi.</summary>
        private BladePart CreateGlow(BladePart source)
        {
            if (source == null || autoGlowMaterial == null) return null;
            var filter = source.transform.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;

            var glowObject = new GameObject(source.transform.name + " Glow");
            Transform glowTransform = glowObject.transform;
            glowTransform.SetParent(source.transform.parent, false);
            glowTransform.localPosition = source.transform.localPosition;
            glowTransform.localRotation = source.transform.localRotation;

            Vector3 scale = source.baseScale * autoGlowWidth;
            scale[source.axis] = source.baseScale[source.axis];
            glowTransform.localScale = scale;

            glowObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            glowObject.AddComponent<MeshRenderer>().sharedMaterial = autoGlowMaterial;
            return MakePart(glowTransform);
        }

        private void CreateLight()
        {
            if (bladeLight != null || !castLight) return;

            var lightObject = new GameObject("Blade Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = lightOffset;
            bladeLight = lightObject.AddComponent<Light>();
            bladeLight.type = LightType.Point;
            bladeLight.shadows = LightShadows.None;
            bladeLight.enabled = false;
        }

        // Traînée
        private void CreateTrail(BladePart bladeSource, bool isSecondBlade)
        {
            if (trailMaterial == null || bladeSource == null) return;
            trails.Add(new BladeTrail(bladeSource, trailMaterial, isSecondBlade));
        }

        /// <summary>Ruban lumineux qui relie les dernières positions d'une lame (base et pointe).</summary>
        private class BladeTrail
        {
            private readonly Transform blade;
            private readonly bool isSecondBlade;
            private readonly Vector3 tipLocal;
            private readonly Vector3[] bases = new Vector3[TrailSamples];
            private readonly Vector3[] tips = new Vector3[TrailSamples];
            private readonly float[] times = new float[TrailSamples];
            private readonly Vector3[] vertices = new Vector3[TrailSamples * 2];
            private readonly Color[] colors = new Color[TrailSamples * 2];
            private readonly Mesh mesh;
            private readonly GameObject holder;
            private int count;

            public MeshRenderer Renderer { get; }

            public BladeTrail(BladePart bladeSource, Material material, bool secondBlade)
            {
                blade = bladeSource.transform;
                tipLocal = bladeSource.tipLocal;
                isSecondBlade = secondBlade;

                // Objet à part, à l'origine du monde : les sommets sont écrits en coordonnées monde.
                holder = new GameObject("Saber Trail");
                mesh = new Mesh { name = "Saber Trail" };
                mesh.MarkDynamic();

                var triangles = new int[(TrailSamples - 1) * 6];
                for (int i = 0; i < TrailSamples - 1; i++)
                {
                    int v = i * 2;
                    int t = i * 6;
                    triangles[t] = v;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 1;
                    triangles[t + 4] = v + 3;
                    triangles[t + 5] = v + 2;
                }
                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.triangles = triangles;

                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                Renderer = holder.AddComponent<MeshRenderer>();
                Renderer.sharedMaterial = material;
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                Renderer.receiveShadows = false;
                Renderer.enabled = false;
            }

            public void Clear() => count = 0;

            public void Destroy()
            {
                if (holder != null) Object.Destroy(holder);
                if (mesh != null) Object.Destroy(mesh);
            }

            public void Update(LaserSaber saber)
            {
                if (blade == null || Renderer == null) return;

                float bladeLevel = isSecondBlade ? saber.level2 : saber.level;
                if (bladeLevel <= 0.001f)
                {
                    count = 0;
                    Renderer.enabled = false;
                    return;
                }

                // Oublie les positions trop anciennes, puis ajoute la position actuelle en tête.
                float now = Time.time;
                float duration = saber.trailDuration;
                while (count > 0 && now - times[count - 1] > duration) count--;

                int kept = Mathf.Min(count, TrailSamples - 1);
                for (int i = kept; i > 0; i--)
                {
                    bases[i] = bases[i - 1];
                    tips[i] = tips[i - 1];
                    times[i] = times[i - 1];
                }
                bases[0] = blade.position;
                tips[0] = blade.TransformPoint(tipLocal);
                times[0] = now;
                count = kept + 1;

                // Les sommets inutilisés sont repliés sur le dernier point : ils ne dessinent rien.
                for (int i = 0; i < TrailSamples; i++)
                {
                    int sample = Mathf.Min(i, count - 1);
                    float fade = 1f - Mathf.Clamp01((now - times[sample]) / duration);
                    var color = new Color(1f, 1f, 1f, fade * bladeLevel);
                    vertices[i * 2] = bases[sample];
                    vertices[i * 2 + 1] = tips[sample];
                    colors[i * 2] = color;
                    colors[i * 2 + 1] = color;
                }

                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.RecalculateBounds();

                Renderer.enabled = count > 1;
                Renderer.GetPropertyBlock(saber.block);
                saber.block.SetColor(ColorId, saber.bladeColor);
                saber.ApplyOverride(SaberSetting.TrailIntensity, IntensityId);
                Renderer.SetPropertyBlock(saber.block);
            }
        }

        // Prise en main
        private void CreateAttachPoint()
        {
            if (grab.attachTransform != null) return;

            attach = new GameObject("Attach").transform;
            attach.SetParent(transform, false);
            grab.attachTransform = attach;
            UpdateAttachPoint();
        }

        private void UpdateAttachPoint()
        {
            if (attach == null) return;

            // La lame suit l'axe Y du modèle : on la ramène vers l'avant de la manette, inclinée vers le haut.
            attach.localPosition = new Vector3(0f, gripOffset, 0f);
            // Puis on le fait tourner dans la main autour de l'axe de la lame (Y).
            attach.localRotation = Quaternion.Euler(0f, gripRoll, 0f) * Quaternion.Euler(-90f + gripTilt, 0f, 0f);
        }

        private Transform FindChild(string childName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName) return child;
            }
            return null;
        }
    }
}
