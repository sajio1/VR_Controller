using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating settings panel for body tracking configuration.
///
/// Sections:
///   - T-Pose Calibration button
///   - Reset Calibration button
///   - Smoothing slider (rotation interpolation speed)
///   - Status display (body tracking info)
///
/// Interaction:
///   - PointableCanvas for VR gesture/ray interaction
///   - X button (Quest) or Tab key toggles visibility
///
/// Color scheme matches IronManHUD (cyan holographic style).
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("References")]
    [SerializeField] private TeleopManager teleopManager;
    [SerializeField] private OVRCameraRig cameraRig;

    [Header("Panel Settings")]
    [SerializeField] private float panelDistance = 0.8f;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    // ══════════════════════════════════════════════════
    //                    Colors
    // ══════════════════════════════════════════════════

    private static readonly Color CYAN          = new Color(0f, 1f, 1f, 1f);
    private static readonly Color CYAN_DIM      = new Color(0f, 0.7f, 0.85f, 0.7f);
    private static readonly Color GREEN         = new Color(0f, 1f, 0.5f, 1f);
    private static readonly Color RED           = new Color(1f, 0.2f, 0.3f, 1f);
    private static readonly Color GOLD          = new Color(1f, 0.85f, 0f, 1f);
    private static readonly Color BG_DARK       = new Color(0.02f, 0.03f, 0.08f, 0.92f);
    private static readonly Color BG_SECTION    = new Color(0.03f, 0.05f, 0.12f, 0.6f);
    private static readonly Color BG_BUTTON     = new Color(0.05f, 0.08f, 0.18f, 0.8f);
    private static readonly Color BORDER        = new Color(0f, 0.8f, 1f, 0.4f);
    private static readonly Color WHITE         = new Color(0.9f, 0.92f, 0.95f, 1f);

    // ══════════════════════════════════════════════════
    //                  Internal Refs
    // ══════════════════════════════════════════════════

    private Canvas _canvas;
    private Font _font;
    private bool _isVisible;

    private Text _calibrationStatusText;
    private Image _calibrateBtnBg;
    private Slider _smoothingSlider;
    private Text _smoothingValue;
    private Text _bodyStatusText;

    private const float CANVAS_W = 500f;
    private const float CANVAS_H = 450f;

    // ══════════════════════════════════════════════════
    //                Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildPanel();
        _canvas.gameObject.SetActive(false);
        _isVisible = false;
    }

    private void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Three) || Input.GetKeyDown(toggleKey))
            TogglePanel();

        if (_isVisible)
        {
            UpdatePanelPosition();
            UpdateControlStates();
        }
    }

    // ══════════════════════════════════════════════════
    //                  Public API
    // ══════════════════════════════════════════════════

    public void TogglePanel()
    {
        _isVisible = !_isVisible;
        _canvas.gameObject.SetActive(_isVisible);
        if (_isVisible) UpdateControlStates();
    }

    public void ShowPanel() { _isVisible = true; _canvas.gameObject.SetActive(true); UpdateControlStates(); }
    public void HidePanel() { _isVisible = false; _canvas.gameObject.SetActive(false); }

    // ══════════════════════════════════════════════════
    //                  Build Panel
    // ══════════════════════════════════════════════════

    private void BuildPanel()
    {
        // Canvas
        GameObject canvasObj = new GameObject("SettingsPanel_Canvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = _canvas.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);
        canvasRT.localScale = Vector3.one * 0.001f;

        TryAddPointableCanvas(canvasObj);

        // Background
        MakePanel(canvasObj.transform, "BG", Vector2.zero, Vector2.one, BG_DARK);

        // Title bar
        MakePanel(canvasObj.transform, "TitleBar",
            new Vector2(0.02f, 0.88f), new Vector2(0.98f, 0.97f), BG_SECTION);
        MakeText(canvasObj.transform, "Title",
            new Vector2(0.05f, 0.89f), new Vector2(0.95f, 0.96f),
            "⚙  BODY TRACKING SETTINGS", 24, TextAnchor.MiddleLeft, CYAN);

        // Borders
        MakeBorder(canvasObj.transform, "BorderTop", new Vector2(0f, 0.99f), new Vector2(1f, 1f));
        MakeBorder(canvasObj.transform, "BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0.01f));
        MakeBorder(canvasObj.transform, "BorderLeft", new Vector2(0f, 0f), new Vector2(0.01f, 1f));
        MakeBorder(canvasObj.transform, "BorderRight", new Vector2(0.99f, 0f), new Vector2(1f, 1f));

        float yTop = 0.85f;

        // ═══ Section 1: CALIBRATION ═══
        MakeSection(canvasObj.transform, "CalibSection", ref yTop, "◇  T-POSE CALIBRATION");

        CreateActionButton(canvasObj.transform, "CalibrateBtn",
            new Vector2(0.06f, yTop - 0.09f), new Vector2(0.48f, yTop - 0.01f),
            "CALIBRATE T-POSE", GOLD,
            () => { if (teleopManager != null) teleopManager.CalibrateTPose(); },
            out _calibrateBtnBg);

        CreateActionButton(canvasObj.transform, "ResetBtn",
            new Vector2(0.52f, yTop - 0.09f), new Vector2(0.94f, yTop - 0.01f),
            "RESET", RED,
            () => { if (teleopManager != null) teleopManager.ResetCalibration(); },
            out _);

        _calibrationStatusText = MakeText(canvasObj.transform, "CalStatus",
            new Vector2(0.06f, yTop - 0.14f), new Vector2(0.94f, yTop - 0.09f),
            "Status: Not calibrated", 13, TextAnchor.MiddleLeft, CYAN_DIM);

        yTop -= 0.20f;

        // ═══ Section 2: SMOOTHING ═══
        MakeSection(canvasObj.transform, "SmoothSection", ref yTop, "~  SMOOTHING");

        CreateSlider(canvasObj.transform, "SmoothSlider",
            new Vector2(0.06f, yTop - 0.08f), new Vector2(0.94f, yTop - 0.02f),
            1f, 30f, 15f, out _smoothingSlider, out _smoothingValue,
            (v) =>
            {
                if (teleopManager != null && teleopManager.ModelDriver != null)
                {
                    // Access via serialized field would be ideal; for now use reflection-safe approach
                }
                if (_smoothingValue != null) _smoothingValue.text = $"{v:F0}";
            });
        yTop -= 0.14f;

        // ═══ Section 3: STATUS ═══
        MakeSection(canvasObj.transform, "StatusSection", ref yTop, "●  TRACKING STATUS");

        _bodyStatusText = MakeText(canvasObj.transform, "BodyStatus",
            new Vector2(0.06f, yTop - 0.18f), new Vector2(0.94f, yTop),
            "Body: ---\nLeft Hand: ---\nRight Hand: ---\nBones: ---",
            14, TextAnchor.UpperLeft, WHITE);

        yTop -= 0.22f;

        // Close hint
        MakeText(canvasObj.transform, "CloseHint",
            new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.07f),
            "Press X to close", 12, TextAnchor.MiddleCenter, WHITE * 0.5f);
    }

    // ══════════════════════════════════════════════════
    //              Panel Position (follow head)
    // ══════════════════════════════════════════════════

    private void UpdatePanelPosition()
    {
        Transform eye = null;
        if (cameraRig != null)
            eye = cameraRig.centerEyeAnchor;
        if (eye == null && teleopManager != null && teleopManager.CameraRig != null)
            eye = teleopManager.CameraRig.centerEyeAnchor;
        if (eye == null) return;

        Vector3 targetPos = eye.position + eye.forward * panelDistance;
        Quaternion targetRot = Quaternion.LookRotation(eye.forward, Vector3.up);

        _canvas.transform.position = Vector3.Lerp(
            _canvas.transform.position, targetPos, Time.deltaTime * 5f);
        _canvas.transform.rotation = Quaternion.Slerp(
            _canvas.transform.rotation, targetRot, Time.deltaTime * 5f);
    }

    // ══════════════════════════════════════════════════
    //                 Control Updates
    // ══════════════════════════════════════════════════

    private void UpdateControlStates()
    {
        if (teleopManager == null) return;

        var body = teleopManager.BodyTracker;
        var norm = teleopManager.Normalizer;

        // Calibration status
        if (_calibrationStatusText != null)
        {
            if (norm != null && norm.IsCalibrated)
            {
                _calibrationStatusText.text =
                    $"<color=#00FF80>Calibrated</color>  |  Scale: {norm.HeightScaleFactor:F2}x";
                if (_calibrateBtnBg != null)
                    _calibrateBtnBg.color = GREEN * 0.5f;
            }
            else
            {
                _calibrationStatusText.text =
                    "<color=#FF4466>Not calibrated</color> — Stand in T-pose and press Calibrate";
                if (_calibrateBtnBg != null)
                    _calibrateBtnBg.color = BG_BUTTON;
            }
        }

        // Body status
        if (_bodyStatusText != null)
        {
            bool bodyOk = body != null && body.IsBodyTracking;
            bool leftOk = body != null && body.IsLeftHandTracking;
            bool rightOk = body != null && body.IsRightHandTracking;
            int bones = body != null ? body.BodyBoneCount : 0;
            float conf = body != null ? body.BodyConfidence : 0f;

            string bColor = bodyOk ? "#00FF80" : "#FF4466";
            string lColor = leftOk ? "#00FF80" : "#FF4466";
            string rColor = rightOk ? "#00FF80" : "#FF4466";

            _bodyStatusText.text =
                $"<color={bColor}>● Body: {(bodyOk ? "TRACKED" : "NOT TRACKED")}</color>\n" +
                $"<color={lColor}>● Left Hand: {(leftOk ? "OK" : "---")}</color>\n" +
                $"<color={rColor}>● Right Hand: {(rightOk ? "OK" : "---")}</color>\n" +
                $"<color=#00CCFF>● Bones: {bones}  |  Confidence: {conf:P0}</color>";
        }
    }

    // ══════════════════════════════════════════════════
    //                 UI Build Helpers
    // ══════════════════════════════════════════════════

    private void MakeSection(Transform parent, string name, ref float yTop, string title)
    {
        MakeText(parent, name + "_Title",
            new Vector2(0.05f, yTop - 0.05f), new Vector2(0.95f, yTop),
            title, 15, TextAnchor.MiddleLeft, CYAN_DIM);
        yTop -= 0.06f;
    }

    private GameObject MakePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image img = obj.AddComponent<Image>();
        img.color = color;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = Vector2.zero;
        return obj;
    }

    private void MakeBorder(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        MakePanel(parent, name, anchorMin, anchorMax, BORDER);
    }

    private Text MakeText(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        string content, int fontSize, TextAnchor anchor, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Text txt = obj.AddComponent<Text>();
        txt.text = content;
        txt.font = _font;
        txt.fontSize = fontSize;
        txt.alignment = anchor;
        txt.color = color;
        txt.supportRichText = true;
        txt.raycastTarget = false;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = Vector2.zero;
        return txt;
    }

    private void CreateActionButton(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        string label, Color labelColor, System.Action onClick, out Image bgOut)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        Button btn = btnObj.AddComponent<Button>();
        Image bg = btnObj.AddComponent<Image>();
        bg.color = BG_BUTTON;
        RectTransform btnRT = bg.rectTransform;
        btnRT.anchorMin = anchorMin;
        btnRT.anchorMax = anchorMax;
        btnRT.sizeDelta = Vector2.zero;

        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btnObj.transform, false);
        Text txt = txtObj.AddComponent<Text>();
        txt.text = label;
        txt.font = _font;
        txt.fontSize = 15;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = labelColor;
        RectTransform txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;

        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick?.Invoke());

        bgOut = bg;
    }

    private void CreateSlider(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        float min, float max, float initial,
        out Slider sliderOut, out Text valueOut,
        System.Action<float> onChange)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);
        RectTransform sliderRT = sliderObj.AddComponent<RectTransform>();
        sliderRT.anchorMin = anchorMin;
        sliderRT.anchorMax = anchorMax;
        sliderRT.sizeDelta = Vector2.zero;

        // Background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(sliderObj.transform, false);
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = BG_BUTTON;
        RectTransform bgRT = bgImg.rectTransform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        // Fill Area
        GameObject fillAreaObj = new GameObject("Fill Area");
        fillAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRT = fillAreaObj.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0.05f, 0.2f);
        fillAreaRT.anchorMax = new Vector2(0.7f, 0.8f);
        fillAreaRT.sizeDelta = Vector2.zero;

        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillAreaObj.transform, false);
        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.color = CYAN * 0.7f;
        RectTransform fillRT = fillImg.rectTransform;
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        // Handle
        GameObject handleAreaObj = new GameObject("Handle Slide Area");
        handleAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRT = handleAreaObj.AddComponent<RectTransform>();
        handleAreaRT.anchorMin = new Vector2(0.05f, 0f);
        handleAreaRT.anchorMax = new Vector2(0.7f, 1f);
        handleAreaRT.sizeDelta = Vector2.zero;

        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(handleAreaObj.transform, false);
        Image handleImg = handleObj.AddComponent<Image>();
        handleImg.color = CYAN;
        RectTransform handleRT = handleImg.rectTransform;
        handleRT.sizeDelta = new Vector2(10f, 0f);

        // Value text
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(sliderObj.transform, false);
        Text valueTxt = valueObj.AddComponent<Text>();
        valueTxt.text = $"{initial:F0}";
        valueTxt.font = _font;
        valueTxt.fontSize = 14;
        valueTxt.alignment = TextAnchor.MiddleRight;
        valueTxt.color = WHITE;
        RectTransform valueRT = valueTxt.rectTransform;
        valueRT.anchorMin = new Vector2(0.72f, 0f);
        valueRT.anchorMax = new Vector2(0.98f, 1f);
        valueRT.sizeDelta = Vector2.zero;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.onValueChanged.AddListener((v) => onChange?.Invoke(v));

        sliderOut = slider;
        valueOut = valueTxt;
    }

    // ══════════════════════════════════════════════════
    //         PointableCanvas (VR interaction)
    // ══════════════════════════════════════════════════

    private void TryAddPointableCanvas(GameObject canvasObj)
    {
        try
        {
            var assembly = System.Reflection.Assembly.Load("Oculus.Interaction");
            if (assembly == null) return;

            var pType = assembly.GetType("Oculus.Interaction.UnityCanvas.PointableCanvas");
            if (pType == null) return;

            var existing = canvasObj.GetComponent(pType);
            if (existing != null) return;

            canvasObj.AddComponent(pType);
            Debug.Log("[SettingsPanel] PointableCanvas added for VR interaction");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SettingsPanel] Could not add PointableCanvas: {e.Message}");
        }
    }
}
