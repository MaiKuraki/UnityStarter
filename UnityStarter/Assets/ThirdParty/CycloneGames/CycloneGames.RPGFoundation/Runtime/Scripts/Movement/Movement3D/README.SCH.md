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
        // 获取输入（本地空间 - 相对于角色的前后左右）
        Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector3 localInput = new Vector3(input.x, 0, input.y);

        // 发送到移动组件（InputDirection 是本地空间）
        // 移动系统会根据角色的朝向自动将其转换为世界空间
        _movement.SetInputDirection(localInput);
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

## 🎛️ 属性修改系统

移动系统支持通过权限系统在运行时修改所有移动属性。

### 简单使用（无需 GAS）

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

        // 覆盖基础值
        authority.SetBaseValueOverride(MovementAttribute.RunSpeed, 7f);
        authority.SetBaseValueOverride(MovementAttribute.JumpForce, 15f);

        // 应用修改器
        authority.SetMultiplier(MovementAttribute.RunSpeed, 1.5f);
    }
}
```

### GAS 集成

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

**支持的属性**：WalkSpeed, RunSpeed, SprintSpeed, CrouchSpeed, JumpForce, Gravity, AirControlMultiplier, RotationSpeed

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

**注意**：对于 BlendTree 动画，建议使用 `Velocity.magnitude` 而不是 `CurrentSpeed`，以获得更平滑的过渡：

```csharp
// 推荐用于 BlendTree
animator.SetFloat("Speed", movement.Velocity.magnitude);

// 也可以使用（CurrentSpeed 在 Idle 状态下会重置为 0）
animator.SetFloat("Speed", movement.CurrentSpeed);
```

## 🎯 最佳实践

### ✅ 应该

- 为每种角色类型创建一个 `MovementConfig` 资产
- 使用 `IMovementStateQuery` 读取移动状态
- 订阅事件以获得视觉反馈（粒子、声音）
- 使用 `RequestStateChange()` 进行显式状态转换
- 使用 `Velocity.magnitude` 做 BlendTree 动画（更平滑的过渡）
- 使用 `MovementAttributeAuthority` 进行运行时属性修改

### ❌ 不应该

- 直接修改 `_currentState` 或内部状态
- 在使用基于状态的输入时调用 `MoveWithVelocity()`
- 混合使用输入方法（使用 `SetInput*` 方法或 `MoveWithVelocity`，二选一）
- 如果需要平滑插值，在 BlendTree 中使用 `CurrentSpeed`（应使用 `Velocity.magnitude`）

## 🔍 API 参考

### MovementComponent

#### 属性

```csharp
MovementStateType CurrentState { get; }          // 当前移动状态
bool IsGrounded { get; }                         // 角色是否在地面
float CurrentSpeed { get; }                      // 目标速度（在 Idle 状态下重置为 0）
Vector3 Velocity { get; }                        // 实际速度向量（推荐用于 BlendTree）
bool IsMoving { get; }                           // 角色是否在移动
IMovementAuthority MovementAuthority { get; set; } // 属性修改权限控制器
```

#### 方法

```csharp
void SetInputDirection(Vector3 localDirection);  // 设置本地空间的移动方向（x=右，z=前）
void SetJumpPressed(bool pressed);               // 跳跃输入
void SetSprintHeld(bool held);                   // 冲刺输入
void SetCrouchHeld(bool held);                   // 蹲伏输入
void SetLookDirection(Vector3 worldDirection);   // 设置旋转目标方向（移动和旋转已分离）
void ClearLookDirection();                       // 清除旋转目标，停止自动旋转
void SetRotation(Quaternion rotation, bool immediate = false); // 直接设置旋转
void SetRotation(Vector3 worldDirection, bool immediate = false); // 从方向设置旋转
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
- **属性修改** - 运行时属性修改无 GC 分配

## 🔗 GameplayFramework 集成

### 自动旋转同步

当 `MovementComponent` 与 `CycloneGames.GameplayFramework` 一起使用时，组件会在 Pawn 生成时自动同步其旋转。这是通过 `IInitialRotationSettable` 接口实现的。

#### Package Manager 安装（推荐）

如果 `RPGFoundation` 和 `GameplayFramework` 都通过 Package Manager 安装：

