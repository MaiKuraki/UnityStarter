# RPG Movement Component

A high-performance, state machine-based character movement system for Unity RPG games and compatible with Gameplay Ability System (GAS).

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## ✨ Features

- 🎮 **State Machine Architecture** - Clean separation of movement states (Idle, Walk, Sprint, Crouch, Jump, Fall)
- 🔌 **GAS Integration Ready** - Optional integration with Gameplay Ability System via interfaces
- 🎯 **Beginner Friendly** - Works standalone without any dependencies
- 📝 **ScriptableObject Config** - Designer-friendly parameter configuration
- 🌍 **Dynamic Gravity Support** - Supports changing gravity direction for planetary movement
- 🎨 **Animation Ready** - Built-in Animator/Animancer parameter support

## 📦 Quick Start

### Step 1: Create Configuration

Right-click in Project window → `Create > CycloneGames > RPG Foundation > Movement Config`

Configure your movement speeds, jump force, and other parameters in the Inspector.

### Step 2: Add Component

Add `MovementComponent` to your character GameObject that has a `CharacterController`.

Assign your created `MovementConfig` to the component.

### Step 3: Basic Input (Without GAS)

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime;

public class PlayerController : MonoBehaviour
{
    private MovementComponent _movement;

    void Awake()
    {
        _movement = GetComponent<MovementComponent>();
    }

    void Update()
    {
        // Get input (in local space - relative to character's forward/right)
        Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector3 localInput = new Vector3(input.x, 0, input.y);

        // Send to movement component (InputDirection is in local space)
        // The movement system will automatically convert it to world space based on character's orientation
        _movement.SetInputDirection(localInput);
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
        _movement.SetCrouchHeld(Input.GetKey(KeyCode.C));
    }
}
```

That's it! Your character now supports walk, sprint, crouch, and jump movement.

## 📚 Core Concepts

### Movement States

The system uses a state machine with the following states:

| State      | Description                                         |
| ---------- | --------------------------------------------------- |
| **Idle**   | Character is stationary on the ground               |
| **Walk**   | Slow walking movement (default when moving)         |
| **Run**    | Normal running movement (faster than walk)          |
| **Sprint** | Fast dash/sprint movement (requires stamina in GAS) |
| **Crouch** | Slower, crouched movement                           |
| **Jump**   | Ascending jump (supports multi-jump)                |
| **Fall**   | Airborne descent with air control                   |

States automatically transition based on input and physics conditions.

## 🎮 Standalone Usage (Without GAS)

### Basic Movement Control

```csharp
MovementComponent movement = GetComponent<MovementComponent>();

// Set input direction (local space - relative to character's forward/right)
// x = right, z = forward, y = up/down (usually 0)
// The system automatically converts to world space based on character orientation
movement.SetInputDirection(direction);

// Control actions
movement.SetJumpPressed(true);
movement.SetSprintHeld(true);
movement.SetCrouchHeld(false);
```

### Query Movement State

```csharp
IMovementStateQuery query = GetComponent<MovementComponent>();

if (query.IsGrounded)
{
    Debug.Log($"Speed: {query.CurrentSpeed}");
    Debug.Log($"State: {query.CurrentState}");
}
```

### Listen to Events

```csharp
void Start()
{
    movement.OnStateChanged += OnMovementStateChanged;
    movement.OnJumpStart += OnJumped;
    movement.OnLanded += OnLanded;
}

void OnMovementStateChanged(MovementStateType from, MovementStateType to)
{
    Debug.Log($"State: {from} → {to}");
}
```

## 🔌 GAS Integration (Advanced)

If you're using the Gameplay Ability System, you can integrate movement control through abilities.

### Step 1: Create Authority

```csharp
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.RPGFoundation.Runtime.Movement;

public class GASMovementAuthority : MonoBehaviour, IMovementAuthority
{
    private AbilitySystemComponent _asc;

    void Awake()
    {
        _asc = GetComponent<AbilitySystemComponent>();
    }

    public bool CanEnterState(MovementStateType stateType, object context)
    {
        switch (stateType)
        {
            case MovementStateType.Sprint:
                return _asc.GetAttribute("Stamina")?.CurrentValue > 10f;

            case MovementStateType.Jump:
                return !_asc.HasMatchingTag(GameplayTag.FromString("State.Cooldown.Jump"));

            default:
                return true;
        }
    }

