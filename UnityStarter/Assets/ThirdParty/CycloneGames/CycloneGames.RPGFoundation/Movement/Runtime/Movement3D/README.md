# RPG Movement Component

A high-performance, state machine-based character movement system for Unity RPG games and compatible with Gameplay Ability System (GAS).

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## GameplayFramework Integration

`MovementComponent` does not directly depend on `CycloneGames.GameplayFramework`. Movement and spawn ownership stay decoupled so the base Movement runtime can compile in Unity projects, headless tools, and package layouts that do not include GameplayFramework.

Strong-typed GameplayFramework integration should live in a dedicated integration asmdef and be enabled by that asmdef through `versionDefines` and `defineConstraints`. Do not use project-wide scripting define symbols as the normal switch.

If no GameplayFramework integration assembly is active, set the initial rotation from your spawn logic.
### Controlling Rotation

**Movement and rotation are decoupled** - the `MovementComponent` handles only movement, not automatic rotation. You must manually control rotation using one of these methods:

```csharp
// Set look direction (smooth rotation towards target direction)
movement.SetLookDirection(targetDirection);

// Set rotation immediately
movement.SetRotation(targetRotation, immediate: true);

// Set rotation from direction
movement.SetRotation(targetDirection, immediate: true);

// Clear look direction (stop automatic rotation)
movement.ClearLookDirection();
```

**Example: Separate movement and rotation inputs**

Here are several common implementations for `CalculateLookDirection`:

**Option 1: Mouse Look with Euler Angles (First/Third Person)**

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime;

public class PlayerController : MonoBehaviour
{
    private MovementComponent _movement;
    private Camera _camera;

    [Header("Rotation Settings")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minVerticalAngle = -80f;
    [SerializeField] private float maxVerticalAngle = 80f;

    private float _verticalRotation = 0f;
    private float _horizontalRotation = 0f;

    void Awake()
    {
        _movement = GetComponent<MovementComponent>();
        _camera = Camera.main; // Or assign your camera reference
    }

    void Update()
    {
        // Movement input (local space - relative to character's forward/right)
        Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector3 localInput = new Vector3(moveInput.x, 0, moveInput.y);
        _movement.SetInputDirection(localInput);

        // Rotation input (mouse look)
        Vector2 lookInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        Vector3 targetLookDirection = CalculateLookDirection(lookInput);
        _movement.SetLookDirection(targetLookDirection);
    }

    private Vector3 CalculateLookDirection(Vector2 lookInput)
    {
        // Accumulate rotation
        _horizontalRotation += lookInput.x * mouseSensitivity;
        _verticalRotation -= lookInput.y * mouseSensitivity;
        _verticalRotation = Mathf.Clamp(_verticalRotation, minVerticalAngle, maxVerticalAngle);

        // Convert to direction vector
        float horizontalRad = _horizontalRotation * Mathf.Deg2Rad;
        float verticalRad = _verticalRotation * Mathf.Deg2Rad;

        Vector3 direction = new Vector3(
            Mathf.Sin(horizontalRad) * Mathf.Cos(verticalRad),
            Mathf.Sin(verticalRad),
            Mathf.Cos(horizontalRad) * Mathf.Cos(verticalRad)
        );

        return direction.normalized;
    }
}
```

**Option 2: Camera-Based Direction (Third Person with Camera Follow)**

```csharp
private Vector3 CalculateLookDirection(Vector2 lookInput)
{
    if (_camera == null) return transform.forward;

    // Get camera's forward direction (projected onto horizontal plane)
    Vector3 cameraForward = _camera.transform.forward;
    cameraForward.y = 0f; // Remove vertical component
    cameraForward.Normalize();

    // Rotate based on mouse input
    float horizontalRotation = lookInput.x * mouseSensitivity;
    Quaternion rotation = Quaternion.Euler(0, horizontalRotation, 0);

    return rotation * cameraForward;
}
```

**Option 3: Screen-to-World Raycast (Click-to-Look)**

```csharp
private Vector3 CalculateLookDirection(Vector2 lookInput)
{
    // For click-to-look or screen-space input
    if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
    {
        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Vector3 direction = (hit.point - transform.position);
            direction.y = 0f; // Keep horizontal
            return direction.normalized;
        }
    }

