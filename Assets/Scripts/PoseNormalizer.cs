using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Normalizes body tracking data for retargeting to a standard SMPL-H model.
///
/// Core approach: rotation-only retargeting.
///   - Joint rotations are inherently body-size invariant (a tall person's
///     elbow bend produces the same quaternion as a short person's).
///   - Root (hips) position is normalized relative to a calibration T-pose
///     so different heights map to the same standard model space.
///
/// Provides the mapping between OVR body skeleton joint IDs and SMPL-H
/// joint names, plus calibration logic for T-pose reference capture.
/// </summary>
public class PoseNormalizer : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //              SMPL-H Joint Definition
    // ══════════════════════════════════════════════════

    /// <summary>SMPL-H body joint names (24 body joints).</summary>
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

    // ══════════════════════════════════════════════════
    //              OVR → SMPL-H Mapping
    // ══════════════════════════════════════════════════

    public struct JointMapping
    {
        public SMPLJoint SmplJoint;
        public OVRSkeleton.BoneId OvrBoneId;

        public JointMapping(SMPLJoint smpl, OVRSkeleton.BoneId ovr)
        {
            SmplJoint = smpl;
            OvrBoneId = ovr;
        }
    }

    private static readonly JointMapping[] _jointMap = new JointMapping[]
    {
        new JointMapping(SMPLJoint.Pelvis,      OVRSkeleton.BoneId.Body_Hips),
        new JointMapping(SMPLJoint.L_Hip,        OVRSkeleton.BoneId.Body_LeftUpperLeg),
        new JointMapping(SMPLJoint.R_Hip,        OVRSkeleton.BoneId.Body_RightUpperLeg),
        new JointMapping(SMPLJoint.Spine1,       OVRSkeleton.BoneId.Body_SpineLower),
        new JointMapping(SMPLJoint.L_Knee,       OVRSkeleton.BoneId.Body_LeftLowerLeg),
        new JointMapping(SMPLJoint.R_Knee,       OVRSkeleton.BoneId.Body_RightLowerLeg),
        new JointMapping(SMPLJoint.Spine2,       OVRSkeleton.BoneId.Body_SpineMiddle),
        new JointMapping(SMPLJoint.L_Ankle,      OVRSkeleton.BoneId.Body_LeftFootAnkle),
        new JointMapping(SMPLJoint.R_Ankle,      OVRSkeleton.BoneId.Body_RightFootAnkle),
        new JointMapping(SMPLJoint.Spine3,       OVRSkeleton.BoneId.Body_SpineUpper),
        new JointMapping(SMPLJoint.L_Foot,       OVRSkeleton.BoneId.Body_LeftFootBall),
        new JointMapping(SMPLJoint.R_Foot,       OVRSkeleton.BoneId.Body_RightFootBall),
        new JointMapping(SMPLJoint.Neck,         OVRSkeleton.BoneId.Body_Neck),
        new JointMapping(SMPLJoint.L_Collar,     OVRSkeleton.BoneId.Body_LeftShoulder),
        new JointMapping(SMPLJoint.R_Collar,     OVRSkeleton.BoneId.Body_RightShoulder),
        new JointMapping(SMPLJoint.Head,         OVRSkeleton.BoneId.Body_Head),
        new JointMapping(SMPLJoint.L_Shoulder,   OVRSkeleton.BoneId.Body_LeftArmUpper),
        new JointMapping(SMPLJoint.R_Shoulder,   OVRSkeleton.BoneId.Body_RightArmUpper),
        new JointMapping(SMPLJoint.L_Elbow,      OVRSkeleton.BoneId.Body_LeftArmLower),
        new JointMapping(SMPLJoint.R_Elbow,      OVRSkeleton.BoneId.Body_RightArmLower),
        new JointMapping(SMPLJoint.L_Wrist,      OVRSkeleton.BoneId.Body_LeftHandWrist),
        new JointMapping(SMPLJoint.R_Wrist,      OVRSkeleton.BoneId.Body_RightHandWrist),
        new JointMapping(SMPLJoint.L_Hand,       OVRSkeleton.BoneId.Body_LeftHandPalm),
        new JointMapping(SMPLJoint.R_Hand,       OVRSkeleton.BoneId.Body_RightHandPalm),
    };

    /// <summary>Static joint hierarchy: parent index for each SMPL joint (-1 = root).</summary>
    private static readonly int[] _smplParent = new int[]
    {
        -1,                     // 0  Pelvis (root)
        (int)SMPLJoint.Pelvis,  // 1  L_Hip
        (int)SMPLJoint.Pelvis,  // 2  R_Hip
        (int)SMPLJoint.Pelvis,  // 3  Spine1
        (int)SMPLJoint.L_Hip,   // 4  L_Knee
        (int)SMPLJoint.R_Hip,   // 5  R_Knee
        (int)SMPLJoint.Spine1,  // 6  Spine2
        (int)SMPLJoint.L_Knee,  // 7  L_Ankle
        (int)SMPLJoint.R_Knee,  // 8  R_Ankle
        (int)SMPLJoint.Spine2,  // 9  Spine3
        (int)SMPLJoint.L_Ankle, // 10 L_Foot
        (int)SMPLJoint.R_Ankle, // 11 R_Foot
        (int)SMPLJoint.Spine3,  // 12 Neck
        (int)SMPLJoint.Spine3,  // 13 L_Collar
        (int)SMPLJoint.Spine3,  // 14 R_Collar
        (int)SMPLJoint.Neck,    // 15 Head
        (int)SMPLJoint.L_Collar,// 16 L_Shoulder
        (int)SMPLJoint.R_Collar,// 17 R_Shoulder
        (int)SMPLJoint.L_Shoulder, // 18 L_Elbow
        (int)SMPLJoint.R_Shoulder, // 19 R_Elbow
        (int)SMPLJoint.L_Elbow, // 20 L_Wrist
        (int)SMPLJoint.R_Elbow, // 21 R_Wrist
        (int)SMPLJoint.L_Wrist, // 22 L_Hand
        (int)SMPLJoint.R_Wrist, // 23 R_Hand
    };

    // ══════════════════════════════════════════════════
    //                Public API
    // ══════════════════════════════════════════════════

    public static JointMapping[] JointMap => _jointMap;
    public static int[] SmplParentIndices => _smplParent;

    // ══════════════════════════════════════════════════
    //              Normalized Pose Data
    // ══════════════════════════════════════════════════

    public struct NormalizedPose
    {
        public Quaternion[] JointRotations;   // local rotations, length = SMPLJoint.Count
        public Vector3 RootPosition;          // normalized hip position
        public bool IsValid;
    }

    private NormalizedPose _currentPose;
    public NormalizedPose CurrentPose => _currentPose;

    // ══════════════════════════════════════════════════
    //              Calibration State
    // ══════════════════════════════════════════════════

    [Header("Calibration")]
    [SerializeField] private float standardModelHeight = 1.7f;

    private bool _isCalibrated;
    private float _calibrationHipHeight;
    private float _heightScaleFactor = 1f;
    private Quaternion[] _calibrationTPoseRotations;
    private Vector3 _calibrationHipPosition;

    public bool IsCalibrated => _isCalibrated;
    public float HeightScaleFactor => _heightScaleFactor;

    [Header("References")]
    [SerializeField] private BodyTrackingManager bodyTrackingManager;

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        int jointCount = (int)SMPLJoint.Count;
        _currentPose.JointRotations = new Quaternion[jointCount];
        for (int i = 0; i < jointCount; i++)
            _currentPose.JointRotations[i] = Quaternion.identity;

        _calibrationTPoseRotations = new Quaternion[jointCount];
    }

    private void LateUpdate()
    {
        if (bodyTrackingManager == null || !bodyTrackingManager.IsBodyTracking)
        {
            _currentPose.IsValid = false;
            return;
        }

        ComputeNormalizedPose();
    }

    // ══════════════════════════════════════════════════
    //                Calibration
    // ══════════════════════════════════════════════════

    /// <summary>
    /// Capture a T-pose calibration. The user should stand upright with arms out.
    /// Records hip height and joint reference rotations for normalization.
    /// </summary>
    public void CalibrateTPose()
    {
        if (bodyTrackingManager == null || !bodyTrackingManager.IsBodyTracking)
        {
            Debug.LogWarning("[PoseNormalizer] Cannot calibrate: body tracking not active");
            return;
        }

        // Record hip height for position normalization
        var hipsJoint = bodyTrackingManager.GetJoint(OVRSkeleton.BoneId.Body_Hips);
        if (!hipsJoint.IsValid)
        {
            Debug.LogWarning("[PoseNormalizer] Cannot calibrate: hips joint invalid");
            return;
        }

        _calibrationHipPosition = hipsJoint.WorldPosition;
        _calibrationHipHeight = hipsJoint.WorldPosition.y;

        if (_calibrationHipHeight > 0.1f)
        {
            // Standard SMPL-H model hip height is roughly 0.93m for a 1.7m person
            float standardHipHeight = standardModelHeight * 0.55f;
            _heightScaleFactor = standardHipHeight / _calibrationHipHeight;
        }
        else
        {
            _heightScaleFactor = 1f;
        }

        // Record T-pose joint rotations as reference
        for (int i = 0; i < _jointMap.Length && i < (int)SMPLJoint.Count; i++)
        {
            var mapping = _jointMap[i];
            Quaternion localRot = bodyTrackingManager.GetBoneLocalRotation(mapping.OvrBoneId);
            _calibrationTPoseRotations[(int)mapping.SmplJoint] = localRot;
        }

        _isCalibrated = true;
        Debug.Log($"[PoseNormalizer] T-Pose calibrated. Hip height: {_calibrationHipHeight:F3}m, " +
                  $"scale factor: {_heightScaleFactor:F3}");
    }

    /// <summary>Reset calibration to defaults.</summary>
    public void ResetCalibration()
    {
        _isCalibrated = false;
        _heightScaleFactor = 1f;
        _calibrationHipHeight = 0f;
        Debug.Log("[PoseNormalizer] Calibration reset");
    }

    // ══════════════════════════════════════════════════
    //              Pose Computation
    // ══════════════════════════════════════════════════

    private void ComputeNormalizedPose()
    {
        int jointCount = (int)SMPLJoint.Count;

        for (int i = 0; i < _jointMap.Length && i < jointCount; i++)
        {
            var mapping = _jointMap[i];
            int smplIdx = (int)mapping.SmplJoint;

            Quaternion localRot = bodyTrackingManager.GetBoneLocalRotation(mapping.OvrBoneId);

            if (_isCalibrated)
            {
                // Remove T-pose reference rotation to get delta from rest pose
                Quaternion tposeRef = _calibrationTPoseRotations[smplIdx];
                localRot = Quaternion.Inverse(tposeRef) * localRot;
            }

            _currentPose.JointRotations[smplIdx] = localRot;
        }

        // Normalize root position
        var hips = bodyTrackingManager.GetJoint(OVRSkeleton.BoneId.Body_Hips);
        if (hips.IsValid)
        {
            Vector3 rawPos = hips.WorldPosition;

            if (_isCalibrated)
            {
                // Position relative to calibration origin, scaled to standard model
                Vector3 delta = rawPos - _calibrationHipPosition;
                _currentPose.RootPosition = delta * _heightScaleFactor;
            }
            else
            {
                _currentPose.RootPosition = rawPos;
            }
        }

        _currentPose.IsValid = true;
    }
}
