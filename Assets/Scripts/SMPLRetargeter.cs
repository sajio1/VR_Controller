using UnityEngine;
using System;

/// <summary>
/// Standard SMPL pose data structure shared across the pipeline.
/// Contains both quaternion (for model driving) and axis-angle (for NPZ/streaming) representations.
/// </summary>
public struct SMPLPose
{
    /// <summary>24 local joint rotations (for driving the model).</summary>
    public Quaternion[] JointRotations;
    /// <summary>Normalized root translation in Unity space.</summary>
    public Vector3 RootPosition;
    /// <summary>72 floats: 24 joints x 3 axis-angle components (for NPZ output).</summary>
    public float[] AxisAngleData;
    /// <summary>3 floats: root translation (for NPZ output).</summary>
    public float[] TranslationData;
    /// <summary>10 shape parameters (zeros until shape estimation is added).</summary>
    public float[] Betas;
    /// <summary>Unix timestamp (seconds since epoch).</summary>
    public double Timestamp;
    public bool IsValid;
}

/// <summary>
/// Retargets Meta Movement SDK body tracking (OVR 84-bone skeleton) to
/// the SMPL standard 24-joint skeleton.
///
/// Proportion normalization:
///   Joint rotations are inherently body-size invariant. The retargeter
///   only needs to normalize root translation: it auto-measures the user's
///   hip height over the first N frames and computes a scale factor to map
///   movement into SMPL standard space (~1.7m model).
///
///   Height scale is auto-calibrated; rotation reference is captured via
///   manual reference-pose calibration for stable retargeting.
/// </summary>
public class SMPLRetargeter : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //              SMPL-H Joint Definition
    // ══════════════════════════════════════════════════

    public enum SMPLJoint
    {
        Pelvis = 0,
        L_Hip, R_Hip,
        Spine1,
        L_Knee, R_Knee,
        Spine2,
        L_Ankle, R_Ankle,
        Spine3,
        L_Foot, R_Foot,
        Neck,
        L_Collar, R_Collar,
        Head,
        L_Shoulder, R_Shoulder,
        L_Elbow, R_Elbow,
        L_Wrist, R_Wrist,
        L_Hand, R_Hand,
        Count
    }

    /// <summary>Parent index for each SMPL joint (-1 = root).</summary>
    public static readonly int[] SmplParentIndices = new int[]
    {
        -1,  //  0 Pelvis
         0,  //  1 L_Hip
         0,  //  2 R_Hip
         0,  //  3 Spine1
         1,  //  4 L_Knee
         2,  //  5 R_Knee
         3,  //  6 Spine2
         4,  //  7 L_Ankle
         5,  //  8 R_Ankle
         6,  //  9 Spine3
         7,  // 10 L_Foot
         8,  // 11 R_Foot
         9,  // 12 Neck
         9,  // 13 L_Collar
         9,  // 14 R_Collar
        12,  // 15 Head
        13,  // 16 L_Shoulder
        14,  // 17 R_Shoulder
        16,  // 18 L_Elbow
        17,  // 19 R_Elbow
        18,  // 20 L_Wrist
        19,  // 21 R_Wrist
        20,  // 22 L_Hand
        21,  // 23 R_Hand
    };

    /// <summary>
    /// Standard SMPL rest-pose joint positions (meters, Y-up, facing +Z).
    /// Based on zero-shape SMPL model (~1.7m tall person).
    /// </summary>
    public static readonly Vector3[] SmplRestPositions = new Vector3[]
    {
        new Vector3( 0.000f, 0.930f, 0.000f),  //  0 Pelvis
        new Vector3( 0.075f, 0.870f, 0.000f),  //  1 L_Hip
        new Vector3(-0.075f, 0.870f, 0.000f),  //  2 R_Hip
        new Vector3( 0.000f, 1.010f, 0.000f),  //  3 Spine1
        new Vector3( 0.075f, 0.480f, 0.000f),  //  4 L_Knee
        new Vector3(-0.075f, 0.480f, 0.000f),  //  5 R_Knee
        new Vector3( 0.000f, 1.120f, 0.000f),  //  6 Spine2
        new Vector3( 0.075f, 0.070f, 0.000f),  //  7 L_Ankle
        new Vector3(-0.075f, 0.070f, 0.000f),  //  8 R_Ankle
        new Vector3( 0.000f, 1.250f, 0.000f),  //  9 Spine3
        new Vector3( 0.075f, 0.015f, 0.060f),  // 10 L_Foot
        new Vector3(-0.075f, 0.015f, 0.060f),  // 11 R_Foot
        new Vector3( 0.000f, 1.430f, 0.000f),  // 12 Neck
        new Vector3( 0.060f, 1.380f, 0.000f),  // 13 L_Collar
        new Vector3(-0.060f, 1.380f, 0.000f),  // 14 R_Collar
        new Vector3( 0.000f, 1.530f, 0.000f),  // 15 Head
        new Vector3( 0.190f, 1.380f, 0.000f),  // 16 L_Shoulder
        new Vector3(-0.190f, 1.380f, 0.000f),  // 17 R_Shoulder
        new Vector3( 0.460f, 1.380f, 0.000f),  // 18 L_Elbow
        new Vector3(-0.460f, 1.380f, 0.000f),  // 19 R_Elbow
        new Vector3( 0.700f, 1.380f, 0.000f),  // 20 L_Wrist
        new Vector3(-0.700f, 1.380f, 0.000f),  // 21 R_Wrist
        new Vector3( 0.780f, 1.380f, 0.000f),  // 22 L_Hand
        new Vector3(-0.780f, 1.380f, 0.000f),  // 23 R_Hand
    };

    // ══════════════════════════════════════════════════
    //              OVR → SMPL Mapping
    // ══════════════════════════════════════════════════
    //
    // IMPORTANT:
    // Meta XR SDK changes OVRSkeleton.BoneId names across versions.
    // To avoid compile-time breaks, we map by bone Transform/Id string names.

    private struct JointMapping
    {
        public SMPLJoint SmplJoint;
        public string[] BoneNameCandidates;
        public JointMapping(SMPLJoint smpl, params string[] candidates)
        { SmplJoint = smpl; BoneNameCandidates = candidates; }
    }

    private static readonly JointMapping[] _jointMap = new JointMapping[]
    {
        new JointMapping(SMPLJoint.Pelvis,    "Body_Hips", "Hips", "Hip", "Pelvis"),
        new JointMapping(SMPLJoint.L_Hip,     "Body_LeftUpperLeg", "LeftUpperLeg", "LeftHip", "L_Hip"),
        new JointMapping(SMPLJoint.R_Hip,     "Body_RightUpperLeg", "RightUpperLeg", "RightHip", "R_Hip"),
        new JointMapping(SMPLJoint.Spine1,    "Body_SpineLower", "SpineLower", "Spine1", "Spine"),
        new JointMapping(SMPLJoint.L_Knee,    "Body_LeftLowerLeg", "LeftLowerLeg", "LeftKnee", "L_Knee"),
        new JointMapping(SMPLJoint.R_Knee,    "Body_RightLowerLeg", "RightLowerLeg", "RightKnee", "R_Knee"),
        new JointMapping(SMPLJoint.Spine2,    "Body_SpineMiddle", "SpineMiddle", "Spine2"),
        new JointMapping(SMPLJoint.L_Ankle,   "Body_LeftFootAnkle", "LeftFootAnkle", "LeftAnkle", "L_Ankle"),
        new JointMapping(SMPLJoint.R_Ankle,   "Body_RightFootAnkle", "RightFootAnkle", "RightAnkle", "R_Ankle"),
        new JointMapping(SMPLJoint.Spine3,    "Body_SpineUpper", "SpineUpper", "Spine3", "Chest"),
        new JointMapping(SMPLJoint.L_Foot,    "Body_LeftFootBall", "LeftFootBall", "LeftFoot", "L_Foot"),
        new JointMapping(SMPLJoint.R_Foot,    "Body_RightFootBall", "RightFootBall", "RightFoot", "R_Foot"),
        new JointMapping(SMPLJoint.Neck,      "Body_Neck", "Neck"),
        new JointMapping(SMPLJoint.L_Collar,  "Body_LeftShoulder", "LeftShoulder", "LeftCollar", "L_Collar"),
        new JointMapping(SMPLJoint.R_Collar,  "Body_RightShoulder", "RightShoulder", "RightCollar", "R_Collar"),
        new JointMapping(SMPLJoint.Head,      "Body_Head", "Head"),
        new JointMapping(SMPLJoint.L_Shoulder,"Body_LeftArmUpper", "LeftArmUpper", "LeftUpperArm", "L_Shoulder"),
        new JointMapping(SMPLJoint.R_Shoulder,"Body_RightArmUpper", "RightArmUpper", "RightUpperArm", "R_Shoulder"),
        new JointMapping(SMPLJoint.L_Elbow,   "Body_LeftArmLower", "LeftArmLower", "LeftLowerArm", "LeftElbow", "L_Elbow"),
        new JointMapping(SMPLJoint.R_Elbow,   "Body_RightArmLower", "RightArmLower", "RightLowerArm", "RightElbow", "R_Elbow"),
        new JointMapping(SMPLJoint.L_Wrist,   "Body_LeftHandWrist", "LeftHandWrist", "LeftWrist", "L_Wrist"),
        new JointMapping(SMPLJoint.R_Wrist,   "Body_RightHandWrist", "RightHandWrist", "RightWrist", "R_Wrist"),
        new JointMapping(SMPLJoint.L_Hand,    "Body_LeftHandPalm", "LeftHandPalm", "LeftPalm", "L_Hand"),
        new JointMapping(SMPLJoint.R_Hand,    "Body_RightHandPalm", "RightHandPalm", "RightPalm", "R_Hand"),
    };

    // ══════════════════════════════════════════════════
    //              Inspector / Config
    // ══════════════════════════════════════════════════

    [Header("Data Source")]
    [SerializeField] private MovementSDKBodyTracker bodyTracker;

    [Header("Calibration")]
    [Tooltip("Standard SMPL model height (meters)")]
    [SerializeField] private float standardModelHeight = 1.7f;

    [Tooltip("Number of frames to average for auto-calibration")]
    [SerializeField] private int calibrationFrameCount = 10;

    [Header("Auto Recalibration (No Controllers)")]
    [Tooltip("Allow repeated auto calibration by holding T-pose")]
    [SerializeField] private bool enableAutoTPoseRecalibration = true;
    [Tooltip("How long T-pose must be held to (re)calibrate")]
    [SerializeField] private float tPoseHoldSeconds = 3f;
    [Tooltip("Cooldown after each successful auto calibration")]
    [SerializeField] private float tPoseRecalibrationCooldown = 1.0f;

    [Header("Retarget Fine Tuning (Euler Offsets)")]
    [Tooltip("Applied to spine joints: Spine1/2/3 + Neck + Head")]
    [SerializeField] private Vector3 spineEulerOffset = Vector3.zero;
    [Tooltip("Applied to left upper arm joint")]
    [SerializeField] private Vector3 leftShoulderEulerOffset = Vector3.zero;
    [Tooltip("Applied to right upper arm joint")]
    [SerializeField] private Vector3 rightShoulderEulerOffset = Vector3.zero;
    [Tooltip("Applied to left elbow/forearm joint")]
    [SerializeField] private Vector3 leftElbowEulerOffset = Vector3.zero;
    [Tooltip("Applied to right elbow/forearm joint")]
    [SerializeField] private Vector3 rightElbowEulerOffset = Vector3.zero;

    // ══════════════════════════════════════════════════
    //              State
    // ══════════════════════════════════════════════════

    private SMPLPose _currentPose;
    private bool _isCalibrated;
    private bool _rotationsCalibrated;
    private float _heightScaleFactor = 1f;
    private Vector3 _calibrationHipPosition;
    private int _calibrationFramesCollected;
    private float _accumulatedHipHeight;
    private Quaternion[] _sourceRestLocalRotations;
    private bool[] _sourceRestCaptured;
    private float _tPoseHoldTimer;
    private bool _tPoseLatched;
    private float _nextAutoTPoseTime;

    // ══════════════════════════════════════════════════
    //              Public API
    // ══════════════════════════════════════════════════

    public SMPLPose CurrentPose => _currentPose;
    public bool IsCalibrated => _isCalibrated;
    public bool IsRotationCalibrated => _rotationsCalibrated;
    public float HeightScaleFactor => _heightScaleFactor;
    public bool IsTracking => bodyTracker != null && bodyTracker.IsTracking;

    public static int JointCount => (int)SMPLJoint.Count;

    private Transform TryFindBoneTransform(params string[] candidates)
    {
        if (bodyTracker == null || bodyTracker.Skeleton == null) return null;
        var bones = bodyTracker.Skeleton.Bones;
        if (bones == null) return null;

        for (int c = 0; c < candidates.Length; c++)
        {
            string cand = candidates[c];
            if (string.IsNullOrEmpty(cand)) continue;
            Transform best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                var t = b.Transform;
                if (t == null) continue;
                int score = GetBoneMatchScore(cand, t.name, b.Id.ToString());
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                    if (bestScore == 0) break; // exact match: cannot get better
                }
            }

            // Score <= 4 means acceptable match; lower is better.
            if (best != null && bestScore <= 4)
                return best;
        }
        return null;
    }

    private static int GetBoneMatchScore(string candidate, string transformName, string idString)
    {
        int scoreFromName = GetStringMatchScore(candidate, transformName);
        int scoreFromId = GetStringMatchScore(candidate, idString);
        int score = Mathf.Min(scoreFromName, scoreFromId);
        if (score >= 1000) return score;

        // Prefer non-twist/non-helper bones when multiple names partially match.
        string n = (transformName ?? string.Empty).ToLowerInvariant();
        if (n.Contains("twist")) score += 2;
        if (n.Contains("helper")) score += 2;
        if (n.Contains("roll")) score += 1;
        return score;
    }

    private static int GetStringMatchScore(string candidate, string source)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(source))
            return 1000;

        if (source.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (source.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (source.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (source.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
            return 4;

        return 1000;
    }

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        int count = (int)SMPLJoint.Count;
        _currentPose.JointRotations = new Quaternion[count];
        _currentPose.AxisAngleData = new float[count * 3];
        _currentPose.TranslationData = new float[3];
        _currentPose.Betas = new float[10];
        _sourceRestLocalRotations = new Quaternion[count];
        _sourceRestCaptured = new bool[count];
        for (int i = 0; i < count; i++)
        {
            _currentPose.JointRotations[i] = Quaternion.identity;
            _sourceRestLocalRotations[i] = Quaternion.identity;
            _sourceRestCaptured[i] = false;
        }
    }

    private void LateUpdate()
    {
        if (bodyTracker == null || !bodyTracker.IsTracking)
        {
            _currentPose.IsValid = false;
            return;
        }

        if (!_isCalibrated)
            AutoCalibrate();

        if (enableAutoTPoseRecalibration)
            UpdateAutoReferenceCalibration();

        ComputeRetargetedPose();
    }

    // ══════════════════════════════════════════════════
    //              Auto Calibration
    // ══════════════════════════════════════════════════

    /// <summary>
    /// Automatically calibrates by averaging hip height over the first N
    /// valid frames. Computes the scale factor mapping user height to
    /// SMPL standard height.
    /// </summary>
    private void AutoCalibrate()
    {
        var hipsT = TryFindBoneTransform("Body_Hips", "Hips", "Hip", "Pelvis");
        Vector3 hipsPos = hipsT != null ? hipsT.position : Vector3.zero;
        if (hipsPos.y < 0.1f) return;

        _accumulatedHipHeight += hipsPos.y;
        _calibrationFramesCollected++;

        if (_calibrationFramesCollected >= calibrationFrameCount)
        {
            float avgHipHeight = _accumulatedHipHeight / _calibrationFramesCollected;
            const float canonicalSmplHeight = 1.7f;
            float scaledSmplHipHeight = SmplRestPositions[0].y * (standardModelHeight / canonicalSmplHeight);
            if (avgHipHeight < 0.001f) avgHipHeight = 0.001f;

            float smplHipHeight = scaledSmplHipHeight;
            _heightScaleFactor = smplHipHeight / avgHipHeight;
            _calibrationHipPosition = new Vector3(hipsPos.x, avgHipHeight, hipsPos.z);
            _isCalibrated = true;
            Debug.Log($"[SMPLRetargeter] Auto-calibrated: hip height={avgHipHeight:F3}m, " +
                      $"scale={_heightScaleFactor:F3}");
        }
    }

    /// <summary>
    /// Captures source skeleton rest local rotations once tracking is stable.
    /// Later frames use delta-from-rest to avoid pose twisting between
    /// different rest-pose conventions (OVR body vs. SMPL body).
    /// </summary>
    private void TryCaptureRestPoseRotations()
    {
        int captured = 0;
        int count = (int)SMPLJoint.Count;
        for (int i = 0; i < _jointMap.Length && i < count; i++)
        {
            var mapping = _jointMap[i];
            int idx = (int)mapping.SmplJoint;
            var boneT = TryFindBoneTransform(mapping.BoneNameCandidates);
            if (boneT == null) continue;

            _sourceRestLocalRotations[idx] = boneT.localRotation;
            _sourceRestCaptured[idx] = true;
            captured++;
        }

        // Require most joints to be available before locking calibration.
        if (captured >= 18)
        {
            _rotationsCalibrated = true;
            Debug.Log($"[SMPLRetargeter] Rotation rest-pose calibrated ({captured}/24 joints)");
        }
    }

    /// <summary>
    /// Hands-free calibration: user holds T-pose for 3 seconds.
    /// Once detected, captures reference rotations to stabilize retargeting.
    /// </summary>
    private void UpdateAutoReferenceCalibration()
    {
        if (Time.time < _nextAutoTPoseTime) return;

        if (!IsLikelyTPose())
        {
            _tPoseHoldTimer = 0f;
            _tPoseLatched = false;
            return;
        }

        if (_tPoseLatched) return;

        _tPoseHoldTimer += Time.deltaTime;
        if (_tPoseHoldTimer < tPoseHoldSeconds) return;

        bool ok = CalibrateReferencePoseNow();
        _tPoseHoldTimer = 0f;
        _tPoseLatched = true;
        _nextAutoTPoseTime = Time.time + tPoseRecalibrationCooldown;
        if (ok)
            Debug.Log("[SMPLRetargeter] Auto reference calibrated by T-pose hold");
    }

    private bool IsLikelyTPose()
    {
        var lShoulder = TryFindBoneTransform("Body_LeftArmUpper", "LeftArmUpper", "LeftUpperArm");
        var lElbow = TryFindBoneTransform("Body_LeftArmLower", "LeftArmLower", "LeftLowerArm", "LeftElbow");
        var lWrist = TryFindBoneTransform("Body_LeftHandWrist", "LeftHandWrist", "LeftWrist");
        var rShoulder = TryFindBoneTransform("Body_RightArmUpper", "RightArmUpper", "RightUpperArm");
        var rElbow = TryFindBoneTransform("Body_RightArmLower", "RightArmLower", "RightLowerArm", "RightElbow");
        var rWrist = TryFindBoneTransform("Body_RightHandWrist", "RightHandWrist", "RightWrist");
        var hips = TryFindBoneTransform("Body_Hips", "Hips", "Hip", "Pelvis");
        var neck = TryFindBoneTransform("Body_Neck", "Neck");
        if (lShoulder == null || lElbow == null || lWrist == null ||
            rShoulder == null || rElbow == null || rWrist == null ||
            hips == null || neck == null)
            return false;

        // Arms should be roughly horizontal at shoulder height.
        const float heightTol = 0.20f;
        bool leftHeightOk =
            Mathf.Abs(lElbow.position.y - lShoulder.position.y) < heightTol &&
            Mathf.Abs(lWrist.position.y - lShoulder.position.y) < heightTol;
        bool rightHeightOk =
            Mathf.Abs(rElbow.position.y - rShoulder.position.y) < heightTol &&
            Mathf.Abs(rWrist.position.y - rShoulder.position.y) < heightTol;
        if (!leftHeightOk || !rightHeightOk) return false;

        // Arms should be nearly straight.
        float lBend = Vector3.Angle(lElbow.position - lShoulder.position, lWrist.position - lElbow.position);
        float rBend = Vector3.Angle(rElbow.position - rShoulder.position, rWrist.position - rElbow.position);
        if (lBend > 35f || rBend > 35f) return false;

        // Body roughly upright.
        Vector3 torso = (neck.position - hips.position).normalized;
        if (Vector3.Dot(torso, Vector3.up) < 0.6f) return false;

        // Left/right arms should spread apart along shoulder line.
        Vector3 shoulderAxis = (rShoulder.position - lShoulder.position).normalized;
        Vector3 lArm = (lWrist.position - lShoulder.position).normalized;
        Vector3 rArm = (rWrist.position - rShoulder.position).normalized;
        bool spreadOk = Vector3.Dot(lArm, -shoulderAxis) > 0.7f && Vector3.Dot(rArm, shoulderAxis) > 0.7f;
        return spreadOk;
    }

    /// <summary>
    /// Manual reference-pose calibration.
    /// User should stand upright in a clear T-pose, then trigger calibration.
    /// </summary>
    public bool CalibrateReferencePoseNow()
    {
        if (bodyTracker == null || !bodyTracker.IsTracking)
        {
            Debug.LogWarning("[SMPLRetargeter] Manual calibration failed: tracking not ready");
            return false;
        }

        // Capture hip reference immediately for stable world-space root delta.
        var hipsT = TryFindBoneTransform("Body_Hips", "Hips", "Hip", "Pelvis");
        if (hipsT != null)
        {
            float hipY = Mathf.Max(hipsT.position.y, 0.001f);
            const float canonicalSmplHeight = 1.7f;
            float scaledSmplHipHeight = SmplRestPositions[0].y * (standardModelHeight / canonicalSmplHeight);
            _heightScaleFactor = scaledSmplHipHeight / hipY;
            _calibrationHipPosition = hipsT.position;
            _isCalibrated = true;
        }

        TryCaptureRestPoseRotations();
        if (!_rotationsCalibrated)
        {
            Debug.LogWarning("[SMPLRetargeter] Manual calibration failed: not enough joints captured");
            return false;
        }

        Debug.Log("[SMPLRetargeter] Manual reference pose calibrated (use T-pose)");
        return true;
    }

    /// <summary>Reset calibration; will re-calibrate on next valid frames.</summary>
    public void ResetCalibration()
    {
        _isCalibrated = false;
        _rotationsCalibrated = false;
        _heightScaleFactor = 1f;
        _calibrationFramesCollected = 0;
        _accumulatedHipHeight = 0f;
        _tPoseHoldTimer = 0f;
        _tPoseLatched = false;
        _nextAutoTPoseTime = 0f;
        if (_sourceRestCaptured != null)
        {
            for (int i = 0; i < _sourceRestCaptured.Length; i++)
                _sourceRestCaptured[i] = false;
        }
        Debug.Log("[SMPLRetargeter] Calibration reset");
    }

    // ══════════════════════════════════════════════════
    //              Pose Computation
    // ══════════════════════════════════════════════════

    private void ComputeRetargetedPose()
    {
        int count = (int)SMPLJoint.Count;

        for (int i = 0; i < _jointMap.Length && i < count; i++)
        {
            var mapping = _jointMap[i];
            int idx = (int)mapping.SmplJoint;

            var boneT = TryFindBoneTransform(mapping.BoneNameCandidates);
            Quaternion sourceLocalRot = boneT != null ? boneT.localRotation : Quaternion.identity;
            Quaternion retargetedLocalRot = sourceLocalRot;
            if (_rotationsCalibrated && _sourceRestCaptured[idx])
            {
                // Use parent-space delta from reference pose:
                //   delta = current * inverse(reference)
                // This is typically the correct form when retargeting between
                // different skeleton local frames.
                retargetedLocalRot = sourceLocalRot * Quaternion.Inverse(_sourceRestLocalRotations[idx]);
            }

            retargetedLocalRot = ApplyJointOffsets((SMPLJoint)idx, retargetedLocalRot);
            _currentPose.JointRotations[idx] = retargetedLocalRot;

            QuaternionToAxisAngle(retargetedLocalRot,
                out _currentPose.AxisAngleData[idx * 3],
                out _currentPose.AxisAngleData[idx * 3 + 1],
                out _currentPose.AxisAngleData[idx * 3 + 2]);
        }

        var hipsT = TryFindBoneTransform("Body_Hips", "Hips", "Hip", "Pelvis");
        Vector3 hipsPos = hipsT != null ? hipsT.position : Vector3.zero;
        if (_isCalibrated)
        {
            Vector3 delta = hipsPos - _calibrationHipPosition;
            _currentPose.RootPosition = delta * _heightScaleFactor;
        }
        else
        {
            _currentPose.RootPosition = Vector3.zero;
        }

        _currentPose.TranslationData[0] = _currentPose.RootPosition.x;
        _currentPose.TranslationData[1] = _currentPose.RootPosition.y;
        _currentPose.TranslationData[2] = _currentPose.RootPosition.z;

        _currentPose.Timestamp = GetUnixTimestamp();
        _currentPose.IsValid = true;
    }

    private Quaternion ApplyJointOffsets(SMPLJoint joint, Quaternion localRot)
    {
        Vector3 eulerOffset = Vector3.zero;
        switch (joint)
        {
            case SMPLJoint.Spine1:
            case SMPLJoint.Spine2:
            case SMPLJoint.Spine3:
            case SMPLJoint.Neck:
            case SMPLJoint.Head:
                eulerOffset = spineEulerOffset;
                break;
            case SMPLJoint.L_Shoulder:
                eulerOffset = leftShoulderEulerOffset;
                break;
            case SMPLJoint.R_Shoulder:
                eulerOffset = rightShoulderEulerOffset;
                break;
            case SMPLJoint.L_Elbow:
                eulerOffset = leftElbowEulerOffset;
                break;
            case SMPLJoint.R_Elbow:
                eulerOffset = rightElbowEulerOffset;
                break;
        }

        if (eulerOffset.sqrMagnitude < 1e-6f) return localRot;
        return localRot * Quaternion.Euler(eulerOffset);
    }

    // ══════════════════════════════════════════════════
    //              Quaternion → Axis-Angle
    // ══════════════════════════════════════════════════

    /// <summary>
    /// Converts a Unity quaternion to an axis-angle (Rodrigues) vector.
    /// Output is rotation_axis * angle_radians, matching SMPL convention.
    /// </summary>
    private static void QuaternionToAxisAngle(Quaternion q, out float ax, out float ay, out float az)
    {
        q.Normalize();
        if (q.w < 0) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }

        float halfAngle = Mathf.Acos(Mathf.Clamp(q.w, -1f, 1f));
        float sinHalf = Mathf.Sin(halfAngle);

        if (sinHalf > 1e-6f)
        {
            float scale = 2f * halfAngle / sinHalf;
            ax = q.x * scale;
            ay = q.y * scale;
            az = q.z * scale;
        }
        else
        {
            ax = ay = az = 0f;
        }
    }

    private static double GetUnixTimestamp()
    {
        return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
