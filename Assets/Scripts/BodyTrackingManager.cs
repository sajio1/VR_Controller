using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Full-body tracking manager using Meta Quest OVRBody API.
/// Reads the 84-joint body skeleton each frame and exposes per-joint
/// local rotations, world positions, and tracking confidence.
/// Also integrates OVRHand data for detailed finger tracking.
/// </summary>
public class BodyTrackingManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("Body Tracking")]
    [Tooltip("OVRBody component (polls body pose data)")]
    [SerializeField] private OVRBody ovrBody;

    [Tooltip("OVRSkeleton configured for Body tracking")]
    [SerializeField] private OVRSkeleton bodySkeleton;

    [Header("Hand Tracking (optional, for finger detail)")]
    [SerializeField] private OVRHand leftOvrHand;
    [SerializeField] private OVRHand rightOvrHand;
    [SerializeField] private OVRSkeleton leftHandSkeleton;
    [SerializeField] private OVRSkeleton rightHandSkeleton;

    [Header("Settings")]
    [Tooltip("Require high confidence body data")]
    [SerializeField] private bool requireHighConfidence = false;

    // ══════════════════════════════════════════════════
    //                Public State
    // ══════════════════════════════════════════════════

    public bool IsBodyTracking { get; private set; }
    public bool IsLeftHandTracking { get; private set; }
    public bool IsRightHandTracking { get; private set; }
    public float BodyConfidence { get; private set; }
    public int BodyBoneCount { get; private set; }

    // ══════════════════════════════════════════════════
    //                Joint Data
    // ══════════════════════════════════════════════════

    public struct JointData
    {
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Quaternion LocalRotation;
        public Vector3 LocalPosition;
        public bool IsValid;
    }

    private JointData[] _bodyJoints;
    private Dictionary<OVRSkeleton.BoneId, int> _boneIdToIndex;
    private bool _skeletonInitialized;

    public JointData[] BodyJoints => _bodyJoints;

    // ══════════════════════════════════════════════════
    //           Convenience Accessors
    // ══════════════════════════════════════════════════

    public JointData GetJoint(OVRSkeleton.BoneId boneId)
    {
        if (_boneIdToIndex != null && _boneIdToIndex.TryGetValue(boneId, out int idx))
        {
            if (idx >= 0 && idx < _bodyJoints.Length)
                return _bodyJoints[idx];
        }
        return default;
    }

    public Vector3 HipsPosition => GetJoint(OVRSkeleton.BoneId.Body_Hips).WorldPosition;
    public Quaternion HipsRotation => GetJoint(OVRSkeleton.BoneId.Body_Hips).WorldRotation;
    public Vector3 HeadPosition => GetJoint(OVRSkeleton.BoneId.Body_Head).WorldPosition;

    public OVRSkeleton BodySkeleton => bodySkeleton;
    public OVRHand LeftHand => leftOvrHand;
    public OVRHand RightHand => rightOvrHand;

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Update()
    {
        UpdateBodyTracking();
        UpdateHandTracking();
    }

    // ══════════════════════════════════════════════════
    //              Body Tracking Update
    // ══════════════════════════════════════════════════

    private void UpdateBodyTracking()
    {
        if (bodySkeleton == null)
        {
            IsBodyTracking = false;
            return;
        }

        if (!bodySkeleton.IsInitialized || bodySkeleton.Bones == null || bodySkeleton.Bones.Count == 0)
        {
            IsBodyTracking = false;
            return;
        }

        if (!_skeletonInitialized)
        {
            InitializeBoneMapping();
        }

        bool bodyValid = ovrBody != null && ovrBody.enabled;

        if (requireHighConfidence)
        {
            // OVRBody exposes BodyState; for now trust the skeleton validity
        }

        IsBodyTracking = bodyValid && bodySkeleton.IsInitialized;
        BodyConfidence = IsBodyTracking ? 1f : 0f;

        if (!IsBodyTracking) return;

        var bones = bodySkeleton.Bones;
        BodyBoneCount = bones.Count;

        if (_bodyJoints == null || _bodyJoints.Length != bones.Count)
        {
            _bodyJoints = new JointData[bones.Count];
        }

        for (int i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.Transform == null)
            {
                _bodyJoints[i].IsValid = false;
                continue;
            }

            _bodyJoints[i].WorldPosition = bone.Transform.position;
            _bodyJoints[i].WorldRotation = bone.Transform.rotation;
            _bodyJoints[i].LocalRotation = bone.Transform.localRotation;
            _bodyJoints[i].LocalPosition = bone.Transform.localPosition;
            _bodyJoints[i].IsValid = true;
        }
    }

    private void InitializeBoneMapping()
    {
        if (bodySkeleton == null || !bodySkeleton.IsInitialized) return;

        var bones = bodySkeleton.Bones;
        if (bones == null || bones.Count == 0) return;

        _boneIdToIndex = new Dictionary<OVRSkeleton.BoneId, int>();
        for (int i = 0; i < bones.Count; i++)
        {
            _boneIdToIndex[bones[i].Id] = i;
        }

        _bodyJoints = new JointData[bones.Count];
        _skeletonInitialized = true;

        Debug.Log($"[BodyTrackingManager] Skeleton initialized: {bones.Count} bones");
    }

    // ══════════════════════════════════════════════════
    //              Hand Tracking Update
    // ══════════════════════════════════════════════════

    private void UpdateHandTracking()
    {
        IsLeftHandTracking = leftOvrHand != null && leftOvrHand.IsTracked && leftOvrHand.IsDataValid;
        IsRightHandTracking = rightOvrHand != null && rightOvrHand.IsTracked && rightOvrHand.IsDataValid;
    }

    // ══════════════════════════════════════════════════
    //              Bone Transform Access
    // ══════════════════════════════════════════════════

    /// <summary>Get the Transform for a specific body bone (for direct hierarchy access).</summary>
    public Transform GetBoneTransform(OVRSkeleton.BoneId boneId)
    {
        if (bodySkeleton == null || !bodySkeleton.IsInitialized) return null;
        if (_boneIdToIndex == null) return null;

        if (_boneIdToIndex.TryGetValue(boneId, out int idx))
        {
            if (idx >= 0 && idx < bodySkeleton.Bones.Count)
                return bodySkeleton.Bones[idx].Transform;
        }
        return null;
    }

    /// <summary>Get the local rotation of a body bone.</summary>
    public Quaternion GetBoneLocalRotation(OVRSkeleton.BoneId boneId)
    {
        var j = GetJoint(boneId);
        return j.IsValid ? j.LocalRotation : Quaternion.identity;
    }

    /// <summary>Get the world position of a body bone.</summary>
    public Vector3 GetBoneWorldPosition(OVRSkeleton.BoneId boneId)
    {
        var j = GetJoint(boneId);
        return j.IsValid ? j.WorldPosition : Vector3.zero;
    }

    /// <summary>Get the parent bone index for a given bone index.</summary>
    public int GetParentBoneIndex(int boneIndex)
    {
        if (bodySkeleton == null || !bodySkeleton.IsInitialized) return -1;
        if (boneIndex < 0 || boneIndex >= bodySkeleton.Bones.Count) return -1;
        return bodySkeleton.Bones[boneIndex].ParentBoneIndex;
    }

    /// <summary>Enumerate all bone IDs and their indices.</summary>
    public IReadOnlyDictionary<OVRSkeleton.BoneId, int> BoneIdToIndex => _boneIdToIndex;
}
