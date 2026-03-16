using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [DEPRECATED] Proxy hand skeleton mirroring.
/// 
/// This script is superseded by the full-body tracking system:
///   - BodyTrackingManager (reads OVRBody data)
///   - SMPLModelDriver (drives SMPL-H model)
///   - HumanoidModelViewer (renders model viewports)
///
/// Kept for reference only. Not used in the current scene.
/// </summary>
[System.Obsolete("Use BodyTrackingManager + SMPLModelDriver instead")]
public class HandUIProxy : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("数据源引用")]
    [Tooltip("真实手的 OVRSkeleton 组件")]
    [SerializeField] private OVRSkeleton sourceSkeleton;

    [Tooltip("真实手的 OVRMesh 组件（可选，如果不设置会自动查找）")]
    [SerializeField] private OVRMesh sourceMesh;

    [Header("外观设置")]
    [Tooltip("代理手使用的材质（可选，不设置则使用默认半透明材质）")]
    [SerializeField] private Material proxyMaterial;

    [Tooltip("代理手的缩放比例")]
    [SerializeField] private float proxyScale = 1.0f;

    [Tooltip("代理手的 Layer（用于渲染隔离）")]
    [SerializeField] private int proxyLayer = 6;

    // ══════════════════════════════════════════════════
    //                  内部状态
    // ══════════════════════════════════════════════════

    private bool _initialized;
    private Transform _proxyRoot;
    private Transform[] _proxyBones;
    private SkinnedMeshRenderer _proxyMeshRenderer;
    private Dictionary<int, int> _boneIdToIndex;

    // ══════════════════════════════════════════════════
    //                  公共属性
    // ══════════════════════════════════════════════════

    /// <summary>代理手是否已初始化完成</summary>
    public bool IsInitialized => _initialized;

    /// <summary>代理手的根 Transform</summary>
    public Transform ProxyRoot => _proxyRoot;

    /// <summary>代理手的 SkinnedMeshRenderer</summary>
    public SkinnedMeshRenderer ProxyMeshRenderer => _proxyMeshRenderer;

    // ══════════════════════════════════════════════════
    //                  公共方法
    // ══════════════════════════════════════════════════

    /// <summary>
    /// 运行时设置数据源（从代码调用时使用）
    /// </summary>
    public void SetSource(OVRSkeleton skeleton, OVRMesh mesh = null)
    {
        sourceSkeleton = skeleton;
        sourceMesh = mesh;
        _initialized = false; // 重新初始化
    }

    /// <summary>
    /// 设置代理手的 Layer
    /// </summary>
    public void SetLayer(int layer)
    {
        proxyLayer = layer;
        if (_proxyRoot != null)
        {
            SetLayerRecursive(_proxyRoot.gameObject, layer);
        }
    }

    /// <summary>
    /// 设置代理手材质
    /// </summary>
    public void SetMaterial(Material mat)
    {
        proxyMaterial = mat;
        if (_proxyMeshRenderer != null)
        {
            _proxyMeshRenderer.material = mat;
        }
    }

    // ══════════════════════════════════════════════════
    //                Unity 生命周期
    // ══════════════════════════════════════════════════

    private void LateUpdate()
    {
        // 等待源骨骼初始化
        if (sourceSkeleton == null || !sourceSkeleton.IsInitialized)
            return;

        // 首次初始化：克隆骨骼结构
        if (!_initialized)
        {
            InitializeProxy();
        }

        // 同步骨骼旋转
        if (_initialized && _proxyBones != null)
        {
            SyncBoneRotations();
        }
    }

    private void OnDestroy()
    {
        // 清理代理手
        if (_proxyRoot != null)
        {
            Destroy(_proxyRoot.gameObject);
        }
    }

    // ══════════════════════════════════════════════════
    //                  初始化
    // ══════════════════════════════════════════════════

    private void InitializeProxy()
    {
        if (sourceSkeleton == null || !sourceSkeleton.IsInitialized)
        {
            Debug.LogWarning("[HandUIProxy] Source skeleton not ready");
            return;
        }

        var srcBones = sourceSkeleton.Bones;
        if (srcBones == null || srcBones.Count == 0)
        {
            Debug.LogWarning("[HandUIProxy] Source skeleton has no bones");
            return;
        }

        Debug.Log($"[HandUIProxy] Initializing proxy with {srcBones.Count} bones");

        // 1. 创建代理手根节点
        CreateProxyRoot();

        // 2. 克隆骨骼层级
        CloneSkeletonHierarchy();

        // 3. 设置 SkinnedMeshRenderer
        SetupMeshRenderer();

        // 4. 设置 Layer
        SetLayerRecursive(_proxyRoot.gameObject, proxyLayer);

        _initialized = true;
        Debug.Log($"[HandUIProxy] Proxy initialized successfully");
    }

    private void CreateProxyRoot()
    {
        // 创建代理手容器
        GameObject rootObj = new GameObject("ProxyHand");
        rootObj.transform.SetParent(transform, false);
        rootObj.transform.localPosition = Vector3.zero;
        rootObj.transform.localRotation = Quaternion.identity;
        rootObj.transform.localScale = Vector3.one * proxyScale;
        _proxyRoot = rootObj.transform;
    }

    private void CloneSkeletonHierarchy()
    {
        var srcBones = sourceSkeleton.Bones;
        int boneCount = srcBones.Count;

        _proxyBones = new Transform[boneCount];
        _boneIdToIndex = new Dictionary<int, int>();

        // 第一遍：创建所有骨骼 Transform
        for (int i = 0; i < boneCount; i++)
        {
            var srcBone = srcBones[i];
            GameObject boneObj = new GameObject(srcBone.Transform.name);
            _proxyBones[i] = boneObj.transform;
            _boneIdToIndex[(int)srcBone.Id] = i;
        }

        // 第二遍：建立父子关系
        for (int i = 0; i < boneCount; i++)
        {
            var srcBone = srcBones[i];
            int parentIndex = srcBone.ParentBoneIndex;

            if (parentIndex >= 0 && parentIndex < boneCount)
            {
                _proxyBones[i].SetParent(_proxyBones[parentIndex], false);
            }
            else
            {
                // 根骨骼，挂到代理手根节点下
                _proxyBones[i].SetParent(_proxyRoot, false);
            }

            // 复制初始 localPosition 和 localRotation
            _proxyBones[i].localPosition = srcBone.Transform.localPosition;
            _proxyBones[i].localRotation = srcBone.Transform.localRotation;
            _proxyBones[i].localScale = srcBone.Transform.localScale;
        }
    }

    private void SetupMeshRenderer()
    {
        // 查找源 Mesh
        if (sourceMesh == null && sourceSkeleton != null)
        {
            sourceMesh = sourceSkeleton.GetComponent<OVRMesh>();
            if (sourceMesh == null)
            {
                sourceMesh = sourceSkeleton.GetComponentInParent<OVRMesh>();
            }
            if (sourceMesh == null)
            {
                sourceMesh = sourceSkeleton.GetComponentInChildren<OVRMesh>();
            }
        }

        // 查找源 SkinnedMeshRenderer
        SkinnedMeshRenderer srcSMR = null;
        if (sourceSkeleton != null)
        {
            srcSMR = sourceSkeleton.GetComponent<SkinnedMeshRenderer>();
            if (srcSMR == null)
            {
                srcSMR = sourceSkeleton.GetComponentInChildren<SkinnedMeshRenderer>();
            }
            if (srcSMR == null)
            {
                srcSMR = sourceSkeleton.GetComponentInParent<SkinnedMeshRenderer>();
            }
        }

        if (srcSMR == null || srcSMR.sharedMesh == null)
        {
            Debug.LogWarning("[HandUIProxy] Could not find source SkinnedMeshRenderer with mesh");
            return;
        }

        Mesh srcMesh = srcSMR.sharedMesh;
        Debug.Log($"[HandUIProxy] Found source mesh: {srcMesh.name}, vertices: {srcMesh.vertexCount}");

        // 创建 SkinnedMeshRenderer
        GameObject meshObj = new GameObject("ProxyMesh");
        meshObj.transform.SetParent(_proxyRoot, false);
        meshObj.transform.localPosition = Vector3.zero;
        meshObj.transform.localRotation = Quaternion.identity;
        meshObj.transform.localScale = Vector3.one;

        _proxyMeshRenderer = meshObj.AddComponent<SkinnedMeshRenderer>();

        // 复制 Mesh
        _proxyMeshRenderer.sharedMesh = srcMesh;

        // 设置骨骼绑定
        int numSkinnableBones = Mathf.Min(_proxyBones.Length, srcSMR.bones.Length);
        Transform[] bones = new Transform[srcSMR.bones.Length];

        // 匹配骨骼
        for (int i = 0; i < srcSMR.bones.Length; i++)
        {
            if (srcSMR.bones[i] != null)
            {
                string boneName = srcSMR.bones[i].name;
                // 在代理骨骼中查找同名骨骼
                Transform proxyBone = FindProxyBoneByName(boneName);
                bones[i] = proxyBone ?? _proxyBones[0]; // fallback to root
            }
            else
            {
                bones[i] = _proxyBones[0];
            }
        }

        _proxyMeshRenderer.bones = bones;

        // 设置 rootBone
        if (srcSMR.rootBone != null)
        {
            Transform proxyRootBone = FindProxyBoneByName(srcSMR.rootBone.name);
            _proxyMeshRenderer.rootBone = proxyRootBone ?? _proxyBones[0];
        }
        else
        {
            _proxyMeshRenderer.rootBone = _proxyBones[0];
        }

        // 复制 bindposes
        _proxyMeshRenderer.sharedMesh.bindposes = srcMesh.bindposes;

        // 设置材质
        if (proxyMaterial != null)
        {
            _proxyMeshRenderer.material = proxyMaterial;
        }
        else if (srcSMR.material != null)
        {
            // 创建一个半透明版本的材质
            Material mat = new Material(srcSMR.material);
            mat.name = "ProxyHand_Material";
            
            // 尝试设置为半透明
            if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                c.a = 0.6f;
                mat.SetColor("_BaseColor", c);
            }
            else if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                c.a = 0.6f;
                mat.SetColor("_Color", c);
            }
            
            _proxyMeshRenderer.material = mat;
        }

        // 确保渲染器启用
        _proxyMeshRenderer.enabled = true;
        _proxyMeshRenderer.updateWhenOffscreen = true;

        Debug.Log($"[HandUIProxy] Mesh renderer setup complete, bones: {bones.Length}");
    }

    private Transform FindProxyBoneByName(string name)
    {
        if (_proxyBones == null) return null;
        foreach (var bone in _proxyBones)
        {
            if (bone != null && bone.name == name)
                return bone;
        }
        return null;
    }

    // ══════════════════════════════════════════════════
    //                  同步更新
    // ══════════════════════════════════════════════════

    private void SyncBoneRotations()
    {
        var srcBones = sourceSkeleton.Bones;
        if (srcBones == null) return;

        int count = Mathf.Min(_proxyBones.Length, srcBones.Count);

        for (int i = 0; i < count; i++)
        {
            if (_proxyBones[i] != null && srcBones[i].Transform != null)
            {
                // 只同步局部旋转，保持位置不变
                _proxyBones[i].localRotation = srcBones[i].Transform.localRotation;
            }
        }
    }

    // ══════════════════════════════════════════════════
    //                  工具方法
    // ══════════════════════════════════════════════════

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }
}

