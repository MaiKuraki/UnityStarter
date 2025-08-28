# CycloneGames.InputSystem

>注意：CycloneGames.InputSystem 代码由作者编写，文档由AI辅助编写

一个基于 Unity 新输入系统的响应式输入高级封装：支持上下文栈（Action Map）、本地多人、设备锁定、基于 YAML 的可视化配置与编辑器工具。

[English](./README.md) | 简体中文

## 功能特点

- **上下文栈**：通过 Push/Pop 管理输入状态（如：游戏、UI、过场动画）。
- **丰富的多人模式**：
    - **单人模式**：自动加入并将所有必需设备锁定给单个玩家。
    - **大厅（设备锁定）Lobby (Device Locking)**：第一个设备加入成为玩家0。后续接入的设备会自动配对给该玩家，非常适合单人玩家在键鼠和手柄间无缝切换的场景。
    - **大厅（设备共享）Lobby (Shared Devices)**：每个新设备加入都会创建一个新玩家（玩家0, 1, 2...），是本地多人合作的理想选择。
- **零GC API**：可选的高性能 API，通过代码生成常量来彻底消除运行时的字符串操作和垃圾回收。
- **可配置的代码生成**：
    - 根据您的 YAML 配置自动生成静态的 `InputActions` 类。
    - 可自定义生成文件的输出目录和命名空间，以适应您的项目结构，保持 `Packages` 目录的整洁。
- **响应式 API (R3)**：为按钮的短按、长按、模拟量输入等提供 `Observable` 事件流。
- **智能热插拔**：在大厅阶段结束后，能自动将新连接的设备配对给正确的玩家。
- **活动设备检测**：`ActiveDeviceKind` 属性可以实时追踪玩家最后一次使用的设备是键鼠还是手柄。

## 安装依赖

- Unity 2022.3+
- Unity Input System
- 依赖：UniTask、R3、VYaml、CycloneGames.Utility、CycloneGames.Logger

## 快速上手

1) 生成默认配置：`Tools → CycloneGames → Input System Editor → Generate Default Config`。
2) **（推荐）** 配置代码生成：
    - 在编辑器窗口中，为即将生成的 `InputActions.cs` 文件设置**输出目录**（例如 `Assets/Scripts/Generated`）和**命名空间**。
    - 点击 **Save and Generate Constants**。
3) 启动时初始化：

```csharp
var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

1) 使用生成的常量来加入游戏并设置上下文，以获得最佳性能和类型安全：

```csharp
// 确保引入了您自定义的命名空间
using YourGame.Input.Generated;

var svc = InputManager.Instance.JoinSinglePlayer(0);
var ctx = new InputContext("Gameplay", "PlayerActions")
  .AddBinding(svc.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(() => {/*...*/}));
svc.RegisterContext(ctx);
svc.PushContext("Gameplay");
```

> **注意**：如果您不想使用代码生成功能，原始的基于字符串的 API (`GetVector2Observable("PlayerActions", "Move")`) 仍然完全可用。

## YAML 配置示例

```yaml
joinAction:
  type: Button
  action: JoinGame
  deviceBindings:
    - "<Keyboard>/enter"
    - "<Gamepad>/start"
playerSlots:
  - playerId: 0
    contexts:
      - name: Gameplay
        actionMap: PlayerActions
        bindings:
          - type: Vector2
            action: Move
            deviceBindings:
              - "<Gamepad>/leftStick"
              - "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
              - "<Mouse>/delta"
          - type: Button
            action: Confirm
            longPressMs: 500 # 可选，长按 500ms 触发
            deviceBindings:
              - "<Gamepad>/buttonSouth"
              - "<Keyboard>/space"
          - type: Float
            action: FireTrigger
            longPressMs: 600                 # 可选：浮点长按
            longPressValueThreshold: 0.6     # 阈值（0-1）达到后视为按下
            deviceBindings:
              - "<Gamepad>/leftTrigger"
```

## 简单示例

1) 新建一个 MonoBehaviour：`SimplePlayer`

```csharp
using UnityEngine;
using CycloneGames.InputSystem.Runtime;

public class SimplePlayer : MonoBehaviour
{
  private IInputService _input;

  private void Start()
  {
    // 加入玩家0并创建游戏上下文
    _input = InputManager.Instance.JoinSinglePlayer(0);
    var ctx = new InputContext("Gameplay", "PlayerActions")
      .AddBinding(_input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
      .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirm))
      // 可选：长按（需要在 YAML 中为 "Confirm" 设置 longPressMs）
      .AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirmLongPress));

    _input.RegisterContext(ctx);
    _input.PushContext("Gameplay");
  }

  private void OnMove(Vector2 dir)
  {
    // 使用 dir.x, dir.y 控制移动
    transform.position += new Vector3(dir.x, 0f, dir.y) * Time.deltaTime * 5f;
  }

  private void OnConfirm()
  {
    Debug.Log("Confirm 按下");
  }

  private void OnConfirmLongPress()
  {
    Debug.Log("Confirm 长按");
  }
}
```

## 不同上下文的短按/长按（互斥）

如果同一个按键在不同情景下需要“短按触发”或“长按触发”，且不能同时触发，建议通过不同的输入上下文来实现：

YAML 示例：

```yaml
playerSlots:
  - playerId: 0
    contexts:
      - name: Inspect
        actionMap: PlayerActions
        bindings:
          - type: Button
            action: Confirm
            # 仅短按（不配置 longPressMs）
            deviceBindings:
              - "<Keyboard>/space"
              - "<Gamepad>/buttonSouth"
      - name: Charge
        actionMap: PlayerActions
        bindings:
          - type: Button
            action: Confirm
            longPressMs: 600  # 仅长按（该上下文下才触发）
            deviceBindings:
              - "<Keyboard>/space"
              - "<Gamepad>/buttonSouth"
