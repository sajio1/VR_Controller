using UnityEngine;

/// <summary>
/// [DEPRECATED] Hand-only tracking controller (wrist pose + pinch grip).
///
/// Superseded by BodyTrackingManager which provides full-body tracking
/// via OVRBody (84 joints). Kept for reference only.
/// </summary>
[System.Obsolete("Use BodyTrackingManager for full-body tracking")]
public class HandTrackingController : MonoBehaviour
{
    // ──────────────── 输入模式 ────────────────
    public enum InputMode
    {
        Hand,       // 手势追踪模式
        Controller  // 手柄控制器模式
    }

    // ──────────────── Inspector 字段 ────────────────

    [Header("右手手势追踪引用")]
    [Tooltip("拖入挂有 OVRHand 组件的 RightHandTracking 对象")]
    [SerializeField] private OVRHand rightOvrHand;

    [Tooltip("拖入挂有 OVRSkeleton 组件的 RightHandTracking 对象")]
    [SerializeField] private OVRSkeleton rightOvrSkeleton;

    [Header("左手手势追踪引用")]
    [Tooltip("拖入挂有 OVRHand 组件的 LeftHandTracking 对象")]
    [SerializeField] private OVRHand leftOvrHand;

    [Tooltip("拖入挂有 OVRSkeleton 组件的 LeftHandTracking 对象")]
    [SerializeField] private OVRSkeleton leftOvrSkeleton;

    [Header("输入模式")]
    [Tooltip("当前输入模式（可在运行时通过左手柄 Menu 键切换）")]
    [SerializeField] private InputMode currentMode = InputMode.Hand;

    [Header("手势模式设置")]
    [Tooltip("是否要求高置信度数据才算有效输入")]
    [SerializeField] private bool requireHighConfidence = true;

    [Header("手柄模式设置")]
    [Tooltip("Grip 按钮作为使能开关的阈值（按下超过此值才发送控制指令）")]
    [SerializeField] private float gripEnableThreshold = 0.5f;

    // ──────────────── 对外只读属性 — 右手 ────────────────

    /// <summary>当前输入模式</summary>
    public InputMode CurrentMode => currentMode;

    /// <summary>右手目标位置（世界坐标）</summary>
    public Vector3 RightTargetPosition { get; private set; }

    /// <summary>右手目标旋转（世界坐标）</summary>
    public Quaternion RightTargetRotation { get; private set; }

    /// <summary>右手夹爪值 0~1（0=张开，1=完全闭合）</summary>
    public float RightGripperValue { get; private set; }

    /// <summary>右手输入是否有效（追踪正常 且 使能条件满足）</summary>
    public bool IsRightInputActive { get; private set; }

    /// <summary>右手手势追踪是否正常</summary>
    public bool IsRightHandTracking { get; private set; }

    /// <summary>右手柄是否已连接</summary>
    public bool IsRightControllerConnected { get; private set; }

    /// <summary>右手中指捏合强度 0~1（用于 Clutch 控制）</summary>
    public float RightMiddlePinchStrength { get; private set; }

    // ──────────────── 对外只读属性 — 左手 ────────────────

    /// <summary>左手目标位置（世界坐标）</summary>
    public Vector3 LeftTargetPosition { get; private set; }

    /// <summary>左手目标旋转（世界坐标）</summary>
    public Quaternion LeftTargetRotation { get; private set; }

    /// <summary>左手夹爪值 0~1（0=张开，1=完全闭合）</summary>
    public float LeftGripperValue { get; private set; }

    /// <summary>左手输入是否有效</summary>
    public bool IsLeftInputActive { get; private set; }

    /// <summary>左手手势追踪是否正常</summary>
    public bool IsLeftHandTracking { get; private set; }

    /// <summary>左手柄是否已连接</summary>
    public bool IsLeftControllerConnected { get; private set; }

    /// <summary>左手中指捏合强度 0~1（用于 Clutch 控制）</summary>
    public float LeftMiddlePinchStrength { get; private set; }

    /// <summary>右手中指-拇指捏合中点位置（Clutch 进度条定位用）</summary>
    public Vector3 RightPinchMidpoint { get; private set; }

    /// <summary>左手中指-拇指捏合中点位置（Clutch 进度条定位用）</summary>
    public Vector3 LeftPinchMidpoint { get; private set; }

