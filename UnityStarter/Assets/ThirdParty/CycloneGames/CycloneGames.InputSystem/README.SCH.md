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
  private IInputPlayer _input;

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
    // 清理：InputPlayer 会在 InputManager.Dispose() 时自动清理
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
InputManager.Instance.OnPlayerJoined += (IInputPlayer playerInput) =>
{
    Debug.Log($"玩家 {((InputPlayer)playerInput).PlayerId} 已加入");
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

### IInputPlayer

单个玩家的输入服务接口。

#### 属性

- `ReadOnlyReactiveProperty<string> ActiveContextName` - 当前活动上下文的名称
- `ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind` - 当前活动设备类型（键盘鼠标/手柄/其他）
- `event Action<string> OnContextChanged` - 上下文切换事件
- `int PlayerId` - 玩家 ID（仅 `InputPlayer` 实现）
- `InputUser User` - Unity Input System 的用户对象（仅 `InputPlayer` 实现）

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

- `event Action<IInputPlayer> OnPlayerJoined` - 玩家加入事件
- `event Action OnConfigurationReloaded` - 配置重载事件

#### 初始化

- `void Initialize(string yamlContent, string userConfigUri)` - 初始化管理器（通常由 `InputSystemLoader` 调用）

#### 玩家加入方法

- `IInputPlayer JoinSinglePlayer(int playerIdToJoin = 0)` - 同步加入单个玩家（自动锁定设备）
- `UniTask<IInputPlayer> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)` - 异步加入单个玩家（等待设备连接）
- `List<IInputPlayer> JoinPlayersBatch(List<int> playerIds)` - 批量同步加入玩家
- `UniTask<List<IInputPlayer>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)` - 批量异步加入玩家
- `IInputPlayer JoinPlayerOnSharedDevice(int playerIdToJoin)` - 在共享设备上加入玩家
- `IInputPlayer JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)` - 锁定特定设备给玩家

#### 大厅模式

- `void StartListeningForPlayers(bool lockDeviceOnJoin)` - 开始监听玩家加入
  - `lockDeviceOnJoin = true`: 设备锁定模式（所有设备配对给玩家 0）
  - `lockDeviceOnJoin = false`: 设备共享模式（每个设备创建新玩家）
- `void StopListeningForPlayers()` - 停止监听玩家加入

#### 配置管理

- `async UniTask<bool> ReloadConfigurationAsync()` - 重新加载配置
- `async UniTask SaveUserConfigurationAsync()` - 保存用户配置

#### 清理

- `void Dispose()` - 释放所有资源（包括所有玩家的 InputPlayer）

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

## 依赖注入 (VContainer) 集成

### 安装

包中包含 VContainer 安装器。在您的 DI 容器设置中注册它：

#### 选项 1：基于 URI 的加载（StreamingAssets/PersistentData）

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 使用 URI 方式加载配置
        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultConfigFileName: "input_config.yaml",
            userConfigFileName: "user_input_settings.yaml",
            postInitCallback: async resolver =>
            {
                //  ...
                var inputResolver = resolver.Resolve<IInputPlayerResolver>();
                var player0Input = inputResolver.GetInputPlayer(0);
            }
        );
        inputSystemInstaller.Install(builder);

        // 注册依赖输入的 game systems
        builder.Register<PlayerController>(Lifetime.Scoped);
    }
}
```

#### 选项 2：AssetManagement 加载（YooAsset/Addressables）

如果您使用 `CycloneGames.AssetManagement` 配合 YooAsset 或 Addressables：

**重要**：用户配置**始终**从 `PersistentData` 路径自动加载。您只需要提供默认配置的加载器。

> **关于配置加载方式的说明：**
>
> - **TextAsset**（推荐用于 Addressables/Resources）：将 YAML 作为 Unity TextAsset 加载。适用于所有 provider（YooAsset、Addressables、Resources）。您的 YAML 文件应在 Unity 中作为 TextAsset 导入。
> - **RawFile**（仅 YooAsset）：将 YAML 作为原始文件加载。仅适用于 YooAsset provider。对 YooAsset 更高效，但 Addressables/Resources 不支持。
>
> 默认情况下，helper 会先尝试 RawFile（针对 YooAsset），然后回退到 TextAsset（针对 Addressables/Resources）。您也可以显式指定 `useTextAsset: true` 来强制使用 TextAsset 加载。

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using CycloneGames.AssetManagement.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 先安装 AssetManagement
        var assetManagementInstaller = new AssetManagementVContainerInstaller();
        assetManagementInstaller.Install(builder);

        // 从 AssetManagement 创建默认配置加载器
        // 直接从您的 AssetManagement 设置获取 package（不从 resolver 获取）
        var package = assetModule.GetPackage("DefaultPackage"); // 从您的设置中获取
        var defaultLoader = InputSystemAssetManagementHelper.CreateDefaultConfigLoader(
            package: package,
            defaultConfigLocation: "input_config.yaml",
            useTextAsset: false  // false = 先尝试 RawFile，失败则回退到 TextAsset
        );

        // 安装 InputSystem
        // 用户配置将自动从 PersistentData/user_input_settings.yaml 加载
        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultLoader,
            userConfigFileName: "user_input_settings.yaml", // 可选：指定用户配置文件名
            postInitCallback: async resolver =>
            {
                var inputResolver = resolver.Resolve<IInputPlayerResolver>();
                var player0Input = inputResolver.GetInputPlayer(0);
                // 设置上下文等
            }
        );
        inputSystemInstaller.Install(builder);

        builder.Register<PlayerController>(Lifetime.Scoped);
    }
}
```

