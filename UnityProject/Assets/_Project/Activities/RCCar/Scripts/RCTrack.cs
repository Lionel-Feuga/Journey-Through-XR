using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Le circuit de voiture RC (A3), posé sur une table de la salle 305.
    /// Tracé fermé : ligne droite du départ avec un pont, virage, looping au fond, virage.
    /// La route est générée à l'ouverture de la scène et au lancement (rien de lourd n'est enregistré).
    /// Un menu crée la table, le circuit, la voiture, la télécommande, le tableau et l'activité.
    /// </summary>
    [ExecuteAlways]
    public class RCTrack : MonoBehaviour
    {
        public enum PieceKind { Straight, Loop, Curve, Bridge }

        public class Piece
        {
            public string name;
            public string hint;      // ex. « le virage du fond »
            public PieceKind kind;
            public float start;      // distance depuis la ligne de départ (m)
            public float length;
            public float radius;     // virage ou looping
            public int respawnPiece; // pièce où la voiture est reposée après une sortie
        }

        // Réglages
        [Header("Tracé (m)")]
        [Tooltip("Hauteur du dessus de la table.")]
        [SerializeField] private float tableHeight = 0.8f;

        [Tooltip("Longueur des lignes droites (chacune des deux grandes).")]
        [SerializeField] private float straightLength = 1.4f;

        [Tooltip("Rayon des deux virages en épingle.")]
        [SerializeField] private float curveRadius = 0.6f;

        [Tooltip("Rayon du looping.")]
        [SerializeField] private float loopRadius = 0.25f;

        [Tooltip("Décalage de côté du looping (la sortie passe à côté de l'entrée).")]
        [SerializeField] private float loopShift = 0.2f;

        [Tooltip("Hauteur du pont sur la ligne droite du fond.")]
        [SerializeField] private float bridgeHeight = 0.12f;

        [Tooltip("Largeur de la piste (2 voies).")]
        [SerializeField] private float roadWidth = 0.16f;

        [Header("Matériaux (créés par le menu)")]
        [SerializeField] private Material roadMaterial;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Material loopMaterial;
        [SerializeField] private Material curbMaterial;

        // Accès
        public float Length { get; private set; }
        public IReadOnlyList<Piece> Pieces => pieces;
        public float RoadWidth => roadWidth;
        public float TableHeight => tableHeight;

        private const float Step = 0.02f;
        private const float RoadThickness = 0.012f;

        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<Vector3> forwards = new List<Vector3>();
        private readonly List<Vector3> ups = new List<Vector3>();
        private readonly List<float> distances = new List<float>();
        private readonly List<int> pieceOfSample = new List<int>();
        private bool built;
        private Mesh mesh;

        // Cycle de vie
        private void OnEnable()
        {
            built = false;
            EnsureBuilt();
        }

        private void OnValidate() => built = false;

        private void Update()
        {
            if (!built) EnsureBuilt();
        }

        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        public void EnsureBuilt()
        {
            if (built) return;
            BuildPath();
            BuildRoadMesh();
            built = true;
        }

        // Tracé
        private void BuildPath()
        {
            pieces.Clear();
            points.Clear();
            forwards.Clear();
            ups.Clear();
            distances.Clear();
            pieceOfSample.Clear();

            float half = straightLength * 0.5f;
            float y = tableHeight + RoadThickness;
            float runUp = half;
            float loopLength = 2f * Mathf.PI * loopRadius;
            float curveLength = Mathf.PI * curveRadius;

            // 0 Départ : ligne droite avant (côté visiteur), avec le pont
            var start = new Vector3(-half, y, -curveRadius);
            AddPiece("Depart_Pont", "la ligne droite du départ", PieceKind.Bridge, straightLength, 0f, 0,
                u => start + Vector3.right * u + Vector3.up * (bridgeHeight * 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * u / straightLength))), null);

            // 1 Virage de droite (épingle à gauche, centre côté +X)
            Vector3 c1 = new Vector3(half, y, 0f);
            AddPiece("Virage_Droite", "le virage au bout de la table, côté droit", PieceKind.Curve, curveLength, curveRadius, 1,
                u => Arc(c1, -Mathf.PI * 0.5f + u / curveRadius), null);

            // 2 Élan : ligne droite du fond avant le looping (sens -X)
            var backStart = new Vector3(half, y, curveRadius);
            AddPiece("Elan_Looping", "la ligne droite du fond", PieceKind.Straight, runUp, 0f, 1,
                u => backStart + Vector3.left * u, null);

            // 3 Looping, au fond de la table. La sortie est décalée vers l'intérieur du circuit.
            Vector3 inward = Vector3.back; // côté intérieur de la ligne droite du fond
            Vector3 loopStart = backStart + Vector3.left * runUp;
            AddPiece("Looping", "le looping", PieceKind.Loop, loopLength, loopRadius, 1,
                u =>
                {
                    float t = u / loopRadius;
                    return loopStart + Vector3.left * (loopRadius * Mathf.Sin(t))
                           + Vector3.up * (loopRadius * (1f - Mathf.Cos(t)))
                           + inward * (loopShift * u / loopLength);
                },
                u =>
                {
                    float t = u / loopRadius;
                    return (Vector3.up * Mathf.Cos(t) - Vector3.left * Mathf.Sin(t)).normalized;
                });

            // 4 Sortie du looping : revient doucement dans l'axe
            Vector3 exitStart = loopStart + inward * loopShift;
            AddPiece("Sortie_Looping", "la sortie du looping", PieceKind.Straight, runUp, 0f, 1,
                u => exitStart + Vector3.left * u - inward * (loopShift * Smooth(u / runUp)), null);

            // 5 Virage de gauche (retour à la ligne de départ)
            Vector3 c2 = new Vector3(-half, y, 0f);
            AddPiece("Virage_Gauche", "le virage au bout de la table, côté gauche", PieceKind.Curve, curveLength, curveRadius, 5,
                u => Arc(c2, Mathf.PI * 0.5f + u / curveRadius), null);

            Length = 0f;
            foreach (var p in pieces) Length += p.length;

            // Directions : avant par différence finie, haut vertical sauf dans le looping
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 next = points[(i + 1) % n];
                Vector3 prev = points[(i - 1 + n) % n];
                Vector3 fwd = (next - prev).normalized;
                Vector3 up = ups[i];
                Vector3 side = Vector3.Cross(up, fwd).normalized;
                up = Vector3.Cross(fwd, side).normalized;
                forwards.Add(fwd);
                ups[i] = up;
            }
        }

        // Angle mesuré depuis +X vers +Z : la voiture tourne à gauche (sens trigonométrique vu de dessus).
        private Vector3 Arc(Vector3 center, float angle)
        {
            return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * curveRadius;
        }

        private void AddPiece(string name, string hint, PieceKind kind, float length, float radius, int respawn,
            Func<float, Vector3> position, Func<float, Vector3> up)
        {
            int index = pieces.Count;
            float start = 0f;
            foreach (var p in pieces) start += p.length;
            pieces.Add(new Piece { name = name, hint = hint, kind = kind, start = start, length = length, radius = radius, respawnPiece = respawn });

            int count = Mathf.Max(2, Mathf.CeilToInt(length / Step));
            for (int i = 0; i < count; i++)
            {
                float u = length * i / count;
                points.Add(position(u));
                ups.Add(up != null ? up(u) : Vector3.up);
                distances.Add(start + u);
                pieceOfSample.Add(index);
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // Lecture du tracé (repère local du circuit)
        public void Evaluate(float distance, out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            EnsureBuilt();
            float d = Wrap(distance);
            int n = points.Count;
            int i = FindSample(d);
            int j = (i + 1) % n;
            float d0 = distances[i];
            float d1 = j == 0 ? Length : distances[j];
            float t = d1 > d0 ? Mathf.Clamp01((d - d0) / (d1 - d0)) : 0f;
            position = Vector3.Lerp(points[i], points[j], t);
            forward = Vector3.Slerp(forwards[i], forwards[j], t).normalized;
            up = Vector3.Slerp(ups[i], ups[j], t).normalized;
        }

        /// Position et orientation dans le monde, décalées sur une voie (m, + = à droite).
        public void EvaluateWorld(float distance, float lane, out Vector3 position, out Quaternion rotation, out Vector3 forward, out Vector3 up)
        {
            Evaluate(distance, out var p, out var f, out var u);
            Vector3 side = Vector3.Cross(u, f);
            position = transform.TransformPoint(p + side * lane);
            forward = transform.TransformDirection(f);
            up = transform.TransformDirection(u);
            rotation = Quaternion.LookRotation(forward, up);
        }

        public Piece PieceAt(float distance)
        {
            EnsureBuilt();
            return pieces[pieceOfSample[FindSample(Wrap(distance))]];
        }

        public int PieceIndexAt(float distance)
        {
            EnsureBuilt();
            return pieceOfSample[FindSample(Wrap(distance))];
        }

        /// Hauteur locale de la direction (pente) : + en montée.
        public float SlopeAt(float distance)
        {
            Evaluate(distance, out _, out var f, out _);
            return f.y;
        }

        /// Distance jusqu'au prochain virage (0 si on y est déjà), cherchée sur « horizon » mètres.
        public float DistanceToNextCurve(float distance, float horizon, out Piece curve)
        {
            EnsureBuilt();
            float d = Wrap(distance);
            curve = null;
            for (int k = 0; k <= pieces.Count; k++)
            {
                var p = pieces[(PieceIndexAt(d) + k) % pieces.Count];
                if (p.kind != PieceKind.Curve) continue;
                float gap = p.start - d;
                if (p.start + p.length <= d) gap += Length;
                if (gap < 0f) gap = 0f;
                if (gap > horizon) return float.MaxValue;
                curve = p;
                return gap;
            }
            return float.MaxValue;
        }

        public float Wrap(float distance)
        {
            if (Length <= 0f) return 0f;
            distance %= Length;
            if (distance < 0f) distance += Length;
            return distance;
        }

        private int FindSample(float d)
        {
            int lo = 0, hi = distances.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (distances[mid] <= d) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        // Route (maillage)
        private void BuildRoadMesh()
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) return;

            if (mesh == null)
            {
                mesh = new Mesh { name = "Circuit_RC", hideFlags = HideFlags.DontSave };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.Clear();

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var road = new List<int>();
            var lines = new List<int>();
            var loop = new List<int>();
            var curb = new List<int>();

            int n = points.Count;
            float hw = roadWidth * 0.5f;
            const float lineHalf = 0.003f;
            const float edge = 0.012f;
            const float lift = 0.0015f;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[i], u = ups[i];
                Vector3 s = Vector3.Cross(u, forwards[i]).normalized;
                Vector3 bottom = p - u * RoadThickness;
                // 0-1 dessus, 2-3 dessous, 4-5 côté gauche, 6-7 côté droit
                Add(p - s * hw, u); Add(p + s * hw, u);
                Add(bottom - s * hw, -u); Add(bottom + s * hw, -u);
                Add(p - s * hw, -s); Add(bottom - s * hw, -s);
                Add(p + s * hw, s); Add(bottom + s * hw, s);
                // 8-11 rails des deux voies (gris clair)
                Vector3 top = p + u * lift;
                Add(top + s * (-roadWidth * 0.25f - lineHalf), u); Add(top + s * (-roadWidth * 0.25f + lineHalf), u);
                Add(top + s * (roadWidth * 0.25f - lineHalf), u); Add(top + s * (roadWidth * 0.25f + lineHalf), u);
                // 12-15 bordures
                Add(top - s * hw, u); Add(top - s * (hw - edge), u);
                Add(top + s * (hw - edge), u); Add(top + s * hw, u);
            }

            const int V = 16;
            for (int i = 0; i < n; i++)
            {
                int a = i * V, b = ((i + 1) % n) * V;
                var kind = pieces[pieceOfSample[i]].kind;
                var body = kind == PieceKind.Loop ? loop : road;
                Quad(body, a + 0, a + 1, b + 0, b + 1, false);
                Quad(body, a + 2, a + 3, b + 2, b + 3, true);
                Quad(body, a + 4, a + 5, b + 4, b + 5, true);
                Quad(body, a + 6, a + 7, b + 6, b + 7, false);
                Quad(lines, a + 8, a + 9, b + 8, b + 9, false);
                Quad(lines, a + 10, a + 11, b + 10, b + 11, false);
                // Bordures rouges et blanches dans les virages, blanches ailleurs
                var edges = kind == PieceKind.Curve && (i / 3) % 2 == 0 ? curb : lines;
                Quad(edges, a + 12, a + 13, b + 12, b + 13, false);
                Quad(edges, a + 14, a + 15, b + 14, b + 15, false);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 4;
            mesh.SetTriangles(road, 0);
            mesh.SetTriangles(lines, 1);
            mesh.SetTriangles(loop, 2);
            mesh.SetTriangles(curb, 3);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { roadMaterial, lineMaterial, loopMaterial, curbMaterial };

            void Add(Vector3 v, Vector3 nrm)
            {
                verts.Add(v);
                normals.Add(nrm);
            }

            // a0-a1 : bord gauche-droit de l'échantillon courant, b0-b1 : du suivant
            void Quad(List<int> tris, int a0, int a1, int b0, int b1, bool flip)
            {
                if (!flip)
                {
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
                else
                {
                    tris.Add(a0); tris.Add(a1); tris.Add(b0);
                    tris.Add(a1); tris.Add(b1); tris.Add(b0);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!built) return;
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < points.Count; i += 5) Gizmos.DrawLine(points[i], points[i] + ups[i] * 0.05f);
        }

#if UNITY_EDITOR
        // Construction dans l'éditeur
        private static readonly string[] GeneratedNames = { "Table", "Ligne_Depart", "Piliers_Pont", "Voiture", "Telecommande", "Tableau" };

        [ContextMenu("Créer le circuit")]
        private void BuildAll()
        {
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Créer le circuit");
            // Les réglages de la voiture et de la télécommande (vitesses, position dans la main...) sont gardés.
            var oldCar = GetComponentInChildren<RCCar>(true);
            var oldRemote = GetComponentInChildren<RCRemote>(true);
            string carSettings = oldCar != null ? UnityEditor.EditorJsonUtility.ToJson(oldCar) : null;
            string remoteSettings = oldRemote != null ? UnityEditor.EditorJsonUtility.ToJson(oldRemote) : null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (Array.IndexOf(GeneratedNames, child.name) >= 0) UnityEditor.Undo.DestroyObjectImmediate(child.gameObject);
            }

            // Matériaux
            roadMaterial = LoadOrCreate("RC_Route", new Color(0.13f, 0.13f, 0.15f), 0f, 0.35f, false);
            lineMaterial = LoadOrCreate("RC_Rails", new Color(0.85f, 0.86f, 0.88f), 0.6f, 0.6f, false);
            loopMaterial = LoadOrCreate("RC_Looping", new Color(1f, 0.45f, 0.05f), 0f, 0.5f, false);
            curbMaterial = LoadOrCreate("RC_Bordure", new Color(0.85f, 0.1f, 0.1f), 0f, 0.4f, false);
            var tableMat = LoadOrCreate("RC_Table", new Color(0.2f, 0.45f, 0.25f), 0f, 0.15f, false);
            var legMat = LoadOrCreate("RC_Pieds", new Color(0.3f, 0.3f, 0.32f), 0.8f, 0.5f, false);
            var carMat = LoadOrCreate("RC_Carrosserie", new Color(0.12f, 0.3f, 0.85f), 0.3f, 0.8f, false);
            var glassMat = LoadOrCreate("RC_Vitres", new Color(0.08f, 0.1f, 0.14f), 0f, 0.9f, false);
            var tyreMat = LoadOrCreate("RC_Pneus", new Color(0.05f, 0.05f, 0.05f), 0f, 0.2f, false);
            var remoteMat = LoadOrCreate("RC_Telecommande", new Color(0.18f, 0.18f, 0.2f), 0f, 0.4f, false);
            var triggerMat = LoadOrCreate("RC_Gachette", new Color(0.9f, 0.15f, 0.1f), 0f, 0.5f, false);
            var ledMat = LoadOrCreate("RC_Voyant", new Color(0.2f, 0.9f, 0.3f), 0f, 0f, true);
            var boardMat = LoadOrCreate("RC_Tableau", new Color(0.06f, 0.07f, 0.1f), 0f, 0f, true);
            var whiteMat = LoadOrCreate("RC_Blanc", Color.white, 0f, 0.3f, false);
            var blackMat = LoadOrCreate("RC_Noir", Color.black, 0f, 0.3f, false);

            // Route
            if (GetComponent<MeshFilter>() == null) UnityEditor.Undo.AddComponent<MeshFilter>(gameObject);
            if (GetComponent<MeshRenderer>() == null) UnityEditor.Undo.AddComponent<MeshRenderer>(gameObject);
            built = false;
            EnsureBuilt();

            // Table : 3 x 1,8 m, légèrement décalée vers l'avant
            var table = Group("Table", transform, Vector3.zero, Quaternion.identity);
            var tableCenter = new Vector3(0f, tableHeight - 0.02f, -0.1f);
            Box("Plateau", table, tableCenter, new Vector3(3f, 0.04f, 1.8f), tableMat);
            foreach (var corner in new[] { new Vector2(-1.4f, -0.9f), new Vector2(1.4f, -0.9f), new Vector2(-1.4f, 0.7f), new Vector2(1.4f, 0.7f) })
            {
                var leg = Primitive(PrimitiveType.Cylinder, "Pied", table, legMat);
                leg.transform.localPosition = new Vector3(corner.x, (tableHeight - 0.04f) * 0.5f, corner.y);
                leg.transform.localScale = new Vector3(0.05f, (tableHeight - 0.04f) * 0.5f, 0.05f);
            }

            // Ligne de départ en damier
            var startLine = Group("Ligne_Depart", transform, Vector3.zero, Quaternion.identity);
            Evaluate(0.01f, out var sp, out var sf, out var su);
            startLine.localPosition = sp + su * 0.002f;
            startLine.localRotation = Quaternion.LookRotation(sf, su);
            const int squares = 8;
            float cell = roadWidth / squares;
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < squares; col++)
                    NoCollider(Box("Case", startLine, new Vector3(-roadWidth * 0.5f + cell * (col + 0.5f), 0f, cell * (row - 0.5f)),
                        new Vector3(cell, 0.002f, cell), (row + col) % 2 == 0 ? whiteMat : blackMat));

            // Piliers sous le pont
            var pillars = Group("Piliers_Pont", transform, Vector3.zero, Quaternion.identity);
            var bridge = pieces[0];
            foreach (float f in new[] { 0.35f, 0.5f, 0.65f })
            {
                Evaluate(bridge.start + bridge.length * f, out var bp, out _, out _);
                float h = bp.y - RoadThickness - tableHeight;
                if (h < 0.01f) continue;
                var pillar = Primitive(PrimitiveType.Cylinder, "Pilier", pillars, legMat);
                NoCollider(pillar);
                pillar.transform.localPosition = new Vector3(bp.x, tableHeight + h * 0.5f, bp.z);
                pillar.transform.localScale = new Vector3(0.025f, h * 0.5f, 0.025f);
            }

            // Voiture (le pivot est sur la route)
            var carGo = new GameObject("Voiture");
            carGo.transform.SetParent(transform, false);
            UnityEditor.Undo.RegisterCreatedObjectUndo(carGo, "Voiture");
            var bodyRoot = new GameObject("Carrosserie").transform;
            bodyRoot.SetParent(carGo.transform, false);
            NoCollider(Box("Caisse", bodyRoot, new Vector3(0f, 0.02f, 0f), new Vector3(0.065f, 0.022f, 0.14f), carMat));
            NoCollider(Box("Cockpit", bodyRoot, new Vector3(0f, 0.038f, -0.01f), new Vector3(0.05f, 0.018f, 0.06f), glassMat));
            NoCollider(Box("Aileron", bodyRoot, new Vector3(0f, 0.045f, -0.065f), new Vector3(0.066f, 0.004f, 0.018f), carMat));
            NoCollider(Box("Phare_G", bodyRoot, new Vector3(-0.022f, 0.022f, 0.07f), new Vector3(0.014f, 0.008f, 0.003f), whiteMat));
            NoCollider(Box("Phare_D", bodyRoot, new Vector3(0.022f, 0.022f, 0.07f), new Vector3(0.014f, 0.008f, 0.003f), whiteMat));
            var wheels = new List<Transform>();
            foreach (var w in new[] { new Vector3(-0.035f, 0.012f, 0.045f), new Vector3(0.035f, 0.012f, 0.045f), new Vector3(-0.035f, 0.012f, -0.045f), new Vector3(0.035f, 0.012f, -0.045f) })
            {
                var pivot = new GameObject("Roue").transform;
                pivot.SetParent(carGo.transform, false);
                pivot.localPosition = w;
                var tyre = Primitive(PrimitiveType.Cylinder, "Pneu", pivot, tyreMat);
                NoCollider(tyre);
                tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tyre.transform.localScale = new Vector3(0.024f, 0.006f, 0.024f);
                wheels.Add(pivot);
            }
            var car = carGo.AddComponent<RCCar>();
            if (carSettings != null) UnityEditor.EditorJsonUtility.FromJsonOverwrite(carSettings, car);
            car.Configure(this, wheels.ToArray());
            EvaluateWorld(0.03f, 0.04f, out var carPos, out var carRot, out _, out _);
            carGo.transform.SetPositionAndRotation(carPos, carRot);
            UnityEditor.EditorUtility.SetDirty(car);

            // Télécommande, posée sur le bord avant de la table, à droite
            var remoteGo = new GameObject("Telecommande");
            remoteGo.transform.SetParent(transform, false);
            remoteGo.transform.localPosition = new Vector3(1.05f, tableHeight + 0.02f, -0.88f);
            remoteGo.transform.localRotation = Quaternion.identity;
            UnityEditor.Undo.RegisterCreatedObjectUndo(remoteGo, "Telecommande");
            var remoteCollider = remoteGo.AddComponent<BoxCollider>();
            remoteCollider.size = new Vector3(0.06f, 0.04f, 0.13f);
            NoCollider(Box("Boitier", remoteGo.transform, Vector3.zero, new Vector3(0.05f, 0.03f, 0.12f), remoteMat));
            NoCollider(Box("Gachette", remoteGo.transform, new Vector3(0f, -0.02f, 0.035f), new Vector3(0.016f, 0.014f, 0.022f), triggerMat));
            var antenna = Primitive(PrimitiveType.Cylinder, "Antenne", remoteGo.transform, legMat);
            NoCollider(antenna);
            antenna.transform.localPosition = new Vector3(0.017f, 0.04f, 0.045f);
            antenna.transform.localScale = new Vector3(0.004f, 0.03f, 0.004f);
            var led = Box("Voyant", remoteGo.transform, new Vector3(0f, 0.016f, 0.02f), new Vector3(0.016f, 0.004f, 0.016f), ledMat);
            NoCollider(led);
            var rb = remoteGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var grab = remoteGo.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = false;
            grab.farAttachMode = UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Near;
            var remote = remoteGo.AddComponent<RCRemote>();
            if (remoteSettings != null) UnityEditor.EditorJsonUtility.FromJsonOverwrite(remoteSettings, remote);
            remote.Configure(car, led.GetComponent<MeshRenderer>());
            UnityEditor.EditorUtility.SetDirty(remote);

            // Tableau, au fond de la table, lisible depuis l'avant
            var board = Group("Tableau", transform, new Vector3(0f, tableHeight + 0.75f, 0.85f), Quaternion.identity);
            NoCollider(Box("Fond", board, new Vector3(0f, 0f, 0.01f), new Vector3(1.1f, 0.42f, 0.01f), boardMat));
            var legBoard = Primitive(PrimitiveType.Cylinder, "Mat", board, legMat);
            NoCollider(legBoard);
            legBoard.transform.localPosition = new Vector3(0f, -0.48f, 0.02f);
            legBoard.transform.localScale = new Vector3(0.03f, 0.27f, 0.03f);
            var text = Label("Texte", board, new Vector3(0f, 0f, 0f), new Vector2(1.02f, 0.38f));

            // Activité : à la racine de la scène, comme les autres. Si elle existe déjà, elle est reprise
            // (le robot garde son lien) ; sinon elle est créée.
            var activity = FindAnyObjectByType<RCCarActivity>(FindObjectsInactive.Include);
            if (activity == null)
            {
                var activityGo = new GameObject("Activite_VoitureRC");
                UnityEditor.Undo.RegisterCreatedObjectUndo(activityGo, "Activite_VoitureRC");
                activity = activityGo.AddComponent<RCCarActivity>();
            }
            else
            {
                // Ancienne version : l'activité était dans le circuit. On la sort à la racine sans la recréer.
                if (activity.transform.IsChildOf(transform))
                    UnityEditor.Undo.SetTransformParent(activity.transform, null, "Activite_VoitureRC à la racine");
                UnityEditor.Undo.RecordObject(activity, "Activite_VoitureRC");
            }
            activity.Configure(this, car, remote, text);
            var so = new UnityEditor.SerializedObject(activity);
            so.FindProperty("activityId").stringValue = "rc_car";
            so.FindProperty("displayName").stringValue = "Voiture RC";
            so.FindProperty("tutorialText").stringValue =
                "Prenez la télécommande posée sur la table avec le grip. Appuyez sur la gâchette pour accélérer, relâchez-la pour freiner. " +
                "Trop vite dans un virage, la voiture sort de la piste ; trop lentement dans le looping, elle tombe. Faites 3 tours !";
            so.ApplyModifiedPropertiesWithoutUndo();
            activity.RefreshPanel();
            UnityEditor.EditorUtility.SetDirty(activity);

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[Circuit RC] Circuit créé. Enregistrer la scène (Ctrl+S).", this);
        }

        private static Material LoadOrCreate(string name, Color color, float metallic, float smoothness, bool unlit)
        {
            const string folder = "Assets/_Project/Activities/RCCar/Materials";
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                material = new Material(shader);
                UnityEditor.AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            UnityEditor.EditorUtility.SetDirty(material);
            UnityEditor.AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            UnityEditor.AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private static Transform Group(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, name);
            return go.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = Primitive(PrimitiveType.Cube, name, parent, material);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            return go;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject NoCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            return go;
        }

        private static TMP_Text Label(string name, Transform parent, Vector3 position, Vector2 box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = "";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.1f;
            tmp.fontSizeMax = 0.6f;
            tmp.rectTransform.sizeDelta = box;
            return tmp;
        }
#endif
    }
}