```

运行时代码：

```csharp
// Inspect 上下文：只绑定短按
ctxInspect.AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnInspectConfirm));

// Charge 上下文：只绑定长按
ctxCharge.AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnChargeConfirm));

// 根据逻辑切换上下文
_input.RegisterContext(ctxInspect);
_input.RegisterContext(ctxCharge);
_input.PushContext("Inspect"); // 需要时切换：_input.PushContext("Charge")
```

## 教程：按住增加进度条（松手停止/可重置）

目标：按住按钮时持续增加进度条，松手时停止（可选择是否清零）。

1) 订阅按下状态：

```csharp
var isPressing = _input.GetPressStateObservable("PlayerActions", "Confirm");
```

### 浮点/Trigger 的长按

YAML（Float 带阈值）：

```yaml
- type: Float
  action: FireTrigger
  longPressMs: 600
  longPressValueThreshold: 0.6
  deviceBindings:
    - "<Gamepad>/leftTrigger"
```

代码：

```csharp
_input.GetLongPressObservable("PlayerActions", "FireTrigger").Subscribe(_ => StartCharge());
_input.GetPressStateObservable("PlayerActions", "FireTrigger").Where(p => !p).Subscribe(_ => CancelCharge());
```

### 同一上下文内短按/长按互斥

如果不切换上下文，需要在同一上下文内判定短按或长按且互斥，可结合按下状态与长按流：

```csharp
var press = _input.GetPressStateObservable("PlayerActions", "Confirm");
var longPress = _input.GetLongPressObservable("PlayerActions", "Confirm").Share();
float thresholdSec = 0.5f; // 与 YAML 保持一致

bool isPressed = false;
float startTime = 0f;
bool longFired = false;

longPress.Subscribe(_ => longFired = true);
press.Subscribe(p =>
{
  if (p)
  {
    isPressed = true; startTime = Time.realtimeSinceStartup; longFired = false;
  }
  else if (isPressed)
  {
    var dur = Time.realtimeSinceStartup - startTime;
    if (!longFired && dur < thresholdSec) OnShortClick();
    if (longFired) OnLongPress();
    isPressed = false;
  }
});
```

### 编辑器提示

- **代码生成**：编辑器窗口提供了设置选项，可自定义生成的 `InputActions.cs` 文件的输出目录和命名空间。这些设置会保存在项目的 `EditorPrefs` 中。
- **长按**：“Long Press (ms)” 字段仅对 `Button` 和 `Float` 类型的动作有效。对于 `Float` 类型，您还可以设置 “Long Press Threshold (0-1)” 来定义模拟量的“按下”阈值。
- **Vector2 来源**：`InputBindingConstants.Vector2Sources` 类为常用的 Vector2 绑定（如 `Gamepad_LeftStick` 和 `Composite_WASD`）提供了方便的常量。

2) 在按住期间逐帧累加：

```csharp
float progress = 0f;
float speed = 0.4f; // 每秒增长 40%

isPressing.Subscribe(pressed =>
{
  if (pressed)
  {
    UniTask.Void(async () =>
    {
      while (pressed && progress < 1f)
      {
        await UniTask.Yield();
        progress = Mathf.Min(1f, progress + Time.deltaTime * speed);
        // 更新 UI
      }
    });
  }
  else
  {
    // 松手：停止。可选清零：
    // progress = 0f;
  }
});
```

可选：要求先长按一段时间再开始。在 YAML 设置：

```yaml
- type: Button
  action: Confirm
  longPressMs: 500
  deviceBindings:
    - "<Keyboard>/space"
    - "<Gamepad>/buttonSouth"
```

然后用长按开始，松手停止：

```csharp
_input.GetLongPressObservable("PlayerActions", "Confirm").Subscribe(_ => StartFilling());
_input.GetPressStateObservable("PlayerActions", "Confirm").Where(p => !p).Subscribe(_ => StopFilling());
```

1) 确保 YAML 中存在对应动作：

```yaml
bindings:
  - type: Vector2
    action: Move
    deviceBindings:
      - "<Gamepad>/leftStick"
      - "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
  - type: Button
    action: Confirm
    deviceBindings:
      - "<Gamepad>/buttonSouth"
      - "<Keyboard>/space"
```

设备类型用法：

```csharp
_input.ActiveDeviceKind.Subscribe(kind => UpdateHUDIcons(kind));
```

## API 概览

- `IInputService`
  - `ReadOnlyReactiveProperty<string> ActiveContextName`
  - `ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind`
  - `event Action<string> OnContextChanged`
  - **零GC API (推荐)**
    - `GetVector2Observable(int actionId)`
    - `GetButtonObservable(int actionId)`
    - `GetLongPressObservable(int actionId)`
    - `GetPressStateObservable(int actionId)`
    - `GetScalarObservable(int actionId)`
  - **基于字符串的 API (兼容/可选)**
    - `Get...Observable(string actionName)`
    - `Get...Observable(string actionMapName, string actionName)`
  - `RegisterContext(InputContext context)`
  - `PushContext(string contextName)`
  - `PopContext()`
  - `BlockInput()`, `UnblockInput()`
