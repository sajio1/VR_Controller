using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Records SMPL pose data to standard NPZ (NumPy archive) format.
///
/// Output arrays:
///   poses      (N, 165) float32  — SMPL-X full pose (root+body+hands+jaw+eyes)
///   trans      (N, 3)   float32  — root translation per frame
///   betas      (1, 10)  float32  — shape parameters
///   root_orient (N, 3)  float32  — root orientation axis-angle
///   pose_body  (N, 63)  float32  — 21 body joints x 3
///   pose_hand  (N, 90)  float32  — 30 hand joints x 3
///   pose_jaw   (N, 3)   float32  — jaw axis-angle (0 if unavailable)
///   pose_eye   (N, 6)   float32  — eye axis-angle (0 if unavailable)
///   timestamps (N,)     float64  — Unix timestamps per frame
///
/// Start/stop recording via public API or controller buttons.
/// Files are saved to Application.persistentDataPath by default.
/// </summary>
public class NPZRecorder : MonoBehaviour
{
    [Header("Data Source")]
    [SerializeField] private HumanoidSMPLPoseProvider humanoidPoseProvider;

    [Header("Recording Settings")]
    [Tooltip("Target recording FPS (0 = every frame)")]
    [SerializeField] private int targetFPS = 30;

    [Tooltip("Output directory (empty = auto). On Quest, enable below to save where you can see in File Explorer.")]
    [SerializeField] private string outputDirectory = "";

    [Tooltip("On Android/Quest: save to external folder so you can copy NPZ via USB (Quest 3 → Android → data → com.openarm.teleop → files)")]
    [SerializeField] private bool saveToVisibleFolderOnQuest = true;

    private readonly List<float[]> _poseFrames = new List<float[]>();
    private readonly List<float[]> _transFrames = new List<float[]>();
    private readonly List<float[]> _rootOrientFrames = new List<float[]>();
    private readonly List<float[]> _poseBodyFrames = new List<float[]>();
    private readonly List<float[]> _poseHandFrames = new List<float[]>();
    private readonly List<float[]> _poseJawFrames = new List<float[]>();
    private readonly List<float[]> _poseEyeFrames = new List<float[]>();
    private readonly List<double> _timestamps = new List<double>();
    private float[] _betas = new float[10];

    private bool _isRecording;
    private float _lastRecordTime;
    private float _recordInterval;

    public bool IsRecording => _isRecording;
    public int FrameCount => _poseFrames.Count;

    private void Start()
    {
        if (humanoidPoseProvider == null)
            humanoidPoseProvider = FindAnyObjectByType<HumanoidSMPLPoseProvider>();
    }

    /// <summary>Begin a new recording session. Clears any previous data.</summary>
    public void StartRecording()
    {
        _poseFrames.Clear();
        _transFrames.Clear();
        _rootOrientFrames.Clear();
        _poseBodyFrames.Clear();
        _poseHandFrames.Clear();
        _poseJawFrames.Clear();
        _poseEyeFrames.Clear();
        _timestamps.Clear();
        _isRecording = true;
        _recordInterval = targetFPS > 0 ? 1f / targetFPS : 0f;
        _lastRecordTime = Time.time;
        if (humanoidPoseProvider != null)
            Debug.Log("[NPZRecorder] Recording started (source: HumanoidSMPLPoseProvider full SMPL-X)");
        else
            Debug.LogWarning("[NPZRecorder] Recording started but no pose source is assigned.");
    }

    /// <summary>Stop recording and write the NPZ file. Returns the file path.</summary>
    public string StopRecording()
    {
        if (!_isRecording) return null;
        _isRecording = false;

        string path = WriteNPZ();
        Debug.Log($"[NPZRecorder] Stopped. {_poseFrames.Count} frames → {path}");
        return path;
    }

    private void LateUpdate()
    {
        if (!_isRecording) return;

        if (humanoidPoseProvider != null &&
            humanoidPoseProvider.TryGetSMPLXFrame(out HumanoidSMPLPoseProvider.SMPLXFrameData smplx))
        {
            if (!smplx.IsValid) return;
            if (_recordInterval > 0f && Time.time - _lastRecordTime < _recordInterval)
                return;
            _lastRecordTime = Time.time;

            _poseFrames.Add((float[])smplx.FullPose165.Clone());
            _transFrames.Add((float[])smplx.Translation3.Clone());
            _rootOrientFrames.Add((float[])smplx.RootOrient3.Clone());
            _poseBodyFrames.Add((float[])smplx.PoseBody63.Clone());
            _poseHandFrames.Add((float[])smplx.PoseHand90.Clone());
            _poseJawFrames.Add((float[])smplx.PoseJaw3.Clone());
            _poseEyeFrames.Add((float[])smplx.PoseEye6.Clone());
            _timestamps.Add(smplx.Timestamp);
            if (smplx.Betas10 != null && smplx.Betas10.Length >= 10)
                Array.Copy(smplx.Betas10, _betas, 10);
            return;
        }
        // No legacy 72-dim fallback: this recorder now only accepts full SMPL-X frames.
    }

    // ══════════════════════════════════════════════════
    //              NPZ File Writing
    // ══════════════════════════════════════════════════

    private string WriteNPZ()
    {
        string dir;
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            dir = outputDirectory;
        }
        else if (saveToVisibleFolderOnQuest && Application.isMobilePlatform)
        {
            dir = GetAndroidExternalFilesDir();
            if (string.IsNullOrEmpty(dir))
                dir = Application.persistentDataPath;
        }
        else
        {
            dir = Application.persistentDataPath;
        }

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string filename = $"smpl_recording_{DateTime.Now:yyyyMMdd_HHmmss}.npz";
        string fullPath = Path.Combine(dir, filename);

