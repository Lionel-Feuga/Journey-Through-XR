using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Panneau de départ de l'ascenseur : bouton « Démarrer » (celui d'origine, qui lance la visite)
    /// et bouton « Mode assis : » avec une croix rouge (debout, par défaut) ou une coche verte (assis).
    /// Le bouton Mode assis change seulement la hauteur ; la visite démarre avec « Démarrer ».
    /// À poser sur « Panneau_Demarrer ». Menu du composant : « Créer le bouton Mode assis ».
    /// </summary>
    public class HeightChoicePanel : MonoBehaviour
    {
        // Réglages
        [Tooltip("Bouton « Démarrer » d'origine (il appelle Elevator Start > StartVisit).")]
        [SerializeField] private Button startButton;

        [Tooltip("Bouton « Mode assis : ».")]
        [SerializeField] private Button seatedButton;

        [Tooltip("Croix ou coche du bouton Mode assis.")]
        [SerializeField] private SeatedIcon seatedIcon;

        [Tooltip("Hauteur du joueur. Vide : trouvée seule dans la scène.")]
        [SerializeField] private PlayerHeight playerHeight;

        private void Awake()
        {
            if (playerHeight == null) playerHeight = FindAnyObjectByType<PlayerHeight>();
            if (playerHeight == null) Debug.LogWarning("[Hauteur] Pas de Player Height dans la scène : l'ajouter sur le XR Origin.", this);
            if (seatedButton != null) seatedButton.onClick.AddListener(ToggleSeated);
        }

        private void OnEnable()
        {
            if (playerHeight != null) playerHeight.Changed += OnChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (playerHeight != null) playerHeight.Changed -= OnChanged;
        }

        public void ToggleSeated()
        {
            if (playerHeight != null) playerHeight.Toggle();
        }

        private void OnChanged(bool seated) => Refresh();

        private void Refresh()
        {
            if (seatedIcon != null) seatedIcon.Set(playerHeight != null && playerHeight.IsSeated);
        }

#if UNITY_EDITOR
        // Garde le bouton « Démarrer » d'origine (remis visible, en haut) et crée « Mode assis : » en dessous.
        [ContextMenu("Créer le bouton Mode assis")]
        private void CreateButtons()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Créer le bouton Mode assis");

            // Nettoyage des versions précédentes.
            foreach (var oldName in new[] { "Bouton_Debout", "Bouton_Assis", "Bouton_ModeAssis" })
            {
                var old = transform.Find(oldName);
                while (old != null)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);
                    old = transform.Find(oldName);
                }
            }

            Button original = startButton;
            if (original == null)
                foreach (var b in GetComponentsInChildren<Button>(true)) { original = b; break; }
            if (original == null)
            {
                Debug.LogWarning("[Hauteur] Pas de bouton « Démarrer » sous ce panneau.", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Créer le bouton Mode assis");
            startButton = original;
            var startRect = (RectTransform)original.transform;
            UnityEditor.Undo.RecordObject(startRect, "Démarrer");
            UnityEditor.Undo.RecordObject(original.gameObject, "Démarrer");
            original.gameObject.SetActive(true);
            Vector2 size = startRect.sizeDelta;
            const float gap = 20f;
            startRect.anchoredPosition = new Vector2(0f, size.y / 2f + gap / 2f);

            // Copie du bouton Démarrer (même style), sans son appel à StartVisit.
            var go = Instantiate(original.gameObject, original.transform.parent);
            go.name = "Bouton_ModeAssis";
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Bouton_ModeAssis");
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = new Vector2(0f, -(size.y / 2f + gap / 2f));
            seatedButton = go.GetComponent<Button>();
            while (seatedButton.onClick.GetPersistentEventCount() > 0)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(seatedButton.onClick, 0);

            // Texte à gauche, icône à droite.
            float iconSize = size.y * 0.5f;
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = "Mode assis :";
                text.enableAutoSizing = false;
                text.fontSize = 44f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                var textRect = text.rectTransform;
                textRect.offsetMin = new Vector2(10f, 0f);
                textRect.offsetMax = new Vector2(-(iconSize + 30f), 0f);
                UnityEditor.EditorUtility.SetDirty(text);
            }
            seatedIcon = SeatedIcon.Create(go.transform, new Vector2(size.x / 2f - iconSize / 2f - 25f, 0f), iconSize);

            UnityEditor.EditorUtility.SetDirty(seatedButton);
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[Hauteur] Boutons Démarrer et Mode assis prêts. Enregistrer la scène (Ctrl+S).", this);
        }
#endif
    }
}
