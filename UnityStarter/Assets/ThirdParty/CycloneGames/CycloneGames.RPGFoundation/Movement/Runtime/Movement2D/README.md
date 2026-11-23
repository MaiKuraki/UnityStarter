# 2D movement reference

[English | 简体中文](README.SCH.md) | [Package overview](../../README.md)

`MovementComponent2D` drives a 2D character through an explicit state machine.
It uses `Rigidbody2D` and `Unity.Mathematics.float2` for velocity and context
values. Configuration comes from a `MovementConfig2D` asset.

## Component setup

| Item | Value |
| --- | --- |
| Required component | `Rigidbody2D` |
| Interpolation | `RigidbodyInterpolation2D.Interpolate` |
| Collision detection | `CollisionDetectionMode2D.Continuous` |
| Physics query | `Physics2D.OverlapBox` for ground detection |
| Thread | Unity main thread |

Create the config with
`Create > CycloneGames > RPG Foundation > Movement > Movement Config 2D`,
assign it to the component, then set `movementType`.

## Configuration

`MovementConfig2D` has 37 serialized fields. The table lists the values most
often changed. Defaults come from the asset source.

| Field | Default | Meaning |
| --- | ---: | --- |
| `movementType` | `Platformer` | `Platformer`, `BeltScroll` or `TopDown` |
| `gravity` | `25f` | Positive downward magnitude |
| `maxFallSpeed` | `20f` | Terminal fall speed |
| `groundCheckDistance` | `0.1f` | Ground probe distance |
| `groundCheckSize` | `(0.8, 0.1)` | Overlap box size |
| `groundCheckOffset` | `(0, -0.5)` | Overlap box offset |
| `coyoteTime` | `0.1f` | Late jump window |
| `jumpBufferTime` | `0.1f` | Early jump window |
| `airControlMultiplier` | `0.5f` | Air steering scale |
| `slideSpeed` | `7f` | Wall slide speed |
| `rollDistance` / `rollDuration` | `5f` / `0.5f` | Roll profile |
| `lockZAxis` | `true` | Keeps the character on the 2D plane |
| `facingRight` | `true` | Initial facing |
| `groundLayer` | `1` | Ground layer mask |

`MovementConfigRules` checks every numeric field against finite bounds. Open the
Inspector and use `Validate Configuration` for a read-only report. Presets
`Platformer` and `TopDown` are built in; external projects can add
`MovementConfigPresetAsset` instances.

The gravity sign differs from the 3D component. A 3D asset uses a negative
value; copying it into a 2D asset inverts the fall direction.

## State machine

States are selected by `MovementStateType`: `Idle`, `Walk`, `Run`, `Sprint`,
`Crouch`, `Jump`, `Fall`, `Roll`, `Climb`, `Swim`, `Fly`. The 2D implementation
supplies these concrete states:

```text
IdleState2D, WalkState2D, RunState2D, SprintState2D, CrouchState2D,
JumpState2D, FallState2D, RollState2D, LadderClimbState2D, WallClimbState2D
```

`RequestStateChange(MovementStateType, object)` asks the machine for a
transition. `OnStateChanged` reports the transition that actually happened.
Climbing uses `ClimbingMode.None`, `Ladder` or `Wall`.

## Input and queries

```csharp
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D;
using UnityEngine;

public sealed class PlatformerInput : MonoBehaviour
{
    private MovementComponent2D _movement;

    private void Awake() => _movement = GetComponent<MovementComponent2D>();

    private void Update()
    {
        _movement.SetInputDirection(new Vector2(Input.GetAxisRaw("Horizontal"), 0f));
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
        _movement.SetCrouchHeld(Input.GetButton("Crouch"));
        _movement.SetRollPressed(Input.GetButtonDown("Roll"));
    }
}
```

| Member | Purpose |
| --- | --- |
| `SetInputDirection(Vector2)` | Horizontal or planar movement intent |
| `SetJumpPressed`, `SetSprintHeld`, `SetCrouchHeld`, `SetRollPressed` | Button intent |
| `RequestClimb(ClimbingMode, int, object)`, `StopClimb()` | Ladder and wall climbing |
| `SetLookDirection(Vector2)`, `ClearLookDirection()`, `SetRotation(...)` | Facing, separate from movement |
| `IsGrounded`, `CurrentSpeed`, `Velocity`, `LookDirection`, `IsMoving` | State queries |
| `LaunchCharacter`, `AddForce`, `AddExplosionForce` | External impulses |
| `PauseGroundConstraint(float)` | Release the ground constraint briefly |
| `Config` | Read-only view of the assigned asset |

## Snapshot and correction

```csharp
using CycloneGames.RPGFoundation.Movement.Core;

MovementSnapshot delta = _movement.GetSnapshot();
MovementFullState full = _movement.CaptureFullState();

_movement.BeginReplay();
_movement.ApplyFullState(in full);
_movement.EndReplay();

// Other entry points:
// _movement.ApplySnapshot(in delta);
// _movement.ResetFromFullState(in full);
```

`CaptureFullState` writes coyote time, pending depth and wall side into the
four float and two integer extension slots. Those slots are simulation state and
must not be used for presentation data.

## Animation binding

```csharp
_movement.BindAnimation(locomotionSink, actionSink);
_movement.BindRootMotion(rootMotionSource);
_movement.SetUseRootMotion(true);
```

`SetUseRootMotion(false)` clears pending root motion. Each physics step
publishes one `MovementAnimationState` and zero or more actions. During a
replay window, publication and action dispatch are suppressed.

## Common scenarios

| Scenario | Settings |
| --- | --- |
| Platformer | `movementType = Platformer`, `gravity = 25f`, `coyoteTime = 0.1f`, `jumpBufferTime = 0.1f` |
| Top-down | `movementType = TopDown`, disable jump in the asset, use `SetInputDirection` with both axes |
| Belt-scroll | `movementType = BeltScroll`, tune `groundCheckOffset` for the lane depth |
| Wall jump | Enable wall climb states, set `slideSpeed`, call `RequestClimb(ClimbingMode.Wall, side)` |

## Extension points

```csharp
var arbiter = new MovementLayerArbiter();
arbiter.TryAdd(new AimLayer());          // IMovementStateLayer
_movement.BindMovementLayers(arbiter);
_movement.BindSemantics(new MovementExtensionRegistry());
_movement.BindPresentationBudget(budget);
_movement.BindDeterministicStep(driver);
```

A layer that keeps its own timers should also implement `IMovementLayerReplay`
so `BeginReplay` / `EndReplay` can save and restore them.

## Troubleshooting

| Symptom | Cause | Action |
| --- | --- | --- |
| Character rises instead of falling | Negative gravity copied from 3D | Use a positive 2D gravity |
| Ground detection misses | `groundCheckSize` or `groundCheckOffset` does not match the collider | Adjust the overlap box |
| Jump feels unresponsive | Coyote or buffer windows at zero | Set both to about `0.1f` |
| Tunneling through thin colliders | Collision detection changed | Keep `CollisionDetectionMode2D.Continuous` |
| Correction replays a roll | Replay window missing | Bracket `ApplyFullState` with `BeginReplay` / `EndReplay` |

## See also

- [Package overview](../../README.md)
- [3D movement reference](../Movement3D/README.md)
- [Movement.Networking](../../../../CycloneGames.RPGFoundation.Movement.Networking/README.md)
