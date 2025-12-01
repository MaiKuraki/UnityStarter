# RPG 移动模块

基于状态机的高性能角色移动模块，专为 Unity RPG 游戏设计，零 GC，Gameplay Ability System (GAS) 适配良好。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## ✨ 特性

- 🎮 **状态机架构** - 清晰的移动状态分离（静止、行走、冲刺、蹲伏、跳跃、下落）
- ⚡ **零垃圾回收** - 使用 Unity.Mathematics 实现 SIMD 加速的零分配计算
- 🔌 **GAS 集成就绪** - 可选的通过接口与 Gameplay Ability System 集成
- 🎯 **新手友好** - 无需任何依赖即可独立工作
- 📝 **ScriptableObject 配置** - 设计师友好的参数配置
- 🌍 **动态重力支持** - 支持更改重力方向，适用于行星移动
- 🎨 **动画就绪** - 内置 Animator 参数支持

## 📦 快速开始

### 步骤 1：创建配置

在 Project 窗口右键 → `Create > CycloneGames > RPG Foundation > Movement Config`

在 Inspector 中配置移动速度、跳跃力度等参数。

### 步骤 2：添加组件

在包含 `CharacterController` 的角色 GameObject 上添加 `MovementComponent`。

将创建的 `MovementConfig` 分配给该组件。

### 步骤 3：基础输入（无 GAS）

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
        // 获取输入
        Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector3 worldInput = transform.TransformDirection(new Vector3(input.x, 0, input.y));
        
        // 发送到移动组件
        _movement.SetInputDirection(worldInput);
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
        _movement.SetCrouchHeld(Input.GetKey(KeyCode.C));
    }
}
```

就这样！您的角色现在支持行走、冲刺、蹲伏和跳跃移动。

## 📚 核心概念

### 移动状态

系统使用状态机，包含以下状态：

| 状态       | 描述                                 |
| ---------- | ------------------------------------ |
| **Idle**   | 角色在地面上静止                     |
| **Walk**   | 慢速行走移动（移动时的默认状态）     |
| **Run**    | 正常跑步移动（比走路快）             |
| **Sprint** | 快速冲刺/Dash 移动（GAS 中需要耐力） |
| **Crouch** | 较慢的蹲伏移动                       |
| **Jump**   | 上升跳跃（支持多段跳）               |
| **Fall**   | 空中下落，带空中控制                 |

状态根据输入和物理条件自动转换。

### 零 GC 设计

系统使用 `Unity.Mathematics` 类型（`float3`、`quaternion`）而非 Unity 的 `Vector3` 和 `Quaternion`，以消除垃圾回收：

```csharp
// 传统方式（每帧分配内存）
Quaternion rotation = Quaternion.Slerp(a, b, t);

// 我们的方式（零分配）
quaternion rotation = math.slerp(a, b, t);
```

## 🎮 独立使用（无 GAS）

### 基础移动控制

```csharp
MovementComponent movement = GetComponent<MovementComponent>();

// 设置输入方向（归一化的世界空间向量）
movement.SetInputDirection(direction);

// 控制动作
movement.SetJumpPressed(true);
movement.SetSprintHeld(true);
movement.SetCrouchHeld(false);
```

### 查询移动状态

```csharp
IMovementStateQuery query = GetComponent<MovementComponent>();

if (query.IsGrounded)
{
    Debug.Log($"速度: {query.CurrentSpeed}");
    Debug.Log($"状态: {query.CurrentState}");
}
```

### 监听事件

```csharp
void Start()
{
    movement.OnStateChanged += OnMovementStateChanged;
    movement.OnJumpStart += OnJumped;
    movement.OnLanded += OnLanded;
}

void OnMovementStateChanged(MovementStateType from, MovementStateType to)
{
    Debug.Log($"状态: {from} → {to}");
}
```

## 🔌 GAS 集成（高级）

如果您使用 Gameplay Ability System，可以通过技能集成移动控制。

### 步骤 1：创建权限控制器

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
                // 检查玩家是否有足够的耐力
                return _asc.GetAttribute("Stamina")?.CurrentValue > 10f;
            
            case MovementStateType.Jump:
                // 检查跳跃是否在冷却中
                return !_asc.HasMatchingTag(GameplayTag.FromString("State.Cooldown.Jump"));
            
            default:
                return true;
        }
    }

    public void OnStateEntered(MovementStateType stateType)
    {
        // 进入状态时应用效果
        if (stateType == MovementStateType.Sprint)
        {
            // 应用耐力消耗效果
        }
    }

    public void OnStateExited(MovementStateType stateType)
    {
        // 退出状态时清理
    }
}
```

