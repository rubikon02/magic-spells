using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
using TMPro;

public class SpellRecorder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Input Nodes")]
    [SerializeField] private XRNode leftNode = XRNode.LeftHand;
    [SerializeField] private XRNode rightNode = XRNode.RightHand;

    private readonly string[] _spellFolders = {
        "1_lightning", "2_fireball", "3_wingardium_leviosa",
        "4_lumos", "5_open_door", "6_knock_off"
    };
    
    private int _currentSpellIdx = 0;

    private InputDevice _leftDevice;
    private InputDevice _rightDevice;
    private bool _isRecording = false;
    private bool _prevCycleButton = false;
    private bool _prevRecordButton = false;

    private float _startTime;
    private int _startFrame;
    private List<string> _recordedLines = new List<string>();

    private void Start()
    {
        statusText.enabled = true;
        UpdateUI();
    }

    private void Update()
    {
        EnsureDevices();
        HandleInput();

        if (_isRecording)
        {
            RecordFrame();
        }
    }

    private void EnsureDevices()
    {
        if (!_leftDevice.isValid) _leftDevice = GetDevice(leftNode);
        if (!_rightDevice.isValid) _rightDevice = GetDevice(rightNode);
    }

    private InputDevice GetDevice(XRNode node)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        return devices.Count > 0 ? devices[0] : default;
    }

    private void HandleInput()
    {
        if (!_rightDevice.isValid) return;

        // Przycisk A (PrimaryButton) - zmiana czaru
        _rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool cyclePressed);
        if (cyclePressed && !_prevCycleButton && !_isRecording)
        {
            _currentSpellIdx = (_currentSpellIdx + 1) % _spellFolders.Length;
            UpdateUI();
        }
        _prevCycleButton = cyclePressed;

        // Przycisk Grip - nagrywanie od wciśnięcia do puszczenia
        _rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out bool gripPressed);
        if (gripPressed && !_isRecording)
        {
            StartRecording();
        }
        else if (!gripPressed && _isRecording)
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        _startTime = Time.time;
        _startFrame = Time.frameCount;
        _recordedLines.Clear();
        
        // Header zgodny z formatem z python/train.py
        _recordedLines.Add("timestamp,frame,left_pos_x,left_pos_y,left_pos_z,left_rot_x,left_rot_y,left_rot_z,left_rot_w,right_pos_x,right_pos_y,right_pos_z,right_rot_x,right_rot_y,right_rot_z,right_rot_w");
        UpdateUI();
    }

    private void StopRecording()
    {
        _isRecording = false;
        SaveRecording();
        UpdateUI();
    }

    private void RecordFrame()
    {
        var lPos = GetPosition(_leftDevice);
        var lRot = GetRotation(_leftDevice);
        var rPos = GetPosition(_rightDevice);
        var rRot = GetRotation(_rightDevice);

        float t = Time.time - _startTime;
        int f = Time.frameCount - _startFrame;

        // Zapis z precyzją wymaganą przez model, ułożenie dokładnie takie samo jak w SpellRecognizer
        string line = string.Format(CultureInfo.InvariantCulture,
            "{0:F6},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10:F6},{11:F6},{12:F6},{13:F6},{14:F6},{15:F6}",
            t, f,
            lPos.x, lPos.y, lPos.z, lRot.x, lRot.y, lRot.z, lRot.w,
            rPos.x, rPos.y, rPos.z, rRot.x, rRot.y, rRot.z, rRot.w);

        _recordedLines.Add(line);
        if (statusText) statusText.text = $"[NAGRYWANIE]\nCzar: {_spellFolders[_currentSpellIdx]}\nKlatki: {_recordedLines.Count - 1}\n(Puść GRIP aby zatrzymać)";
    }

    private void SaveRecording()
    {
        if (_recordedLines.Count <= 1) return; // Tylko nagłówek, brak klatek

        string baseDir = Path.Combine(Application.persistentDataPath, "NewRecordings", _spellFolders[_currentSpellIdx]);
        Directory.CreateDirectory(baseDir);

        string filename = $"motion_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string fullPath = Path.Combine(baseDir, filename);

        File.WriteAllLines(fullPath, _recordedLines);
        Debug.Log($"[SpellRecorder] Zapisano do {fullPath}");
        
        if (statusText) statusText.text = $"Zapisano:\n{filename}\n\nKliknij A by zmienić czar.\nWciśnij GRIP by nagrać kolejny.";
    }

    private void UpdateUI()
    {
        if (!_isRecording && statusText)
        {
            statusText.text = $"[GOTOWY DO NAGRYWANIA]\nWybrany czar: {_spellFolders[_currentSpellIdx]}\n\nKliknij A by zmienić\nWciśnij i przytrzymaj GRIP by nagrywać";
        }
    }

    private Vector3 GetPosition(InputDevice device) => device.isValid && device.TryGetFeatureValue(CommonUsages.devicePosition, out var p) ? p : Vector3.zero;
    private Quaternion GetRotation(InputDevice device) => device.isValid && device.TryGetFeatureValue(CommonUsages.deviceRotation, out var r) ? r : Quaternion.identity;
}
