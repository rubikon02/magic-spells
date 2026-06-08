using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.XR;

public class WandSwitcher : MonoBehaviour {
    [Header("Input")]
    [SerializeField] private XRNode controllerNode = XRNode.RightHand;
    [SerializeField, Range(0.05f, 0.95f)] private float inputThreshold = 0.5f;

    private readonly List<WandEntry> _wands = new();
    private readonly List<int> _distinctNumbers = new();
    private readonly List<string> _distinctColorsForSelectedNumber = new();
    private int _selectedNumberIndex;
    private int _selectedColorIndex;

    private float _lastHorizontal;
    private float _lastVertical;
    private InputDevice _controllerDevice;

    private static readonly string[] ColorOrder = { "blue", "green", "purple", "red", "yellow" };
    private static readonly Regex WandNameRegex = new(@"^wand(?<number>\d+)_(?<color>[a-zA-Z]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void Awake() {
        RebuildWandList();
        CacheControllerDevice();
        InitializeSelection();
    }

    private void Update() {
        if (_wands.Count == 0) {
            return;
        }

        EnsureControllerDevice();
        if (!_controllerDevice.isValid) {
            return;
        }

        var axis = ReadPrimaryAxis(_controllerDevice);
        var horizontal = axis.x;
        var vertical = axis.y;

        if (WasPressedLeft(horizontal)) {
            MoveNumber(-1);
        } else if (WasPressedRight(horizontal)) {
            MoveNumber(1);
        }

        if (WasPressedDown(vertical)) {
            MoveColor(-1);
        } else if (WasPressedUp(vertical)) {
            MoveColor(1);
        }

        _lastHorizontal = horizontal;
        _lastVertical = vertical;
    }

    private void RebuildWandList() {
        _wands.Clear();

        var found = transform.GetComponentsInChildren<Transform>(true)
            .Where(t => t != transform)
            .Select(t => new { Transform = t, Match = WandNameRegex.Match(t.name) })
            .Where(x => x.Match.Success)
            .Select(x => new WandEntry {
                transform = x.Transform,
                number = int.Parse(x.Match.Groups["number"].Value, CultureInfo.InvariantCulture),
                color = NormalizeColor(x.Match.Groups["color"].Value)
            })
            .Where(x => !string.IsNullOrEmpty(x.color))
            .OrderBy(x => x.number)
            .ThenBy(x => GetColorIndex(x.color))
            .ToList();

        _wands.AddRange(found);
    }

    private void InitializeSelection() {
        _distinctNumbers.Clear();
        _distinctNumbers.AddRange(GetDistinctNumbers());

        if (_distinctNumbers.Count == 0) {
            return;
        }

        _selectedNumberIndex = WrapIndex(_selectedNumberIndex, _distinctNumbers.Count);
        RefreshColorsForSelectedNumber();
        _selectedColorIndex = _distinctColorsForSelectedNumber.Count > 0 ? WrapIndex(_selectedColorIndex, _distinctColorsForSelectedNumber.Count) : 0;
        ApplySelection();
    }

    private void MoveNumber(int direction) {
        var numberCount = _distinctNumbers.Count;
        if (numberCount <= 0) {
            return;
        }

        _selectedNumberIndex = WrapIndex(_selectedNumberIndex + direction, numberCount);
        RefreshColorsForSelectedNumber();
        _selectedColorIndex = _distinctColorsForSelectedNumber.Count > 0 ? Mathf.Clamp(_selectedColorIndex, 0, _distinctColorsForSelectedNumber.Count - 1) : 0;
        ApplySelection();
    }

    private void MoveColor(int direction) {
        RefreshColorsForSelectedNumber();
        var colorCount = _distinctColorsForSelectedNumber.Count;
        if (colorCount <= 0) {
            return;
        }

        _selectedColorIndex = WrapIndex(_selectedColorIndex + direction, colorCount);
        ApplySelection();
    }

    private void ApplySelection() {
        if (_distinctNumbers.Count == 0) {
            return;
        }

        RefreshColorsForSelectedNumber();
        if (_distinctColorsForSelectedNumber.Count == 0) {
            return;
        }

        var selectedNumber = _distinctNumbers[_selectedNumberIndex];
        var selectedColor = _distinctColorsForSelectedNumber[_selectedColorIndex];

        foreach (var wand in _wands) {
            var active = wand.number == selectedNumber && string.Equals(wand.color, selectedColor, StringComparison.OrdinalIgnoreCase);
            if (wand.transform && wand.transform.gameObject.activeSelf != active) {
                wand.transform.gameObject.SetActive(active);
            }
        }
    }

    private void RefreshColorsForSelectedNumber() {
        _distinctColorsForSelectedNumber.Clear();

        if (_distinctNumbers.Count == 0) {
            return;
        }

        var selectedNumber = _distinctNumbers[WrapIndex(_selectedNumberIndex, _distinctNumbers.Count)];
        _distinctColorsForSelectedNumber.AddRange(_wands
            .Where(w => w.number == selectedNumber)
            .Select(w => w.color)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetColorIndex));
    }

    private bool WasPressedLeft(float horizontal) {
        return horizontal < -inputThreshold && _lastHorizontal >= -inputThreshold;
    }

    private bool WasPressedRight(float horizontal) {
        return horizontal > inputThreshold && _lastHorizontal <= inputThreshold;
    }

    private bool WasPressedDown(float vertical) {
        return vertical < -inputThreshold && _lastVertical >= -inputThreshold;
    }

    private bool WasPressedUp(float vertical) {
        return vertical > inputThreshold && _lastVertical <= inputThreshold;
    }

    private List<int> GetDistinctNumbers() {
        return _wands.Select(w => w.number).Distinct().OrderBy(n => n).ToList();
    }

    private static string NormalizeColor(string color) {
        if (string.IsNullOrWhiteSpace(color)) {
            return null;
        }

        var normalized = color.Trim().ToLowerInvariant();
        return ColorOrder.Contains(normalized) ? normalized : null;
    }

    private static int GetColorIndex(string color) {
        if (string.IsNullOrEmpty(color)) {
            return int.MaxValue;
        }

        for (var i = 0; i < ColorOrder.Length; i++) {
            if (string.Equals(ColorOrder[i], color, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return int.MaxValue;
    }

    private void CacheControllerDevice() {
        _controllerDevice = GetDevice(controllerNode);
        _lastHorizontal = 0f;
        _lastVertical = 0f;
    }

    private void EnsureControllerDevice() {
        if (_controllerDevice.isValid) {
            return;
        }

        _controllerDevice = GetDevice(controllerNode);
    }

    private static Vector2 ReadPrimaryAxis(InputDevice device) {
        if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)) {
            return axis;
        }

        if (device.TryGetFeatureValue(CommonUsages.secondary2DAxis, out axis)) {
            return axis;
        }

        return Vector2.zero;
    }

    private static InputDevice GetDevice(XRNode node) {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        return devices.Count > 0 ? devices[0] : default;
    }

    private static int WrapIndex(int index, int count) {
        if (count <= 0) {
            return 0;
        }

        index %= count;
        if (index < 0) {
            index += count;
        }

        return index;
    }

    [Serializable]
    private class WandEntry {
        public Transform transform;
        public int number;
        public string color;
    }
}