**工作原理：**

1. 首先尝试从 `PersistentData/user_input_settings.yaml` 加载用户配置（如果指定了子目录路径，例如 `ConfigFolder/user_input_settings.yaml`，也会正确加载）
2. 如果未找到，从 AssetManagement（或使用选项 1 时从 StreamingAssets）加载默认配置
3. 如果加载了默认配置，自动将其保存到 `PersistentData/user_input_settings.yaml`（或子目录）供以后使用
4. 所有后续的用户配置保存/加载都使用 `PersistentData` 路径

**路径支持：**

- **用户配置**：支持子目录（例如 `ConfigFolder/user_input_settings.yaml` 将保存到 `PersistentData/ConfigFolder/user_input_settings.yaml`）。如果目录不存在，会自动创建。
- **默认配置（StreamingAssets）**：支持子目录（例如 `Config/input_config.yaml` 将从 `StreamingAssets/Config/input_config.yaml` 加载）。
- **默认配置（AssetManagement）**：使用 AssetManagement 包中定义的位置（例如 `Assets/Config/input_config.yaml` 或仅 `input_config.yaml`）。

#### 选项 3：自定义默认配置加载器

完全控制默认配置的加载（例如，从数据库、网络等）：

```csharp
var inputSystemInstaller = new InputSystemVContainerInstaller(
    defaultLoader: async resolver =>
    {
        // 您的自定义加载逻辑
        // 例如：从数据库、网络等加载
        return await LoadConfigFromCustomSource();
    },
    userConfigFileName: "user_input_settings.yaml" // 用户配置始终从 PersistentData
);
inputSystemInstaller.Install(builder);
```

**注意**：用户配置始终在 `PersistentData` 路径中管理，因为：

- 需要可写权限以保存用户自定义设置
- 在应用更新后仍然持久化
- 与默认配置分离，默认配置可能在只读位置（AssetManagement、StreamingAssets）

#### 选项 4：延迟初始化（热更新场景）

