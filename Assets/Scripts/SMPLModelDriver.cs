using UnityEngine;

/// <summary>
/// Drives the SMPL model skeleton using pose data from SMPLRetargeter.
///
/// Supports two modes:
///   1. ProceduralHumanoid: drives the capsule-based placeholder model
///   2. Imported FBX: drives an imported SMPL model's Transform hierarchy
///
/// Each frame, reads the joint rotations and root position from
/// SMPLRetargeter and applies them to the model's bone transforms
/// with configurable smoothing.
/// </summary>
public class SMPLModelDriver : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private SMPLRetargeter retargeter;

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

    private Transform[] _targetJoints;
    private Quaternion[] _smoothedRotations;
    private Vector3 _smoothedRootPos;
    private bool _initialized;

    public ProceduralHumanoid Model => proceduralModel;
    public bool IsInitialized => _initialized;
    public float RotationSmoothSpeed
    {
        get => rotationSmoothSpeed;
        set => rotationSmoothSpeed = value;
    }

    public void Initialize()
    {
        if (proceduralModel == null)
            proceduralModel = gameObject.AddComponent<ProceduralHumanoid>();

        if (proceduralModel.JointTransforms == null || proceduralModel.JointCount == 0)
            proceduralModel.Build();

        _targetJoints = proceduralModel.JointTransforms;
        int jointCount = SMPLRetargeter.JointCount;
        _smoothedRotations = new Quaternion[jointCount];
        for (int i = 0; i < jointCount; i++)
            _smoothedRotations[i] = Quaternion.identity;

        _smoothedRootPos = proceduralModel.ModelRoot != null
            ? proceduralModel.ModelRoot.localPosition
            : Vector3.zero;

        _initialized = true;
        Debug.Log("[SMPLModelDriver] Initialized");
    }

    public void SetModelLayer(int layer)
    {
        if (proceduralModel != null)
            proceduralModel.SetLayer(layer);
    }

    public Transform GetModelRoot()
    {
        return proceduralModel != null ? proceduralModel.ModelRoot : null;
    }

    private void Start()
    {
        if (!_initialized)
            Initialize();
    }

    private void LateUpdate()
    {
        if (!_initialized || retargeter == null) return;

        var pose = retargeter.CurrentPose;
        if (!pose.IsValid || pose.JointRotations == null) return;

        ApplyPose(pose);
    }

    private void ApplyPose(SMPLPose pose)
    {
        if (_targetJoints == null) return;

        float dt = Time.deltaTime;
        int jointCount = Mathf.Min(pose.JointRotations.Length, _targetJoints.Length);

        for (int i = 0; i < jointCount; i++)
        {
            if (_targetJoints[i] == null) continue;

            _smoothedRotations[i] = Quaternion.Slerp(
                _smoothedRotations[i],
                pose.JointRotations[i],
                1f - Mathf.Exp(-rotationSmoothSpeed * dt));

            _targetJoints[i].localRotation = _smoothedRotations[i];
        }

        if (applyRootPosition && proceduralModel != null && proceduralModel.ModelRoot != null)
        {
            Vector3 targetPos = pose.RootPosition;
            targetPos.y += rootVerticalOffset;

            _smoothedRootPos = Vector3.Lerp(
                _smoothedRootPos,
                targetPos,
                1f - Mathf.Exp(-positionSmoothSpeed * dt));

            if (_targetJoints.Length > 0 && _targetJoints[0] != null)
            {
                Vector3 restPos = ProceduralHumanoid.GetRestPosition(SMPLRetargeter.SMPLJoint.Pelvis);
                _targetJoints[0].localPosition = restPos + _smoothedRootPos;
            }
        }
    }
}
