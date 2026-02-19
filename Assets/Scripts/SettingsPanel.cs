using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Iron Man 风格浮动设置面板
///
/// 功能：
///   - Clutch 开关（Toggle 按钮）
///   - ROS 连接开关
///   - 发送频率滑块 (10-120 Hz)
///   - 位置缩放滑块 (0.5-5.0x)
///   - ROS URL 显示
///   - 校准 / 归位按钮
///
/// 交互：
///   - 通过 PointableCanvas 支持 VR 手势戳/射线交互
///   - 左手柄 X 键 或 代码调用 TogglePanel() 显示/隐藏
///
/// 配色与 IronManHUD 统一（青蓝色全息风格）。
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("引用")]
    [SerializeField] private TeleopManager teleopManager;
    [SerializeField] private OVRCameraRig cameraRig;

    [Header("面板设置")]
    [SerializeField] private float panelDistance = 0.8f;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    // ══════════════════════════════════════════════════
    //                    颜色
    // ══════════════════════════════════════════════════

    private static readonly Color CYAN          = new Color(0f, 1f, 1f, 1f);
    private static readonly Color CYAN_DIM      = new Color(0f, 0.7f, 0.85f, 0.7f);
    private static readonly Color GREEN         = new Color(0f, 1f, 0.5f, 1f);
    private static readonly Color RED           = new Color(1f, 0.2f, 0.3f, 1f);
    private static readonly Color GOLD          = new Color(1f, 0.85f, 0f, 1f);
    private static readonly Color BG_DARK       = new Color(0.02f, 0.03f, 0.08f, 0.92f);
    private static readonly Color BG_SECTION    = new Color(0.03f, 0.05f, 0.12f, 0.6f);
    private static readonly Color BG_BUTTON     = new Color(0.05f, 0.08f, 0.18f, 0.8f);
    private static readonly Color BG_BTN_HOVER  = new Color(0f, 0.4f, 0.6f, 0.5f);
    private static readonly Color BORDER        = new Color(0f, 0.8f, 1f, 0.4f);
    private static readonly Color WHITE         = new Color(0.9f, 0.92f, 0.95f, 1f);

    // ══════════════════════════════════════════════════
    //                  内部引用
    // ══════════════════════════════════════════════════

    private Canvas _canvas;
    private Font _font;
    private bool _isVisible;

    // 控件引用
    private Text _clutchButtonText;
    private Image _clutchButtonBg;
    private Text _connectionButtonText;
    private Image _connectionButtonBg;
    private Slider _sendRateSlider;
    private Text _sendRateValue;
    private Slider _scaleSlider;
    private Text _scaleValue;
    private Text _urlText;
    private Text _statusText;

    private const float CANVAS_W = 500f;
    private const float CANVAS_H = 650f;

    // ══════════════════════════════════════════════════
    //                Unity 生命周期
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
        // X 键切换面板
        if (OVRInput.GetDown(OVRInput.Button.Three) || Input.GetKeyDown(toggleKey))
            TogglePanel();

        if (_isVisible)
        {
            UpdatePanelPosition();
            UpdateControlStates();
        }
    }

    // ══════════════════════════════════════════════════
    //                  公开接口
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
    //                  构建面板
    // ══════════════════════════════════════════════════

    private void BuildPanel()
    {
        // ─── Canvas ───
        GameObject canvasObj = new GameObject("SettingsPanel_Canvas");
        canvasObj.transform.SetParent(transform);
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = _canvas.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);
        canvasRT.localScale = Vector3.one * 0.001f;

        // ─── 添加 PointableCanvas（Meta Interaction SDK VR 交互）───
        TryAddPointableCanvas(canvasObj);

        // ─── 主背景 ───
        MakePanel(canvasObj.transform, "BG", Vector2.zero, Vector2.one, BG_DARK);

        // ─── 标题栏 ───
        MakePanel(canvasObj.transform, "TitleBar", new Vector2(0.02f, 0.90f), new Vector2(0.98f, 0.98f), BG_SECTION);
        MakeText(canvasObj.transform, "Title", new Vector2(0.05f, 0.91f), new Vector2(0.95f, 0.97f), "⚙  SETTINGS", 28, TextAnchor.MiddleLeft, CYAN);

        // ─── 边框装饰 ───
        MakeBorder(canvasObj.transform, "BorderTop", new Vector2(0f, 0.99f), new Vector2(1f, 1f));
        MakeBorder(canvasObj.transform, "BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0.01f));
        MakeBorder(canvasObj.transform, "BorderLeft", new Vector2(0f, 0f), new Vector2(0.01f, 1f));
        MakeBorder(canvasObj.transform, "BorderRight", new Vector2(0.99f, 0f), new Vector2(1f, 1f));

        // ═══ Section 1: CLUTCH ═══
        float yTop = 0.86f;
        MakeSection(canvasObj.transform, "ClutchSection", ref yTop, "⚡  CLUTCH CONTROL");
        CreateToggleButton(canvasObj.transform, "ClutchBtn",
            SectionBtnMin(yTop), SectionBtnMax(yTop),
            out _clutchButtonText, out _clutchButtonBg,
            "CLUTCH OFF",
            () => {
                if (teleopManager != null)
                    teleopManager.ClutchEngaged = !teleopManager.ClutchEngaged;
            });
        yTop -= 0.10f;

        // ═══ Section 2: CONNECTION ═══
        MakeSection(canvasObj.transform, "ConnSection", ref yTop, "●  ROS CONNECTION");
        CreateToggleButton(canvasObj.transform, "ConnBtn",
            SectionBtnMin(yTop), SectionBtnMax(yTop),
            out _connectionButtonText, out _connectionButtonBg,
            "CONNECT",
            () => {
                if (teleopManager == null || teleopManager.RosBridge == null) return;
                var ros = teleopManager.RosBridge;
                if (ros.IsConnected)
                    ros.Disconnect();
                else
                    ros.Connect();
            });
        yTop -= 0.10f;

        // ═══ Section 3: SEND RATE ═══
        MakeSection(canvasObj.transform, "RateSection", ref yTop, "↗  SEND RATE");
        CreateSlider(canvasObj.transform, "RateSlider",
            new Vector2(0.06f, yTop - 0.06f), new Vector2(0.94f, yTop - 0.02f),
            10f, 120f, 60f, out _sendRateSlider, out _sendRateValue,
            (v) => {
                if (teleopManager != null) teleopManager.SendRate = v;
                _sendRateValue.text = $"{v:F0} Hz";
            });
        yTop -= 0.10f;

        // ═══ Section 4: POSITION SCALE ═══
        MakeSection(canvasObj.transform, "ScaleSection", ref yTop, "⇄  POSITION SCALE");
        CreateSlider(canvasObj.transform, "ScaleSlider",
            new Vector2(0.06f, yTop - 0.06f), new Vector2(0.94f, yTop - 0.02f),
            0.5f, 5.0f, 2.0f, out _scaleSlider, out _scaleValue,
            (v) => {
                if (teleopManager != null) teleopManager.PositionScale = v;
                _scaleValue.text = $"{v:F1}x";
            });
        yTop -= 0.10f;

        // ═══ Section 5: QUICK ACTIONS ═══
        MakeSection(canvasObj.transform, "ActionsSection", ref yTop, "✦  QUICK ACTIONS");
        CreateActionButton(canvasObj.transform, "CalibrateBtn",
            new Vector2(0.06f, yTop - 0.07f), new Vector2(0.48f, yTop - 0.01f),
            "CALIBRATE",
            () => { if (teleopManager != null) teleopManager.Calibrate(); });

        CreateActionButton(canvasObj.transform, "HomeBtn",
            new Vector2(0.52f, yTop - 0.07f), new Vector2(0.94f, yTop - 0.01f),
            "SEND HOME",
            () => { if (teleopManager != null) teleopManager.SendHome(); });
        yTop -= 0.10f;

        // ═══ URL Display ═══
        _urlText = MakeText(canvasObj.transform, "URLText",
            new Vector2(0.05f, yTop - 0.06f), new Vector2(0.95f, yTop),
            "ROS URL: ws://...", 14, TextAnchor.MiddleLeft, CYAN_DIM);
        yTop -= 0.08f;

        // ═══ Status Text ═══
        _statusText = MakeText(canvasObj.transform, "StatusText",
            new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.08f),
            "Press X to close", 12, TextAnchor.MiddleCenter, WHITE * 0.6f);
    }

    // ══════════════════════════════════════════════════
    //              面板位置更新（跟随头部）
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

        _canvas.transform.position = Vector3.Lerp(_canvas.transform.position, targetPos, Time.deltaTime * 5f);
        _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, targetRot, Time.deltaTime * 5f);
    }

    // ══════════════════════════════════════════════════
    //                 控件状态更新
    // ══════════════════════════════════════════════════

    private void UpdateControlStates()
    {
        if (teleopManager == null) return;

        // Clutch 按钮
        if (_clutchButtonText != null && _clutchButtonBg != null)
        {
            bool engaged = teleopManager.ClutchEngaged;
            _clutchButtonText.text = engaged ? "CLUTCH ON" : "CLUTCH OFF";
            _clutchButtonBg.color = engaged ? GREEN * 0.6f : BG_BUTTON;
        }

        // 连接按钮
        if (_connectionButtonText != null && _connectionButtonBg != null && teleopManager.RosBridge != null)
        {
            var ros = teleopManager.RosBridge;
            bool connected = ros.IsConnected;
            string stateStr = ros.State.ToString().ToUpper();
            _connectionButtonText.text = connected ? "CONNECTED" : $"{stateStr}";
            _connectionButtonBg.color = connected ? GREEN * 0.6f :
                (ros.State == ROSBridgeConnection.ConnectionState.Error ? RED * 0.6f : BG_BUTTON);
        }

        // 发送频率滑块
        if (_sendRateSlider != null && _sendRateValue != null)
        {
            _sendRateSlider.SetValueWithoutNotify(teleopManager.SendRate);
            _sendRateValue.text = $"{teleopManager.SendRate:F0} Hz";
        }

        // 位置缩放滑块
        if (_scaleSlider != null && _scaleValue != null)
        {
            _scaleSlider.SetValueWithoutNotify(teleopManager.PositionScale);
            _scaleValue.text = $"{teleopManager.PositionScale:F1}x";
        }

        // URL 文本
        if (_urlText != null && teleopManager.RosBridge != null)
        {
            _urlText.text = $"<color=#006688>URL:</color> {teleopManager.RosBridge.RosbridgeUrl}";
        }
    }

    // ══════════════════════════════════════════════════
    //                 UI 构建辅助方法
    // ══════════════════════════════════════════════════

    private void MakeSection(Transform parent, string name, ref float yTop, string title)
    {
        MakeText(parent, name + "_Title", new Vector2(0.05f, yTop - 0.04f), new Vector2(0.95f, yTop), title, 16, TextAnchor.MiddleLeft, CYAN_DIM);
        yTop -= 0.05f;
    }

    private Vector2 SectionBtnMin(float yTop) => new Vector2(0.06f, yTop - 0.06f);
    private Vector2 SectionBtnMax(float yTop) => new Vector2(0.94f, yTop);

    private GameObject MakePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
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

    private void MakeBorder(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        MakePanel(parent, name, anchorMin, anchorMax, BORDER);
    }

    private Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string content, int fontSize, TextAnchor anchor, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Text txt = obj.AddComponent<Text>();
        txt.text = content;
        txt.font = _font;
        txt.fontSize = fontSize;
        txt.alignment = anchor;
        txt.color = color;
        txt.raycastTarget = false;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = Vector2.zero;
        return txt;
    }

    private void CreateToggleButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, out Text textOut, out Image bgOut, string label, System.Action onClick)
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
        txt.fontSize = 18;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = WHITE;
        RectTransform txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;

        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick?.Invoke());

        textOut = txt;
        bgOut = bg;
    }

    private void CreateActionButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, System.Action onClick)
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
        txt.fontSize = 16;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = GOLD;
        RectTransform txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;

        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }

    private void CreateSlider(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float min, float max, float initial, out Slider sliderOut, out Text valueOut, System.Action<float> onChange)
    {
        // 容器
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);
        RectTransform sliderRT = sliderObj.AddComponent<RectTransform>();
        sliderRT.anchorMin = anchorMin;
        sliderRT.anchorMax = anchorMax;
        sliderRT.sizeDelta = Vector2.zero;

        // 背景
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

        // Handle Slide Area
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

        // Value Text
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

        // Slider Component
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
    //         添加 PointableCanvas (VR 交互)
    // ══════════════════════════════════════════════════

    private void TryAddPointableCanvas(GameObject canvasObj)
    {
        try
        {
            // 反射式获取 Oculus.Interaction.UnityCanvas.PointableCanvas
            var assembly = System.Reflection.Assembly.Load("Oculus.Interaction");
            if (assembly == null) return;

            var pType = assembly.GetType("Oculus.Interaction.UnityCanvas.PointableCanvas");
            if (pType == null) return;

            // 检查是否已有该组件
            var existing = canvasObj.GetComponent(pType);
            if (existing != null) return;

            // 添加组件
            canvasObj.AddComponent(pType);
            Debug.Log("[SettingsPanel] PointableCanvas 已添加（支持 VR 手势交互）");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SettingsPanel] 无法添加 PointableCanvas: {e.Message}");
        }
    }
}
