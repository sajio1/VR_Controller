using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedurally generates a capsule-based humanoid placeholder matching
/// the SMPL-H joint hierarchy (24 body joints). Each body segment is
/// represented by a capsule or sphere primitive.
///
/// This serves as an immediate placeholder until the real SMPL-H FBX
/// mesh is imported. The joint hierarchy and naming follow SMPL-H conventions,
/// so the SMPLModelDriver can drive it identically.
/// </summary>
public class ProceduralHumanoid : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Color bodyColor = new Color(0.3f, 0.8f, 1f, 0.7f);
    [SerializeField] private int modelLayer = 6;

    private Transform[] _jointTransforms;
    private GameObject _modelRoot;
    private Material _bodyMaterial;

    public Transform[] JointTransforms => _jointTransforms;
    public Transform ModelRoot => _modelRoot != null ? _modelRoot.transform : null;
    public int JointCount => _jointTransforms != null ? _jointTransforms.Length : 0;

    // Standard SMPL-H rest pose joint positions (meters, Y-up, facing +Z)
    // Based on zero-shape SMPL-H model (~1.7m tall person)
    private static readonly Vector3[] REST_POSITIONS = new Vector3[]
    {
        new Vector3( 0.000f, 0.930f, 0.000f),  //  0 Pelvis
        new Vector3( 0.075f, 0.870f, 0.000f),  //  1 L_Hip
        new Vector3(-0.075f, 0.870f, 0.000f),  //  2 R_Hip
        new Vector3( 0.000f, 1.010f, 0.000f),  //  3 Spine1
        new Vector3( 0.075f, 0.480f, 0.000f),  //  4 L_Knee
        new Vector3(-0.075f, 0.480f, 0.000f),  //  5 R_Knee
        new Vector3( 0.000f, 1.120f, 0.000f),  //  6 Spine2
        new Vector3( 0.075f, 0.070f, 0.000f),  //  7 L_Ankle
        new Vector3(-0.075f, 0.070f, 0.000f),  //  8 R_Ankle
        new Vector3( 0.000f, 1.250f, 0.000f),  //  9 Spine3
        new Vector3( 0.075f, 0.015f, 0.060f),  // 10 L_Foot
        new Vector3(-0.075f, 0.015f, 0.060f),  // 11 R_Foot
        new Vector3( 0.000f, 1.430f, 0.000f),  // 12 Neck
        new Vector3( 0.060f, 1.380f, 0.000f),  // 13 L_Collar
        new Vector3(-0.060f, 1.380f, 0.000f),  // 14 R_Collar
        new Vector3( 0.000f, 1.530f, 0.000f),  // 15 Head
        new Vector3( 0.190f, 1.380f, 0.000f),  // 16 L_Shoulder
        new Vector3(-0.190f, 1.380f, 0.000f),  // 17 R_Shoulder
        new Vector3( 0.460f, 1.380f, 0.000f),  // 18 L_Elbow
        new Vector3(-0.460f, 1.380f, 0.000f),  // 19 R_Elbow
        new Vector3( 0.700f, 1.380f, 0.000f),  // 20 L_Wrist
        new Vector3(-0.700f, 1.380f, 0.000f),  // 21 R_Wrist
        new Vector3( 0.780f, 1.380f, 0.000f),  // 22 L_Hand
        new Vector3(-0.780f, 1.380f, 0.000f),  // 23 R_Hand
    };

    // Segment definitions: which two joints form a capsule
    private struct Segment
    {
        public int JointA, JointB;
        public float Radius;
        public Segment(int a, int b, float r) { JointA = a; JointB = b; Radius = r; }
    }

    private static readonly Segment[] SEGMENTS = new Segment[]
    {
        // Torso
        new Segment(0, 3, 0.08f),   // Pelvis → Spine1
        new Segment(3, 6, 0.07f),   // Spine1 → Spine2
        new Segment(6, 9, 0.07f),   // Spine2 → Spine3
        new Segment(9, 12, 0.05f),  // Spine3 → Neck
        new Segment(12, 15, 0.06f), // Neck → Head

        // Left arm
        new Segment(9, 13, 0.04f),  // Spine3 → L_Collar
        new Segment(13, 16, 0.04f), // L_Collar → L_Shoulder
        new Segment(16, 18, 0.035f),// L_Shoulder → L_Elbow
        new Segment(18, 20, 0.03f), // L_Elbow → L_Wrist
        new Segment(20, 22, 0.025f),// L_Wrist → L_Hand

        // Right arm
        new Segment(9, 14, 0.04f),  // Spine3 → R_Collar
        new Segment(14, 17, 0.04f), // R_Collar → R_Shoulder
        new Segment(17, 19, 0.035f),// R_Shoulder → R_Elbow
        new Segment(19, 21, 0.03f), // R_Elbow → R_Wrist
        new Segment(21, 23, 0.025f),// R_Wrist → R_Hand

        // Left leg
        new Segment(0, 1, 0.06f),   // Pelvis → L_Hip
        new Segment(1, 4, 0.055f),  // L_Hip → L_Knee
        new Segment(4, 7, 0.045f),  // L_Knee → L_Ankle
        new Segment(7, 10, 0.035f), // L_Ankle → L_Foot

        // Right leg
        new Segment(0, 2, 0.06f),   // Pelvis → R_Hip
        new Segment(2, 5, 0.055f),  // R_Hip → R_Knee
        new Segment(5, 8, 0.045f),  // R_Knee → R_Ankle
        new Segment(8, 11, 0.035f), // R_Ankle → R_Foot
    };

    // Joint sphere radii for visualization at each joint
    private static readonly float[] JOINT_RADII = new float[]
    {
        0.04f, // Pelvis
        0.03f, 0.03f, // L/R_Hip
        0.03f, // Spine1
        0.03f, 0.03f, // L/R_Knee
        0.03f, // Spine2
        0.025f, 0.025f, // L/R_Ankle
        0.03f, // Spine3
        0.02f, 0.02f, // L/R_Foot
        0.03f, // Neck
        0.025f, 0.025f, // L/R_Collar
        0.07f, // Head (sphere)
        0.03f, 0.03f, // L/R_Shoulder
        0.025f, 0.025f, // L/R_Elbow
        0.02f, 0.02f, // L/R_Wrist
        0.02f, 0.02f, // L/R_Hand
    };

    // ══════════════════════════════════════════════════
    //                  Public API
    // ══════════════════════════════════════════════════

    /// <summary>Build the procedural humanoid and return the root transform.</summary>
    public void Build()
    {
        if (_modelRoot != null)
        {
            Destroy(_modelRoot);
        }

        _bodyMaterial = CreateBodyMaterial();

        _modelRoot = new GameObject("SMPLH_Humanoid");
        _modelRoot.transform.SetParent(transform, false);
        _modelRoot.transform.localPosition = Vector3.zero;
        _modelRoot.transform.localRotation = Quaternion.identity;

        int jointCount = (int)PoseNormalizer.SMPLJoint.Count;
        _jointTransforms = new Transform[jointCount];

        // Build joint hierarchy
        BuildJointHierarchy();

        // Build visual segments (capsules between joints)
        BuildSegmentVisuals();

        // Build joint spheres
        BuildJointVisuals();

        // Set layer recursively
        SetLayerRecursive(_modelRoot, modelLayer);

        Debug.Log($"[ProceduralHumanoid] Built with {jointCount} joints");
    }

    public void SetLayer(int layer)
    {
        modelLayer = layer;
        if (_modelRoot != null)
            SetLayerRecursive(_modelRoot, layer);
    }

    /// <summary>Get the transform for a specific SMPL joint.</summary>
    public Transform GetJointTransform(PoseNormalizer.SMPLJoint joint)
    {
        int idx = (int)joint;
        if (_jointTransforms != null && idx >= 0 && idx < _jointTransforms.Length)
            return _jointTransforms[idx];
        return null;
    }

    /// <summary>Get the rest position for a specific SMPL joint.</summary>
    public static Vector3 GetRestPosition(PoseNormalizer.SMPLJoint joint)
    {
        int idx = (int)joint;
        if (idx >= 0 && idx < REST_POSITIONS.Length)
            return REST_POSITIONS[idx];
        return Vector3.zero;
    }

    // ══════════════════════════════════════════════════
    //              Internal Construction
    // ══════════════════════════════════════════════════

    private void BuildJointHierarchy()
    {
        int jointCount = (int)PoseNormalizer.SMPLJoint.Count;
        int[] parentIndices = PoseNormalizer.SmplParentIndices;

        // Create all joint GameObjects
        for (int i = 0; i < jointCount; i++)
        {
            string name = ((PoseNormalizer.SMPLJoint)i).ToString();
            GameObject jointObj = new GameObject($"Joint_{name}");
            _jointTransforms[i] = jointObj.transform;
        }

        // Set up hierarchy and local positions
        for (int i = 0; i < jointCount; i++)
        {
            int parentIdx = parentIndices[i];
            if (parentIdx >= 0 && parentIdx < jointCount)
            {
                _jointTransforms[i].SetParent(_jointTransforms[parentIdx], false);
                // Local position = world offset from parent
                Vector3 localPos = REST_POSITIONS[i] - REST_POSITIONS[parentIdx];
                _jointTransforms[i].localPosition = localPos;
            }
            else
            {
                _jointTransforms[i].SetParent(_modelRoot.transform, false);
                _jointTransforms[i].localPosition = REST_POSITIONS[i];
            }
            _jointTransforms[i].localRotation = Quaternion.identity;
        }
    }

    private void BuildSegmentVisuals()
    {
        foreach (var seg in SEGMENTS)
        {
            Vector3 posA = REST_POSITIONS[seg.JointA];
            Vector3 posB = REST_POSITIONS[seg.JointB];
            Vector3 midpoint = (posA + posB) * 0.5f;
            float length = Vector3.Distance(posA, posB);

            if (length < 0.001f) continue;

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = $"Seg_{seg.JointA}_{seg.JointB}";

            // Remove collider
            var col = capsule.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Parent to the "from" joint so it moves with the skeleton
            capsule.transform.SetParent(_jointTransforms[seg.JointA], false);

            // Capsule default is Y-aligned, height=2, radius=0.5
            // Scale: height along Y = length, radius along X/Z
            float scaleY = length / 2f;
            float scaleXZ = seg.Radius * 2f;
            capsule.transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);

            // Position at midpoint of the two joints (in parent local space)
            Vector3 localMid = _jointTransforms[seg.JointA].InverseTransformPoint(
                _modelRoot.transform.TransformPoint(midpoint));
            capsule.transform.localPosition = localMid;

            // Orient capsule Y-axis along the bone direction
            Vector3 dir = posB - posA;
            Quaternion worldRot = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            capsule.transform.rotation = worldRot;
            // Convert to local rotation relative to parent
            capsule.transform.localRotation =
                Quaternion.Inverse(_jointTransforms[seg.JointA].rotation) * worldRot;

            // Assign material
            var rend = capsule.GetComponent<Renderer>();
            if (rend != null) rend.material = _bodyMaterial;
        }
    }

    private void BuildJointVisuals()
    {
        int jointCount = (int)PoseNormalizer.SMPLJoint.Count;

        for (int i = 0; i < jointCount && i < JOINT_RADII.Length; i++)
        {
            float radius = JOINT_RADII[i];
            if (radius <= 0) continue;

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"JointViz_{((PoseNormalizer.SMPLJoint)i).ToString()}";

            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);

            sphere.transform.SetParent(_jointTransforms[i], false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * radius * 2f;

            var rend = sphere.GetComponent<Renderer>();
            if (rend != null)
            {
                Material jointMat = new Material(_bodyMaterial);
                Color jc = bodyColor;
                jc.r = Mathf.Min(1f, jc.r + 0.2f);
                jc.g = Mathf.Min(1f, jc.g + 0.1f);
                jointMat.color = jc;
                rend.material = jointMat;
            }
        }
    }

    private Material CreateBodyMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.name = "SMPLH_Body_Material";

        // Set up for transparency
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", bodyColor);
        }
        else if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", bodyColor);
        }

        // Surface type = Transparent for URP
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha
        }

        mat.renderQueue = 3000;

        // Standard shader transparency
        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");

        return mat;
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }

    private void OnDestroy()
    {
        if (_bodyMaterial != null)
            Destroy(_bodyMaterial);
    }
}
