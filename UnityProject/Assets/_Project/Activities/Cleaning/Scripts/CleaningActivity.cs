using JourneyThroughXR.Core;
using JourneyThroughXR.Guide;
using UnityEngine;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Activité Nettoyage (A1), sanitaires mixte et PMR. Reliée à la progression (C1).
    /// Terminée quand les 3 zones d'un des sanitaires sont propres.
    /// Aide progressive si le visiteur n'avance plus :
    ///  1. le robot et le panneau disent précisément quoi faire, la zone suivante clignote en jaune ;
    ///  2. ensuite, les taches partent beaucoup plus vite au frottement, et le conseil est répété (tout le monde finit).
    /// </summary>
    public class CleaningActivity : Activity
    {
        // Réglages
        [Tooltip("Les sanitaires où l'activité est installée. Finir l'un d'eux suffit.")]
        [SerializeField] private CleaningRoom[] rooms;

        [Tooltip("Prendre une éponge démarre l'activité si le robot ne l'a pas déjà fait.")]
        [SerializeField] private bool startWhenGrabbed = true;

        [Header("Aide progressive (secondes sans progrès)")]
        [Tooltip("Premier conseil précis (quoi faire, où), la zone suivante clignote.")]
        [SerializeField] private float hintDelay = 25f;

        [Tooltip("Aide forte : les taches partent 2,5 fois plus vite au frottement.")]
        [SerializeField] private float assistDelay = 50f;

        [Tooltip("Rappel de l'aide forte tant que rien n'avance.")]
        [SerializeField] private float repeatDelay = 40f;

        [Tooltip("Bulle du robot. Vide : celle de la scène est trouvée seule.")]
        [SerializeField] private GuideSpeechBubble speech;

        [Tooltip("Le robot ne donne l'aide que si le visiteur est à moins de cette distance de lui (m). Le panneau, lui, l'affiche toujours.")]
        [SerializeField] private float robotHintDistance = 8f;

        // État
        private float lastProgress;
        private bool hintGiven;
        private bool assisted;
        private float nextRepeat;

        // Cycle de vie
        protected override void OnEnable()
        {
            base.OnEnable();
            if (speech == null) speech = FindAnyObjectByType<GuideSpeechBubble>();
            if (rooms == null) return;
            foreach (var room in rooms)
            {
                if (room == null) continue;
                room.Progressed += OnProgressed;
                room.Completed += OnRoomCompleted;
                room.SpongeGrabbed += OnSpongeGrabbed;
            }
        }

        protected override void OnDisable()
        {
            if (rooms != null)
                foreach (var room in rooms)
                {
                    if (room == null) continue;
                    room.Progressed -= OnProgressed;
                    room.Completed -= OnRoomCompleted;
                    room.SpongeGrabbed -= OnSpongeGrabbed;
                }
            base.OnDisable();
        }

        private void Update()
        {
            if (State != ActivityState.InProgress || rooms == null) return;
            float idle = Time.time - lastProgress;

            if (!hintGiven && idle >= hintDelay)
            {
                hintGiven = true;
                GiveHint(false);
            }

            if (!assisted && idle >= assistDelay)
            {
                assisted = true;
                foreach (var room in rooms) if (room != null) room.SetAssisted(true);
                GiveHint(true);
                nextRepeat = Time.time + repeatDelay;
            }
            else if (assisted && idle >= assistDelay && Time.time >= nextRepeat)
            {
                GiveHint(true);
                nextRepeat = Time.time + repeatDelay;
            }
        }

        // Événements
        private void OnSpongeGrabbed(CleaningRoom room)
        {
            if (startWhenGrabbed) StartActivity();
        }

        private void OnProgressed(CleaningRoom room)
        {
            lastProgress = Time.time;
            hintGiven = false;
            nextRepeat = Time.time + repeatDelay;
        }

        private void OnRoomCompleted(CleaningRoom room)
        {
            if (State == ActivityState.NotStarted) StartActivity();
            CompleteActivity();
        }

        // Cycle de l'activité
        protected override void OnActivityStarted()
        {
            lastProgress = Time.time;
            hintGiven = false;
        }

        protected override void OnActivityCompleted()
        {
            if (rooms == null) return;
            foreach (var room in rooms)
            {
                if (room == null) continue;
                room.SetAssisted(false);
                room.HighlightOnly(null);
                room.SetHint("");
            }
        }

        protected override void OnActivityReset()
        {
            assisted = false;
            hintGiven = false;
            if (rooms == null) return;
            foreach (var room in rooms)
            {
                if (room == null) continue;
                room.SetAssisted(false);
                room.ResetRoom();
            }
        }

        // Aide
        private void GiveHint(bool strong)
        {
            if (rooms == null) return;
            var room = TargetRoom();
            if (room == null) return;
            var spot = room.NextSpot();
            if (spot == null) return;

            string text;
            if (room.Sponge != null && !room.Sponge.IsHeld)
                text = "Prenez l'éponge jaune posée sur le lavabo : visez-la et appuyez sur le grip.";
            else if (strong)
                text = $"Il reste {spot.HintName}. Frottez-le de près avec l'éponge : quelques allers-retours suffisent maintenant.";
            else
                text = $"Frottez {spot.HintName} : posez l'éponge sur les taches et faites des allers-retours.";

            room.HighlightOnly(spot);
            room.SetHint(text);

            if (speech != null && VisitorNear(speech.transform.position, robotHintDistance))
                speech.Say(text);
        }

        /// Le sanitaire le plus avancé ; à égalité, le plus proche du visiteur.
        private CleaningRoom TargetRoom()
        {
            CleaningRoom best = null;
            int bestDone = -1;
            float bestDistance = float.MaxValue;
            var cam = Camera.main;
            foreach (var room in rooms)
            {
                if (room == null || room.IsComplete) continue;
                int done = room.CleanedCount;
                float distance = cam != null ? Vector3.Distance(cam.transform.position, room.Center) : 0f;
                if (done > bestDone || (done == bestDone && distance < bestDistance))
                {
                    best = room;
                    bestDone = done;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static bool VisitorNear(Vector3 point, float distance)
        {
            var cam = Camera.main;
            return cam != null && Vector3.Distance(cam.transform.position, point) <= distance;
        }
    }
}
