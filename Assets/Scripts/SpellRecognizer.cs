using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using TMPro;
using Unity.Sentis;    // requires com.unity.sentis 2.1.x (Unity 6 / 2022.3 LTS)

/// <summary>
/// Records right-controller motion while the right trigger is held,
/// then classifies the gesture with the ONNX spell model on release.
///
/// Wiring in the scene:
///   • Assign the .onnx file to Model Asset.
///   • Assign a world-space TMP text for Result Label (optional).
///   • The right-hand trigger starts / stops recording.
/// </summary>
public class SpellRecognizer : MonoBehaviour
{
    // ── inspector ──────────────────────────────────────────────────────────
    [Header("Model")]
    [SerializeField] private ModelAsset modelAsset;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI resultLabel;

    [Header("Recording")]
    [Tooltip("Minimum frames before a gesture is accepted.")]
    [SerializeField] private int minFrames = 10;

    // ── spell names must match python/models/labels.json ───────────────────
    private static readonly string[] SpellNames =
    {
        "Lightning", "Fireball", "Wingardium Leviosa",
        "Lumos", "Open Door", "Knock Off"
    };

    // ── Sentis ─────────────────────────────────────────────────────────────
    private Model  _runtimeModel;
    private Worker _worker;   // Sentis 2.x: Worker, not IWorker

    // ── recording state ────────────────────────────────────────────────────
    private readonly List<float[]> _frames = new();
    private bool _recording;

    // ── controller input ───────────────────────────────────────────────────
    private InputDevice _leftDevice;
    private InputDevice _rightDevice;
    private bool _triggerWasHeld;

    // preprocessing constants (must match python/train.py)
    private const int N_FRAMES   = 64;
    private const int N_CHANNELS = 14;
    private const int N_FEATURES = N_FRAMES * N_CHANNELS; // 896

    // ── lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _runtimeModel = ModelLoader.Load(modelAsset);
        _worker = new Worker(_runtimeModel, BackendType.CPU); // Sentis 2.x constructor
    }

    private void OnDestroy()
    {
        _worker?.Dispose();
    }

    private void Update()
    {
        EnsureDevices();

        bool triggerHeld = IsTriggerHeld();

        // ── trigger pressed → start recording ─────────────────────────────
        if (triggerHeld && !_triggerWasHeld)
        {
            _frames.Clear();
            _recording = true;
            SetLabel("Recording…");
        }

        // ── trigger held → collect frame ───────────────────────────────────
        if (_recording && triggerHeld)
        {
            _frames.Add(CaptureFrame());
        }

        // ── trigger released → classify ────────────────────────────────────
        if (!triggerHeld && _triggerWasHeld && _recording)
        {
            _recording = false;
            if (_frames.Count >= minFrames)
                Classify();
            else
                SetLabel("Too short — hold longer");
        }

        _triggerWasHeld = triggerHeld;
    }

    // ── gesture recording ──────────────────────────────────────────────────

    /// Returns a 14-float frame: left pos(3) + left rot(4) + right pos(3) + right rot(4).
    private float[] CaptureFrame()
    {
        Vector3    lp = GetPosition(XRNode.LeftHand);
        Quaternion lr = GetRotation(XRNode.LeftHand);
        Vector3    rp = GetPosition(XRNode.RightHand);
        Quaternion rr = GetRotation(XRNode.RightHand);

        return new float[]
        {
            lp.x, lp.y, lp.z,
            lr.x, lr.y, lr.z, lr.w,
            rp.x, rp.y, rp.z,
            rr.x, rr.y, rr.z, rr.w
        };
    }

    // ── preprocessing ──────────────────────────────────────────────────────

    /// Resamples to N_FRAMES, subtracts first-frame position, flattens to float[896].
    private float[] Preprocess(List<float[]> rawFrames)
    {
        // 1. resample to N_FRAMES via linear interpolation
        float[] resampled = Resample(rawFrames, N_FRAMES);

        // resampled layout: [frame0_ch0, frame0_ch1, … frame0_ch13, frame1_ch0, …]

        // 2. subtract first-frame position for translation invariance
        //    left  pos: channels 0,1,2   → byte offset 0
        //    right pos: channels 7,8,9   → byte offset 7
        float leftX0  = resampled[0 * N_CHANNELS + 0];
        float leftY0  = resampled[0 * N_CHANNELS + 1];
        float leftZ0  = resampled[0 * N_CHANNELS + 2];
        float rightX0 = resampled[0 * N_CHANNELS + 7];
        float rightY0 = resampled[0 * N_CHANNELS + 8];
        float rightZ0 = resampled[0 * N_CHANNELS + 9];

        for (int f = 0; f < N_FRAMES; f++)
        {
            int base_ = f * N_CHANNELS;
            resampled[base_ + 0] -= leftX0;
            resampled[base_ + 1] -= leftY0;
            resampled[base_ + 2] -= leftZ0;
            resampled[base_ + 7] -= rightX0;
            resampled[base_ + 8] -= rightY0;
            resampled[base_ + 9] -= rightZ0;
        }

        return resampled; // already flat, length = N_FEATURES
    }

    /// Linear interpolation of a variable-length sequence to targetLen frames.
    private static float[] Resample(List<float[]> frames, int targetLen)
    {
        int n        = frames.Count;
        int channels = frames[0].Length;
        float[] out_ = new float[targetLen * channels];

        for (int ti = 0; ti < targetLen; ti++)
        {
            float t    = (targetLen == 1) ? 0f : (float)ti / (targetLen - 1);
            float fIdx = t * (n - 1);
            int   lo   = Mathf.FloorToInt(fIdx);
            int   hi   = Mathf.Min(lo + 1, n - 1);
            float alpha = fIdx - lo;

            for (int c = 0; c < channels; c++)
                out_[ti * channels + c] = Mathf.Lerp(frames[lo][c], frames[hi][c], alpha);
        }
        return out_;
    }

    // ── inference ──────────────────────────────────────────────────────────

    private void Classify()
    {
        float[] features = Preprocess(_frames);

        // Sentis 2.x: Tensor<float> instead of TensorFloat, Schedule instead of Execute
        using var inputTensor = new Tensor<float>(new TensorShape(1, N_FEATURES), features);
        _worker.Schedule(inputTensor);

        // PeekOutput borrows the tensor — do NOT dispose it
        var outputTensor = _worker.PeekOutput("probabilities") as Tensor<float>;

        // ReadbackAndClone pulls the data to CPU synchronously (safe for both CPU & GPU backends)
        using Tensor<float> cpuOut = outputTensor.ReadbackAndClone();

        float[] probs    = new float[SpellNames.Length];
        int     spellIdx = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] = cpuOut[i]; // flat index: shape is [1,6], flat 0..5
            if (probs[i] > probs[spellIdx]) spellIdx = i;
        }

        float  confidence = probs[spellIdx] * 100f;
        string spellName  = spellIdx < SpellNames.Length ? SpellNames[spellIdx] : $"Spell {spellIdx}";

        Debug.Log($"[SpellRecognizer] {spellName}  ({confidence:F1}%)  probs: {string.Join(", ", System.Array.ConvertAll(probs, p => p.ToString("F3")))}");
        SetLabel($"{spellName}\n{confidence:F0}%");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SetLabel(string text)
    {
        if (resultLabel != null) resultLabel.text = text;
    }

    private bool IsTriggerHeld()
    {
        if (_rightDevice.isValid &&
            _rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
            return true;

        if (_rightDevice.isValid &&
            _rightDevice.TryGetFeatureValue(CommonUsages.trigger, out float axis) && axis > 0.5f)
            return true;

        return false;
    }

    private Vector3 GetPosition(XRNode node)
    {
        var device = (node == XRNode.LeftHand) ? _leftDevice : _rightDevice;
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
            return pos;
        return Vector3.zero;
    }

    private Quaternion GetRotation(XRNode node)
    {
        var device = (node == XRNode.LeftHand) ? _leftDevice : _rightDevice;
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            return rot;
        return Quaternion.identity;
    }

    private void EnsureDevices()
    {
        if (!_leftDevice.isValid)  _leftDevice  = GetDevice(XRNode.LeftHand);
        if (!_rightDevice.isValid) _rightDevice = GetDevice(XRNode.RightHand);
    }

    private static InputDevice GetDevice(XRNode node)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        return devices.Count > 0 ? devices[0] : default;
    }
}
