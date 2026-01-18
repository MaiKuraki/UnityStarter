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

## 实战示例
<img src="./Documents~/Input_IntegrateSample.gif" alt="Input integrate preview" style="width: 100%; height: auto; max-width: 854px;" />

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

生成的文件将包含：

- `InputActions.Contexts.*` - 上下文名称的字符串常量（例如 `InputActions.Contexts.Gameplay`）
- `InputActions.ActionMaps.*` - 动作映射名称的字符串常量（例如 `InputActions.ActionMaps.PlayerActions`）
- `InputActions.Actions.*` - 动作的整型哈希 ID（例如 `InputActions.Actions.Gameplay_Move`）

这些常量支持类型安全、零 GC 的运行时输入访问。

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
// 确保引入了 R3 命名空间
using R3;
// 确保引入了您自定义的命名空间
using YourGame.Input.Generated;
using CycloneGames.InputSystem.Runtime;

var svc = InputManager.Instance.JoinSinglePlayer(0);

// 创建 Context（name 参数可选，默认为 actionMapName）
// 方式 1：仅传入 ActionMap（name 将自动使用 "PlayerActions"）
var ctx = new InputContext(InputActions.ActionMaps.PlayerActions)
  .AddBinding(svc.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(() => {/*...*/}));

// 方式 2：ActionMap + 自定义名称（用于调试）
// var ctx = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
//   .AddBinding(...);

// 绑定生命周期到当前组件 (this) - 当组件销毁时自动从输入栈中移除 Context
// 注意：AddTo 返回的是 CancellationTokenRegistration，所以要单独调用，不要赋回给 ctx
ctx.AddTo(this);

// 直接将 Context 对象推入栈
svc.PushContext(ctx);
```

**使用字符串 API（兼容模式）：**

```csharp
var svc = InputManager.Instance.JoinSinglePlayer(0);
// 仅需传入 ActionMap（name 默认为 "PlayerActions"）
var ctx = new InputContext("PlayerActions")
  .AddBinding(svc.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(dir => {/*...*/}))
  .AddBinding(svc.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(() => {/*...*/}));

// 或使用自定义名称用于调试：
// var ctx = new InputContext("PlayerActions", "Gameplay")...

svc.PushContext(ctx);
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
  private InputContext _context;

  private void Start()
  {
    // 加入玩家0并创建游戏上下文
    _input = InputManager.Instance.JoinSinglePlayer(0);
    // 仅需传入 ActionMap（name 默认为 "PlayerActions"）
    _context = new InputContext("PlayerActions")
      .AddBinding(_input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
      .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirm))
      // 可选：长按（需要在 YAML 中为 "Confirm" 设置 longPressMs）
      .AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirmLongPress));

    // 绑定生命周期到当前组件（需要引入 R3 命名空间）
    // using R3;
    _context.AddTo(this);

    _input.PushContext(_context);
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

  // 如果使用了 AddTo(this)，就不再需要 OnDestroy 手动清理了！
  /*
  private void OnDestroy()
  {
    if (_input != null && _context != null)
    {
        _input.RemoveContext(_context);
    }
  }
  */
}
```

## Context 管理机制（对象引用）

`InputSystem` 使用 **Context 对象引用** 来管理输入栈作为唯一标识。

### 核心特性

1.  **独立实例**：每次 `new InputContext(...)` 都会创建一个独立的对象。即使它们的名称相同（如 "Gameplay"），它们也是不同的实例。
2.  **栈管理**：`PushContext(ctx)` 将对象推入栈。栈顶的 Context 为活动状态。
3.  **自动置顶**：如果 `PushContext` 一个已经在栈中的 Context 对象，它会自动移动到栈顶（聚焦行为）。
4.  **精确移除**：`RemoveContext(ctx)` 只移除指定的对象实例，不会误删其他同名的 Context。

### 示例：UI 叠加场景

当 UI B 叠加在 UI A 上时，通过 Context Stack 管理输入优先级：

```csharp
using R3; // 需要引入 R3 命名空间以使用 AddTo 扩展

