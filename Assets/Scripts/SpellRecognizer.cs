using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using TMPro;
using Unity.Barracuda;

public class SpellRecognizer : MonoBehaviour
{
    [Header("Model")]
    [SerializeField] private NNModel modelAsset;

    [Header("Input")]
    [SerializeField] private XRNode inputNode = XRNode.RightHand;
    [SerializeField, Range(0.1f, 0.9f)] private float triggerThreshold = 0.5f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI resultLabel;

    [Header("Recording")]
    [SerializeField] private int minFrames = 10;

    private static readonly string[] SpellNames =
    {
        "Lightning", "Fireball", "Wingardium Leviosa",
        "Lumos", "Open Door", "Knock Off"
    };

    private Model _runtimeModel;
    private IWorker _worker;
    private readonly List<float[]> _frames = new();
    private bool _recording;

    private InputDevice _leftDevice;
    private InputDevice _rightDevice;
    private InputDevice _inputDevice;
    private bool _triggerWasHeld;

    private const int NFrames = 64;
    private const int NChannels = 14;

    public event System.Action<string, int, float> OnSpellRecognized;

    private void Awake()
    {
        _runtimeModel = ModelLoader.Load(modelAsset);
        _worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, _runtimeModel);
    }

    private void OnDestroy()
    {
        _worker?.Dispose();
    }

    private void Update()
    {
        EnsureDevices();
        var triggerHeld = IsTriggerHeld();

        if (triggerHeld && !_triggerWasHeld)
        {
            _frames.Clear();
            _recording = true;
            SetLabel("Recording...");
        }

        if (_recording && triggerHeld)
        {
            _frames.Add(CaptureFrame());
        }

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

    private float[] CaptureFrame()
    {
        var lp = GetPosition(_leftDevice);
        var lr = GetRotation(_leftDevice);
        var rp = GetPosition(_rightDevice);
        var rr = GetRotation(_rightDevice);

        return new[]
        {
            lp.x, lp.y, lp.z,
            lr.x, lr.y, lr.z, lr.w,
            rp.x, rp.y, rp.z,
            rr.x, rr.y, rr.z, rr.w
        };
    }

    private static float[] Preprocess(List<float[]> rawFrames)
    {
        var resampled = Resample(rawFrames, NFrames);

        var leftX0  = resampled[0];
        var leftY0  = resampled[1];
        var leftZ0  = resampled[2];
        var rightX0 = resampled[7];
        var rightY0 = resampled[8];
        var rightZ0 = resampled[9];

        for (var f = 0; f < NFrames; f++)
        {
            var idx = f * NChannels;
            resampled[idx + 0] -= leftX0;
            resampled[idx + 1] -= leftY0;
            resampled[idx + 2] -= leftZ0;
            resampled[idx + 7] -= rightX0;
            resampled[idx + 8] -= rightY0;
            resampled[idx + 9] -= rightZ0;
        }

        return resampled;
    }

    private static float[] Resample(List<float[]> frames, int targetLen)
    {
        var n = frames.Count;
        var channels = frames[0].Length;
        var result = new float[targetLen * channels];

        for (var ti = 0; ti < targetLen; ti++)
        {
            var t = (targetLen == 1) ? 0f : (float)ti / (targetLen - 1);
            var fIdx = t * (n - 1);
            var lo = Mathf.FloorToInt(fIdx);
            var hi = Mathf.Min(lo + 1, n - 1);
            var alpha = fIdx - lo;

            for (var c = 0; c < channels; c++)
                result[ti * channels + c] = Mathf.Lerp(frames[lo][c], frames[hi][c], alpha);
        }
        return result;
    }

    private void Classify()
    {
        var features = Preprocess(_frames);

        using var inputTensor = new Tensor(1, 1, NFrames, NChannels, features);
        _worker.Execute(inputTensor);
        
        var outputTensor = _worker.PeekOutput("probabilities");
        if (outputTensor == null) return;

        var probabilities = outputTensor.ToReadOnlyArray();
        var spellIdx = 0;

        for (var i = 0; i < Mathf.Min(probabilities.Length, SpellNames.Length); i++)
        {
            if (probabilities[i] > probabilities[spellIdx]) spellIdx = i;
        }

        var confidence = probabilities[spellIdx] * 100f;
        var spellName = spellIdx < SpellNames.Length ? SpellNames[spellIdx] : $"Spell {spellIdx}";

        Debug.Log($"[SpellRecognizer] {spellName} ({confidence:F1}%)");
        SetLabel($"{spellName}\n{confidence:F0}%");

        OnSpellRecognized?.Invoke(spellName, spellIdx, confidence);
    }

    private void SetLabel(string text)
    {
        if (resultLabel) resultLabel.text = text;
    }

    private bool IsTriggerHeld()
    {
        if (!_inputDevice.isValid) return false;
        
        if (_inputDevice.TryGetFeatureValue(CommonUsages.triggerButton, out var pressed) && pressed) return true;
        if (_inputDevice.TryGetFeatureValue(CommonUsages.trigger, out var axis) && axis > triggerThreshold) return true;
        
        return false;
    }

    private static Vector3 GetPosition(InputDevice device)
    {
        return device.isValid && device.TryGetFeatureValue(CommonUsages.devicePosition, out var pos) ? pos : Vector3.zero;
    }

    private static Quaternion GetRotation(InputDevice device)
    {
        return device.isValid && device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot) ? rot : Quaternion.identity;
    }

    private void EnsureDevices()
    {
        if (!_leftDevice.isValid) _leftDevice = GetDevice(XRNode.LeftHand);
        if (!_rightDevice.isValid) _rightDevice = GetDevice(XRNode.RightHand);
        if (!_inputDevice.isValid) _inputDevice = GetDevice(inputNode);
    }

    private static InputDevice GetDevice(XRNode node)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        return devices.Count > 0 ? devices[0] : default;
    }
}