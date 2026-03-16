using UnityEngine;

/// <summary>
/// Body tracking coordinator (v3 — full body tracking).
///
/// Manages the body tracking lifecycle, T-pose calibration,
/// and coordinates between BodyTrackingManager, PoseNormalizer,
/// SMPLModelDriver, and HumanoidModelViewer.
///
/// No clutch, no ROS — purely local body visualization.
/// </summary>
public class TeleopManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                Inspector References
    // ══════════════════════════════════════════════════

    [Header("Core Components")]
    [SerializeField] private BodyTrackingManager bodyTrackingManager;
    [SerializeField] private PoseNormalizer poseNormalizer;
    [SerializeField] private SMPLModelDriver modelDriver;
    [SerializeField] private HumanoidModelViewer modelViewer;
    [SerializeField] private OVRCameraRig cameraRig;

    // ══════════════════════════════════════════════════
    //              Public Properties (for HUD / Settings)
    // ══════════════════════════════════════════════════

    public BodyTrackingManager BodyTracker => bodyTrackingManager;
    public PoseNormalizer Normalizer => poseNormalizer;
    public SMPLModelDriver ModelDriver => modelDriver;
    public HumanoidModelViewer ModelViewer => modelViewer;
    public OVRCameraRig CameraRig => cameraRig;

    public bool IsBodyTracking => bodyTrackingManager != null && bodyTrackingManager.IsBodyTracking;
    public bool IsCalibrated => poseNormalizer != null && poseNormalizer.IsCalibrated;

    // ══════════════════════════════════════════════════
    //                    Events
    // ══════════════════════════════════════════════════

    public event System.Action OnCalibrated;

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;

        if (modelDriver != null && !modelDriver.IsInitialized)
            modelDriver.Initialize();
    }

    private void Update()
    {
        HandleButtonInputs();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused && poseNormalizer != null)
        {
            // After resume, reset calibration to avoid stale reference
            poseNormalizer.ResetCalibration();
            Debug.Log("[TeleopManager] App resumed — calibration reset");
        }
    }

    // ══════════════════════════════════════════════════
    //                 Button Inputs
    // ══════════════════════════════════════════════════

    private void HandleButtonInputs()
    {
        // A button → T-Pose calibration
        if (OVRInput.GetDown(OVRInput.Button.One))
            CalibrateTPose();
    }

    // ══════════════════════════════════════════════════
    //                  Calibration
    // ══════════════════════════════════════════════════

    /// <summary>Trigger T-Pose calibration (user should stand in T-pose).</summary>
    public void CalibrateTPose()
    {
        if (poseNormalizer == null)
        {
            Debug.LogWarning("[TeleopManager] Calibration failed — PoseNormalizer not available");
            return;
        }

        poseNormalizer.CalibrateTPose();

        try { OnCalibrated?.Invoke(); } catch { }
    }

    /// <summary>Reset calibration to defaults.</summary>
    public void ResetCalibration()
    {
        if (poseNormalizer != null)
            poseNormalizer.ResetCalibration();
    }
}
