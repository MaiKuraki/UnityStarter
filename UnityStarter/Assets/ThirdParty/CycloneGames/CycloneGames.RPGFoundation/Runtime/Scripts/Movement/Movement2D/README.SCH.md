# RPG 2D 移动模块

基于状态机的高性能 2D 角色移动模块，专为 Unity 平台游戏和横版卷轴游戏设计，零 GC 分配，可选的 Gameplay Ability System (GAS) 集成。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## ✨ 特性

- 🎮 **状态机架构** - 清晰的 2D 移动状态分离
- ⚡ **零垃圾回收** - 使用 Unity.Mathematics 实现零分配计算
- 🎯 **平台游戏就绪** - 土狼时间、跳跃缓冲、空中控制
- 🔌 **GAS 集成就绪** - 可选的通过接口集成
- 📝 **ScriptableObject 配置** - 设计师友好的参数
- 🎨 **2D 物理** - 完整的 Rigidbody2D 和 Physics2D 集成
- 🕐 **慢动作支持** - 多层时间缩放

## 🎯 完美适用于

- **DNF 类游戏** - 带纵深的横版格斗
- **平台跳跃游戏** - 恶魔城、银河战士
- **2D 格斗游戏** - 街霸、拳皇风格
- **2.5D 游戏** - Trine、小小大星球
- **俯视角 RPG** - 经典 RPG 风格

## 📦 移动类型

### MovementType2D 枚举

| 类型           | 描述            | 输入               | 物理           |
| -------------- | --------------- | ------------------ | -------------- |
| **Platformer** | 标准横板卷轴    | X=水平移动         | Y=重力/跳跃    |
| **BeltScroll** | DNF 风格带纵深  | X=水平移动, Y=纵深 | 跳跃由物理控制 |
| **TopDown**    | 经典 RPG 俯视角 | X/Y=移动           | 无重力         |

### BeltScroll 模式（DNF 风格）

类似 DNF（地下城与勇士）的横版格斗游戏使用**伪 3D** 方式：

- **X 轴**：水平移动（左/右）
- **Y 轴**：模拟纵深（上=远，下=近）
- **跳跃**：通过 Rigidbody2D 物理临时增加 Y 偏移

```
┌────────────────────────────────────────────┐
  DNF 风格横版卷轴移动
├────────────────────────────────────────────┤
  Input.y ↑ = 向屏幕内移动（远）
  Input.y ↓ = 向屏幕外移动（近）
  跳跃 = 临时 Y 偏移（由物理驱动）
  精灵排序 = 基于 Y 坐标
└────────────────────────────────────────────┘
```

**重要**：使用 SpriteRenderer 的 `Sorting Layer` 或基于 Y 坐标的 `Order in Layer` 实现正确的深度渲染。

## 📦 快速开始

### 步骤 1：创建配置

在 Project 窗口右键 → `Create > CycloneGames > RPG Foundation > Movement Config 2D`

### 步骤 2：添加组件

在 2D 角色 GameObject 上添加 `MovementComponent2D`。

分配：

- `MovementConfig2D` 资产
- `Rigidbody2D`（如果缺失会自动添加）
- `Animator`（可选）

### 步骤 3：基础输入

#### Platformer 模式

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
        // Platformer 模式仅需水平输入
        float horizontal = Input.GetAxis("Horizontal");
        _movement.SetInputDirection(new Vector2(horizontal, 0));

        // 跳跃
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));

        // 冲刺
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
    }
}
```

#### BeltScroll 模式（DNF 风格）

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement2D;

public class DNFStyleController : MonoBehaviour
{
    private MovementComponent2D _movement;

    void Awake()
    {
        _movement = GetComponent<MovementComponent2D>();
    }

    void Update()
    {
        // X = 水平移动, Y = 纵深移动
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        _movement.SetInputDirection(new Vector2(horizontal, vertical));

        // 跳跃（通过物理添加临时 Y 偏移）
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));

        // 冲刺
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
    }
}
```

## 🎮 2D 专属特性

### 土狼时间（Coyote Time）

玩家离开平台后短时间内仍可跳跃：

```csharp
config.coyoteTime = 0.1f; // 100ms 宽限期
```

### 跳跃缓冲（Jump Buffer）

