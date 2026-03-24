using UnityEngine;

/// <summary>
/// Publishes SMPL pose data in real-time via ROSBridgeConnection.
///
/// Each frame (at the configured rate), serializes the current SMPLPose
/// (72 axis-angle values + 3 translation values) and publishes to a
/// ROS topic for the partner's robot arm mapping pipeline.
/// </summary>
public class SMPLDataPublisher : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private SMPLRetargeter retargeter;

    [Header("Transport")]
    [SerializeField] private ROSBridgeConnection rosConnection;

    [Header("Publishing Settings")]
    [Tooltip("Publishing rate in Hz (0 = every frame)")]
    [SerializeField] private int publishRate = 30;

    [Tooltip("Enable/disable publishing")]
    [SerializeField] private bool publishEnabled = true;

    private float _lastPublishTime;
    private float _publishInterval;

    public bool IsPublishing => publishEnabled && rosConnection != null && rosConnection.IsConnected;

    private void Start()
    {
        _publishInterval = publishRate > 0 ? 1f / publishRate : 0f;
    }

    private void LateUpdate()
    {
        if (!publishEnabled || retargeter == null || rosConnection == null) return;
        if (!rosConnection.IsConnected) return;

        var pose = retargeter.CurrentPose;
        if (!pose.IsValid) return;

        if (_publishInterval > 0f && Time.time - _lastPublishTime < _publishInterval)
            return;
        _lastPublishTime = Time.time;

        rosConnection.PublishSMPLPose(pose.AxisAngleData, pose.TranslationData, pose.Timestamp);
    }

    public void SetEnabled(bool enabled)
    {
        publishEnabled = enabled;
    }
}
