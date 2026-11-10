using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JourneyThroughXR.Activities
{
    public enum DrawingTool
    {
        Free,
        Line,
        Circle,
        Square,
        Triangle,
        Eraser
    }

    public enum BoardButtonKind
    {
        Tool,
        Color,
        Clear,
        Undo,
        Redo,
        Width
    }

    [Serializable]
    public class BoardButton
    {
        public BoardButtonKind kind;
        public DrawingTool tool;
        public Color color = Color.black;
        public float width = 0.008f;
        public Transform area;
    }

    public class DrawingBoard : MonoBehaviour
    {
        // Réglages
        [Header("Écran")]
        [Tooltip("Taille de l'écran en mètres (largeur, hauteur), centré sur l'objet et tourné vers son axe Z.")]
        [SerializeField] private Vector2 size = new Vector2(1.9f, 1.07f);

        [Tooltip("Largeur de la barre d'outils, à gauche de l'écran.")]
        [SerializeField] private float toolbarWidth = 0.2f;

        [Tooltip("Distance à laquelle la pointe du stylet touche l'écran.")]
        [SerializeField] private float touchDistance = 0.04f;

        [Tooltip("Profondeur derrière l'écran où le stylet continue de dessiner.")]
        [SerializeField] private float behindTolerance = 0.25f;

        [Header("Trait")]
        [Tooltip("Épaisseur au lancement, en mètres.")]
        [SerializeField] private float lineWidth = 0.008f;

        [Tooltip("La gomme est plus large que le trait, de ce facteur.")]
        [SerializeField] private float eraserFactor = 3f;

        [SerializeField] private Material lineMaterial;
        [SerializeField] private Material surfaceMaterial;

        [Header("Boutons")]
        [SerializeField] private List<BoardButton> buttons = new List<BoardButton>();
        [SerializeField] private float selectedScale = 1.2f;

        [Header("Événements")]
        public UnityEvent onStroke = new UnityEvent();
        public UnityEvent onCleared = new UnityEvent();

        // État
        public DrawingTool Tool { get; private set; } = DrawingTool.Free;
        public Color CurrentColor { get; private set; } = new Color(0.1f, 0.3f, 0.9f);
        public float CurrentWidth { get; private set; }
        public int StrokeCount { get; private set; }
        public Material LineMaterial => lineMaterial;

        private class BoardAction
        {
            public bool isClear;
            public List<LineRenderer> lines = new List<LineRenderer>();
        }

        private readonly List<LineRenderer> allLines = new List<LineRenderer>();
        private readonly Stack<BoardAction> undoStack = new Stack<BoardAction>();
        private readonly Stack<BoardAction> redoStack = new Stack<BoardAction>();
        private readonly Dictionary<Transform, Vector3> baseScales = new Dictionary<Transform, Vector3>();
        private Transform strokesRoot;
        private LineRenderer current;
        private Vector3 shapeStart;
        private int nextOrder = 10;

        // Cycle de vie
        private void Awake()
        {
            ApplyColors();
            CurrentWidth = lineWidth;
            strokesRoot = new GameObject("Traits").transform;
            strokesRoot.SetParent(transform, false);

            foreach (var button in buttons)
                if (button.area != null) baseScales[button.area] = button.area.localScale;

            foreach (var button in buttons)
            {
                if (button.kind != BoardButtonKind.Color) continue;
                CurrentColor = button.color;
                break;
            }
            RefreshSelection();
        }

        private void OnValidate() => ApplyColors();

        // Couleurs
        private static readonly Color FrameColor = new Color(0.05f, 0.05f, 0.06f);
        private static readonly Color ToolbarColor = new Color(0.88f, 0.9f, 0.93f);
        private static readonly Color ClearColor = new Color(0.85f, 0.2f, 0.2f);
        private static readonly Color IconColor = new Color(0.15f, 0.15f, 0.2f);

        private void ApplyColors()
        {
            Tint(transform.Find("Cadre"), FrameColor);
            Tint(transform.Find("Ecran"), Color.white);
            Tint(transform.Find("Barre_Outils"), ToolbarColor);

            foreach (var button in buttons)
            {
                if (button.area == null) continue;
                Color color = button.kind == BoardButtonKind.Color ? button.color
                    : button.kind == BoardButtonKind.Clear ? ClearColor : Color.white;
                Tint(button.area, color);
            }
        }

        private static void Tint(Transform part, Color color)
        {
            if (part == null) return;
            var meshRenderer = part.GetComponent<Renderer>();
            if (meshRenderer == null) return;
            var block = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            meshRenderer.SetPropertyBlock(block);
        }

        // Contact
        public bool TryGetContact(Vector3 worldPoint, out Vector3 local)
        {
            local = transform.InverseTransformPoint(worldPoint);
            return local.z <= touchDistance && local.z >= -behindTolerance
                && Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f;
        }

        public void BeginTouch(Vector3 local)
        {
            BoardButton button = ButtonAt(local);
            if (button != null)
            {
                Press(button);
                return;
            }
            if (local.x < DrawMinX) return;

            bool eraser = Tool == DrawingTool.Eraser;
            Color color = eraser ? Color.white : CurrentColor;
            float width = eraser ? CurrentWidth * eraserFactor : CurrentWidth;

            current = NewLine(color, width, strokesRoot, "Trait", nextOrder++);
            current.loop = Tool == DrawingTool.Circle || Tool == DrawingTool.Square || Tool == DrawingTool.Triangle;
            shapeStart = OnSurface(local);
            if (Tool == DrawingTool.Free || eraser) AddPoint(shapeStart);
        }

        public void MoveTouch(Vector3 local)
        {
            if (current == null) return;
            Vector3 point = OnSurface(local);

            if (Tool == DrawingTool.Free || Tool == DrawingTool.Eraser)
            {
                Vector3 last = current.GetPosition(current.positionCount - 1);
                if ((point - last).sqrMagnitude > 0.004f * 0.004f) AddPoint(point);
            }
            else
            {
                SetShape(Tool, shapeStart, point, current);
            }
        }

        public void EndTouch()
        {
            if (current == null) return;
            if (current.positionCount < 2)
            {
                Destroy(current.gameObject);
            }
            else
            {
                allLines.Add(current);
                var action = new BoardAction();
                action.lines.Add(current);
                Record(action);
                StrokeCount++;
                onStroke.Invoke();
            }
            current = null;
        }

        // Pointage à distance
        public bool RaycastBoard(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 worldHit, out BoardButton button)
        {
            worldHit = Vector3.zero;
            button = null;

            Vector3 localOrigin = transform.InverseTransformPoint(origin);
            Vector3 localDirection = transform.InverseTransformDirection(direction);
            if (localOrigin.z <= 0f || localDirection.z >= -0.0001f) return false;

            float distance = (0.009f - localOrigin.z) / localDirection.z;
            if (distance < 0f || distance > maxDistance) return false;

            Vector3 hit = localOrigin + localDirection * distance;
            if (Mathf.Abs(hit.x) > size.x * 0.5f || Mathf.Abs(hit.y) > size.y * 0.5f) return false;

            worldHit = transform.TransformPoint(hit);
            button = ButtonAt(hit);
            return true;
        }

        public void PressButton(BoardButton button)
        {
            if (button != null) Press(button);
        }

        // Commandes
        public void Clear()
        {
            current = null;
            var action = new BoardAction { isClear = true };
            foreach (var line in allLines)
            {
                if (line == null || !line.gameObject.activeSelf) continue;
                line.gameObject.SetActive(false);
                action.lines.Add(line);
            }
            if (action.lines.Count > 0) Record(action);
            onCleared.Invoke();
        }

        public void ResetAll()
        {
            current = null;
            foreach (var line in allLines)
                if (line != null) Destroy(line.gameObject);
            allLines.Clear();
            undoStack.Clear();
            redoStack.Clear();
            StrokeCount = 0;
            onCleared.Invoke();
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            BoardAction action = undoStack.Pop();
            SetActive(action.lines, action.isClear);
            redoStack.Push(action);
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;
            BoardAction action = redoStack.Pop();
            SetActive(action.lines, !action.isClear);
            undoStack.Push(action);
        }

        public void SelectTool(DrawingTool tool)
        {
            Tool = tool;
            RefreshSelection();
        }

        public void SelectColor(Color color)
        {
            CurrentColor = color;
            if (Tool == DrawingTool.Eraser) Tool = DrawingTool.Free;
            RefreshSelection();
        }

        public void SelectWidth(float width)
        {
            CurrentWidth = width;
            RefreshSelection();
        }

        // Historique
        private void Record(BoardAction action)
        {
            undoStack.Push(action);
            foreach (var undone in redoStack)
            {
                if (undone.isClear) continue;
                foreach (var line in undone.lines)
                {
                    if (line == null) continue;
                    allLines.Remove(line);
                    Destroy(line.gameObject);
                }
            }
            redoStack.Clear();
        }

        private static void SetActive(List<LineRenderer> lines, bool active)
        {
            foreach (var line in lines)
                if (line != null) line.gameObject.SetActive(active);
        }

        // Boutons
        private BoardButton ButtonAt(Vector3 local)
        {
            foreach (var button in buttons)
            {
                if (button.area == null) continue;
                Vector3 center = transform.InverseTransformPoint(button.area.position);
                Vector3 scale = baseScales.TryGetValue(button.area, out Vector3 stored) ? stored : button.area.localScale;
                if (Mathf.Abs(local.x - center.x) <= scale.x * 0.5f && Mathf.Abs(local.y - center.y) <= scale.y * 0.5f)
                    return button;
            }
            return null;
        }

        private void Press(BoardButton button)
        {
            switch (button.kind)
            {
                case BoardButtonKind.Tool: SelectTool(button.tool); break;
                case BoardButtonKind.Color: SelectColor(button.color); break;
                case BoardButtonKind.Width: SelectWidth(button.width); break;
                case BoardButtonKind.Clear: Clear(); break;
                case BoardButtonKind.Undo: Undo(); break;
                case BoardButtonKind.Redo: Redo(); break;
            }
        }

        private void RefreshSelection()
        {
            foreach (var button in buttons)
            {
                if (button.area == null || !baseScales.TryGetValue(button.area, out Vector3 scale)) continue;
                bool selected = (button.kind == BoardButtonKind.Tool && button.tool == Tool)
                    || (button.kind == BoardButtonKind.Color && button.color == CurrentColor && Tool != DrawingTool.Eraser)
                    || (button.kind == BoardButtonKind.Width && Mathf.Approximately(button.width, CurrentWidth));
                button.area.localScale = selected ? new Vector3(scale.x * selectedScale, scale.y * selectedScale, scale.z) : scale;
            }
        }

        // Tracé
        private float DrawMinX => -size.x * 0.5f + toolbarWidth;

        private Vector3 OnSurface(Vector3 local)
        {
            float x = Mathf.Clamp(local.x, DrawMinX, size.x * 0.5f);
            float y = Mathf.Clamp(local.y, -size.y * 0.5f, size.y * 0.5f);
            return new Vector3(x, y, 0.006f);
        }

        private void AddPoint(Vector3 point)
        {
            current.positionCount++;
            current.SetPosition(current.positionCount - 1, point);
        }

        private static void SetShape(DrawingTool tool, Vector3 start, Vector3 end, LineRenderer line)
        {
            var points = new List<Vector3>();
            float z = start.z;

            switch (tool)
            {
                case DrawingTool.Line:
                    points.Add(start);
                    points.Add(end);
                    break;
                case DrawingTool.Circle:
                    float radius = Vector2.Distance(start, end);
                    for (int i = 0; i < 48; i++)
                    {
                        float angle = i / 48f * Mathf.PI * 2f;
                        points.Add(new Vector3(start.x + Mathf.Cos(angle) * radius, start.y + Mathf.Sin(angle) * radius, z));
                    }
                    break;
                case DrawingTool.Square:
                    points.Add(new Vector3(start.x, start.y, z));
                    points.Add(new Vector3(end.x, start.y, z));
                    points.Add(new Vector3(end.x, end.y, z));
                    points.Add(new Vector3(start.x, end.y, z));
                    break;
                case DrawingTool.Triangle:
                    points.Add(new Vector3(start.x, start.y, z));
                    points.Add(new Vector3(end.x, start.y, z));
                    points.Add(new Vector3((start.x + end.x) * 0.5f, end.y, z));
                    break;
            }

            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
        }

        private LineRenderer NewLine(Color color, float width, Transform parent, string objectName, int order)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.sharedMaterial = lineMaterial;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            line.sortingOrder = order;
            return line;
        }

        // Outils de mise en place
        [ContextMenu("Créer l'écran et le stylet")]
        private void CreateBoard()
        {
#if UNITY_EDITOR
            if (transform.Find("Ecran") != null)
            {
                Debug.LogWarning("[Dessin] L'écran existe déjà. Supprime ses éléments (sauf le stylet et son support) pour le recréer.", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Créer l'écran");
            lineMaterial = CreateMaterial("Dessin_Trait", "Sprites/Default");
            surfaceMaterial = CreateMaterial("Dessin_Surface", "Sprites/Default");
            buttons.Clear();

            float left = -size.x * 0.5f;
            float barX = left + toolbarWidth * 0.5f;
            float colA = barX - 0.045f;
            float colB = barX + 0.045f;

            AddQuad("Cadre", new Vector3(0f, 0f, -0.02f), new Vector2(size.x + 0.08f, size.y + 0.08f), FrameColor, 0);
            AddQuad("Ecran", new Vector3(0f, 0f, 0f), size, Color.white, 1);
            AddQuad("Barre_Outils", new Vector3(barX, 0f, 0.004f), new Vector2(toolbarWidth, size.y), ToolbarColor, 2);

            DrawingTool[] tools = { DrawingTool.Free, DrawingTool.Line, DrawingTool.Circle, DrawingTool.Square, DrawingTool.Triangle, DrawingTool.Eraser };
            for (int i = 0; i < tools.Length; i++)
            {
                var position = new Vector3(i % 2 == 0 ? colA : colB, 0.46f - (i / 2) * 0.09f, 0.009f);
                var area = AddQuad("Outil_" + tools[i], position, new Vector2(0.08f, 0.08f), Color.white, 3);
                AddToolIcon(area, tools[i]);
                buttons.Add(new BoardButton { kind = BoardButtonKind.Tool, tool = tools[i], area = area });
            }

            var undo = AddQuad("Annuler", new Vector3(colB, 0.18f, 0.009f), new Vector2(0.08f, 0.06f), Color.white, 3);
            AddArrowIcon(undo, 1f);
            buttons.Add(new BoardButton { kind = BoardButtonKind.Undo, area = undo });
            var redo = AddQuad("Retablir", new Vector3(colA, 0.18f, 0.009f), new Vector2(0.08f, 0.06f), Color.white, 3);
            AddArrowIcon(redo, -1f);
            buttons.Add(new BoardButton { kind = BoardButtonKind.Redo, area = redo });

            float[] widths = { 0.004f, 0.008f, 0.015f, 0.025f };
            for (int i = 0; i < widths.Length; i++)
            {
                var position = new Vector3(i % 2 == 0 ? colA : colB, 0.09f - (i / 2) * 0.07f, 0.009f);
                var area = AddQuad("Epaisseur_" + (i + 1), position, new Vector2(0.08f, 0.055f), Color.white, 3);
                AddLine(area, "Icone_Epaisseur_" + (i + 1), IconColor, widths[i], new Vector2(-0.025f, 0f), new Vector2(0.025f, 0f));
                buttons.Add(new BoardButton { kind = BoardButtonKind.Width, width = widths[i], area = area });
            }

            Color[] colors =
            {
                new Color(0.1f, 0.3f, 0.9f), new Color(0.08f, 0.08f, 0.1f),
                new Color(0.9f, 0.15f, 0.2f), new Color(0.15f, 0.7f, 0.3f),
                new Color(1f, 0.6f, 0.1f), new Color(0.95f, 0.15f, 0.45f)
            };
            for (int i = 0; i < colors.Length; i++)
            {
                var position = new Vector3(i % 2 == 0 ? colA : colB, -0.10f - (i / 2) * 0.08f, 0.009f);
                var area = AddQuad("Couleur_" + (i + 1), position, new Vector2(0.06f, 0.06f), colors[i], 3);
                buttons.Add(new BoardButton { kind = BoardButtonKind.Color, color = colors[i], area = area });
            }

            var clear = AddQuad("Effacer", new Vector3(barX, -0.42f, 0.009f), new Vector2(0.15f, 0.08f), ClearColor, 3);
            AddLine(clear, "Effacer_Croix_1", Color.white, 0.008f, new Vector2(-0.02f, -0.02f), new Vector2(0.02f, 0.02f));
            AddLine(clear, "Effacer_Croix_2", Color.white, 0.008f, new Vector2(-0.02f, 0.02f), new Vector2(0.02f, -0.02f));
            buttons.Add(new BoardButton { kind = BoardButtonKind.Clear, area = clear });

            if (transform.Find("Stylet") == null) CreatePen();
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        private Transform AddQuad(string objectName, Vector3 position, Vector2 dimensions, Color color, int order)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = objectName;
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = position;
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(dimensions.x, dimensions.y, 1f);

            var meshRenderer = quad.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = surfaceMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.sortingOrder = order;
            Tint(quad.transform, color);

            UnityEditor.Undo.RegisterCreatedObjectUndo(quad, "Créer l'écran");
            return quad.transform;
        }

        private void AddToolIcon(Transform area, DrawingTool tool)
        {
            Vector3 c = area.localPosition;
            float r = 0.025f;
            float z = 0.015f;

            if (tool == DrawingTool.Eraser)
            {
                var body = NewLine(IconColor, 0.004f, transform, "Icone_Eraser", 4);
                body.loop = true;
                body.positionCount = 4;
                body.SetPositions(new[]
                {
                    new Vector3(c.x - r, c.y - r * 0.2f, z), new Vector3(c.x - r * 0.2f, c.y - r, z),
                    new Vector3(c.x + r, c.y + r * 0.2f, z), new Vector3(c.x + r * 0.2f, c.y + r, z)
                });
                AddLine(area, "Icone_Eraser_Bande", IconColor, 0.004f, new Vector2(-r * 0.6f, r * 0.2f), new Vector2(r * 0.2f, -r * 0.6f));
                return;
            }

            var line = NewLine(IconColor, 0.005f, transform, "Icone_" + tool, 4);
            line.loop = tool == DrawingTool.Circle || tool == DrawingTool.Square || tool == DrawingTool.Triangle;

            if (tool == DrawingTool.Free)
            {
                var points = new Vector3[16];
                for (int i = 0; i < points.Length; i++)
                {
                    float t = i / (points.Length - 1f);
                    points[i] = new Vector3(c.x - r + t * 2f * r, c.y + Mathf.Sin(t * Mathf.PI * 2f) * r * 0.6f, z);
                }
                line.positionCount = points.Length;
                line.SetPositions(points);
            }
            else if (tool == DrawingTool.Circle)
            {
                SetShape(tool, new Vector3(c.x, c.y, z), new Vector3(c.x + r, c.y, z), line);
            }
            else
            {
                SetShape(tool, new Vector3(c.x - r, c.y - r, z), new Vector3(c.x + r, c.y + r, z), line);
            }
        }

        private void AddArrowIcon(Transform area, float direction)
        {
            float r = 0.025f * direction;
            AddLine(area, "Icone_Fleche", IconColor, 0.005f, new Vector2(-r, 0f), new Vector2(r, 0f));
            AddLine(area, "Icone_Fleche_Haut", IconColor, 0.005f, new Vector2(r, 0f), new Vector2(r * 0.4f, 0.015f));
            AddLine(area, "Icone_Fleche_Bas", IconColor, 0.005f, new Vector2(r, 0f), new Vector2(r * 0.4f, -0.015f));
        }

        private void AddLine(Transform area, string objectName, Color color, float width, Vector2 from, Vector2 to)
        {
            Vector3 c = area.localPosition;
            var line = NewLine(color, width, transform, objectName, 4);
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(c.x + from.x, c.y + from.y, 0.015f));
            line.SetPosition(1, new Vector3(c.x + to.x, c.y + to.y, 0.015f));
        }

        private void CreatePen()
        {
            var support = GameObject.CreatePrimitive(PrimitiveType.Cube);
            support.name = "Support_Stylet";
            DestroyImmediate(support.GetComponent<Collider>());
            support.transform.SetParent(transform, false);
            support.transform.localPosition = new Vector3(size.x * 0.5f - 0.2f, -size.y * 0.5f - 0.07f, 0.05f);
            support.transform.localScale = new Vector3(0.25f, 0.02f, 0.08f);
            UnityEditor.Undo.RegisterCreatedObjectUndo(support, "Créer l'écran");

            var home = new GameObject("Place_Stylet").transform;
            home.SetParent(transform, false);
            home.localPosition = new Vector3(size.x * 0.5f - 0.2f, -size.y * 0.5f - 0.045f, 0.05f);
            home.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var pen = new GameObject("Stylet");
            pen.transform.SetParent(transform, false);
            pen.transform.SetPositionAndRotation(home.position, home.rotation);
            UnityEditor.Undo.RegisterCreatedObjectUndo(pen, "Créer l'écran");

            var body = pen.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var capsule = pen.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = 0.015f;
            capsule.height = 0.17f;

            var grip = new GameObject("Prise").transform;
            grip.SetParent(pen.transform, false);
            grip.localPosition = new Vector3(0f, 0f, -0.03f);

            var tip = new GameObject("Pointe").transform;
            tip.SetParent(pen.transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.085f);

            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.name = "Corps_Stylet";
            DestroyImmediate(stick.GetComponent<Collider>());
            stick.transform.SetParent(pen.transform, false);
            stick.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            stick.transform.localScale = new Vector3(0.018f, 0.075f, 0.018f);

            var colored = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            colored.name = "Pointe_Couleur";
            DestroyImmediate(colored.GetComponent<Collider>());
            colored.transform.SetParent(pen.transform, false);
            colored.transform.localPosition = new Vector3(0f, 0f, 0.078f);
            colored.transform.localScale = Vector3.one * 0.016f;

            var grab = pen.AddComponent<XRGrabInteractable>();
            grab.attachTransform = grip;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;

            var drawingPen = pen.AddComponent<DrawingPen>();
            var serialized = new UnityEditor.SerializedObject(drawingPen);
            serialized.FindProperty("board").objectReferenceValue = this;
            serialized.FindProperty("tip").objectReferenceValue = tip;
            serialized.FindProperty("tipRenderer").objectReferenceValue = colored.GetComponent<Renderer>();
            serialized.FindProperty("home").objectReferenceValue = home;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material CreateMaterial(string materialName, string shaderName)
        {
            const string folder = "Assets/_Project/Activities/Drawing/Materials";
            EnsureFolder(folder);
            string path = folder + "/" + materialName + ".mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find(shaderName));
            UnityEditor.AssetDatabase.CreateAsset(material, path);
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
#endif

        // Aide visuelle
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.01f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.3f);
        }
    }
}
