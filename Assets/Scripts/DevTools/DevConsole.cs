using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Cheat values the console sets and gameplay scripts read. Static, so they survive scene loads
// with nothing to re-apply, and every script reads the same numbers.
public static class DevCheats
{
    public static float Speed = 1f;
    public static float Jump = 1f;
    public static float Gravity = 1f;
    public static float Strength = 1f;
    public static bool NoClip;
    public static bool MaxStrength;

    public static bool AnyActive =>
        NoClip || MaxStrength || Speed != 1f || Jump != 1f || Gravity != 1f || Strength != 1f;

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetAll()
    {
        Speed = 1f;
        Jump = 1f;
        Gravity = 1f;
        Strength = 1f;
        NoClip = false;
        MaxStrength = false;
    }
}

// A Skyrim style console for debugging. Spawns itself in every scene, the key under Escape opens
// it, and the game freezes while it is open. Type help for the commands.
public class DevConsole : MonoBehaviour
{
    private struct Command
    {
        public string[] Names;
        public string Usage;
        public string Help;
        public Action<string[]> Run;
    }

    private struct Line
    {
        public string Text;
        public Color Color;
    }

    [Tooltip("Opens and closes the console. Backquote is read by position, so it is the key " +
             "under Escape on any keyboard layout.")]
    [SerializeField] private Key toggleKey = Key.Backquote;

    [Tooltip("How many lines of output are kept.")]
    [SerializeField] private int maxLines = 200;

    [Tooltip("Share of the screen height the console covers.")]
    [Range(0.2f, 1f)]
    [SerializeField] private float heightFraction = 0.4f;

