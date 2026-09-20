using UnityEngine;
using UnityEngine.InputSystem;

// Debug overlay and crosshair. Spawns itself, F3 toggles it. Temporary - delete Temp folder.
public class GUIstats : MonoBehaviour
{
    [Tooltip("Optional. Left empty, the first Movement in the scene is used.")]
    [SerializeField] private Movement movement;

    [Tooltip("Optional. Left empty, the first GrabInteract in the scene is used.")]
    [SerializeField] private GrabInteract grab;

    [SerializeField] private Key toggleKey = Key.F3;
    [SerializeField] private bool visible = true;

    [Header("Crosshair")]
    [Tooltip("Temporary aiming dot at the centre of the screen. Independent of the stats " +
             "panel, so F3 hides the numbers but leaves the dot.")]
    [SerializeField] private bool showCrosshair = true;

    [SerializeField] private float crosshairSize = 4f;

    [Header("Graph")]
    [Tooltip("How many frames of speed history the graph holds.")]
    [SerializeField] private int historyLength = 200;

    [Tooltip("Extra vertical space above the peak so a climbing trace does not touch the top.")]
    [SerializeField] private float graphHeadroom = 1.15f;

    [Header("Layout")]
    [SerializeField] private Vector2 origin = new Vector2(12f, 12f);
    [SerializeField] private float panelWidth = 250f;

    // Source measures in inches, for comparing against sv_maxspeed.
    private const float MetresToSourceUnits = 1f / 0.0254f;

    private const float Padding = 10f;
    private const float RowHeight = 16f;
    private const float GraphHeight = 56f;
    private const float PanelHeight = 240f;

