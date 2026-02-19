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

            // 播放扫频音效
            PlayClutchSound(_clutchEngaged);

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

    // 左手小指 Pinch 手势控制 Clutch
    private bool _lastPinchState;
    
    // Clutch 切换音效
    private AudioSource _audioSource;
    private AudioClip _clutchOnSound;   // 和弦 ON
    private AudioClip _clutchOffSound;  // 和弦 OFF
    private AudioClip _rosConnectSound;  // 上行扫频（ROS 连接）
    private AudioClip _rosDisconnectSound; // 下行扫频（ROS 断开）
    private bool _wasRosConnected;  // 跟踪上一次连接状态

    // ══════════════════════════════════════════════════
    //              Unity 生命周期
    // ══════════════════════════════════════════════════

    private void Start()
    {
        _sendInterval = 1f / sendRate;

        // Passthrough 透视
        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;
        
        // 初始化 Clutch 切换音效
        InitializeClutchSounds();
    }
    
    /// <summary>
    /// 初始化所有音效
    /// </summary>
    private void InitializeClutchSounds()
    {
        // 创建 AudioSource
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D 音效，不受位置影响
        _audioSource.volume = 0.6f;
        
        // Clutch ON: 明亮的大三和弦 C5+E5+G5 (523Hz + 659Hz + 784Hz)
        _clutchOnSound = GenerateChord(new float[] { 523f, 659f, 784f }, 0.15f, true);
        
        // Clutch OFF: 下行的小和弦 A4+C5 (440Hz + 523Hz)
        _clutchOffSound = GenerateChord(new float[] { 440f, 523f }, 0.12f, false);
        
        // ROS 连接: 上行扫频 (科幻感)
        _rosConnectSound = GenerateSweep(300f, 1000f, 0.2f, true);
        
        // ROS 断开: 下行扫频
        _rosDisconnectSound = GenerateSweep(800f, 300f, 0.25f, false);
        
        // 订阅 ROS 连接状态变化
        if (rosBridge != null)
        {
            rosBridge.OnStateChanged += OnRosStateChanged;
        }
        
        Debug.Log("[TeleopManager] 音效系统初始化完成");
    }
    
    /// <summary>
    /// ROS 连接状态变化回调
    /// </summary>
    private void OnRosStateChanged(ROSBridgeConnection.ConnectionState newState)
    {
        bool isConnected = (newState == ROSBridgeConnection.ConnectionState.Connected);
        
        // 检测状态变化
        if (isConnected && !_wasRosConnected)
        {
            // 刚连接上 → 播放上行扫频
            PlaySound(_rosConnectSound);
            Debug.Log("[TeleopManager] 🔊 ROS 连接成功音效");
        }
        else if (!isConnected && _wasRosConnected && 
                 newState != ROSBridgeConnection.ConnectionState.Reconnecting)
        {
            // 意外断开（不是正在重连） → 播放下行扫频
            PlaySound(_rosDisconnectSound);
            Debug.Log("[TeleopManager] 🔊 ROS 断开音效");
        }
        
        _wasRosConnected = isConnected;
    }
    
    /// <summary>
    /// 生成扫频音效（用于 ROS 连接状态）
    /// </summary>
    private AudioClip GenerateSweep(float startFreq, float endFreq, float duration, bool ascending)
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];
        
        float phase = 0f;
        float prevFreq = startFreq;
        
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            
            // 指数扫频
            float freq = startFreq * Mathf.Pow(endFreq / startFreq, t);
            
            // 相位累积（平滑过渡，避免杂音）
            phase += 2f * Mathf.PI * (prevFreq + freq) * 0.5f / sampleRate;
            prevFreq = freq;
            
            // 音量包络
            float envelope;
            if (t < 0.08f)
                envelope = t / 0.08f;
            else if (t > 0.65f)
                envelope = (1f - t) / 0.35f;
            else
                envelope = 1f;
            
            // 基频 + 谐波
            float fundamental = Mathf.Sin(phase);
            float harmonic = Mathf.Sin(phase * 2f) * 0.25f;
            float harmonic2 = Mathf.Sin(phase * 3f) * 0.1f;
            
            data[i] = (fundamental + harmonic + harmonic2) * envelope * 0.35f;
        }
        
        AudioClip clip = AudioClip.Create(ascending ? "RosConnect" : "RosDisconnect", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
    
    /// <summary>
    /// 生成和弦音效
    /// </summary>
    /// <param name="frequencies">和弦中各音的频率</param>
    /// <param name="duration">持续时间 秒</param>
    /// <param name="bright">是否明亮（ON=明亮上扬，OFF=柔和下沉）</param>
    private AudioClip GenerateChord(float[] frequencies, float duration, bool bright)
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];
        
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float sample = 0f;
            
            // 叠加所有频率
            for (int f = 0; f < frequencies.Length; f++)
            {
                float freq = frequencies[f];
                float phase = 2f * Mathf.PI * freq * i / sampleRate;
                
                // 基频
                sample += Mathf.Sin(phase);
                
                // 添加轻微的二次谐波，让声音更温暖
                sample += Mathf.Sin(phase * 2f) * 0.15f;
            }
            
            // 归一化
            sample /= frequencies.Length * 1.15f;
            
            // 音量包络：ADSR 简化版
            float envelope;
            if (t < 0.05f)
            {
                // Attack: 快速上升
                envelope = t / 0.05f;
            }
            else if (t < 0.15f)
            {
                // Decay: 轻微下降到 sustain
                envelope = 1f - (t - 0.05f) / 0.1f * 0.2f;
            }
            else if (t > 0.6f)
            {
                // Release: 淡出
                envelope = (1f - t) / 0.4f * 0.8f;
            }
            else
            {
                // Sustain
                envelope = 0.8f;
            }
            
            // ON 音效稍亮，OFF 音效稍暗
            float brightness = bright ? 1.0f : 0.85f;
            
            data[i] = sample * envelope * 0.4f * brightness;
        }
        
        AudioClip clip = AudioClip.Create(bright ? "ClutchOn" : "ClutchOff", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
    
    /// <summary>
    /// 播放 Clutch 切换音效
    /// </summary>
    private void PlayClutchSound(bool clutchOn)
    {
        PlaySound(clutchOn ? _clutchOnSound : _clutchOffSound);
    }
    
    /// <summary>
    /// 通用音效播放
    /// </summary>
    private void PlaySound(AudioClip clip)
    {
        if (_audioSource == null || clip == null) return;
        _audioSource.PlayOneShot(clip);  // 使用 PlayOneShot 允许音效叠加
    }
    
    private void OnDestroy()
    {
        // 取消订阅事件
        if (rosBridge != null)
        {
            rosBridge.OnStateChanged -= OnRosStateChanged;
        }
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
