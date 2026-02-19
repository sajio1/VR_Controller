using UnityEngine;

/// <summary>
/// 代理手渲染脚本 (方案 A：直接引用源骨骼)
/// 
/// 功能：
///   - 等待真实手的 OVRSkeleton 初始化完成
///   - 创建一个新的 SkinnedMeshRenderer，直接使用源骨骼的 Transform
///   - 骨骼绑定保证正确，手部 Mesh 正确变形
///   - 通过专用相机跟随手腕来实现 UI 中的"位置相对固定"效果
/// </summary>
public class HandUIProxy : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("数据源引用")]
    [Tooltip("真实手的 OVRSkeleton 组件")]
    [SerializeField] private OVRSkeleton sourceSkeleton;

    [Header("外观设置")]
    [Tooltip("代理手使用的材质")]
    [SerializeField] private Material proxyMaterial;

    [Tooltip("代理手的 Layer（用于渲染隔离）")]
    [SerializeField] private int proxyLayer = 6;

    // ══════════════════════════════════════════════════
    //                  内部状态
    // ══════════════════════════════════════════════════

    private bool _initialized;
    private GameObject _proxyMeshObj;
    private SkinnedMeshRenderer _proxyMeshRenderer;
    private SkinnedMeshRenderer _sourceSMR;
    private Transform _wristBone;

    // ══════════════════════════════════════════════════
    //                  公共属性
    // ══════════════════════════════════════════════════

    /// <summary>代理手是否已初始化完成</summary>
    public bool IsInitialized => _initialized;

    /// <summary>代理手的 SkinnedMeshRenderer</summary>
    public SkinnedMeshRenderer ProxyMeshRenderer => _proxyMeshRenderer;

    /// <summary>手腕骨骼的 Transform（用于相机跟随）</summary>
    public Transform WristBone => _wristBone;

    // ══════════════════════════════════════════════════
    //                  公共方法
    // ══════════════════════════════════════════════════

    /// <summary>
    /// 运行时设置数据源
    /// </summary>
    public void SetSource(OVRSkeleton skeleton, OVRMesh mesh = null)
    {
        sourceSkeleton = skeleton;
        _initialized = false;
    }

    /// <summary>
    /// 设置代理手的 Layer
    /// </summary>
    public void SetLayer(int layer)
    {
        proxyLayer = layer;
        if (_proxyMeshObj != null)
        {
            _proxyMeshObj.layer = layer;
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

        // 首次初始化
        if (!_initialized)
        {
            InitializeProxy();
        }
    }

    private void OnDestroy()
    {
        if (_proxyMeshObj != null)
        {
            Destroy(_proxyMeshObj);
        }
    }

    // ══════════════════════════════════════════════════
    //                  初始化
    // ══════════════════════════════════════════════════

    private void InitializeProxy()
    {
        // 查找源 SkinnedMeshRenderer
        _sourceSMR = FindSourceSkinnedMeshRenderer();
        if (_sourceSMR == null || _sourceSMR.sharedMesh == null)
        {
            Debug.LogWarning("[HandUIProxy] Source SkinnedMeshRenderer not found, retrying...");
            return;
        }

        Debug.Log($"[HandUIProxy] Found source SMR: {_sourceSMR.name}, mesh: {_sourceSMR.sharedMesh.name}, bones: {_sourceSMR.bones.Length}");

        // 查找手腕骨骼
        FindWristBone();

        // 创建代理 Mesh 对象
        CreateProxyMesh();

        _initialized = true;
        Debug.Log("[HandUIProxy] Proxy initialized successfully!");
    }

    private SkinnedMeshRenderer FindSourceSkinnedMeshRenderer()
    {
        if (sourceSkeleton == null) return null;

        // 在 OVRSkeleton 同级或子级查找
        SkinnedMeshRenderer smr = sourceSkeleton.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh != null) return smr;

        smr = sourceSkeleton.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh != null) return smr;

        // 在父级查找
        smr = sourceSkeleton.GetComponentInParent<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh != null) return smr;

        // 在同一个 GameObject 层级的兄弟节点查找
        if (sourceSkeleton.transform.parent != null)
        {
            foreach (Transform sibling in sourceSkeleton.transform.parent)
            {
                smr = sibling.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) return smr;
                
                smr = sibling.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) return smr;
            }
        }

        return null;
    }

    private void FindWristBone()
    {
        if (sourceSkeleton == null || sourceSkeleton.Bones == null) return;

        foreach (var bone in sourceSkeleton.Bones)
        {
            if (bone.Id == OVRSkeleton.BoneId.Hand_WristRoot)
            {
                _wristBone = bone.Transform;
                Debug.Log($"[HandUIProxy] Found wrist bone: {_wristBone.name}");
                return;
            }
        }

        // Fallback: 用第一个骨骼
        if (sourceSkeleton.Bones.Count > 0)
        {
            _wristBone = sourceSkeleton.Bones[0].Transform;
            Debug.Log($"[HandUIProxy] Using first bone as wrist: {_wristBone.name}");
        }
    }

    private void CreateProxyMesh()
    {
        // 创建代理 Mesh 对象
        _proxyMeshObj = new GameObject("ProxyHandMesh");
        _proxyMeshObj.transform.SetParent(transform, false);
        _proxyMeshObj.layer = proxyLayer;

        // 添加 SkinnedMeshRenderer
        _proxyMeshRenderer = _proxyMeshObj.AddComponent<SkinnedMeshRenderer>();

        // ═══════════════════════════════════════════════════════════
        // 关键：直接复制源 SMR 的所有设置，包括骨骼引用
        // 这样骨骼绑定保证正确，手部会跟随真实手变形
        // ═══════════════════════════════════════════════════════════
        
        _proxyMeshRenderer.sharedMesh = _sourceSMR.sharedMesh;
        _proxyMeshRenderer.bones = _sourceSMR.bones;           // 直接引用源骨骼！
        _proxyMeshRenderer.rootBone = _sourceSMR.rootBone;     // 直接引用源根骨骼！
        
        // 设置材质
        if (proxyMaterial != null)
        {
            _proxyMeshRenderer.material = proxyMaterial;
        }
        else
        {
            // 复制源材质并调整
            Material mat = new Material(_sourceSMR.material);
            mat.name = "ProxyHand_Material";
            
            // 尝试设置为半透明青色
            Color handColor = new Color(0.3f, 0.8f, 1f, 0.7f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", handColor);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", handColor);
            
            _proxyMeshRenderer.material = mat;
        }

        // 确保渲染器启用
        _proxyMeshRenderer.enabled = true;
        _proxyMeshRenderer.updateWhenOffscreen = true;

        Debug.Log($"[HandUIProxy] Proxy mesh created, bones: {_proxyMeshRenderer.bones.Length}, layer: {proxyLayer}");
    }
}
