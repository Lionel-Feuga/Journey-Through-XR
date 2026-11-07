using UnityEngine;

namespace JourneyThroughXR.Core.Examples
{
    public class ExampleActivity : Activity
    {
        // Démarrage
        protected override void OnActivityStarted()
        {
            Debug.Log($"[{DisplayName}] Démarrée.", this);
        }

        // Réussite
        protected override void OnActivityCompleted()
        {
            Debug.Log($"[{DisplayName}] Réussie !", this);
        }

        // Remise à zéro
        protected override void OnActivityReset()
        {
            Debug.Log($"[{DisplayName}] Remise à zéro.", this);
        }
    }
}