    public void OnStateEntered(MovementStateType stateType) { }
    public void OnStateExited(MovementStateType stateType) { }

    public MovementAttributeModifier GetAttributeModifier(MovementAttribute attribute)
    {
        return new MovementAttributeModifier(null, 1f);
    }

    public float? GetBaseValue(MovementAttribute attribute) { return null; }
    public float GetMultiplier(MovementAttribute attribute) { return 1f; }
    public float GetFinalValue(MovementAttribute attribute, float configValue) { return configValue; }
}
```

### Step 2: Inject Authority

```csharp
void Start()
{
    var movement = GetComponent<MovementComponent>();
    var authority = GetComponent<GASMovementAuthority>();
    movement.MovementAuthority = authority;
}
```

### Step 3: Control from Abilities

```csharp
public class RollAbility : GameplayAbility
{
    public override void ActivateAbility()
    {
        var movement = GetComponent<MovementComponent>();

        if (movement.RequestStateChange(MovementStateType.Roll))
        {
            CommitAbility();
        }
        else
        {
            CancelAbility();
        }
    }
}
```

## 🎛️ Attribute Modification System

The Movement system supports runtime modification of all movement attributes through the authority system.

### Simple Usage (No GAS)

```csharp
using CycloneGames.RPGFoundation.Runtime.Movement;
using UnityEngine;

public class SimpleAttributeController : MonoBehaviour
{
    void Start()
    {
        var movement = GetComponent<MovementComponent>();
        var authority = GetComponent<MovementAttributeAuthority>();

        if (authority == null)
        {
            authority = gameObject.AddComponent<MovementAttributeAuthority>();
        }

        movement.MovementAuthority = authority;

        // Override base values
        authority.SetBaseValueOverride(MovementAttribute.RunSpeed, 7f);
        authority.SetBaseValueOverride(MovementAttribute.JumpForce, 15f);

        // Apply multipliers
        authority.SetMultiplier(MovementAttribute.RunSpeed, 1.5f);
    }
}
```

### GAS Integration

```csharp
#if GAMEPLAY_ABILITIES_PRESENT
using CycloneGames.RPGFoundation.Runtime.Movement;
using UnityEngine;

public class GASAttributeController : MonoBehaviour
{
    void Start()
    {
        var movement = GetComponent<MovementComponent>();
        var gasAuthority = GetComponent<GASMovementAttributeAuthority>();

        if (gasAuthority == null)
        {
            gasAuthority = gameObject.AddComponent<GASMovementAttributeAuthority>();
        }

        movement.MovementAuthority = gasAuthority;

        // Map GAS attributes
        gasAuthority.AddAttributeMapping(
            MovementAttribute.RunSpeed,
            "Attribute.Secondary.Speed",
            baseValue: 100f
        );
    }
}
#endif
```

**Supported Attributes**: WalkSpeed, RunSpeed, SprintSpeed, CrouchSpeed, JumpForce, Gravity, AirControlMultiplier, RotationSpeed

## ⚙️ Configuration

### MovementConfig Parameters

| Parameter         | Description              | Default |
| ----------------- | ------------------------ | ------- |
| **walkSpeed**     | Walking speed            | 3.0     |
| **runSpeed**      | Running speed            | 5.0     |
| **sprintSpeed**   | Sprinting speed          | 8.0     |
| **crouchSpeed**   | Crouching speed          | 1.5     |
| **jumpForce**     | Upward jump velocity     | 8.0     |
| **maxJumpCount**  | Number of jumps allowed  | 1       |
| **gravity**       | Gravity acceleration     | -25.0   |
| **rotationSpeed** | Character rotation speed | 10.0    |

### Animation Parameters

The component automatically sets these Animator parameters:

- `MovementSpeed` (Float) - Current movement magnitude
- `IsGrounded` (Bool) - Whether character is on ground
- `Jump` (Trigger) - Jump action trigger

**Note**: For BlendTree animations, use `Velocity.magnitude` instead of `CurrentSpeed` for smoother transitions:

```csharp
// Recommended for BlendTree
animator.SetFloat("Speed", movement.Velocity.magnitude);

