# RPG Movement Component 2D

A high-performance, state machine-based 2D character movement system for Unity platformer and side-scrolling games with zero GC allocation and optional Gameplay Ability System (GAS) integration.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## ✨ Features

- 🎮 **State Machine Architecture** - Clean separation of 2D movement states
- ⚡ **Zero Garbage Collection** - Uses Unity.Mathematics for allocation-free calculations
- 🎯 **Platform Game Ready** - Coyote time, jump buffering, air control
- 🔌 **GAS Integration Ready** - Optional integration via interfaces
- 📝 **ScriptableObject Config** - Designer-friendly parameters
- 🎨 **2D Physics** - Full Rigidbody2D and Physics2D integration
- 🕐 **Slowmotion Support** - Multi-layer time scaling

## 🎯 Perfect For

- **DNF-style Games** - Side-scrolling beat 'em up
- **Platformers** - Metroidvania, Castlevania
- **2D Fighters** - Street Fighter, KOF style
- **2.5D Games** - Trine, LittleBigPlanet

## 📦 Quick Start

### Step 1: Create Configuration

Right-click in Project window → `Create > CycloneGames > RPG Foundation > Movement Config 2D`

### Step 2: Add Component

Add `MovementComponent2D` to your 2D character GameObject.

Assign:
- `MovementConfig2D` asset
- `Rigidbody2D` (auto-added if missing)
- `Animator` (optional)

### Step 3: Basic Input

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement2D;

public class Player2DController : MonoBehaviour
{
    private MovementComponent2D _movement;

    void Awake()
    {
        _movement = GetComponent<MovementComponent2D>();
    }

    void Update()
    {
        // Horizontal input only
        float horizontal = Input.GetAxis("Horizontal");
        _movement.SetInputDirection(new Vector2(horizontal, 0));
        
        // Jump
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        
        // Sprint
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
    }
}
```

## 🎮 2D-Specific Features

### Coyote Time
Players can jump for a short time after leaving a platform:
```csharp
config.coyoteTime = 0.1f; // 100ms grace period
```

### Jump Buffering
Pressing jump before landing will execute on land:
```csharp
config.jumpBufferTime = 0.1f; // 100ms buffer window
```

### Automatic Facing
Character automatically flips to face movement direction:
```csharp
// Controlled by input direction
_movement.SetInputDirection(new Vector2(1, 0)); // Faces right
_movement.SetInputDirection(new Vector2(-1, 0)); // Faces left
```

### Air Control
Adjust horizontal movement while in air:
```csharp
config.airControlMultiplier = 0.5f; // 50% control in air
```

## ⚙️ Configuration

### MovementConfig2D Parameters

| Category    | Parameter      | Description            | Default |
| ----------- | -------------- | ---------------------- | ------- |
| **Ground**  | walkSpeed      | Walking speed          | 3.0     |
| **Ground**  | runSpeed       | Running speed          | 5.0     |
| **Ground**  | sprintSpeed    | Sprinting speed        | 8.0     |
| **Air**     | jumpForce      | Jump velocity          | 12.0    |
| **Air**     | maxJumpCount   | Multi-jump count       | 1       |
| **Air**     | maxFallSpeed   | Terminal velocity      | 20.0    |
| **Physics** | gravity        | Gravity force          | 25.0    |
| **Physics** | groundLayer    | Ground detection layer | Default |
| **Feel**    | coyoteTime     | Late jump window       | 0.1s    |
| **Feel**    | jumpBufferTime | Early jump window      | 0.1s    |

## 🔄 Differences from 3D Version

| Feature          | 3D (MovementComponent)         | 2D (MovementComponent2D) |
| ---------------- | ------------------------------ | ------------------------ |
| **Physics**      | CharacterController            | Rigidbody2D              |
| **Movement**     | float3 (XYZ)                   | float2 (XY)              |
| **Gravity**      | Manual calculation             | Physics2D.gravity        |
| **Ground Check** | CharacterController.isGrounded | Physics2D.OverlapBox     |
| **Rotation**     | Slerp CurrentMovement Dir      | X Flip(Platformer)       |
| **Coyote Time**  | ❌                              | ✅                        |
| **Jump Buffer**  | ❌                              | ✅                        |

## 🎬 Slow Motion Support

Same as 3D version:

```csharp
// Global slow motion
Time.timeScale = 0.2f;

// Character-specific time scale
movementComponent.LocalTimeScale = 1.5f;

// Ignore global time scale
movementComponent.ignoreTimeScale = true;
```

## 🔌 GAS Integration

Identical interface to 3D version:

```csharp
public class GASMovementAuthority2D : MonoBehaviour, IMovementAuthority2D
{
    public bool CanEnterState(MovementStateType stateType, object context)
    {
        if (stateType == MovementStateType.Sprint)
        {
            return HasStamina();
        }
        return true;
    }
}

// Inject
movement.MovementAuthority = GetComponent<GASMovementAuthority2D>();
```

## 📊 API Reference

### MovementComponent2D

```csharp
// Properties
MovementStateType CurrentState { get; }
bool IsGrounded { get; }
float CurrentSpeed { get; }
Vector2 Velocity { get; }
bool IsMoving { get; }

// Methods
void SetInputDirection(Vector2 direction);
void SetJumpPressed(bool pressed);
void SetSprintHeld(bool held);
void SetCrouchHeld(bool held);
bool RequestStateChange(MovementStateType type);

// Events
event Action<MovementStateType, MovementStateType> OnStateChanged;
event Action OnJumpStart;
event Action OnLanded;
```

## 🎯 Best Practices

### ✅ Do

- Set up `groundCheck` Transform at character's feet
- Use `coyoteTime` and `jumpBufferTime` for better feel
- Configure `groundLayer` to avoid false ground detection
- Use `maxFallSpeed` to prevent crazy fall speeds

### ❌ Don't

- Mix 2D and 3D physics components
- Forget to set Rigidbody2D to Continuous collision detection
- Use on non-2D games (use MovementComponent instead)