落地前按下跳跃会在落地时立即执行：

```csharp
config.jumpBufferTime = 0.1f; // 100ms 缓冲窗口
```

### 自动转向

角色自动翻转朝向移动方向：

```csharp
// 由输入方向控制
_movement.SetInputDirection(new Vector2(1, 0)); // 朝右
_movement.SetInputDirection(new Vector2(-1, 0)); // 朝左
```

### 空中控制

在空中可调整水平移动：

```csharp
config.airControlMultiplier = 0.5f; // 空中 50% 控制力
```

### 沟槽跨越（马里奥风格）

当快速奔跑时，角色会保持接地状态跨越小沟槽 - 就像马里奥一样！

```
快速奔跑 → 未检测到地面 → 检查前方 → 发现地面 → 保持接地！
```

| 参数                   | 说明                     | 默认值 |
| ---------------------- | ------------------------ | ------ |
| `enableGapBridging`    | 启用/禁用功能            | true   |
| `minSpeedForGapBridge` | 触发所需的最低速度 (m/s) | 4.0    |
| `maxGapDistance`       | 可跨越的最大沟槽宽度 (m) | 1.0    |

> **注意**：慢走时不会触发沟槽跨越 - 角色会正常掉入沟槽。

### AI 寻路（2D）

对于 2D 游戏，推荐使用 **A\* Pathfinding Project**，因为它原生支持 2D Grid 图。

| 系统              | 2D 支持 | 原因              |
| ----------------- | ------- | ----------------- |
| A\* Pathfinding   | ✅      | 原生 2D Grid 支持 |
| Unity NavMesh     | ❌      | 仅 XZ 平面        |
| Agents Navigation | ❌      | 专注 3D DOTS      |

#### 在 2D 中使用 A\* PathFinding

```csharp
// 需要: com.arongranberg.astar
var astarInput = GetComponent<AStarInputProvider>();

// 重要：在 Inspector 中启用 2D 模式
// - is2DMode: true

astarInput.SetDestination(targetPosition);

if (astarInput.HasReachedDestination)
{
    // 已到达目标
}
```

功能特性：

- 使用 A\* 原生 2D Grid/Point 图
- 在 XY 平面工作
- 通过反射调用 `MovementComponent2D.SetInputDirection`

## ⚙️ 配置

### MovementConfig2D 参数

| 分类     | 参数           | 描述         | 默认值  |
| -------- | -------------- | ------------ | ------- |
| **地面** | walkSpeed      | 行走速度     | 3.0     |
| **地面** | runSpeed       | 跑步速度     | 5.0     |
| **地面** | sprintSpeed    | 冲刺速度     | 8.0     |
| **空中** | jumpForce      | 跳跃力度     | 12.0    |
| **空中** | maxJumpCount   | 多段跳次数   | 1       |
| **空中** | maxFallSpeed   | 最大下落速度 | 20.0    |
| **物理** | gravity        | 重力         | 25.0    |
| **物理** | groundLayer    | 地面检测层   | Default |
| **手感** | coyoteTime     | 延迟跳跃窗口 | 0.1s    |
| **手感** | jumpBufferTime | 提前跳跃窗口 | 0.1s    |

## 🔄 与 3D 版本的区别

| 特性         | 3D (MovementComponent)         | 2D (MovementComponent2D) |
| ------------ | ------------------------------ | ------------------------ |
| **物理**     | CharacterController            | Rigidbody2D              |
| **移动**     | float3 (XYZ)                   | float2 (XY)              |
| **重力**     | 手动计算                       | Physics2D.gravity        |
| **地面检测** | CharacterController.isGrounded | Physics2D.OverlapBox     |
| **旋转**     | Slerp 向移动方向               | X 轴翻转(横板卷轴)       |
| **土狼时间** | ❌                             | ✅                       |
| **跳跃缓冲** | ❌                             | ✅                       |

## 🎬 慢动作支持

与 3D 版本相同：

```csharp
// 全局慢动作
Time.timeScale = 0.2f;

// 角色独立时间缩放
movementComponent.LocalTimeScale = 1.5f;

// 忽略全局时间缩放
movementComponent.ignoreTimeScale = true;
```

## 🔌 GAS 集成

与 3D 版本接口相同：

