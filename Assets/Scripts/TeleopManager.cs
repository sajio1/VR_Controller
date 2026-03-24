using UnityEngine;
using System.IO;

/// <summary>
/// Pipeline coordinator for the Movement SDK SMPL body tracking system.
///
/// Wires together:
///   MovementSDKBodyTracker → SMPLRetargeter → SMPLModelDriver (Editor visualization)
///                                           → NPZRecorder    (file recording)
///                                           → SMPLDataPublisher (real-time streaming)
///
/// Also manages:
///   - Passthrough mode (Quest sees real world only)
///   - Left hand thumb+pinky clutch: press to start recording, press again to stop (with audio cues)
///   - Disabling legacy UI (IronManHUD, SettingsPanel)
///   - Placing the SMPL model at a fixed world position for VR viewing
/// </summary>
public class TeleopManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //              New Pipeline Components
    // ══════════════════════════════════════════════════

    [Header("Movement SDK Pipeline")]
    [SerializeField] private MovementSDKBodyTracker bodyTracker;
    [SerializeField] private NPZRecorder npzRecorder;
    [SerializeField] private SMPLDataPublisher dataPublisher;

    [Header("VR")]
    [SerializeField] private OVRCameraRig cameraRig;

    [Header("Humanoid Placement")]
    [Tooltip("SMPL humanoid root transform (official retarget target)")]
    [SerializeField] private Transform humanoidCharacterRoot;
    [Tooltip("Where the humanoid stands in world space")]
    [SerializeField] private Vector3 humanoidWorldPosition = new Vector3(0f, 0f, 3f);
    [Tooltip("Character faces user by default")]
    [SerializeField] private float humanoidFacingAngleY = 180f;
    [Tooltip("Force character Y to ground (0) on startup")]
    [SerializeField] private bool forceGroundY = true;

    [Header("Hands-Only Interaction")]
    [Tooltip("Left hand thumb+pinky clutch: above this = pressed (0~1)")]
    [SerializeField] private float leftClutchThreshold = 0.5f;
    [Tooltip("Min time between two clutch presses to avoid double-trigger (seconds)")]
    [SerializeField] private float clutchCooldownSeconds = 0.4f;

    [Header("Audio Cues (start/stop recording)")]
    [Tooltip("Optional custom sound when recording starts")]
    [SerializeField] private AudioClip recordingStartClip;
    [Tooltip("Optional custom sound when recording stops")]
    [SerializeField] private AudioClip recordingStopClip;
    [Tooltip("Always play a beep when no custom clip is set")]
    [SerializeField] private bool useGeneratedBeepFallback = true;
    [Range(0f, 1f)]
    [SerializeField] private float cueVolume = 0.9f;

    [Header("In-HMD Status Overlay")]
    [Tooltip("Show small runtime status text in HMD (for build-only workflow)")]
    [SerializeField] private bool showStatusOverlay = true;
    [SerializeField] private Color overlayTextColor = new Color(0.2f, 1f, 0.3f, 1f);
    [SerializeField] private int overlayFontSize = 34;

    // ══════════════════════════════════════════════════
    //              New Public API
    // ══════════════════════════════════════════════════

    public MovementSDKBodyTracker BodyTrackerSDK => bodyTracker;
    public SMPLRetargeter Retargeter => null;
    public SMPLModelDriver ModelDriver => null;
    public HumanoidModelViewer ModelViewer => null;
    public NPZRecorder Recorder => npzRecorder;
    public SMPLDataPublisher Publisher => dataPublisher;
    public OVRCameraRig CameraRig => cameraRig;

    public bool IsBodyTracking => bodyTracker != null && bodyTracker.IsTracking;
    public bool IsCalibrated => true; // Legacy UI compatibility; no manual T-pose calibration.
    public bool IsRecording => npzRecorder != null && npzRecorder.IsRecording;

    // ══════════════════════════════════════════════════
    //    Backward-Compatible Properties (for preserved UI)
    // ══════════════════════════════════════════════════

    public BodyTrackingManager BodyTracker => null;
    public PoseNormalizer Normalizer => null;

    // ══════════════════════════════════════════════════
    //                    Events
    // ══════════════════════════════════════════════════

    public event System.Action OnCalibrated;

    private bool _leftClutchWasAbove;
    private float _nextClutchAllowedTime;
    private AudioSource _cueAudioSource;
    private AudioClip _generatedStartBeep;
    private AudioClip _generatedStopBeep;
    private string _statusMessage = "READY";
    private float _statusExpireTime = -1f;

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        if (humanoidCharacterRoot == null)
        {
            var retargetedHumanoid = FindAnyObjectByType<OVRUnityHumanoidSkeletonRetargeter>();
            if (retargetedHumanoid != null)
                humanoidCharacterRoot = retargetedHumanoid.transform;
        }
        DisableLegacyUI();
        DisableLegacyHandcraftedPipeline();
        SetupPassthrough();
        PlaceHumanoidInWorld();
        SetupAudioCues();
        SetStatus("READY", -1f);
    }

    private void Update()
    {
        HandleButtonInputs();
    }

    private void OnApplicationPause(bool paused) { }

    // ══════════════════════════════════════════════════
    //              Disable Legacy UI
    // ══════════════════════════════════════════════════

    private void DisableLegacyUI()
    {
        var hud = FindAnyObjectByType<IronManHUD>(FindObjectsInactive.Include);
        if (hud != null)
        {
            hud.enabled = false;
            hud.gameObject.SetActive(false);
            Debug.Log("[TeleopManager] Disabled legacy IronManHUD");
        }

        var settings = FindAnyObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        if (settings != null)
        {
            settings.enabled = false;
            settings.gameObject.SetActive(false);
            Debug.Log("[TeleopManager] Disabled legacy SettingsPanel");
        }

        var legacyViewer = FindAnyObjectByType<HumanoidModelViewer>(FindObjectsInactive.Include);
        if (legacyViewer != null)
        {
            legacyViewer.enabled = false;
            Debug.Log("[TeleopManager] Disabled unlinked legacy HumanoidModelViewer");
        }
    }

    private void DisableLegacyHandcraftedPipeline()
    {
        var oldRetargeter = FindAnyObjectByType<SMPLRetargeter>(FindObjectsInactive.Include);
        if (oldRetargeter != null)
        {
            oldRetargeter.enabled = false;
            Debug.Log("[TeleopManager] Disabled legacy SMPLRetargeter (handcrafted)");
        }

        var oldDriver = FindAnyObjectByType<SMPLModelDriver>(FindObjectsInactive.Include);
        if (oldDriver != null)
        {
            oldDriver.enabled = false;
            Debug.Log("[TeleopManager] Disabled legacy SMPLModelDriver (handcrafted)");
        }

        var oldProcedural = FindAnyObjectByType<ProceduralHumanoid>(FindObjectsInactive.Include);
        if (oldProcedural != null)
        {
            oldProcedural.enabled = false;
            if (oldProcedural.gameObject != null) oldProcedural.gameObject.SetActive(false);
            Debug.Log("[TeleopManager] Disabled legacy ProceduralHumanoid (handcrafted)");
        }
    }

    // ══════════════════════════════════════════════════
    //           Place Model at Fixed World Position
    // ══════════════════════════════════════════════════

    private void PlaceHumanoidInWorld()
    {
        if (humanoidCharacterRoot == null) return;

        Vector3 targetPos = humanoidWorldPosition;
        if (forceGroundY) targetPos.y = 0f;

        humanoidCharacterRoot.position = targetPos;
        humanoidCharacterRoot.rotation = Quaternion.Euler(0f, humanoidFacingAngleY, 0f);

        Debug.Log($"[TeleopManager] Humanoid placed at {targetPos}, facing {humanoidFacingAngleY}°");
    }

    // ══════════════════════════════════════════════════
    //              Passthrough Setup
    // ══════════════════════════════════════════════════

    private void SetupPassthrough()
    {
        if (OVRManager.instance != null)
        {
            OVRManager.instance.isInsightPassthroughEnabled = true;
        }

        var existingLayer = FindAnyObjectByType<OVRPassthroughLayer>();
        if (existingLayer == null)
        {
            GameObject ptObj = new GameObject("OVRPassthroughLayer");
            var ptLayer = ptObj.AddComponent<OVRPassthroughLayer>();
            ptLayer.overlayType = OVROverlay.OverlayType.Underlay;
            ptLayer.compositionDepth = 0;
        }

        if (cameraRig != null)
        {
            Camera centerCam = cameraRig.centerEyeAnchor.GetComponent<Camera>();
            if (centerCam != null)
            {
                centerCam.clearFlags = CameraClearFlags.SolidColor;
                centerCam.backgroundColor = Color.clear;
            }
        }
    }

    // ══════════════════════════════════════════════════
    //                 Button Inputs
    // ══════════════════════════════════════════════════

    private void HandleButtonInputs()
    {
        HandleLeftClutchInput();
    }

    private void HandleLeftClutchInput()
    {
        if (bodyTracker == null || npzRecorder == null) return;

        float clutch = bodyTracker.LeftClutchStrength;
        bool above = clutch >= leftClutchThreshold;

        if (above && !_leftClutchWasAbove && Time.time >= _nextClutchAllowedTime)
        {
            ToggleRecording();
            _nextClutchAllowedTime = Time.time + clutchCooldownSeconds;
        }
        _leftClutchWasAbove = above;
    }

    // ══════════════════════════════════════════════════
    //                 Recording
    // ══════════════════════════════════════════════════

    public void ToggleRecording()
    {
        if (npzRecorder == null) return;

        if (npzRecorder.IsRecording)
        {
            string path = npzRecorder.StopRecording();
            PlayRecordingCue(false);
            string file = string.IsNullOrEmpty(path) ? "(unknown)" : Path.GetFileName(path);
            SetStatus($"SAVED {file}", 2.5f);
            Debug.Log($"[TeleopManager] Recording saved: {path}");
        }
        else
        {
            npzRecorder.StartRecording();
            PlayRecordingCue(true);
            SetStatus("RECORDING...", -1f);
            Debug.Log("[TeleopManager] Recording started");
        }
    }

    public void ForceRecalibrate()
    {
        Debug.Log("[TeleopManager] Recalibration removed in Humanoid official pipeline.");
    }

    // ══════════════════════════════════════════════════
    //        Calibration (backward-compatible API)
    // ══════════════════════════════════════════════════

    public void CalibrateTPose()
    {
        Debug.Log("[TeleopManager] T-pose calibration removed in Humanoid official pipeline.");
    }

    public void ResetCalibration()
    {
        Debug.Log("[TeleopManager] Calibration reset removed in Humanoid official pipeline.");
    }

    private void SetupAudioCues()
    {
        _cueAudioSource = GetComponent<AudioSource>();
        if (_cueAudioSource == null)
            _cueAudioSource = gameObject.AddComponent<AudioSource>();
        _cueAudioSource.playOnAwake = false;
        _cueAudioSource.spatialBlend = 0f;
        _cueAudioSource.volume = cueVolume;

        if (useGeneratedBeepFallback)
        {
            _generatedStartBeep = CreateBeepClip("rec_start_beep", 880f, 0.12f);
            _generatedStopBeep = CreateBeepClip("rec_stop_beep", 520f, 0.16f);
        }
    }

    private void PlayRecordingCue(bool recordingStarted)
    {
        if (_cueAudioSource == null) return;
        _cueAudioSource.volume = cueVolume;

        AudioClip clip = recordingStarted ? recordingStartClip : recordingStopClip;
        if (clip == null && useGeneratedBeepFallback)
            clip = recordingStarted ? _generatedStartBeep : _generatedStopBeep;
        if (clip == null) return;

        _cueAudioSource.PlayOneShot(clip, 1f);
    }

    private static AudioClip CreateBeepClip(string clipName, float frequencyHz, float durationSeconds, int sampleRate = 44100)
    {
        int sampleCount = Mathf.Max(1, Mathf.CeilToInt(durationSeconds * sampleRate));
        float[] data = new float[sampleCount];
        float twoPiF = 2f * Mathf.PI * frequencyHz;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = 1f;
            float fade = 0.015f;
            if (t < fade) envelope = t / fade;
            else if (durationSeconds - t < fade) envelope = (durationSeconds - t) / fade;
            data[i] = Mathf.Sin(twoPiF * t) * envelope * 0.25f;
        }
        var clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void SetStatus(string text, float durationSeconds)
    {
        _statusMessage = text;
        _statusExpireTime = durationSeconds > 0f ? Time.time + durationSeconds : -1f;
    }

    private void OnGUI()
    {
        if (!showStatusOverlay) return;
        if (_statusExpireTime > 0f && Time.time > _statusExpireTime)
        {
            _statusExpireTime = -1f;
            _statusMessage = npzRecorder != null && npzRecorder.IsRecording ? "RECORDING..." : "READY";
        }

        if (string.IsNullOrEmpty(_statusMessage)) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = overlayFontSize;
        style.normal.textColor = overlayTextColor;
        style.alignment = TextAnchor.UpperLeft;

        Rect r = new Rect(24, 20, Screen.width - 40, 80);
        GUI.Label(r, _statusMessage, style);
    }
}
