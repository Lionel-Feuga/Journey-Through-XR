using JourneyThroughXR.Core;
using UnityEngine;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Activité Ordinateurs (A7), open-space 309. Reliée à la progression (C1).
    /// Le contenu sur l'écran appellera CompleteActivity() à la réussite.
    /// En attendant, la fin passe par le robot (« Confirm With Robot » sur l'étape PC).
    /// </summary>
    public class ComputerActivity : Activity
    {
        // Réglages
        [Tooltip("Chaise du poste de travail.")]
        [SerializeField] private ComputerSeat seat;

        [Tooltip("S'asseoir démarre l'activité si le robot ne l'a pas déjà fait.")]
        [SerializeField] private bool startWhenSeated = true;

        // Cycle de vie
        protected override void OnEnable()
        {
            base.OnEnable();
            if (seat != null) seat.onSat.AddListener(OnSeated);
        }

        protected override void OnDisable()
        {
            if (seat != null) seat.onSat.RemoveListener(OnSeated);
            base.OnDisable();
        }

        private void OnSeated()
        {
            if (startWhenSeated) StartActivity();
        }

        // Cycle de l'activité
        protected override void OnActivityReset()
        {
            if (seat != null) seat.StandUp();
        }
    }
}
