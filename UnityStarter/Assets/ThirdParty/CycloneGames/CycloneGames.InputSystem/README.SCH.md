# CycloneGames.InputSystem

> 注意：CycloneGames.InputSystem 代码由作者编写，文档由 AI 辅助编写

一个基于 Unity 新输入系统的响应式输入高级封装：支持上下文栈（Action Map）、本地多人、设备锁定、基于 YAML 的可视化配置与编辑器工具。

[English](./README.md) | 简体中文

## 功能特点

- **上下文栈**：通过 Push/Pop 管理输入状态（如：游戏、UI、过场动画）。
- **丰富的多人模式**：
  - **单人模式**：自动加入并将所有必需设备锁定给单个玩家。
  - **大厅（设备锁定）Lobby (Device Locking)**：第一个设备加入成为玩家 0。后续接入的设备会自动配对给该玩家，非常适合单人玩家在键鼠和手柄间无缝切换的场景。
  - **大厅（设备共享）Lobby (Shared Devices)**：每个新设备加入都会创建一个新玩家（玩家 0, 1, 2...），是本地多人合作的理想选择。
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

### 步骤 1：生成默认配置

打开编辑器窗口：`Tools → CycloneGames → Input System Editor`，然后点击 **Generate Default Config** 生成默认的 YAML 配置文件。

### 步骤 2：配置代码生成（推荐）

在编辑器窗口中：

1. 设置**输出目录**（例如 `Assets/Scripts/Generated`）和**命名空间**（例如 `YourGame.Input.Generated`）。
2. 点击 **Save and Generate Constants** 保存配置并生成 `InputActions.cs` 文件。

### 步骤 3：启动时初始化

在游戏启动时（例如在 `MonoBehaviour.Start()` 或初始化脚本中）加载配置：

```csharp
using CycloneGames.Utility.Runtime;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;

// 在启动时调用
var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

`InputSystemLoader` 会优先加载用户配置（`PersistentData`），如果不存在则回退到默认配置（`StreamingAssets`）。首次运行时，会自动将默认配置复制到用户配置目录。

### 步骤 4：加入玩家并设置上下文

**使用生成的常量（推荐）：**

```csharp
// 确保引入了您自定义的命名空间
using YourGame.Input.Generated;
using CycloneGames.InputSystem.Runtime;

var svc = InputManager.Instance.JoinSinglePlayer(0);
var ctx = new InputContext("Gameplay", "PlayerActions")
  .AddBinding(svc.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(() => {/*...*/}));
svc.RegisterContext(ctx);
svc.PushContext("Gameplay");
```

**使用字符串 API（兼容模式）：**

```csharp
var svc = InputManager.Instance.JoinSinglePlayer(0);
var ctx = new InputContext("Gameplay", "PlayerActions")
  .AddBinding(svc.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(() => {/*...*/}));
svc.RegisterContext(ctx);
svc.PushContext("Gameplay");
```

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
            longPressMs: 600 # 可选：浮点长按
            longPressValueThreshold: 0.6 # 阈值（0-1）达到后视为按下
            deviceBindings:
              - "<Gamepad>/leftTrigger"
```

## 简单示例

