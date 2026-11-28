using TMPro;
using UnityEngine;

namespace JourneyThroughXR.Player
{
    /// <summary>
    /// Diagnostic lisible dans le casque, sans Quest Link : petit texte sous la carte du poignet, à gauche.
    /// Affiche la hauteur des yeux dans le jeu, la hauteur du casque, la taille du Character Controller
    /// et le dernier objet qui a repoussé la tête (avec la hauteur du contact).
    /// À poser sur l'objet qui porte « Wrist Map ». Menu : « Créer l'affichage de diagnostic ».
    /// À retirer (ou désactiver) une fois le problème réglé.
    /// </summary>
    public class HeightDebugReadout : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private PlayerHeight playerHeight;
        [SerializeField] private HeadWallBlocker blocker;
        [SerializeField] private CharacterController body;

        private void Awake()
        {
            if (playerHeight == null) playerHeight = FindAnyObjectByType<PlayerHeight>();
            if (blocker == null) blocker = FindAnyObjectByType<HeadWallBlocker>();
            if (body == null && playerHeight != null) body = playerHeight.GetComponent<CharacterController>();
            // Enregistre ce que le corps (Character Controller) touche.
            if (body != null && body.GetComponent<BodyHitRecorder>() == null) body.gameObject.AddComponent<BodyHitRecorder>();
        }

        private void LateUpdate()
        {
            if (label == null || !label.isActiveAndEnabled) return;
            var sb = new System.Text.StringBuilder();
            if (playerHeight != null)
                sb.Append($"Yeux (jeu) : {playerHeight.EyeHeightInGame:0.00} m au-dessus de « {playerHeight.BelowEyesName} »\n"
                          + $"Casque (réel) : {playerHeight.HeadsetHeight:0.00} m · yeux à {Camera.main.transform.position.y:0.00} m (monde)\n");
            if (body != null)
            {
                float top = body.transform.position.y + body.center.y + body.height / 2f + body.skinWidth;
                sb.Append($"Corps : haut à {top:0.00} m, rayon {body.radius:0.00}, pieds à {body.transform.position.y:0.00} m\n");
                var rec = body.GetComponent<BodyHitRecorder>();
                if (rec != null && Time.time - rec.LastTime < 10f)
                    sb.Append($"<color=#FFC060>Corps bloqué par « {rec.LastName} » à {rec.LastHeight:0.00} m</color>\n");
            }
            if (blocker != null && Time.time - blocker.LastBlockTime < 10f)
                sb.Append($"<color=#FF8080>Bloqué par « {blocker.LastBlockName} » à {blocker.LastBlockHeight:0.00} m</color>");
            else
                sb.Append("Tête : aucun blocage récent");
            label.text = sb.ToString();
        }

#if UNITY_EDITOR
        [ContextMenu("Créer l'affichage de diagnostic")]
        private void CreateLabel()
        {
            var map = transform.Find("Carte");
            if (map == null)
            {
                Debug.LogWarning("[Diagnostic] Pas de « Carte » sous cet objet.", this);
                return;
            }
            var old = map.Find("Diagnostic");
            if (old != null) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);

            var go = new GameObject("Diagnostic", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(map, false);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Diagnostic");
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -30f); // sous la carte, à gauche (le bouton est à droite)
            rect.sizeDelta = new Vector2(900f, 260f);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 36f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            tmp.text = "Diagnostic";
            UnityEditor.Undo.RecordObject(this, "Diagnostic");
            label = tmp;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[Diagnostic] Affichage créé sous la carte. Enregistrer la scène (Ctrl+S).", this);
        }
#endif
    }
}
