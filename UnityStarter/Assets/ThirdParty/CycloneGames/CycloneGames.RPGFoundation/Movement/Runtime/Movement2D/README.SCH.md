# 2D 移动参考

[English](README.md) | 简体中文 | [包总览](../../README.SCH.md)

`MovementComponent2D` 用显式状态机驱动 2D 角色，依赖 `Rigidbody2D`，速度与上下文使用 `Unity.Mathematics.float2`，配置来自 `MovementConfig2D` 资产。

## 组件设置

| 项 | 值 |
| --- | --- |
| 必需组件 | `Rigidbody2D` |
| 插值 | `RigidbodyInterpolation2D.Interpolate` |
| 碰撞检测 | `CollisionDetectionMode2D.Continuous` |
| 着地检测 | `Physics2D.OverlapBox` |
| 线程 | Unity 主线程 |

用 `Create > CycloneGames > RPG Foundation > Movement > Movement Config 2D` 创建配置，赋值给组件后设置 `movementType`。

## 配置

`MovementConfig2D` 有 37 个序列化字段。下表列出常改项，默认值取自资产源码。

| 字段 | 默认值 | 含义 |
| --- | ---: | --- |
| `movementType` | `Platformer` | `Platformer`、`BeltScroll`、`TopDown` |
| `gravity` | `25f` | 正值，表示向下量级 |
| `maxFallSpeed` | `20f` | 下落终速 |
| `groundCheckDistance` | `0.1f` | 着地探测距离 |
| `groundCheckSize` | `(0.8, 0.1)` | 重叠盒尺寸 |
| `groundCheckOffset` | `(0, -0.5)` | 重叠盒偏移 |
| `coyoteTime` | `0.1f` | 延迟跳跃窗口 |
| `jumpBufferTime` | `0.1f` | 提前跳跃窗口 |
| `airControlMultiplier` | `0.5f` | 空中控制系数 |
| `slideSpeed` | `7f` | 贴墙下滑速度 |
| `rollDistance` / `rollDuration` | `5f` / `0.5f` | 翻滚参数 |
| `lockZAxis` | `true` | 保持在 2D 平面 |
| `facingRight` | `true` | 初始朝向 |
| `groundLayer` | `1` | 地面层掩码 |

`MovementConfigRules` 用有限边界校验每个数值字段。Inspector 的 `Validate Configuration` 返回只读问题报告。内置预设为 `Platformer` 与 `TopDown`；外部项目可添加 `MovementConfigPresetAsset`。

重力符号与 3D 组件相反。3D 使用负值，直接填进 2D 资产会让角色反向运动。

## 状态机

状态由 `MovementStateType` 选择：`Idle`、`Walk`、`Run`、`Sprint`、`Crouch`、`Jump`、`Fall`、`Roll`、`Climb`、`Swim`、`Fly`。2D 实现提供以下具体状态：

```text
IdleState2D, WalkState2D, RunState2D, SprintState2D, CrouchState2D,
JumpState2D, FallState2D, RollState2D, LadderClimbState2D, WallClimbState2D
```

`RequestStateChange(MovementStateType, object)` 请求切换，`OnStateChanged` 报告实际发生的切换。攀爬使用 `ClimbingMode.None`、`Ladder` 或 `Wall`。

## 输入与查询

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

| 成员 | 用途 |
| --- | --- |
| `SetInputDirection(Vector2)` | 水平或平面移动意图 |
| `SetJumpPressed`、`SetSprintHeld`、`SetCrouchHeld`、`SetRollPressed` | 按键意图 |
| `RequestClimb(ClimbingMode, int, object)`、`StopClimb()` | 梯子与墙面攀爬 |
| `SetLookDirection(Vector2)`、`ClearLookDirection()`、`SetRotation(...)` | 朝向，与移动分离 |
| `IsGrounded`、`CurrentSpeed`、`Velocity`、`LookDirection`、`IsMoving` | 状态查询 |
| `LaunchCharacter`、`AddForce`、`AddExplosionForce` | 外部冲量 |
| `PauseGroundConstraint(float)` | 临时释放地面约束 |
| `Config` | 配置资产的只读视图 |

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

`CaptureFullState` 把 coyote time、pending depth 与 wall side 写入 4 个 float 和 2 个 int 扩展槽。这些槽属于模拟状态，不存表现数据。

## 动画绑定

```csharp
_movement.BindAnimation(locomotionSink, actionSink);
_movement.BindRootMotion(rootMotionSource);
_movement.SetUseRootMotion(true);
```

`SetUseRootMotion(false)` 会清空待处理的 root motion。每个物理步发布一份 `MovementAnimationState` 与零到多个动作；重放窗口内不发布、不派发。

## 常见场景

| 场景 | 设置 |
| --- | --- |
| 平台跳跃 | `movementType = Platformer`，`gravity = 25f`，两个跳跃窗口约 `0.1f` |
| 俯视角 | `movementType = TopDown`，关闭跳跃，`SetInputDirection` 传双轴 |
| 横版纵深 | `movementType = BeltScroll`，按轨道深度调整 `groundCheckOffset` |
| 蹬墙跳 | 启用墙面攀爬状态，设置 `slideSpeed`，调用 `RequestClimb(ClimbingMode.Wall, side)` |

## 扩展点

```csharp
var arbiter = new MovementLayerArbiter();
arbiter.TryAdd(new AimLayer());          // IMovementStateLayer
_movement.BindMovementLayers(arbiter);
_movement.BindSemantics(new MovementExtensionRegistry());
_movement.BindPresentationBudget(budget);
_movement.BindDeterministicStep(driver);
```

层若持有自己的计时器，应同时实现 `IMovementLayerReplay`，让 `BeginReplay` / `EndReplay` 保存并恢复。

## 故障排查

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 角色向上运动 | 从 3D 复制了负值重力 | 2D 使用正值 |
| 着地检测失效 | `groundCheckSize` 或 `groundCheckOffset` 与碰撞体不匹配 | 调整重叠盒 |
| 跳跃手感迟钝 | coyote 或 buffer 窗口为 0 | 两者设为约 `0.1f` |
| 穿透薄碰撞体 | 碰撞检测模式被改动 | 保持 `CollisionDetectionMode2D.Continuous` |
| 校正时重播翻滚 | 缺少重放窗口 | 用 `BeginReplay` / `EndReplay` 包住 `ApplyFullState` |

## 延伸阅读

- [包总览](../../README.SCH.md)
- [3D 移动参考](../Movement3D/README.SCH.md)
- [Movement.Networking](../../../../CycloneGames.RPGFoundation.Movement.Networking/README.SCH.md)