// UI A
public class UIWindowA : MonoBehaviour
{
    private InputContext _context;

    private void OnEnable()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        // ActionMap 必需，name 可选（默认为 "UIActions"）
        _context = new InputContext("UIActions", "UI")
            .AddBinding(..., new ActionCommand(OnConfirmA));

        // 将生命周期绑定到此组件 - 当组件禁用/销毁时自动移除
        _context.AddTo(this);

        input.PushContext(_context); // 栈：[A]
    }

    // OnDisable 不再需要 - AddTo(this) 会自动处理清理！
}

// UI B (叠加在 A 上)
public class UIWindowB : MonoBehaviour
{
    private InputContext _context;

    private void OnEnable()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        // 名称相同也没关系，是不同实例
        _context = new InputContext("UIActions", "UI")
            .AddBinding(..., new ActionCommand(OnConfirmB));

        // 将生命周期绑定到此组件
        _context.AddTo(this);

        input.PushContext(_context); // 栈：[A, B]。B 在栈顶，A 被暂停。
    }

    // OnDisable 不再需要 - AddTo(this) 会自动处理清理！
    // 当 B 被销毁时，它会自动从栈中移除，A 自动恢复。
}
```

## 多个 Context 共享相同的 ActionMap 名称

```
不同的 Context 能否使用相同的 ActionMap 名称（例如，都使用 "PlayerActions"）？
可以，这是安全且完全支持的！
```

每个 Context 的绑定都独立存储在 Context 对象中。当在共享相同 ActionMap 名称的 Context 之间切换时：

1. ActionMap 会正确启用/禁用（Unity Input System 安全处理）
2. 只有**栈顶 Context 的绑定**会被订阅并生效
3. 其他 Context 的绑定会自动暂停（它们的订阅会被 Dispose）

### 示例：游戏和暂停菜单

两个 Context 都使用 "PlayerActions"，但绑定不同的命令：

```csharp
using R3;

// 游戏 Context - 绑定移动和战斗
public class GameplayController : MonoBehaviour
{
    private InputContext _gameplayContext;

    private void Start()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        _gameplayContext = new InputContext("PlayerActions", "Gameplay")
            .AddBinding(input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
            .AddBinding(input.GetButtonObservable("PlayerActions", "Jump"), new ActionCommand(OnJump))
            .AddBinding(input.GetButtonObservable("PlayerActions", "Attack"), new ActionCommand(OnAttack));

        _gameplayContext.AddTo(this);
        input.PushContext(_gameplayContext); // 栈：[Gameplay]
    }
}

// 暂停菜单 Context - 也使用 "PlayerActions"，但只绑定菜单导航
public class PauseMenu : MonoBehaviour
{
    private InputContext _pauseContext;

    private void OnEnable()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        // 相同的 ActionMap 名称 "PlayerActions"，但绑定不同
        _pauseContext = new InputContext("PlayerActions", "PauseMenu")
            .AddBinding(input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMenuNavigate)) // 菜单导航
            .AddBinding(input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnMenuConfirm))
            .AddBinding(input.GetButtonObservable("PlayerActions", "Cancel"), new ActionCommand(OnMenuCancel));

        _pauseContext.AddTo(this);
        input.PushContext(_pauseContext); // 栈：[Gameplay, PauseMenu]。只有 PauseMenu 的绑定生效。
    }

    // 当 PauseMenu 被销毁时，Gameplay Context 会自动恢复
}
```

**关键点**：

- ✅ 多个 Context 可以安全地共享相同的 ActionMap 名称
- ✅ 每个 Context 的绑定是独立的，存储在 Context 对象中
- ✅ 切换 Context 时，只会激活栈顶 Context 的绑定
- ✅ Context 之间不会产生冲突或干扰

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
var ctxInspect = new InputContext("PlayerActions", "Inspect");
ctxInspect.AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnInspectConfirm));

// Charge 上下文：只绑定长按
var ctxCharge = new InputContext("PlayerActions", "Charge");
ctxCharge.AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnChargeConfirm));

// 根据逻辑切换上下文
_input.PushContext(ctxInspect); // 切换到 Inspect
// 需要时切换：
_input.PushContext(ctxCharge); // 切换到 Charge
```