适用于热更新游戏，AssetManagement 包在注册时可能尚未准备好：

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 先安装 AssetManagement
        var assetManagementInstaller = new AssetManagementVContainerInstaller();
        assetManagementInstaller.Install(builder);

        // 安装 InputSystem，延迟初始化
        // 设置 autoInitialize: false 以延迟初始化，直到包准备好
        // 从您的 AssetManagement 设置获取 package（不从 resolver 获取）
        var package = assetModule.GetPackage("DefaultPackage"); // 从您的设置中获取
        var defaultLoader = InputSystemAssetManagementHelper.CreateDefaultConfigLoader(
            package: package,
            defaultConfigLocation: "Assets/Config/input_config.yaml"
        );

        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultConfigLoader: defaultLoader,
            userConfigFileName: "user_input_settings.yaml",
            autoInitialize: false // 延迟初始化
        );
        inputSystemInstaller.Install(builder);

        builder.Register<PlayerController>(Lifetime.Scoped);
    }

    protected override async UniTaskVoid Start()
    {
        // 等待 AssetManagement 包准备好
        var assetModule = Container.Resolve<IAssetModule>();
        await assetModule.InitializeAsync();

        var defaultPackage = assetModule.CreatePackage("DefaultPackage");
        await defaultPackage.InitializeAsync(/* ... */);

        // 现在手动初始化 InputSystem
        var initializer = Container.Resolve<IInputSystemInitializer>();
        await initializer.InitializeAsync(Container);

        // 设置玩家等
    }
}
```

**热更新后更新配置：**

```csharp
public class HotUpdateHandler
{
    private readonly IInputSystemInitializer _inputInitializer;
    private readonly IAssetPackage _package;  // 直接注入 package，而不是 IAssetModule

    [Inject]
    public HotUpdateHandler(IInputSystemInitializer inputInitializer, IAssetPackage package)
    {
        _inputInitializer = inputInitializer;
        _package = package;  // Package 应该在 DI 容器中注册
    }

    public async UniTask OnHotUpdateComplete()
    {
        // 热更新后，从更新的 AssetManagement 包重新加载配置
        // 选项 1：使用 ReinitializeFromPackageAsync（推荐）
        await _inputInitializer.ReinitializeFromPackageAsync(
            _package,
            "Assets/Config/input_config.yaml",
            saveToUserConfig: true
        );

        // 选项 2：手动加载并更新
        // var loader = InputSystemAssetManagementHelper.CreateConfigLoader(
        //     package,
        //     "Assets/Config/input_config.yaml"
        // );
        // string newConfig = await loader();
        // if (!string.IsNullOrEmpty(newConfig))
        // {
        //     await _inputInitializer.UpdateConfigurationAsync(newConfig, saveToUserConfig: true);
        // }
    }
}
```

**重新加载用户配置（玩家改键）：**

```csharp
public class SettingsMenu
{
    private readonly IInputSystemInitializer _inputInitializer;

    [Inject]
    public SettingsMenu(IInputSystemInitializer inputInitializer)
    {
        _inputInitializer = inputInitializer;
    }

    public async UniTask OnPlayerSavedKeyBindings()
    {
        // 玩家已修改并保存按键绑定
        // 重新加载更新后的用户配置
        await _inputInitializer.ReloadUserConfigurationAsync();
    }
}
```

**跨场景/跨 Resolver 使用：**

您可以从任何 resolver（父作用域或子作用域）解析 `IInputSystemInitializer` 来重新加载配置：

```csharp
// 在任何场景或 LifetimeScope 中
public class SomeOtherScene : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // ... 其他注册
    }

    protected override async UniTaskVoid Start()
    {
        // 从父作用域解析 initializer
        var initializer = Parent.Container.Resolve<IInputSystemInitializer>();

        // 从您的设置获取 package（应该在 DI 中注册或从您的 AssetManagement 设置获取）
        var package = Parent.Container.Resolve<IAssetPackage>(); // 或从您的 AssetManagement 设置获取
        await initializer.ReinitializeFromPackageAsync(package, "Assets/Config/input_config.yaml");
    }
}
```

### 使用模式

#### 模式 1：注入 IInputPlayerResolver（推荐）

需要时使用解析器获取输入服务：

```csharp
using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using VContainer;

public class PlayerController
{
    private readonly IInputPlayerResolver _inputResolver;
    private IInputPlayer _input;

