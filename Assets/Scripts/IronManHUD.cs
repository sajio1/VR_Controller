using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Iron Man 风格 HUD — VR 遥操作主界面
///
/// 布局：
///   ┌──────────────────────────────────────────────────────┐
///   │  ◆ STARK TELEOP SYSTEM v2.0            [FPS] [TIME] │
///   ├────────────┬──────────────────────┬─────────────────┤
///   │  LEFT HAND │     CAMERA FEED      │   RIGHT HAND    │
///   │  ┌──────┐  │  ┌────────────────┐  │  ┌───────────┐  │
///   │  │skel  │  │  │                │  │  │   skel    │  │
///   │  │viz   │  │  │  [placeholder] │  │  │   viz     │  │
///   │  └──────┘  │  └────────────────┘  │  └───────────┘  │
///   │  Grip 0.85 │                      │  Grip 0.92      │
///   ├────────────┴──────────────────────┴─────────────────┤
///   │ ● ROS: Connected │ ⚡ Clutch: ON │ 60Hz │ Scale 2x  │
///   └──────────────────────────────────────────────────────┘
///
/// 特性：
///   - 青蓝色全息配色方案
///   - 角落装饰支架 + 扫描线动画
///   - 脉冲状态指示灯
///   - 平滑跟随头部视线
///   - 自动创建 PerEyeHand 材质并分配给手部网格
/// </summary>
public class IronManHUD : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("引用")]
    [SerializeField] private TeleopManager teleopManager;

    [Header("HUD 设置")]
    [Tooltip("HUD 距头部距离 (m)")]
    [SerializeField] private float hudDistance = 1.2f;
    [Tooltip("跟随平滑速度")]
    [SerializeField] private float followSmoothSpeed = 3f;

    // ══════════════════════════════════════════════════
    //                    颜色方案
    // ══════════════════════════════════════════════════

    // 主色调
    private static readonly Color CYAN          = new Color(0.00f, 1.00f, 1.00f, 1.0f);
    private static readonly Color CYAN_DIM      = new Color(0.00f, 0.75f, 0.90f, 0.6f);
    private static readonly Color BLUE          = new Color(0.00f, 0.50f, 1.00f, 1.0f);
    private static readonly Color GOLD          = new Color(1.00f, 0.85f, 0.00f, 1.0f);
    private static readonly Color GREEN         = new Color(0.00f, 1.00f, 0.50f, 1.0f);
    private static readonly Color RED           = new Color(1.00f, 0.20f, 0.30f, 1.0f);
    private static readonly Color WHITE_DIM     = new Color(0.85f, 0.90f, 0.95f, 0.9f);

    // 背景
    private static readonly Color BG_DARK       = new Color(0.02f, 0.03f, 0.08f, 0.88f);
    private static readonly Color BG_PANEL      = new Color(0.03f, 0.05f, 0.12f, 0.80f);
    private static readonly Color BG_STATUS     = new Color(0.02f, 0.02f, 0.06f, 0.92f);
    private static readonly Color BORDER        = new Color(0.00f, 0.80f, 1.00f, 0.35f);
    private static readonly Color BORDER_BRIGHT = new Color(0.00f, 0.90f, 1.00f, 0.70f);

    // ══════════════════════════════════════════════════
    //                   HUD 尺寸
    // ══════════════════════════════════════════════════

    private const float CANVAS_W = 1400f;
    private const float CANVAS_H = 820f;
    private const float CANVAS_SCALE = 0.001f; // 1px = 1mm → 1.4m × 0.82m

    // ══════════════════════════════════════════════════
    //                  内部引用
    // ══════════════════════════════════════════════════

    private Canvas _canvas;
    private Font _font;

    // UI 元素
    private Text _titleText;
    private Text _timeText;
    private Text _fpsText;
    private Text _leftHandLabel;
    private Text _leftHandData;
    private Text _rightHandLabel;
    private Text _rightHandData;
    private Text _cameraPlaceholder;
    private Text _statusText;
    private Image _scanLine;
    private Image _rosIndicator;
    private Image _clutchIndicator;

    // 骨骼可视化（已移除）
    // private HandSkeletonVisualizer _leftSkeletonViz;
    // private HandSkeletonVisualizer _rightSkeletonViz;

    // HUD 跟随
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private bool _positionInitialized;

    // FPS 计算
    private float _fpsTimer;
    private int _fpsCount;
    private int _currentFps;

    // 角落装饰
    private Image[] _cornerBrackets;

    // PerEyeHand 材质
    private Material _leftEyeMat;
    private Material _rightEyeMat;

    // ══════════════════════════════════════════════════
    //             Proxy Hand Rendering
    // ══════════════════════════════════════════════════

    // Proxy Hand 组件
    private HandUIProxy _leftProxy;
    private HandUIProxy _rightProxy;

    // Proxy Hand 专用相机
    private Camera _leftHandCam;
    private Camera _rightHandCam;

    // RenderTexture
    private RenderTexture _leftHandRT;
    private RenderTexture _rightHandRT;

    // UI 显示
    private RawImage _leftHandRawImage;
    private RawImage _rightHandRawImage;

    // Layer 设置
    private const int LAYER_HAND_UI_LEFT = 6;   // 需要在 Unity 中手动添加
    private const int LAYER_HAND_UI_RIGHT = 7;  // 需要在 Unity 中手动添加

    // RenderTexture 尺寸
    private const int HAND_RT_SIZE = 512;

    // ══════════════════════════════════════════════════
    //                Unity 生命周期
    // ══════════════════════════════════════════════════

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        BuildHUD();
        SetupPerEyeHandMaterials();
        
        // 延迟创建 Proxy Hands（等待 OVRSkeleton 初始化）
        Invoke(nameof(CreateProxyHands), 0.5f);
    }

    private bool _layersExcluded = false;
    
    private void Update()
    {
        UpdateHUDPosition();
        UpdateStatusBar();
        UpdateHandPanels();
        UpdateScanLineAnimation();
        UpdatePulsingEffects();
        UpdateFPS();
        UpdateProxyCameras();  // 让相机跟随手腕
        
        // 持续确保主相机排除 Proxy Layer（VR 相机可能动态变化）
        if (!_layersExcluded && (_leftProxy != null || _rightProxy != null))
        {
            ExcludeProxyLayersFromMainCamera();
            _layersExcluded = true;
        }
    }

    private void OnDestroy()
    {
        if (_leftEyeMat != null) Destroy(_leftEyeMat);
        if (_rightEyeMat != null) Destroy(_rightEyeMat);
        
        // 清理 RenderTexture
        if (_leftHandRT != null)
        {
            _leftHandRT.Release();
            Destroy(_leftHandRT);
        }
        if (_rightHandRT != null)
        {
            _rightHandRT.Release();
            Destroy(_rightHandRT);
        }
    }

    // ══════════════════════════════════════════════════
    //                  构建 HUD
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

        // ─── 主背景 ───
        CreatePanel(canvasObj.transform, "MainBG",
            Vector2.zero, Vector2.one, BG_DARK);

        // ─── 外框发光边线 ───
        CreateBorder(canvasObj.transform, "OuterBorder", BORDER_BRIGHT, 2f);

        // ─── 内框线 ───
        CreateBorder(canvasObj.transform, "InnerBorder", BORDER, 1f, 4f);

        // ─── 角落装饰支架 ───
        CreateCornerBrackets(canvasObj.transform);

        // ─── 标题栏 (顶部 6%) ───
        BuildTitleBar(canvasObj.transform);

        // ─── 左手面板 ───
        BuildHandPanel(canvasObj.transform, true,
            new Vector2(0.01f, 0.12f), new Vector2(0.20f, 0.92f));

        // ─── 摄像头区域 ───
        BuildCameraArea(canvasObj.transform,
            new Vector2(0.21f, 0.12f), new Vector2(0.79f, 0.92f));

        // ─── 右手面板 ───
        BuildHandPanel(canvasObj.transform, false,
            new Vector2(0.80f, 0.12f), new Vector2(0.99f, 0.92f));

        // ─── 底部状态栏 ───
        BuildStatusBar(canvasObj.transform);

        // ─── 扫描线 ───
        CreateScanLine(canvasObj.transform);
    }

    // ──────────── 标题栏 ────────────

    private void BuildTitleBar(Transform parent)
    {
        // 标题背景
        Image titleBg = CreatePanel(parent, "TitleBar",
            new Vector2(0.01f, 0.93f), new Vector2(0.99f, 0.99f),
            new Color(0.01f, 0.02f, 0.06f, 0.95f));

        // 装饰横线
        CreatePanel(parent, "TitleLine",
            new Vector2(0.01f, 0.925f), new Vector2(0.99f, 0.932f),
            BORDER);

        // 标题文字
        _titleText = CreateText(titleBg.transform, "Title",
            new Vector2(0.02f, 0f), new Vector2(0.65f, 1f),
            "◆  S H A D O W   M O D E",
            CYAN, 24, TextAnchor.MiddleLeft);

        // FPS 显示
        _fpsText = CreateText(titleBg.transform, "FPS",
            new Vector2(0.68f, 0f), new Vector2(0.82f, 1f),
            "FPS: --", CYAN_DIM, 18, TextAnchor.MiddleRight);

        // 时间
        _timeText = CreateText(titleBg.transform, "Time",
            new Vector2(0.84f, 0f), new Vector2(0.98f, 1f),
            "00:00:00", CYAN_DIM, 18, TextAnchor.MiddleRight);
    }

    // ──────────── 手部面板 ────────────

    private void BuildHandPanel(Transform parent, bool isLeft,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        string side = isLeft ? "Left" : "Right";

        // 面板背景
        Image panelBg = CreatePanel(parent, $"{side}HandPanel",
            anchorMin, anchorMax, BG_PANEL);

        // 面板边框
        CreatePanelBorder(panelBg.transform, BORDER);

        // ── 标题区 ──
        Image headerBg = CreatePanel(panelBg.transform, "Header",
            new Vector2(0f, 0.88f), new Vector2(1f, 1f),
            new Color(0f, 0.6f, 1f, 0.15f));

        Text label = CreateText(headerBg.transform, "Label",
            new Vector2(0.08f, 0f), new Vector2(0.92f, 1f),
            isLeft ? "◁  LEFT HAND" : "RIGHT HAND  ▷",
            CYAN, 30, isLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);  // 字体放大 1.5x

        if (isLeft) _leftHandLabel = label;
        else _rightHandLabel = label;

        // ── 手部渲染区域 ──
        Image skeletonArea = CreatePanel(panelBg.transform, "SkeletonArea",
            new Vector2(0.05f, 0.25f), new Vector2(0.95f, 0.85f),
            new Color(0f, 0.5f, 0.8f, 0.05f));

        // 手部 RawImage（用于显示 Proxy Hand 的 RenderTexture）
        GameObject rawImgObj = new GameObject("HandRawImage");
        rawImgObj.transform.SetParent(skeletonArea.transform, false);
        RawImage rawImg = rawImgObj.AddComponent<RawImage>();
        rawImg.color = Color.white;
        rawImg.raycastTarget = false;
        RectTransform rawRT = rawImg.rectTransform;
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.offsetMin = Vector2.zero;
        rawRT.offsetMax = Vector2.zero;

        if (isLeft) _leftHandRawImage = rawImg;
        else _rightHandRawImage = rawImg;

        // 骨骼区域十字准星
        CreatePanel(panelBg.transform, "CrossH",
            new Vector2(0.1f, 0.545f), new Vector2(0.9f, 0.555f),
            new Color(0f, 0.8f, 1f, 0.08f));
        CreatePanel(panelBg.transform, "CrossV",
            new Vector2(0.495f, 0.28f), new Vector2(0.505f, 0.82f),
            new Color(0f, 0.8f, 1f, 0.08f));

        // ── 数据区 ──
        Text data = CreateText(panelBg.transform, "Data",
            new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.25f),
            "Tracking: ---\nGrip: ---\nMode: ---",
            WHITE_DIM, 24, TextAnchor.UpperLeft);  // 字体放大 1.5x

        if (isLeft) _leftHandData = data;
        else _rightHandData = data;
    }

    // ──────────── 摄像头区域 ────────────

    private void BuildCameraArea(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        Image camBg = CreatePanel(parent, "CameraPanel",
            anchorMin, anchorMax, new Color(0.05f, 0.05f, 0.08f, 0.85f));

        CreatePanelBorder(camBg.transform, BORDER);

        // 标题
        CreateText(camBg.transform, "CamTitle",
            new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.98f),
            "▣  CAMERA FEED",
            CYAN_DIM, 16, TextAnchor.MiddleLeft);

        // 网格线（装饰）
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

        // 十字准星
        CreatePanel(camBg.transform, "CrossH",
            new Vector2(0.35f, 0.495f), new Vector2(0.65f, 0.505f),
            new Color(0f, 1f, 1f, 0.12f));
        CreatePanel(camBg.transform, "CrossV",
            new Vector2(0.498f, 0.30f), new Vector2(0.502f, 0.70f),
            new Color(0f, 1f, 1f, 0.12f));

        // 占位文字
        _cameraPlaceholder = CreateText(camBg.transform, "Placeholder",
            new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.75f),
            "<color=#006688>NO SIGNAL</color>\n\n<size=14><color=#004466>Camera feed placeholder\nAwaiting connection...</color></size>",
            WHITE_DIM, 28, TextAnchor.MiddleCenter);

        // 底部信息
        CreateText(camBg.transform, "CamInfo",
            new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.10f),
            "<color=#005577>RES: --- × ---  │  CODEC: ---  │  LATENCY: ---ms</color>",
            CYAN_DIM, 12, TextAnchor.MiddleCenter);
    }

    // ──────────── 状态栏 ────────────

    private void BuildStatusBar(Transform parent)
    {
        // 背景
        Image statusBg = CreatePanel(parent, "StatusBar",
            new Vector2(0.01f, 0.015f), new Vector2(0.99f, 0.105f),
            BG_STATUS);

        // 顶部装饰线
        CreatePanel(parent, "StatusLine",
            new Vector2(0.01f, 0.105f), new Vector2(0.99f, 0.112f),
            BORDER);

        // ROS 指示灯
        _rosIndicator = CreatePanel(statusBg.transform, "RosDot",
            new Vector2(0.015f, 0.25f), new Vector2(0.025f, 0.75f),
            GREEN).GetComponent<Image>();
        // 强制方形
        var dotRT = _rosIndicator.rectTransform;
        dotRT.anchorMin = new Vector2(0.015f, 0.25f);
        dotRT.anchorMax = new Vector2(0.028f, 0.75f);

        // Clutch 指示灯
        _clutchIndicator = CreatePanel(statusBg.transform, "ClutchDot",
            new Vector2(0.28f, 0.25f), new Vector2(0.293f, 0.75f),
            RED).GetComponent<Image>();

        // 状态文字
        _statusText = CreateText(statusBg.transform, "StatusText",
            new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f),
            "INITIALIZING...",
            WHITE_DIM, 17, TextAnchor.MiddleLeft);
    }

    // ══════════════════════════════════════════════════
    //                  装饰元素
    // ══════════════════════════════════════════════════

    private void CreateCornerBrackets(Transform parent)
    {
        _cornerBrackets = new Image[8]; // 4 角 × 2 线
        float size = 40f / CANVAS_W;   // 40px 长度
        float thick = 3f / CANVAS_H;   // 3px 粗

        // 左上角
        _cornerBrackets[0] = CreatePanel(parent, "BracketTL_H",
            new Vector2(0.005f, 1f - 0.005f - thick),
            new Vector2(0.005f + size, 1f - 0.005f), CYAN).GetComponent<Image>();
        _cornerBrackets[1] = CreatePanel(parent, "BracketTL_V",
            new Vector2(0.005f, 1f - 0.005f - size),
            new Vector2(0.005f + thick * (CANVAS_H / CANVAS_W), 1f - 0.005f), CYAN).GetComponent<Image>();

        // 右上角
        _cornerBrackets[2] = CreatePanel(parent, "BracketTR_H",
            new Vector2(1f - 0.005f - size, 1f - 0.005f - thick),
            new Vector2(1f - 0.005f, 1f - 0.005f), CYAN).GetComponent<Image>();
        _cornerBrackets[3] = CreatePanel(parent, "BracketTR_V",
            new Vector2(1f - 0.005f - thick * (CANVAS_H / CANVAS_W), 1f - 0.005f - size),
            new Vector2(1f - 0.005f, 1f - 0.005f), CYAN).GetComponent<Image>();

        // 左下角
        _cornerBrackets[4] = CreatePanel(parent, "BracketBL_H",
            new Vector2(0.005f, 0.005f),
            new Vector2(0.005f + size, 0.005f + thick), CYAN).GetComponent<Image>();
        _cornerBrackets[5] = CreatePanel(parent, "BracketBL_V",
            new Vector2(0.005f, 0.005f),
            new Vector2(0.005f + thick * (CANVAS_H / CANVAS_W), 0.005f + size), CYAN).GetComponent<Image>();

        // 右下角
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

        // Top
        CreatePanel(parent, $"{name}_T",
            new Vector2(ix, 1f - iy - ty), new Vector2(1f - ix, 1f - iy), color);
        // Bottom
        CreatePanel(parent, $"{name}_B",
            new Vector2(ix, iy), new Vector2(1f - ix, iy + ty), color);
        // Left
        CreatePanel(parent, $"{name}_L",
            new Vector2(ix, iy), new Vector2(ix + tx, 1f - iy), color);
        // Right
        CreatePanel(parent, $"{name}_R",
            new Vector2(1f - ix - tx, iy), new Vector2(1f - ix, 1f - iy), color);
    }

    private void CreatePanelBorder(Transform parent, Color color)
    {
        float t = 1f / 200f; // 1px 边框
        CreatePanel(parent, "Border_T", new Vector2(0, 1 - t), Vector2.one, color);
        CreatePanel(parent, "Border_B", Vector2.zero, new Vector2(1, t), color);
        CreatePanel(parent, "Border_L", Vector2.zero, new Vector2(t, 1), color);
        CreatePanel(parent, "Border_R", new Vector2(1 - t, 0), Vector2.one, color);
    }

    // ══════════════════════════════════════════════════
    //              Proxy Hand 系统
    // ══════════════════════════════════════════════════

    /// <summary>
    /// 创建代理手渲染系统
    /// </summary>
    private void CreateProxyHands()
    {
        if (teleopManager == null || teleopManager.HandController == null)
        {
            Debug.LogWarning("[IronManHUD] TeleopManager or HandController not available, retrying...");
            Invoke(nameof(CreateProxyHands), 0.5f);
            return;
        }

        var handController = teleopManager.HandController;

        // 创建 RenderTextures
        _leftHandRT = new RenderTexture(HAND_RT_SIZE, HAND_RT_SIZE, 16, RenderTextureFormat.ARGB32);
        _leftHandRT.name = "LeftHandRT";
        _leftHandRT.Create();

        _rightHandRT = new RenderTexture(HAND_RT_SIZE, HAND_RT_SIZE, 16, RenderTextureFormat.ARGB32);
        _rightHandRT.name = "RightHandRT";
        _rightHandRT.Create();

        // 绑定 RenderTexture 到 RawImage
        // 交换：左手渲染结果显示在右侧面板，右手显示在左侧面板（修复镜像问题）
        if (_leftHandRawImage != null)
            _leftHandRawImage.texture = _rightHandRT;  // 左侧面板显示右手
        if (_rightHandRawImage != null)
            _rightHandRawImage.texture = _leftHandRT;  // 右侧面板显示左手

        // 创建左手 Proxy
        CreateSingleProxyHand(true, handController);

        // 创建右手 Proxy
        CreateSingleProxyHand(false, handController);

        // 关键：让主相机排除 Proxy Hand 的 Layer，防止粉色重影
        ExcludeProxyLayersFromMainCamera();

        Debug.Log("[IronManHUD] Proxy hand system initialized");
    }

    /// <summary>
    /// 让主相机排除 Proxy Hand 的 Layer，防止重影
    /// </summary>
    private void ExcludeProxyLayersFromMainCamera()
    {
        // 方法1：通过 OVRCameraRig 找相机
        if (teleopManager != null && teleopManager.CameraRig != null)
        {
            var rig = teleopManager.CameraRig;
            Camera leftEye = rig.leftEyeAnchor?.GetComponent<Camera>();
            Camera rightEye = rig.rightEyeAnchor?.GetComponent<Camera>();
            Camera centerEye = rig.centerEyeAnchor?.GetComponent<Camera>();
            
            int excludeMask = ~((1 << LAYER_HAND_UI_LEFT) | (1 << LAYER_HAND_UI_RIGHT));
            
            if (leftEye != null)
            {
                leftEye.cullingMask &= excludeMask;
                Debug.Log($"[IronManHUD] Excluded proxy layers from: {leftEye.name}");
            }
            if (rightEye != null)
            {
                rightEye.cullingMask &= excludeMask;
                Debug.Log($"[IronManHUD] Excluded proxy layers from: {rightEye.name}");
            }
            if (centerEye != null)
            {
                centerEye.cullingMask &= excludeMask;
                Debug.Log($"[IronManHUD] Excluded proxy layers from: {centerEye.name}");
            }
        }
        
        // 方法2：遍历所有相机作为备用
        Camera[] allCameras = Camera.allCameras;
        foreach (var cam in allCameras)
        {
            // 跳过我们自己创建的 Proxy 相机
            if (cam == _leftHandCam || cam == _rightHandCam) continue;
            
            // 排除 Layer 6 和 7
            cam.cullingMask &= ~(1 << LAYER_HAND_UI_LEFT);
            cam.cullingMask &= ~(1 << LAYER_HAND_UI_RIGHT);
            Debug.Log($"[IronManHUD] Excluded proxy layers from camera: {cam.name}");
        }
        
        // 方法3：找 Main Camera tag
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam != _leftHandCam && mainCam != _rightHandCam)
        {
            mainCam.cullingMask &= ~(1 << LAYER_HAND_UI_LEFT);
            mainCam.cullingMask &= ~(1 << LAYER_HAND_UI_RIGHT);
            Debug.Log($"[IronManHUD] Excluded proxy layers from Main Camera: {mainCam.name}");
        }
    }

    private void CreateSingleProxyHand(bool isLeft, HandTrackingController handController)
    {
        string side = isLeft ? "Left" : "Right";
        int layer = isLeft ? LAYER_HAND_UI_LEFT : LAYER_HAND_UI_RIGHT;
        RenderTexture rt = isLeft ? _leftHandRT : _rightHandRT;

        // 获取源骨骼
        OVRSkeleton sourceSkeleton = handController.GetSkeleton(isLeft);
        if (sourceSkeleton == null)
        {
            Debug.LogWarning($"[IronManHUD] {side} OVRSkeleton not found");
            return;
        }

        // 创建 Proxy 容器
        GameObject proxyContainer = new GameObject($"{side}HandProxyContainer");
        proxyContainer.transform.SetParent(transform, false);
        proxyContainer.transform.localPosition = new Vector3(isLeft ? -2f : 2f, 0f, 5f);
        proxyContainer.transform.localRotation = Quaternion.Euler(0f, isLeft ? 45f : -45f, 0f);

        // 添加 HandUIProxy 组件
        HandUIProxy proxy = proxyContainer.AddComponent<HandUIProxy>();
        proxy.SetSource(sourceSkeleton, handController.GetMesh(isLeft));
        proxy.SetLayer(layer);

        // 创建材质（半透明全息效果）
        Material proxyMat = CreateProxyHandMaterial(isLeft);
        proxy.SetMaterial(proxyMat);

        if (isLeft) _leftProxy = proxy;
        else _rightProxy = proxy;

        // 创建专用相机
        CreateProxyCamera(isLeft, proxyContainer.transform, rt, layer);
    }

    private Material CreateProxyHandMaterial(bool isLeft)
    {
        // 尝试使用 PerEyeHand shader
        Shader shader = Shader.Find("Custom/PerEyeHand");
        
        if (shader != null)
        {
            Debug.Log($"[IronManHUD] Found Custom/PerEyeHand shader");
            Material mat = new Material(shader);
            mat.name = isLeft ? "LeftProxyHand_Mat" : "RightProxyHand_Mat";
            
            // 内部填充色
            mat.SetColor("_BaseColor", new Color(0.02f, 0.06f, 0.15f, 0.15f));
            // 边缘发光
            mat.SetColor("_EdgeColor", new Color(0f, 1f, 1f, 1f));
            mat.SetFloat("_EdgePower", 1.5f);
            mat.SetFloat("_EdgeIntensity", 4.0f);
            mat.SetFloat("_EdgeWidth", 0.5f);
            mat.renderQueue = 3000;
            return mat;
        }
        
        // Fallback: 使用 URP Lit shader（支持 Emission 发光）
        Debug.LogWarning("[IronManHUD] Custom/PerEyeHand not found, using URP Lit with emission");
        shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        }
        if (shader == null)
        {
            Debug.LogError("[IronManHUD] No suitable shader found!");
            shader = Shader.Find("Standard");
        }

        Material litMat = new Material(shader);
        litMat.name = isLeft ? "LeftProxyHand_Lit" : "RightProxyHand_Lit";

        // 设置为透明模式
        litMat.SetFloat("_Surface", 1); // 1 = Transparent
        litMat.SetFloat("_Blend", 0);   // 0 = Alpha
        litMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        litMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        litMat.SetInt("_ZWrite", 0);
        litMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        litMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        litMat.renderQueue = 3000;

        // 基础颜色 - 更亮的青色
        Color baseColor = new Color(0.2f, 0.8f, 1f, 0.5f);
        litMat.SetColor("_BaseColor", baseColor);
        
        // 发光 (Emission) - 强烈的青色发光
        litMat.EnableKeyword("_EMISSION");
        litMat.SetColor("_EmissionColor", new Color(0f, 1f, 1f, 1f) * 5f); // 更强的 HDR 发光
        litMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        // 光滑度和金属度
        litMat.SetFloat("_Smoothness", 0.9f);
        litMat.SetFloat("_Metallic", 0.3f);

        return litMat;
    }

    private void CreateProxyCamera(bool isLeft, Transform proxyContainer, RenderTexture rt, int layer)
    {
        string side = isLeft ? "Left" : "Right";

        GameObject camObj = new GameObject($"{side}HandCamera");
        camObj.transform.SetParent(proxyContainer, false);
        // 相机位置：在手的正上方偏后，俯视手部
        camObj.transform.localPosition = new Vector3(0f, 0.15f, -0.3f);
        camObj.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);

        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透明背景
        cam.cullingMask = 1 << layer; // 只渲染对应 Layer
        cam.orthographic = true;
        cam.orthographicSize = 0.11f;  // 进一步缩小视野 = 手部再大 1.5 倍
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 2f;
        cam.targetTexture = rt;
        cam.depth = -10; // 比主相机低

        // 禁用音频监听
        AudioListener listener = camObj.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);

        if (isLeft) _leftHandCam = cam;
        else _rightHandCam = cam;

        Debug.Log($"[IronManHUD] {side} proxy camera created, culling mask: {cam.cullingMask}");
    }

    /// <summary>
    /// 让代理相机跟随手腕位置，实现"手在 UI 中相对固定"的效果
    /// </summary>
    private void UpdateProxyCameras()
    {
        // 左手相机跟随左手腕
        if (_leftHandCam != null && _leftProxy != null && _leftProxy.IsInitialized && _leftProxy.WristBone != null)
        {
            Transform wrist = _leftProxy.WristBone;
            // 相机位置：手腕正上方偏后
            _leftHandCam.transform.position = wrist.position + wrist.up * 0.12f + wrist.forward * -0.15f;
            // 相机朝向手腕
            _leftHandCam.transform.LookAt(wrist.position, Vector3.up);
        }

        // 右手相机跟随右手腕
        if (_rightHandCam != null && _rightProxy != null && _rightProxy.IsInitialized && _rightProxy.WristBone != null)
        {
            Transform wrist = _rightProxy.WristBone;
            // 相机位置：手腕正上方偏后
            _rightHandCam.transform.position = wrist.position + wrist.up * 0.12f + wrist.forward * -0.15f;
            // 相机朝向手腕
            _rightHandCam.transform.LookAt(wrist.position, Vector3.up);
        }
    }

    // ══════════════════════════════════════════════════
    //          PerEyeHand 材质创建与分配
    // ══════════════════════════════════════════════════

    private void SetupPerEyeHandMaterials()
    {
        Shader perEyeShader = Shader.Find("Custom/PerEyeHand");
        if (perEyeShader == null)
        {
            Debug.LogWarning("[IronManHUD] PerEyeHand shader 未找到，跳过手部材质设置");
            return;
        }

        // 左眼材质
        _leftEyeMat = new Material(perEyeShader);
        _leftEyeMat.name = "LeftEyeHand_Runtime";
        _leftEyeMat.SetColor("_BaseColor", new Color(0.5f, 0.8f, 1f, 0.3f));
        _leftEyeMat.SetFloat("_TargetEye", 0f); // 左眼
        _leftEyeMat.SetFloat("_FresnelPower", 2.5f);
        _leftEyeMat.SetColor("_FresnelColor", new Color(0f, 0.9f, 1f, 0.8f));
        _leftEyeMat.renderQueue = 3000;

        // 右眼材质
        _rightEyeMat = new Material(perEyeShader);
        _rightEyeMat.name = "RightEyeHand_Runtime";
        _rightEyeMat.SetColor("_BaseColor", new Color(0.5f, 0.8f, 1f, 0.3f));
        _rightEyeMat.SetFloat("_TargetEye", 1f); // 右眼
        _rightEyeMat.SetFloat("_FresnelPower", 2.5f);
        _rightEyeMat.SetColor("_FresnelColor", new Color(0f, 0.9f, 1f, 0.8f));
        _rightEyeMat.renderQueue = 3000;

        // 查找场景中的手部追踪对象并分配材质
        AssignHandMaterials();
    }

    private void AssignHandMaterials()
    {
        // 查找 LeftHandTracking 和 RightHandTracking
        string[] leftNames = { "LeftHandTracking", "LeftHand", "OVRLeftHand" };
        string[] rightNames = { "RightHandTracking", "RightHand", "OVRRightHand" };

        foreach (string name in leftNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                ApplyMaterialToRenderers(obj, _leftEyeMat);
                Debug.Log($"[IronManHUD] PerEyeHand (左眼) 材质已分配到 {name}");
                break;
            }
        }

        foreach (string name in rightNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                ApplyMaterialToRenderers(obj, _rightEyeMat);
                Debug.Log($"[IronManHUD] PerEyeHand (右眼) 材质已分配到 {name}");
                break;
            }
        }
    }

    private void ApplyMaterialToRenderers(GameObject root, Material mat)
    {
        // OVRMeshRenderer / SkinnedMeshRenderer
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] mats = smr.materials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            smr.materials = mats;
        }

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] mats = mr.materials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.materials = mats;
        }
    }

    // ══════════════════════════════════════════════════
    //               HUD 位置跟随
    // ══════════════════════════════════════════════════

    private void UpdateHUDPosition()
    {
        if (_canvas == null || teleopManager == null || teleopManager.CameraRig == null) return;

        Transform eye = teleopManager.CameraRig.centerEyeAnchor;
        if (eye == null) return;

        // 目标位置：正前方 hudDistance 处
        Vector3 forward = eye.forward;
        forward.y *= 0.3f; // 减弱俯仰影响
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
            // 平滑阻尼跟随
            float t = followSmoothSpeed * Time.deltaTime;
            _targetPosition = Vector3.Lerp(_targetPosition, targetPos, t);
            _targetRotation = Quaternion.Slerp(_targetRotation, targetRot, t);
            _canvas.transform.position = _targetPosition;
            _canvas.transform.rotation = _targetRotation;
        }

        // 更新骨骼可视化器位置
        UpdateSkeletonPositions();
    }

    private void UpdateSkeletonPositions()
    {
        if (_canvas == null) return;

        Transform canvasT = _canvas.transform;
        RectTransform canvasRT = _canvas.GetComponent<RectTransform>();

        // 骨骼可视化已移除
    }

    // ══════════════════════════════════════════════════
    //                 状态更新
    // ══════════════════════════════════════════════════

    private void UpdateStatusBar()
    {
        if (_statusText == null || teleopManager == null) return;

        var ros = teleopManager.RosBridge;
        var hand = teleopManager.HandController;

        // ─── ROS 连接 ───
        string rosState, rosColor;
        if (ros != null && ros.IsConnected)
        {
            rosState = "CONNECTED";
            rosColor = "#00FF80";
            if (_rosIndicator != null) _rosIndicator.color = GREEN;
        }
        else if (ros != null && ros.State == ROSBridgeConnection.ConnectionState.Connecting)
        {
            rosState = "CONNECTING...";
            rosColor = "#FFDD00";
            if (_rosIndicator != null) _rosIndicator.color = GOLD;
        }
        else if (ros != null && ros.State == ROSBridgeConnection.ConnectionState.Reconnecting)
        {
            rosState = $"RECONNECTING ({ros.ReconnectAttempts})";
            rosColor = "#FFAA00";
            if (_rosIndicator != null) _rosIndicator.color = GOLD;
        }
        else if (ros != null && ros.State == ROSBridgeConnection.ConnectionState.Error)
        {
            rosState = "ERROR";
            rosColor = "#FF3344";
            if (_rosIndicator != null) _rosIndicator.color = RED;
        }
        else
        {
            rosState = "OFFLINE";
            rosColor = "#FF4466";
            if (_rosIndicator != null) _rosIndicator.color = RED;
        }

        // ─── Clutch ───
        string clutchState, clutchColor;
        if (teleopManager.ClutchEngaged)
        {
            clutchState = "ENGAGED";
            clutchColor = "#00FF80";
            if (_clutchIndicator != null) _clutchIndicator.color = GREEN;
        }
        else
        {
            clutchState = "DISENGAGED";
            clutchColor = "#FF4466";
            if (_clutchIndicator != null) _clutchIndicator.color = RED;
        }

        // ─── 输入模式 ───
        string inputMode = hand != null ? hand.CurrentMode.ToString().ToUpper() : "N/A";

        // ─── 组装 ───
        float uptime = ros != null ? ros.Uptime : 0f;
        string uptimeStr = uptime > 0 ? $"{uptime:F0}s" : "---";

        _statusText.text =
            $"  <color={rosColor}>●</color> ROS: <color={rosColor}>{rosState}</color>" +
            $"    │    <color={clutchColor}>⚡</color> CLUTCH: <color={clutchColor}>{clutchState}</color>" +
            $"    │    ↗ <color=#00CCFF>{teleopManager.SendRate:F0}Hz</color>" +
            $"    │    SCALE: <color=#00CCFF>{teleopManager.PositionScale:F1}x</color>" +
            $"    │    MODE: <color=#00CCFF>{inputMode}</color>" +
            $"    │    UPTIME: <color=#00CCFF>{uptimeStr}</color>";

        // 时间
        if (_timeText != null)
            _timeText.text = System.DateTime.Now.ToString("HH:mm:ss");
    }

    private void UpdateHandPanels()
    {
        if (teleopManager == null || teleopManager.HandController == null) return;
        var hand = teleopManager.HandController;

        // 左手
        if (_leftHandData != null)
        {
            bool tracked = hand.IsLeftHandTracking;
            bool active = hand.IsLeftInputActive;
            string tColor = tracked ? "#00FF80" : "#FF4466";
            string aColor = active ? "#00FF80" : "#FF4466";

            _leftHandData.text =
                $"<color={tColor}>● TRACK: {(tracked ? "OK" : "LOST")}</color>\n" +
                $"<color={aColor}>● ACTIVE: {(active ? "YES" : "NO")}</color>\n" +
                $"<color=#00CCFF>GRIP: {hand.LeftGripperValue:F2}</color>";
        }

        // 右手
        if (_rightHandData != null)
        {
            bool tracked = hand.IsRightHandTracking;
            bool active = hand.IsRightInputActive;
            string tColor = tracked ? "#00FF80" : "#FF4466";
            string aColor = active ? "#00FF80" : "#FF4466";

            _rightHandData.text =
                $"<color={tColor}>● TRACK: {(tracked ? "OK" : "LOST")}</color>\n" +
                $"<color={aColor}>● ACTIVE: {(active ? "YES" : "NO")}</color>\n" +
                $"<color=#00CCFF>GRIP: {hand.RightGripperValue:F2}</color>";
        }
    }

    // ══════════════════════════════════════════════════
    //                动画效果
    // ══════════════════════════════════════════════════

    private void UpdateScanLineAnimation()
    {
        if (_scanLine == null) return;

        // 扫描线从上到下缓慢移动
        float y = Mathf.Repeat(Time.time * 0.06f, 1.2f) - 0.1f;
        var rt = _scanLine.rectTransform;
        rt.anchorMin = new Vector2(0.01f, y);
        rt.anchorMax = new Vector2(0.99f, y + 0.005f);

        // 靠近边缘时淡出
        float alpha = 1f - Mathf.Abs(y - 0.5f) * 2f;
        alpha = Mathf.Clamp01(alpha) * 0.10f;
        _scanLine.color = new Color(0f, 1f, 1f, alpha);
    }

    private void UpdatePulsingEffects()
    {
        if (_cornerBrackets == null) return;

        // 角落支架微弱脉冲
        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 1.5f);
        Color bracketColor = CYAN * new Color(1, 1, 1, pulse);
        foreach (var bracket in _cornerBrackets)
        {
            if (bracket != null) bracket.color = bracketColor;
        }

        // 标题文字呼吸效果
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
    //                  UI 工具方法
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

