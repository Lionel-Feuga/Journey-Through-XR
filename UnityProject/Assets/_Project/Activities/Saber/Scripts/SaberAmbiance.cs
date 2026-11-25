using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Niveau d'ambiance des sabres : 0 = lumière normale de la scène, 1 = pénombre (lumière de XR_Test).
    ///
    /// - Approche à la main : à moins de « Approach Distance » d'un sabre posé, le niveau suit la distance
    ///   de la manette la plus proche (40 cm → 0, contact → 1), dans les deux sens.
    /// - Sabre en main : niveau 1.
    /// - Saisie à distance (au rayon) : le niveau monte jusqu'à 1 en « Remote Fade Duration » secondes.
    ///   Pendant ce temps, la gâchette n'allume pas les sabres.
    /// - Sabre lâché (plus aucun en main) : le niveau reste à 1 pendant « Release Hold Duration »,
    ///   puis redescend en « Release Fade Duration » (sans passer sous le niveau dû à la proximité).
    ///
    /// Le son d'ambiance à venir pourra suivre <see cref="Level"/> (événement Level Changed ou champ Ambiance Audio),
    /// et démarrer sur l'événement Remote Grab Started.
    /// À poser une seule fois dans la scène (le menu d'installation le met sur « Sabres_301 »).
    /// </summary>
    [DisallowMultipleComponent]
    public class SaberAmbiance : MonoBehaviour
    {
        [Header("Sabres (vide = tous ceux de la scène)")]
        [SerializeField] private List<LaserSaber> sabers = new List<LaserSaber>();

        [Header("Manettes (vide = recherche automatique)")]
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;

        [Header("Distances (m)")]
        [Tooltip("Distance manette-sabre à partir de laquelle la lumière commence à baisser.")]
        [SerializeField] private float approachDistance = 0.4f;
        [Tooltip("Au-delà de cette distance au moment de la saisie, on considère que le sabre a été attrapé à distance.")]
        [SerializeField] private float remoteGrabMinDistance = 0.35f;

        [Header("Durées (s)")]
        [SerializeField] private float remoteFadeDuration = 2f;
        [SerializeField] private float releaseHoldDuration = 5f;
        [SerializeField] private float releaseFadeDuration = 2f;

        [Header("Pénombre (valeurs de la scène XR_Test)")]
        [SerializeField] private Light sun;
        [SerializeField] private float darkSunIntensity = 0.1f;
        [SerializeField] private Color darkAmbientColor = new Color(0.039215688f, 0.039215688f, 0.0627451f);
        [Tooltip("Intensité des reflets dans la pénombre. 1 = comme XR_Test (reflets inchangés).")]
        [SerializeField, Range(0f, 1f)] private float darkReflectionIntensity = 1f;
        [Tooltip("Exposition du ciel visible par les fenêtres dans la pénombre.")]
        [SerializeField, Range(0f, 1f)] private float darkSkyExposure = 0.1f;

        [Header("Son (plus tard)")]
        [Tooltip("Facultatif : son dont le volume suit le niveau d'ambiance.")]
        [SerializeField] private AudioSource ambianceAudio;
        [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;

        [Header("Événements")]
        public UnityEvent<float> levelChanged = new UnityEvent<float>();
        public UnityEvent remoteGrabStarted = new UnityEvent();

        /// <summary>Niveau d'ambiance, de 0 (lumière normale) à 1 (pénombre).</summary>
        public float Level { get; private set; }

        /// <summary>Faux pendant la montée d'une saisie à distance, ou tant que la pénombre n'est pas atteinte.</summary>
        public bool IgnitionAllowed => !remoteFading && Level >= 0.99f;

        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        private readonly Dictionary<XRGrabInteractable, LaserSaber> grabs = new Dictionary<XRGrabInteractable, LaserSaber>();
        private readonly List<Transform> hands = new List<Transform>();
        private readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
        private readonly List<float> brightProbeIntensity = new List<float>();

        private bool remoteFading;
        private float remoteStartLevel;
        private float remoteStartTime;
        private float releaseTime = float.NegativeInfinity;

        // Lumière d'origine
        private float brightSunIntensity;
        private SphericalHarmonicsL2 brightAmbient;
        private SphericalHarmonicsL2 darkAmbient;
        private float brightReflectionIntensity;
        private Material originalSky;
        private Material skyCopy;
        private float brightSkyExposure;
        private bool captured;
        private float appliedLevel = -1f;

        private void Awake()
        {
            if (sabers.Count == 0)
                sabers.AddRange(FindObjectsByType<LaserSaber>(FindObjectsInactive.Include, FindObjectsSortMode.None));

            foreach (var saber in sabers)
            {
                if (saber == null) continue;
                var grab = saber.GetComponent<XRGrabInteractable>();
                if (grab == null) continue;
                grabs[grab] = saber;
                grab.selectEntered.AddListener(OnGrabbed);
                grab.selectExited.AddListener(OnReleased);
            }

            LaserSaber.CanIgnite = Gate;
        }

        private void Start()
        {
            FindHands();
            CaptureBrightLighting();
            Apply(0f);
        }

        private void OnDestroy()
        {
            foreach (var grab in grabs.Keys)
            {
                if (grab == null) continue;
                grab.selectEntered.RemoveListener(OnGrabbed);
                grab.selectExited.RemoveListener(OnReleased);
            }
            if (LaserSaber.CanIgnite == (System.Func<LaserSaber, bool>)Gate) LaserSaber.CanIgnite = null;
            RestoreBrightLighting();
        }

        private bool Gate(LaserSaber saber) => !sabers.Contains(saber) || IgnitionAllowed;

        // ---------------------------------------------------------------- Saisie

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            var grab = (XRGrabInteractable)args.interactableObject;
            var collider = grab.colliders.Count > 0 ? grab.colliders[0] : null;
            Vector3 hand = args.interactorObject.transform.position;
            Vector3 closest = collider != null ? collider.ClosestPoint(hand) : grab.transform.position;
            bool remote = Vector3.Distance(hand, closest) > remoteGrabMinDistance;

            if (remote && !remoteFading && Level < 0.99f)
            {
                remoteFading = true;
                remoteStartLevel = Level;
                remoteStartTime = Time.time;
                remoteGrabStarted.Invoke();
            }
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            // Ressaisie technique de LaserSaber (lâcher + reprendre dans la même image) : on attend la fin de l'image.
            releaseTime = Time.time;
        }

        // ---------------------------------------------------------------- Niveau

        private void Update()
        {
            bool anyHeld = false;
            foreach (var grab in grabs.Keys)
            {
                if (grab != null && grab.isSelected) { anyHeld = true; break; }
            }

            float level;
            if (remoteFading)
            {
                if (!anyHeld)
                {
                    // Lâché pendant la montée : on passe directement au maintien puis au retour.
                    remoteFading = false;
                    level = Mathf.Max(Proximity(), ReleaseCurve());
                }
                else
                {
                    float t = remoteFadeDuration > 0f ? (Time.time - remoteStartTime) / remoteFadeDuration : 1f;
                    level = Mathf.Lerp(remoteStartLevel, 1f, Mathf.Clamp01(t));
                    if (t >= 1f) remoteFading = false;
                }
            }
            else if (anyHeld)
            {
                level = 1f;
            }
            else
            {
                level = Mathf.Max(Proximity(), ReleaseCurve());
            }

            Level = level;
            if (!Mathf.Approximately(level, appliedLevel)) Apply(level);
        }

        /// <summary>1 pendant le maintien après un lâcher, puis redescente linéaire jusqu'à 0.</summary>
        private float ReleaseCurve()
        {
            float t = Time.time - releaseTime;
            if (t < 0f) return 0f;
            if (t < releaseHoldDuration) return 1f;
            if (releaseFadeDuration <= 0f) return 0f;
            return Mathf.Clamp01(1f - (t - releaseHoldDuration) / releaseFadeDuration);
        }

        /// <summary>1 au contact d'un sabre posé, 0 à « approachDistance » ou plus.</summary>
        private float Proximity()
        {
            if (approachDistance <= 0f || hands.Count == 0) return 0f;
            float best = float.PositiveInfinity;
            foreach (var pair in grabs)
            {
                var grab = pair.Key;
                if (grab == null || grab.isSelected) continue;
                var collider = grab.colliders.Count > 0 ? grab.colliders[0] : null;
                foreach (var hand in hands)
                {
                    if (hand == null) continue;
                    Vector3 p = hand.position;
                    Vector3 closest = collider != null ? collider.ClosestPoint(p) : grab.transform.position;
                    best = Mathf.Min(best, Vector3.Distance(p, closest));
                }
            }
            return float.IsInfinity(best) ? 0f : Mathf.Clamp01(1f - best / approachDistance);
        }

        // ---------------------------------------------------------------- Lumière

        private void CaptureBrightLighting()
        {
            if (sun == null) sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional) { sun = l; break; }
                }
            }
            if (sun != null) brightSunIntensity = sun.intensity;

            brightAmbient = RenderSettings.ambientProbe;
            darkAmbient = new SphericalHarmonicsL2();
            darkAmbient.AddAmbientLight(darkAmbientColor.linear);
            brightReflectionIntensity = RenderSettings.reflectionIntensity;

            probes.Clear();
            brightProbeIntensity.Clear();
            foreach (var probe in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                probes.Add(probe);
                brightProbeIntensity.Add(probe.intensity);
            }

            // Copie du ciel : on ne modifie jamais le matériau d'origine.
            originalSky = RenderSettings.skybox;
            if (originalSky != null && originalSky.HasProperty(ExposureId))
            {
                skyCopy = new Material(originalSky);
                brightSkyExposure = originalSky.GetFloat(ExposureId);
                RenderSettings.skybox = skyCopy;
            }
            captured = true;
        }

        private void Apply(float level)
        {
            appliedLevel = level;
            if (captured)
            {
                if (sun != null) sun.intensity = Mathf.Lerp(brightSunIntensity, darkSunIntensity, level);
                RenderSettings.ambientProbe = brightAmbient * (1f - level) + darkAmbient * level;
                RenderSettings.reflectionIntensity = Mathf.Lerp(brightReflectionIntensity, darkReflectionIntensity, level);
                for (int i = 0; i < probes.Count; i++)
                {
                    if (probes[i] != null)
                        probes[i].intensity = Mathf.Lerp(brightProbeIntensity[i], brightProbeIntensity[i] * darkReflectionIntensity, level);
                }
                if (skyCopy != null) skyCopy.SetFloat(ExposureId, Mathf.Lerp(brightSkyExposure, darkSkyExposure, level));
            }

            if (ambianceAudio != null) ambianceAudio.volume = level * maxVolume;
            levelChanged.Invoke(level);
        }

        private void RestoreBrightLighting()
        {
            if (!captured) return;
            if (sun != null) sun.intensity = brightSunIntensity;
            RenderSettings.ambientProbe = brightAmbient;
            RenderSettings.reflectionIntensity = brightReflectionIntensity;
            for (int i = 0; i < probes.Count; i++)
            {
                if (probes[i] != null) probes[i].intensity = brightProbeIntensity[i];
            }
            if (skyCopy != null)
            {
                RenderSettings.skybox = originalSky;
                Destroy(skyCopy);
            }
            captured = false;
        }

        // ---------------------------------------------------------------- Manettes

        private void FindHands()
        {
            hands.Clear();
            if (leftController != null) hands.Add(leftController);
            if (rightController != null) hands.Add(rightController);
            if (hands.Count > 0) return;

            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name == "Left Controller" || t.name == "Right Controller") hands.Add(t);
            }
            if (hands.Count > 0) return;

            // Repli : tous les interacteurs de manette (ils suivent les mains).
            foreach (var interactor in FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None))
                hands.Add(interactor.transform);

            if (hands.Count == 0)
                Debug.LogWarning("[Sabres] SaberAmbiance : aucune manette trouvée, l'approche à la main ne baissera pas la lumière.");
        }
    }
}
