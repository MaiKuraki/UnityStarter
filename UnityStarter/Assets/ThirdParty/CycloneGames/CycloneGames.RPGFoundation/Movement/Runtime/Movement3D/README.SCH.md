# RPG 移动模块

基于状态机的高性能角色移动模块，专为 Unity RPG 游戏设计，Gameplay Ability System (GAS) 适配良好。

<p align="left"><br> <a href="README.md">English</a> | 简体中文</p>

## GameplayFramework 集成

`MovementComponent` 不直接依赖 `CycloneGames.GameplayFramework`。移动逻辑和生成所有权保持解耦，因此基础 Movement Runtime 可以在不包含 GameplayFramework 的 Unity 项目、headless 工具和不同 package 布局中正常编译。

强类型 GameplayFramework 集成应放在独立 integration asmdef 中，并由该 asmdef 通过 `versionDefines` 和 `defineConstraints` 启用。不要把项目级全局 scripting define symbol 作为常规开关。

如果没有启用 GameplayFramework integration assembly，请在生成逻辑中设置初始旋转。
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