using System.Collections.Generic;
using UnityEngine;

namespace JourneyThroughXR.Building
{
    public class DoorOpener : MonoBehaviour
    {
        // Registre
        private static readonly List<DoorOpener> all = new List<DoorOpener>();
        public static IReadOnlyList<DoorOpener> All => all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        // Inscription
        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);
    }
}
