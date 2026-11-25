using System.Collections;
using System.Collections.Generic;
using JourneyThroughXR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// « Ton premier programme » sur l'écran du poste : choisir un langage (Python, JavaScript, C),
    /// remettre 4 blocs de code dans l'ordre, puis Compiler. Réussi : la console affiche le résultat et l'activité est terminée.
    /// Raté : message d'erreur avec un indice, puis Effacer pour réessayer.
    /// Après 2 essais ratés, le prochain bon bloc s'allume en jaune : tout le monde finit par réussir.
    /// L'interface est créée au lancement, sur la surface de l'écran.
    /// </summary>
    public class ComputerProgram : MonoBehaviour
    {
        // Réglages
        [Tooltip("Surface de l'écran (Surface_Ecran). Son axe bleu pointe vers le visiteur.")]
        [SerializeField] private Transform screenSurface;

        [Tooltip("Activité terminée quand le programme fonctionne.")]
        [SerializeField] private Activity activity;

        [Tooltip("Taille utile de l'écran en mètres.")]
        [SerializeField] private Vector2 screenSize = new Vector2(0.585f, 0.315f);

        [Tooltip("Délai en secondes entre deux lignes affichées dans la console.")]
        [SerializeField] private float consoleLineDelay = 0.4f;

        [Header("Événements")]
        public UnityEvent onSuccess = new UnityEvent();
        public UnityEvent onError = new UnityEvent();

        // Programme à reconstituer, dans le bon ordre, pour chaque langage.
        private const int BlockNom = 0, BlockFor = 1, BlockBonjour = 2, BlockFin = 3;

        private const string K = "<color=#FF9E5E>", F = "<color=#7EC8FF>", S = "<color=#9BE37B>", E = "</color>";
        private const string Lt = "<noparse><</noparse>";

        private enum LanguageKind { Python, JavaScript, C }

        private sealed class Language
        {
            public LanguageKind Kind;
            public string Name;
            public string Run;
            public string[] Blocks;
        }

        private static readonly Language[] Languages =
        {
            new Language
            {
                Kind = LanguageKind.Python,
                Name = "Python",
                Run = "python bonjour.py",
                Blocks = new[]
                {
                    $"nom = {S}\"Epitech\"{E}",
                    $"{K}for{E} i {K}in{E} {F}range{E}(3):",
                    $"    {F}print{E}({S}\"Bonjour\"{E}, nom)",
                    $"{F}print{E}({S}\"Programme terminé !\"{E})",
                },
            },
            new Language
            {
                Kind = LanguageKind.JavaScript,
                Name = "JavaScript",
                Run = "node bonjour.js",
                Blocks = new[]
                {
                    $"{K}let{E} nom = {S}\"Epitech\"{E};",
                    $"{K}for{E} ({K}let{E} i = 0; i {Lt} 3; i++)",
                    $"    console.{F}log{E}({S}\"Bonjour \"{E} + nom);",
                    $"console.{F}log{E}({S}\"Programme terminé !\"{E});",
                },
            },
            new Language
            {
                Kind = LanguageKind.C,
                Name = "C",
                Run = "gcc bonjour.c && ./a.out",
                Blocks = new[]
                {
                    $"{K}char{E} *nom = {S}\"Epitech\"{E};",
                    $"{K}for{E} ({K}int{E} i = 0; i {Lt} 3; i++)",
                    $"    {F}printf{E}({S}\"Bonjour %s\\n\"{E}, nom);",
                    $"{F}printf{E}({S}\"Programme terminé !\\n\"{E});",
                },
            },
        };

        // Ordre d'affichage des blocs à droite (mélangé, jamais le bon ordre).
        private static readonly int[] ShuffledOrder = { BlockBonjour, BlockNom, BlockFin, BlockFor };

        // Couleurs
        private static readonly Color Background = new Color32(11, 16, 32, 255);
        private static readonly Color PanelColor = new Color32(20, 27, 46, 255);
        private static readonly Color BlockColor = new Color32(42, 53, 86, 255);
        private static readonly Color LineEmpty = new Color32(26, 34, 58, 255);
        private static readonly Color Help = new Color32(170, 135, 20, 255);
        private static readonly Color LineError = new Color32(110, 30, 40, 255);
        private static readonly Color LineOk = new Color32(28, 70, 45, 255);
        private static readonly Color Muted = new Color32(150, 165, 200, 255);

        private const float PixelsPerMeter = 2000f;

        // État
        private readonly List<int> program = new List<int>();
        private readonly Button[] blockButtons = new Button[4];
        private readonly TMP_Text[] blockTexts = new TMP_Text[4];
        private readonly Image[] lineBackgrounds = new Image[4];
        private readonly TMP_Text[] lineTexts = new TMP_Text[4];
        private RectTransform choicePage;
        private RectTransform gamePage;
        private TMP_Text titleText;
        private Button compileButton;
        private Button clearButton;
        private TMP_Text consoleText;
        private Coroutine running;
        private Language language;
        private bool solved;
        private bool waitingForClear;
        private int failures;

        public bool Solved => solved;

        // Cycle de vie
        private void Start()
        {
            BuildInterface();
            ResetProgram();
        }

        private void OnEnable()
        {
            if (activity != null) activity.onReset.AddListener(ResetProgram);
        }

        private void OnDisable()
        {
            if (activity != null) activity.onReset.RemoveListener(ResetProgram);
        }

        // Commandes
        /// <summary>Retour au choix du langage, programme vide.</summary>
        public void ResetProgram()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            solved = false;
            failures = 0;
            language = null;
            ClearProgram();
            if (choicePage != null) choicePage.gameObject.SetActive(true);
            if (gamePage != null) gamePage.gameObject.SetActive(false);
        }

        private void ChooseLanguage(int index)
        {
            language = Languages[index];
            if (activity != null) activity.StartActivity();
            titleText.text = $"Ton premier programme <color=#96A5C8>· {language.Name}</color>";
            for (int i = 0; i < 4; i++) blockTexts[i].text = language.Blocks[ShuffledOrder[i]];
            choicePage.gameObject.SetActive(false);
            gamePage.gameObject.SetActive(true);
            ClearProgram();
        }

        private void AddBlock(int block)
        {
            if (solved || waitingForClear || program.Contains(block) || program.Count >= 4) return;
            program.Add(block);
            Refresh();
        }

        private void ClearProgram()
        {
            if (solved) return;
            program.Clear();
            waitingForClear = false;
            if (consoleText != null)
                consoleText.text = failures >= 2
                    ? "<color=#FFD27A>Coup de pouce : le bloc à toucher maintenant est en jaune.</color>"
                    : "<color=#96A5C8>> En attente du programme...</color>";
            Refresh();
        }

        // Compilation et exécution simulées, ligne par ligne, selon le langage.
        private sealed class Result
        {
            public bool Ok;
            public int Line = -1;              // ligne fautive (0 à 3), -1 si aucune
            public string Error;               // message d'erreur, null si le programme s'exécute
            public string Hint;
            public readonly List<string> Output = new List<string>();
        }

        private const string Hello = "Bonjour Epitech";
        private const string Done = "Programme terminé !";
        private static readonly string[] Expected = { Hello, Hello, Hello, Done };

        private const string HintVariable = "On crée la variable <b>nom</b> avant de s'en servir.";
        private const string HintIndent = "La ligne décalée vers la droite doit venir juste sous le <b>for</b> : c'est elle qu'il répète.";
        private const string HintForLast = "Le <b>for</b> doit être suivi de la ligne qu'il répète.";
        private const string HintForBody = "Le <b>for</b> répète la ligne juste en dessous de lui : ici, ce serait la création de <b>nom</b>.";

        private void Compile()
        {
            if (solved || waitingForClear || program.Count < 4 || running != null) return;
            var result = Evaluate(language, program);
            if (result.Ok) running = StartCoroutine(RunSuccess());
            else ShowFailure(result);
        }

        private static Result Evaluate(Language lang, List<int> code)
        {
            var r = new Result();
            int nomLine = code.IndexOf(BlockNom);

            // 1. Avant l'exécution : syntaxe (Python, JavaScript) ou compilation (C), de la première ligne à la dernière.
            for (int j = 0; j < code.Count; j++)
            {
                int block = code[j];
                bool afterFor = j > 0 && code[j - 1] == BlockFor;
                bool last = j == code.Count - 1;

                switch (lang.Kind)
                {
                    case LanguageKind.Python:
                        if (block == BlockBonjour && !afterFor)
                            return Fail(r, j, $"Ligne {j + 1} : IndentationError: unexpected indent", HintIndent);
                        if (afterFor && block != BlockBonjour)
                            return Fail(r, j, $"Ligne {j + 1} : IndentationError: expected an indented block after 'for' statement on line {j}", HintIndent);
                        if (block == BlockFor && last)
                            return Fail(r, j, $"Ligne {j + 1} : IndentationError: expected an indented block after 'for' statement on line {j + 1}", HintForLast);
                        break;

                    case LanguageKind.JavaScript:
                        if (afterFor && block == BlockNom)
                            return Fail(r, j, $"Ligne {j + 1} : SyntaxError: Lexical declaration cannot appear in a single-statement context", HintForBody);
                        if (block == BlockFor && last)
                            return Fail(r, j, $"Ligne {j + 1} : SyntaxError: Unexpected end of input", HintForLast);
                        break;

                    case LanguageKind.C:
                        if (afterFor && block == BlockNom)
                            return Fail(r, j, $"bonjour.c:{j + 1}: error: expected expression before 'char'", HintForBody);
                        if (block == BlockBonjour && nomLine > j)
                            return Fail(r, j, $"bonjour.c:{j + 1}: error: 'nom' undeclared (first use in this function)", HintVariable);
                        if (block == BlockFor && last)
                            return Fail(r, j, $"bonjour.c:{j + 1}: error: expected statement before '}}' token", HintForLast);
                        break;
                }
            }

            // 2. Exécution. Le for répète 3 fois la ligne juste en dessous.
            bool nomExists = false;
            for (int j = 0; j < code.Count; j++)
            {
                if (code[j] == BlockFor)
                {
                    for (int k = 0; k < 3; k++)
                        if (!Execute(lang, code[j + 1], j + 1, ref nomExists, r)) return r;
                    j++;
                    continue;
                }
                if (!Execute(lang, code[j], j, ref nomExists, r)) return r;
            }

            // 3. Le programme tourne : le résultat est-il le bon ?
            bool same = r.Output.Count == Expected.Length;
            for (int i = 0; same && i < Expected.Length; i++) same = r.Output[i] == Expected[i];
            if (same)
            {
                r.Ok = true;
                return r;
            }

            int helloCount = r.Output.FindAll(line => line == Hello).Count;
            int doneCount = r.Output.FindAll(line => line == Done).Count;
            if (doneCount > 1)
            {
                r.Line = code.IndexOf(BlockFin);
                r.Hint = "Le <b>for</b> répète la ligne juste en dessous de lui : ici, c'est « Programme terminé ! ». Place dessous celle qui dit Bonjour.";
            }
            else if (helloCount != 3)
            {
                r.Line = code.IndexOf(BlockBonjour);
                r.Hint = "Bonjour ne s'affiche qu'une fois : le <b>for</b> répète seulement la ligne placée juste en dessous de lui.";
            }
            else
            {
                r.Line = code.IndexOf(BlockFin);
                r.Hint = "« Programme terminé ! » s'affiche trop tôt : ce message doit venir en dernier.";
            }
            return r;
        }

        // Exécute une ligne. Renvoie false si le programme plante.
        private static bool Execute(Language lang, int block, int line, ref bool nomExists, Result r)
        {
            switch (block)
            {
                case BlockNom:
                    nomExists = true;
                    return true;
                case BlockBonjour:
                    if (!nomExists)
                    {
                        string error = lang.Kind == LanguageKind.JavaScript
                            ? "ReferenceError: Cannot access 'nom' before initialization"
                            : "NameError: name 'nom' is not defined";
                        Fail(r, line, $"Ligne {line + 1} : {error}", HintVariable);
                        return false;
                    }
                    r.Output.Add(Hello);
                    return true;
                case BlockFin:
                    r.Output.Add(Done);
                    return true;
                default:
                    return true;
            }
        }

        private static Result Fail(Result r, int line, string error, string hint)
        {
            r.Line = line;
            r.Error = error;
            r.Hint = hint;
            return r;
        }

        private void ShowFailure(Result result)
        {
            failures++;
            waitingForClear = true;
            Refresh();
            if (result.Line >= 0 && result.Line < 4) lineBackgrounds[result.Line].color = LineError;

            var text = new System.Text.StringBuilder();
            text.Append($"<color=#96A5C8>> {language.Run}</color>");
            foreach (var line in result.Output) text.Append('\n').Append(line);
            if (result.Error != null)
                text.Append($"\n<color=#FF6E6E>{result.Error}</color>");
            else
                text.Append("\n<color=#FFB35E>Pas d'erreur, mais ce n'est pas le résultat attendu.</color>");
            text.Append($"\n<color=#FFD27A>Indice : {result.Hint}</color>");
            text.Append(failures >= 2
                ? "\n<color=#96A5C8>Appuie sur Effacer : le bon bloc s'allumera en jaune à chaque étape.</color>"
                : "\n<color=#96A5C8>Appuie sur Effacer pour réessayer.</color>");
            consoleText.text = text.ToString();
            onError.Invoke();
        }

        private IEnumerator RunSuccess()
        {
            solved = true;
            Refresh();
            for (int i = 0; i < 4; i++) lineBackgrounds[i].color = LineOk;

            string output = $"<color=#96A5C8>> {language.Run}</color>";
            consoleText.text = output;
            foreach (var line in Expected)
            {
                yield return new WaitForSeconds(consoleLineDelay);
                output += "\n" + line;
                consoleText.text = output;
            }
            yield return new WaitForSeconds(consoleLineDelay);
            consoleText.text = output + "\n<color=#78E68C><b>Bravo, ton premier programme fonctionne !</b></color>";

            running = null;
            onSuccess.Invoke();
            if (activity != null) activity.CompleteActivity();
        }

        private void Refresh()
        {
            if (compileButton == null || language == null) return;
            for (int i = 0; i < 4; i++)
            {
                bool filled = i < program.Count;
                lineTexts[i].text = filled ? language.Blocks[program[i]] : "<color=#3C4766>...</color>";
                lineBackgrounds[i].color = LineEmpty;
            }

            // Coup de pouce après 2 essais ratés : le prochain bon bloc est en jaune.
            int next = program.Count;
            bool help = failures >= 2 && !solved && !waitingForClear && next < 4;
            for (int i = 0; i < 4; i++)
            {
                int block = ShuffledOrder[i];
                blockButtons[i].interactable = !solved && !waitingForClear && !program.Contains(block);
                SetButtonColor(blockButtons[i], help && block == next ? Help : BlockColor);
            }
            compileButton.interactable = !solved && !waitingForClear && program.Count == 4;
            clearButton.interactable = !solved && program.Count > 0;
        }

