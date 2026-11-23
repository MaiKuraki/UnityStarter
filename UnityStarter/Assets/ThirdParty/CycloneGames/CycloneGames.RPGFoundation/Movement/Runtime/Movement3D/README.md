# 3D movement reference

[English | 简体中文](README.SCH.md) | [Package overview](../../README.md)

`MovementComponent` drives a 3D character through an explicit state machine.
It uses `Rigidbody` and `CapsuleCollider`; `CharacterController` is not used.
Movement and rotation are separate APIs. Configuration comes from a
`MovementConfig` asset.

## Component setup

| Item | Value |
| --- | --- |
| Required components | `Rigidbody`, `CapsuleCollider` |
| Physics | Kinematic `Rigidbody`, manual gravity, explicit position writes |
| Ground probe | `groundedCheckDistance`, `groundLayer`, `slopeLimit` |
| Thread | Unity main thread |

Create the config with
`Create > CycloneGames > RPG Foundation > Movement > Movement Config` and assign
it to the component. The Inspector reports settings that differ from the
recommended physics setup.

## Configuration

`MovementConfig` has 40 serialized fields. Common values:

| Field | Default | Meaning |
| --- | ---: | --- |
| `gravity` | `-20f` | Negative Y acceleration |
| `groundedCheckDistance` | `0.025f` | Ground probe distance |
| `slopeLimit` | `45f` | Maximum walkable slope, degrees |
| `stepHeight` | `0.3f` | Maximum step climb |
| `minAirborneTimeForFall` | `0.1f` | Delay before entering Fall |
| `airControlMultiplier` | `0.5f` | Air steering scale |
| `rotationSpeed` | `10f` | Turn rate for `SetLookDirection` |
| `rollDistance` / `rollDuration` | `5f` / `0.5f` | Roll profile |
| `swimSpeed` / `flySpeed` | `3f` / `6f` | Swim and fly speeds |
| `groundLayer` | `1` | Ground layer mask |
| `enableMovingPlatform` | `true` | Platform inheritance |
| `enableGapBridging` | `true` | Small-gap bridging |

`MovementConfigRules` validates all 54 numeric fields across both configs and
replaces non-finite values with defaults. Built-in presets are `Standard`,
`Swift` and `Bulky`. External projects can add `MovementConfigPresetAsset`
instances without editing this package.

Gravity is negative in 3D and positive in 2D. A value copied between the two
configs reverses the fall direction.

## State machine

`MovementStateType` contains `Idle`, `Walk`, `Run`, `Sprint`, `Crouch`, `Jump`,
`Fall`, `Roll`, `Climb`, `Swim`, `Fly`. The 3D implementation supplies:

```text
IdleState, WalkState, RunState, SprintState, CrouchState, JumpState,
FallState, RollState, SwimState, FlyState, LadderClimbState, WallClimbState
```

`RequestStateChange(MovementStateType, object)` asks for a transition;
`OnStateChanged` reports the one that happened. `OnLanded` and `OnJumpStart`
report the matching edge events.

## Input, rotation and queries

```csharp
using CycloneGames.RPGFoundation.Movement.Runtime;
using UnityEngine;

public sealed class ThirdPersonInput : MonoBehaviour
{
    [SerializeField] private Transform _camera;
    private MovementComponent _movement;

    private void Awake() => _movement = GetComponent<MovementComponent>();

    private void Update()
    {
        Vector3 forward = _camera.forward;
        forward.y = 0f;
        Vector3 right = _camera.right;
        right.y = 0f;

        Vector3 stick = forward * Input.GetAxisRaw("Vertical")
                      + right * Input.GetAxisRaw("Horizontal");
        _movement.SetInputDirection(stick);
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
        _movement.SetCrouchHeld(Input.GetButton("Crouch"));
        _movement.SetRollPressed(Input.GetButtonDown("Roll"));
    }
}
```

| Member | Purpose |
| --- | --- |
| `SetInputDirection(Vector3)` | Local-space movement intent |
| `SetJumpPressed`, `SetSprintHeld`, `SetCrouchHeld`, `SetRollPressed` | Button intent |
| `RequestClimb(ClimbingMode, Vector3, object)`, `StopClimb()` | Ladder or wall climbing |
| `SetLookDirection(Vector3)`, `ClearLookDirection()`, `SetRotation(...)` | Rotation, separate from movement |
| `IsGrounded`, `CurrentSpeed`, `Velocity`, `IsMoving` | State queries |
| `LaunchCharacter`, `AddForce`, `AddExplosionForce` | External impulses |
| `PauseGroundConstraint(float)` | Release the ground constraint briefly |
| `IgnoreCollision`, `TryIgnoreCollision` | Ignored-collider cache |
| `GetMemorySnapshot()` | Cache counts and rejection counters |
| `Config` | Read-only view of the assigned asset |

`SetRotation(Quaternion, bool)` and `SetRotation(Vector3, bool)` accept an
`immediate` flag. Movement does not rotate the character on its own.

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

The four float and two integer extension slots in `MovementFullState` carry
deterministic simulation scalars. Presentation data does not belong there.

## Animation and root motion

```csharp
_movement.BindAnimation(locomotionSink, actionSink);
_movement.BindRootMotion(rootMotionSource);
_movement.SetUseRootMotion(true);
```

`AnimatorRootMotionSource` captures `OnAnimatorMove` on the Animator's
GameObject. The component converts the accumulated delta into velocity, so root
motion cannot bypass collision or reconciliation. Only one capture is allowed
per GameObject; `MovementRootMotionCaptureGuard` disables the second.

One Animator is driven by one backend. `UnityAnimatorMovementPresenter` writes
changed Animator parameters on the render frame; `PlayableMovementPresenter`
evaluates a manual graph. Both are guarded by
`MovementAnimationBackendGuard`.

## Extension points

```csharp
var arbiter = new MovementLayerArbiter();
arbiter.TryAdd(new AimLayer());
_movement.BindMovementLayers(arbiter);
_movement.BindSemantics(new MovementExtensionRegistry());
_movement.SetSemanticStateId(MovementSemanticId.FromName("Game.Stagger").Value);
_movement.BindPresentationBudget(budget);
_movement.BindDeterministicStep(driver);
```

Layers that keep timers implement `IMovementLayerReplay` so replay windows save
and restore them. `BuildAnimationFrame` and `PublishAnimationFrame` are
`protected virtual`.

## Troubleshooting

| Symptom | Cause | Action |
| --- | --- | --- |
| Character rises instead of falling | Positive gravity copied from 2D | Use a negative 3D gravity |
| Sliding on gentle slopes | `slopeLimit` too low or `groundLayer` wrong | Raise the limit and check the mask |
| Cannot climb a step | `stepHeight` below the step | Raise `stepHeight` |
| Character never rotates | No rotation API called | Call `SetLookDirection` or `SetRotation` |
| Two backends fight over the Animator | Both presenters attached | Keep one; the guard disables the other |
| Root motion applied twice | Two `OnAnimatorMove` handlers | Keep one capture and implement `IMovementRootMotionCapture` |
| Ignored-collider request rejected | Cache at capacity | Use `TryIgnoreCollision` and route the rejection to product policy |

## See also

- [Package overview](../../README.md)
- [2D movement reference](../Movement2D/README.md)
- [Movement.Networking](../../../../CycloneGames.RPGFoundation.Movement.Networking/README.md)
