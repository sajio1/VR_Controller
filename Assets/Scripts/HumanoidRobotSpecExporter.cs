using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 导出「人体→机械臂」用的规格 JSON：SMPL-X β=0 说明、上肢骨段长度（米）、模型版本。
/// .pkl/.npz 需自行从 https://smpl-x.is.tue.mpg.de 下载（许可不允许随工程分发）。
/// 挂到 smplx-male-basic（Humanoid Animator）上；Play 下按 E 或右键组件菜单导出。
/// </summary>
[RequireComponent(typeof(Animator))]
public class HumanoidRobotSpecExporter : MonoBehaviour
{
    [SerializeField] private KeyCode exportKey = KeyCode.E;

    [Tooltip("写入 Application.persistentDataPath；false 则写入 Application.dataPath 上一级（仅 Editor）")]
    [SerializeField] private bool writeToPersistentDataPath = true;

    [SerializeField] private string fileName = "humanoid_robot_spec.json";

    [SerializeField] private bool useCollarForShoulderWidth;

    private Animator _anim;

    private void Awake()
    {
        _anim = GetComponent<Animator>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(exportKey))
            Export();
    }

    [ContextMenu("Export humanoid_robot_spec.json")]
    public void Export()
    {
        if (_anim == null) _anim = GetComponent<Animator>();
        if (_anim == null || !_anim.isHuman)
        {
            Debug.LogWarning("[HumanoidRobotSpecExporter] 需要 Humanoid Animator。");
            return;
        }

        var spec = BuildSpec(_anim, useCollarForShoulderWidth);
        string json = ToJson(spec);

        string dir = Application.persistentDataPath;
#if UNITY_EDITOR
        if (!writeToPersistentDataPath)
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#endif

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, json, Encoding.UTF8);
        Debug.Log($"[HumanoidRobotSpecExporter] 已写入: {path}");
    }

    private static RobotHumanoidSpec BuildSpec(Animator anim, bool collarShoulderWidth)
    {
        var s = new RobotHumanoidSpec
        {
            model_id = "SMPL-X v1.1 male basic (MPI Unity package, baked neutral shape)",
            smplx_shape_betas_10d = new float[10],
            smplx_expression_10d = new float[10],
            unity_humanoid_segment_lengths_m = new SegmentLengths(),
            official_files_note =
                "SMPL-X v1.1 male weights: register at https://smpl-x.is.tue.mpg.de and download model files (.npz/.pkl). " +
                "Same license as Unity package; not shipped in this repo. " +
                "Unity JSON template joints: Assets/ThirdParty/SMPLX/Resources/smplx_betas_to_joints_male.json (key template_J, 55x3 m)."
        };

        Transform lWide = GetShoulderPoint(anim, true, collarShoulderWidth);
        Transform rWide = GetShoulderPoint(anim, false, collarShoulderWidth);
        if (lWide != null && rWide != null)
            s.unity_humanoid_segment_lengths_m.shoulder_width_upper_arm_roots = Vector3.Distance(lWide.position, rWide.position);

        var lc = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
        var rc = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
        if (lc != null && rc != null)
            s.unity_humanoid_segment_lengths_m.clavicle_span_reference = Vector3.Distance(lc.position, rc.position);

        s.unity_humanoid_segment_lengths_m.left_upper_arm = SegLen(anim, true, true);
        s.unity_humanoid_segment_lengths_m.right_upper_arm = SegLen(anim, false, true);
        s.unity_humanoid_segment_lengths_m.left_forearm = SegLen(anim, true, false);
        s.unity_humanoid_segment_lengths_m.right_forearm = SegLen(anim, false, false);

        var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (lh != null && rh != null)
            s.unity_humanoid_segment_lengths_m.wrist_to_wrist_span = Vector3.Distance(lh.position, rh.position);

        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (hips != null && head != null)
            s.unity_humanoid_segment_lengths_m.hips_to_head = Vector3.Distance(hips.position, head.position);

        s.export_unity_time_utc = DateTime.UtcNow.ToString("o");
        s.export_pose_note =
            "Lengths are world-space 3D distances at export instant; use T-pose / known pose for reproducibility.";
        return s;
    }

    private static Transform GetShoulderPoint(Animator anim, bool left, bool collar)
    {
        if (collar)
        {
            var t = anim.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
            if (t != null) return t;
        }
        return anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
    }

    private static float SegLen(Animator anim, bool left, bool upperArm)
    {
        var upper = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (upperArm)
        {
            if (upper == null || lower == null) return float.NaN;
            return Vector3.Distance(upper.position, lower.position);
        }
        if (lower == null || hand == null) return float.NaN;
        return Vector3.Distance(lower.position, hand.position);
    }

    private static string ToJson(RobotHumanoidSpec s)
    {
        var b = new StringBuilder(1024);
        b.Append("{\n");
        b.Append("  \"model_id\": ").Append(JsonEscape(s.model_id)).Append(",\n");
        b.Append("  \"export_unity_time_utc\": ").Append(JsonEscape(s.export_unity_time_utc)).Append(",\n");
        b.Append("  \"export_pose_note\": ").Append(JsonEscape(s.export_pose_note)).Append(",\n");
        b.Append("  \"smplx_shape_betas_10d\": ").Append(FloatArr(s.smplx_shape_betas_10d)).Append(",\n");
        b.Append("  \"smplx_expression_10d\": ").Append(FloatArr(s.smplx_expression_10d)).Append(",\n");
        b.Append("  \"unity_humanoid_segment_lengths_m\": {\n");
        var u = s.unity_humanoid_segment_lengths_m;
        b.Append("    \"shoulder_width_upper_arm_roots\": ").Append(F(u.shoulder_width_upper_arm_roots)).Append(",\n");
        b.Append("    \"clavicle_span_reference\": ").Append(F(u.clavicle_span_reference)).Append(",\n");
        b.Append("    \"left_upper_arm_shoulder_to_elbow\": ").Append(F(u.left_upper_arm)).Append(",\n");
        b.Append("    \"right_upper_arm_shoulder_to_elbow\": ").Append(F(u.right_upper_arm)).Append(",\n");
        b.Append("    \"left_forearm_elbow_to_wrist\": ").Append(F(u.left_forearm)).Append(",\n");
        b.Append("    \"right_forearm_elbow_to_wrist\": ").Append(F(u.right_forearm)).Append(",\n");
        b.Append("    \"wrist_to_wrist_span\": ").Append(F(u.wrist_to_wrist_span)).Append(",\n");
        b.Append("    \"hips_to_head_distance\": ").Append(F(u.hips_to_head)).Append("\n");
        b.Append("  },\n");
        b.Append("  \"official_files_note\": ").Append(JsonEscape(s.official_files_note)).Append("\n");
        b.Append("}\n");
        return b.ToString();
    }

    private static string F(float v) => float.IsNaN(v) ? "null" : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string FloatArr(float[] a)
    {
        var b = new StringBuilder("[");
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0) b.Append(", ");
            b.Append(a[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        b.Append("]");
        return b.ToString();
    }

    private static string JsonEscape(string s)
    {
        if (s == null) return "\"\"";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    }

    [Serializable]
    private class RobotHumanoidSpec
    {
        public string model_id;
        public string export_unity_time_utc;
        public string export_pose_note;
        public float[] smplx_shape_betas_10d;
        public float[] smplx_expression_10d;
        public SegmentLengths unity_humanoid_segment_lengths_m;
        public string official_files_note;
    }

    [Serializable]
    private class SegmentLengths
    {
        public float shoulder_width_upper_arm_roots;
        public float clavicle_span_reference;
        public float left_upper_arm;
        public float right_upper_arm;
        public float left_forearm;
        public float right_forearm;
        public float wrist_to_wrist_span;
        public float hips_to_head;
    }
}