## 长按进度条（UI 进度显示）

如果需要在长按期间显示进度条，可以使用 `GetLongPressProgressObservable()` 来获取持续的进度值（0~1）：

**使用生成的常量（推荐，ZeroGC）：**

```csharp
using YourGame.Input.Generated; // 引入生成的常量

// 将长按进度绑定到 UI 进度条
var ctx = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
    .AddBinding(
        _input.GetLongPressProgressObservable(InputActions.Actions.Gameplay_Confirm),
        new ScalarCommand(progress =>
        {
            if (progress < 0)
            {
                // 取消（在完成前松开）
                progressBar.gameObject.SetActive(false);
            }
            else
            {
                // 显示并更新进度条 (0~1)
                progressBar.gameObject.SetActive(true);
                progressBar.fillAmount = progress;
                
                if (progress >= 1f)
                {
                    // 长按完成！
                    OnLongPressComplete();
                }
            }
        }));
```

**使用字符串 API：**

```csharp
_input.GetLongPressProgressObservable("PlayerActions", "Confirm")
```

**进度值说明：**

| 值 | 含义 |
|----|------|
| `0~1` | 长按进度 (0% → 100%) |
| `1.0` | 长按完成（**只触发一次**） |
| `-1`  | 取消（在完成前松开） |

## 高级用法

### 多人游戏模式

#### 单人模式（自动锁定设备）

```csharp
// 自动加入玩家0，并将所有必需设备锁定给该玩家
var svc = InputManager.Instance.JoinSinglePlayer(0);

// 如果玩家已经加入，JoinSinglePlayer 会返回现有的服务
// 这允许您安全地多次调用它
var svc2 = InputManager.Instance.JoinSinglePlayer(0); // 返回相同的服务，不会触发事件
```

**注意**：如果玩家已经加入，`JoinSinglePlayer` 会返回现有服务而不会触发 `OnPlayerInputReady` 事件。这在需要多次获取服务时很有用，但如果您需要在玩家已经加入后重新绑定输入上下文，请使用 `RefreshPlayerInput`。

#### 大厅模式（设备锁定）

第一个设备加入成为玩家 0，后续设备自动配对给该玩家，适合单人玩家在键鼠和手柄间切换：

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
InputManager.Instance.StartListeningForPlayers(true); // true = 设备锁定模式
```

#### 大厅模式（设备共享）

每个新设备加入都会创建一个新玩家，适合本地多人合作：

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
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

### 重置为默认配置

将用户配置重置为默认配置（跨平台兼容：Windows、macOS、Linux、Android、iOS、WebGL）：

```csharp
using CycloneGames.Utility.Runtime;
using CycloneGames.InputSystem.Runtime;

var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);

bool success = await InputSystemLoader.ResetToDefaultAsync(defaultUri, userUri);
if (success)
{
    Debug.Log("配置已重置为默认值");
}
```

### 事件回调

```csharp
// 监听玩家输入就绪事件
InputManager.Instance.OnPlayerInputReady += (IInputPlayer playerInput) =>
{
    Debug.Log($"玩家 {((InputPlayer)playerInput).PlayerId} 输入已就绪");
    // 设置玩家输入上下文等
};

// 监听配置重载事件
InputManager.Instance.OnConfigurationReloaded += () =>
{
    Debug.Log("配置已重新加载");
};

// 通过触发已加入玩家的 OnPlayerInputReady 事件来刷新玩家输入
// 当您在玩家已经加入后动态绑定输入上下文时很有用（例如，在不同场景中）
// 示例：LaunchScene 初始化输入系统，GameplayScene 绑定输入上下文
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
// ... 稍后，在 GameplayScene 中绑定上下文后 ...
if (InputManager.Instance.GetInputPlayer(0) != null)
{
    InputManager.Instance.RefreshPlayerInput(0); // 触发 OnPlayerInputReady 事件以激活新绑定的上下文
}

