using JourneyThroughXR.Core;
using JourneyThroughXR.Guide;
using TMPro;
using UnityEngine;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Activité Voiture RC (A3), salle 305. Reliée à la progression (C1).
    /// Terminée quand la voiture a fait ses 3 tours.
    /// Aide progressive (tout le monde finit) :
    ///  1. conseils du robot et du tableau si rien n'avance (télécommande pas prise, voiture immobile) ;
    ///  2. après 2 sorties de piste, conseil précis selon l'endroit (virage ou looping) ;
    ///  3. sans tour terminé depuis longtemps (télécommande en main), la voiture freine seule avant les virages
    ///     et garde son élan dans le looping : il suffit de garder la gâchette appuyée.
    /// </summary>
    public class RCCarActivity : Activity
    {
        // Réglages
        [SerializeField] private RCTrack track;
        [SerializeField] private RCCar car;
        [SerializeField] private RCRemote remote;

        [Tooltip("Texte du tableau au fond du circuit.")]
        [SerializeField] private TMP_Text panel;

        [Tooltip("Prendre la télécommande démarre l'activité si le robot ne l'a pas déjà fait.")]
        [SerializeField] private bool startWhenGrabbed = true;

        [Header("Aide progressive")]
        [Tooltip("Secondes sans progrès avant un conseil (télécommande pas prise, voiture arrêtée).")]
        [SerializeField] private float hintDelay = 20f;

        [Tooltip("Sorties de piste avant le conseil précis.")]
        [SerializeField] private int hintAfterCrashes = 2;

        [Tooltip("Secondes sans tour terminé (télécommande en main) avant l'aide forte.")]
        [SerializeField] private float assistDelay = 75f;

        [Tooltip("Rappel du conseil tant que rien n'avance (s).")]
        [SerializeField] private float repeatDelay = 30f;

        [Tooltip("Bulle du robot. Vide : celle de la scène est trouvée seule.")]
        [SerializeField] private GuideSpeechBubble speech;

        [Tooltip("Le robot ne donne l'aide que si le visiteur est à moins de cette distance de lui (m). Le tableau, lui, l'affiche toujours.")]
        [SerializeField] private float robotHintDistance = 8f;

        // État
        private float lastProgress;
        private float lastLap;
        private float lastTravelled;
        private float nextHint;
        private bool assisted;
        private string hint = "";

        private const string HintColor = "#FFB020";
        private const string DoneColor = "#5CC276";

        public void Configure(RCTrack circuit, RCCar rcCar, RCRemote rcRemote, TMP_Text board)
        {
            track = circuit;
            car = rcCar;
            remote = rcRemote;
            panel = board;
        }

        // Cycle de vie
        protected override void OnEnable()
        {
            base.OnEnable();
            if (track == null) track = GetComponentInParent<RCTrack>();
            if (track != null)
            {
                if (car == null) car = track.GetComponentInChildren<RCCar>();
                if (remote == null) remote = track.GetComponentInChildren<RCRemote>();
            }
            if (speech == null) speech = FindAnyObjectByType<GuideSpeechBubble>();
            if (car != null)
            {
                car.Crashed += OnCrashed;
                car.LapCompleted += OnLap;
            }
            if (remote != null) remote.Grabbed += OnGrabbed;
            RefreshPanel();
        }

        protected override void OnDisable()
        {
            if (car != null)
            {
                car.Crashed -= OnCrashed;
                car.LapCompleted -= OnLap;
            }
            if (remote != null) remote.Grabbed -= OnGrabbed;
            base.OnDisable();
        }

        private void Update()
        {
            if (car == null) return;

            // La voiture qui avance compte comme un progrès.
            if (car.Travelled - lastTravelled > 0.3f)
            {
                lastTravelled = car.Travelled;
                lastProgress = Time.time;
            }

            if (State != ActivityState.InProgress) return;

            bool held = remote != null && remote.IsHeld;
            if (Time.time - lastProgress >= hintDelay && Time.time >= nextHint)
            {
                nextHint = Time.time + repeatDelay;
                if (!held)
                    GiveHint("Prenez la télécommande posée sur le bord de la table : visez-la et appuyez sur le grip.");
                else
                    GiveHint("Appuyez sur la gâchette, sous l'index, pour faire avancer la voiture. Plus vous appuyez, plus elle va vite.");
            }

            if (!assisted && held && Time.time - lastLap >= assistDelay) EnableAssist();
        }

        // Événements
        private void OnGrabbed(RCRemote r)
        {
            if (startWhenGrabbed) StartActivity();
            lastProgress = Time.time;
            if (State == ActivityState.InProgress && !string.IsNullOrEmpty(hint) && hint.StartsWith("Prenez")) SetHint("");
        }

        private void OnCrashed(RCCar c, RCTrack.Piece piece, RCCar.CrashKind kind)
        {
            if (remote != null) remote.Pulse(0.9f, 0.3f);
            if (State != ActivityState.InProgress) { RefreshPanel(); return; }

            if (c.Crashes >= hintAfterCrashes)
            {
                string text = kind == RCCar.CrashKind.Loop
                    ? "Pour le looping, il faut de l'élan : gâchette à fond dans la ligne droite juste avant."
                    : $"Trop vite dans {piece.hint} ! Relâchez la gâchette juste avant le virage, puis réaccélérez à la sortie. Le voyant orange vous prévient.";
                // Le robot ne le répète qu'une fois par type d'erreur ; le tableau le garde affiché.
                GiveHint(text, c.Crashes == hintAfterCrashes || hint != text);
            }
            else
            {
                RefreshPanel();
            }
        }

        private void OnLap(RCCar c, int lap)
        {
            lastLap = Time.time;
            lastProgress = Time.time;
            if (remote != null) remote.Pulse(0.5f, 0.15f);
            if (lap >= c.Laps)
            {
                if (State == ActivityState.NotStarted) StartActivity();
                CompleteActivity();
                return;
            }
            RefreshPanel();
        }

        private void EnableAssist()
        {
            assisted = true;
            if (car != null) car.Assisted = true;
            GiveHint("Je vous donne un coup de main : la voiture freine maintenant toute seule avant les virages. Gardez la gâchette appuyée !");
        }

        // Cycle de l'activité
        protected override void OnActivityStarted()
        {
            lastProgress = Time.time;
            lastLap = Time.time;
            nextHint = 0f;
            RefreshPanel();
        }

        protected override void OnActivityCompleted()
        {
            hint = "";
            if (speech != null && speech.IsSpeaking) speech.Hide();
            RefreshPanel();
        }

        protected override void OnActivityReset()
        {
            assisted = false;
            hint = "";
            if (car != null)
            {
                car.Assisted = false;
                car.ResetCar();
            }
            lastTravelled = 0f;
            if (remote != null) remote.ReturnHome();
            RefreshPanel();
        }

        // Aide
        private void GiveHint(string text, bool robotSays = true)
        {
            SetHint(text);
            if (robotSays && speech != null && VisitorNear(speech.transform.position, robotHintDistance))
                speech.Say(text);
        }

        private void SetHint(string text)
        {
            hint = text;
            RefreshPanel();
        }

        public void RefreshPanel()
        {
            if (panel == null) return;
            if (State == ActivityState.Completed)
            {
                panel.text = $"<color={DoneColor}><b>Bravo, {(car != null ? car.Laps : 3)} tours !</b></color>\n<size=60%>Vous pouvez continuer à piloter.</size>";
                return;
            }
            int laps = car != null ? car.Laps : 3;
            int lap = car != null ? Mathf.Min(car.Lap + 1, laps) : 1;
            int crashes = car != null ? car.Crashes : 0;
            string text = $"<b>Tour {lap}/{laps}</b>\n<size=55%>Sorties de piste : {crashes}</size>";
            if (assisted) text += "\n<size=45%>Aide au freinage activée</size>";
            if (!string.IsNullOrEmpty(hint)) text += $"\n<size=45%><color={HintColor}>{hint}</color></size>";
            panel.text = text;
        }

        private static bool VisitorNear(Vector3 point, float distance)
        {
            var cam = Camera.main;
            return cam != null && Vector3.Distance(cam.transform.position, point) <= distance;
        }
    }
}
