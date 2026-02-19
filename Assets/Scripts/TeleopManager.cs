using UnityEngine;

/// <summary>
/// 遥操作主管理器（v2 精简版）
///
/// 职责：协调双手输入采集、Clutch 开关、网络发送、安全限制、校准/归位。
/// 所有 UI 已迁移至 IronManHUD / SettingsPanel。
///
/// Clutch 现为简单 bool 属性，由 SettingsPanel 或左手小指 Pinch 手势控制。
/// </summary>
public class TeleopManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                Inspector 引用
    // ══════════════════════════════════════════════════

    [Header("核心组件引用")]
    [SerializeField] private HandTrackingController handTrackingController;
    [SerializeField] private ROSBridgeConnection rosBridge;
    [SerializeField] private OVRCameraRig cameraRig;

    [Header("发送设置")]
    [Tooltip("位姿数据发送频率 (Hz)")]
    [SerializeField] private float sendRate = 60f;

    [Header("坐标映射")]
    [Tooltip("手部运动到机械臂的缩放倍数")]
    [SerializeField] private float positionScale = 2.0f;

    // ══════════════════════════════════════════════════
    //                  安全限制
    // ══════════════════════════════════════════════════

    [Header("安全限制 — 工作空间边界")]
    [SerializeField] private Vector3 workspaceMin = new Vector3(-1.0f, -1.0f, -1.0f);
    [SerializeField] private Vector3 workspaceMax = new Vector3(1.0f, 1.0f, 1.0f);

    [Tooltip("最大位移速度 (m/s)")]
    [SerializeField] private float maxVelocity = 2.0f;

    // ══════════════════════════════════════════════════
    //                  归位预设
    // ══════════════════════════════════════════════════

    [Header("归位预设")]
    [SerializeField] private Vector3 homePosition = new Vector3(0f, 0.3f, 0.3f);
    [SerializeField] private Quaternion homeRotation = Quaternion.identity;

    // ══════════════════════════════════════════════════
    //           公开属性（供 HUD / SettingsPanel）
    // ══════════════════════════════════════════════════

    /// <summary>手势输入控制器引用</summary>
    public HandTrackingController HandController => handTrackingController;

    /// <summary>ROS Bridge 引用</summary>
    public ROSBridgeConnection RosBridge => rosBridge;

    /// <summary>OVR Camera Rig</summary>
    public OVRCameraRig CameraRig => cameraRig;

    /// <summary>Clutch 离合器（true=发送数据，false=暂停）</summary>
    public bool ClutchEngaged
    {
        get => _clutchEngaged;
        set
        {
            if (_clutchEngaged == value) return;
            _clutchEngaged = value;

            if (_clutchEngaged)
            {
                // 接合时自动 re-anchor，避免位置跳变
                ReAnchorBothHands();
                Debug.Log("[TeleopManager] Clutch ON — 开始发送数据");
            }
            else
            {
                Debug.Log("[TeleopManager] Clutch OFF — 停止发送数据");
            }

            try { OnClutchChanged?.Invoke(_clutchEngaged); } catch { }
        }
    }

    /// <summary>发送频率 Hz</summary>
    public float SendRate
    {
        get => sendRate;
        set
        {
            sendRate = Mathf.Clamp(value, 10f, 120f);
            _sendInterval = 1f / sendRate;
        }
    }

    /// <summary>位置缩放倍数</summary>
    public float PositionScale
    {
        get => positionScale;
        set => positionScale = Mathf.Clamp(value, 0.5f, 10f);
    }

    /// <summary>最大速度 (m/s)</summary>
    public float MaxVelocity
    {
        get => maxVelocity;
        set => maxVelocity = Mathf.Clamp(value, 0.05f, 5f);
    }

    /// <summary>右手是否已校准</summary>
    public bool IsRightCalibrated => _isRightCalibrated;

    /// <summary>左手是否已校准</summary>
    public bool IsLeftCalibrated => _isLeftCalibrated;

    /// <summary>上次发送的右手位置</summary>
    public Vector3 LastSentRightPosition => _lastSentRightPosition;

    /// <summary>上次发送的左手位置</summary>
    public Vector3 LastSentLeftPosition => _lastSentLeftPosition;

    // ══════════════════════════════════════════════════
    //                    事件
    // ══════════════════════════════════════════════════

    public event System.Action<bool> OnClutchChanged;
    public event System.Action OnCalibrated;

    // ══════════════════════════════════════════════════
    //                   内部状态
    // ══════════════════════════════════════════════════

    private bool _clutchEngaged;
    private float _sendInterval;
    private float _lastSendTime;

    // 右手校准
    private bool _isRightCalibrated;
    private Vector3 _rightCalibrationOffset;
    private Quaternion _rightCalibrationRotation = Quaternion.identity;

    // 左手校准
    private bool _isLeftCalibrated;
    private Vector3 _leftCalibrationOffset;
    private Quaternion _leftCalibrationRotation = Quaternion.identity;

    // 右手速度限制
    private Vector3 _lastSentRightPosition;
    private Quaternion _lastSentRightRotation = Quaternion.identity;
    private float _lastSentRightTimestamp;

    // 左手速度限制
    private Vector3 _lastSentLeftPosition;
    private Quaternion _lastSentLeftRotation = Quaternion.identity;
    private float _lastSentLeftTimestamp;

    // 左手中指 Pinch 手势控制 Clutch
    private bool _lastPinchState;

    // ══════════════════════════════════════════════════
    //              Unity 生命周期
    // ══════════════════════════════════════════════════

    private void Start()
    {
        _sendInterval = 1f / sendRate;

        // Passthrough 透视
        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;
    }

    private void Update()
    {
        HandleButtonInputs();
        HandleSendLoop();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
        {
            // 头显恢复 → 关闭 clutch、重置校准
            _clutchEngaged = false;
            _isRightCalibrated = false;
            _isLeftCalibrated = false;
            _lastSentRightTimestamp = 0f;
            _lastSentLeftTimestamp = 0f;
            Debug.Log("[TeleopManager] 头显恢复 — Clutch 已自动关闭");
        }
    }

    // ══════════════════════════════════════════════════
    //                 按钮输入处理
    // ══════════════════════════════════════════════════

    private void HandleButtonInputs()
    {
        // A 键 → 校准
        if (OVRInput.GetDown(OVRInput.Button.One))
            Calibrate();

        // B 键 → 归位
        if (OVRInput.GetDown(OVRInput.Button.Two))
            SendHome();

        // 左手小指+拇指 pinch → 切换 Clutch
        UpdateClutchPinchGesture();
    }

    // ══════════════════════════════════════════════════
    //            左手 Pinch 手势控制 Clutch
    // ══════════════════════════════════════════════════

    private void UpdateClutchPinchGesture()
    {
        if (handTrackingController == null) return;

        // 获取左手小指 pinch 强度（0-1）- 小指比中指更不容易误触
        float pinchStrength = handTrackingController.LeftPinkyPinchStrength;
        bool isPinching = pinchStrength > 0.8f; // 阈值：80%

        // 检测从未捏 → 捏（上升沿触发）
        if (isPinching && !_lastPinchState)
        {
            ClutchEngaged = !ClutchEngaged; // 切换
            Debug.Log($"[TeleopManager] 左手小指 Pinch 切换 Clutch → {(ClutchEngaged ? "ON" : "OFF")}");
        }

        _lastPinchState = isPinching;
    }

    // ══════════════════════════════════════════════════
    //                    发送循环
    // ══════════════════════════════════════════════════

    private void HandleSendLoop()
    {
        if (handTrackingController == null || rosBridge == null) return;
        if (!rosBridge.IsConnected) return;
        if (!_clutchEngaged) return;

        float now = Time.time;
        if (now - _lastSendTime < _sendInterval) return;
        _lastSendTime = now;

        if (handTrackingController.IsRightInputActive)
            SendRightHand(now);

        if (handTrackingController.IsLeftInputActive)
            SendLeftHand(now);
    }

    private void SendRightHand(float now)
    {
        Vector3 rawPos = handTrackingController.RightTargetPosition;
        Quaternion rawRot = handTrackingController.RightTargetRotation;

        Vector3 pos; Quaternion rot;
        if (_isRightCalibrated)
        {
            pos = (rawPos - _rightCalibrationOffset) * positionScale;
            rot = Quaternion.Inverse(_rightCalibrationRotation) * rawRot;
        }
        else
        {
            pos = rawPos * positionScale;
            rot = rawRot;
        }

        // 安全限制
        pos = ClampToWorkspace(pos);
        pos = ApplyVelocityLimit(pos, _lastSentRightPosition, ref _lastSentRightTimestamp, now);

        // 发送
        rosBridge.PublishRightPose(pos, rot);
        rosBridge.PublishRightGripper(handTrackingController.RightGripperValue);

        _lastSentRightPosition = pos;
        _lastSentRightRotation = rot;
    }

    private void SendLeftHand(float now)
    {
        Vector3 rawPos = handTrackingController.LeftTargetPosition;
        Quaternion rawRot = handTrackingController.LeftTargetRotation;

        Vector3 pos; Quaternion rot;
        if (_isLeftCalibrated)
        {
            pos = (rawPos - _leftCalibrationOffset) * positionScale;
            rot = Quaternion.Inverse(_leftCalibrationRotation) * rawRot;
        }
        else
        {
            pos = rawPos * positionScale;
            rot = rawRot;
        }

        // 安全限制
        pos = ClampToWorkspace(pos);
        pos = ApplyVelocityLimit(pos, _lastSentLeftPosition, ref _lastSentLeftTimestamp, now);

        // 发送
        rosBridge.PublishLeftPose(pos, rot);
        rosBridge.PublishLeftGripper(handTrackingController.LeftGripperValue);

        _lastSentLeftPosition = pos;
        _lastSentLeftRotation = rot;
    }

    // ══════════════════════════════════════════════════
    //                    校准
    // ══════════════════════════════════════════════════

    /// <summary>双手校准：记录当前位姿为原点偏移</summary>
    public void Calibrate()
    {
        if (handTrackingController == null)
        {
            Debug.LogWarning("[TeleopManager] 校准失败 — 输入控制器不可用");
            return;
        }

        // 右手
        if (handTrackingController.IsRightInputActive)
        {
            _rightCalibrationOffset = handTrackingController.RightTargetPosition;
            _rightCalibrationRotation = handTrackingController.RightTargetRotation;
            _isRightCalibrated = true;
            _lastSentRightPosition = Vector3.zero;
            _lastSentRightRotation = Quaternion.identity;
            _lastSentRightTimestamp = 0f;
            Debug.Log($"[TeleopManager] 右手校准完成 — 原点: {_rightCalibrationOffset}");
        }
        else
        {
            Debug.LogWarning("[TeleopManager] 右手校准失败 — 输入不可用");
        }

        // 左手
        if (handTrackingController.IsLeftInputActive)
        {
            _leftCalibrationOffset = handTrackingController.LeftTargetPosition;
            _leftCalibrationRotation = handTrackingController.LeftTargetRotation;
            _isLeftCalibrated = true;
            _lastSentLeftPosition = Vector3.zero;
            _lastSentLeftRotation = Quaternion.identity;
            _lastSentLeftTimestamp = 0f;
            Debug.Log($"[TeleopManager] 左手校准完成 — 原点: {_leftCalibrationOffset}");
        }
        else
        {
            Debug.LogWarning("[TeleopManager] 左手校准失败 — 输入不可用");
        }

        try { OnCalibrated?.Invoke(); } catch { }
    }

    // ══════════════════════════════════════════════════
    //                    归位
    // ══════════════════════════════════════════════════

    /// <summary>发送预设归位位姿</summary>
    public void SendHome()
    {
        if (rosBridge == null || !rosBridge.IsConnected)
        {
            Debug.LogWarning("[TeleopManager] 归位失败 — 未连接 ROS Bridge");
            return;
        }

        rosBridge.PublishRightPose(homePosition, homeRotation);
        rosBridge.PublishLeftPose(homePosition, homeRotation);
        Debug.Log("[TeleopManager] 已发送归位指令");
    }

    // ══════════════════════════════════════════════════
    //                  安全限制
    // ══════════════════════════════════════════════════

    private Vector3 ClampToWorkspace(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, workspaceMin.x, workspaceMax.x);
        pos.y = Mathf.Clamp(pos.y, workspaceMin.y, workspaceMax.y);
        pos.z = Mathf.Clamp(pos.z, workspaceMin.z, workspaceMax.z);
        return pos;
    }

    private Vector3 ApplyVelocityLimit(Vector3 target, Vector3 last, ref float ts, float now)
    {
        if (ts <= 0f) { ts = now; return target; }

        float dt = now - ts;
        ts = now;
        if (dt <= 0f) return target;

        Vector3 delta = target - last;
        float speed = delta.magnitude / dt;
        if (speed > maxVelocity)
            target = last + delta.normalized * maxVelocity * dt;

        return target;
    }

    // ══════════════════════════════════════════════════
    //               Re-Anchor 内部
    // ══════════════════════════════════════════════════

    private void ReAnchorBothHands()
    {
        if (handTrackingController == null) return;

        // 右手
        if (handTrackingController.IsRightInputActive)
        {
            _rightCalibrationOffset = handTrackingController.RightTargetPosition;
            _rightCalibrationRotation = handTrackingController.RightTargetRotation;
            _isRightCalibrated = true;
            _lastSentRightTimestamp = 0f;
        }

        // 左手
        if (handTrackingController.IsLeftInputActive)
        {
            _leftCalibrationOffset = handTrackingController.LeftTargetPosition;
            _leftCalibrationRotation = handTrackingController.LeftTargetRotation;
            _isLeftCalibrated = true;
            _lastSentLeftTimestamp = 0f;
        }

        Debug.Log("[TeleopManager] Re-anchor 完成（双手）");
    }
}
