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
    [SerializeField] private bool flipInput = true;

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

    private float[] Preprocess(List<float[]> rawFrames)
    {
        var resampled = Resample(rawFrames, NFrames);

        // Reference: right hand position and rotation at frame 0
        var rPos0 = new Vector3(resampled[7], resampled[8], resampled[9]);
        var rRot0 = new Quaternion(resampled[10], resampled[11], resampled[12], resampled[13]);
        if (rRot0.x == 0 && rRot0.y == 0 && rRot0.z == 0 && rRot0.w == 0) rRot0 = Quaternion.identity;
        var invRot0 = Quaternion.Inverse(rRot0);

        // Optional 180° X flip applied uniformly to right-hand rotation (controlled by flipInput flag)
        var flip = flipInput ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;

        var lPos0 = new Vector3(resampled[0], resampled[1], resampled[2]);
        var maxLeftDistSq = 0f;

        for (var f = 0; f < NFrames; f++)
        {
            var idx = f * NChannels;

            // Right hand position — translate to local space
            var rp = new Vector3(resampled[idx + 7], resampled[idx + 8], resampled[idx + 9]);
            var localRP = invRot0 * (rp - rPos0);
            resampled[idx + 7] = localRP.x;
            resampled[idx + 8] = localRP.y;
            resampled[idx + 9] = localRP.z;

            // Right hand rotation — normalize then optionally flip
            var rr = new Quaternion(resampled[idx + 10], resampled[idx + 11], resampled[idx + 12], resampled[idx + 13]);
            var localRR = flip * (invRot0 * rr);
            resampled[idx + 10] = localRR.x;
            resampled[idx + 11] = localRR.y;
            resampled[idx + 12] = localRR.z;
            resampled[idx + 13] = localRR.w;

            // Left hand position — track max movement before zeroing
            var lp = new Vector3(resampled[idx + 0], resampled[idx + 1], resampled[idx + 2]);
            maxLeftDistSq = Mathf.Max(maxLeftDistSq, (lp - lPos0).sqrMagnitude);

            var localLP = invRot0 * (lp - rPos0);
            resampled[idx + 0] = localLP.x;
            resampled[idx + 1] = localLP.y;
            resampled[idx + 2] = localLP.z;

            var lr = new Quaternion(resampled[idx + 3], resampled[idx + 4], resampled[idx + 5], resampled[idx + 6]);
            var localLR = invRot0 * lr;
            resampled[idx + 3] = localLR.x;
            resampled[idx + 4] = localLR.y;
            resampled[idx + 5] = localLR.z;
            resampled[idx + 6] = localLR.w;
        }

        // If left hand barely moved, zero it out (right-hand-only gesture)
        if (maxLeftDistSq < 0.15f * 0.15f)
        {
            for (var f = 0; f < NFrames; f++)
            {
                var idx = f * NChannels;
                resampled[idx + 0] = 0f;
                resampled[idx + 1] = 0f;
                resampled[idx + 2] = 0f;
                resampled[idx + 3] = 0f;
                resampled[idx + 4] = 0f;
                resampled[idx + 5] = 0f;
                resampled[idx + 6] = 1f;
            }
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
        var maxProb = 0f;
        for (var i = 0; i < probabilities.Length; i++)
        {
            if (probabilities[i] > maxProb)
            {
                maxProb = probabilities[i];
                spellIdx = i;
            }
        }

        var confidence = maxProb * 100f;
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