    private static readonly Color TextColor = new Color(0.85f, 0.88f, 0.92f);
    private static readonly Color EchoColor = new Color(0.55f, 0.8f, 1f);
    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.4f);
    private static readonly Color BackgroundColor = new Color(0.03f, 0.04f, 0.06f, 0.92f);

    private const float LineHeight = 16f;
    private const float Padding = 8f;

    private static DevConsole instance;
    private static int closedFrame = -1;

    private readonly List<Command> commands = new List<Command>();
    private readonly List<Line> lines = new List<Line>();
    private readonly List<string> history = new List<string>();

    private string input = "";
    private int historyIndex;
    private float resumeTimeScale = 1f;
    private Keyboard textSource;

    private Texture2D pixel;
    private GUIStyle lineStyle;
    private GUIStyle badgeStyle;

    public static bool IsOpen { get; private set; }

    // Also true on the frame the console closed, so the Escape or Enter that closed it is not
    // read again by the pause menu or the player in the same frame.
    public static bool BlocksInput => IsOpen || closedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        IsOpen = false;
        closedFrame = -1;
    }

    // Spawns itself, so every scene has it without placing anything.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (instance != null) return;

        GameObject host = new GameObject("Dev Console (auto)");
        host.AddComponent<DevConsole>();
        DontDestroyOnLoad(host);
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        RegisterCommands();
        Print("Console ready. Type help for commands.", TextColor);
    }

    void OnDestroy()
    {
        if (instance != this) return;

        if (IsOpen) Close();

        SetTextSource(null);
        instance = null;

        if (pixel != null) Destroy(pixel);
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != textSource) SetTextSource(keyboard);

        if (keyboard == null) return;

        if (keyboard[toggleKey].wasPressedThisFrame)
        {
            if (IsOpen) Close();
            else Open();

            return;
        }

        if (!IsOpen) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Submit();
        if (keyboard.backspaceKey.wasPressedThisFrame && input.Length > 0) input = input.Substring(0, input.Length - 1);
        if (keyboard.upArrowKey.wasPressedThisFrame) StepHistory(-1);
        if (keyboard.downArrowKey.wasPressedThisFrame) StepHistory(1);
        if (keyboard.tabKey.wasPressedThisFrame) Complete();
    }

    private void Open()
    {
        // The pause menu owns time and the cursor while it is up, and a fade is mid scene load.
        if (PauseMenu.IsPaused || SceneTransition.IsTransitioning) return;

        IsOpen = true;
        input = "";
        historyIndex = history.Count;

        resumeTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Close()
    {
        IsOpen = false;
        closedFrame = Time.frameCount;
        Time.timeScale = resumeTimeScale;

        PlayerCamera look = FindAnyObjectByType<PlayerCamera>();

        if (look != null) look.RelockCursor();
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Typed characters arrive as text, so shift, layouts and accents all come out right.
    private void SetTextSource(Keyboard keyboard)
    {
        if (textSource != null) textSource.onTextInput -= OnTextInput;

        textSource = keyboard;

        if (textSource != null) textSource.onTextInput += OnTextInput;
    }

    private void OnTextInput(char character)
    {
        if (!IsOpen || char.IsControl(character)) return;

        // The toggle key's own character, typed on the press that opened the console.
        if (character == '`' || character == '~') return;

        input += character;
    }

    private void Submit()
    {
        string line = input.Trim();
        input = "";

        if (line.Length == 0) return;

        history.Add(line);
        historyIndex = history.Count;

        Print("> " + line, EchoColor);
        Execute(line);
    }

    // The same path the input line takes, so commands can also be run from code or a test.
    public static void Execute(string line)
    {
        if (instance == null || string.IsNullOrWhiteSpace(line)) return;

        string[] parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string name = parts[0].ToLowerInvariant();
        string[] args = new string[parts.Length - 1];

        Array.Copy(parts, 1, args, 0, args.Length);

        foreach (Command command in instance.commands)
        {
            if (Array.IndexOf(command.Names, name) < 0) continue;

            try
            {
                command.Run(args);
            }
            catch (Exception exception)
            {
                instance.Error(name + " failed: " + exception.Message);
            }

            return;
        }

        instance.Error("Unknown command '" + name + "'. Type help.");
    }

    private void StepHistory(int direction)
    {
        if (history.Count == 0) return;

        historyIndex = Mathf.Clamp(historyIndex + direction, 0, history.Count);
        input = historyIndex < history.Count ? history[historyIndex] : "";
    }

    private void Complete()
    {
        string typed = input.Trim().ToLowerInvariant();

        if (typed.Length == 0 || typed.Contains(" ")) return;

        List<string> matches = new List<string>();

        foreach (Command command in commands)
        {
            foreach (string name in command.Names)
            {
                if (name.StartsWith(typed)) matches.Add(name);
            }
        }

        if (matches.Count == 1) input = matches[0] + " ";
        else if (matches.Count > 1) Print(string.Join("  ", matches), TextColor);
    }

    private void Print(string text, Color color)
    {
        lines.Add(new Line { Text = text, Color = color });

        if (lines.Count > maxLines) lines.RemoveRange(0, lines.Count - maxLines);
    }

    private void Info(string text) => Print(text, TextColor);
    private void Error(string text) => Print(text, ErrorColor);

    // ---------------------------------------------------------------- commands

    private void Add(string usage, string help, Action<string[]> run, params string[] names)
    {
        commands.Add(new Command { Names = names, Usage = usage, Help = help, Run = run });
    }

    private void RegisterCommands()
    {
        Add("help", "List every command.", _ => ListCommands(), "help", "?");
        Add("tcl", "Toggle noclip: fly through walls. WASD along the view, Space up, Ctrl down, Shift faster.",
            _ => Toggle("noclip", ref DevCheats.NoClip), "tcl", "noclip");
        Add("speed <x>", "Walk speed multiplier. 1 is normal.", args => SetMultiplier("speed", ref DevCheats.Speed, args), "speed");
        Add("jump <x>", "Jump height multiplier.", args => SetMultiplier("jump", ref DevCheats.Jump, args), "jump");
        Add("gravity <x>", "Player gravity multiplier. 0 floats.", args => SetMultiplier("gravity", ref DevCheats.Gravity, args, true), "gravity");
        Add("strength <x>", "Carry strength: heavy props stop lagging, throws hit harder.",
            args => SetMultiplier("strength", ref DevCheats.Strength, args), "strength");
        Add("maxstrength", "Toggle: carry anything as if weightless, throws x3.",
            _ => Toggle("max strength", ref DevCheats.MaxStrength), "maxstrength", "tsm");
        Add("timescale <x>", "Game speed once the console closes. 0.25 is slow motion.", SetTimeScale, "timescale", "sgtm");
        Add("tp <x> <y> <z>", "Teleport the player. tp checkpoint goes to the current checkpoint.", Teleport, "tp", "coc");
        Add("checkpoint", "Reset the level to the last checkpoint.", _ => ResetCheckpoint(), "checkpoint", "reset");
        Add("reload", "Restart the current scene.", _ => Reload(), "reload");
        Add("laser", "Toggle every photon emitter.", _ => ToggleLasers(), "laser");
        Add("stats", "Toggle the F3 stats overlay.", _ => ToggleStats(), "stats");
        Add("pos", "Print where the player stands.", _ => PrintPosition(), "pos");
        Add("resetdebug", "Every cheat back to normal.", _ => ResetCheats(), "resetdebug");
        Add("clear", "Clear the console.", _ => lines.Clear(), "clear", "cls");
    }

    private void ListCommands()
    {
        foreach (Command command in commands)
        {
            string aliases = command.Names.Length > 1
                ? "  (" + string.Join(", ", command.Names, 1, command.Names.Length - 1) + ")"
                : "";

            Info(command.Usage.PadRight(16) + command.Help + aliases);
        }
    }

    private void Toggle(string label, ref bool value)
    {
        value = !value;
        Info(label + ": " + (value ? "ON" : "off"));
    }

    private void SetMultiplier(string label, ref float value, string[] args, bool allowZero = false)
    {
        if (args.Length == 0)
        {
            Info(label + " is x" + value.ToString("0.##", CultureInfo.InvariantCulture));
            return;
        }

        if (!TryParse(args[0], out float parsed) || parsed < 0f || (!allowZero && parsed == 0f))
        {
            Error(label + " needs a " + (allowZero ? "number of 0 or more" : "number above 0") + ", like " + label + " 2");
            return;
        }

        Info(label + ": x" + value.ToString("0.##", CultureInfo.InvariantCulture) + " -> x" + parsed.ToString("0.##", CultureInfo.InvariantCulture));
        value = parsed;
    }

    private void SetTimeScale(string[] args)
    {
        if (args.Length == 0)
        {
            Info("timescale is " + resumeTimeScale.ToString("0.##", CultureInfo.InvariantCulture));
            return;
        }

        if (!TryParse(args[0], out float parsed) || parsed < 0f || parsed > 10f)
        {
            Error("timescale needs a number from 0 to 10, like timescale 0.5");
            return;
        }

        // Stored, not applied: time stays frozen until the console closes.
        Info("timescale: " + resumeTimeScale.ToString("0.##", CultureInfo.InvariantCulture) + " -> " + parsed.ToString("0.##", CultureInfo.InvariantCulture) + " (on close)");
        resumeTimeScale = parsed;
    }

    private void Teleport(string[] args)
    {
        Movement player = FindAnyObjectByType<Movement>();

        if (player == null)
        {
            Error("No player in this scene.");
            return;
        }

        PlayerCamera look = player.GetComponentInChildren<PlayerCamera>();
        float yaw = look != null ? look.Yaw : player.transform.eulerAngles.y;

        if (args.Length == 1 && args[0].ToLowerInvariant() == "checkpoint")
        {
            Checkpoint current = CheckpointManager.HasInstance ? CheckpointManager.Instance.Current : null;

            if (current == null)
            {
                Error("No checkpoint reached yet.");
                return;
            }

            player.Teleport(current.SpawnPosition, current.SpawnYaw);
            Info("Teleported to " + current.RoomName);
            return;
        }

        if (args.Length != 3 || !TryParse(args[0], out float x) || !TryParse(args[1], out float y) || !TryParse(args[2], out float z))
        {
            Error("tp needs three numbers, like tp 0 2 10, or tp checkpoint");
            return;
        }

        player.Teleport(new Vector3(x, y, z), yaw);
        Info("Teleported to " + x + " " + y + " " + z);
    }

    private void ResetCheckpoint()
    {
        CheckpointManager.Instance.ResetToCheckpoint();
        Info("Reset to checkpoint.");
    }

    private void Reload()
    {
        // Time must run again before the fade, or a frozen fade never finishes.
        resumeTimeScale = 1f;
        Close();
        SceneTransition.Load(new TransitionSettings(SceneManager.GetActiveScene().name));
    }

    private void ToggleLasers()
    {
        PhotonEmitter[] emitters = FindObjectsByType<PhotonEmitter>();

        if (emitters.Length == 0)
        {
            Error("No photon emitters in this scene.");
            return;
        }

        bool turnOn = !emitters[0].IsOn;

        foreach (PhotonEmitter emitter in emitters) emitter.IsOn = turnOn;

        Info(emitters.Length + " laser(s) " + (turnOn ? "on" : "off"));
    }

    private void ToggleStats()
    {
        GUIstats stats = FindAnyObjectByType<GUIstats>();

        if (stats == null)
        {
            Error("The stats overlay is not running.");
            return;
        }

        stats.Visible = !stats.Visible;
        Info("stats: " + (stats.Visible ? "on" : "off"));
    }

    private void PrintPosition()
    {
        Movement player = FindAnyObjectByType<Movement>();

        if (player == null) Error("No player in this scene.");
        else Info("Player at " + player.transform.position.ToString("0.00"));
    }

    private void ResetCheats()
    {
        DevCheats.ResetAll();
        Info("All cheats off, multipliers back to 1.");
    }

    // Invariant culture, so 0.5 parses on a Dutch system that writes it as 0,5.
    private static bool TryParse(string text, out float value)
    {
        return float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ---------------------------------------------------------------- drawing

    void OnGUI()
    {
        EnsureResources();

        if (!IsOpen)
        {
            DrawBadge();
            return;
        }

        float height = Mathf.Round(Screen.height * heightFraction);
        DrawRect(new Rect(0f, 0f, Screen.width, height), BackgroundColor);
        DrawRect(new Rect(0f, height, Screen.width, 2f), EchoColor);

        // Newest at the bottom, filling upwards above the input line.
        float inputY = height - LineHeight - Padding;
        float y = inputY - LineHeight - 4f;

        for (int i = lines.Count - 1; i >= 0 && y > -LineHeight; i--)
        {
            lineStyle.normal.textColor = lines[i].Color;
            GUI.Label(new Rect(Padding, y, Screen.width - Padding * 2f, LineHeight), lines[i].Text, lineStyle);
            y -= LineHeight;
        }

        // Unscaled, so the caret still blinks while time is frozen.
        bool caret = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;

        lineStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(Padding, inputY, Screen.width - Padding * 2f, LineHeight), "> " + input + (caret ? "_" : ""), lineStyle);
    }

    // A reminder in the corner while any cheat is on, so nobody tests a puzzle with noclip still set.
    private void DrawBadge()
    {
        if (!DevCheats.AnyActive) return;

        List<string> active = new List<string>();

        if (DevCheats.NoClip) active.Add("noclip");
        if (DevCheats.MaxStrength) active.Add("maxstrength");
        if (DevCheats.Speed != 1f) active.Add("speed x" + DevCheats.Speed.ToString("0.##", CultureInfo.InvariantCulture));
        if (DevCheats.Jump != 1f) active.Add("jump x" + DevCheats.Jump.ToString("0.##", CultureInfo.InvariantCulture));
        if (DevCheats.Gravity != 1f) active.Add("gravity x" + DevCheats.Gravity.ToString("0.##", CultureInfo.InvariantCulture));
        if (DevCheats.Strength != 1f) active.Add("strength x" + DevCheats.Strength.ToString("0.##", CultureInfo.InvariantCulture));

        string text = "DEBUG: " + string.Join(", ", active);
        Vector2 size = badgeStyle.CalcSize(new GUIContent(text));
        Rect rect = new Rect(Screen.width - size.x - 16f, Screen.height - size.y - 12f, size.x + 8f, size.y + 4f);

        DrawRect(rect, new Color(0f, 0f, 0f, 0.6f));
        GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, size.x, size.y), text, badgeStyle);
    }

    private void DrawRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }

    // Styles and textures need a GUI context, so not in Awake.
    private void EnsureResources()
    {
        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        if (lineStyle != null) return;

        lineStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            richText = false,
            wordWrap = false
        };

        badgeStyle = new GUIStyle(lineStyle) { fontSize = 12, fontStyle = FontStyle.Bold };
        badgeStyle.normal.textColor = new Color(1f, 0.75f, 0.3f);
    }
}
