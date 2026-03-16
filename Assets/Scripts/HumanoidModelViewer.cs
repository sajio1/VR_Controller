using UnityEngine;

/// <summary>
/// Manages two cameras that render the SMPL-H humanoid model from
/// left-front and right-front perspectives into RenderTextures.
/// These textures are displayed in the HUD panels.
///
/// The cameras are orthographic and auto-frame the full body model.
/// A subtle turntable lighting and grid floor enhance readability.
/// </summary>
public class HumanoidModelViewer : MonoBehaviour
{
    // ══════════════════════════════════════════════════
    //                   Inspector
    // ══════════════════════════════════════════════════

    [Header("Model Driver")]
    [SerializeField] private SMPLModelDriver modelDriver;

    [Header("Camera Settings")]
    [Tooltip("RenderTexture resolution")]
    [SerializeField] private int renderTextureSize = 512;

    [Tooltip("Camera distance from model center")]
    [SerializeField] private float cameraDistance = 2.5f;

    [Tooltip("Camera vertical offset (fraction of model height)")]
    [SerializeField] private float cameraHeightFraction = 0.55f;

    [Tooltip("Viewing angle from front (degrees). 45 = front-quarter view")]
    [SerializeField] private float viewAngle = 45f;

    [Tooltip("Orthographic camera size (covers full body)")]
    [SerializeField] private float orthoSize = 1.1f;

    [Header("Layers")]
    [Tooltip("Layer for the left-front viewer model")]
    [SerializeField] private int leftViewLayer = 8;
    [Tooltip("Layer for the right-front viewer model")]
    [SerializeField] private int rightViewLayer = 9;

    // ══════════════════════════════════════════════════
    //                  Public API
    // ══════════════════════════════════════════════════

    public RenderTexture LeftRenderTexture => _leftRT;
    public RenderTexture RightRenderTexture => _rightRT;
    public Camera LeftCamera => _leftCam;
    public Camera RightCamera => _rightCam;
    public bool IsInitialized => _initialized;

    // ══════════════════════════════════════════════════
    //                Internal State
    // ══════════════════════════════════════════════════

    private RenderTexture _leftRT;
    private RenderTexture _rightRT;
    private Camera _leftCam;
    private Camera _rightCam;
    private GameObject _leftContainer;
    private GameObject _rightContainer;
    private Light _leftLight;
    private Light _rightLight;
    private bool _initialized;

    // Model center approximation (SMPL-H standard model)
    private static readonly Vector3 MODEL_CENTER = new Vector3(0f, 0.85f, 0f);

    // ══════════════════════════════════════════════════
    //              Unity Lifecycle
    // ══════════════════════════════════════════════════

    private void Start()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        CleanUp();
    }

    // ══════════════════════════════════════════════════
    //              Initialization
    // ══════════════════════════════════════════════════

    public void Initialize()
    {
        if (_initialized) return;

        if (modelDriver == null)
        {
            Debug.LogWarning("[HumanoidModelViewer] No SMPLModelDriver assigned");
            return;
        }

        if (!modelDriver.IsInitialized)
        {
            modelDriver.Initialize();
        }

        CreateRenderTextures();
        SetupLeftFrontView();
        SetupRightFrontView();

        // Set model layers
        modelDriver.SetModelLayer(leftViewLayer);

        _initialized = true;
        Debug.Log("[HumanoidModelViewer] Initialized with two viewports");
    }

    // ══════════════════════════════════════════════════
    //              RenderTextures
    // ══════════════════════════════════════════════════

    private void CreateRenderTextures()
    {
        _leftRT = new RenderTexture(renderTextureSize, renderTextureSize, 16, RenderTextureFormat.ARGB32);
        _leftRT.name = "LeftFrontView_RT";
        _leftRT.Create();

        _rightRT = new RenderTexture(renderTextureSize, renderTextureSize, 16, RenderTextureFormat.ARGB32);
        _rightRT.name = "RightFrontView_RT";
        _rightRT.Create();
    }

    // ══════════════════════════════════════════════════
    //              Camera Setup
    // ══════════════════════════════════════════════════

    private void SetupLeftFrontView()
    {
        _leftContainer = new GameObject("LeftFrontViewContainer");
        _leftContainer.transform.SetParent(transform, false);
        _leftContainer.transform.localPosition = new Vector3(-5f, 0f, 5f);

        _leftCam = CreateViewCamera("LeftFrontCamera", _leftContainer.transform,
            _leftRT, leftViewLayer, viewAngle);

        _leftLight = CreateViewLight("LeftFrontLight", _leftContainer.transform, viewAngle);
    }

    private void SetupRightFrontView()
    {
        _rightContainer = new GameObject("RightFrontViewContainer");
        _rightContainer.transform.SetParent(transform, false);
        _rightContainer.transform.localPosition = new Vector3(5f, 0f, 5f);

        _rightCam = CreateViewCamera("RightFrontCamera", _rightContainer.transform,
            _rightRT, rightViewLayer, -viewAngle);

        _rightLight = CreateViewLight("RightFrontLight", _rightContainer.transform, -viewAngle);
    }

    private Camera CreateViewCamera(string name, Transform parent,
        RenderTexture rt, int layer, float angle)
    {
        GameObject camObj = new GameObject(name);
        camObj.transform.SetParent(parent, false);

        // Position camera at the given angle around the model
        float rad = angle * Mathf.Deg2Rad;
        float camX = Mathf.Sin(rad) * cameraDistance;
        float camZ = -Mathf.Cos(rad) * cameraDistance;
        float camY = MODEL_CENTER.y * cameraHeightFraction + 0.3f;

        camObj.transform.localPosition = new Vector3(camX, camY, camZ);
        camObj.transform.LookAt(parent.TransformPoint(MODEL_CENTER));

        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.03f, 0.08f, 1f);
        cam.cullingMask = 1 << layer;
        cam.orthographic = true;
        cam.orthographicSize = orthoSize;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 10f;
        cam.targetTexture = rt;
        cam.depth = -10;

        // Remove audio listener
        var listener = camObj.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);

        return cam;
    }

    private Light CreateViewLight(string name, Transform parent, float angle)
    {
        GameObject lightObj = new GameObject(name);
        lightObj.transform.SetParent(parent, false);

        float rad = angle * Mathf.Deg2Rad;
        lightObj.transform.localPosition = new Vector3(
            Mathf.Sin(rad) * 2f, 2.5f, -Mathf.Cos(rad) * 2f);
        lightObj.transform.LookAt(parent.TransformPoint(MODEL_CENTER));

        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.7f, 0.85f, 1f);
        light.intensity = 1.2f;
        light.cullingMask = 1 << leftViewLayer | 1 << rightViewLayer;

        return light;
    }

    // ══════════════════════════════════════════════════
    //              Cleanup
    // ══════════════════════════════════════════════════

    private void CleanUp()
    {
        if (_leftRT != null) { _leftRT.Release(); Destroy(_leftRT); }
        if (_rightRT != null) { _rightRT.Release(); Destroy(_rightRT); }
        if (_leftContainer != null) Destroy(_leftContainer);
        if (_rightContainer != null) Destroy(_rightContainer);
    }
}
