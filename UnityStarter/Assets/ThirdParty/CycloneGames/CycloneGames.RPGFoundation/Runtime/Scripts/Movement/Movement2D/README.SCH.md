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

- **DNF 类游戏** - 横版格斗
- **平台跳跃游戏** - 恶魔城、银河战士
- **2D 格斗游戏** - 街霸、拳皇风格
- **2.5D 游戏** - Trine、小小大星球

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
        // 仅水平输入
        float horizontal = Input.GetAxis("Horizontal");
        _movement.SetInputDirection(new Vector2(horizontal, 0));
        
        // 跳跃
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
| **旋转**     | Slerp向移动方向                | X轴翻转(横板卷轴)        |
| **土狼时间** | ❌                              | ✅                        |
| **跳跃缓冲** | ❌                              | ✅                        |

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

// 注入
movement.MovementAuthority = GetComponent<GASMovementAuthority2D>();
```

## 📊 API 参考

### MovementComponent2D

```csharp
// 属性
MovementStateType CurrentState { get; }
bool IsGrounded { get; }
float CurrentSpeed { get; }
Vector2 Velocity { get; }
bool IsMoving { get; }

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

## 🎯 最佳实践

### ✅ 应该

- 在角色脚部设置 `groundCheck` Transform
- 使用 `coyoteTime` 和 `jumpBufferTime` 获得更好手感
- 配置 `groundLayer` 避免错误的地面检测
- 使用 `maxFallSpeed` 防止过快的下落速度

### ❌ 不应该

- 混合使用 2D 和 3D 物理组件
- 忘记将 Rigidbody2D 设置为 Continuous 碰撞检测
- 在非 2D 游戏中使用（请使用 MovementComponent）