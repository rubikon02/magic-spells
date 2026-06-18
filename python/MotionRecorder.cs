using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

public class MotionRecorderCsv : MonoBehaviour
{
    public TMP_Text debugText;
    public bool autoStartRecording = true;

    private InputDevice _targetDeviceRight;
    private InputDevice _targetDeviceLeft;
    private StreamWriter _csvWriter;
    private bool _isRecording;
    private bool _previousToggleButtonState;
    private string _outputFilePath;

    void Start()
    {
        _targetDeviceRight = GetDevice(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller);
        _targetDeviceLeft = GetDevice(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller);

        _outputFilePath = BuildOutputPath();

        if (autoStartRecording)
        {
            StartRecording();
        }
        else
        {
            UpdateDebugText("Ready. Press A (right controller primaryButton) to start recording.");
        }
    }

    void Update()
    {
        EnsureDevices();
        HandleToggleInput();

        if (!_isRecording)
        {
            return;
        }

        _targetDeviceLeft.TryGetFeatureValue(CommonUsages.devicePosition, out var leftPos);
        _targetDeviceLeft.TryGetFeatureValue(CommonUsages.deviceRotation, out var leftRot);
        _targetDeviceRight.TryGetFeatureValue(CommonUsages.devicePosition, out var rightPos);
        _targetDeviceRight.TryGetFeatureValue(CommonUsages.deviceRotation, out var rightRot);

        string row = string.Format(
            CultureInfo.InvariantCulture,
            "{0:F6},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10:F6},{11:F6},{12:F6},{13:F6},{14:F6},{15:F6}",
            Time.time,
            Time.frameCount,
            leftPos.x,
            leftPos.y,
            leftPos.z,
            leftRot.x,
            leftRot.y,
            leftRot.z,
            leftRot.w,
            rightPos.x,
            rightPos.y,
            rightPos.z,
            rightRot.x,
            rightRot.y,
            rightRot.z,
            rightRot.w);

        _csvWriter.WriteLine(row);
        UpdateDebugText("Recording: " + Path.GetFileName(_outputFilePath) + "\n" + row);
    }

    private void OnDestroy()
    {
        StopRecording();
    }

    public void StartRecording()
    {
        if (_isRecording)
        {
            return;
        }

        _outputFilePath = BuildOutputPath();
        _csvWriter = new StreamWriter(_outputFilePath, false);
        _csvWriter.WriteLine("timestamp,frame,left_pos_x,left_pos_y,left_pos_z,left_rot_x,left_rot_y,left_rot_z,left_rot_w,right_pos_x,right_pos_y,right_pos_z,right_rot_x,right_rot_y,right_rot_z,right_rot_w");
        _isRecording = true;
        UpdateDebugText("Recording started. File: " + _outputFilePath);
    }

    public void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;

        if (_csvWriter != null)
        {
            _csvWriter.Flush();
            _csvWriter.Close();
            _csvWriter = null;
        }

        UpdateDebugText("Recording stopped. File saved: " + _outputFilePath);
    }

    private void HandleToggleInput()
    {
        if (!_targetDeviceRight.isValid)
        {
            return;
        }

        _targetDeviceRight.TryGetFeatureValue(CommonUsages.primaryButton, out var primaryButtonPressed);
        var pressedNow = primaryButtonPressed && !_previousToggleButtonState;
        _previousToggleButtonState = primaryButtonPressed;

        if (!pressedNow)
        {
            return;
        }

        if (_isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void EnsureDevices()
    {
        if (!_targetDeviceRight.isValid)
        {
            _targetDeviceRight = GetDevice(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller);
        }

        if (!_targetDeviceLeft.isValid)
        {
            _targetDeviceLeft = GetDevice(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller);
        }
    }

    private static InputDevice GetDevice(InputDeviceCharacteristics characteristics)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
        return devices.Count > 0 ? devices[0] : default;
    }

    private static string BuildOutputPath()
    {
        return Path.Combine(
            Application.persistentDataPath,
            "motion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
    }

    private void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
    }
}