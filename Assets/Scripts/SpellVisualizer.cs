using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
using TMPro;

public class SpellVisualizer : MonoBehaviour
{
    [Header("Resources")]
    [SerializeField] private GameObject wandPrefab;
    [SerializeField] private Transform visualisationOriginRotation;
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Input")]
    [SerializeField] private XRNode inputNode = XRNode.LeftHand;

    private static readonly string[] SpellNames =
    {
        "Lightning", "Fireball", "Wingardium Leviosa",
        "Lumos", "Open Door", "Knock Off"
    };

    private static readonly string[] RecordingFiles =
    {
        "1_lightning.csv", "2_fireball.csv", "3_wingardium_leviosa.csv",
        "4_lumos.csv", "5_open_door.csv", "6_knock_off.csv"
    };

    private int _currentSpellIdx = 0;
    private InputDevice _inputDevice;
    private bool _prevCycleButton;
    private bool _prevShowButton;

    private GameObject _visualWandRight;
    private GameObject _visualWandLeft;
    private bool _isVisualizing;
    private float _vizStartTime;
    private List<MotionFrame> _activeRecording;
    private bool _firstShowClickDone;

    private struct MotionFrame
    {
        public Vector3 LeftPos;
        public Quaternion LeftRot;
        public Vector3 RightPos;
        public Quaternion RightRot;
        public float Time;
    }

    private void Start()
    {
        _firstShowClickDone = false;
    }

    private void Update()
    {
        if (!_inputDevice.isValid)
        {
            _inputDevice = InputDevices.GetDeviceAtXRNode(inputNode);
            return;
        }

        HandleInput();

        if (_isVisualizing)
        {
            UpdateVisualization();
        }
    }

    private void HandleInput()
    {
        // Cycle: Primary Button (X/A)
        _inputDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool cyclePressed);
        if (cyclePressed && !_prevCycleButton)
        {
            _currentSpellIdx = (_currentSpellIdx + 1) % SpellNames.Length;
            if (_isVisualizing)
            {
                StartVisualization(); // Restart immediate visualizer for new spell
            }
        }
        _prevCycleButton = cyclePressed;

