using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ObsidienneVR;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Panneau de réglage des sabres, utilisable dans le casque.
    /// Bouton B / Y (ou touche Tab) : affiche ou masque le panneau devant soi.
    /// On vise un curseur avec le rayon de la manette libre et on le déplace avec la gâchette.
    /// Grip en visant le panneau : on le déplace comme un objet ; le joystick l'approche ou l'éloigne.
    ///
    /// La barre « Réglages appliqués à » choisit la cible : tous les sabres, ou un seul.
    /// Onglets : Lame, Halo, Effets, Couleur, Prise (maniement), Rotor (sabre Obsidienne), Décor (éclairage de test),
    /// Mémoire (préréglages d'apparence, conservés d'une session à l'autre).
    ///
    /// Hors préréglages, les réglages ne durent que le temps de la session : il faut reporter les valeurs retenues.
    /// À poser sur un objet vide de la scène de test. Outil de mise au point, à retirer du jeu final.
    /// </summary>
    public class SaberTuningMenu : MonoBehaviour
    {
        // Réglages
        [Tooltip("Distance d'apparition du panneau devant le regard, en mètres.")]
        [SerializeField] private float distance = 0.9f;

        [Tooltip("Affiche le panneau dès le lancement.")]
        [SerializeField] private bool openAtStart;

        // Mise en page (en pixels du panneau)
        private const float Width = 900f;
        private const float Margin = 24f;
        private const float TitleBarHeight = 60f;
        private const float TargetBarHeight = 56f;
        private const float TabBarHeight = 54f;
        private const float FooterHeight = 72f;
        private const float RowHeight = 48f;
        private const float HeaderHeight = 40f;
        private const float PanelScale = 0.0009f;
        private const float PageTop = TitleBarHeight + TargetBarHeight + TabBarHeight;

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);
        private static readonly Color BarColor = new Color(0.11f, 0.12f, 0.15f, 1f);
        private static readonly Color TrackColor = new Color(0.24f, 0.26f, 0.3f, 1f);
        private static readonly Color FillColor = new Color(0.25f, 0.55f, 0.8f, 1f);
        private static readonly Color HandleColor = new Color(0.85f, 0.87f, 0.9f, 1f);
        private static readonly Color ButtonColor = new Color(0.34f, 0.37f, 0.43f, 1f);
        private static readonly Color TabColor = new Color(0.2f, 0.22f, 0.26f, 1f);
        private static readonly Color ActiveTabColor = new Color(0.25f, 0.55f, 0.8f, 1f);
        private static readonly Color TextColor = new Color(0.82f, 0.84f, 0.87f, 1f);
        private static readonly Color DimTextColor = new Color(0.55f, 0.58f, 0.62f, 1f);
        private static readonly Color HeaderColor = new Color(0.45f, 0.7f, 0.85f, 1f);

        private static readonly string[] TabTitles = { "Lame", "Halo", "Effets", "Couleur", "Prise", "Rotor", "Décor", "Mémoire" };

        // Teintes proposées en raccourci, en degrés : bleu, cyan, vert, jaune, orange, rouge, violet.
        private static readonly float[] HuePresets = { 217f, 185f, 125f, 55f, 25f, 0f, 280f };

        // Réglages d'apparence contenus dans un préréglage, dans cet ordre, suivis des trois valeurs du Bloom.
        private static readonly SaberSetting[] PresetSettings =
        {
            SaberSetting.BladeCoreIntensity, SaberSetting.BladeCore, SaberSetting.BladeIntensity, SaberSetting.Flicker,
            SaberSetting.GlowIntensity, SaberSetting.GlowFalloff, SaberSetting.GlowOpacity,
            SaberSetting.LightIntensity, SaberSetting.LightRange,
            SaberSetting.TrailIntensity, SaberSetting.TrailDuration
        };
        private const int PresetSlots = 6;
        private const string PresetFileName = "saber_presets.json";

        private class Row
        {
            public string label;
            public Slider slider;
            public TextMeshProUGUI valueText;
            public string format;
            public Func<float> read;
            public float initial;
            public int pageIndex;
            public bool perSaber;
            public SaberSetting setting;
        }

        [Serializable]
        private class PresetData
        {
            public bool used;
            public float[] values;
        }

        [Serializable]
        private class PresetFile
        {
            public PresetData[] slots;
        }

        // État
        private readonly List<Row> rows = new List<Row>();
        private readonly List<RectTransform> pages = new List<RectTransform>();
        private readonly List<Image> tabImages = new List<Image>();
        private readonly List<Action> extraRefresh = new List<Action>();
        private LaserSaber[] sabers;
        private float[][] saberInitial;
        private Color[] colorInitial;
        private float[] lastHue;
        private int targetIndex = -1;
        private Bloom bloom;
        private GameObject panel;
        private RectTransform root;
        private RectTransform page;
        private int pageIndex;
        private int currentPage;
        private int colorPage = -1;
        private float cursorY;
        private float tallestPage;
        private string section = "";
        private Texture2D hueTexture;
        private Sprite hueSprite;

        // Préréglages
        private PresetFile presets;
        private float[] basePreset;

        // Déplacement du panneau
        private XRGrabInteractable grab;

        // Décor : éclairage de la scène et valeurs d'origine, pour pouvoir les rétablir.
        private Light sun;
        private Camera view;
        private Row sunRow;
        private Row ambientRow;
        private Row backgroundRow;
        private int sceneryPage = -1;
        private float ambientLevel;
        private float backgroundLevel;
        private AmbientMode startAmbientMode;
        private Color startAmbientLight;
        private CameraClearFlags startClearFlags;
        private Color startBackground;

        // Cycle de vie
        private void Start()
        {
            sabers = FindObjectsByType<LaserSaber>(FindObjectsSortMode.None);
            Array.Sort(sabers, (first, second) => string.CompareOrdinal(first.name, second.name));
            CaptureInitialValues();

            FindBloom();
            FindEnvironment();
            if (sabers.Length > 0) basePreset = CapturePreset();
            LoadPresets();

            BuildPanel();
            MakeGrabbable();
            SelectPage(0);
            RefreshRows();
            panel.SetActive(false);
            if (openAtStart) Show();
        }

        private void Update()
        {
            if (grab != null && grab.isSelected) FaceViewer();
            if (!TogglePressed()) return;
            if (panel.activeSelf) Hide();
            else Show();
        }

        private void OnDestroy()
        {
            if (panel != null) Destroy(panel);
            if (hueSprite != null) Destroy(hueSprite);
            if (hueTexture != null) Destroy(hueTexture);
        }

        // Cible des réglages : tous les sabres, ou un seul
        private LaserSaber FirstTarget => sabers[targetIndex < 0 ? 0 : targetIndex];

        private void ForTargets(Action<LaserSaber, int> action)
        {
            if (targetIndex >= 0)
            {
                action(sabers[targetIndex], targetIndex);
                return;
            }
            for (int i = 0; i < sabers.Length; i++)
            {
                if (sabers[i] != null) action(sabers[i], i);
            }
        }

        private void StepTarget(int step)
        {
            if (sabers.Length == 0) return;
            int count = sabers.Length + 1;
            targetIndex = (targetIndex + 1 + step + count) % count - 1;
            RefreshRows();
        }

        private string TargetLabel()
        {
            if (sabers.Length == 0) return "Aucun sabre dans la scène";
            if (targetIndex < 0) return "Tous les sabres (" + sabers.Length + ")";
            return "Sabre " + (targetIndex + 1) + " : " + sabers[targetIndex].name;
        }

        private void CaptureInitialValues()
        {
            int settingCount = Enum.GetValues(typeof(SaberSetting)).Length;
            saberInitial = new float[sabers.Length][];
            colorInitial = new Color[sabers.Length];
            lastHue = new float[sabers.Length];
            for (int i = 0; i < sabers.Length; i++)
            {
                saberInitial[i] = new float[settingCount];
                for (int k = 0; k < settingCount; k++) saberInitial[i][k] = sabers[i].GetSetting((SaberSetting)k);
                colorInitial[i] = sabers[i].BladeColor;
                Color.RGBToHSV(colorInitial[i], out lastHue[i], out _, out _);
            }
        }

        // Affichage
        private void Show()
        {
            if (view == null) view = Camera.main;
            if (view != null)
            {
                // Devant le regard, à hauteur des yeux, tourné vers le joueur.
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                forward.Normalize();
                panel.transform.position = view.transform.position + forward * distance + Vector3.down * 0.1f;
                panel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                panel.GetComponent<Canvas>().worldCamera = view;
            }
            panel.SetActive(true);
            RefreshRows();
        }

        private void Hide()
        {
            panel.SetActive(false);
            LogValues();
        }

        private void SelectPage(int index)
        {
            currentPage = index;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].gameObject.SetActive(i == index);
                tabImages[i].color = i == index ? ActiveTabColor : TabColor;
            }
        }

        /// <summary>Remet chaque curseur sur la valeur réelle de la cible (après un changement de cible ou un préréglage).</summary>
        private void RefreshRows()
        {
            foreach (var row in rows)
            {
                float current = row.read();
                if (current < row.slider.minValue) row.slider.minValue = current;
                if (current > row.slider.maxValue) row.slider.maxValue = current;
                row.slider.SetValueWithoutNotify(current);
                row.valueText.text = current.ToString(row.format);
            }
            foreach (var refresh in extraRefresh) refresh();
        }

        private static bool TogglePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) return true;

            return SecondaryButtonPressed(UnityEngine.InputSystem.XR.XRController.rightHand)
                || SecondaryButtonPressed(UnityEngine.InputSystem.XR.XRController.leftHand);
        }

        private static bool SecondaryButtonPressed(InputDevice controller)
        {
            if (controller == null) return false;
            var button = controller.TryGetChildControl<ButtonControl>("secondaryButton");
            return button != null && button.wasPressedThisFrame;
        }

        private void LogValues()
        {
            var text = new StringBuilder("[Sabre] Réglages (" + TargetLabel() + ") : ");
            foreach (var row in rows) text.Append(row.label).Append(" = ").Append(row.slider.value.ToString("0.00")).Append(" ; ");
            for (int i = 0; i < sabers.Length; i++)
            {
                if (sabers[i] == null) continue;
                text.Append("Sabre ").Append(i + 1).Append(" (").Append(sabers[i].name).Append(") / ").Append(ColorText(sabers[i].BladeColor)).Append(" ; ");
            }
            Debug.Log(text.ToString(), this);
        }

        private static string ColorText(Color color)
        {
            return "RVB " + color.r.ToString("0.00") + " / " + color.g.ToString("0.00") + " / " + color.b.ToString("0.00");
        }

        // Déplacement du panneau
        private void MakeGrabbable()
        {
            // Collision fine, juste derrière l'interface, pour que le rayon puisse saisir le panneau au grip.
            var body = panel.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var box = panel.AddComponent<BoxCollider>();
            box.size = new Vector3(root.sizeDelta.x, root.sizeDelta.y, 10f);
            box.center = new Vector3(0f, 0f, 8f);

            grab = panel.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = true;
            grab.trackRotation = false;
            grab.throwOnDetach = false;
            // Saisie à distance : le panneau reste au bout du rayon et le joystick l'approche ou l'éloigne.
            grab.farAttachMode = InteractableFarAttachMode.Far;
        }

        private void FaceViewer()
        {
            if (view == null) return;
            Vector3 away = Vector3.ProjectOnPlane(panel.transform.position - view.transform.position, Vector3.up);
            if (away.sqrMagnitude < 0.001f) return;
            panel.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        // Bloom
        private void FindBloom()
        {
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (volume.sharedProfile == null || !volume.sharedProfile.Has<Bloom>()) continue;

                // volume.profile travaille sur une copie : le profil enregistré dans le projet n'est pas modifié.
                if (volume.profile.TryGet(out bloom)) return;
            }
        }

        // Décor
        private void FindEnvironment()
        {
            view = Camera.main;
            sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (candidate.type != LightType.Directional) continue;
                    sun = candidate;
                    break;
                }
            }

            startAmbientMode = RenderSettings.ambientMode;
            startAmbientLight = RenderSettings.ambientLight;
            if (view != null)
            {
                startClearFlags = view.clearFlags;
                startBackground = view.backgroundColor;
            }
            ReadEnvironmentLevels();
        }

        private void ReadEnvironmentLevels()
        {
            ambientLevel = startAmbientMode == AmbientMode.Flat ? startAmbientLight.maxColorComponent : 0.5f;
            backgroundLevel = startClearFlags == CameraClearFlags.SolidColor ? startBackground.maxColorComponent : 0.5f;
        }

        private void SetAmbient(float brightness)
        {
            ambientLevel = brightness;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(brightness, brightness, brightness);
        }

        private void SetBackground(float brightness)
        {
            backgroundLevel = brightness;
            if (view == null) return;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(brightness, brightness, brightness);
        }

        private void RestoreEnvironment()
        {
            RenderSettings.ambientMode = startAmbientMode;
            RenderSettings.ambientLight = startAmbientLight;
            ReadEnvironmentLevels();
            if (view == null) return;
            view.clearFlags = startClearFlags;
            view.backgroundColor = startBackground;
        }

        private void ApplySceneryPreset(float sunIntensity, float ambient, float background)
        {
            if (sunRow != null) sunRow.slider.value = sunIntensity;
            if (ambientRow != null) ambientRow.slider.value = ambient;
            if (backgroundRow != null) backgroundRow.slider.value = background;
        }

        // Préréglages d'apparence
        private string PresetPath => Path.Combine(Application.persistentDataPath, PresetFileName);

        private int PresetLength => PresetSettings.Length + 3;

        private float[] CapturePreset()
        {
            var values = new float[PresetLength];
            for (int i = 0; i < PresetSettings.Length; i++) values[i] = FirstTarget.GetSetting(PresetSettings[i]);
            int b = PresetSettings.Length;
            values[b] = bloom != null ? bloom.threshold.value : -1f;
            values[b + 1] = bloom != null ? bloom.intensity.value : -1f;
            values[b + 2] = bloom != null ? bloom.scatter.value : -1f;
            return values;
        }

        private void ApplyPresetValues(float[] values)
        {
            if (values == null || values.Length != PresetLength || sabers.Length == 0) return;

            ForTargets((saber, saberIndex) =>
            {
                for (int i = 0; i < PresetSettings.Length; i++) saber.SetSetting(PresetSettings[i], values[i]);
            });

            int b = PresetSettings.Length;
            if (bloom != null && values[b] >= 0f)
            {
                bloom.threshold.overrideState = true;
                bloom.threshold.value = values[b];
                bloom.intensity.overrideState = true;
                bloom.intensity.value = values[b + 1];
                bloom.scatter.overrideState = true;
                bloom.scatter.value = values[b + 2];
            }
            RefreshRows();
        }

        private void LoadPresets()
        {
            presets = null;
            try
            {
                if (File.Exists(PresetPath)) presets = JsonUtility.FromJson<PresetFile>(File.ReadAllText(PresetPath));
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Sabre] Lecture des préréglages impossible : " + error.Message, this);
            }

            if (presets == null) presets = new PresetFile();
            if (presets.slots == null || presets.slots.Length != PresetSlots)
            {
                var slots = new PresetData[PresetSlots];
                for (int i = 0; i < PresetSlots; i++)
                {
                    bool kept = presets.slots != null && i < presets.slots.Length && presets.slots[i] != null;
                    slots[i] = kept ? presets.slots[i] : new PresetData();
                }
                presets.slots = slots;
            }
            foreach (var data in presets.slots)
            {
                if (data.values == null || data.values.Length != PresetLength) data.used = false;
            }
        }

        private void SavePreset(int number)
        {
            if (sabers.Length == 0) return;
            presets.slots[number].used = true;
            presets.slots[number].values = CapturePreset();
            try
            {
                File.WriteAllText(PresetPath, JsonUtility.ToJson(presets, true));
                Debug.Log("[Sabre] Préréglage " + (number + 1) + " enregistré dans " + PresetPath, this);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Sabre] Enregistrement des préréglages impossible : " + error.Message, this);
            }
            RefreshRows();
        }

        private static string PresetSummary(PresetData data)
        {
            if (data == null || !data.used) return "vide";
            return "Bords " + data.values[2].ToString("0.00") + "   Halo " + data.values[4].ToString("0.00")
                + "   Traînée " + data.values[9].ToString("0.00");
        }

        // Construction du panneau
        private void BuildPanel()
        {
            panel = new GameObject("Saber Tuning Menu", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            panel.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            root = (RectTransform)panel.transform;
            root.localScale = Vector3.one * PanelScale;

            var panelBackground = AddImage(root, "Fond", PanelColor);
            panelBackground.raycastTarget = true;
            Stretch(panelBackground.rectTransform);

            BuildTitleBar();
            BuildTargetBar();
            BuildTabs();

            BuildBladePage();
            BuildGlowPage();
            BuildEffectsPage();
            BuildColorPage();
            BuildHandlingPage();
            BuildRotorPage();
            BuildSceneryPage();
            BuildMemoryPage();

            float panelHeight = PageTop + tallestPage + FooterHeight;
            root.sizeDelta = new Vector2(Width, panelHeight);
            BuildFooter(panelHeight);
        }

        private void BuildTitleBar()
        {
            var bar = AddImage(root, "Barre de titre", BarColor);
            PlaceTopLeft(bar.rectTransform, 0f, 0f, Width, TitleBarHeight);

            var title = AddText(root, "Réglages des sabres", 30f, TextColor, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            PlaceTopLeft(title.rectTransform, Margin, 0f, 320f, TitleBarHeight);

            var hint = AddText(root, "Grip : déplacer   Joystick : distance", 20f, DimTextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(hint.rectTransform, 350f, 0f, 380f, TitleBarHeight);

            var close = AddButton(root, "Fermer", new Vector2(Width - Margin - 60f, -TitleBarHeight * 0.5f), new Vector2(120f, 42f), 22f, ButtonColor);
            close.onClick.AddListener(Hide);
        }

        private void BuildTargetBar()
        {
            float middle = -TitleBarHeight - TargetBarHeight * 0.5f;
            var caption = AddText(root, "Réglages appliqués à", 22f, DimTextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(caption.rectTransform, Margin, -TitleBarHeight, 240f, TargetBarHeight);

            var previous = AddButton(root, "<", new Vector2(292f, middle), new Vector2(46f, 40f), 26f, ButtonColor);
            previous.onClick.AddListener(() => StepTarget(-1));

            var plate = AddImage(root, "Cible", BarColor);
            PlaceTopLeft(plate.rectTransform, 322f, -TitleBarHeight - 8f, 500f, TargetBarHeight - 16f);
            var targetText = AddText(plate.rectTransform, "", 24f, TextColor, TextAlignmentOptions.Center);
            targetText.fontStyle = FontStyles.Bold;
            Stretch(targetText.rectTransform);

            var next = AddButton(root, ">", new Vector2(852f, middle), new Vector2(46f, 40f), 26f, ButtonColor);
            next.onClick.AddListener(() => StepTarget(1));

            extraRefresh.Add(() => targetText.text = TargetLabel());
        }

        private void BuildTabs()
        {
            float tabWidth = (Width - Margin * 2f) / TabTitles.Length;
            for (int i = 0; i < TabTitles.Length; i++)
            {
                int index = i;
                var center = new Vector2(Margin + tabWidth * (i + 0.5f), -TitleBarHeight - TargetBarHeight - TabBarHeight * 0.5f);
                var tab = AddButton(root, TabTitles[i], center, new Vector2(tabWidth - 6f, TabBarHeight - 12f), 21f, TabColor);
                tab.onClick.AddListener(() => SelectPage(index));
                tabImages.Add(tab.GetComponent<Image>());
            }
        }

        private void BuildFooter(float panelHeight)
        {
            float y = -panelHeight + FooterHeight * 0.5f;
            var resetPage = AddButton(root, "Réinitialiser l'onglet", new Vector2(Width * 0.5f - 170f, y), new Vector2(310f, 46f), 22f, ButtonColor);
            resetPage.onClick.AddListener(() => ResetRows(currentPage));
            var resetAll = AddButton(root, "Tout réinitialiser", new Vector2(Width * 0.5f + 170f, y), new Vector2(310f, 46f), 22f, ButtonColor);
            resetAll.onClick.AddListener(() => ResetRows(-1));
        }

        /// <summary>Rétablit les valeurs du lancement, pour la cible choisie. onlyPage négatif : tous les onglets.</summary>
        private void ResetRows(int onlyPage)
        {
            foreach (var row in rows)
            {
                if (onlyPage >= 0 && row.pageIndex != onlyPage) continue;
                if (row.perSaber)
                {
                    SaberSetting setting = row.setting;
                    ForTargets((saber, saberIndex) => saber.SetSetting(setting, saberInitial[saberIndex][(int)setting]));
                }
                else
                {
                    row.slider.value = row.initial;
                }
            }
            if (onlyPage < 0 || onlyPage == colorPage)
            {
                ForTargets((saber, saberIndex) => saber.BladeColor = colorInitial[saberIndex]);
            }
            if (onlyPage < 0 || onlyPage == sceneryPage) RestoreEnvironment();
            RefreshRows();
        }

        // Onglets
        private bool NoSaber()
        {
            if (sabers.Length > 0) return false;
            AddHeader("Aucun sabre dans la scène");
            return true;
        }

        private void BuildBladePage()
        {
            BeginPage();
            if (!NoSaber())
            {
                AddHeader("Centre de la lame");
                AddSaberRow("Blanc du centre", SaberSetting.BladeCoreIntensity, 0f, 4f);
                AddSaberRow("Finesse du centre", SaberSetting.BladeCore, 0.2f, 8f);

                AddHeader("Bords de la lame");
                AddSaberRow("Couleur des bords", SaberSetting.BladeIntensity, 0f, 8f);

                AddHeader("Scintillement");
                AddSaberRow("Amplitude", SaberSetting.Flicker, 0f, 0.3f);
                AddSaberRow("Vitesse", SaberSetting.FlickerSpeed, 0f, 60f, "0");
            }
            EndPage();
        }

        private void BuildGlowPage()
        {
            BeginPage();
            if (!NoSaber())
            {
                AddHeader("Halo autour de la lame");
                AddSaberRow("Intensité", SaberSetting.GlowIntensity, 0f, 8f);
                AddSaberRow("Resserrement", SaberSetting.GlowFalloff, 0.5f, 10f);
                AddSaberRow("Opacité", SaberSetting.GlowOpacity, 0f, 1f);

                AddHeader("Lumière projetée sur le décor");
                AddSaberRow("Intensité", SaberSetting.LightIntensity, 0f, 10f);
                AddSaberRow("Portée (m)", SaberSetting.LightRange, 0.5f, 8f);
            }
            EndPage();
        }

        private void BuildEffectsPage()
        {
            BeginPage();
            if (sabers.Length > 0)
            {
                AddHeader("Traînée");
                AddSaberRow("Intensité", SaberSetting.TrailIntensity, 0f, 8f);
                AddSaberRow("Durée (s)", SaberSetting.TrailDuration, 0.02f, 0.6f);

                AddHeader("Allumage");
                AddSaberRow("Sortie de lame (s)", SaberSetting.IgniteDuration, 0f, 1f);
            }

            if (bloom != null)
            {
                AddHeader("Bloom (commun à toute la scène)");
                AddRow("Seuil", 0f, 2f, "0.00", () => bloom.threshold.value, amount => { bloom.threshold.overrideState = true; bloom.threshold.value = amount; });
                AddRow("Intensité", 0f, 5f, "0.00", () => bloom.intensity.value, amount => { bloom.intensity.overrideState = true; bloom.intensity.value = amount; });
                AddRow("Diffusion", 0f, 1f, "0.00", () => bloom.scatter.value, amount => { bloom.scatter.overrideState = true; bloom.scatter.value = amount; });
            }
            else
            {
                AddHeader("Bloom : aucun Volume avec Bloom dans la scène");
            }
            EndPage();
        }

        private void BuildColorPage()
        {
            BeginPage();
            colorPage = pageIndex;
            if (NoSaber())
            {
                EndPage();
                return;
            }

            float headerY = cursorY;
            AddHeader("Couleur de la lame");

            // Aperçu de la couleur et valeurs à reporter dans Blade Color.
            var swatch = AddImage(page, "Aperçu", Color.white);
            PlaceTopLeft(swatch.rectTransform, 300f, headerY - 6f, 70f, HeaderHeight - 12f);
            var values = AddText(page, "", 22f, DimTextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(values.rectTransform, 390f, headerY, 420f, HeaderHeight);
            extraRefresh.Add(() =>
            {
                Color shown = FirstTarget.BladeColor;
                swatch.color = new Color(shown.r, shown.g, shown.b, 1f);
                values.text = ColorText(shown);
            });

            var hueRow = AddRow("Teinte", 0f, 360f, "0", () =>
            {
                Color.RGBToHSV(FirstTarget.BladeColor, out float hue, out float saturation, out _);
                return (saturation < 0.01f ? lastHue[targetIndex < 0 ? 0 : targetIndex] : hue) * 360f;
            }, amount =>
            {
                float newHue = Mathf.Repeat(amount / 360f, 1f);
                ForTargets((saber, saberIndex) =>
                {
                    Color.RGBToHSV(saber.BladeColor, out _, out float keptSaturation, out float keptBrightness);
                    lastHue[saberIndex] = newHue;
                    saber.BladeColor = Color.HSVToRGB(newHue, keptSaturation, keptBrightness);
                });
                foreach (var refresh in extraRefresh) refresh();
            });
            ShowHueGradient(hueRow.slider);

            var saturationRow = AddRow("Saturation", 0f, 1f, "0.00", () =>
            {
                Color.RGBToHSV(FirstTarget.BladeColor, out _, out float currentSaturation, out _);
                return currentSaturation;
            }, amount =>
            {
                ForTargets((saber, saberIndex) =>
                {
                    Color.RGBToHSV(saber.BladeColor, out float keptHue, out float oldSaturation, out float brightnessNow);
                    if (oldSaturation < 0.01f) keptHue = lastHue[saberIndex];
                    else lastHue[saberIndex] = keptHue;
                    saber.BladeColor = Color.HSVToRGB(keptHue, amount, brightnessNow);
                });
                foreach (var refresh in extraRefresh) refresh();
            });

            // Raccourcis : une pastille par teinte courante.
            var shortcutLabel = AddText(page, "Raccourcis", 24f, TextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(shortcutLabel.rectTransform, Margin + 16f, cursorY, 240f, 48f);
            float shortcutY = cursorY - 24f;
            for (int i = 0; i < HuePresets.Length; i++)
            {
                float shortcutHue = HuePresets[i];
                var center = new Vector2(368f + i * 66f, shortcutY);
                var shortcut = AddButton(page, "", center, new Vector2(56f, 36f), 20f, Color.HSVToRGB(shortcutHue / 360f, 0.9f, 1f));
                shortcut.onClick.AddListener(() =>
                {
                    saturationRow.slider.value = 0.9f;
                    hueRow.slider.value = shortcutHue;
                    RefreshRows();
                });
            }
            cursorY -= 56f;
            EndPage();
        }

        private void BuildHandlingPage()
        {
            BeginPage();
            if (!NoSaber())
            {
                AddHeader("Prise en main des sabres droits");
                AddSaberRow("Inclinaison (°)", SaberSetting.GripTilt, 0f, 90f, "0");
                AddSaberRow("Décalage (m)", SaberSetting.GripOffset, -0.12f, 0.12f);
                AddSaberRow("Rotation autour de la lame (°)", SaberSetting.GripRoll, -180f, 180f, "0");

                AddHeader("Lancer");
                AddSaberRow("Masse (kg)", SaberSetting.Mass, 0.05f, 2f);
                AddSaberRow("Puissance", SaberSetting.ThrowBoost, 0.5f, 4f);

                AddHeader("Commandes et retour automatique");
                AddSaberRow("Appui long (s)", SaberSetting.HoldDuration, 0.15f, 1f);
                AddSaberRow("Chute avant retour (m)", SaberSetting.RespawnFallDistance, 1f, 20f, "0");
            }
            EndPage();
        }

        private void BuildRotorPage()
        {
            BeginPage();
            var rotor = FindFirstObjectByType<ObsidienneController>();
            if (rotor == null)
            {
                AddHeader("Aucun sabre Obsidienne dans la scène");
                EndPage();
                return;
            }

            // Prise en main : on déplace le point de prise du sabre par rapport à sa position d'origine.
            var grabbable = rotor.GetComponent<XRGrabInteractable>();
            Transform attach = grabbable != null ? grabbable.attachTransform : null;
            if (attach != null)
            {
                AddHeader("Prise en main de l'Obsidienne");
                Vector3 basePosition = attach.localPosition;
                Quaternion baseRotation = attach.localRotation;
                var rotorSaber = rotor.GetComponent<LaserSaber>();
                float tilt = 0f;
                float turn = 0f;
                float slide = 0f;
                Action place = () =>
                {
                    attach.localRotation = baseRotation * Quaternion.Euler(tilt, 0f, turn);
                    attach.localPosition = basePosition + baseRotation * new Vector3(0f, slide * 0.01f, 0f);
                    if (rotorSaber != null) rotorSaber.RefreshGrip();
                };
                AddRow("Inclinaison (°)", -90f, 90f, "0", () => tilt, amount => { tilt = amount; place(); });
                AddRow("Rotation (°)", -180f, 180f, "0", () => turn, amount => { turn = amount; place(); });
                AddRow("Décalage (cm)", -10f, 10f, "0.0", () => slide, amount => { slide = amount; place(); });
            }

            AddHeader("Rotor");
            AddRow("Raideur", 0f, 150f, "0", () => rotor.balanceStiffness, amount => rotor.balanceStiffness = amount);
            AddRow("Amortissement", 0f, 20f, "0.0", () => rotor.balanceDamping, amount => rotor.balanceDamping = amount);
            AddRow("Seuil du geste", 60f, 600f, "0", () => rotor.gestureThreshold, amount => rotor.gestureThreshold = amount);
            AddRow("Vitesse", 180f, 2160f, "0", () => rotor.spinSpeed, amount => rotor.spinSpeed = amount);
            AddRow("Accélération", 500f, 10000f, "0", () => rotor.spinAcceleration, amount => rotor.spinAcceleration = amount);

            float toggleY = cursorY - 26f;
            var invert = AddButton(page, InvertLabel(rotor.invertGesture), new Vector2(Width * 0.5f, toggleY), new Vector2(460f, 42f), 22f, ButtonColor);
            var invertText = invert.GetComponentInChildren<TextMeshProUGUI>();
            invert.onClick.AddListener(() =>
            {
                rotor.invertGesture = !rotor.invertGesture;
                invertText.text = InvertLabel(rotor.invertGesture);
            });
            cursorY -= 54f;
            EndPage();
        }

        private static string InvertLabel(bool inverted)
        {
            return inverted ? "Sens du geste : inversé" : "Sens du geste : normal";
        }

        private void BuildSceneryPage()
        {
            BeginPage();
            sceneryPage = pageIndex;
            AddHeader("Éclairage de la scène (pour tester, non enregistré)");
            if (sun != null)
            {
                sunRow = AddRow("Lumière directe", 0f, 2f, "0.00", () => sun.intensity, amount => sun.intensity = amount);
            }
            ambientRow = AddRow("Ambiance", 0f, 1f, "0.00", () => ambientLevel, SetAmbient);
            if (view != null)
            {
                backgroundRow = AddRow("Fond", 0f, 1f, "0.00", () => backgroundLevel, SetBackground);
            }

            AddRow("Reflets des métaux", 0f, 1f, "0.00", () => RenderSettings.reflectionIntensity, amount => RenderSettings.reflectionIntensity = amount);

            AddHeader("Ambiances types");
            float buttonY = cursorY - 26f;
            AddButton(page, "Nuit", new Vector2(180f, buttonY), new Vector2(230f, 44f), 24f, ButtonColor)
                .onClick.AddListener(() => ApplySceneryPreset(0.05f, 0.03f, 0f));
            AddButton(page, "Intérieur", new Vector2(450f, buttonY), new Vector2(230f, 44f), 24f, ButtonColor)
                .onClick.AddListener(() => ApplySceneryPreset(0.6f, 0.35f, 0.25f));
            AddButton(page, "Plein jour", new Vector2(720f, buttonY), new Vector2(230f, 44f), 24f, ButtonColor)
                .onClick.AddListener(() => ApplySceneryPreset(1.3f, 0.8f, 0.8f));
            cursorY -= 56f;
            EndPage();
        }

        private void BuildMemoryPage()
        {
            BeginPage();
            if (NoSaber())
            {
                EndPage();
                return;
            }

            AddHeader("Préréglages d'apparence : lame, halo, lumière, traînée, Bloom");
            AddPresetLine("Réglages de base", () => "valeurs au lancement", () => ApplyPresetValues(basePreset), null);
            for (int i = 0; i < PresetSlots; i++)
            {
                int number = i;
                AddPresetLine("Préréglage " + (number + 1),
                    () => PresetSummary(presets.slots[number]),
                    () => { if (presets.slots[number].used) ApplyPresetValues(presets.slots[number].values); },
                    () => SavePreset(number));
            }
            EndPage();
        }

        private void AddPresetLine(string title, Func<string> summary, Action apply, Action save)
        {
            float y = cursorY;
            float middle = y - RowHeight * 0.5f;
            cursorY -= RowHeight;

            var titleText = AddText(page, title, 24f, TextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(titleText.rectTransform, Margin + 16f, y, 220f, RowHeight);

            var summaryText = AddText(page, "", 20f, DimTextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(summaryText.rectTransform, 262f, y, 330f, RowHeight);
            extraRefresh.Add(() => summaryText.text = summary());

            var applyButton = AddButton(page, "Appliquer", new Vector2(664f, middle), new Vector2(134f, 38f), 21f, ButtonColor);
            applyButton.onClick.AddListener(() => apply());
            if (save != null)
            {
                var saveButton = AddButton(page, "Enregistrer", new Vector2(808f, middle), new Vector2(140f, 38f), 21f, ButtonColor);
                saveButton.onClick.AddListener(() => save());
            }
        }

        // Éléments de page
        private void BeginPage()
        {
            pageIndex = pages.Count;
            page = AddRect(root, "Onglet " + TabTitles[pageIndex]);
            PlaceTopLeft(page, 0f, -PageTop, Width, 0f);
            pages.Add(page);
            cursorY = -6f;
            section = TabTitles[pageIndex];
        }

        private void EndPage()
        {
            float pageHeight = -cursorY + 10f;
            page.sizeDelta = new Vector2(Width, pageHeight);
            tallestPage = Mathf.Max(tallestPage, pageHeight);
        }

        private void AddHeader(string title)
        {
            var text = AddText(page, title, 23f, HeaderColor, TextAlignmentOptions.Left);
            text.fontStyle = FontStyles.Bold;
            section = TabTitles[pageIndex] + " / " + title;
            PlaceTopLeft(text.rectTransform, Margin, cursorY, Width - Margin * 2f, HeaderHeight);
            cursorY -= HeaderHeight;
        }

        /// <summary>Curseur lié à un réglage de sabre : il lit la cible et s'applique à tous les sabres ciblés.</summary>
        private void AddSaberRow(string label, SaberSetting setting, float min, float max, string format = "0.00")
        {
            Row row = AddRow(label, min, max, format,
                () => FirstTarget.GetSetting(setting),
                amount => ForTargets((saber, saberIndex) => saber.SetSetting(setting, amount)));
            row.perSaber = true;
            row.setting = setting;
        }

        private Row AddRow(string label, float min, float max, string format, Func<float> read, Action<float> apply)
        {
            float y = cursorY;
            float middle = y - RowHeight * 0.5f;
            cursorY -= RowHeight;
            float current = read();

            var labelText = AddText(page, label, 24f, TextColor, TextAlignmentOptions.Left);
            PlaceTopLeft(labelText.rectTransform, Margin + 16f, y, 250f, RowHeight);

            var valueText = AddText(page, current.ToString(format), 24f, TextColor, TextAlignmentOptions.Right);
            PlaceTopLeft(valueText.rectTransform, Width - Margin - 84f, y, 84f, RowHeight);

            var slider = AddSlider(new Vector2(350f, middle), 370f);
            slider.minValue = Mathf.Min(min, current);
            slider.maxValue = Mathf.Max(max, current);
            slider.SetValueWithoutNotify(current);
            slider.onValueChanged.AddListener(amount =>
            {
                apply(amount);
                valueText.text = amount.ToString(format);
            });

            // Boutons de réglage fin : un centième de la plage par pression.
            float step = (max - min) / 100f;
            var minus = AddButton(page, "-", new Vector2(316f, middle), new Vector2(40f, 40f), 30f, ButtonColor);
            minus.onClick.AddListener(() => slider.value -= step);
            var plus = AddButton(page, "+", new Vector2(754f, middle), new Vector2(40f, 40f), 30f, ButtonColor);
            plus.onClick.AddListener(() => slider.value += step);

            var row = new Row
            {
                label = section + " / " + label,
                slider = slider,
                valueText = valueText,
                format = format,
                read = read,
                initial = current,
                pageIndex = pageIndex
            };
            rows.Add(row);
            return row;
        }

        private void ShowHueGradient(Slider slider)
        {
            if (hueSprite == null)
            {
                hueTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 256; x++) hueTexture.SetPixel(x, 0, Color.HSVToRGB(x / 255f, 0.9f, 0.9f));
                hueTexture.Apply();
                hueSprite = Sprite.Create(hueTexture, new Rect(0f, 0f, 256f, 1f), new Vector2(0.5f, 0.5f));
            }

            // Le rail affiche l'arc-en-ciel des teintes, le remplissage devient inutile.
            var track = slider.transform.Find("Rail").GetComponent<Image>();
            track.sprite = hueSprite;
            track.color = Color.white;
            track.rectTransform.sizeDelta = new Vector2(0f, 20f);
            slider.fillRect.GetComponent<Image>().enabled = false;
        }

        // Éléments d'interface
        private Slider AddSlider(Vector2 leftCenter, float width)
        {
            var sliderObject = new GameObject("Curseur", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(page, false);
            var rect = (RectTransform)sliderObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = leftCenter;
            rect.sizeDelta = new Vector2(width, 42f);

            // Zone de visée large, pour attraper le curseur facilement avec le rayon.
            var hitArea = AddImage(rect, "Zone", new Color(0f, 0f, 0f, 0f));
            hitArea.raycastTarget = true;
            Stretch(hitArea.rectTransform);

            var track = AddImage(rect, "Rail", TrackColor);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 12f);
            track.rectTransform.anchoredPosition = Vector2.zero;

            var fillArea = AddRect(rect, "Zone de remplissage");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 12f);
            fillArea.anchoredPosition = Vector2.zero;
            var fill = AddImage(fillArea, "Remplissage", FillColor);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = AddRect(rect, "Zone de la poignée");
            Stretch(handleArea);
            var handle = AddImage(handleArea, "Poignée", HandleColor);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(24f, 0f);

            var slider = sliderObject.GetComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.colors = FeedbackColors();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        private Button AddButton(RectTransform parent, string label, Vector2 center, Vector2 size, float fontSize, Color color)
        {
            var image = AddImage(parent, "Bouton " + label, color);
            image.raycastTarget = true;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0f, 1f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = center;
            image.rectTransform.sizeDelta = size;

            if (!string.IsNullOrEmpty(label))
            {
                var text = AddText(image.rectTransform, label, fontSize, TextColor, TextAlignmentOptions.Center);
                Stretch(text.rectTransform);
            }

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = FeedbackColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        // Retour visuel : l'élément s'éclaircit quand le rayon le survole, et s'assombrit quand on appuie.
        private static ColorBlock FeedbackColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.pressedColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1.25f;
            colors.fadeDuration = 0.05f;
            return colors;
        }

        private static RectTransform AddRect(RectTransform parent, string objectName)
        {
            var rectObject = new GameObject(objectName, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            return (RectTransform)rectObject.transform;
        }

        private static Image AddImage(RectTransform parent, string objectName, Color color)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI AddText(RectTransform parent, string content, float size, Color color, TextAlignmentOptions alignment)
        {
            var textObject = new GameObject("Texte", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