// 监听上下文切换事件
inputService.OnContextChanged += (string contextName) =>
{
    Debug.Log($"上下文切换到: {contextName}");
};
```

### 输入阻塞

临时禁用所有输入（例如，显示暂停菜单时）：

```csharp
// 阻塞输入
inputService.BlockInput();

// 恢复输入
inputService.UnblockInput();
```

### 上下文栈管理

```csharp
using R3; // 需要引入 R3 命名空间以使用 AddTo 扩展

// 仅需传入 ActionMap（name 默认为 "UIActions"）
var menuContext = new InputContext("UIActions", "Menu");

// 推入新上下文（例如，打开菜单）
inputService.PushContext(menuContext);

// ⚠️ 警告：如果您使用了 AddTo(this) 进行生命周期绑定，请不要使用 PopContext。
// PopContext 会无脑移除栈顶，如果栈顺序发生变化（如动态 UI），可能会移除错误的上下文。
// 请使用 RemoveContext(context) 或 AddTo(this) 代替。
// inputService.PopContext(); // 使用生命周期绑定时不推荐

// 推荐：通过对象引用移除特定上下文
inputService.RemoveContext(menuContext);

// 或者将生命周期绑定到组件（组件销毁时自动移除）
// menuContext.AddTo(this);

// 查看当前活动上下文
string currentContext = inputService.ActiveContextName.CurrentValue;

// 订阅上下文变化
inputService.ActiveContextName.Subscribe(ctxName =>
{
    Debug.Log($"当前上下文: {ctxName}");
});
```

### 先创建后绑定模式（推荐用于游戏初始化）

在游戏初始化时，可以先创建 Context，然后在需要时动态添加绑定和 Push。这种方式适合需要延迟绑定或动态管理的场景：

```csharp
// 游戏初始化时 - 创建 Context
// 仅需传入 ActionMap（name 默认为 ActionMapName）
var gameplayContext = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay);
var uiContext = new InputContext(InputActions.ActionMaps.UIActions, InputActions.Contexts.UI);
var inputPlayer = inputManager.GetInputPlayer(0);

// 此时不需要 Register，Context 是独立对象

// 稍后，在需要时动态添加绑定
gameplayContext.AddBinding(
    inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move),
    new MoveCommand(OnMove)
);
gameplayContext.AddBinding(
    inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Jump),
    new ActionCommand(OnJump)
);

// 如果这是在一个 MonoBehaviour 中，可以绑定生命周期
// gameplayContext.AddTo(this);

// 激活 Context（此时绑定已经添加，会立即生效）
inputPlayer.PushContext(gameplayContext);

// 或者，如果 Context 已经激活，添加绑定后需要刷新
if (inputPlayer.ActiveContextName.Value == InputActions.Contexts.Gameplay)
{
    // ... 添加更多绑定 ...
    inputPlayer.RefreshActiveContext(); // 使新绑定生效
}
```

**重要提示：**

- ✅ Context 是独立对象，无需预注册
- ✅ 如果 Context 还未激活（未 Push），添加绑定后直接 Push 即可
- ⚠️ 如果 Context 已经激活（已 Push），添加绑定后需要调用 `RefreshActiveContext()` 来使新绑定生效

### 细粒度绑定管理

当您在游戏场景中动态向上下文添加绑定时，可以移除特定绑定而不影响整个上下文。

```csharp
using R3; // 需要引入 R3 命名空间以使用 AddTo 扩展

// 在游戏场景中
var gameplayContext = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
    .AddBinding(inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(OnMove));

// 将生命周期绑定到此组件（组件销毁时自动移除上下文）
gameplayContext.AddTo(this);

inputPlayer.PushContext(gameplayContext);

// 稍后，添加动态绑定
var jumpObservable = inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Jump);
var jumpCommand = new ActionCommand(OnJump);
gameplayContext.AddBinding(jumpObservable, jumpCommand);

// 重要：添加绑定后，如果该上下文当前是活动的，需要刷新以使其生效
inputPlayer.RefreshActiveContext();

// 离开游戏场景时 - 如果使用了 AddTo(this)，则不需要 OnDestroy！
// 上下文会在组件销毁时自动移除。