创建一个简单的玩家控制器：

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

  private void OnDestroy()
  {
    // 清理：InputService 会在 InputManager.Dispose() 时自动清理
    // 如果需要在组件销毁时提前清理，可以调用：
    // (_input as IDisposable)?.Dispose();
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
            longPressMs: 600 # 仅长按（该上下文下才触发）
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

## 高级用法

### 多人游戏模式

#### 单人模式（自动锁定设备）

```csharp
// 自动加入玩家0，并将所有必需设备锁定给该玩家
var svc = InputManager.Instance.JoinSinglePlayer(0);
```

#### 大厅模式（设备锁定）

第一个设备加入成为玩家 0，后续设备自动配对给该玩家，适合单人玩家在键鼠和手柄间切换：

```csharp
InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;
InputManager.Instance.StartListeningForPlayers(true); // true = 设备锁定模式
```

#### 大厅模式（设备共享）

每个新设备加入都会创建一个新玩家，适合本地多人合作：

```csharp
InputManager.Instance.OnPlayerJoined += HandlePlayerJoined;
InputManager.Instance.StartListeningForPlayers(false); // false = 设备共享模式
```

#### 批量加入玩家

```csharp
// 同步批量加入
var players = InputManager.Instance.JoinPlayersBatch(new List<int> { 0, 1, 2 });

// 异步批量加入（带超时）
var players = await InputManager.Instance.JoinPlayersBatchAsync(
    new List<int> { 0, 1, 2 },
    timeoutPerPlayerInSeconds: 5
);
```

#### 异步加入玩家（等待设备连接）

```csharp
// 等待设备连接，超时时间 5 秒
var svc = await InputManager.Instance.JoinSinglePlayerAsync(0, timeoutInSeconds: 5);
if (svc != null)
{
    // 玩家成功加入
}
```

#### 手动锁定设备

```csharp
// 将特定设备锁定给特定玩家
var svc = InputManager.Instance.JoinPlayerAndLockDevice(0, Keyboard.current);
```

#### 共享设备模式

```csharp
// 多个玩家共享键盘（适合回合制游戏）
var player0 = InputManager.Instance.JoinPlayerOnSharedDevice(0);
var player1 = InputManager.Instance.JoinPlayerOnSharedDevice(1);
```

### 配置热重载

在运行时重新加载配置（例如，玩家修改了按键绑定）：

```csharp
bool success = await InputManager.Instance.ReloadConfigurationAsync();
if (success)
{
    Debug.Log("配置已重新加载");
    // 新加入的玩家将使用新配置
}
```

### 保存用户配置

将当前配置保存到用户配置目录：

```csharp
await InputManager.Instance.SaveUserConfigurationAsync();
```

### 事件回调

```csharp
// 监听玩家加入事件
InputManager.Instance.OnPlayerJoined += (IInputService playerInput) =>
{
    Debug.Log($"玩家 {((InputService)playerInput).PlayerId} 已加入");
    // 设置玩家输入上下文等
};

// 监听配置重载事件
InputManager.Instance.OnConfigurationReloaded += () =>
{
    Debug.Log("配置已重新加载");
};

// 监听上下文切换事件
inputService.OnContextChanged += (string contextName) =>
{
    Debug.Log($"上下文切换到: {contextName}");
};
```

## 教程：按住增加进度条（松手停止/可重置）

目标：按住按钮时持续增加进度条，松手时停止（可选择是否清零）。

### 步骤 1：订阅按下状态

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

2. 在按住期间逐帧累加：

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

### 步骤 3：可选 - 要求先长按一段时间再开始

在 YAML 中设置长按时间：

```yaml
- type: Button
  action: Confirm
  longPressMs: 500
  deviceBindings:
    - "<Keyboard>/space"
    - "<Gamepad>/buttonSouth"
```

然后在代码中使用长按开始，松手停止：

```csharp
_input.GetLongPressObservable("PlayerActions", "Confirm").Subscribe(_ => StartFilling());
_input.GetPressStateObservable("PlayerActions", "Confirm").Where(p => !p).Subscribe(_ => StopFilling());
```

## 其他高级功能

### 浮点/Trigger 的长按

对于模拟量输入（如手柄扳机），可以使用阈值来定义"按下"状态：

YAML 配置：

```yaml
- type: Float
  action: FireTrigger
  longPressMs: 600
  longPressValueThreshold: 0.6 # 阈值（0-1）达到后视为按下
  deviceBindings:
    - "<Gamepad>/leftTrigger"
```

代码使用：

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
- **长按**："Long Press (ms)" 字段仅对 `Button` 和 `Float` 类型的动作有效。对于 `Float` 类型，您还可以设置 "Long Press Threshold (0-1)" 来定义模拟量的"按下"阈值。
- **Vector2 来源**：`InputBindingConstants.Vector2Sources` 类为常用的 Vector2 绑定（如 `Gamepad_LeftStick` 和 `Composite_WASD`）提供了方便的常量。
- **重置配置**：编辑器窗口提供 "Reset User to Default" 按钮，可以将用户配置重置为默认配置。

## API 概览

### IInputService

单个玩家的输入服务接口。

#### 属性

- `ReadOnlyReactiveProperty<string> ActiveContextName` - 当前活动上下文的名称
- `ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind` - 当前活动设备类型（键盘鼠标/手柄/其他）
- `event Action<string> OnContextChanged` - 上下文切换事件
- `int PlayerId` - 玩家 ID（仅 `InputService` 实现）
- `InputUser User` - Unity Input System 的用户对象（仅 `InputService` 实现）

#### 基于常量 API（推荐）

使用生成的常量，完全避免运行时字符串操作：

- `Observable<Vector2> GetVector2Observable(int actionId)`
- `Observable<Unit> GetButtonObservable(int actionId)`
- `Observable<Unit> GetLongPressObservable(int actionId)`
- `Observable<bool> GetPressStateObservable(int actionId)` - 按下状态流（true=按下，false=释放）
- `Observable<float> GetScalarObservable(int actionId)` - 标量值流（用于 Float 类型动作）

#### 基于字符串的 API（兼容模式）

- `Observable<Vector2> GetVector2Observable(string actionName)` - 使用当前上下文的 ActionMap
- `Observable<Vector2> GetVector2Observable(string actionMapName, string actionName)` - 指定 ActionMap
- `Observable<Unit> GetButtonObservable(string actionName)`
- `Observable<Unit> GetButtonObservable(string actionMapName, string actionName)`
- `Observable<Unit> GetLongPressObservable(string actionName)`
- `Observable<Unit> GetLongPressObservable(string actionMapName, string actionName)`
- `Observable<bool> GetPressStateObservable(string actionName)`
- `Observable<bool> GetPressStateObservable(string actionMapName, string actionName)`
- `Observable<float> GetScalarObservable(string actionName)`
- `Observable<float> GetScalarObservable(string actionMapName, string actionName)`

#### 上下文管理

- `void RegisterContext(InputContext context)` - 注册上下文（必须在 PushContext 之前调用）
- `void PushContext(string contextName)` - 推入新上下文到栈顶
- `void PopContext()` - 弹出栈顶上下文，恢复上一个上下文

#### 输入控制

- `void BlockInput()` - 阻塞所有输入
- `void UnblockInput()` - 恢复输入（恢复当前活动上下文的 ActionMap）

### InputManager

输入系统的单例管理器。

#### 静态属性

- `static InputManager Instance` - 单例实例
- `static bool IsListeningForPlayers` - 是否正在监听玩家加入

#### 事件

- `event Action<IInputService> OnPlayerJoined` - 玩家加入事件
- `event Action OnConfigurationReloaded` - 配置重载事件

#### 初始化

- `void Initialize(string yamlContent, string userConfigUri)` - 初始化管理器（通常由 `InputSystemLoader` 调用）

#### 玩家加入方法

- `IInputService JoinSinglePlayer(int playerIdToJoin = 0)` - 同步加入单个玩家（自动锁定设备）
- `UniTask<IInputService> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)` - 异步加入单个玩家（等待设备连接）
- `List<IInputService> JoinPlayersBatch(List<int> playerIds)` - 批量同步加入玩家
- `UniTask<List<IInputService>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)` - 批量异步加入玩家
- `IInputService JoinPlayerOnSharedDevice(int playerIdToJoin)` - 在共享设备上加入玩家
- `IInputService JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)` - 锁定特定设备给玩家

#### 大厅模式

- `void StartListeningForPlayers(bool lockDeviceOnJoin)` - 开始监听玩家加入
  - `lockDeviceOnJoin = true`: 设备锁定模式（所有设备配对给玩家 0）
  - `lockDeviceOnJoin = false`: 设备共享模式（每个设备创建新玩家）
- `void StopListeningForPlayers()` - 停止监听玩家加入

#### 配置管理

- `async UniTask<bool> ReloadConfigurationAsync()` - 重新加载配置
- `async UniTask SaveUserConfigurationAsync()` - 保存用户配置

#### 清理

- `void Dispose()` - 释放所有资源（包括所有玩家的 InputService）

### InputContext

输入上下文，包含动作绑定和命令。

#### 构造函数

- `InputContext(string name, string actionMapName)` - 创建上下文

#### 方法

- `InputContext AddBinding(Observable<Unit> source, IActionCommand command)` - 添加按钮动作绑定
- `InputContext AddBinding(Observable<Vector2> source, IMoveCommand command)` - 添加 Vector2 动作绑定
- `InputContext AddBinding(Observable<float> source, IScalarCommand command)` - 添加标量动作绑定

### 命令接口

- `IActionCommand` - 无参数命令接口
- `IMoveCommand` - Vector2 参数命令接口
- `IScalarCommand` - float 参数命令接口

### 预定义命令类

- `ActionCommand(Action action)` - 无参数命令
- `MoveCommand(Action<Vector2> action)` - Vector2 命令
- `ScalarCommand(Action<float> action)` - float 命令

### InputSystemLoader

配置加载器，处理默认配置和用户配置的加载。

#### 静态方法

- `static async Task InitializeAsync(string defaultConfigUri, string userConfigUri)` - 初始化输入系统
  - 优先加载用户配置，如果不存在则使用默认配置
  - 首次运行时自动将默认配置复制到用户配置目录

### 生成的 InputActions 类

代码生成后，您将获得：

```csharp
namespace YourGame.Input.Generated
{
    public static class InputActions
    {
        public static class ActionMaps
        {
            public static readonly int PlayerActions = ...;
            // 其他 ActionMap 常量
        }

        public static class Actions
        {
            public static readonly int Gameplay_Move = ...;
            public static readonly int Gameplay_Confirm = ...;
            // 其他动作常量（格式：ContextName_ActionName）
        }
    }
}
```

使用方式：

```csharp
using YourGame.Input.Generated;

// 使用常量获取 Observable
var moveStream = inputService.GetVector2Observable(InputActions.Actions.Gameplay_Move);
```
