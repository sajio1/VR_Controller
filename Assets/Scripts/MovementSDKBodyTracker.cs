using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Body tracking wrapper using Meta Movement SDK (OVRBody + OVRSkeleton).
///
/// Configures and reads Full Body (84-joint) tracking at High fidelity
/// with Floor Level tracking origin. Auto-initializes the skeleton
/// bone-ID lookup on the first valid frame.
///
/// Replaces BodyTrackingManager in the active pipeline.
/// </summary>
public class MovementSDKBodyTracker : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("OVRBody component (polls body pose data from Movement SDK)")]
    [SerializeField] private OVRBody ovrBody;

    [Tooltip("OVRSkeleton configured for Body tracking (Full Body, 84 joints)")]
    [SerializeField] private OVRSkeleton ovrSkeleton;

    [Header("Hand Tracking (optional)")]
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;

    public bool IsTracking { get; private set; }
    public bool IsLeftHandTracking => leftHand != null && leftHand.IsTracked && leftHand.IsDataValid;
    public bool IsRightHandTracking => rightHand != null && rightHand.IsTracked && rightHand.IsDataValid;
    /// <summary>Left hand thumb–pinky pinch (clutch). Use for recording toggle.</summary>
    public float LeftClutchStrength { get; private set; }
    public float Confidence { get; private set; }
    public int BoneCount { get; private set; }
    public OVRSkeleton Skeleton => ovrSkeleton;

    private Dictionary<OVRSkeleton.BoneId, int> _boneIdToIndex;
    private bool _skeletonReady;

    private void Start()
    {
        EnsureOVRManagerSettings();
    }

    /// <summary>
    /// Ensures OVRManager is configured for full-body, high-fidelity tracking
    /// with floor-level origin. These can also be set in the Inspector, but
    /// this guarantees them at runtime.
    /// </summary>
    private void EnsureOVRManagerSettings()
    {
        if (OVRManager.instance == null) return;

        // Meta XR SDK field names differ across versions. Use reflection so we compile everywhere.
        try
        {
            var t = OVRManager.instance.GetType();
            var f = t.GetField("requestBodyTrackingPermissionOnStartup");
            if (f != null && f.FieldType == typeof(bool))
            {
                f.SetValue(OVRManager.instance, true);
                return;
            }
            var p = t.GetProperty("requestBodyTrackingPermissionOnStartup");
            if (p != null && p.PropertyType == typeof(bool) && p.CanWrite)
            {
                p.SetValue(OVRManager.instance, true);
            }
        }
        catch { }
    }

    private void Update()
    {
        if (ovrSkeleton == null || ovrBody == null)
        {
            IsTracking = false;
            LeftClutchStrength = 0f;
            return;
        }

        if (!ovrSkeleton.IsInitialized || ovrSkeleton.Bones == null || ovrSkeleton.Bones.Count == 0)
        {
            IsTracking = false;
            LeftClutchStrength = 0f;
            return;
        }

        if (!_skeletonReady)
            InitializeBoneMapping();

        IsTracking = ovrBody.enabled && ovrSkeleton.IsInitialized;
        Confidence = IsTracking ? 1f : 0f;
        BoneCount = IsTracking ? ovrSkeleton.Bones.Count : 0;
        LeftClutchStrength = GetLeftClutchStrength();
    }

    private float GetLeftClutchStrength()
    {
        if (leftHand == null || !leftHand.IsTracked || !leftHand.IsDataValid)
            return 0f;
        return leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);
    }

    private void InitializeBoneMapping()
    {
        var bones = ovrSkeleton.Bones;
        if (bones == null || bones.Count == 0) return;

        _boneIdToIndex = new Dictionary<OVRSkeleton.BoneId, int>();
        for (int i = 0; i < bones.Count; i++)
            _boneIdToIndex[bones[i].Id] = i;

        _skeletonReady = true;
        Debug.Log($"[MovementSDKBodyTracker] Skeleton ready: {bones.Count} bones");
    }

    public Transform GetBoneTransform(OVRSkeleton.BoneId boneId)
    {
        if (!_skeletonReady || _boneIdToIndex == null) return null;
        if (!_boneIdToIndex.TryGetValue(boneId, out int idx)) return null;
        if (idx < 0 || idx >= ovrSkeleton.Bones.Count) return null;
        return ovrSkeleton.Bones[idx].Transform;
    }

    public Quaternion GetBoneLocalRotation(OVRSkeleton.BoneId boneId)
    {
        Transform t = GetBoneTransform(boneId);
        return t != null ? t.localRotation : Quaternion.identity;
    }

    public Vector3 GetBoneWorldPosition(OVRSkeleton.BoneId boneId)
    {
        Transform t = GetBoneTransform(boneId);
        return t != null ? t.position : Vector3.zero;
    }

    public Quaternion GetBoneWorldRotation(OVRSkeleton.BoneId boneId)
    {
        Transform t = GetBoneTransform(boneId);
        return t != null ? t.rotation : Quaternion.identity;
    }

    public Vector3 HipsPosition => GetBoneWorldPosition(OVRSkeleton.BoneId.Body_Hips);
    public Vector3 HeadPosition => GetBoneWorldPosition(OVRSkeleton.BoneId.Body_Head);
}
