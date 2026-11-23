# 3D 移动参考

[English](README.md) | 简体中文 | [包总览](../../README.SCH.md)

`MovementComponent` 用显式状态机驱动 3D 角色，依赖 `Rigidbody` 与 `CapsuleCollider`，不使用 `CharacterController`。移动与旋转是两套独立 API，配置来自 `MovementConfig` 资产。

## 组件设置

| 项 | 值 |
| --- | --- |
| 必需组件 | `Rigidbody`、`CapsuleCollider` |
| 物理 | Kinematic `Rigidbody`、手动重力、直接写位置 |
| 着地探测 | `groundedCheckDistance`、`groundLayer`、`slopeLimit` |
| 线程 | Unity 主线程 |

用 `Create > CycloneGames > RPG Foundation > Movement > Movement Config` 创建配置并赋值。Inspector 会列出与推荐物理设置不一致的项。

## 配置

`MovementConfig` 有 40 个序列化字段。常用值如下：

| 字段 | 默认值 | 含义 |
| --- | ---: | --- |
| `gravity` | `-20f` | 沿 Y 轴向下的负加速度 |
| `groundedCheckDistance` | `0.025f` | 着地探测距离 |
| `slopeLimit` | `45f` | 可行走最大坡度，单位度 |
| `stepHeight` | `0.3f` | 可跨越台阶高度 |
| `minAirborneTimeForFall` | `0.1f` | 进入 Fall 前的延迟 |
| `airControlMultiplier` | `0.5f` | 空中控制系数 |
| `rotationSpeed` | `10f` | `SetLookDirection` 的转向速率 |
| `rollDistance` / `rollDuration` | `5f` / `0.5f` | 翻滚参数 |
| `swimSpeed` / `flySpeed` | `3f` / `6f` | 游泳与飞行速度 |
| `groundLayer` | `1` | 地面层掩码 |
| `enableMovingPlatform` | `true` | 移动平台继承 |
| `enableGapBridging` | `true` | 小间隙跨越 |

`MovementConfigRules` 校验两份配置共 54 个数值字段，并把非有限值替换为默认值。内置预设为 `Standard`、`Swift`、`Bulky`；外部项目可添加 `MovementConfigPresetAsset`。

3D 重力为负、2D 为正。在两个配置间直接复制数值会反转下落方向。

## 状态机

`MovementStateType` 包含 `Idle`、`Walk`、`Run`、`Sprint`、`Crouch`、`Jump`、`Fall`、`Roll`、`Climb`、`Swim`、`Fly`。3D 实现提供：

```text
IdleState, WalkState, RunState, SprintState, CrouchState, JumpState,
FallState, RollState, SwimState, FlyState, LadderClimbState, WallClimbState
```

`RequestStateChange(MovementStateType, object)` 请求切换，`OnStateChanged` 报告实际发生的切换；`OnLanded` 与 `OnJumpStart` 报告对应的边沿事件。

## 输入、旋转与查询

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

| 成员 | 用途 |
| --- | --- |
| `SetInputDirection(Vector3)` | 局部空间移动意图 |
| `SetJumpPressed`、`SetSprintHeld`、`SetCrouchHeld`、`SetRollPressed` | 按键意图 |
| `RequestClimb(ClimbingMode, Vector3, object)`、`StopClimb()` | 梯子或墙面攀爬 |
| `SetLookDirection(Vector3)`、`ClearLookDirection()`、`SetRotation(...)` | 旋转，与移动分离 |
| `IsGrounded`、`CurrentSpeed`、`Velocity`、`IsMoving` | 状态查询 |
| `LaunchCharacter`、`AddForce`、`AddExplosionForce` | 外部冲量 |
| `PauseGroundConstraint(float)` | 临时释放地面约束 |
| `IgnoreCollision`、`TryIgnoreCollision` | 忽略碰撞体缓存 |
| `GetMemorySnapshot()` | 缓存计数与拒绝计数 |
| `Config` | 配置资产的只读视图 |

`SetRotation(Quaternion, bool)` 与 `SetRotation(Vector3, bool)` 带 `immediate` 参数。移动本身不会自动旋转角色。

## 快照与校正

```csharp
using CycloneGames.RPGFoundation.Movement.Core;

MovementSnapshot delta = _movement.GetSnapshot();
MovementFullState full = _movement.CaptureFullState();

_movement.BeginReplay();
_movement.ApplyFullState(in full);
_movement.EndReplay();

// 其他入口：
// _movement.ApplySnapshot(in delta);
// _movement.ResetFromFullState(in full);
```

`MovementFullState` 的 4 个 float 与 2 个 int 扩展槽承载确定性模拟标量，不存表现数据。

## 动画与 root motion

```csharp
_movement.BindAnimation(locomotionSink, actionSink);
_movement.BindRootMotion(rootMotionSource);
_movement.SetUseRootMotion(true);
```

`AnimatorRootMotionSource` 在 Animator 所在 GameObject 上采集 `OnAnimatorMove`。组件把累积增量转成速度，root motion 因此无法绕过碰撞或 reconciliation。同一 GameObject 只允许一个采集者，`MovementRootMotionCaptureGuard` 会禁用第二个。

一个 Animator 只由一个后端驱动：`UnityAnimatorMovementPresenter` 在渲染帧写入变化的 Animator 参数，`PlayableMovementPresenter` 求值手动图。两者由 `MovementAnimationBackendGuard` 仲裁。

## 扩展点

```csharp
var arbiter = new MovementLayerArbiter();
arbiter.TryAdd(new AimLayer());
_movement.BindMovementLayers(arbiter);
_movement.BindSemantics(new MovementExtensionRegistry());
_movement.SetSemanticStateId(MovementSemanticId.FromName("Game.Stagger").Value);
_movement.BindPresentationBudget(budget);
_movement.BindDeterministicStep(driver);
```

持有计时器的层实现 `IMovementLayerReplay`，重放窗口即可保存并恢复。`BuildAnimationFrame` 与 `PublishAnimationFrame` 都是 `protected virtual`。

## 故障排查

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 角色向上运动 | 从 2D 复制了正值重力 | 3D 使用负值 |
| 缓坡上打滑 | `slopeLimit` 过低或 `groundLayer` 不对 | 提高坡度上限并检查掩码 |
| 跨不上台阶 | `stepHeight` 小于台阶高度 | 提高 `stepHeight` |
| 角色不转向 | 未调用旋转 API | 调用 `SetLookDirection` 或 `SetRotation` |
| 两个后端争用 Animator | 同时挂了两个 presenter | 保留一个，守卫会禁用另一个 |
| root motion 生效两次 | 两个 `OnAnimatorMove` 处理者 | 只留一个采集者，并实现 `IMovementRootMotionCapture` |
| 忽略碰撞体请求被拒 | 缓存已满 | 使用 `TryIgnoreCollision`，把拒绝交给项目策略 |

## 延伸阅读

- [包总览](../../README.SCH.md)
- [2D 移动参考](../Movement2D/README.SCH.md)
- [Movement.Networking](../../../../CycloneGames.RPGFoundation.Movement.Networking/README.SCH.md)
