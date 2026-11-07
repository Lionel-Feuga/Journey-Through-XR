using System;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Core
{
    public enum ActivityState
    {
        NotStarted,
        InProgress,
        Completed
    }

    public abstract class Activity : MonoBehaviour
    {
        // Réglages
        [Header("Identité")]
        [Tooltip("Identifiant unique, sans espace (ex. cleaning, drawing, rc_car).")]
        [SerializeField] private string activityId = "";

        [Tooltip("Nom affiché au visiteur (ex. Nettoyage).")]
        [SerializeField] private string displayName = "";

        [Tooltip("Texte de la fenêtre de tutoriel.")]
        [SerializeField, TextArea(2, 6)] private string tutorialText = "";

        [Header("Événements")]
        public UnityEvent onStarted = new UnityEvent();
        public UnityEvent onCompleted = new UnityEvent();
        public UnityEvent onReset = new UnityEvent();

        // État
        public event Action<Activity> StateChanged;

        public string ActivityId => string.IsNullOrWhiteSpace(activityId) ? gameObject.name : activityId.Trim();
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ActivityId : displayName;
        public string TutorialText => tutorialText;
        public ActivityState State { get; private set; } = ActivityState.NotStarted;
        public bool IsCompleted => State == ActivityState.Completed;

        // Inscription
        protected virtual void OnEnable() => ProgressManager.Register(this);
        protected virtual void OnDisable() => ProgressManager.Unregister(this);

        // Cycle de vie
        public void StartActivity()
        {
            if (State != ActivityState.NotStarted) return;
            SetState(ActivityState.InProgress);
            OnActivityStarted();
            onStarted.Invoke();
        }

        public void CompleteActivity()
        {
            if (State == ActivityState.Completed) return;
            SetState(ActivityState.Completed);
            OnActivityCompleted();
            onCompleted.Invoke();
        }

        public void ResetActivity()
        {
            OnActivityReset();
            SetState(ActivityState.NotStarted);
            onReset.Invoke();
        }

        // Points d'extension
        protected virtual void OnActivityStarted() { }
        protected virtual void OnActivityCompleted() { }
        protected virtual void OnActivityReset() { }

        private void SetState(ActivityState newState)
        {
            if (State == newState) return;
            State = newState;
            StateChanged?.Invoke(this);
        }

        // Menus de test
        [ContextMenu("Test : Démarrer")] protected void DebugStart() => StartActivity();
        [ContextMenu("Test : Terminer")] protected void DebugComplete() => CompleteActivity();
        [ContextMenu("Test : Réinitialiser")] protected void DebugReset() => ResetActivity();
    }
}