#if UNITY_EDITOR
        internal void ConfigureScreen(Transform surface) => screenSurface = surface;
#endif

        // Interface
        private void BuildInterface()
        {
            var surface = screenSurface != null ? screenSurface : transform;
            var size = screenSize * PixelsPerMeter;

            var canvasGo = new GameObject("Ecran_Programme", typeof(RectTransform));
            canvasGo.transform.SetParent(surface, false);
            // Le texte se lit en regardant l'axe bleu de face : on retourne le canvas vers le visiteur.
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            canvasGo.transform.localScale = Vector3.one / PixelsPerMeter;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
            var root = (RectTransform)canvasGo.transform;
            root.sizeDelta = size;

            var background = Panel(root, "Fond", 0, 0, size.x, size.y, Background);

            // Page 1 : choix du langage
            choicePage = Place(background, "Choix_Langage", 0, 0, size.x, size.y);
            Label(choicePage, "Titre", 24, 40, 1132, 60, "Ton premier programme", 44, Color.white, TextAlignmentOptions.Center, true);
            Label(choicePage, "Consigne", 24, 120, 1132, 40, "Choisis un langage de programmation :", 26, Muted, TextAlignmentOptions.Center, false);
            for (int i = 0; i < Languages.Length; i++)
            {
                int index = i;
                MakeButton(choicePage, $"Langage_{Languages[i].Name}", 90 + i * 340, 220, 320, 150, BlockColor,
                    $"<b>{Languages[i].Name}</b>", 40, TextAlignmentOptions.Center, () => ChooseLanguage(index));
            }
            Label(choicePage, "Aide", 24, 430, 1132, 40, "Vise un bouton avec la manette et appuie sur la gâchette.", 22, Muted, TextAlignmentOptions.Center, false);

            // Page 2 : le programme
            gamePage = Place(background, "Programme", 0, 0, size.x, size.y);
            titleText = Label(gamePage, "Titre", 24, 12, 860, 42, "Ton premier programme", 32, Color.white, TextAlignmentOptions.Left, true);
            MakeButton(gamePage, "Changer_Langage", 900, 12, 256, 40, BlockColor, "Changer de langage", 18,
                TextAlignmentOptions.Center, ResetProgram);
            Label(gamePage, "Consigne", 24, 58, 1132, 34,
                "Touche les blocs dans le bon ordre pour que l'ordinateur dise bonjour 3 fois, puis appuie sur Compiler.",
                21, Muted, TextAlignmentOptions.Left, false);

            // Blocs à gauche, programme à droite.
            var blocksPanel = Panel(gamePage, "Blocs", 24, 100, 492, 292, PanelColor);
            Label(blocksPanel, "Titre", 16, 6, 400, 30, "Blocs", 20, Muted, TextAlignmentOptions.Left, true);
            for (int i = 0; i < 4; i++)
            {
                int block = ShuffledOrder[i];
                blockButtons[i] = MakeButton(blocksPanel, $"Bloc_{i + 1}", 12, 42 + i * 62, 468, 56, BlockColor,
                    "", 20, TextAlignmentOptions.Left, () => AddBlock(block));
                blockTexts[i] = blockButtons[i].GetComponentInChildren<TMP_Text>();
            }

            var programPanel = Panel(gamePage, "Code", 536, 100, 620, 292, PanelColor);
            Label(programPanel, "Titre", 16, 6, 400, 30, "Programme", 20, Muted, TextAlignmentOptions.Left, true);
            for (int i = 0; i < 4; i++)
            {
                var line = Panel(programPanel, $"Ligne_{i + 1}", 12, 42 + i * 62, 596, 56, LineEmpty);
                lineBackgrounds[i] = line.GetComponent<Image>();
                Label(line, "Numero", 6, 0, 36, 56, (i + 1).ToString(), 22, Muted, TextAlignmentOptions.Center, false);
                lineTexts[i] = Label(line, "Code", 48, 0, 540, 56, "", 22, Color.white, TextAlignmentOptions.Left, false);
            }

            var console = Panel(gamePage, "Console", 24, 404, 820, 222, Color.black);
            Label(console, "Titre", 14, 4, 400, 24, "Console", 16, Muted, TextAlignmentOptions.Left, true);
            consoleText = Label(console, "Sortie", 14, 28, 792, 190, "", 18, Color.white, TextAlignmentOptions.TopLeft, false);

            clearButton = MakeButton(gamePage, "Effacer", 864, 404, 292, 100, new Color32(138, 45, 59, 255),
                "Effacer", 28, TextAlignmentOptions.Center, ClearProgram);
            compileButton = MakeButton(gamePage, "Compiler", 864, 524, 292, 102, new Color32(46, 125, 79, 255),
                "<b>Compiler</b>", 30, TextAlignmentOptions.Center, Compile);
        }

        private static void SetButtonColor(Button button, Color color)
        {
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
            colors.pressedColor = Color.Lerp(color, Color.white, 0.45f);
            colors.selectedColor = color;
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.3f);
            button.colors = colors;
        }

        private static RectTransform Place(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static RectTransform Panel(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rect = Place(parent, name, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static TMP_Text Label(RectTransform parent, string name, float x, float y, float w, float h,
            string content, float fontSize, Color color, TextAlignmentOptions alignment, bool bold)
        {
            var rect = Place(parent, name, x, y, w, h);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            if (bold) text.fontStyle = FontStyles.Bold;
            return text;
        }

        private static Button MakeButton(RectTransform parent, string name, float x, float y, float w, float h, Color color,
            string label, float fontSize, TextAlignmentOptions alignment, UnityAction onClick)
        {
            var rect = Place(parent, name, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            SetButtonColor(button, color);
            button.onClick.AddListener(onClick);
            Label(rect, "Texte", 16, 0, w - 32, h, label, fontSize, Color.white, alignment, false);
            return button;
        }
    }
}