- ✅ **自动**：`GAMEPLAY_FRAMEWORK_PRESENT` 定义符号会通过 asmdef 中的 `versionDefines` 自动设置
- ✅ **无需配置**：旋转同步自动工作

#### 直接放在 Assets 文件夹

如果 `RPGFoundation` 直接放在 `Assets` 文件夹中（非 Package 形式）：

- ⚠️ **需要手动设置**：必须在 `PlayerSettings > Scripting Define Symbols` 中手动设置 `GAMEPLAY_FRAMEWORK_PRESENT` 定义符号
- ⚠️ **否则**：自动旋转同步将不会工作，您必须在生成后手动设置 Pawn 的旋转

#### 手动设置旋转（当定义符号未设置时）

如果 `GAMEPLAY_FRAMEWORK_PRESENT` 未定义，您需要在生成后手动设置旋转：

```csharp
// 在您的 GameMode 或生成逻辑中
Pawn pawn = SpawnDefaultPawnAtTransform(playerController, spawnTransform);

// 为 MovementComponent 手动设置旋转
var movement = pawn.GetComponent<MovementComponent>();
if (movement != null)
{
    movement.SetRotation(spawnTransform.rotation, immediate: true);
}
```

### 控制旋转

**移动和旋转已分离** - `MovementComponent` 只负责移动，不自动旋转。您必须使用以下方法之一手动控制旋转：

```csharp
// 设置朝向方向（平滑旋转到目标方向）
movement.SetLookDirection(targetDirection);

// 立即设置旋转
movement.SetRotation(targetRotation, immediate: true);

// 从方向设置旋转
movement.SetRotation(targetDirection, immediate: true);

// 清除朝向方向（停止自动旋转）
movement.ClearLookDirection();
```

**示例：分离移动和旋转输入**

以下是 `CalculateLookDirection` 的几种常见实现方式：

**选项 1：基于欧拉角的鼠标视角（第一/第三人称）**

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime;

public class PlayerController : MonoBehaviour
{
    private MovementComponent _movement;
    private Camera _camera;

