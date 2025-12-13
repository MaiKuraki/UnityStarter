# RPG Movement Component

A high-performance, state machine-based character movement system for Unity RPG games with zero GC allocation and compatible with Gameplay Ability System (GAS).

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## ✨ Features

- 🎮 **State Machine Architecture** - Clean separation of movement states (Idle, Walk, Sprint, Crouch, Jump, Fall)
- ⚡ **Zero Garbage Collection** - Uses Unity.Mathematics for SIMD-accelerated, allocation-free calculations
- 🔌 **GAS Integration Ready** - Optional integration with Gameplay Ability System via interfaces
- 🎯 **Beginner Friendly** - Works standalone without any dependencies
- 📝 **ScriptableObject Config** - Designer-friendly parameter configuration
- 🌍 **Dynamic Gravity Support** - Supports changing gravity direction for planetary movement
- 🎨 **Animation Ready** - Built-in Animator parameter support

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
        // Get input
        Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector3 worldInput = transform.TransformDirection(new Vector3(input.x, 0, input.y));
        
        // Send to movement component
        _movement.SetInputDirection(worldInput);
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

### Zero-GC Design

The system uses `Unity.Mathematics` types (`float3`, `quaternion`) instead of Unity's `Vector3` and `Quaternion` to eliminate garbage collection:

```csharp
// Traditional (allocates per frame)
Quaternion rotation = Quaternion.Slerp(a, b, t);

// Our approach (zero allocation)
quaternion rotation = math.slerp(a, b, t);
```

## 🎮 Standalone Usage (Without GAS)

### Basic Movement Control

```csharp
MovementComponent movement = GetComponent<MovementComponent>();

// Set input direction (normalized world-space vector)
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
                // Check if player has enough stamina
                return _asc.GetAttribute("Stamina")?.CurrentValue > 10f;
            
            case MovementStateType.Jump:
                // Check if jump is not on cooldown
                return !_asc.HasMatchingTag(GameplayTag.FromString("State.Cooldown.Jump"));
            
            default:
                return true;
        }
    }

    public void OnStateEntered(MovementStateType stateType)
    {
        // Apply effects when entering states
        if (stateType == MovementStateType.Sprint)
        {
            // Apply stamina drain effect
        }
    }

    public void OnStateExited(MovementStateType stateType)
    {
        // Cleanup when exiting states
    }
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
        
        // Request state change (will ask authority first)
        if (movement.RequestStateChange(MovementStateType.Roll))
        {
            CommitAbility(); // Apply cost and cooldown
        }
        else
        {
            CancelAbility();
        }
    }
}
```

## ⚙️ Configuration

### MovementConfig Parameters

| Parameter         | Description              | Default |
| ----------------- | ------------------------ | ------- |
| **walkSpeed**     | Walking speed            | 3.0     |
| **runSpeed**      | Running speed            | 5.0     |
| **sprintSpeed**   | Sprinting speed          | 8.0     |
| **crouchSpeed**   | Crouching speed          | 1.5     |
| **jumpForce**     | Upward jump velocity     | 10.0    |
| **maxJumpCount**  | Number of jumps allowed  | 1       |
| **gravity**       | Gravity acceleration     | -25.0   |
| **rotationSpeed** | Character rotation speed | 20.0    |

### Animation Parameters

The component automatically sets these Animator parameters:

- `MovementSpeed` (Float) - Current movement magnitude
- `IsGrounded` (Bool) - Whether character is on ground
- `Jump` (Trigger) - Jump action trigger

## 🎯 Best Practices

### ✅ Do

- Create one `MovementConfig` asset per character type
- Use `IMovementStateQuery` to read movement state
- Subscribe to events for visual feedback (particles, sounds)
- Use `RequestStateChange()` for explicit state transitions

### ❌ Don't

- Directly modify `_currentState` or internal state
- Call `MoveWithVelocity()` when using state-based input
- Mix input methods (use either `SetInput*` methods OR `MoveWithVelocity`)

## 🔍 API Reference

### MovementComponent

#### Properties

```csharp
MovementStateType CurrentState { get; }          // Current movement state
bool IsGrounded { get; }                         // Is character on ground
float CurrentSpeed { get; }                      // Current movement speed
Vector3 Velocity { get; }                        // Current velocity
bool IsMoving { get; }                           // Is character moving
IMovementAuthority MovementAuthority { get; set; } // Optional GAS authority
```

#### Methods

```csharp
void SetInputDirection(Vector3 direction);       // Set movement direction
void SetJumpPressed(bool pressed);               // Jump input
void SetSprintHeld(bool held);                   // Sprint input
void SetCrouchHeld(bool held);                   // Crouch input
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

The `MovementComponent` automatically rotates the character to face the movement direction. To manually control rotation:

```csharp
// Set look direction (smooth rotation)
movement.SetLookDirection(targetDirection);

// Set rotation immediately
movement.SetRotation(targetRotation, immediate: true);

// Set rotation from direction
movement.SetRotation(targetDirection, immediate: true);
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