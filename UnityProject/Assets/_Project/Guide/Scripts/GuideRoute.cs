using System.Collections.Generic;
using UnityEngine;

namespace JourneyThroughXR.Guide
{
    public class GuideRoute : MonoBehaviour
    {
        // Parcours
        public IReadOnlyList<GuideWaypoint> Waypoints
        {
            get
            {
                var list = new List<GuideWaypoint>();
                foreach (Transform child in transform)
                {
                    var waypoint = child.GetComponent<GuideWaypoint>();
                    if (waypoint != null && waypoint.gameObject.activeInHierarchy) list.Add(waypoint);
                }
                return list;
            }
        }

        // Parcours par défaut
        private readonly struct RoutePoint
        {
            public readonly string Name;
            public readonly bool IsStop;
            public readonly float X;
            public readonly float Z;

            public RoutePoint(string name, bool isStop, float x, float z)
            {
                Name = name;
                IsStop = isStop;
                X = x;
                Z = z;
            }
        }

        private static readonly RoutePoint[] DefaultRoute =
        {
            new RoutePoint("Accueil", true, -14.80f, -9.60f),
            new RoutePoint("Passage_01", false, -14.00f, -6.40f),
            new RoutePoint("Passage_02", false, -13.70f, -6.10f),
            new RoutePoint("PC", true, -9.40f, -5.80f),
            new RoutePoint("Passage_03", false, -16.40f, -5.60f),
            new RoutePoint("Dessin", true, -19.00f, -4.40f),
            new RoutePoint("Passage_04", false, -15.00f, -6.10f),
            new RoutePoint("Passage_05", false, -14.80f, -6.30f),
            new RoutePoint("Passage_06", false, -14.80f, -6.80f),
            new RoutePoint("Passage_07", false, -15.00f, -7.00f),
            new RoutePoint("Nettoyage", true, -19.00f, -7.50f),
            new RoutePoint("Passage_08", false, -31.90f, -7.80f),
            new RoutePoint("Passage_09", false, -32.10f, -8.00f),
            new RoutePoint("Passage_10", false, -34.50f, -11.80f),
            new RoutePoint("Passage_11", false, -34.40f, -12.70f),
            new RoutePoint("Voiture_RC", true, -33.60f, -14.20f),
            new RoutePoint("Passage_12", false, -28.70f, -16.20f),
            new RoutePoint("Passage_13", false, -24.80f, -15.80f),
            new RoutePoint("Passage_304", false, -22.00f, -14.50f),
            new RoutePoint("Passage_14", false, -16.40f, -13.10f),
            new RoutePoint("Passage_15", false, -14.80f, -13.30f),
            new RoutePoint("Passage_16", false, -14.50f, -13.60f),
            new RoutePoint("Sabre_Echecs", true, -8.00f, -14.00f),
            new RoutePoint("Passage_17", false, -14.50f, -13.60f),
            new RoutePoint("Passage_18", false, -14.70f, -13.40f),
            new RoutePoint("Fin", true, -14.80f, -9.00f),
        };

        [ContextMenu("Créer le parcours par défaut")]
        private void CreateDefaultRoute()
        {
            if (transform.childCount > 0)
            {
                Debug.LogWarning("[Parcours] Le parcours contient déjà des points. Supprimez-les d'abord.", this);
                return;
            }

            foreach (var point in DefaultRoute)
            {
                var child = new GameObject(point.Name);
#if UNITY_EDITOR
                UnityEditor.Undo.RegisterCreatedObjectUndo(child, "Créer le parcours");
#endif
                child.transform.SetParent(transform, false);
                child.transform.position = new Vector3(point.X, 0f, point.Z);
                child.AddComponent<GuideWaypoint>().IsStop = point.IsStop;
            }
        }

        // Aide visuelle
        private void OnDrawGizmos()
        {
            var points = Waypoints;
            Gizmos.color = new Color(0.2f, 0.8f, 1f);
            for (int i = 0; i < points.Count - 1; i++)
                Gizmos.DrawLine(points[i].transform.position + Vector3.up * 0.1f, points[i + 1].transform.position + Vector3.up * 0.1f);
        }
    }
}