    [Header("旋转设置")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minVerticalAngle = -80f;
    [SerializeField] private float maxVerticalAngle = 80f;

    private float _verticalRotation = 0f;
    private float _horizontalRotation = 0f;

    void Awake()
    {
        _movement = GetComponent<MovementComponent>();
        _camera = Camera.main; // 或分配您的相机引用
    }

    void Update()
    {
        // 移动输入（本地空间 - 相对于角色的前后左右）
        Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector3 localInput = new Vector3(moveInput.x, 0, moveInput.y);
        _movement.SetInputDirection(localInput);

        // 旋转输入（鼠标视角）
        Vector2 lookInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        Vector3 targetLookDirection = CalculateLookDirection(lookInput);
        _movement.SetLookDirection(targetLookDirection);
    }

    private Vector3 CalculateLookDirection(Vector2 lookInput)
    {
        // 累积旋转
        _horizontalRotation += lookInput.x * mouseSensitivity;
        _verticalRotation -= lookInput.y * mouseSensitivity;
        _verticalRotation = Mathf.Clamp(_verticalRotation, minVerticalAngle, maxVerticalAngle);

        // 转换为方向向量
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

**选项 2：基于相机的方向（第三人称相机跟随）**

```csharp
private Vector3 CalculateLookDirection(Vector2 lookInput)
{
    if (_camera == null) return transform.forward;

    // 获取相机的向前方向（投影到水平面）
    Vector3 cameraForward = _camera.transform.forward;
    cameraForward.y = 0f; // 移除垂直分量
    cameraForward.Normalize();

    // 根据鼠标输入旋转
    float horizontalRotation = lookInput.x * mouseSensitivity;
    Quaternion rotation = Quaternion.Euler(0, horizontalRotation, 0);

    return rotation * cameraForward;
}
```

**选项 3：屏幕到世界的射线检测（点击朝向）**

```csharp
private Vector3 CalculateLookDirection(Vector2 lookInput)
{
    // 用于点击朝向或屏幕空间输入
    if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
    {
        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Vector3 direction = (hit.point - transform.position);
            direction.y = 0f; // 保持水平
            return direction.normalized;
        }
    }

    // 回退：使用当前向前方向
    return transform.forward;
}
```

**选项 4：手柄右摇杆**

```csharp
private Vector3 CalculateLookDirection(Vector2 lookInput)
{
    // 用于手柄右摇杆输入
    if (lookInput.magnitude < 0.1f)
        return transform.forward; // 无输入，保持当前方向

    // 获取相机的右和向前向量（仅水平）
    Vector3 cameraRight = _camera.transform.right;
    Vector3 cameraForward = _camera.transform.forward;
    cameraRight.y = 0f;
    cameraForward.y = 0f;
    cameraRight.Normalize();
    cameraForward.Normalize();

    // 根据摇杆输入组合
    Vector3 direction = (cameraForward * lookInput.y + cameraRight * lookInput.x).normalized;
    return direction;
}
```

**选项 5：第三人称动作游戏（基于相机的移动）**

适用于第三人称动作游戏，其中：

- 相机跟随角色
- 移动输入相对于相机方向（而非角色方向）
- 角色自动面向移动方向

```csharp
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime;

public class ThirdPersonPlayerController : MonoBehaviour
{
    private MovementComponent _movement;
    private Camera _camera;

    [Header("移动设置")]
    [SerializeField] private bool autoFaceMovementDirection = true;
    [SerializeField] private float rotationSmoothing = 10f;

    void Awake()
    {
        _movement = GetComponent<MovementComponent>();
        _camera = Camera.main; // 或分配您的相机引用
    }

    void Update()
    {
        // 获取相机空间的输入（相对于相机的向前/右方向）
        Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

        // 将基于相机的输入转换为世界空间方向
        Vector3 worldMoveDirection = GetCameraRelativeMovementDirection(moveInput);

        // 将世界方向转换为本地空间供 MovementComponent 使用
        // MovementComponent 期望本地空间输入（相对于角色的向前/右方向）
        Vector3 localInput = transform.InverseTransformDirection(worldMoveDirection);
        _movement.SetInputDirection(localInput);

        // 可选：让角色面向移动方向
        if (autoFaceMovementDirection && moveInput.magnitude > 0.1f)
        {
            Vector3 lookDirection = worldMoveDirection;
            lookDirection.y = 0f; // 仅保持水平
            if (lookDirection.magnitude > 0.1f)
            {
                _movement.SetLookDirection(lookDirection.normalized);
            }
        }

        // 其他输入
        _movement.SetJumpPressed(Input.GetButtonDown("Jump"));
        _movement.SetSprintHeld(Input.GetButton("Sprint"));
        _movement.SetCrouchHeld(Input.GetKey(KeyCode.C));
    }

    /// <summary>
    /// 将基于相机的输入（WASD）转换为世界空间移动方向。
    /// 这允许相对于相机移动，而不是角色朝向。
    /// </summary>
    private Vector3 GetCameraRelativeMovementDirection(Vector2 input)
    {
        if (_camera == null || input.magnitude < 0.1f)
            return Vector3.zero;

        // 获取相机的向前和右向量（投影到水平面）
        Vector3 cameraForward = _camera.transform.forward;
        Vector3 cameraRight = _camera.transform.right;

        // 移除垂直分量以保持移动在水平面上
        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        // 根据输入组合相机方向
        // input.y 是前后（W/S），input.x 是左右（A/D）
        Vector3 direction = (cameraForward * input.y + cameraRight * input.x).normalized;

        return direction;
    }
}
```

**替代方案：更简单的基于相机的移动（无自动旋转）**

如果您想要基于相机的移动但不想要自动旋转：

```csharp
void Update()
{
    // 获取相机空间的输入
    Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

    // 转换为相对于相机的世界空间方向
    Vector3 worldMoveDirection = GetCameraRelativeMovementDirection(moveInput);

    // 将世界方向转换为角色的本地空间
    Vector3 localInput = transform.InverseTransformDirection(worldMoveDirection);
    _movement.SetInputDirection(localInput);

    // 旋转单独控制（例如，通过相机或鼠标视角）
    // 您可以使用选项 1 或选项 2 进行旋转控制
}
```

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