        // Toggle visibility: Secondary Button (Y/B)
        _inputDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool togglePressed);
        if (togglePressed && !_prevShowButton)
        {
            if (!_firstShowClickDone)
            {
                _firstShowClickDone = true;
                StartVisualization();
            }
            else
            {
                if (_isVisualizing)
                {
                    StopVisualization();
                }
                else
                {
                    StartVisualization();
                }
            }
        }
        _prevShowButton = togglePressed;
    }

    private void StartVisualization()
    {
        StopVisualization();

        string path = Path.Combine(Application.streamingAssetsPath, "Recordings", RecordingFiles[_currentSpellIdx]);
        if (!File.Exists(path))
        {
            Debug.LogError($"[SpellVisualizer] File not found: {path}");
            if (statusLabel)
            {
                statusLabel.gameObject.SetActive(true);
            }
            return;
        }

        _activeRecording = LoadRecording(path);
        if (_activeRecording.Count == 0) return;

        // Create container for Right Wand
        _visualWandRight = new GameObject("WandContainer_R");
        GameObject rWand = Instantiate(wandPrefab);
        rWand.transform.SetParent(_visualWandRight.transform, false);

        // Only 5_open_door (idx 4) uses both hands
        if (_currentSpellIdx == 4)
        {
            _visualWandLeft = new GameObject("WandContainer_L");
            GameObject lWand = Instantiate(wandPrefab);
            lWand.transform.SetParent(_visualWandLeft.transform, false);
        }

        _isVisualizing = true;
        _vizStartTime = Time.time;
        if (statusLabel)
        {
            statusLabel.gameObject.SetActive(true);
            statusLabel.text = $"Visualizing: {SpellNames[_currentSpellIdx]}";
        }
    }

    private void StopVisualization()
    {
        _isVisualizing = false;
        if (_visualWandRight) Destroy(_visualWandRight);
        if (_visualWandLeft) Destroy(_visualWandLeft);
        if (statusLabel)
        {
            statusLabel.text = "";
            statusLabel.gameObject.SetActive(false);
        }
    }

    private void UpdateVisualization()
    {
        float elapsed = Time.time - _vizStartTime;
        float totalDuration = _activeRecording[_activeRecording.Count - 1].Time - _activeRecording[0].Time;

        if (elapsed > totalDuration + 0.5f)
        {
            _vizStartTime = Time.time; // Loop
            elapsed = 0;
        }

        // Simple linear interpolation
        MotionFrame frame = SampleRecording(_activeRecording, elapsed);

        // Position visualization relative to player camera or this object
        Transform refTransform = Camera.main ? Camera.main.transform : transform;
        Vector3 origin = refTransform.position + refTransform.forward * 1.5f + refTransform.up * -0.5f;
        Quaternion orientation = Quaternion.LookRotation(refTransform.forward, Vector3.up);

        // Wand offset relative to hand/controller
        Vector3 offsetPos = Vector3.zero;
        Quaternion offsetRot = Quaternion.identity;
        if (visualisationOriginRotation)
        {
            offsetPos = visualisationOriginRotation.localPosition;
            offsetRot = visualisationOriginRotation.localRotation;
        }

        if (_visualWandRight)
        {
            Vector3 handPos = origin + orientation * frame.RightPos;
            Quaternion handRot = orientation * frame.RightRot;
            _visualWandRight.transform.position = handPos + handRot * offsetPos;
            _visualWandRight.transform.rotation = handRot * offsetRot;
        }

        if (_visualWandLeft)
        {
            Vector3 handPos = origin + orientation * frame.LeftPos;
            Quaternion handRot = orientation * frame.LeftRot;
            _visualWandLeft.transform.position = handPos + handRot * offsetPos;
            _visualWandLeft.transform.rotation = handRot * offsetRot;
        }
    }

    private List<MotionFrame> LoadRecording(string path)
    {
        var frames = new List<MotionFrame>();
        var lines = File.ReadAllLines(path);
        
        // Skip header
        float startTime = -1;

        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 16) continue;

            float t = float.Parse(parts[0], CultureInfo.InvariantCulture);
            if (startTime < 0) startTime = t;

            var frame = new MotionFrame
            {
                Time = t - startTime,
                LeftPos = new Vector3(float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture), float.Parse(parts[4], CultureInfo.InvariantCulture)),
                LeftRot = new Quaternion(float.Parse(parts[5], CultureInfo.InvariantCulture), float.Parse(parts[6], CultureInfo.InvariantCulture), float.Parse(parts[7], CultureInfo.InvariantCulture), float.Parse(parts[8], CultureInfo.InvariantCulture)),
                RightPos = new Vector3(float.Parse(parts[9], CultureInfo.InvariantCulture), float.Parse(parts[10], CultureInfo.InvariantCulture), float.Parse(parts[11], CultureInfo.InvariantCulture)),
                RightRot = new Quaternion(float.Parse(parts[12], CultureInfo.InvariantCulture), float.Parse(parts[13], CultureInfo.InvariantCulture), float.Parse(parts[14], CultureInfo.InvariantCulture), float.Parse(parts[15], CultureInfo.InvariantCulture))
            };
            
            // Normalize relative to first frame right hand to match the "shape" logic
            if (i == 1)
            {
                // We'll store the raw but reshifted to local 0,0,0
            }
            
            frames.Add(frame);
        }

        // Apply normalization relative to first frame right hand
        if (frames.Count > 0)
        {
            Vector3 p0 = frames[0].RightPos;
            Quaternion r0 = frames[0].RightRot;
            if (r0.x == 0 && r0.y == 0 && r0.z == 0 && r0.w == 0) r0 = Quaternion.identity;
            Quaternion invR0 = Quaternion.Inverse(r0);

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                
                // Base normalization
                f.RightPos = invR0 * (f.RightPos - p0);
                f.RightRot = invR0 * f.RightRot;
                f.LeftPos = invR0 * (f.LeftPos - p0);
                f.LeftRot = invR0 * f.LeftRot;

                frames[i] = f;
            }
        }

        return frames;
    }

    private MotionFrame SampleRecording(List<MotionFrame> frames, float time)
    {
        if (frames.Count == 0) return default;
        if (time <= frames[0].Time) return frames[0];
        if (time >= frames[frames.Count - 1].Time) return frames[frames.Count - 1];

        for (int i = 0; i < frames.Count - 1; i++)
        {
            if (time >= frames[i].Time && time <= frames[i + 1].Time)
            {
                float t = (time - frames[i].Time) / (frames[i + 1].Time - frames[i].Time);
                return new MotionFrame
                {
                    RightPos = Vector3.Lerp(frames[i].RightPos, frames[i + 1].RightPos, t),
                    RightRot = Quaternion.Slerp(frames[i].RightRot, frames[i + 1].RightRot, t),
                    LeftPos = Vector3.Lerp(frames[i].LeftPos, frames[i + 1].LeftPos, t),
                    LeftRot = Quaternion.Slerp(frames[i].LeftRot, frames[i + 1].LeftRot, t)
                };
            }
        }
        return frames[frames.Count - 1];
    }
}