    // ──────────────── 向后兼容属性（映射到右手） ────────────────

    /// <summary>当前目标位置（向后兼容，等同于 RightTargetPosition）</summary>
    public Vector3 TargetPosition => RightTargetPosition;

    /// <summary>当前目标旋转（向后兼容，等同于 RightTargetRotation）</summary>
    public Quaternion TargetRotation => RightTargetRotation;

    /// <summary>夹爪值（向后兼容，等同于 RightGripperValue）</summary>
    public float GripperValue => RightGripperValue;

    /// <summary>输入是否有效（向后兼容，等同于 IsRightInputActive）</summary>
    public bool IsInputActive => IsRightInputActive;

    /// <summary>手势追踪是否正常（向后兼容，等同于 IsRightHandTracking）</summary>
    public bool IsHandTracking => IsRightHandTracking;

    /// <summary>手柄是否已连接（向后兼容，等同于 IsRightControllerConnected）</summary>
    public bool IsControllerConnected => IsRightControllerConnected;

    // ──────────────── 内部状态 ────────────────

    private OVRSkeleton.BoneId wristBoneId = OVRSkeleton.BoneId.Hand_WristRoot;

    // 右手骨骼状态
    private Transform _rightWristTransform;
    private bool _rightSkeletonInitialized;

    // 左手骨骼状态
    private Transform _leftWristTransform;
    private bool _leftSkeletonInitialized;

    // 右手 pinch 骨骼缓存（用于 clutch 进度条定位在中指-拇指之间）
    private Transform _rightThumbTip;
    private Transform _rightMiddleTip;
    private bool _rightPinchBonesFound;

    // 左手 pinch 骨骼缓存
    private Transform _leftThumbTip;
    private Transform _leftMiddleTip;
    private bool _leftPinchBonesFound;

    // ──────────────── Unity 生命周期 ────────────────

    private void Update()
    {
        // 左手柄 Menu 键切换输入模式
        if (OVRInput.GetDown(OVRInput.Button.Start))
        {
            ToggleInputMode();
        }

        switch (currentMode)
        {
            case InputMode.Hand:
                UpdateRightHandMode();
                UpdateLeftHandMode();
                break;
            case InputMode.Controller:
                UpdateRightControllerMode();
                UpdateLeftControllerMode();
                break;
        }
    }

    // ──────────────── 模式切换 ────────────────

    /// <summary>切换输入模式</summary>
    public void ToggleInputMode()
    {
        currentMode = currentMode == InputMode.Hand ? InputMode.Controller : InputMode.Hand;
        Debug.Log($"[HandTrackingController] 输入模式切换为: {currentMode}");
    }

    /// <summary>设置输入模式</summary>
    public void SetInputMode(InputMode mode)
    {
        currentMode = mode;
        Debug.Log($"[HandTrackingController] 输入模式设为: {currentMode}");
    }

    // ──────────────── 右手手势模式更新 ────────────────

    private void UpdateRightHandMode()
    {
        IsRightControllerConnected = false;

        if (rightOvrHand == null || rightOvrSkeleton == null)
        {
            IsRightInputActive = false;
            IsRightHandTracking = false;
            RightMiddlePinchStrength = 0f;
            return;
        }

        // 检查追踪状态
        bool isTracked = rightOvrHand.IsTracked && rightOvrHand.IsDataValid;
        if (requireHighConfidence)
        {
            isTracked = isTracked && rightOvrHand.IsDataHighConfidence;
        }

        IsRightHandTracking = isTracked;

        if (!isTracked)
        {
            IsRightInputActive = false;
            RightMiddlePinchStrength = 0f;
            return;
        }

        // 尝试获取手腕骨骼 Transform
        if (!_rightSkeletonInitialized || _rightWristTransform == null)
        {
            _rightWristTransform = FindWristBone(rightOvrSkeleton);
            _rightSkeletonInitialized = _rightWristTransform != null;
        }

        if (_rightWristTransform == null)
        {
            IsRightInputActive = false;
            return;
        }

        // 读取手腕位姿
        RightTargetPosition = _rightWristTransform.position;
        RightTargetRotation = _rightWristTransform.rotation;

        // 食指捏合强度作为夹爪值
        RightGripperValue = rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

        // 中指捏合强度（Clutch 控制用）
        RightMiddlePinchStrength = rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);

