using UnityEngine;

/// <summary>
/// Play 模式下按 M 打印：肩宽（默认上臂根）、锁骨间距(可选)、上臂/前臂、臂展(腕↔腕)。
/// 世界坐标距离（米）。挂到带 Humanoid Animator 的角色上（如 smplx-male-basic）。
/// </summary>
[RequireComponent(typeof(Animator))]
public class HumanoidBoneMeasurementDebug : MonoBehaviour
{
    [Header("按键")]
    [SerializeField] private KeyCode logKey = KeyCode.M;

    [Header("肩宽定义")]
    [Tooltip("false=用上臂根 LeftUpperArm↔RightUpperArm（接近真实肩宽，推荐）。true=用 Humanoid 的 LeftShoulder（常为锁骨 collar，间距很小，易只有 ~9cm）")]
    [SerializeField] private bool useCollarBonesForShoulderWidth = false;

    [Tooltip("额外打印锁骨左右间距（Humanoid LeftShoulder 骨骼），便于对比")]
    [SerializeField] private bool alsoLogCollarSpan = true;

    private Animator _anim;

    private void Awake()
    {
        _anim = GetComponent<Animator>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(logKey))
            LogMeasurements();
    }

    [ContextMenu("Log Bone Measurements Now")]
    public void LogMeasurements()
    {
        if (_anim == null) _anim = GetComponent<Animator>();
        if (_anim == null || !_anim.isHuman)
        {
            Debug.LogWarning("[HumanoidBoneMeasurementDebug] 需要 Humanoid Animator。");
            return;
        }

        Transform lWide = GetShoulderWidthPoint(true);
        Transform rWide = GetShoulderWidthPoint(false);
        if (lWide == null || rWide == null)
        {
            Debug.LogWarning("[HumanoidBoneMeasurementDebug] 无法解析左右肩宽参考骨骼。");
            return;
        }

        float shoulderWidth = Vector3.Distance(lWide.position, rWide.position);

        float lUpper = ArmSegmentLength(true, out string lUpperNote);
        float rUpper = ArmSegmentLength(false, out string rUpperNote);
        float lFore = ForearmLength(true, out string lForeNote);
        float rFore = ForearmLength(false, out string rForeNote);
        float span = ArmSpan(out string spanNote);

        Transform lc = null, rc = null;
        float collarSpan = float.NaN;
        if (alsoLogCollarSpan && !useCollarBonesForShoulderWidth)
        {
            lc = _anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
            rc = _anim.GetBoneTransform(HumanBodyBones.RightShoulder);
            if (lc != null && rc != null)
                collarSpan = Vector3.Distance(lc.position, rc.position);
        }

        const string tag = "[HumanoidBoneMeasurementDebug]";
        // 单行汇总：Console 里一眼能看到/复制全部数字（单位：米）
        string sum = $"{tag} ★汇总(米) 肩宽={FmtShort(shoulderWidth)} | 左肩→肘={FmtShort(lUpper)} 右肩→肘={FmtShort(rUpper)} | 左肘→腕={FmtShort(lFore)} 右肘→腕={FmtShort(rFore)} | 腕↔腕臂展={FmtShort(span)}";
        if (!float.IsNaN(collarSpan)) sum += $" | 锁骨间距={FmtShort(collarSpan)}";
        Debug.Log(sum);

        Debug.Log($"{tag} —— 明细（世界坐标）——");
        Debug.Log($"{tag} 肩宽 ({lWide.name} ↔ {rWide.name}): {FmtM(shoulderWidth)}");

        if (lc != null && rc != null && !float.IsNaN(collarSpan))
            Debug.Log($"{tag} 锁骨参考间距 ({lc.name} ↔ {rc.name}): {FmtM(collarSpan)} （通常远小于真实肩宽，仅作对照）");

        Debug.Log($"{tag} 左上臂 肩→肘: {FmtM(lUpper)}  [{lUpperNote}]");
        Debug.Log($"{tag} 右上臂 肩→肘: {FmtM(rUpper)}  [{rUpperNote}]");
        Debug.Log($"{tag} 左前臂 肘→腕: {FmtM(lFore)}  [{lForeNote}]");
        Debug.Log($"{tag} 右前臂 肘→腕: {FmtM(rFore)}  [{rForeNote}]");
        Debug.Log($"{tag} 臂展(腕↔腕): {FmtM(span)}  [{spanNote}]");
    }

    private static string FmtShort(float meters)
    {
        if (float.IsNaN(meters)) return "NaN";
        return $"{meters:F3}";
    }

    private static string FmtM(float meters)
    {
        if (float.IsNaN(meters)) return "NaN";
        return $"{meters:F4} m  ({meters * 100f:F1} cm)";
    }

    /// <summary>左右腕（Hand）世界坐标距离，双臂侧伸时接近臂展。</summary>
    private float ArmSpan(out string note)
    {
        var lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
        var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        note = $"{BoneName(lh)} ↔ {BoneName(rh)}";
        if (lh == null || rh == null) return float.NaN;
        return Vector3.Distance(lh.position, rh.position);
    }

    private Transform GetShoulderWidthPoint(bool left)
    {
        if (useCollarBonesForShoulderWidth)
        {
            var t = _anim.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
            if (t != null) return t;
        }
        return _anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
    }

    /// <summary>肩关节 → 肘（UpperArm.position 到 LowerArm.position）。</summary>
    private float ArmSegmentLength(bool left, out string note)
    {
        var upper = _anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        note = $"{BoneName(upper)} → {BoneName(lower)}";
        if (upper == null || lower == null) return float.NaN;
        return Vector3.Distance(upper.position, lower.position);
    }

    /// <summary>肘 → 腕（LowerArm.position 到 Hand.position）。</summary>
    private float ForearmLength(bool left, out string note)
    {
        var lower = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = _anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        note = $"{BoneName(lower)} → {BoneName(hand)}";
        if (lower == null || hand == null) return float.NaN;
        return Vector3.Distance(lower.position, hand.position);
    }

    private static string BoneName(Transform t) => t != null ? t.name : "(null)";
}