```csharp
public class GASMovementAuthority2D : MonoBehaviour, IMovementAuthority
{
    public bool CanEnterState(MovementStateType stateType, object context)
    {
        if (stateType == MovementStateType.Sprint)
        {
            return HasStamina();
        }
        return true;
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

// 注入
movement.MovementAuthority = GetComponent<GASMovementAuthority2D>();
```

## 🎛️ 属性修改系统

移动系统支持在运行时修改所有移动属性。

### 简单使用（无需 GAS）

```csharp
using CycloneGames.RPGFoundation.Runtime.Movement;
using UnityEngine;

public class SimpleAttributeController2D : MonoBehaviour
{
    void Start()
    {
        var movement = GetComponent<MovementComponent2D>();
        var authority = GetComponent<MovementAttributeAuthority>();

        if (authority == null)
        {
            authority = gameObject.AddComponent<MovementAttributeAuthority>();
        }

        movement.MovementAuthority = authority;

        // 覆盖基础值
        authority.SetBaseValueOverride(MovementAttribute.RunSpeed, 7f);
        authority.SetMultiplier(MovementAttribute.JumpForce, 1.2f);
    }
}
```

### GAS 集成

```csharp
#if GAMEPLAY_ABILITIES_PRESENT
using CycloneGames.RPGFoundation.Runtime.Movement;
using UnityEngine;

public class GASAttributeController2D : MonoBehaviour
{
    void Start()
    {
        var movement = GetComponent<MovementComponent2D>();
        var gasAuthority = GetComponent<GASMovementAttributeAuthority>();

        if (gasAuthority == null)
        {
            gasAuthority = gameObject.AddComponent<GASMovementAttributeAuthority>();
        }

        movement.MovementAuthority = gasAuthority;

        // 映射 GAS 属性
        gasAuthority.AddAttributeMapping(
            MovementAttribute.RunSpeed,
            "Attribute.Secondary.Speed",
            baseValue: 100f
        );
    }
}
#endif
```

**支持的属性**：WalkSpeed, RunSpeed, SprintSpeed, CrouchSpeed, JumpForce, Gravity, AirControlMultiplier

## 📊 API 参考

### MovementComponent2D

```csharp
// 属性
MovementStateType CurrentState { get; }
bool IsGrounded { get; }
float CurrentSpeed { get; }        // 目标速度（在 Idle 状态下重置为 0）
Vector2 Velocity { get; }         // 实际速度向量（推荐用于 BlendTree）
bool IsMoving { get; }
IMovementAuthority MovementAuthority { get; set; }

// 方法
void SetInputDirection(Vector2 direction);
void SetJumpPressed(bool pressed);
void SetSprintHeld(bool held);
void SetCrouchHeld(bool held);
bool RequestStateChange(MovementStateType type);

// 事件
event Action<MovementStateType, MovementStateType> OnStateChanged;
event Action OnJumpStart;
event Action OnLanded;
```

### 动画 BlendTree

对于 BlendTree 动画，使用 `Velocity.magnitude` 以获得平滑插值：

```csharp
void Update()
{
    var movement = GetComponent<MovementComponent2D>();

    // 推荐：使用 Velocity.magnitude 做 BlendTree
    animator.SetFloat("Speed", movement.Velocity.magnitude);

    // 也可以使用：CurrentSpeed（在 Idle 状态下会重置为 0）
    // animator.SetFloat("Speed", movement.CurrentSpeed);
}
```

## 🎯 最佳实践

### ✅ 应该

- 在角色脚部设置 `groundCheck` Transform
- 使用 `coyoteTime` 和 `jumpBufferTime` 获得更好手感
- 配置 `groundLayer` 避免错误的地面检测
- 使用 `maxFallSpeed` 防止过快的下落速度
- 使用 `Velocity.magnitude` 做 BlendTree 动画（更平滑的过渡）
- 使用 `MovementAttributeAuthority` 进行运行时属性修改

### ❌ 不应该

- 混合使用 2D 和 3D 物理组件
- 忘记将 Rigidbody2D 设置为 Continuous 碰撞检测
- 在非 2D 游戏中使用（请使用 MovementComponent）
- 如果需要平滑插值，在 BlendTree 中使用 `CurrentSpeed`（应使用 `Velocity.magnitude`）
