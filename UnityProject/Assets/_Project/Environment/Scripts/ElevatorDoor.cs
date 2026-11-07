using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace JourneyThroughXR.Building
{
    public enum RetractSide
    {
        Left,
        Right
    }

    [DisallowMultipleComponent]
    public class ElevatorDoor : MonoBehaviour
    {
        // Réglages
        [Tooltip("Côté vers lequel la porte se rétracte, vu depuis la cabine.")]
        [SerializeField] private RetractSide side = RetractSide.Left;

        [Tooltip("Durée de l'ouverture en secondes.")]
        [SerializeField] private float duration = 1f;

        [Tooltip("Largeur restante une fois ouverte (0,05 = 5 %).")]
        [SerializeField, Range(0.01f, 1f)] private float openedRatio = 0.05f;

        [Header("Événements")]
        public UnityEvent onOpened = new UnityEvent();
        public UnityEvent onClosed = new UnityEvent();

        // État
        private Transform pivot;
        private Coroutine running;

        public bool IsOpen { get; private set; }

        // Commandes
        public void Open(Vector3 viewerPosition)
        {
            if (IsOpen || !Application.isPlaying) return;
            EnsurePivot(viewerPosition);
            IsOpen = true;
            Play(openedRatio, onOpened);
        }

        public void Open() => Open(Camera.main != null ? Camera.main.transform.position : transform.position);

        public void Close()
        {
            if (!IsOpen || pivot == null || !Application.isPlaying) return;
            IsOpen = false;
            Play(1f, onClosed);
        }

        // Animation
        private void Play(float targetRatio, UnityEvent onDone)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Animate(targetRatio, onDone));
        }

        private IEnumerator Animate(float targetRatio, UnityEvent onDone)
        {
            float start = pivot.localScale.z;
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, time / duration);
                pivot.localScale = new Vector3(1f, 1f, Mathf.Lerp(start, targetRatio, t));
                yield return null;
            }
            pivot.localScale = new Vector3(1f, 1f, targetRatio);
            running = null;
            onDone.Invoke();
        }

        // Charnière de rétraction
        private void EnsurePivot(Vector3 viewerPosition)
        {
            if (pivot != null) return;

            var meshRenderer = GetComponentInChildren<Renderer>();
            Bounds bounds = meshRenderer.bounds;

            Vector3 widthAxis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
            Vector3 toViewer = viewerPosition - bounds.center;
            toViewer.y = 0f;
            Vector3 rightOfViewer = Vector3.Cross(Vector3.up, -toViewer.normalized);
            if (Vector3.Dot(widthAxis, rightOfViewer) > 0f) widthAxis = -widthAxis;
            if (side == RetractSide.Right) widthAxis = -widthAxis;

            float halfWidth = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(widthAxis.x), 0f, Mathf.Abs(widthAxis.z)));
            Vector3 jamb = bounds.center + widthAxis * halfWidth;
            jamb.y = bounds.min.y;

            pivot = new GameObject(name + "_Pivot").transform;
            pivot.SetParent(transform.parent, true);
            pivot.SetPositionAndRotation(jamb, Quaternion.LookRotation(-widthAxis, Vector3.up));
            transform.SetParent(pivot, true);
        }

        // Menus de test
        [ContextMenu("Test : Ouvrir")] private void DebugOpen() => Open();
        [ContextMenu("Test : Fermer")] private void DebugClose() => Close();
    }
}
