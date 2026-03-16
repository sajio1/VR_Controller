using UnityEngine;

/// <summary>
/// Drives the SMPL-H model skeleton using normalized pose data from PoseNormalizer.
///
/// Supports two modes:
///   1. ProceduralHumanoid: drives the capsule-based placeholder model
///   2. Imported FBX: drives an imported SMPL-H model's Transform hierarchy
///
/// Each frame, reads the normalized joint rotations and root position from
/// PoseNormalizer and applies them to the model's bone transforms.
/// </summary>
public class SMPLModelDriver : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("Data Source")]
    [SerializeField] private PoseNormalizer poseNormalizer;

    [Header("Model")]
    [Tooltip("Procedural humanoid placeholder (auto-created if none assigned)")]
    [SerializeField] private ProceduralHumanoid proceduralModel;

    [Header("Smoothing")]
    [Tooltip("Rotation interpolation speed (higher = more responsive, lower = smoother)")]
    [SerializeField] private float rotationSmoothSpeed = 15f;
    [Tooltip("Position interpolation speed")]
    [SerializeField] private float positionSmoothSpeed = 12f;

    [Header("Root Motion")]
    [Tooltip("Apply root (hip) position from tracking")]
    [SerializeField] private bool applyRootPosition = true;
    [Tooltip("Vertical offset for the model root")]
    [SerializeField] private float rootVerticalOffset = 0f;

    // ══════════════════════════════════════════════════
    //                  Internal State
    // ══════════════════════════════════════════════════

    private Transform[] _targetJoints;
    private Quaternion[] _smoothedRotations;
    private Vector3 _smoothedRootPos;
    private bool _initialized;

    // ══════════════════════════════════════════════════
    //                  Public API
    // ══════════════════════════════════════════════════

    public ProceduralHumanoid Model => proceduralModel;
    public bool IsInitialized => _initialized;

    /// <summary>Initialize or re-initialize the model driver.</summary>
    public void Initialize()
    {
        if (proceduralModel == null)
        {
            proceduralModel = gameObject.AddComponent<ProceduralHumanoid>();
        }

        if (proceduralModel.JointTransforms == null || proceduralModel.JointCount == 0)
        {
            proceduralModel.Build();
        }

        _targetJoints = proceduralModel.JointTransforms;
        int jointCount = (int)PoseNormalizer.SMPLJoint.Count;
        _smoothedRotations = new Quaternion[jointCount];
        for (int i = 0; i < jointCount; i++)
            _smoothedRotations[i] = Quaternion.identity;

        _smoothedRootPos = proceduralModel.ModelRoot != null
            ? proceduralModel.ModelRoot.localPosition
            : Vector3.zero;

        _initialized = true;
        Debug.Log("[SMPLModelDriver] Initialized");
    }

    /// <summary>Set the layer for the model (for camera isolation).</summary>
    public void SetModelLayer(int layer)
    {
        if (proceduralModel != null)
            proceduralModel.SetLayer(layer);
    }

    /// <summary>Get the model root transform (for camera framing).</summary>
    public Transform GetModelRoot()
    {
        return proceduralModel != null ? proceduralModel.ModelRoot : null;
    }

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        if (!_initialized)
            Initialize();
    }

    private void LateUpdate()
    {
        if (!_initialized || poseNormalizer == null) return;

        var pose = poseNormalizer.CurrentPose;
        if (!pose.IsValid || pose.JointRotations == null) return;

        ApplyPose(pose);
    }

    // ══════════════════════════════════════════════════
    //              Pose Application
    // ══════════════════════════════════════════════════

    private void ApplyPose(PoseNormalizer.NormalizedPose pose)
    {
        if (_targetJoints == null) return;

        float dt = Time.deltaTime;
        int jointCount = Mathf.Min(pose.JointRotations.Length, _targetJoints.Length);

        for (int i = 0; i < jointCount; i++)
        {
            if (_targetJoints[i] == null) continue;

            Quaternion targetRot = pose.JointRotations[i];

            // Smooth interpolation
            _smoothedRotations[i] = Quaternion.Slerp(
                _smoothedRotations[i],
                targetRot,
                1f - Mathf.Exp(-rotationSmoothSpeed * dt)
            );

            _targetJoints[i].localRotation = _smoothedRotations[i];
        }

        // Apply root position
        if (applyRootPosition && proceduralModel != null && proceduralModel.ModelRoot != null)
        {
            Vector3 targetPos = pose.RootPosition;
            targetPos.y += rootVerticalOffset;

            _smoothedRootPos = Vector3.Lerp(
                _smoothedRootPos,
                targetPos,
                1f - Mathf.Exp(-positionSmoothSpeed * dt)
            );

            // Apply to the pelvis joint (index 0) rather than the model root,
            // so the model root stays at origin for camera framing
            if (_targetJoints.Length > 0 && _targetJoints[0] != null)
            {
                Vector3 restPos = ProceduralHumanoid.GetRestPosition(PoseNormalizer.SMPLJoint.Pelvis);
                _targetJoints[0].localPosition = restPos + _smoothedRootPos;
            }
        }
    }
}
