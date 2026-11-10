using JourneyThroughXR.Core;
using UnityEngine;

namespace JourneyThroughXR.Activities
{
    public class DrawingActivity : Activity
    {
        // Réglages
        [Tooltip("Écran sur lequel le visiteur dessine.")]
        [SerializeField] private DrawingBoard board;

        // Cycle de l'activité
        protected override void OnActivityReset()
        {
            if (board != null) board.ResetAll();
        }
    }
}