// 但是，如果您需要在组件销毁前移除特定绑定：
// inputPlayer.RemoveBindingFromContext(gameplayContext, jumpObservable);
```

**重要提示：**

- `RemoveBindingFromContext` 允许您从上下文中移除特定绑定，而不影响其他绑定
- `RemoveContext` 移除整个上下文及其所有绑定，**并自动从栈中移除该上下文**（如果它在栈中）

### InputContext 生命周期管理

`InputContext` 是普通的 C# 类，**可以多次创建**。您可以根据项目需求选择不同的使用模式：

#### 模式 1：共享 Context 实例（推荐用于静态绑定）

如果多个场景使用相同的绑定配置，可以创建一个共享的 Context 实例：

```csharp
using R3; // 需要引入 R3 命名空间以使用 AddTo 扩展

// 在全局管理器或单例中创建
public class InputContextManager
{
    private static InputContext _sharedGameplayContext;

    public static InputContext GetGameplayContext(IInputPlayer inputPlayer)
    {
        if (_sharedGameplayContext == null)
        {
            _sharedGameplayContext = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
                .AddBinding(inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(OnMove))
                .AddBinding(inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(OnConfirm));
        }
        return _sharedGameplayContext;
    }
}

// 在场景 A 中使用
public class SceneA : MonoBehaviour
{
    private void Start()
    {
        var inputPlayer = InputManager.Instance.GetInputPlayer(0);
        var ctx = InputContextManager.GetGameplayContext(inputPlayer);

        // 对于共享的 Context，您可能需要手动管理移除
        // 或者绑定到一个持久存在的 GameObject（生命周期超过场景切换）
        inputPlayer.PushContext(ctx);
    }

    private void OnDestroy()
    {
        // 对于共享的 Context，在场景卸载时手动移除
        var inputPlayer = InputManager.Instance.GetInputPlayer(0);
        inputPlayer.RemoveContext(InputContextManager.GetGameplayContext(inputPlayer));
    }
}
```

#### 模式 2：每个场景独立的 Context 实例（推荐用于动态绑定）

如果每个场景需要不同的绑定配置，每个场景创建自己的 Context 实例：

```csharp
using R3; // 需要引入 R3 命名空间以使用 AddTo 扩展

// 场景 A - 创建自己的 Context
public class SceneA : MonoBehaviour
{
    private InputContext _gameplayContext;

    private void Start()
    {
        var inputPlayer = InputManager.Instance.GetInputPlayer(0);
        _gameplayContext = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
            .AddBinding(inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(OnMoveA))
            .AddBinding(inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Jump), new ActionCommand(OnJumpA));

        // 将生命周期绑定到此组件 - 场景卸载时自动移除
        _gameplayContext.AddTo(this);

