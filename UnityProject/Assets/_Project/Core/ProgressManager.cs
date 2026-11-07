using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Core
{
    [DisallowMultipleComponent]
    public class ProgressManager : MonoBehaviour
    {
        // Données
        public static ProgressManager Instance { get; private set; }

        private static readonly List<Activity> registered = new List<Activity>();
        private static readonly HashSet<string> knownIds = new HashSet<string>();
        private static readonly HashSet<string> completedIds = new HashSet<string>();

        // Événements
        public static event Action<Activity> ActivityStateChanged;
        public static event Action AllActivitiesCompleted;

        [Tooltip("Appelé quand toutes les activités sont terminées.")]
        public UnityEvent onAllActivitiesCompleted = new UnityEvent();

        [Tooltip("Écrit chaque changement d'état dans la Console.")]
        [SerializeField] private bool logToConsole = true;

        // Lecture de la progression
        public static IReadOnlyList<Activity> Activities => registered;
        public static int TotalCount => knownIds.Count;
        public static int CompletedCount => completedIds.Count;
        public static bool AllCompleted => knownIds.Count > 0 && completedIds.Count == knownIds.Count;

        public static bool IsCompleted(string activityId) => completedIds.Contains(activityId);

        public static Activity Find(string activityId)
        {
            foreach (var activity in registered)
                if (activity.ActivityId == activityId) return activity;
            return null;
        }

        // Initialisation
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            registered.Clear();
            knownIds.Clear();
            completedIds.Clear();
            ActivityStateChanged = null;
            AllActivitiesCompleted = null;
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[Progression] Deux ProgressManager dans la scène : celui de « {name} » est ignoré.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Inscription des activités
        internal static void Register(Activity activity)
        {
            if (registered.Contains(activity)) return;

            if (Find(activity.ActivityId) != null)
                Debug.LogWarning($"[Progression] L'identifiant « {activity.ActivityId} » est utilisé deux fois. Donnez un Activity Id unique à chaque activité.", activity);

            registered.Add(activity);
            knownIds.Add(activity.ActivityId);
            activity.StateChanged += HandleStateChanged;
        }

        internal static void Unregister(Activity activity)
        {
            if (!registered.Remove(activity)) return;
            activity.StateChanged -= HandleStateChanged;
        }

        // Rejouer
        public static void ResetAll()
        {
            completedIds.Clear();
            foreach (var activity in registered.ToArray())
                activity.ResetActivity();
        }

        public void ResetAllActivities() => ResetAll();

        // Suivi des changements
        private static void HandleStateChanged(Activity activity)
        {
            bool wasAllCompleted = AllCompleted;

            if (activity.IsCompleted) completedIds.Add(activity.ActivityId);
            else completedIds.Remove(activity.ActivityId);

            if (Instance == null || Instance.logToConsole)
                Debug.Log($"[Progression] {activity.DisplayName} : {activity.State} ({CompletedCount}/{TotalCount} terminées)", activity);

            ActivityStateChanged?.Invoke(activity);

            if (!wasAllCompleted && AllCompleted)
            {
                if (Instance == null || Instance.logToConsole)
                    Debug.Log("[Progression] Toutes les activités sont terminées.");
                AllActivitiesCompleted?.Invoke();
                if (Instance != null) Instance.onAllActivitiesCompleted.Invoke();
            }
        }
    }
}