    [Inject]
    public PlayerController(IInputPlayerResolver inputResolver)
    {
        _inputResolver = inputResolver;
    }

    public void Initialize(int playerId)
    {
        _input = _inputResolver.GetInputPlayer(playerId);

        var ctx = new InputContext("Gameplay", "PlayerActions")
            .AddBinding(_input.GetVector2Observable("Move"), new MoveCommand(OnMove))
            .AddBinding(_input.GetButtonObservable("Confirm"), new ActionCommand(OnConfirm));

        _input.RegisterContext(ctx);
        _input.PushContext("Gameplay");
    }

    private void OnMove(Vector2 dir) { /* ... */ }
    private void OnConfirm() { /* ... */ }
}
```

#### 模式 2：直接注入 InputManager

需要完全控制的场景：

```csharp
using CycloneGames.InputSystem.Runtime;
using VContainer;

public class GameSession
{
    private readonly InputManager _inputManager;

    [Inject]
    public GameSession(InputManager inputManager)
    {
        _inputManager = inputManager;
    }

    public async UniTask StartMultiplayerLobby()
    {
        _inputManager.OnPlayerJoined += OnPlayerJoined;
        _inputManager.StartListeningForPlayers(false); // 设备共享模式
    }

    private void OnPlayerJoined(IInputPlayer service)
    {
        // 设置玩家特定的输入上下文
        var ctx = new InputContext("Gameplay", "PlayerActions")
            .AddBinding(service.GetVector2Observable("Move"), new MoveCommand(OnMove));
        service.RegisterContext(ctx);
        service.PushContext("Gameplay");
    }
}
```

#### 模式 3：带玩家 ID 的工厂方法

创建按玩家 ID 解析输入服务的工厂：

```csharp
using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using VContainer;

public class PlayerFactory
{
    private readonly IInputPlayerResolver _inputResolver;
    private readonly IObjectResolver _resolver;

    [Inject]
    public PlayerFactory(IInputPlayerResolver inputResolver, IObjectResolver resolver)
    {
        _inputResolver = inputResolver;
        _resolver = resolver;
    }

    public PlayerController CreatePlayer(int playerId)
    {
        var inputService = _inputResolver.GetInputPlayer(playerId);
        var controller = _resolver.Resolve<PlayerController>();
        controller.Initialize(inputService, playerId);
        return controller;
    }
}
```

### 完整示例：VContainer 集成

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;
using YourGame.Input.Generated;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 安装 InputSystem
        builder.Install(new InputSystemVContainerInstaller());

        // 注册游戏系统
        builder.Register<PlayerController>(Lifetime.Scoped);
        builder.Register<GameSession>(Lifetime.Singleton);
    }

    protected override async UniTaskVoid Start()
    {
        // InputSystem 由安装器自动初始化
        // 现在可以使用了
        var resolver = Container.Resolve<IInputPlayerResolver>();
        var inputService = resolver.GetInputPlayer(0);

        // 设置输入上下文
        var ctx = new InputContext("Gameplay", "PlayerActions")
            .AddBinding(
                inputService.GetVector2Observable(InputActions.Actions.Gameplay_Move),
                new MoveCommand(OnMove)
            )
            .AddBinding(
                inputService.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
                new ActionCommand(OnConfirm)
            );

        inputService.RegisterContext(ctx);
        inputService.PushContext("Gameplay");
    }

    private void OnMove(Vector2 dir) { /* ... */ }
    private void OnConfirm() { /* ... */ }
}

// 示例：注入输入的 PlayerController
public class PlayerController
{
    private readonly IInputPlayerResolver _inputResolver;
    private IInputPlayer _input;

    [Inject]
    public PlayerController(IInputPlayerResolver inputResolver)
    {
        _inputResolver = inputResolver;
    }

    public void Initialize(int playerId)
    {
        _input = _inputResolver.GetInputPlayer(playerId);
        // 设置上下文...
    }
}
```
