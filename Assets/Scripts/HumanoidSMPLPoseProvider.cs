using UnityEngine;
using System;

/// <summary>
/// Reads the final Humanoid avatar pose (already retargeted by Meta's
/// OVRUnityHumanoidSkeletonRetargeter) and exports it as SMPLPose data.
/// This keeps the pipeline on official Movement SDK retargeting while
/// preserving NPZ output compatibility.
/// </summary>
[RequireComponent(typeof(Animator))]
public class HumanoidSMPLPoseProvider : MonoBehaviour
{
    public struct SMPLXFrameData
    {
        public bool IsValid;
        public double Timestamp;
        public float[] FullPose165;
        public float[] RootOrient3;
        public float[] PoseBody63;
        public float[] PoseHand90;
        public float[] PoseJaw3;
        public float[] PoseEye6;
        public float[] Translation3;
        public float[] Betas10;
    }

    [Header("Source")]
    [SerializeField] private Animator humanoidAnimator;

    [Header("Root Motion")]
    [Tooltip("Use hip delta from first valid frame as SMPL trans")]
    [SerializeField] private bool useRelativeRootPosition = true;

    private SMPLPose _currentPose;
    private bool _initialized;
    private Transform _hips;
    private Vector3 _rootOrigin;
    private bool _rootOriginSet;

    private static readonly HumanBodyBones[] JointToHumanBone = new HumanBodyBones[]
    {
        HumanBodyBones.Hips,         // Pelvis
        HumanBodyBones.LeftUpperLeg, // L_Hip
        HumanBodyBones.RightUpperLeg,// R_Hip
        HumanBodyBones.Spine,        // Spine1
        HumanBodyBones.LeftLowerLeg, // L_Knee
        HumanBodyBones.RightLowerLeg,// R_Knee
        HumanBodyBones.Chest,        // Spine2
        HumanBodyBones.LeftFoot,     // L_Ankle
        HumanBodyBones.RightFoot,    // R_Ankle
        HumanBodyBones.UpperChest,   // Spine3
        HumanBodyBones.LeftToes,     // L_Foot
        HumanBodyBones.RightToes,    // R_Foot
        HumanBodyBones.Neck,         // Neck
        HumanBodyBones.LeftShoulder, // L_Collar
        HumanBodyBones.RightShoulder,// R_Collar
        HumanBodyBones.Head,         // Head
        HumanBodyBones.LeftUpperArm, // L_Shoulder
        HumanBodyBones.RightUpperArm,// R_Shoulder
        HumanBodyBones.LeftLowerArm, // L_Elbow
        HumanBodyBones.RightLowerArm,// R_Elbow
        HumanBodyBones.LeftHand,     // L_Wrist
        HumanBodyBones.RightHand,    // R_Wrist
        HumanBodyBones.LeftHand,     // L_Hand
        HumanBodyBones.RightHand,    // R_Hand
    };

    public SMPLPose CurrentPose => _currentPose;

    // SMPL-X convention: root + 21 body + 30 hand + jaw + eyes = 55 joints => 165 axis-angle values.
    private static readonly HumanBodyBones[] SmplxBody21 = new HumanBodyBones[]
    {
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.Spine,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.Chest,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot,
        HumanBodyBones.UpperChest,
        HumanBodyBones.LeftToes,
        HumanBodyBones.RightToes,
        HumanBodyBones.Neck,
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
    };