### 步骤 2：注入权限控制器

```csharp
void Start()
{
    var movement = GetComponent<MovementComponent>();
    var authority = GetComponent<GASMovementAuthority>();
    movement.MovementAuthority = authority;
}
```

### 步骤 3：从技能中控制

```csharp
public class RollAbility : GameplayAbility
{
    public override void ActivateAbility()
    {
        var movement = GetComponent<MovementComponent>();
        
        // 请求状态变更（会先询问权限控制器）
        if (movement.RequestStateChange(MovementStateType.Roll))
        {
            CommitAbility(); // 应用消耗和冷却
        }
        else
        {
            CancelAbility();
        }
    }
}
```

## ⚙️ 配置

### MovementConfig 参数

| 参数              | 描述           | 默认值 |
| ----------------- | -------------- | ------ |
| **walkSpeed**     | 行走速度       | 3.0    |
| **runSpeed**      | 跑步速度       | 5.0    |
| **sprintSpeed**   | 冲刺速度       | 8.0    |
| **crouchSpeed**   | 蹲伏速度       | 1.5    |
| **jumpForce**     | 向上跳跃速度   | 10.0   |
| **maxJumpCount**  | 允许的跳跃次数 | 1      |
| **gravity**       | 重力加速度     | -25.0  |
| **rotationSpeed** | 角色旋转速度   | 20.0   |

### 动画参数

组件会自动设置这些 Animator 参数：

- `MovementSpeed` (Float) - 当前移动速度
- `IsGrounded` (Bool) - 角色是否在地面上
- `Jump` (Trigger) - 跳跃动作触发器

## 🎯 最佳实践

### ✅ 应该

- 为每种角色类型创建一个 `MovementConfig` 资产
- 使用 `IMovementStateQuery` 读取移动状态
- 订阅事件以获得视觉反馈（粒子、声音）
- 使用 `RequestStateChange()` 进行显式状态转换

### ❌ 不应该

- 直接修改 `_currentState` 或内部状态
- 在使用基于状态的输入时调用 `MoveWithVelocity()`
- 混合使用输入方法（使用 `SetInput*` 方法或 `MoveWithVelocity`，二选一）

## 🔍 API 参考

### MovementComponent

#### 属性

```csharp
MovementStateType CurrentState { get; }          // 当前移动状态
bool IsGrounded { get; }                         // 角色是否在地面
float CurrentSpeed { get; }                      // 当前移动速度
Vector3 Velocity { get; }                        // 当前速度
bool IsMoving { get; }                           // 角色是否在移动
IMovementAuthority MovementAuthority { get; set; } // 可选的 GAS 权限控制器
```

#### 方法

```csharp
void SetInputDirection(Vector3 direction);       // 设置移动方向
void SetJumpPressed(bool pressed);               // 跳跃输入
void SetSprintHeld(bool held);                   // 冲刺输入
void SetCrouchHeld(bool held);                   // 蹲伏输入
bool RequestStateChange(MovementStateType type); // 请求状态转换
```

#### 事件

```csharp
event Action<MovementStateType, MovementStateType> OnStateChanged;
event Action OnJumpStart;
event Action OnLanded;
```

## 🚀 性能

- **零 GC 分配** - 所有核心逻辑使用值类型
- **SIMD 加速** - Unity.Mathematics 利用 CPU 向量指令
- **状态池化** - 状态实例通过对象池复用
- **优化的旋转** - 使用 `math.slerp` 而非 `Quaternion.Slerp`

## 🎨 扩展系统

### 添加新状态

1. 创建继承自 `MovementStateBase` 的新状态类
2. 实现必需的方法（`OnEnter`、`OnUpdate`、`OnExit`、`EvaluateTransition`）
3. 将状态添加到 `MovementStateType` 枚举
4. 在 `MovementComponent.GetStateByType()` 中注册

示例：

```csharp
public class DashState : MovementStateBase
{
    public override MovementStateType StateType => MovementStateType.Dash;

    public override void OnEnter(ref MovementContext context)
    {
        // 初始化冲刺
    }

    public override void OnUpdate(ref MovementContext context, out float3 displacement)
    {
        // 执行冲刺移动
        displacement = context.InputDirection * context.Config.dashSpeed * context.DeltaTime;
    }

    public override MovementStateBase EvaluateTransition(ref MovementContext context)
    {
        // 冲刺完成后返回行走状态
        return StatePool.GetState<WalkState>();
    }
}
```