    // Fallback: use current forward direction
    return transform.forward;
}
```

**Option 4: Gamepad Right Stick**

```csharp
private Vector3 CalculateLookDirection(Vector2 lookInput)
{
    // For gamepad right stick input
    if (lookInput.magnitude < 0.1f)
        return transform.forward; // No input, maintain current direction

    // Get camera's right and forward vectors (horizontal only)
    Vector3 cameraRight = _camera.transform.right;
    Vector3 cameraForward = _camera.transform.forward;
    cameraRight.y = 0f;
    cameraForward.y = 0f;
    cameraRight.Normalize();
    cameraForward.Normalize();

    // Combine based on stick input
    Vector3 direction = (cameraForward * lookInput.y + cameraRight * lookInput.x).normalized;
    return direction;
}
```

**Option 5: Third-Person Action Game (Camera-Relative Movement)**

For third-person action games where:

- Camera follows the character
- Movement input is relative to camera direction (not character direction)
- Character automatically faces movement direction

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime;

public class ThirdPersonPlayerController : MonoBehaviour
{
    private MovementComponent _movement;
    private Camera _camera;

    [Header("Movement Settings")]
    [SerializeField] private bool autoFaceMovementDirection = true;
    [SerializeField] private float rotationSmoothing = 10f;

    void Awake()
    {
        _movement = GetComponent<MovementComponent>();
        _camera = Camera.main; // Or assign your camera reference
    }

    void Update()
    {
        // Get input in camera space (relative to camera's forward/right)
        Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

        // Convert camera-relative input to world space direction
        Vector3 worldMoveDirection = GetCameraRelativeMovementDirection(moveInput);

        // Convert world direction to local space for MovementComponent
        // MovementComponent expects local space input (relative to character's forward/right)
        Vector3 localInput = transform.InverseTransformDirection(worldMoveDirection);
        _movement.SetInputDirection(localInput);

        // Optionally: Make character face movement direction
        if (autoFaceMovementDirection && moveInput.magnitude > 0.1f)
        {
            Vector3 lookDirection = worldMoveDirection;
            lookDirection.y = 0f; // Keep horizontal only
            if (lookDirection.magnitude > 0.1f)
            {
                _movement.SetLookDirection(lookDirection.normalized);
            }
        }

        // Other inputs
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
        _movement.SetCrouchHeld(Input.GetKey(KeyCode.C));
    }

    /// <summary>
    /// Converts camera-relative input (WASD) to world space movement direction.
    /// This allows movement relative to camera, not character orientation.
    /// </summary>
    private Vector3 GetCameraRelativeMovementDirection(Vector2 input)
    {
        if (_camera == null || input.magnitude < 0.1f)
            return Vector3.zero;

        // Get camera's forward and right vectors (projected onto horizontal plane)
        Vector3 cameraForward = _camera.transform.forward;
        Vector3 cameraRight = _camera.transform.right;

        // Remove vertical component to keep movement on horizontal plane
        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        // Combine camera directions based on input
        // input.y is forward/back (W/S), input.x is left/right (A/D)
        Vector3 direction = (cameraForward * input.y + cameraRight * input.x).normalized;

        return direction;
    }
}
```

**Alternative: Simpler Camera-Relative Movement (Without Auto-Rotation)**

If you want camera-relative movement but don't want automatic rotation:

```csharp
void Update()
{
    // Get input in camera space
    Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

    // Convert to world space direction relative to camera
    Vector3 worldMoveDirection = GetCameraRelativeMovementDirection(moveInput);

    // Convert world direction to character's local space
    Vector3 localInput = transform.InverseTransformDirection(worldMoveDirection);
    _movement.SetInputDirection(localInput);

    // Rotation is controlled separately (e.g., by camera or mouse look)
    // You can use Option 1 or Option 2 for rotation control
}
```

## 🎨 Extending the System

### Adding New States

1. Create a new state class inheriting from `MovementStateBase`
2. Implement required methods (`OnEnter`, `OnUpdate`, `OnExit`, `EvaluateTransition`)
3. Add the state to `MovementStateType` enum
4. Register in `MovementComponent.GetStateByType()`

Example:

```csharp
public class DashState : MovementStateBase
{
    public override MovementStateType StateType => MovementStateType.Dash;

    public override void OnEnter(ref MovementContext context)
    {
        // Initialize dash
    }

    public override void OnUpdate(ref MovementContext context, out float3 displacement)
    {
        // Execute dash movement
        displacement = context.InputDirection * context.Config.dashSpeed * context.DeltaTime;
    }

    public override MovementStateBase EvaluateTransition(ref MovementContext context)
    {
        // Return to walk when dash completes
        return StatePool.GetState<WalkState>();
    }
}
```