    // Per-hand 15 joints in common SMPL-X order: index, middle, pinky, ring, thumb.
    private static readonly HumanBodyBones[] SmplxLeftHand15 = new HumanBodyBones[]
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
    };

    private static readonly HumanBodyBones[] SmplxRightHand15 = new HumanBodyBones[]
    {
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
    };

    private void Start()
    {
        if (humanoidAnimator == null)
            humanoidAnimator = GetComponent<Animator>();

        int count = (int)SMPLRetargeter.SMPLJoint.Count;
        _currentPose.JointRotations = new Quaternion[count];
        _currentPose.AxisAngleData = new float[count * 3];
        _currentPose.TranslationData = new float[3];
        _currentPose.Betas = new float[10];
        for (int i = 0; i < count; i++)
            _currentPose.JointRotations[i] = Quaternion.identity;

        if (humanoidAnimator == null || !humanoidAnimator.isHuman)
        {
            Debug.LogWarning("[HumanoidSMPLPoseProvider] Animator missing or not Humanoid.");
            return;
        }

        _hips = humanoidAnimator.GetBoneTransform(HumanBodyBones.Hips);
        _initialized = true;
    }

    private void LateUpdate()
    {
        if (!_initialized || humanoidAnimator == null || !humanoidAnimator.isHuman)
        {
            _currentPose.IsValid = false;
            return;
        }

        int count = (int)SMPLRetargeter.SMPLJoint.Count;
        for (int i = 0; i < count; i++)
        {
            Transform t = humanoidAnimator.GetBoneTransform(JointToHumanBone[i]);
            Quaternion localRot = t != null ? t.localRotation : Quaternion.identity;
            _currentPose.JointRotations[i] = localRot;
            QuaternionToAxisAngle(localRot,
                out _currentPose.AxisAngleData[i * 3],
                out _currentPose.AxisAngleData[i * 3 + 1],
                out _currentPose.AxisAngleData[i * 3 + 2]);
        }

        Vector3 rootPos = Vector3.zero;
        if (_hips != null)
        {
            if (useRelativeRootPosition)
            {
                if (!_rootOriginSet)
                {
                    _rootOrigin = _hips.position;
                    _rootOriginSet = true;
                }
                rootPos = _hips.position - _rootOrigin;
            }
            else
            {
                rootPos = _hips.position;
            }
        }

        _currentPose.RootPosition = rootPos;
        _currentPose.TranslationData[0] = rootPos.x;
        _currentPose.TranslationData[1] = rootPos.y;
        _currentPose.TranslationData[2] = rootPos.z;
        _currentPose.Timestamp = GetUnixTimestamp();
        _currentPose.IsValid = true;
    }

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

    public bool TryGetSMPLXFrame(out SMPLXFrameData frame)
    {
        frame = default;
        if (!_currentPose.IsValid || humanoidAnimator == null || !humanoidAnimator.isHuman)
            return false;

        frame.IsValid = true;
        frame.Timestamp = _currentPose.Timestamp;

        frame.RootOrient3 = new float[3];
        WriteAxisAngle(humanoidAnimator.GetBoneTransform(HumanBodyBones.Hips), frame.RootOrient3, 0);

        frame.PoseBody63 = new float[63];
        for (int i = 0; i < SmplxBody21.Length; i++)
            WriteAxisAngle(humanoidAnimator.GetBoneTransform(SmplxBody21[i]), frame.PoseBody63, i * 3);

        frame.PoseHand90 = new float[90];
        for (int i = 0; i < SmplxLeftHand15.Length; i++)
            WriteAxisAngle(humanoidAnimator.GetBoneTransform(SmplxLeftHand15[i]), frame.PoseHand90, i * 3);
        for (int i = 0; i < SmplxRightHand15.Length; i++)
            WriteAxisAngle(humanoidAnimator.GetBoneTransform(SmplxRightHand15[i]), frame.PoseHand90, 45 + i * 3);

        // Humanoid rigs usually don't expose jaw/eyes consistently; keep zeros for compatibility.
        frame.PoseJaw3 = new float[3];
        frame.PoseEye6 = new float[6];

        frame.FullPose165 = new float[165];
        Array.Copy(frame.RootOrient3, 0, frame.FullPose165, 0, 3);
        Array.Copy(frame.PoseBody63, 0, frame.FullPose165, 3, 63);
        Array.Copy(frame.PoseHand90, 0, frame.FullPose165, 66, 90);
        // jaw (3) + eyes (6)
        Array.Copy(frame.PoseJaw3, 0, frame.FullPose165, 156, 3);
        Array.Copy(frame.PoseEye6, 0, frame.FullPose165, 159, 6);

        frame.Translation3 = new float[3];
        Array.Copy(_currentPose.TranslationData, frame.Translation3, 3);

        frame.Betas10 = new float[10];
        if (_currentPose.Betas != null && _currentPose.Betas.Length >= 10)
            Array.Copy(_currentPose.Betas, frame.Betas10, 10);

        return true;
    }

    private static void WriteAxisAngle(Transform t, float[] dst, int offset)
    {
        Quaternion q = t != null ? t.localRotation : Quaternion.identity;
        QuaternionToAxisAngle(q, out dst[offset], out dst[offset + 1], out dst[offset + 2]);
    }
}
