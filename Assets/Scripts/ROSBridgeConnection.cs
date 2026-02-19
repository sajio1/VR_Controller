using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ROSBridge WebSocket 通信模块（v2 重构版）
///
/// 改进：
///   - 正式状态机，状态转换防抖（防 Android 上连接状态闪烁）
///   - 心跳健康检查（连续 N 次失败才判定断连）
///   - OnApplicationPause 自动重连
///   - 指数退避重连（2s → 最大 30s）
///   - 状态变更事件（供 HUD 订阅）
///   - 运行时间 & 重连次数追踪
/// </summary>
public class ROSBridgeConnection : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                    连接状态
    // ══════════════════════════════════════════════════

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Error
    }

    // ══════════════════════════════════════════════════
    //                 Inspector 字段
    // ══════════════════════════════════════════════════

    [Header("ROS Bridge 设置")]
    [Tooltip("rosbridge_server 的 WebSocket URL")]
    [SerializeField] private string rosbridgeUrl = "ws://192.168.1.100:9090";

    [Header("右手话题")]
    [SerializeField] private string rightPoseTopic = "/quest3/right_hand_pose";
    [SerializeField] private string rightGripperTopic = "/quest3/right_gripper";

    [Header("左手话题")]
    [SerializeField] private string leftPoseTopic = "/quest3/left_hand_pose";
    [SerializeField] private string leftGripperTopic = "/quest3/left_gripper";

    [Header("连接设置")]
    [Tooltip("重连基础间隔（秒），指数退避 ×1.5")]
    [SerializeField] private float reconnectBaseInterval = 2.0f;
    [Tooltip("重连最大间隔（秒）")]
    [SerializeField] private float reconnectMaxInterval = 30.0f;
    [SerializeField] private bool connectOnStart = true;

    [Header("心跳设置")]
    [Tooltip("心跳检查间隔（秒）")]
    [SerializeField] private float heartbeatInterval = 3.0f;
    [Tooltip("连续心跳失败次数阈值，超过则判定断连")]
    [SerializeField] private int heartbeatFailThreshold = 2;

    // ══════════════════════════════════════════════════
    //                  对外只读属性
    // ══════════════════════════════════════════════════

    private volatile int _stateValue = (int)ConnectionState.Disconnected;

    /// <summary>当前连接状态（线程安全）</summary>
    public ConnectionState State
    {
        get => (ConnectionState)_stateValue;
        private set
        {
            var prev = (ConnectionState)_stateValue;
            if (prev == value) return;
            _stateValue = (int)value;
            Debug.Log($"[ROSBridge] 状态: {prev} → {value}");
            try { OnStateChanged?.Invoke(value); } catch { }
        }
    }

    /// <summary>最近的错误信息</summary>
    public string LastError { get; private set; } = "";

    /// <summary>重连次数</summary>
    public int ReconnectAttempts { get; private set; }

    /// <summary>本次连接运行时长（秒）</summary>
    public float Uptime { get; private set; }

    /// <summary>WebSocket 是否处于 Open 状态（安全判定）</summary>
    public bool IsConnected
    {
        get
        {
            try { return _ws != null && _ws.State == WebSocketState.Open; }
            catch { return false; }
        }
    }

    /// <summary>向后兼容属性</summary>
    public bool IsActuallyConnected => IsConnected;

    /// <summary>rosbridge URL</summary>
    public string RosbridgeUrl
    {
        get => rosbridgeUrl;
        set => rosbridgeUrl = value;
    }

    // ══════════════════════════════════════════════════
    //                      事件
    // ══════════════════════════════════════════════════

    /// <summary>状态变更事件（主线程回调不保证，UI 监听时注意）</summary>
    public event Action<ConnectionState> OnStateChanged;

    // ══════════════════════════════════════════════════
    //                    内部状态
    // ══════════════════════════════════════════════════

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>();
    private readonly StringBuilder _jsonBuilder = new StringBuilder(1024);

    // 话题 advertise
    private bool _rightPoseAdvertised;
    private bool _rightGripperAdvertised;
    private bool _leftPoseAdvertised;
    private bool _leftGripperAdvertised;

    // 重连
    private bool _shouldReconnect;
    private float _lastReconnectTime;
    private int _reconnectAttempt;

    // 心跳
    private int _heartbeatFailCount;

    // 运行时间
    private float _connectedSince;

    // ══════════════════════════════════════════════════
    //                 Unity 生命周期
    // ══════════════════════════════════════════════════

    private void Start()
    {
        if (connectOnStart) Connect();
    }

    private void Update()
    {
        // 更新运行时长
        if (State == ConnectionState.Connected)
            Uptime = Time.time - _connectedSince;

        // 自动重连（指数退避）
        if (_shouldReconnect &&
            (State == ConnectionState.Disconnected ||
             State == ConnectionState.Error ||
             State == ConnectionState.Reconnecting))
        {
            float interval = Mathf.Min(
                reconnectBaseInterval * Mathf.Pow(1.5f, _reconnectAttempt),
                reconnectMaxInterval);

            if (Time.time - _lastReconnectTime >= interval)
            {
                _lastReconnectTime = Time.time;
                _reconnectAttempt++;
                ReconnectAttempts = _reconnectAttempt;
                State = ConnectionState.Reconnecting;
                LaunchConnect();
            }
        }
    }

    private void OnDestroy() => Disconnect();
    private void OnApplicationQuit() => Disconnect();

    private void OnApplicationPause(bool paused)
    {
        if (!paused && _shouldReconnect)
        {
            Debug.Log("[ROSBridge] 头显恢复 — 强制重连");
            ForceReconnect();
        }
    }

    // ══════════════════════════════════════════════════
    //                   公开接口
    // ══════════════════════════════════════════════════

    /// <summary>连接到 rosbridge_server</summary>
    public void Connect()
    {
        if (State == ConnectionState.Connecting || State == ConnectionState.Connected)
            return;

        _shouldReconnect = true;
        _reconnectAttempt = 0;
        ReconnectAttempts = 0;
        State = ConnectionState.Connecting;
        LaunchConnect();
    }

    /// <summary>断开连接并停止自动重连</summary>
    public void Disconnect()
    {
        _shouldReconnect = false;
        CleanupConnection();
        State = ConnectionState.Disconnected;
        Uptime = 0f;
    }

    /// <summary>强制断开并立即重连</summary>
    public void ForceReconnect()
    {
        CleanupConnection();
        State = ConnectionState.Disconnected;
        _lastReconnectTime = 0f; // 允许立即重连
        _reconnectAttempt = 0;
    }

    // ══════════════════════════════════════════════════
    //               右手发布接口
    // ══════════════════════════════════════════════════

    public void PublishRightPose(Vector3 position, Quaternion rotation, string frameId = "quest3_right_hand")
    {
        if (State != ConnectionState.Connected) return;
        EnsureAdvertised(ref _rightPoseAdvertised, rightPoseTopic, "geometry_msgs/PoseStamped");
        EnqueuePoseMessage(rightPoseTopic, position, rotation, frameId);
    }

    public void PublishRightGripper(float value)
    {
        if (State != ConnectionState.Connected) return;
        EnsureAdvertised(ref _rightGripperAdvertised, rightGripperTopic, "std_msgs/Float32");
        EnqueueFloat32Message(rightGripperTopic, value);
    }

    // ══════════════════════════════════════════════════
    //               左手发布接口
    // ══════════════════════════════════════════════════

    public void PublishLeftPose(Vector3 position, Quaternion rotation, string frameId = "quest3_left_hand")
    {
        if (State != ConnectionState.Connected) return;
        EnsureAdvertised(ref _leftPoseAdvertised, leftPoseTopic, "geometry_msgs/PoseStamped");
        EnqueuePoseMessage(leftPoseTopic, position, rotation, frameId);
    }

    public void PublishLeftGripper(float value)
    {
        if (State != ConnectionState.Connected) return;
        EnsureAdvertised(ref _leftGripperAdvertised, leftGripperTopic, "std_msgs/Float32");
        EnqueueFloat32Message(leftGripperTopic, value);
    }

    // ══════════════════════════════════════════════════
    //           向后兼容接口（映射到右手）
    // ══════════════════════════════════════════════════

    public void PublishPose(Vector3 position, Quaternion rotation, string frameId = "quest3_hand")
        => PublishRightPose(position, rotation, frameId);

    public void PublishGripper(float value)
        => PublishRightGripper(value);

    // ══════════════════════════════════════════════════
    //                 消息构建
    // ══════════════════════════════════════════════════

    private void EnsureAdvertised(ref bool flag, string topic, string type)
    {
        if (flag) return;
        AdvertiseTopic(topic, type);
        flag = true;
    }

    private void AdvertiseTopic(string topic, string type)
    {
        _sendQueue.Enqueue($"{{\"op\":\"advertise\",\"topic\":\"{topic}\",\"type\":\"{type}\"}}");
    }

    private void UnadvertiseAll()
    {
        void Unadv(string t) => _sendQueue.Enqueue($"{{\"op\":\"unadvertise\",\"topic\":\"{t}\"}}");
        Unadv(rightPoseTopic);
        Unadv(rightGripperTopic);
        Unadv(leftPoseTopic);
        Unadv(leftGripperTopic);
    }

    private void EnqueuePoseMessage(string topic, Vector3 pos, Quaternion rot, string frameId)
    {
        long stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int sec = (int)(stamp / 1000);
        int nsec = (int)((stamp % 1000) * 1000000);

        _jsonBuilder.Clear();
        _jsonBuilder.Append("{\"op\":\"publish\",\"topic\":\"").Append(topic);
        _jsonBuilder.Append("\",\"msg\":{\"header\":{\"stamp\":{\"sec\":").Append(sec);
        _jsonBuilder.Append(",\"nanosec\":").Append(nsec);
        _jsonBuilder.Append("},\"frame_id\":\"").Append(frameId);
        _jsonBuilder.Append("\"},\"pose\":{\"position\":{\"x\":").Append(pos.x.ToString("F6"));
        _jsonBuilder.Append(",\"y\":").Append(pos.y.ToString("F6"));
        _jsonBuilder.Append(",\"z\":").Append(pos.z.ToString("F6"));
        _jsonBuilder.Append("},\"orientation\":{\"x\":").Append(rot.x.ToString("F6"));
        _jsonBuilder.Append(",\"y\":").Append(rot.y.ToString("F6"));
        _jsonBuilder.Append(",\"z\":").Append(rot.z.ToString("F6"));
        _jsonBuilder.Append(",\"w\":").Append(rot.w.ToString("F6"));
        _jsonBuilder.Append("}}}}");
        _sendQueue.Enqueue(_jsonBuilder.ToString());
    }

    private void EnqueueFloat32Message(string topic, float value)
    {
        _jsonBuilder.Clear();
        _jsonBuilder.Append("{\"op\":\"publish\",\"topic\":\"").Append(topic);
        _jsonBuilder.Append("\",\"msg\":{\"data\":").Append(value.ToString("F4")).Append("}}");
        _sendQueue.Enqueue(_jsonBuilder.ToString());
    }

    // ══════════════════════════════════════════════════
    //                连接清理
    // ══════════════════════════════════════════════════

    private void CleanupConnection()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open) UnadvertiseAll();
                _ws.Dispose();
            }
            catch { }
            _ws = null;
        }
        ResetTopicFlags();
    }

    private void ResetTopicFlags()
    {
        _rightPoseAdvertised = false;
        _rightGripperAdvertised = false;
        _leftPoseAdvertised = false;
        _leftGripperAdvertised = false;
    }

    // ══════════════════════════════════════════════════
    //              异步网络逻辑
    // ══════════════════════════════════════════════════

    private void LaunchConnect()
    {
        CleanupConnection();
        ResetTopicFlags();
        _heartbeatFailCount = 0;
        _cts = new CancellationTokenSource();
        Task.Run(() => ConnectAsync(_cts.Token));
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(heartbeatInterval);
            Debug.Log($"[ROSBridge] 正在连接: {rosbridgeUrl}");
            await _ws.ConnectAsync(new Uri(rosbridgeUrl), ct);

            // 连接成功
            State = ConnectionState.Connected;
            _connectedSince = Time.time;
            _reconnectAttempt = 0; // 重置退避计数
            ReconnectAttempts = 0;
            _heartbeatFailCount = 0;
            Debug.Log("[ROSBridge] ✓ 连接成功!");

            // 阻塞在发送循环直到断连
            await SendLoopAsync(ct);
        }
        catch (OperationCanceledException) { /* 正常取消 */ }
        catch (Exception e)
        {
            LastError = e.Message;
            State = ConnectionState.Error;
            Debug.LogWarning($"[ROSBridge] 连接失败: {e.Message}");
        }
        finally
        {
            if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
                State = ConnectionState.Disconnected;

            if (_ws != null)
            {
                try { _ws.Dispose(); } catch { }
                _ws = null;
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        int tickCounter = 0;
        int heartbeatTicks = Mathf.Max(1, (int)(heartbeatInterval / 0.01f)); // 每次 tick 10ms

        while (!ct.IsCancellationRequested)
        {
            // ─── 发送队列中的所有消息 ───
            while (_sendQueue.TryDequeue(out string msg))
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(msg);
                    await _ws.SendAsync(new ArraySegment<byte>(data),
                        WebSocketMessageType.Text, true, ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    LastError = e.Message;
                    Debug.LogWarning($"[ROSBridge] 发送失败: {e.Message}");
                    State = ConnectionState.Error;
                    return;
                }
            }

            // ─── 心跳健康检查（防抖：连续 N 次失败才判定断连）───
            tickCounter++;
            if (tickCounter >= heartbeatTicks)
            {
                tickCounter = 0;
                try
                {
                    if (_ws == null || _ws.State != WebSocketState.Open)
                    {
                        _heartbeatFailCount++;
                        if (_heartbeatFailCount >= heartbeatFailThreshold)
                        {
                            Debug.LogWarning($"[ROSBridge] 心跳检查失败 ×{_heartbeatFailCount}，判定断连");
                            State = ConnectionState.Error;
                            return;
                        }
                        Debug.Log($"[ROSBridge] 心跳检查异常 ({_heartbeatFailCount}/{heartbeatFailThreshold})");
                    }
                    else
                    {
                        _heartbeatFailCount = 0; // 正常则重置
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ROSBridge] 心跳异常: {e.Message}");
                    _heartbeatFailCount++;
                    if (_heartbeatFailCount >= heartbeatFailThreshold)
                    {
                        State = ConnectionState.Error;
                        return;
                    }
                }
            }

            // 短暂等待
            try { await Task.Delay(10, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