    private readonly Color backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.85f);
    private readonly Color labelColor = new Color(0.62f, 0.66f, 0.72f);
    private readonly Color normalSpeedColor = new Color(0.45f, 0.78f, 1f);
    private readonly Color gainedSpeedColor = new Color(1f, 0.64f, 0.25f);
    private readonly Color referenceColor = new Color(1f, 1f, 1f, 0.25f);

    private float[] history;
    private int historyHead;
    private float peakSpeed;
    private float nextSearchTime;

    private Texture2D pixel;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle bigStyle;
    private GUIStyle unitStyle;

    // Spawns itself, so a missing overlay means a compile error, not a missing object.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (FindAnyObjectByType<GUIstats>() != null) return;

        GameObject host = new GameObject("GUI Stats (auto)");
        host.AddComponent<GUIstats>();
        DontDestroyOnLoad(host);
    }

    void Awake()
    {
        history = new float[Mathf.Max(16, historyLength)];
        FindReferences();

        Debug.Log("GUIstats overlay running. Press " + toggleKey + " to toggle it.", this);
    }

    void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) visible = !visible;

        if ((movement == null || grab == null) && Time.unscaledTime >= nextSearchTime)
        {
            nextSearchTime = Time.unscaledTime + 1f;
            FindReferences();
        }

        if (movement == null) return;

        float speed = movement.Speed;

        // Sampled here, not in OnGUI, which runs several times a frame.
        history[historyHead] = speed;
        historyHead = (historyHead + 1) % history.Length;

        // Peak is per run, so stopping starts a fresh measurement.
        if (movement.IsGrounded && speed < 0.1f)
        {
            peakSpeed = 0f;
        }
        else
        {
            peakSpeed = Mathf.Max(peakSpeed, speed);
        }
    }

    void OnGUI()
    {
        EnsureResources();

        if (showCrosshair) DrawCrosshair();

        if (!visible) return;

        Rect panel = new Rect(origin.x, origin.y, panelWidth, PanelHeight);
        DrawRect(panel, backgroundColor);

        if (movement == null)
        {
            GUI.Label(new Rect(panel.x + Padding, panel.y + Padding, panel.width - Padding * 2f, 40f),
                "No Movement component found in the scene.", labelStyle);
            return;
        }

        float speed = movement.Speed;
        float baseSpeed = Mathf.Max(0.01f, movement.BaseSpeed);
        bool gaining = speed > baseSpeed + 0.05f;

        float x = panel.x + Padding;
        float w = panel.width - Padding * 2f;
        float y = panel.y + Padding;

        bigStyle.normal.textColor = gaining ? gainedSpeedColor : normalSpeedColor;
        GUI.Label(new Rect(x, y, w, 30f), speed.ToString("0.00"), bigStyle);

        GUI.Label(new Rect(x + 74f, y + 10f, w - 74f, 20f), "m/s", unitStyle);
        y += 30f;

        GUI.Label(new Rect(x, y, w, 14f),
            (speed * MetresToSourceUnits).ToString("0") + " u/s  (Source units)", labelStyle);
        y += 20f;

        Row(x, ref y, w, "Peak", peakSpeed.ToString("0.00") + " m/s");
        Row(x, ref y, w, "Vertical", movement.Velocity.y.ToString("0.00") + " m/s");
        Row(x, ref y, w, "State", movement.IsGrounded ? "Grounded" : "Airborne");
        Row(x, ref y, w, "Mode", movement.IsSourceMode ? "Source" : "Simple");

        // Full line, because rejection reasons are long.
        if (grab != null)
        {
            y += 4f;
            GUI.Label(new Rect(x, y, w, RowHeight), "Aim", labelStyle);
            y += RowHeight;

            GUI.Label(new Rect(x, y, w, RowHeight), grab.AimStatus,
                grab.Aimed != null ? valueStyle : labelStyle);

            y += RowHeight;
        }

        y += 6f;
        DrawGraph(new Rect(x, y, w, GraphHeight), baseSpeed);
    }

    private void Row(float x, ref float y, float width, string label, string value)
    {
        GUI.Label(new Rect(x, y, width * 0.45f, RowHeight), label, labelStyle);
        GUI.Label(new Rect(x + width * 0.45f, y, width * 0.55f, RowHeight), value, valueStyle);
        y += RowHeight;
    }

    private void DrawGraph(Rect area, float baseSpeed)
    {
        DrawRect(area, new Color(0f, 0f, 0f, 0.35f));

        // Never below walk speed, or the reference line sits off the top.
        float max = Mathf.Max(peakSpeed, baseSpeed) * graphHeadroom;

        float barWidth = area.width / history.Length;

        for (int i = 0; i < history.Length; i++)
        {
            // Oldest first, so the trace scrolls left to right.
            float sample = history[(historyHead + i) % history.Length];

            if (sample <= 0.001f) continue;

            float height = Mathf.Clamp01(sample / max) * area.height;
            Color color = sample > baseSpeed + 0.05f ? gainedSpeedColor : normalSpeedColor;

            DrawRect(new Rect(area.x + i * barWidth, area.yMax - height,
                Mathf.Max(1f, barWidth), height), color);
        }

        // Above this line is speed gained from momentum, not from holding a key.
        float referenceY = area.yMax - Mathf.Clamp01(baseSpeed / max) * area.height;
        DrawRect(new Rect(area.x, referenceY, area.width, 1f), referenceColor);

        GUI.Label(new Rect(area.x + 3f, referenceY - 14f, area.width, 14f),
            "walk " + baseSpeed.ToString("0.0"), labelStyle);
    }

    private void DrawRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }

    private void FindReferences()
    {
        if (movement == null) movement = FindAnyObjectByType<Movement>();
        if (grab == null) grab = FindAnyObjectByType<GrabInteract>();
    }

    private void DrawCrosshair()
    {
        float size = Mathf.Max(1f, crosshairSize);
        float x = (Screen.width - size) * 0.5f;
        float y = (Screen.height - size) * 0.5f;

        // Dark halo, so the dot reads against bright and dark alike.
        DrawRect(new Rect(x - 1f, y - 1f, size + 2f, size + 2f), new Color(0f, 0f, 0f, 0.55f));

        bool onTarget = grab != null && grab.Aimed != null;

        DrawRect(new Rect(x, y, size, size), new Color(1f, 1f, 1f, onTarget ? 1f : 0.5f));

        DrawChargeMeter();
    }

    private void DrawChargeMeter()
    {
        if (grab == null) return;

        float charge = grab.ChargeNormalized;

        if (charge <= 0f) return;

        const float meterWidth = 46f;
        const float meterHeight = 3f;

        float x = (Screen.width - meterWidth) * 0.5f;
        float y = Screen.height * 0.5f + 12f;

        DrawRect(new Rect(x - 1f, y - 1f, meterWidth + 2f, meterHeight + 2f), new Color(0f, 0f, 0f, 0.6f));
        DrawRect(new Rect(x, y, meterWidth, meterHeight), new Color(1f, 1f, 1f, 0.2f));
        DrawRect(new Rect(x, y, meterWidth * charge, meterHeight), Color.white);
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

        if (labelStyle != null) return;

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft
        };
        labelStyle.normal.textColor = labelColor;

        valueStyle = new GUIStyle(labelStyle)
        {
            fontStyle = FontStyle.Bold
        };
        valueStyle.normal.textColor = Color.white;

        bigStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        unitStyle = new GUIStyle(labelStyle)
        {
            fontSize = 12
        };
    }
}