        int N = _poseFrames.Count;
        if (N == 0)
        {
            Debug.LogWarning("[NPZRecorder] No frames recorded");
            return fullPath;
        }

        using (var fs = new FileStream(fullPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteNpyFloat32(zip, "poses", _poseFrames, N, 165);
            WriteNpyFloat32(zip, "trans", _transFrames, N, 3);

            var betasList = new List<float[]> { _betas };
            WriteNpyFloat32(zip, "betas", betasList, 1, 10);

            if (_rootOrientFrames.Count == N) WriteNpyFloat32(zip, "root_orient", _rootOrientFrames, N, 3);
            if (_poseBodyFrames.Count == N) WriteNpyFloat32(zip, "pose_body", _poseBodyFrames, N, 63);
            if (_poseHandFrames.Count == N) WriteNpyFloat32(zip, "pose_hand", _poseHandFrames, N, 90);
            if (_poseJawFrames.Count == N) WriteNpyFloat32(zip, "pose_jaw", _poseJawFrames, N, 3);
            if (_poseEyeFrames.Count == N) WriteNpyFloat32(zip, "pose_eye", _poseEyeFrames, N, 6);

            WriteNpyFloat64(zip, "timestamps", _timestamps);
        }

        return fullPath;
    }

    /// <summary>
    /// On Android/Quest, returns getExternalFilesDir() so NPZ files appear under
    /// Android/data/&lt;package&gt;/files when you connect the device via USB.
    /// Empty on non-Android or if unavailable.
    /// </summary>
    private static string GetAndroidExternalFilesDir()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        return "";
#else
        // Path visible when Quest is connected via USB: Internal shared storage → Android → data → <package> → files
        // Avoids Call<AndroidJavaObject> which can cause CS0815 in some Unity versions.
        string path = "/storage/emulated/0/Android/data/" + Application.identifier + "/files";
        return path;
#endif
    }

    // ══════════════════════════════════════════════════
    //              NPY Format Helpers
    // ══════════════════════════════════════════════════

    private static void WriteNpyFloat32(ZipArchive zip, string name, List<float[]> data, int rows, int cols)
    {
        var entry = zip.CreateEntry(name + ".npy", System.IO.Compression.CompressionLevel.NoCompression);
        using (var stream = entry.Open())
        {
            WriteNpyHeader(stream, "<f4", new int[] { rows, cols });

            byte[] buffer = new byte[cols * 4];
            for (int i = 0; i < rows; i++)
            {
                Buffer.BlockCopy(data[i], 0, buffer, 0, cols * 4);
                stream.Write(buffer, 0, buffer.Length);
            }
        }
    }

    private static void WriteNpyFloat64(ZipArchive zip, string name, List<double> data)
    {
        var entry = zip.CreateEntry(name + ".npy", System.IO.Compression.CompressionLevel.NoCompression);
        using (var stream = entry.Open())
        {
            WriteNpyHeader(stream, "<f8", new int[] { data.Count });

            byte[] buffer = new byte[8];
            for (int i = 0; i < data.Count; i++)
            {
                long bits = BitConverter.DoubleToInt64Bits(data[i]);
                buffer[0] = (byte)(bits);
                buffer[1] = (byte)(bits >> 8);
                buffer[2] = (byte)(bits >> 16);
                buffer[3] = (byte)(bits >> 24);
                buffer[4] = (byte)(bits >> 32);
                buffer[5] = (byte)(bits >> 40);
                buffer[6] = (byte)(bits >> 48);
                buffer[7] = (byte)(bits >> 56);
                stream.Write(buffer, 0, 8);
            }
        }
    }

    /// <summary>
    /// Writes a NumPy .npy v1.0 header.
    /// Format: magic(6) + version(2) + header_len(2) + header_string
    /// Total must be divisible by 64 for alignment.
    /// </summary>
    private static void WriteNpyHeader(Stream stream, string dtype, int[] shape)
    {
        stream.WriteByte(0x93);
        stream.Write(Encoding.ASCII.GetBytes("NUMPY"), 0, 5);

        stream.WriteByte(1); // major version
        stream.WriteByte(0); // minor version

        var sb = new StringBuilder();
        sb.Append("(");
        for (int i = 0; i < shape.Length; i++)
        {
            sb.Append(shape[i]);
            if (i < shape.Length - 1) sb.Append(", ");
            else if (shape.Length == 1) sb.Append(",");
        }
        sb.Append(")");
        string shapeStr = sb.ToString();

        string dictStr = $"{{'descr': '{dtype}', 'fortran_order': False, 'shape': {shapeStr}, }}";

        // Pad so that (10 + header_string_length) is divisible by 64
        int baseLen = dictStr.Length + 1; // +1 for final newline
        int totalLen = 10 + baseLen;
        int remainder = totalLen % 64;
        int padding = remainder == 0 ? 0 : 64 - remainder;

        string header = dictStr + new string(' ', padding) + "\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        ushort hLen = (ushort)headerBytes.Length;
        stream.WriteByte((byte)(hLen & 0xFF));
        stream.WriteByte((byte)((hLen >> 8) & 0xFF));

        stream.Write(headerBytes, 0, headerBytes.Length);
    }
}
