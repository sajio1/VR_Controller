using UnityEngine;

/// <summary>
/// Publishes SMPL pose data in real-time via ROSBridgeConnection.
///
/// Each frame (at the configured rate), serializes full SMPL-X data
/// from HumanoidSMPLPoseProvider (poses165 + split fields) and publishes
/// to ROS for real-time teleoperation.
/// </summary>
public class SMPLDataPublisher : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private HumanoidSMPLPoseProvider humanoidPoseProvider;

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
        if (humanoidPoseProvider == null)
            humanoidPoseProvider = FindAnyObjectByType<HumanoidSMPLPoseProvider>();
        if (rosConnection == null)
            rosConnection = FindAnyObjectByType<ROSBridgeConnection>();
        _publishInterval = publishRate > 0 ? 1f / publishRate : 0f;
    }

    private void LateUpdate()
    {
        if (!publishEnabled || humanoidPoseProvider == null || rosConnection == null) return;
        if (!rosConnection.IsConnected) return;

        if (!humanoidPoseProvider.TryGetSMPLXFrame(out HumanoidSMPLPoseProvider.SMPLXFrameData frame))
            return;
        if (!frame.IsValid) return;

        if (_publishInterval > 0f && Time.time - _lastPublishTime < _publishInterval)
            return;
        _lastPublishTime = Time.time;

        rosConnection.PublishSMPLXFullPose(
            frame.FullPose165,
            frame.Translation3,
            frame.RootOrient3,
            frame.PoseBody63,
            frame.PoseHand90,
            frame.PoseJaw3,
            frame.PoseEye6,
            frame.Betas10,
            frame.Timestamp
        );
    }

    public void SetEnabled(bool enabled)
    {
        publishEnabled = enabled;
    }
}
