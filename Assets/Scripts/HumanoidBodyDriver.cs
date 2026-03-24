using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Drives a Unity Humanoid (e.g. SMPL-X with Humanoid rig) from Meta Movement SDK
/// OVRSkeleton. Use this when OVRUnityHumanoidSkeletonRetargeter is not available
/// or you want a single place to wire BodyTracker -> Humanoid.
///
/// Assign: Body Tracker = your MovementSDKBodyTracker (same GameObject as OVRSkeleton).
/// The character must have an Animator with a valid Humanoid Avatar.
/// </summary>
[RequireComponent(typeof(Animator))]
public class HumanoidBodyDriver : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Body tracker that holds OVRBody + OVRSkeleton (Full Body)")]
    [SerializeField] private MovementSDKBodyTracker bodyTracker;

    [Header("Options")]
    [Tooltip("Apply root position from tracking (hip movement)")]
    [SerializeField] private bool applyRootPosition = true;
    [Tooltip("Smoothing for joint rotations (0 = no smooth)")]
    [SerializeField] private float rotationSmoothTime = 0.05f;

    private Animator _animator;
    private Quaternion[] _smoothRotations;
    private Dictionary<HumanBodyBones, Transform> _sourceBones;
    private bool _initialized;

    private struct BoneMap { public HumanBodyBones human; public string[] names; }
    private static readonly BoneMap[] BoneMapping = new BoneMap[]
    {
        new BoneMap { human = HumanBodyBones.Hips,         names = new[] { "Body_Hips", "Hips", "Pelvis" } },
        new BoneMap { human = HumanBodyBones.Spine,       names = new[] { "Body_SpineLower", "SpineLower", "Spine" } },
        new BoneMap { human = HumanBodyBones.Chest,       names = new[] { "Body_SpineMiddle", "SpineMiddle" } },
        new BoneMap { human = HumanBodyBones.UpperChest,  names = new[] { "Body_SpineUpper", "SpineUpper", "Chest" } },
        new BoneMap { human = HumanBodyBones.Neck,        names = new[] { "Body_Neck", "Neck" } },
        new BoneMap { human = HumanBodyBones.Head,        names = new[] { "Body_Head", "Head" } },
        new BoneMap { human = HumanBodyBones.LeftShoulder,  names = new[] { "Body_LeftShoulder", "LeftShoulder" } },
        new BoneMap { human = HumanBodyBones.LeftUpperArm,  names = new[] { "Body_LeftArmUpper", "LeftArmUpper", "LeftUpperArm" } },
        new BoneMap { human = HumanBodyBones.LeftLowerArm,  names = new[] { "Body_LeftArmLower", "LeftArmLower", "LeftLowerArm" } },
        new BoneMap { human = HumanBodyBones.LeftHand,    names = new[] { "Body_LeftHandWrist", "Body_LeftHandPalm", "LeftHandWrist", "LeftHandPalm" } },
        new BoneMap { human = HumanBodyBones.RightShoulder, names = new[] { "Body_RightShoulder", "RightShoulder" } },
        new BoneMap { human = HumanBodyBones.RightUpperArm, names = new[] { "Body_RightArmUpper", "RightArmUpper", "RightUpperArm" } },
        new BoneMap { human = HumanBodyBones.RightLowerArm, names = new[] { "Body_RightArmLower", "RightArmLower", "RightLowerArm" } },
        new BoneMap { human = HumanBodyBones.RightHand,   names = new[] { "Body_RightHandWrist", "Body_RightHandPalm", "RightHandWrist", "RightHandPalm" } },
        new BoneMap { human = HumanBodyBones.LeftUpperLeg,  names = new[] { "Body_LeftUpperLeg", "LeftUpperLeg" } },
        new BoneMap { human = HumanBodyBones.LeftLowerLeg,  names = new[] { "Body_LeftLowerLeg", "LeftLowerLeg" } },
        new BoneMap { human = HumanBodyBones.LeftFoot,    names = new[] { "Body_LeftFootAnkle", "LeftFootAnkle", "LeftAnkle" } },
        new BoneMap { human = HumanBodyBones.LeftToes,    names = new[] { "Body_LeftFootBall", "LeftFootBall" } },
        new BoneMap { human = HumanBodyBones.RightUpperLeg, names = new[] { "Body_RightUpperLeg", "RightUpperLeg" } },
        new BoneMap { human = HumanBodyBones.RightLowerLeg, names = new[] { "Body_RightLowerLeg", "RightLowerLeg" } },
        new BoneMap { human = HumanBodyBones.RightFoot,   names = new[] { "Body_RightFootAnkle", "RightFootAnkle", "RightAnkle" } },
        new BoneMap { human = HumanBodyBones.RightToes,  names = new[] { "Body_RightFootBall", "RightFootBall" } },
    };

    private void Start()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null || !_animator.isHuman)
        {
            Debug.LogWarning("[HumanoidBodyDriver] Animator is missing or not Humanoid.");
            return;
        }
        int n = (int)HumanBodyBones.LastBone + 1;
        _smoothRotations = new Quaternion[n];
        for (int i = 0; i < n; i++)
            _smoothRotations[i] = Quaternion.identity;
        _sourceBones = new Dictionary<HumanBodyBones, Transform>();
        _initialized = true;
    }

    private void LateUpdate()
    {
        if (!_initialized || bodyTracker == null || !bodyTracker.IsTracking)
            return;

        var skeleton = bodyTracker.Skeleton;
        if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            return;

        CacheSourceBones(skeleton);
        ApplySkeletonToHumanoid(skeleton);
    }

    private void CacheSourceBones(OVRSkeleton skeleton)
    {
        _sourceBones.Clear();
        foreach (var map in BoneMapping)
        {
            Transform t = TryFindBone(skeleton, map.names);
            if (t != null)
                _sourceBones[map.human] = t;
        }
    }

    private static Transform TryFindBone(OVRSkeleton skeleton, string[] candidates)
    {
        if (skeleton?.Bones == null) return null;
        int count = skeleton.Bones.Count;
        for (int c = 0; c < candidates.Length; c++)
        {
            string cand = candidates[c];
            if (string.IsNullOrEmpty(cand)) continue;
            string candLower = cand.ToLowerInvariant();
            for (int i = 0; i < count; i++)
            {
                Transform t = skeleton.Bones[i].Transform;
                if (t == null) continue;
                if (t.name != null && t.name.ToLowerInvariant().Contains(candLower))
                    return t;
                string idStr = skeleton.Bones[i].Id.ToString();
                if (!string.IsNullOrEmpty(idStr) && idStr.ToLowerInvariant().Contains(candLower))
                    return t;
            }
        }
        return null;
    }

    private void ApplySkeletonToHumanoid(OVRSkeleton skeleton)
    {
        float dt = Time.deltaTime;
        float t = rotationSmoothTime > 0f ? Mathf.Clamp01(dt / rotationSmoothTime) : 1f;

        foreach (var kv in _sourceBones)
        {
            HumanBodyBones humanBone = kv.Key;
            Transform src = kv.Value;
            Transform dst = _animator.GetBoneTransform(humanBone);
            if (dst == null) continue;

            Quaternion targetRot = src.rotation;
            int idx = (int)humanBone;
            if (idx >= 0 && idx < _smoothRotations.Length)
            {
                _smoothRotations[idx] = Quaternion.Slerp(_smoothRotations[idx], targetRot, t);
                dst.rotation = _smoothRotations[idx];
            }
            else
            {
                dst.rotation = targetRot;
            }
        }

        if (applyRootPosition && _sourceBones.TryGetValue(HumanBodyBones.Hips, out Transform hipsSrc))
        {
            Transform hipsDst = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hipsDst != null)
                hipsDst.position = hipsSrc.position;
        }
    }
}