// Also works (CurrentSpeed resets to 0 in Idle state)
animator.SetFloat("Speed", movement.CurrentSpeed);
```

## 🎯 Best Practices

### ✅ Do

- Create one `MovementConfig` asset per character type
- Use `IMovementStateQuery` to read movement state
- Subscribe to events for visual feedback (particles, sounds)
- Use `RequestStateChange()` for explicit state transitions
- Use `Velocity.magnitude` for BlendTree animations (smoother transitions)
- Use `MovementAttributeAuthority` for runtime attribute modification

### ❌ Don't

- Directly modify `_currentState` or internal state
- Call `MoveWithVelocity()` when using state-based input
- Mix input methods (use either `SetInput*` methods OR `MoveWithVelocity`)
- Use `CurrentSpeed` for BlendTree if you need smooth interpolation (use `Velocity.magnitude` instead)

## 🔍 API Reference

### MovementComponent

#### Properties

```csharp
MovementStateType CurrentState { get; }          // Current movement state
bool IsGrounded { get; }                         // Is character on ground
float CurrentSpeed { get; }                      // Target speed (resets to 0 in Idle)
Vector3 Velocity { get; }                        // Actual velocity vector (recommended for BlendTree)
bool IsMoving { get; }                           // Is character moving
IMovementAuthority MovementAuthority { get; set; } // Attribute modification authority
```

#### Methods

```csharp
void SetInputDirection(Vector3 localDirection);  // Set movement direction in local space (x=right, z=forward)
void SetJumpPressed(bool pressed);               // Jump input
void SetSprintHeld(bool held);                   // Sprint input
void SetCrouchHeld(bool held);                   // Crouch input
void SetLookDirection(Vector3 worldDirection);   // Set rotation target direction (movement and rotation are decoupled)
void ClearLookDirection();                       // Clear rotation target, stop automatic rotation
void SetRotation(Quaternion rotation, bool immediate = false); // Set rotation directly
void SetRotation(Vector3 worldDirection, bool immediate = false); // Set rotation from direction
bool RequestStateChange(MovementStateType type); // Request state transition
```

#### Events

```csharp
event Action<MovementStateType, MovementStateType> OnStateChanged;
event Action OnJumpStart;
event Action OnLanded;
```

## 🚀 Performance

- **Zero GC Allocation** - All core logic uses value types
- **SIMD Acceleration** - Unity.Mathematics leverages CPU vector instructions
- **State Pooling** - State instances are reused via object pool
- **Optimized Rotation** - Uses `math.slerp` instead of `Quaternion.Slerp`
- **Attribute Modification** - Runtime attribute changes without GC allocation

## 🔗 GameplayFramework Integration

### Automatic Rotation Synchronization

When using `MovementComponent` with `CycloneGames.GameplayFramework`, the component automatically synchronizes its rotation when a Pawn is spawned. This is handled via the `IInitialRotationSettable` interface.

#### Package Manager Installation (Recommended)

If both `RPGFoundation` and `GameplayFramework` are installed via Package Manager:

- ✅ **Automatic**: The `GAMEPLAY_FRAMEWORK_PRESENT` define symbol is automatically set via `versionDefines` in asmdef
- ✅ **No configuration needed**: Rotation synchronization works automatically

#### Direct Assets Installation

If `RPGFoundation` is placed directly in the `Assets` folder (not as a Package):

- ⚠️ **Manual setup required**: You must manually set the `GAMEPLAY_FRAMEWORK_PRESENT` define symbol in `PlayerSettings > Scripting Define Symbols`
- ⚠️ **Otherwise**: Automatic rotation synchronization will not work, and you must manually set the Pawn's rotation after spawning

#### Manual Rotation Setup (When Define Symbol is Not Set)

If `GAMEPLAY_FRAMEWORK_PRESENT` is not defined, you need to manually set the rotation after spawning:

```csharp
// In your GameMode or spawn logic
Pawn pawn = SpawnDefaultPawnAtTransform(playerController, spawnTransform);

// Manually set rotation for MovementComponent
var movement = pawn.GetComponent<MovementComponent>();
if (movement != null)
{
    movement.SetRotation(spawnTransform.rotation, immediate: true);
}
```

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