        inputPlayer.PushContext(_gameplayContext);
    }

    // OnDestroy 不再需要 - AddTo(this) 会自动处理清理！
}
```

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
- `Observable<float> GetLongPressProgressObservable(int actionId)` - 持续进度流（0~1），松开取消时发送-1。适用于进度条显示。
- `Observable<bool> GetPressStateObservable(int actionId)` - 按下状态流（true=按下，false=释放）
- `Observable<float> GetScalarObservable(int actionId)` - 标量值流（用于 Float 类型动作）

#### 上下文管理

- `void PushContext(InputContext context)` - 将上下文推入栈顶（如果已在栈中，则移动到栈顶）
- `void PopContext()` - ⚠️ **使用生命周期绑定（`AddTo`）时不推荐**。PopContext 会无脑移除栈顶元素。如果栈顺序发生变化（如动态 UI），可能会移除错误的上下文。请使用 `RemoveContext(context)` 或 `AddTo(this)` 代替。
- `bool RemoveContext(InputContext context)` - 通过对象引用从栈中移除指定上下文
- `void RefreshActiveContext()` - 刷新当前活动的上下文，重新订阅所有绑定

#### 绑定管理

- `bool RemoveBindingFromContext(InputContext context, Observable<Unit> source)` - 移除绑定
- `bool RemoveBindingFromContext(InputContext context, Observable<Vector2> source)`
- `bool RemoveBindingFromContext(InputContext context, Observable<float> source)`

#### 输入控制

- `void BlockInput()` - 阻塞所有输入
- `void UnblockInput()` - 恢复输入（恢复当前活动上下文的 ActionMap）

### InputManager

输入系统的单例管理器。

#### 静态属性

- `static InputManager Instance` - 单例实例
- `static bool IsListeningForPlayers` - 是否正在监听玩家加入

#### 事件

- `event Action<IInputPlayer> OnPlayerInputReady` - 玩家输入就绪事件（在玩家加入或输入刷新时触发）
- `event Action OnConfigurationReloaded` - 配置重载事件

#### 初始化

- `void Initialize(string yamlContent, string userConfigUri)` - 初始化管理器（通常由 `InputSystemLoader` 调用）

#### 玩家加入方法

- `IInputPlayer JoinSinglePlayer(int playerIdToJoin = 0)` - 同步加入单个玩家（自动锁定设备）。如果玩家已经加入，返回现有服务而不会触发 `OnPlayerInputReady` 事件。
- `UniTask<IInputPlayer> JoinSinglePlayerAsync(int playerIdToJoin = 0, int timeoutInSeconds = 5)` - 异步加入单个玩家（等待设备连接）
- `List<IInputPlayer> JoinPlayersBatch(List<int> playerIds)` - 批量同步加入玩家
- `UniTask<List<IInputPlayer>> JoinPlayersBatchAsync(List<int> playerIds, int timeoutPerPlayerInSeconds = 5)` - 批量异步加入玩家
- `IInputPlayer JoinPlayerOnSharedDevice(int playerIdToJoin)` - 在共享设备上加入玩家
- `IInputPlayer JoinPlayerAndLockDevice(int playerIdToJoin, InputDevice deviceToLock)` - 锁定特定设备给玩家
- `IInputPlayer GetInputPlayer(int playerId)` - 获取指定玩家 ID 的输入玩家，如果未加入则返回 null
- `bool RefreshPlayerInput(int playerId)` - 通过触发已加入玩家的 `OnPlayerInputReady` 事件来刷新玩家输入。

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

输入上下文，包含动作绑定和命令。实现 `IDisposable` 接口以支持自动生命周期管理。

#### 构造函数

- `InputContext(string actionMapName, string name = null)` - 创建上下文
  - `actionMapName`（必需）：Unity Input System 的 ActionMap 名称（功能必需）
  - `name`（可选）：用于调试的显示名称。如果为 null，默认使用 `actionMapName`
  - **推荐**：使用生成的常量：`new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)`
  - **简化用法**：`new InputContext(InputActions.ActionMaps.PlayerActions)` - name 将自动使用 "PlayerActions"

#### 方法

- `InputContext AddBinding(Observable<Unit> source, IActionCommand command)` - 添加按钮动作绑定
- `InputContext AddBinding(Observable<Vector2> source, IMoveCommand command)` - 添加 Vector2 动作绑定
- `InputContext AddBinding(Observable<float> source, IScalarCommand command)` - 添加标量动作绑定
- `bool RemoveBinding(Observable<Unit> source)` - 移除按钮动作绑定
- `bool RemoveBinding(Observable<Vector2> source)` - 移除 Vector2 动作绑定
- `bool RemoveBinding(Observable<float> source)` - 移除标量动作绑定

#### 生命周期管理

- `void Dispose()` - 自动从所有活动的 InputPlayer 中移除此上下文。设计为通过 R3 的 `AddTo(this)` 扩展方法调用。

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

        // 仅需传入 ActionMap（name 默认为 "PlayerActions"）
        var ctx = new InputContext("PlayerActions", "Gameplay")
            .AddBinding(_input.GetVector2Observable("Move"), new MoveCommand(OnMove))
            .AddBinding(_input.GetButtonObservable("Confirm"), new ActionCommand(OnConfirm));

        _input.PushContext(ctx);
    }

    private void OnMove(Vector2 dir) { /* ... */ }
    private void OnConfirm() { /* ... */ }
}
```
