using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SHADOW MODE HUD — Full body tracking visualization interface.
///
/// Layout:
///   ┌──────────────────────────────────────────────────────────┐
///   │  ◆ SHADOW MODE (Body Tracking)           [FPS] [TIME]    │
///   ├──────────────┬────────────────────┬──────────────────────┤
///   │  LEFT-FRONT  │    CAMERA FEED     │   RIGHT-FRONT        │
///   │  ┌────────┐  │  ┌──────────────┐  │  ┌────────────────┐  │
///   │  │ model  │  │  │              │  │  │     model      │  │
///   │  │ 45°L   │  │  │ [placeholder]│  │  │     45°R       │  │
///   │  └────────┘  │  └──────────────┘  │  └────────────────┘  │
///   │  Body: OK    │                    │  Hands: OK           │
///   ├──────────────┴────────────────────┴──────────────────────┤
///   │ ● BODY: TRACKED │ HANDS: OK │ Calibrated │ Confidence   │
///   └──────────────────────────────────────────────────────────┘
///
/// Features:
///   - Cyan holographic color scheme
///   - Corner bracket decorations + scan line animation
///   - Pulsing status indicators
///   - Smooth head-follow positioning
///   - Two 3D model viewports showing real-time body tracking
/// </summary>
public class IronManHUD : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("References")]
    [SerializeField] private TeleopManager teleopManager;
    [SerializeField] private HumanoidModelViewer modelViewer;

    [Header("HUD Settings")]
    [Tooltip("HUD distance from head (m)")]
    [SerializeField] private float hudDistance = 1.2f;
    [Tooltip("Follow smoothing speed")]
    [SerializeField] private float followSmoothSpeed = 3f;

    // ══════════════════════════════════════════════════
    //                    Color Scheme
    // ══════════════════════════════════════════════════

    private static readonly Color CYAN          = new Color(0.00f, 1.00f, 1.00f, 1.0f);
    private static readonly Color CYAN_DIM      = new Color(0.00f, 0.75f, 0.90f, 0.6f);
    private static readonly Color GREEN         = new Color(0.00f, 1.00f, 0.50f, 1.0f);
    private static readonly Color RED           = new Color(1.00f, 0.20f, 0.30f, 1.0f);
    private static readonly Color GOLD          = new Color(1.00f, 0.85f, 0.00f, 1.0f);
    private static readonly Color WHITE_DIM     = new Color(0.85f, 0.90f, 0.95f, 0.9f);

    private static readonly Color BG_DARK       = new Color(0.02f, 0.03f, 0.08f, 0.88f);
    private static readonly Color BG_PANEL      = new Color(0.03f, 0.05f, 0.12f, 0.80f);
    private static readonly Color BG_STATUS     = new Color(0.02f, 0.02f, 0.06f, 0.92f);
    private static readonly Color BORDER        = new Color(0.00f, 0.80f, 1.00f, 0.35f);
    private static readonly Color BORDER_BRIGHT = new Color(0.00f, 0.90f, 1.00f, 0.70f);

    // ══════════════════════════════════════════════════
    //                   HUD Dimensions
    // ══════════════════════════════════════════════════

    private const float CANVAS_W = 1400f;
    private const float CANVAS_H = 820f;
    private const float CANVAS_SCALE = 0.001f;

    // ══════════════════════════════════════════════════
    //                  Internal Refs
    // ══════════════════════════════════════════════════

    private Canvas _canvas;
    private Font _font;

    // UI elements
    private Text _titleText;
    private Text _timeText;
    private Text _fpsText;
    private Text _leftViewLabel;
    private Text _leftViewData;
    private Text _rightViewLabel;
    private Text _rightViewData;
    private Text _cameraPlaceholder;
    private Text _statusText;
    private Image _scanLine;
    private Image _bodyIndicator;
    private Image _handsIndicator;

    // Model view RawImages
    private RawImage _leftViewRawImage;
    private RawImage _rightViewRawImage;

    // HUD follow
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private bool _positionInitialized;

    // FPS
    private float _fpsTimer;
    private int _fpsCount;
    private int _currentFps;

    // Corner brackets
    private Image[] _cornerBrackets;

    // ══════════════════════════════════════════════════
    //                Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();

        // Bind RenderTextures from viewer (delayed to allow initialization)
        Invoke(nameof(BindViewerTextures), 0.5f);
    }

    private void Update()
    {
        UpdateHUDPosition();
        UpdateStatusBar();
        UpdateBodyStatus();
        UpdateScanLineAnimation();
        UpdatePulsingEffects();
        UpdateFPS();
    }

    // ══════════════════════════════════════════════════
    //                  Build HUD
    // ══════════════════════════════════════════════════

    private void BuildHUD()
    {
        // ─── Canvas ───
        GameObject canvasObj = new GameObject("IronManHUD_Canvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = _canvas.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);
        canvasRT.localScale = Vector3.one * CANVAS_SCALE;

        // ─── Main background ───
        CreatePanel(canvasObj.transform, "MainBG", Vector2.zero, Vector2.one, BG_DARK);

        // ─── Borders ───
        CreateBorder(canvasObj.transform, "OuterBorder", BORDER_BRIGHT, 2f);
        CreateBorder(canvasObj.transform, "InnerBorder", BORDER, 1f, 4f);

        // ─── Corner brackets ───
        CreateCornerBrackets(canvasObj.transform);

        // ─── Title bar (top 6%) ───
        BuildTitleBar(canvasObj.transform);

        // ─── Left-front model view ───
        BuildModelViewPanel(canvasObj.transform, true,
            new Vector2(0.01f, 0.12f), new Vector2(0.22f, 0.92f));

        // ─── Camera feed area (center) ───
        BuildCameraArea(canvasObj.transform,
            new Vector2(0.23f, 0.12f), new Vector2(0.77f, 0.92f));

        // ─── Right-front model view ───
        BuildModelViewPanel(canvasObj.transform, false,
            new Vector2(0.78f, 0.12f), new Vector2(0.99f, 0.92f));

        // ─── Status bar ───
        BuildStatusBar(canvasObj.transform);

        // ─── Scan line ───
        CreateScanLine(canvasObj.transform);
    }

    // ──────────── Title Bar ────────────

    private void BuildTitleBar(Transform parent)
    {
        Image titleBg = CreatePanel(parent, "TitleBar",
            new Vector2(0.01f, 0.93f), new Vector2(0.99f, 0.99f),
            new Color(0.01f, 0.02f, 0.06f, 0.95f));

        CreatePanel(parent, "TitleLine",
            new Vector2(0.01f, 0.925f), new Vector2(0.99f, 0.932f), BORDER);

        _titleText = CreateText(titleBg.transform, "Title",
            new Vector2(0.02f, 0f), new Vector2(0.65f, 1f),
            "◆  S H A D O W   M O D E",
            CYAN, 24, TextAnchor.MiddleLeft);

        _fpsText = CreateText(titleBg.transform, "FPS",
            new Vector2(0.68f, 0f), new Vector2(0.82f, 1f),
            "FPS: --", CYAN_DIM, 18, TextAnchor.MiddleRight);

        _timeText = CreateText(titleBg.transform, "Time",
            new Vector2(0.84f, 0f), new Vector2(0.98f, 1f),
            "00:00:00", CYAN_DIM, 18, TextAnchor.MiddleRight);
    }

    // ──────────── Model View Panel ────────────

    private void BuildModelViewPanel(Transform parent, bool isLeft,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        string side = isLeft ? "Left" : "Right";

        Image panelBg = CreatePanel(parent, $"{side}ViewPanel",
            anchorMin, anchorMax, BG_PANEL);

        CreatePanelBorder(panelBg.transform, BORDER);

        // Header
        Image headerBg = CreatePanel(panelBg.transform, "Header",
            new Vector2(0f, 0.88f), new Vector2(1f, 1f),
            new Color(0f, 0.6f, 1f, 0.15f));

        Text label = CreateText(headerBg.transform, "Label",
            new Vector2(0.08f, 0f), new Vector2(0.92f, 1f),
            isLeft ? "◁  LEFT-FRONT" : "RIGHT-FRONT  ▷",
            CYAN, 18, isLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);

        if (isLeft) _leftViewLabel = label;
        else _rightViewLabel = label;

        // Model render area
        Image renderArea = CreatePanel(panelBg.transform, "RenderArea",
            new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.86f),
            new Color(0.02f, 0.03f, 0.08f, 0.95f));

        // RawImage for RenderTexture
        GameObject rawImgObj = new GameObject("ModelRawImage");
        rawImgObj.transform.SetParent(renderArea.transform, false);
        RawImage rawImg = rawImgObj.AddComponent<RawImage>();
        rawImg.color = Color.white;
        rawImg.raycastTarget = false;
        RectTransform rawRT = rawImg.rectTransform;
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.offsetMin = Vector2.zero;
        rawRT.offsetMax = Vector2.zero;

        if (isLeft) _leftViewRawImage = rawImg;
        else _rightViewRawImage = rawImg;

        // Render area border glow
        CreatePanelBorder(renderArea.transform,
            new Color(0f, 0.6f, 0.8f, 0.2f));

        // Corner tick marks on render area
        float tick = 0.08f;
        float thin = 0.01f;
        Color tickColor = new Color(0f, 0.8f, 1f, 0.15f);
        CreatePanel(renderArea.transform, "TL_H", new Vector2(0, 1 - thin), new Vector2(tick, 1), tickColor);
        CreatePanel(renderArea.transform, "TL_V", new Vector2(0, 1 - tick), new Vector2(thin, 1), tickColor);
        CreatePanel(renderArea.transform, "TR_H", new Vector2(1 - tick, 1 - thin), new Vector2(1, 1), tickColor);
        CreatePanel(renderArea.transform, "TR_V", new Vector2(1 - thin, 1 - tick), new Vector2(1, 1), tickColor);
        CreatePanel(renderArea.transform, "BL_H", new Vector2(0, 0), new Vector2(tick, thin), tickColor);
        CreatePanel(renderArea.transform, "BL_V", new Vector2(0, 0), new Vector2(thin, tick), tickColor);
        CreatePanel(renderArea.transform, "BR_H", new Vector2(1 - tick, 0), new Vector2(1, thin), tickColor);
        CreatePanel(renderArea.transform, "BR_V", new Vector2(1 - thin, 0), new Vector2(1, tick), tickColor);

        // Angle label
        CreateText(renderArea.transform, "AngleLabel",
            new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.08f),
            isLeft ? "<color=#005577>VIEW: 45° LEFT</color>" : "<color=#005577>VIEW: 45° RIGHT</color>",
            CYAN_DIM, 11, TextAnchor.MiddleCenter);

        // Data area below render
        Text data = CreateText(panelBg.transform, "Data",
            new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.16f),
            "Body: ---\nHands: ---",
            WHITE_DIM, 14, TextAnchor.UpperLeft);

        if (isLeft) _leftViewData = data;
        else _rightViewData = data;
    }

    // ──────────── Camera Area ────────────

    private void BuildCameraArea(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        Image camBg = CreatePanel(parent, "CameraPanel",
            anchorMin, anchorMax, new Color(0.05f, 0.05f, 0.08f, 0.85f));

        CreatePanelBorder(camBg.transform, BORDER);

        CreateText(camBg.transform, "CamTitle",
            new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.98f),
            "▣  CAMERA FEED",
            CYAN_DIM, 16, TextAnchor.MiddleLeft);

        // Grid lines
        for (int i = 1; i < 4; i++)
        {
            float y = i * 0.22f + 0.1f;
            CreatePanel(camBg.transform, $"GridH_{i}",
                new Vector2(0.02f, y), new Vector2(0.98f, y + 0.002f),
                new Color(0f, 0.6f, 0.8f, 0.06f));
        }
        for (int i = 1; i < 4; i++)
        {
            float x = i * 0.25f;
            CreatePanel(camBg.transform, $"GridV_{i}",
                new Vector2(x, 0.05f), new Vector2(x + 0.002f, 0.88f),
                new Color(0f, 0.6f, 0.8f, 0.06f));
        }

        // Crosshair
        CreatePanel(camBg.transform, "CrossH",
            new Vector2(0.35f, 0.495f), new Vector2(0.65f, 0.505f),
            new Color(0f, 1f, 1f, 0.12f));
        CreatePanel(camBg.transform, "CrossV",
            new Vector2(0.498f, 0.30f), new Vector2(0.502f, 0.70f),
            new Color(0f, 1f, 1f, 0.12f));

        _cameraPlaceholder = CreateText(camBg.transform, "Placeholder",
            new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.75f),
            "<color=#006688>NO SIGNAL</color>\n\n<size=14><color=#004466>Camera feed placeholder\nAwaiting connection...</color></size>",
            WHITE_DIM, 28, TextAnchor.MiddleCenter);

        CreateText(camBg.transform, "CamInfo",
            new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.10f),
            "<color=#005577>RES: --- x ---  |  CODEC: ---  |  LATENCY: ---ms</color>",
            CYAN_DIM, 12, TextAnchor.MiddleCenter);
    }

    // ──────────── Status Bar ────────────

    private void BuildStatusBar(Transform parent)
    {
        Image statusBg = CreatePanel(parent, "StatusBar",
            new Vector2(0.01f, 0.015f), new Vector2(0.99f, 0.105f), BG_STATUS);

        CreatePanel(parent, "StatusLine",
            new Vector2(0.01f, 0.105f), new Vector2(0.99f, 0.112f), BORDER);

        // Body tracking indicator
        _bodyIndicator = CreatePanel(statusBg.transform, "BodyDot",
            new Vector2(0.015f, 0.25f), new Vector2(0.028f, 0.75f), RED).GetComponent<Image>();

        // Hands tracking indicator
        _handsIndicator = CreatePanel(statusBg.transform, "HandsDot",
            new Vector2(0.28f, 0.25f), new Vector2(0.293f, 0.75f), RED).GetComponent<Image>();

        // Status text
        _statusText = CreateText(statusBg.transform, "StatusText",
            new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f),
            "INITIALIZING...",
            WHITE_DIM, 17, TextAnchor.MiddleLeft);
    }

    // ══════════════════════════════════════════════════
    //                  Decorations
    // ══════════════════════════════════════════════════

    private void CreateCornerBrackets(Transform parent)
    {
        _cornerBrackets = new Image[8];
        float size = 40f / CANVAS_W;
        float thick = 3f / CANVAS_H;

        _cornerBrackets[0] = CreatePanel(parent, "BracketTL_H",
            new Vector2(0.005f, 1f - 0.005f - thick),
            new Vector2(0.005f + size, 1f - 0.005f), CYAN).GetComponent<Image>();
        _cornerBrackets[1] = CreatePanel(parent, "BracketTL_V",
            new Vector2(0.005f, 1f - 0.005f - size),
            new Vector2(0.005f + thick * (CANVAS_H / CANVAS_W), 1f - 0.005f), CYAN).GetComponent<Image>();

        _cornerBrackets[2] = CreatePanel(parent, "BracketTR_H",
            new Vector2(1f - 0.005f - size, 1f - 0.005f - thick),
            new Vector2(1f - 0.005f, 1f - 0.005f), CYAN).GetComponent<Image>();
        _cornerBrackets[3] = CreatePanel(parent, "BracketTR_V",
            new Vector2(1f - 0.005f - thick * (CANVAS_H / CANVAS_W), 1f - 0.005f - size),
            new Vector2(1f - 0.005f, 1f - 0.005f), CYAN).GetComponent<Image>();

        _cornerBrackets[4] = CreatePanel(parent, "BracketBL_H",
            new Vector2(0.005f, 0.005f),
            new Vector2(0.005f + size, 0.005f + thick), CYAN).GetComponent<Image>();
        _cornerBrackets[5] = CreatePanel(parent, "BracketBL_V",
            new Vector2(0.005f, 0.005f),
            new Vector2(0.005f + thick * (CANVAS_H / CANVAS_W), 0.005f + size), CYAN).GetComponent<Image>();

        _cornerBrackets[6] = CreatePanel(parent, "BracketBR_H",
            new Vector2(1f - 0.005f - size, 0.005f),
            new Vector2(1f - 0.005f, 0.005f + thick), CYAN).GetComponent<Image>();
        _cornerBrackets[7] = CreatePanel(parent, "BracketBR_V",
            new Vector2(1f - 0.005f - thick * (CANVAS_H / CANVAS_W), 0.005f),
            new Vector2(1f - 0.005f, 0.005f + size), CYAN).GetComponent<Image>();
    }

    private void CreateScanLine(Transform parent)
    {
        _scanLine = CreatePanel(parent, "ScanLine",
            new Vector2(0.01f, 0.5f), new Vector2(0.99f, 0.504f),
            new Color(0f, 1f, 1f, 0.08f)).GetComponent<Image>();
    }

    private void CreateBorder(Transform parent, string name, Color color, float thickness, float inset = 0f)
    {
        float ix = inset / CANVAS_W;
        float iy = inset / CANVAS_H;
        float tx = thickness / CANVAS_W;
        float ty = thickness / CANVAS_H;

        CreatePanel(parent, $"{name}_T", new Vector2(ix, 1f - iy - ty), new Vector2(1f - ix, 1f - iy), color);
        CreatePanel(parent, $"{name}_B", new Vector2(ix, iy), new Vector2(1f - ix, iy + ty), color);
        CreatePanel(parent, $"{name}_L", new Vector2(ix, iy), new Vector2(ix + tx, 1f - iy), color);
        CreatePanel(parent, $"{name}_R", new Vector2(1f - ix - tx, iy), new Vector2(1f - ix, 1f - iy), color);
    }

    private void CreatePanelBorder(Transform parent, Color color)
    {
        float t = 1f / 200f;
        CreatePanel(parent, "Border_T", new Vector2(0, 1 - t), Vector2.one, color);
        CreatePanel(parent, "Border_B", Vector2.zero, new Vector2(1, t), color);
        CreatePanel(parent, "Border_L", Vector2.zero, new Vector2(t, 1), color);
        CreatePanel(parent, "Border_R", new Vector2(1 - t, 0), Vector2.one, color);
    }

    // ══════════════════════════════════════════════════
    //          Viewer Texture Binding
    // ══════════════════════════════════════════════════

    private void BindViewerTextures()
    {
        if (modelViewer == null)
        {
            Debug.LogWarning("[IronManHUD] HumanoidModelViewer not assigned, retrying...");
            Invoke(nameof(BindViewerTextures), 0.5f);
            return;
        }

        if (!modelViewer.IsInitialized)
        {
            Invoke(nameof(BindViewerTextures), 0.5f);
            return;
        }

        if (_leftViewRawImage != null && modelViewer.LeftRenderTexture != null)
            _leftViewRawImage.texture = modelViewer.LeftRenderTexture;

        if (_rightViewRawImage != null && modelViewer.RightRenderTexture != null)
            _rightViewRawImage.texture = modelViewer.RightRenderTexture;

        Debug.Log("[IronManHUD] Viewer textures bound to HUD");
    }

    // ══════════════════════════════════════════════════
    //               HUD Position Follow
    // ══════════════════════════════════════════════════

    private void UpdateHUDPosition()
    {
        if (_canvas == null || teleopManager == null || teleopManager.CameraRig == null) return;

        Transform eye = teleopManager.CameraRig.centerEyeAnchor;
        if (eye == null) return;

        Vector3 forward = eye.forward;
        forward.y *= 0.3f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 targetPos = eye.position + forward * hudDistance;
        Quaternion targetRot = Quaternion.LookRotation(forward, Vector3.up);

        if (!_positionInitialized)
        {
            _canvas.transform.position = targetPos;
            _canvas.transform.rotation = targetRot;
            _targetPosition = targetPos;
            _targetRotation = targetRot;
            _positionInitialized = true;
        }
        else
        {
            float t = followSmoothSpeed * Time.deltaTime;
            _targetPosition = Vector3.Lerp(_targetPosition, targetPos, t);
            _targetRotation = Quaternion.Slerp(_targetRotation, targetRot, t);
            _canvas.transform.position = _targetPosition;
            _canvas.transform.rotation = _targetRotation;
        }
    }

    // ══════════════════════════════════════════════════
    //                 Status Updates
    // ══════════════════════════════════════════════════

    private void UpdateStatusBar()
    {
        if (_statusText == null || teleopManager == null) return;

        var body = teleopManager.BodyTracker;
        var normalizer = teleopManager.Normalizer;

        // ─── Body tracking ───
        string bodyState, bodyColor;
        if (body != null && body.IsBodyTracking)
        {
            bodyState = "TRACKED";
            bodyColor = "#00FF80";
            if (_bodyIndicator != null) _bodyIndicator.color = GREEN;
        }
        else
        {
            bodyState = "NOT TRACKED";
            bodyColor = "#FF4466";
            if (_bodyIndicator != null) _bodyIndicator.color = RED;
        }

        // ─── Hand tracking ───
        string handsState, handsColor;
        bool leftHand = body != null && body.IsLeftHandTracking;
        bool rightHand = body != null && body.IsRightHandTracking;
        if (leftHand && rightHand)
        {
            handsState = "BOTH OK";
            handsColor = "#00FF80";
            if (_handsIndicator != null) _handsIndicator.color = GREEN;
        }
        else if (leftHand || rightHand)
        {
            handsState = leftHand ? "LEFT ONLY" : "RIGHT ONLY";
            handsColor = "#FFDD00";
            if (_handsIndicator != null) _handsIndicator.color = GOLD;
        }
        else
        {
            handsState = "NONE";
            handsColor = "#FF4466";
            if (_handsIndicator != null) _handsIndicator.color = RED;
        }

        // ─── Calibration ───
        string calState, calColor;
        if (normalizer != null && normalizer.IsCalibrated)
        {
            calState = "YES";
            calColor = "#00FF80";
        }
        else
        {
            calState = "NO";
            calColor = "#FF4466";
        }

        // ─── Confidence ───
        string confStr = "---";
        string confColor = "#00CCFF";
        if (body != null && body.IsBodyTracking)
        {
            float conf = body.BodyConfidence;
            confStr = $"{conf:P0}";
            confColor = conf >= 0.8f ? "#00FF80" : conf >= 0.5f ? "#FFDD00" : "#FF4466";
        }

        // ─── Bones ───
        int boneCount = body != null ? body.BodyBoneCount : 0;

        _statusText.text =
            $"  <color={bodyColor}>●</color> BODY: <color={bodyColor}>{bodyState}</color>" +
            $"    |    <color={handsColor}>●</color> HANDS: <color={handsColor}>{handsState}</color>" +
            $"    |    CALIBRATED: <color={calColor}>{calState}</color>" +
            $"    |    CONFIDENCE: <color={confColor}>{confStr}</color>" +
            $"    |    BONES: <color=#00CCFF>{boneCount}</color>";

        if (_timeText != null)
            _timeText.text = System.DateTime.Now.ToString("HH:mm:ss");
    }

    private void UpdateBodyStatus()
    {
        if (teleopManager == null) return;

        var body = teleopManager.BodyTracker;

        // Left panel data
        if (_leftViewData != null)
        {
            bool bodyOk = body != null && body.IsBodyTracking;
            bool leftOk = body != null && body.IsLeftHandTracking;
            string bColor = bodyOk ? "#00FF80" : "#FF4466";
            string lColor = leftOk ? "#00FF80" : "#FF4466";

            _leftViewData.text =
                $"<color={bColor}>● BODY: {(bodyOk ? "OK" : "LOST")}</color>\n" +
                $"<color={lColor}>● L.HAND: {(leftOk ? "OK" : "---")}</color>";
        }

        // Right panel data
        if (_rightViewData != null)
        {
            bool bodyOk = body != null && body.IsBodyTracking;
            bool rightOk = body != null && body.IsRightHandTracking;
            string bColor = bodyOk ? "#00FF80" : "#FF4466";
            string rColor = rightOk ? "#00FF80" : "#FF4466";

            _rightViewData.text =
                $"<color={bColor}>● BODY: {(bodyOk ? "OK" : "LOST")}</color>\n" +
                $"<color={rColor}>● R.HAND: {(rightOk ? "OK" : "---")}</color>";
        }
    }

    // ══════════════════════════════════════════════════
    //                Animations
    // ══════════════════════════════════════════════════

    private void UpdateScanLineAnimation()
    {
        if (_scanLine == null) return;

        float y = Mathf.Repeat(Time.time * 0.06f, 1.2f) - 0.1f;
        var rt = _scanLine.rectTransform;
        rt.anchorMin = new Vector2(0.01f, y);
        rt.anchorMax = new Vector2(0.99f, y + 0.005f);

        float alpha = 1f - Mathf.Abs(y - 0.5f) * 2f;
        alpha = Mathf.Clamp01(alpha) * 0.10f;
        _scanLine.color = new Color(0f, 1f, 1f, alpha);
    }

    private void UpdatePulsingEffects()
    {
        if (_cornerBrackets == null) return;

        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 1.5f);
        Color bracketColor = CYAN * new Color(1, 1, 1, pulse);
        foreach (var bracket in _cornerBrackets)
        {
            if (bracket != null) bracket.color = bracketColor;
        }

        if (_titleText != null)
        {
            float titlePulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 0.8f);
            _titleText.color = CYAN * new Color(1, 1, 1, titlePulse);
        }
    }

    private void UpdateFPS()
    {
        _fpsCount++;
        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= 0.5f)
        {
            _currentFps = (int)(_fpsCount / _fpsTimer);
            _fpsCount = 0;
            _fpsTimer = 0f;
        }

        if (_fpsText != null)
        {
            string fpsColor = _currentFps >= 60 ? "#00FF80" :
                              _currentFps >= 30 ? "#FFDD00" : "#FF4466";
            _fpsText.text = $"FPS: <color={fpsColor}>{_currentFps}</color>";
        }
    }

    // ══════════════════════════════════════════════════
    //                  UI Utilities
    // ══════════════════════════════════════════════════

    private Image CreatePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image img = obj.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return img;
    }

    private Text CreateText(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        string content, Color color, int fontSize, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Text txt = obj.AddComponent<Text>();
        txt.text = content;
        txt.font = _font;
        txt.fontSize = fontSize;
        txt.alignment = alignment;
        txt.color = color;
        txt.supportRichText = true;
        txt.raycastTarget = false;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Truncate;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return txt;
    }
}