        // 查找并缓存 pinch 骨骼（thumb tip + middle tip），用于 clutch 进度条定位
        if (!_rightPinchBonesFound || _rightThumbTip == null || _rightMiddleTip == null)
        {
            _rightThumbTip = FindBoneTransform(rightOvrSkeleton, OVRSkeleton.BoneId.Hand_ThumbTip);
            if (_rightThumbTip == null) _rightThumbTip = FindBoneTransform(rightOvrSkeleton, OVRSkeleton.BoneId.Hand_Thumb3);
            _rightMiddleTip = FindBoneTransform(rightOvrSkeleton, OVRSkeleton.BoneId.Hand_MiddleTip);
            if (_rightMiddleTip == null) _rightMiddleTip = FindBoneTransform(rightOvrSkeleton, OVRSkeleton.BoneId.Hand_Middle3);
            _rightPinchBonesFound = (_rightThumbTip != null && _rightMiddleTip != null);
        }
        RightPinchMidpoint = _rightPinchBonesFound
            ? (_rightThumbTip.position + _rightMiddleTip.position) * 0.5f
            : RightTargetPosition + Vector3.up * 0.05f;

        IsRightInputActive = true;
    }

    // ──────────────── 左手手势模式更新 ────────────────

    private void UpdateLeftHandMode()
    {
        IsLeftControllerConnected = false;

        if (leftOvrHand == null || leftOvrSkeleton == null)
        {
            IsLeftInputActive = false;
            IsLeftHandTracking = false;
            LeftMiddlePinchStrength = 0f;
            return;
        }

        // 检查追踪状态
        bool isTracked = leftOvrHand.IsTracked && leftOvrHand.IsDataValid;
        if (requireHighConfidence)
        {
            isTracked = isTracked && leftOvrHand.IsDataHighConfidence;
        }

        IsLeftHandTracking = isTracked;

        if (!isTracked)
        {
            IsLeftInputActive = false;
            LeftMiddlePinchStrength = 0f;
            return;
        }

        // 尝试获取手腕骨骼 Transform
        if (!_leftSkeletonInitialized || _leftWristTransform == null)
        {
            _leftWristTransform = FindWristBone(leftOvrSkeleton);
            _leftSkeletonInitialized = _leftWristTransform != null;
        }

        if (_leftWristTransform == null)
        {
            IsLeftInputActive = false;
            return;
        }

        // 读取手腕位姿
        LeftTargetPosition = _leftWristTransform.position;
        LeftTargetRotation = _leftWristTransform.rotation;

        // 食指捏合强度作为夹爪值
        LeftGripperValue = leftOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

        // 中指捏合强度（Clutch 控制用）
        LeftMiddlePinchStrength = leftOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);

        // 查找并缓存 pinch 骨骼（thumb tip + middle tip）
        if (!_leftPinchBonesFound || _leftThumbTip == null || _leftMiddleTip == null)
        {
            _leftThumbTip = FindBoneTransform(leftOvrSkeleton, OVRSkeleton.BoneId.Hand_ThumbTip);
            if (_leftThumbTip == null) _leftThumbTip = FindBoneTransform(leftOvrSkeleton, OVRSkeleton.BoneId.Hand_Thumb3);
            _leftMiddleTip = FindBoneTransform(leftOvrSkeleton, OVRSkeleton.BoneId.Hand_MiddleTip);
            if (_leftMiddleTip == null) _leftMiddleTip = FindBoneTransform(leftOvrSkeleton, OVRSkeleton.BoneId.Hand_Middle3);
            _leftPinchBonesFound = (_leftThumbTip != null && _leftMiddleTip != null);
        }
        LeftPinchMidpoint = _leftPinchBonesFound
            ? (_leftThumbTip.position + _leftMiddleTip.position) * 0.5f
            : LeftTargetPosition + Vector3.up * 0.05f;

        IsLeftInputActive = true;
    }

    // ──────────────── 骨骼查找 ────────────────

    /// <summary>从 OVRSkeleton 的 Bones 列表中查找指定骨骼 Transform</summary>
    private Transform FindBoneTransform(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
    {
        if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            return null;
        foreach (var bone in skeleton.Bones)
        {
            if (bone.Id == boneId)
                return bone.Transform;
        }
        return null;
    }

    /// <summary>从 OVRSkeleton 的 Bones 列表中查找手腕骨骼</summary>
    private Transform FindWristBone(OVRSkeleton skeleton)
    {
        if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            return null;

        foreach (var bone in skeleton.Bones)
        {
            if (bone.Id == wristBoneId)
            {
                return bone.Transform;
            }
        }

        // 如果找不到 WristRoot，取第一个骨骼作为 fallback
        if (skeleton.Bones.Count > 0)
        {
            return skeleton.Bones[0].Transform;
        }

        return null;
    }

    // ──────────────── 骨骼数据 API（供 HandSkeletonVisualizer 使用）────────────────

    /// <summary>
    /// 获取指定手的所有骨骼世界坐标位置。
    /// 返回 null 表示骨骼数据不可用。
    /// </summary>
    public Vector3[] GetAllBonePositions(bool isRightHand)
    {
        OVRSkeleton skeleton = isRightHand ? rightOvrSkeleton : leftOvrSkeleton;
        if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null || skeleton.Bones.Count == 0)
            return null;

        Vector3[] positions = new Vector3[skeleton.Bones.Count];
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            if (skeleton.Bones[i].Transform != null)
                positions[i] = skeleton.Bones[i].Transform.position;
        }
        return positions;
    }

    /// <summary>获取指定手的骨骼数量</summary>
    public int GetBoneCount(bool isRightHand)
    {
        OVRSkeleton skeleton = isRightHand ? rightOvrSkeleton : leftOvrSkeleton;
        if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            return 0;
        return skeleton.Bones.Count;
    }

    /// <summary>获取指定手的 OVRSkeleton 引用（高级用途）</summary>
    public OVRSkeleton GetSkeleton(bool isRightHand)
    {
        return isRightHand ? rightOvrSkeleton : leftOvrSkeleton;
    }

    /// <summary>获取指定手的 OVRHand 引用（高级用途）</summary>
    public OVRHand GetHand(bool isRightHand)
    {
        return isRightHand ? rightOvrHand : leftOvrHand;
    }

    /// <summary>获取指定手的 OVRMesh 引用（通过 OVRSkeleton 自动查找）</summary>
    public OVRMesh GetMesh(bool isRightHand)
    {
        OVRSkeleton skeleton = isRightHand ? rightOvrSkeleton : leftOvrSkeleton;
        if (skeleton == null) return null;
        
        // OVRMesh 通常挂在与 OVRSkeleton 相同的 GameObject 上
        OVRMesh mesh = skeleton.GetComponent<OVRMesh>();
        if (mesh == null)
        {
            mesh = skeleton.GetComponentInChildren<OVRMesh>();
        }
        if (mesh == null)
        {
            mesh = skeleton.GetComponentInParent<OVRMesh>();
        }
        return mesh;
    }

    // ──────────────── 右手手柄模式更新 ────────────────

    private void UpdateRightControllerMode()
    {
        IsRightHandTracking = false;

        // 检查右手柄是否连接
        OVRInput.Controller activeController = OVRInput.GetActiveController();
        IsRightControllerConnected = (activeController & OVRInput.Controller.RTouch) != 0;

        if (!IsRightControllerConnected)
        {
            IsRightInputActive = false;
            return;
        }

        // 右手柄位姿
        RightTargetPosition = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        RightTargetRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);

        // 右手食指扳机作为夹爪值
        RightGripperValue = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);

        // Grip 按钮作为使能开关（按住 Grip 才发送指令）
        float gripValue = OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger);
        IsRightInputActive = gripValue >= gripEnableThreshold;
    }

    // ──────────────── 左手手柄模式更新 ────────────────

    private void UpdateLeftControllerMode()
    {
        IsLeftHandTracking = false;

        // 检查左手柄是否连接
        OVRInput.Controller activeController = OVRInput.GetActiveController();
        IsLeftControllerConnected = (activeController & OVRInput.Controller.LTouch) != 0;

        if (!IsLeftControllerConnected)
        {
            IsLeftInputActive = false;
            return;
        }

        // 左手柄位姿
        LeftTargetPosition = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch);
        LeftTargetRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.LTouch);

        // 左手食指扳机作为夹爪值
        LeftGripperValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);

        // Grip 按钮作为使能开关
        float gripValue = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger);
        IsLeftInputActive = gripValue >= gripEnableThreshold;
    }
}
