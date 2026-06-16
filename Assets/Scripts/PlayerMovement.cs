using UnityEngine;
using UnityEngine.XR;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private XRNode movementNode = XRNode.LeftHand;
    [SerializeField, Range(0f, 1f)] private float deadZone = 0.2f;

    [Header("Movement")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private float moveSpeed = 1.6f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Snap Turn")]
    [SerializeField] private bool enableSnapTurn = true;
    [SerializeField] private float snapTurnAngle = 45f;
    [SerializeField] private float snapTurnThreshold = 0.7f;
    [SerializeField] private float snapTurnCooldown = 0.3f;

    private CharacterController _controller;
    private InputDevice _movementDevice;
    private Vector3 _horizontalVelocity;
    private float _verticalVelocity;

    private float _lastSnapTurnTime;
    private float _lastTurnInputX;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (!headTransform)
        {
            var cameraComponent = GetComponentInChildren<Camera>();
            if (cameraComponent)
            {
                headTransform = cameraComponent.transform;
            }
        }

        CacheDevice();
    }

    private void Update()
    {
        EnsureDevice();

        var moveInput = ApplyDeadZone(ReadMoveInput());

        UpdateSnapTurn(moveInput);
        Move(moveInput, Time.deltaTime);
    }

    private void UpdateSnapTurn(Vector2 turnInput)
    {
        if (!enableSnapTurn)
        {
            return;
        }

        var timeSinceLastTurn = Time.time - _lastSnapTurnTime;

        if (timeSinceLastTurn < snapTurnCooldown)
        {
            return;
        }

        if (turnInput.x > snapTurnThreshold && _lastTurnInputX <= snapTurnThreshold)
        {
            PerformSnapTurn(snapTurnAngle);
            _lastSnapTurnTime = Time.time;
        }
        else if (turnInput.x < -snapTurnThreshold && _lastTurnInputX >= -snapTurnThreshold)
        {
            PerformSnapTurn(-snapTurnAngle);
            _lastSnapTurnTime = Time.time;
        }

        _lastTurnInputX = turnInput.x;
    }

    private void PerformSnapTurn(float angle)
    {
        var pivot = headTransform ? headTransform.position : transform.position;
        transform.RotateAround(pivot, Vector3.up, angle);
    }

    private void Move(Vector2 moveInput, float deltaTime)
    {
        var moveDirection = GetMoveDirection(moveInput);
        _horizontalVelocity = moveDirection * moveSpeed;

        if (_controller.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * deltaTime;

        var motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
        _controller.Move(motion * deltaTime);
    }

    private Vector2 ReadMoveInput()
    {
        if (_movementDevice.isValid)
        {
            if (_movementDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            {
                return Vector2.ClampMagnitude(axis, 1f);
            }

            if (_movementDevice.TryGetFeatureValue(CommonUsages.secondary2DAxis, out axis))
            {
                return Vector2.ClampMagnitude(axis, 1f);
            }
        }

        var keyboard = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        return Vector2.ClampMagnitude(keyboard, 1f);
    }

    private Vector2 ApplyDeadZone(Vector2 input)
    {
        var magnitude = input.magnitude;
        if (magnitude < deadZone)
        {
            return Vector2.zero;
        }

        var normalizedMagnitude = Mathf.InverseLerp(deadZone, 1f, magnitude);
        return input.normalized * normalizedMagnitude;
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        var basis = headTransform ? headTransform : transform;
        var forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(basis.right, Vector3.up).normalized;

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.right;
        }

        return (right * input.x + forward * input.y).normalized;
    }

    private void CacheDevice()
    {
        _movementDevice = GetDevice(movementNode);
    }

    private void EnsureDevice()
    {
        if (!_movementDevice.isValid)
        {
            _movementDevice = GetDevice(movementNode);
        }
    }

    private static InputDevice GetDevice(XRNode node)
    {
        var devices = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(node, devices);
        return devices.Count > 0 ? devices[0] : default;
    }

    private void OnValidate()
    {
        deadZone = Mathf.Clamp01(deadZone);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        snapTurnAngle = Mathf.Max(1f, snapTurnAngle);
        snapTurnThreshold = Mathf.Max(0.1f, snapTurnThreshold);
        snapTurnCooldown = Mathf.Max(0.1f, snapTurnCooldown);
    }
}