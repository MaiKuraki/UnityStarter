# CycloneGames.InputSystem

> 注意：CycloneGames.InputSystem 代码由项目开发者编写，本文档由 AI 辅助编写

基于 Unity Input System 的生产级响应式输入封装，支持上下文栈、多人设备配对、YAML 驱动配置，以及可选的录制回放、手势识别和反作弊时序验证工具。

[English](./README.md) | 简体中文

## 快速上手

### 步骤 1：通过编辑器窗口生成默认配置

打开编辑器窗口：`Tools → CycloneGames → Input System Editor`，点击 **Generate Default Config** 生成默认 YAML 配置文件。

### 步骤 2：配置代码生成（推荐）

在编辑器窗口中：

1. 设置**输出目录**（例如 `Assets/Scripts/Generated`）和**命名空间**（例如 `YourGame.Input.Generated`）
2. 点击 **Save and Generate Constants** 保存配置并生成 `InputActions.cs`

生成的文件将包含：

- `InputActions.Contexts.*` — 上下文名称的字符串常量（如 `InputActions.Contexts.Gameplay`）
- `InputActions.ActionMaps.*` — ActionMap 名称的字符串常量（如 `InputActions.ActionMaps.PlayerActions`）
- `InputActions.Actions.*` — 动作的整型哈希 ID（如 `InputActions.Actions.Gameplay_Move`）

这些常量支持类型安全、零 GC 的运行时输入访问。

### 步骤 3：启动时初始化

在游戏启动时（例如在 `MonoBehaviour.Start()` 或初始化脚本中）加载配置：

```csharp
using CycloneGames.IO.Unity;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;

// 在启动时调用
var defaultUri = UnityFileUri.Create("input_config.yaml", UnityFileLocation.StreamingAssets);
var userUri = UnityFileUri.Create("user_input_settings.yaml", UnityFileLocation.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

`InputSystemLoader` 优先加载用户配置（`PersistentData`），若不存在则回退到默认配置（`StreamingAssets`）。首次运行时，自动将默认配置复制到用户配置目录。

### 步骤 4：加入玩家并创建第一个上下文

**使用生成的常量（推荐，零 GC）：**

```csharp
using R3;
using YourGame.Input.Generated;               // 生成的常量命名空间
using CycloneGames.InputSystem.Runtime;

var svc = InputManager.Instance.JoinSinglePlayer(0);

// 创建 Context（name 参数可选，默认使用 actionMapName）
var ctx = new InputContext(InputActions.ActionMaps.PlayerActions)
    .AddBinding(svc.GetVector2Observable(InputActions.Actions.Gameplay_Move), new MoveCommand(OnMove))
    .AddBinding(svc.GetButtonObservable(InputActions.Actions.Gameplay_Confirm), new ActionCommand(OnConfirm));

// 绑定生命周期到当前组件 — 组件销毁时自动从栈中移除
// 注意：AddTo 返回 CancellationTokenRegistration，应单独调用，不要赋回给 ctx
ctx.AddTo(this);

svc.PushContext(ctx);
```

**使用字符串 API（兼容模式）：**

```csharp
var svc = InputManager.Instance.JoinSinglePlayer(0);
// ActionMap 必需，name 默认为 "PlayerActions"
var ctx = new InputContext("PlayerActions")
    .AddBinding(svc.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(dir => { /* ... */ }))
    .AddBinding(svc.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(() => { /* ... */ }));

svc.PushContext(ctx);
```

**完整 SimplePlayer 示例：**

```csharp
using UnityEngine;
using CycloneGames.InputSystem.Runtime;
using R3;

public class SimplePlayer : MonoBehaviour
{
    private IInputPlayer _input;
    private InputContext _context;

    private void Start()
    {
        _input = InputManager.Instance.JoinSinglePlayer(0);

        _context = new InputContext("PlayerActions")
            .AddBinding(_input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
            .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirm))
            .AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirmLongPress));

        _context.AddTo(this);
        _input.PushContext(_context);
    }

    private void OnMove(Vector2 dir)
    {
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

## 核心概念

### 4.1 上下文栈（Context Stack）

`InputSystem` 使用**Context 对象引用**作为唯一标识来管理输入栈：

**核心特性：**

1. **独立实例**：每次 `new InputContext(...)` 创建独立对象。即使名称相同（如 "Gameplay"），也是不同实例
2. **栈管理**：`PushContext(ctx)` 将对象推入栈，栈顶的 Context 为活动状态
3. **自动置顶**：若 `PushContext` 一个已在栈中的 Context 对象，它会自动移至栈顶（聚焦行为）
4. **精确移除**：`RemoveContext(ctx)` 只移除指定对象实例，不会误删同名 Context
5. **输入捕获**：`CaptureContext(ctx)` 会临时让指定 Context 覆盖普通栈顶，适合 Loading、全屏视频、模态弹窗等需要遮住下层但仍可输入的界面

**Push / Pop / AddTo 生命周期：**

```csharp
using R3;

// Push 推入栈顶（若已在栈中，移至栈顶）
inputService.PushContext(menuContext);          // 栈：[Gameplay, Menu]

// ⚠️ 使用 AddTo(this) 时不推荐 PopContext！
// PopContext 无脑移除栈顶。若栈顺序因动态 UI 发生变化，可能移除错误的 Context
// 请使用 RemoveContext(context) 或 AddTo(this) 代替

// 推荐：通过对象引用精确移除
inputService.RemoveContext(menuContext);

// 或者绑定生命周期（组件销毁时自动移除）
menuContext.AddTo(this);

// 查看当前活动上下文
string currentContext = inputService.ActiveContextName.CurrentValue;

// 订阅上下文变化
inputService.ActiveContextName.Subscribe(ctxName => Debug.Log($"当前上下文: {ctxName}"));
```

**UI 叠加场景示例：**

```csharp
using R3;

// UI A
public class UIWindowA : MonoBehaviour
{
    private InputContext _context;

    private void OnEnable()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        _context = new InputContext("UIActions", "UI")
            .AddBinding(input.GetButtonObservable("UIActions", "Confirm"), new ActionCommand(OnConfirmA));

        _context.AddTo(this);
        input.PushContext(_context);            // 栈：[A]
    }
}

// UI B（叠加在 A 上）
public class UIWindowB : MonoBehaviour
{
    private InputContext _context;

    private void OnEnable()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        _context = new InputContext("UIActions", "UI")  // 名称相同也没关系，不同实例
            .AddBinding(input.GetButtonObservable("UIActions", "Confirm"), new ActionCommand(OnConfirmB));

        _context.AddTo(this);
        input.PushContext(_context);            // 栈：[A, B]，B 在栈顶，A 被暂停
    }
    // B 关闭时，AddTo 自动移除，A 自动恢复
}
```

**多个 Context 共享相同 ActionMap：**

✅ 多个 Context 可以安全地共享相同的 ActionMap 名称
✅ 每个 Context 的绑定独立存储
✅ 切换 Context 时，仅激活栈顶 Context 的绑定
✅ Context 之间不会产生冲突

```csharp
using R3;

// 游戏 Context
public class GameplayController : MonoBehaviour
{
    private void Start()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        var gameplay = new InputContext("PlayerActions", "Gameplay")
            .AddBinding(input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
            .AddBinding(input.GetButtonObservable("PlayerActions", "Jump"), new ActionCommand(OnJump));

        gameplay.AddTo(this);
        input.PushContext(gameplay);            // 栈：[Gameplay]
    }
}

// 暂停菜单 Context — 同样使用 "PlayerActions"
public class PauseMenu : MonoBehaviour
{
    private void OnEnable()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        var pause = new InputContext("PlayerActions", "PauseMenu")
            .AddBinding(input.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMenuNavigate))
            .AddBinding(input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnMenuConfirm))
            .AddBinding(input.GetButtonObservable("PlayerActions", "Cancel"), new ActionCommand(OnMenuCancel));

        pause.AddTo(this);
        input.PushContext(pause);               // 栈：[Gameplay, PauseMenu]，仅 PauseMenu 绑定生效
    }
    // PauseMenu 销毁时，Gameplay Context 自动恢复
}
```

### 4.1.1 作用域输入捕获（Scoped Input Capture）

`CaptureContext(ctx)` 用于临时接管当前输入焦点，但不会阻止普通 Context 栈继续变化。它适合全屏加载界面、开场视频、确认弹窗、暂停菜单等场景：上层 UI 需要继续接收输入，下层 Gameplay、HUD、PlayerController 仍然可以异步初始化并注册自己的输入。

捕获期间的行为：

| 操作 | 行为 |
|------|------|
| `CaptureContext(ctx)` | 将 `ctx` 激活到普通栈之上，并返回一个 `IDisposable` 作用域 |
| `PushContext(gameplayCtx)` | 仍然更新普通栈，但不会抢走当前输入焦点 |
| `RemoveContext(ctx)` / `ctx.Dispose()` | 从普通栈和捕获栈中同时移除该 Context |
| 释放捕获作用域 | 恢复到下一个捕获 Context；若没有捕获，则恢复普通栈顶 |

```csharp
var loadingContext = new InputContext("UIActions", "Loading")
    .AddBinding(input.GetButtonObservable("UIActions", "Cancel"), new ActionCommand(CancelLoading));

loadingContext.AddTo(this);
using (input.CaptureContext(loadingContext))
{
    await LoadWorldAsync();

    // 下层内容可以安全 PushContext，Loading UI 仍然保持活动输入。
    input.PushContext(gameplayContext);
    input.PushContext(playerControllerContext);
}

// 捕获释放后，输入恢复到真实的普通栈顶。
```

捕获也支持嵌套。例如 Loading 界面上再弹出确认框时，可以 Capture 确认框 Context；确认框关闭后，输入会回到 Loading Context。

### 4.2 InputPlayer 与 IInputPlayer

**IInputPlayer** 是单个玩家的输入服务接口，提供响应式流、上下文管理和运行时键位修改。

**关键属性：**

| 属性 | 类型 | 描述 |
|------|------|------|
| `ActiveContextName` | `ReadOnlyReactiveProperty<string>` | 当前活动上下文名称 |
| `ActiveDeviceKind` | `ReadOnlyReactiveProperty<InputDeviceKind>` | 当前活动设备类型（键鼠/手柄/触控） |
| `OnContextChanged` | `event Action<string>` | 上下文切换事件 |

**加入玩家：**

```csharp
// 单人模式 — 自动加入并将所有必需设备锁定给单个玩家
var svc = InputManager.Instance.JoinSinglePlayer(0);

// 若玩家已加入，JoinSinglePlayer 返回现有服务，不触发 OnPlayerInputReady
var svc2 = InputManager.Instance.JoinSinglePlayer(0);  // 返回同一服务，无事件触发

// 如需在玩家已加入后刷新绑定，使用 RefreshPlayerInput
InputManager.Instance.RefreshPlayerInput(0);            // 触发 OnPlayerInputReady 激活新绑定
```

**ActiveDeviceKind — 活动设备检测：**

```csharp
// 实时追踪玩家最后一次使用的设备是键鼠还是手柄
inputPlayer.ActiveDeviceKind.Subscribe(kind =>
{
    switch (kind)
    {
        case InputDeviceKind.KeyboardMouse:
            ShowKeyboardPrompts();
            break;
        case InputDeviceKind.Gamepad:
            ShowGamepadPrompts();
            break;
    }
});
```

### 4.3 命令模式

所有输入绑定通过命令模式回调，支持四种命令类型：

| 命令 | 接口 | Observable 类型 | 签名 | 典型用途 |
|------|------|-----------------|------|----------|
| **ActionCommand** | `IActionCommand` | `Observable<Unit>` | `() => void` | 按钮确认、跳跃、攻击 |
| **MoveCommand** | `IMoveCommand` | `Observable<Vector2>` | `(Vector2) => void` | 移动、菜单导航 |
| **ScalarCommand** | `IScalarCommand` | `Observable<float>` | `(float) => void` | 扳机轴、长按进度 |
| **BoolCommand** | `IBoolCommand` | `Observable<bool>` | `(bool) => void` | 按下/释放状态 |

```csharp
var ctx = new InputContext("PlayerActions")
    .AddBinding(svc.GetButtonObservable("PlayerActions", "Jump"), new ActionCommand(OnJump))
    .AddBinding(svc.GetVector2Observable("PlayerActions", "Move"), new MoveCommand(OnMove))
    .AddBinding(svc.GetScalarObservable("PlayerActions", "FireTrigger"), new ScalarCommand(OnFireTrigger))
    .AddBinding(svc.GetPressStateObservable("PlayerActions", "Sprint"), new BoolCommand(OnSprintState));
```

### 4.4 响应式输入流

`IInputPlayer` 提供七种 Observable 流，均支持**字符串**和**整型哈希（零 GC）** 两种调用方式：

| Observable | 返回类型 | 适用动作类型 | 描述 |
|------------|----------|-------------|------|
| `GetButtonObservable` | `Observable<Unit>` | Button | 按下时触发 |
| `GetVector2Observable` | `Observable<Vector2>` | Vector2 | 持续输出二维向量（摇杆/WASD） |
| `GetScalarObservable` | `Observable<float>` | Float | 持续输出标量值（扳机轴） |
| `GetLongPressObservable` | `Observable<Unit>` | Button（配置 longPressMs） | 按住达到时长后触发一次 |
| `GetLongPressProgressObservable` | `Observable<float>` | Button（配置 longPressMs） | 持续输出 0~1 进度，取消时输出 -1 |
| `GetPressStateObservable` | `Observable<bool>` | Button | 按下=true，释放=false |
| `GetChordObservable` | `Observable<Unit>` | Button ×2 | 两个按钮在时间窗口内同时按下 |

**字符串 vs 整型哈希：**

```csharp
// 字符串（兼容模式，运行时字符串查找）
svc.GetButtonObservable("PlayerActions", "Jump");

// 整型哈希（推荐，零 GC，编译时常量）
svc.GetButtonObservable(InputActions.Actions.Gameplay_Jump);
```

## 功能指南

### 5.1 运行时键位修改

支持运行时重新绑定、重置和查询当前绑定：

```csharp
var player = InputManager.Instance.GetInputPlayer(0);

// 重新绑定 — 将旧绑定路径替换为新路径
bool ok = player.RebindAction("PlayerActions", "Jump", "<Keyboard>/space", "<Keyboard>/j");

// 重置单个动作的绑定（移除所有覆写）
bool reset = player.ResetActionBinding("PlayerActions", "Jump");

// 重置所有动作的绑定
player.ResetAllActionBindings();

// 获取当前生效的绑定路径（含覆写）
string[] bindings = player.GetActionBindings("PlayerActions", "Jump");
// 例如输出: ["<Keyboard>/space", "<Gamepad>/buttonSouth"]

// 通过 InputManager 代理调用
InputManager.Instance.RebindAction(0, "PlayerActions", "Jump", "<Keyboard>/space", "<Keyboard>/j");
InputManager.Instance.ResetActionBinding(0, "PlayerActions", "Jump");
string[] paths = InputManager.Instance.GetActionBindings(0, "PlayerActions", "Jump");
```

### 5.2 复合键（Chord）检测

检测两个按钮在指定时间窗口内同时按下（如 A+B 组合技）。零 GC 实现，发射路径无分配。

```csharp
using YourGame.Input.Generated;
using CycloneGames.InputSystem.Runtime;

var ctx = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
    .AddBinding(
        // A+B 同时按下，300ms 窗口
        _input.GetChordObservable(
            InputActions.Actions.Gameplay_Punch,
            InputActions.Actions.Gameplay_Kick,
            windowMs: 300f),
        new ActionCommand(OnPunchAndKickCombo))
    .AddBinding(
        // 快速按下技：50ms 严格窗口
        _input.GetChordObservable(
            InputActions.Actions.Gameplay_LightAttack,
            InputActions.Actions.Gameplay_HeavyAttack,
            windowMs: 50f),
        new ActionCommand(OnRapidCombo));

// 字符串 API
_input.GetChordObservable("PlayerActions", "Punch", "Kick", 300f);
```

**行为说明：** 当两个按钮都按下且在 `windowMs` 内时发射一次 `Unit`。任一按钮释放后重置，可再次触发。适用于 A+B 组合技、快速连续技、音游双押等场景。

### 5.3 长按与进度条

**配置长按（YAML）：**

```yaml
playerSlots:
  - playerId: 0
    contexts:
      - name: Gameplay
        actionMap: PlayerActions
        bindings:
          - type: Button
            action: Confirm
            longPressMs: 500          # 按住 500ms 触发长按
            deviceBindings:
              - "<Keyboard>/space"
              - "<Gamepad>/buttonSouth"
```

**运行时：**

```csharp
using YourGame.Input.Generated;

// 长按完成事件
var ctx = new InputContext(InputActions.ActionMaps.PlayerActions)
    .AddBinding(
        _input.GetLongPressObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(OnActionCharged));

// 长按进度条（UI 显示 0~1）
public class ChargeSkillUI : MonoBehaviour
{
    [SerializeField] private Image _progressBar;

    private void Start()
    {
        var input = InputManager.Instance.GetInputPlayer(0);
        var ctx = new InputContext(InputActions.ActionMaps.PlayerActions, InputActions.Contexts.Gameplay)
            .AddBinding(
                input.GetLongPressProgressObservable(InputActions.Actions.Gameplay_Confirm),
                new ScalarCommand(progress =>
                {
                    if (progress < 0f)
                    {
                        _progressBar.gameObject.SetActive(false);   // 取消
                    }
                    else
                    {
                        _progressBar.gameObject.SetActive(true);
                        _progressBar.fillAmount = progress;         // 进度 0~1
                        if (progress >= 1f)
                            OnSkillReady();                          // 长按完成
                    }
                }));

        ctx.AddTo(this);
        input.PushContext(ctx);
    }

    private void OnSkillReady() { /* 蓄力完成 */ }
}
```

**进度值含义：**

| 值 | 含义 |
|----|------|
| `0 ~ 1` | 长按进度（0% → 100%） |
| `1.0` | 长按完成（**仅触发一次**） |
| `-1` | 取消（在完成前松开） |

### 5.4 上下文特定的短按 / 长按互斥

如果同一按键在不同情景下分别需要"短按触发"和"长按触发"且互斥，定义两个 Context 分别配置：

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
            longPressMs: 600          # 仅长按
            deviceBindings:
              - "<Keyboard>/space"
              - "<Gamepad>/buttonSouth"
```

```csharp
// Inspect 上下文：仅绑定短按
var ctxInspect = new InputContext("PlayerActions", "Inspect")
    .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnInspectConfirm));

// Charge 上下文：仅绑定长按
var ctxCharge = new InputContext("PlayerActions", "Charge")
    .AddBinding(_input.GetLongPressObservable("PlayerActions", "Confirm"), new ActionCommand(OnChargeConfirm));

if (enteredChargeZone)
    _input.PushContext(ctxCharge);
else
    _input.PushContext(ctxInspect);
```

### 5.5 输入阻塞

临时禁用所有输入（例如暂停菜单、过场动画）：

```csharp
inputService.BlockInput();    // 阻塞所有输入
inputService.UnblockInput();  // 恢复输入（恢复当前活动上下文的 ActionMap）
```

`BlockInput` / `UnblockInput` 支持嵌套计数：只有所有阻塞都释放后，输入才会恢复。异步加载流程建议使用作用域写法：

```csharp
using (input.BlockInputScope())
{
    await LoadWorldAsync();

    // 下层 Gameplay 可以继续 PushContext，但不会触发任何输入响应。
    input.PushContext(gameplayContext);
    input.PushContext(playerControllerContext);
}

// 输入恢复到当前捕获 Context，若没有捕获，则恢复普通栈顶。
```

如果 Loading、视频、模态 UI 自己仍然需要响应按钮输入，请优先使用 `CaptureContext`，而不是 `BlockInput`。`BlockInput` 会禁用整个 `InputActionAsset`，而 `CaptureContext` 只是在输入优先级上临时覆盖普通栈顶。

### 5.6 Loading / 模态输入模式

当上层界面需要遮住全屏，但下层还会异步创建大量对象并注册输入时，使用作用域捕获：

```csharp
private IDisposable _loadingInputCapture;
private InputContext _loadingContext;

private async UniTask ShowLoadingAndEnterGameplayAsync()
{
    _loadingContext = new InputContext("UIActions", "Loading")
        .AddBinding(_input.GetButtonObservable("UIActions", "Skip"), new ActionCommand(TrySkip));

    _loadingContext.AddTo(this);
    _loadingInputCapture = _input.CaptureContext(_loadingContext);

    await InitializeGameplayAsync(); // PlayerController / HUD 可以在这里 PushContext。

    _loadingInputCapture.Dispose();
    _loadingInputCapture = null;
}
```

长流程建议使用 `try/finally`，确保加载失败或取消时也能释放捕获：

```csharp
IDisposable capture = _input.CaptureContext(_loadingContext);
try
{
    await InitializeGameplayAsync();
}
finally
{
    capture.Dispose();
}
```

### 5.7 设备图标自动切换

`InputDeviceIconSet` 是 ScriptableObject 资产，映射 `InputDeviceKind` 到对应的 UI 精灵。

**创建资产：** `Create → CycloneGames → Input → Device Icon Set`

**InputDeviceIconSet：**

```csharp
// ScriptableObject 引用
[SerializeField] private InputDeviceIconSet _iconSet;

// 运行时获取对应设备图标
Sprite icon = _iconSet.GetIcon(InputDeviceKind.Gamepad);    // 手柄图标
Sprite icon2 = _iconSet.GetIcon(InputDeviceKind.KeyboardMouse); // 键鼠图标
```

**InputDeviceIconSwitcher** 是 MonoBehaviour 组件，自动订阅 `ActiveDeviceKind` 并切换 `Image.sprite`：

```csharp
// 挂载到带有 Image 组件的 GameObject 上
// [RequireComponent(typeof(Image))]
// 在 Inspector 中指定 iconSet 引用即可
// 运行时自动根据输入端切换图标（无需额外代码）
```

### 5.8 鼠标按键轮询

在热路径中可直接轮询鼠标按钮状态，无需通过 Observable 订阅：

```csharp
var player = InputManager.Instance.GetInputPlayer(0);

if (player.IsLeftMouseButtonPressed)    { /* 左键按下 */ }
if (player.IsRightMouseButtonPressed)   { /* 右键按下 */ }
if (player.IsMiddleMouseButtonPressed)  { /* 中键按下 */ }
```

## YAML 配置

完整的 Schema 如下：

```yaml
# 加入动作 — 大厅阶段玩家按此键加入
joinAction:
  type: Button
  action: JoinGame
  deviceBindings:
    - "<Keyboard>/enter"
    - "<Gamepad>/start"

# 玩家槽位 — 每个槽位可配置多个上下文（Context）
playerSlots:
  - playerId: 0
    contexts:
      - name: Gameplay                         # Context 名称（用于调试）
        actionMap: PlayerActions               # Unity Input System ActionMap 名称
        bindings:
          # === Vector2 类型（摇杆/移动键） ===
          - type: Vector2
            action: Move
            deviceBindings:
              - "<Gamepad>/leftStick"
              - "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
              - "<Mouse>/delta"

          # === Button 类型（按键/短按/长按） ===
          - type: Button
            action: Jump
            deviceBindings:
              - "<Gamepad>/buttonSouth"
              - "<Keyboard>/space"

          - type: Button
            action: Confirm
            longPressMs: 500                    # 可选，配置后触发长按事件
            deviceBindings:
              - "<Gamepad>/buttonSouth"
              - "<Keyboard>/space"

          # === Float 类型（扳机轴） ===
          - type: Float
            action: FireTrigger
            longPressMs: 600                    # 可选：浮点长按
            longPressValueThreshold: 0.6        # 阈值（0~1），达到后视为按下
            deviceBindings:
              - "<Gamepad>/leftTrigger"
```

**字段说明：**

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `joinAction.type` | `Button` | 是 | 加入动作类型（固定为 Button） |
| `joinAction.action` | `string` | 是 | 加入动作名称 |
| `joinAction.deviceBindings` | `string[]` | 是 | 加入按钮的绑定路径 |
| `playerSlots[].playerId` | `int` | 是 | 玩家 ID |
| `contexts[].name` | `string` | 是 | Context 显示名称 |
| `contexts[].actionMap` | `string` | 是 | Unity Input System ActionMap 名称 |
| `bindings[].type` | `Vector2` / `Button` / `Float` | 是 | 动作值类型 |
| `bindings[].action` | `string` | 是 | 动作名称 |
| `bindings[].longPressMs` | `int` | 否 | 长按触发时间（毫秒），仅 Button/Float 类型有效 |
| `bindings[].longPressValueThreshold` | `float` | 否 | Float 类型的按下阈值（0~1），默认 0.5 |
| `bindings[].deviceBindings` | `string[]` | 是 | 设备绑定路径，支持 `2DVector(...)` 复合绑定 |

## 多人游戏模式

### 单人模式（自动锁定设备）

```csharp
var svc = InputManager.Instance.JoinSinglePlayer(0);

// 若玩家已加入，返回现有服务而不触发 OnPlayerInputReady
var svc2 = InputManager.Instance.JoinSinglePlayer(0); // 同一服务，无事件

// 若需在玩家已加入后重新绑定 Context，使用 RefreshPlayerInput
InputManager.Instance.RefreshPlayerInput(0);
```

### 大厅模式 — 设备锁定

第一个设备加入成为玩家 0，后续设备自动配对给该玩家。适合单人玩家在键鼠和手柄间切换：

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
InputManager.Instance.StartListeningForPlayers(true); // true = 设备锁定模式
```

### 大厅模式 — 设备共享

每个新设备加入创建一个新玩家（玩家 0, 1, 2...），适合本地多人合作：

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
InputManager.Instance.StartListeningForPlayers(false); // false = 设备共享模式
```

### 批量加入

```csharp
// 同步批量加入
var players = InputManager.Instance.JoinPlayersBatch(new List<int> { 0, 1, 2 });

// 异步批量加入（带超时）
var players = await InputManager.Instance.JoinPlayersBatchAsync(
    new List<int> { 0, 1, 2 },
    timeoutPerPlayerInSeconds: 5
);
```

### 异步加入（等待设备连接）

```csharp
var svc = await InputManager.Instance.JoinSinglePlayerAsync(0, timeoutInSeconds: 5);
if (svc != null)
{
    // 玩家成功加入
}
```

### 手动锁定设备

```csharp
// 将特定设备锁定给特定玩家
var svc = InputManager.Instance.JoinPlayerAndLockDevice(0, Keyboard.current);

// 多个玩家共享键盘（适合回合制游戏）
var player0 = InputManager.Instance.JoinPlayerOnSharedDevice(0);
var player1 = InputManager.Instance.JoinPlayerOnSharedDevice(1);
```

## 运行时配置管理

### 热重载

```csharp
bool success = await InputManager.Instance.ReloadConfigurationAsync();
if (success)
{
    Debug.Log("配置已重新加载");
    // 新加入的玩家将使用新配置
}
```

### 保存用户配置

```csharp
await InputManager.Instance.SaveUserConfigurationAsync();
```

### 重置为默认配置

```csharp
using CycloneGames.IO.Unity;
using CycloneGames.InputSystem.Runtime;

var defaultUri = UnityFileUri.Create("input_config.yaml", UnityFileLocation.StreamingAssets);
var userUri = UnityFileUri.Create("user_input_settings.yaml", UnityFileLocation.PersistentData);

bool success = await InputSystemLoader.ResetToDefaultAsync(defaultUri, userUri);
if (success)
{
    Debug.Log("配置已重置为默认值");
}
```

### 事件回调

```csharp
// 玩家输入就绪事件
InputManager.Instance.OnPlayerInputReady += (IInputPlayer playerInput) =>
{
    Debug.Log($"玩家 {(playerInput as InputPlayer)?.PlayerId} 输入已就绪");
};

// 配置重载事件
InputManager.Instance.OnConfigurationReloaded += () =>
{
    Debug.Log("配置已重新加载");
};

// 刷新已加入玩家的输入绑定（跨场景使用时）
// 场景：LaunchScene 初始化输入系统 → GameplayScene 绑定 Context
// 在 GameplayScene 中绑定 Context 后触发刷新：
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;
// ...在绑定 Context 之后...
if (InputManager.Instance.GetInputPlayer(0) != null)
{
    InputManager.Instance.RefreshPlayerInput(0);
}

// 上下文切换事件
inputService.OnContextChanged += (string contextName) =>
{
    Debug.Log($"上下文切换到: {contextName}");
};
```

## 输入工具集（Runtime/Tools/）

工具集位于独立 asmdef `CycloneGames.InputSystem.Tools.Runtime`，需额外引用。

### 9.1 InputRecorder — 录制回放

录制玩家的输入操作，将时序数据导出为 `InputRecording` 对象，支持将录制内容重放为 Observable 流。

```csharp
using CycloneGames.InputSystem.Runtime;
using R3;

var recorder = new InputRecorder();

// 注册要录制的动作
recorder.RecordAction("PlayerActions", "Move");
recorder.RecordAction("PlayerActions", "Jump");
recorder.RecordAction("PlayerActions", "Attack");

// 开始录制
recorder.StartRecording(player);

// ...玩游戏...

// 停止录制
InputRecording recording = recorder.StopRecording();

Debug.Log($"录制时长: {recording.Duration}s, 帧数: {recording.FrameCount}");

// 重放为 Observable（可像普通输入一样绑定到 Context）
recording.CreateReplayVector2Observable()
    .Subscribe(v => Debug.Log($"重放移动: {v}"));

recording.CreateReplayObservable()
    .Subscribe(_ => Debug.Log("重放按钮事件"));

recording.CreateReplayFloatObservable()
    .Subscribe(f => Debug.Log($"重放浮点值: {f}"));

// 用完释放
recorder.Dispose();
```

**典型应用：** 教程演示、AI 训练数据录制、Bug 复现、自动化测试。

### 9.2 InputSequenceMatcher — 序列匹配与手势识别

在录制数据中检测特定输入序列（如格斗游戏出招表）和手势模式。

#### 序列匹配

```csharp
using CycloneGames.InputSystem.Runtime;

var recording = /* 来自 InputRecorder */;

// 定义要检测的序列（如：波动拳 ↓↘→ + P）
var hadouken = new SequenceStep[]
{
    new SequenceStep { ActionName = "Move", ExpectedType = ActionValueType.Vector2,
        ExpectedDirection = new Vector2(0, -1), DirectionTolerance = 0.3f, MaxDelayMs = 200f },
    new SequenceStep { ActionName = "Move", ExpectedType = ActionValueType.Vector2,
        ExpectedDirection = new Vector2(1, -1).normalized, DirectionTolerance = 0.3f, MaxDelayMs = 200f },
    new SequenceStep { ActionName = "Move", ExpectedType = ActionValueType.Vector2,
        ExpectedDirection = new Vector2(1, 0), DirectionTolerance = 0.3f, MaxDelayMs = 200f },
    new SequenceStep { ActionName = "Punch", ExpectedType = ActionValueType.Button, MaxDelayMs = 150f },
};

SequenceMatchResult result = InputSequenceMatcher.DetectSequence(recording, hadouken);

if (result.Matched)
{
    Debug.Log($"检测到招式! 出现 {result.OccurrenceCount} 次, "
            + $"最佳时长: {result.BestTotalDuration:F3}s, 平均: {result.AverageDuration:F3}s");

    foreach (var occ in result.Occurrences)
    {
        Debug.Log($"  出招时间: {occ.StartTime:F3}s ~ {occ.EndTime:F3}s");
    }
}
```

#### 手势识别（内置格斗游戏出招表）

```csharp
using CycloneGames.InputSystem.Runtime;

var recording = /* 来自 InputRecorder */;

// 使用内置手势定义
var result1 = InputGestureRecognizer.DetectGesture(recording, GestureDefinition.QuarterCircleForward);
var result2 = InputGestureRecognizer.DetectGesture(recording, GestureDefinition.DragonPunch);
var result3 = InputGestureRecognizer.DetectGesture(recording, GestureDefinition.HalfCircleForward);

if (result1.Matched) Debug.Log($"波动拳! 时长: {result1.Duration:F3}s, "
                                + $"方向切换数: {result1.DirectionTransitions.Length}");

// 自定义手势
var myGesture = new GestureDefinition(
    "我的必杀技",
    new[] { Direction8Way.Left, Direction8Way.Right, Direction8Way.Up },
    timeWindowSec: 0.3f,
    inputDeadZone: 0.3f
);
var customResult = InputGestureRecognizer.DetectGesture(recording, myGesture);
```

**内置手势定义：**

| 手势 | 名称 | 方向序列 | 时间窗口 |
|------|------|----------|----------|
| `QuarterCircleForward` | 波动拳（↓↘→） | ↓ → ↘ → → | 0.4s |
| `QuarterCircleBack` | 反波动拳（↓↙←） | ↓ → ↙ → ← | 0.4s |
| `DragonPunch` | 升龙拳（→↓↘） | → → ↓ → ↘ | 0.35s |
| `HalfCircleForward` | 半圆前（←↙↓↘→） | ← → ↙ → ↓ → ↘ → → | 0.6s |
| `HalfCircleBack` | 半圆后（→↘↓↙←） | → → ↘ → ↓ → ↙ → ← | 0.6s |
| `FullCircle` | 全圆 360° | 8 方向全周 | 0.8s |
| `DashForward` | 前冲 | → → 回中 → → | 0.2s |
| `DashBack` | 后退 | ← → 回中 → ← | 0.2s |

### 9.3 InputTimingValidator — 反作弊时序验证

通过多层统计分析检测非人类输入模式。两级判定机制：

- **Tier 1（硬判定）**：物理上人类不可能达到的模式 — 近零误判
- **Tier 2（软评分）**：统计异常指标加权计算 `HumanLikenessScore`（0~1）

```csharp
using CycloneGames.InputSystem.Runtime;

var recording = /* 来自 InputRecorder */;

// 定义节拍时间点（秒）
float[] beatTimings = { 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f };

// 使用默认配置（Normal）
TimingValidationResult result = InputTimingValidator.ValidateTiming(
    recording, beatTimings,
    actionMapName: "PlayerActions",
    actionName: "Hit",
    timingWindowMs: 100f);

// 分析结果
if (result.IsDefinitivelyBot)
{
    Debug.LogWarning($"机器人判定! "
        + $"Perfect: {result.IsSuspiciousPerfect}, "
        + $"SubFrame: {result.IsSuspiciousSubFrame}, "
        + $"Uniform: {result.IsSuspiciousUniform}");
}
else
{
    Debug.Log($"人类相似度: {result.HumanLikenessScore:P2}, "
            + $"平均偏差: {result.MeanDeviationMs:F1}ms, "
            + $"标准差: {result.StdDeviationMs:F1}ms, "
            + $"自相关(lag1): {result.AutocorrelationLag1:F3}, "
            + $"漂移斜率: {result.DriftSlopeMsPerBeat:F4}ms/beat");
}

if (result.IsHumanLikely)
{
    Debug.Log("✅ 通过人类验证");
}
```

### 9.4 InputBindingValidator — Context 感知的绑定冲突检测

在 Editor 或运行时检测单个 Context 内的绑定冲突，按严重程度分级。

```csharp
using CycloneGames.InputSystem.Runtime;

// 从配置中检测所有 Context 的冲突
var config = /* 来自 InputConfiguration 的 PlayerSlotConfig */;
List<BindingConflict> conflicts = InputBindingValidator.DetectConflicts(config);

// 按 Context 名称过滤
List<BindingConflict> gameplayConflicts = InputBindingValidator.DetectConflicts(config, "Gameplay");

// 格式化冲突报告
string report = InputBindingValidator.FormatConflictsReport(conflicts);
Debug.Log(report);
// 输出示例:
// === Binding Conflict Report (3 total: 1 critical, 1 warning, 1 info) ===
// Context: "Gameplay" (3 conflicts)
//   Critical  | <Keyboard>/space
//     └─ "Jump"(Button) vs "Confirm"(Button)
//   Warning   | <Gamepad>/leftStick
//     └─ "Move"(Vector2) vs "Look"(Vector2)
//   Info      | <Keyboard>/f
//     └─ "QuickUse"(Button) vs "HoldUse"(Button)
```

**严重程度：**

| 级别 | 含义 | 触发条件 |
|------|------|----------|
| `Critical` | 必须修复 | 同一绑定路径上两个**相同类型**的动作，且无长按区分 |
| `Warning` | 建议检查 | 同一绑定路径上两个**不同类型**的动作 |
| `Info` | 可忽略 | 同一绑定路径上有长按时间区分（短按 vs 长按属于预期行为） |

## UGUI 集成：ItemNavigator

`ItemNavigator` 是一套零 GC、生产级的 UGUI 导航组件，专为手柄和键盘导航设计，同时完美支持鼠标和触控交互。

<img src="./Documents~/Input_IntegrateSample.gif" alt="Input integrate preview" style="width: 100%; height: auto; max-width: 854px;" />

### 核心特性

- **零 GC 分配**：所有操作在运行时不产生垃圾回收
- **多控件支持**：Button、Toggle、Slider、自定义 Transform
- **双向导航**：MenuNavigatorVertical（垂直）和 MenuNavigatorHorizontal（水平）
- **智能焦点管理**：可移动的焦点指示器、自动跳过禁用项
- **触控确认门**：手柄切换到触控时，首次点击仅聚焦，二次点击确认
- **Slider 灵活配置**：步进模式、平滑模式、混合模式
- **统一事件处理**：无需额外绑定 UGUI 的 OnClick 等事件

### 无需绑定 UGUI 原生事件

使用 `MenuNavigator` 后，所有交互（手柄确认键、键盘回车、鼠标点击、触控点击）统一通过 `NavigableItemSetup.OnConfirm` 回调处理：

| 控件类型 | UGUI 原生事件 | MenuNavigator 处理方式 |
|----------|---------------|------------------------|
| **Button** | ~~onClick~~ | `OnConfirm` 回调 |
| **Toggle** | ~~onValueChanged~~ | `OnConfirm` 回调（需手动控制 `isOn`） |
| **Slider** | ~~onValueChanged~~ | `SliderConfig.OnValueChanged` 或直接读取 `slider.value` |
| **CustomTransform** | 无 | `OnConfirm` 回调 |

### 快速上手：垂直导航

```csharp
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.InputSystem.Runtime;
using R3;

public class SettingsMenu : MonoBehaviour
{
    [SerializeField] private Slider _volumeSlider;
    [SerializeField] private Slider _brightnessSlider;
    [SerializeField] private Toggle _fullscreenToggle;
    [SerializeField] private Button _backButton;
    [SerializeField] private Transform _focusIndicator;

    private MenuNavigatorVertical _navigator;
    private IInputPlayer _input;
    private InputContext _context;

    private void Start()
    {
        _navigator = gameObject.AddComponent<MenuNavigatorVertical>();
        _input = InputManager.Instance.GetInputPlayer(0);

        _navigator.Initialize(
            setupData: new NavigableItemSetup[]
            {
                new NavigableItemSetup
                {
                    Slider = _volumeSlider,
                    SliderConfig = SliderConfig.Default,    // Step=0.1
                    OnFocused = t => Debug.Log("音量聚焦"),
                    OnConfirm = () => Debug.Log("音量确认")
                },
                new NavigableItemSetup
                {
                    Slider = _brightnessSlider,
                    SliderConfig = new SliderConfig { Step = 0.05f },
                    OnFocused = t => Debug.Log("亮度聚焦")
                },
                new NavigableItemSetup
                {
                    Toggle = _fullscreenToggle,
                    OnConfirm = () =>
                    {
                        _fullscreenToggle.isOn = !_fullscreenToggle.isOn;
                        ApplyFullscreen(_fullscreenToggle.isOn);
                    }
                },
                new NavigableItemSetup
                {
                    Button = _backButton,
                    OnConfirm = () => CloseMenu()
                }
            },
            focusIndicator: _focusIndicator,
            defaultFocusIndex: 0,
            allowLooping: true,
            focusIndicatorOnTop: true,
            inputPlayer: _input                 // 启用触控确认门
        );

        _context = new InputContext("UIActions", "Settings")
            .AddBinding(_input.GetVector2Observable("UIActions", "Navigate"),
                new MoveCommand(dir => _navigator.Navigate(dir)))
            .AddBinding(_input.GetButtonObservable("UIActions", "Confirm"),
                new ActionCommand(() => _navigator.ConfirmSelection()))
            .AddBinding(_input.GetButtonObservable("UIActions", "Cancel"),
                new ActionCommand(() => _navigator.TryCancelEdit()));

        _context.AddTo(this);
        _input.PushContext(_context);
    }

    private void Update()
    {
        Vector2 navDir = _input.GetVector2Observable("UIActions", "Navigate")
            .ToReadOnlyReactiveProperty().CurrentValue;
        _navigator.UpdateSmoothSlider(navDir);
    }

    private void ApplyFullscreen(bool isOn) { /* ... */ }
    private void CloseMenu() { /* ... */ }
}
```

### SliderConfig 配置

| 模式 | Step | SmoothSpeed | 行为描述 |
|------|------|-------------|----------|
| **Step（默认）** | 0.1 | 0 | 按方向键 = 离散步进 ±0.1 |
| **Smooth** | 0 | 1.0 | 按住方向键 = 持续变化 ±1.0/s |
| **Hybrid** | 0.1 | 0.5 | 按一次 = ±0.1，按住 = 持续 ±0.5/s |

```csharp
SliderConfig.Default   // Step=0.1, SmoothSpeed=0（最常用）
SliderConfig.Smooth    // Step=0, SmoothSpeed=1.0（进度条、时间轴）
SliderConfig.Hybrid    // Step=0.1, SmoothSpeed=0.5（音量等需精细微调）

// 自定义 + 防误触编辑模式
new NavigableItemSetup
{
    Slider = _masterVolume,
    SliderConfig = new SliderConfig
    {
        Step = 0.1f,
        RequireConfirmToEdit = true,    // 需按确认键进入编辑模式
        OnValueChanged = v => ApplyVolume(v)
    }
};
```

### 水平导航

```csharp
var navigator = gameObject.AddComponent<MenuNavigatorHorizontal>();

navigator.Initialize(
    setupData: new HorizontalNavItemSetup[]
    {
        new HorizontalNavItemSetup
        {
            Button = _tab1,
            OnConfirm = () => SwitchToTab(0),
            OnFocused = t => Debug.Log("Tab 1 聚焦"),
            OnNavigateUp = () => _verticalNav.SetFocusByIndex(0),
            OnNavigateDown = () => _verticalNav.SetFocusByIndex(0)
        },
        // ...
    },
    focusIndicator: _tabIndicator,
    defaultFocusIndex: 0,
    allowLooping: true,
    inputPlayer: InputManager.Instance.GetInputPlayer(0)
);
```

### API 参考

#### NavigableItemSetup（垂直）

| 属性 | 类型 | 描述 |
|------|------|------|
| `Button` | Button | 按钮控件 |
| `Toggle` | Toggle | 开关控件 |
| `Slider` | Slider | 滑块控件 |
| `CustomTransform` | Transform | 自定义组件 Transform |
| `SliderConfig` | SliderConfig | Slider 控制配置 |
| `OnConfirm` | Action | 确认回调 |
| `OnNavigateLeft` | Action | 左导航回调 |
| `OnNavigateRight` | Action | 右导航回调 |
| `OnFocused` | Action\<Transform\> | 聚焦回调 |
| `OnUnfocused` | Action\<Transform\> | 失焦回调 |

#### MenuNavigatorVertical

| 方法 | 描述 |
|------|------|
| `Initialize(...)` | 初始化导航器 |
| `Navigate(Vector2)` | 处理导航输入 |
| `ConfirmSelection()` | 确认当前选择 |
| `TryCancelEdit()` | 尝试退出 Slider 编辑模式 |
| `UpdateSmoothSlider(Vector2)` | 更新平滑 Slider（每帧调用） |
| `StopSmoothSlider()` | 停止平滑调整 |
| `SetFocusByIndex(int)` | 设置焦点索引 |
| `RefreshFocus()` | 刷新当前焦点 |

#### MenuNavigatorHorizontal

| 方法 | 描述 |
|------|------|
| `Initialize(...)` | 初始化导航器 |
| `Navigate(Vector2)` | 处理导航输入 |
| `ConfirmSelection()` | 确认当前选择 |
| `SetFocusByIndex(int)` | 设置焦点索引 |
| `RefreshFocus()` | 刷新当前焦点 |

### 最佳实践

1. **优先使用 SliderConfig.Default**：大多数场景下步进模式足够
2. **平滑模式需要每帧调用**：别忘记在 Update 中调用 `UpdateSmoothSlider()`
3. **启用触控确认门**：防止手柄用户切换到触控时误触
4. **使用 AllowLooping**：允许循环导航提升用户体验
5. **焦点指示器可加动画**：添加 Animator 或 DOTween 实现平滑过渡

## VContainer 集成

### 安装

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.InputSystem.Runtime.Integrations.VContainer;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        var inputSystemInstaller = new InputSystemVContainerInstaller(
            defaultConfigFileName: "input_config.yaml",
            userConfigFileName: "user_input_settings.yaml",
            postInitCallback: async resolver =>
            {
                var inputResolver = resolver.Resolve<IInputPlayerResolver>();
                var player0Input = inputResolver.GetInputPlayer(0);
            }
        );
        inputSystemInstaller.Install(builder);

        builder.Register<PlayerController>(Lifetime.Scoped);
    }
}
```

### 使用模式：注入 IInputPlayerResolver（推荐）

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

        var ctx = new InputContext("PlayerActions", "Gameplay")
            .AddBinding(_input.GetVector2Observable("Move"), new MoveCommand(OnMove))
            .AddBinding(_input.GetButtonObservable("Confirm"), new ActionCommand(OnConfirm));

        _input.PushContext(ctx);
    }

    private void OnMove(Vector2 dir) { /* ... */ }
    private void OnConfirm() { /* ... */ }
}
```

## API 参考

### IInputPlayer

| 方法 / 属性 | 签名 | 描述 |
|-------------|------|------|
| `ActiveContextName` | `ReadOnlyReactiveProperty<string>` | 当前活动上下文名称 |
| `ActiveDeviceKind` | `ReadOnlyReactiveProperty<InputDeviceKind>` | 当前活动设备类型 |
| `OnContextChanged` | `event Action<string>` | 上下文切换事件 |
| `GetButtonObservable` | `(string/int) → Observable<Unit>` | 按钮按下流 |
| `GetVector2Observable` | `(string/int) → Observable<Vector2>` | 二维向量流 |
| `GetScalarObservable` | `(string/int) → Observable<float>` | 标量值流 |
| `GetLongPressObservable` | `(string/int) → Observable<Unit>` | 长按完成事件 |
| `GetLongPressProgressObservable` | `(string/int) → Observable<float>` | 长按进度流（0~1 及 -1 取消） |
| `GetPressStateObservable` | `(string/int) → Observable<bool>` | 按下状态流 |
| `GetChordObservable` | `(string×2/int×2, float) → Observable<Unit>` | 复合键检测流 |
| `PushContext(ctx)` | `void` | 推入栈顶（已存在则移至栈顶） |
| `CaptureContext(ctx)` | `IDisposable` | 临时捕获输入到普通栈之上；释放返回的作用域后恢复 |
| `RemoveContext(ctx)` | `bool` | 按对象引用精确移除 |
| `PopContext()` | `void` | ⚠️ 不建议与 AddTo 混用 |
| `RefreshActiveContext()` | `void` | 刷新当前上下文绑定 |
| `RemoveBindingFromContext(ctx, obs)` | `bool` | 移除指定绑定 |
| `BlockInput()` / `UnblockInput()` | `void` | 阻塞 / 恢复输入 |
| `BlockInputScope()` | `IDisposable` | 作用域输入阻塞，适合 `using` / 异步加载流程 |
| `IsLeftMouseButtonPressed` | `bool` | 鼠标左键轮询 |
| `IsRightMouseButtonPressed` | `bool` | 鼠标右键轮询 |
| `IsMiddleMouseButtonPressed` | `bool` | 鼠标中键轮询 |
| `RebindAction(map, action, old, new)` | `bool` | 运行时重新绑定 |
| `ResetActionBinding(map, action)` | `bool` | 重置单个动作绑定 |
| `ResetAllActionBindings()` | `void` | 重置所有动作绑定 |
| `GetActionBindings(map, action)` | `string[]` | 获取当前有效绑定路径 |

### InputManager

| 方法 | 签名 | 描述 |
|------|------|------|
| `Instance` | `static InputManager` | 单例实例 |
| `IsListeningForPlayers` | `static bool` | 是否正在监听玩家加入 |
| `OnPlayerInputReady` | `event Action<IInputPlayer>` | 玩家输入就绪事件 |
| `OnConfigurationReloaded` | `event Action` | 配置重载事件 |
| `JoinSinglePlayer(id)` | `IInputPlayer` | 同步加入单人 |
| `JoinSinglePlayerAsync(id, timeout)` | `UniTask<IInputPlayer>` | 异步加入单人 |
| `JoinPlayersBatch(ids)` | `List<IInputPlayer>` | 批量加入 |
| `JoinPlayersBatchAsync(ids, timeout)` | `UniTask<List<IInputPlayer>>` | 批量异步加入 |
| `JoinPlayerOnSharedDevice(id)` | `IInputPlayer` | 共享设备加入 |
| `JoinPlayerAndLockDevice(id, device)` | `IInputPlayer` | 锁定设备加入 |
| `GetInputPlayer(id)` | `IInputPlayer` | 获取已加入玩家（null 表示未加入） |
| `RefreshPlayerInput(id)` | `bool` | 刷新玩家输入（触发 OnPlayerInputReady） |
| `StartListeningForPlayers(bool)` | `void` | 开始大厅监听 |
| `StopListeningForPlayers()` | `void` | 停止大厅监听 |
| `ReloadConfigurationAsync()` | `UniTask<bool>` | 热重载配置 |
| `SaveUserConfigurationAsync()` | `UniTask` | 保存用户配置 |
| `Dispose()` | `void` | 释放所有资源 |

### InputContext

| 方法 | 描述 |
|------|------|
| `new InputContext(actionMapName, name?)` | 创建上下文（name 可选，默认 = actionMapName） |
| `AddBinding(Observable<Unit>, IActionCommand)` | 添加按钮绑定 |
| `AddBinding(Observable<Vector2>, IMoveCommand)` | 添加 Vector2 绑定 |
| `AddBinding(Observable<float>, IScalarCommand)` | 添加标量绑定 |
| `AddBinding(Observable<bool>, IBoolCommand)` | 添加布尔绑定 |
| `RemoveBinding(Observable<T>)` | 移除指定绑定 |
| `Dispose()` | 自动从所有活动中移除（设计为供 `AddTo(this)` 调用） |

### 工具集 API

| 类 | 关键方法 | 描述 |
|----|----------|------|
| `InputRecorder` | `RecordAction()` / `StartRecording()` / `StopRecording()` | 录制回放工具 |
| `InputRecording` | `CreateReplayObservable()` / `CreateReplayVector2Observable()` / `CreateReplayFloatObservable()` | 重放为 Observable |
| `InputSequenceMatcher` | `DetectSequence(recording, steps)` | 序列匹配检测 |
| `InputGestureRecognizer` | `DetectGesture(recording, gesture)` | 手势识别 |
| `InputTimingValidator` | `ValidateTiming(recording, beats, map, action)` | 反作弊时序验证 |
| `InputTimingValidator` | `ValidateTiming(recording, beats, map, action)` | 反作弊时序验证 |
| `InputBindingValidator` | `DetectConflicts(config)` / `FormatConflictsReport(conflicts)` | 绑定冲突检测 |

## 游戏类型适配指南

| 游戏类型 | 上下文栈 | Chord 复合键 | 录制回放 | 手势识别 | 反作弊 |
|----------|:---:|:---:|:---:|:---:|:---:|
| FPS/TPS | ✓ | ✓ | — | — | — |
| 格斗游戏 | ✓ | ✓ | ✓ | ✓ | — |
| 音游 | ✓ | — | ✓ | — | ✓ |
| 动作 RPG | ✓ | ✓ | ✓ | — | — |
| 平台跳跃 | ✓ | ✓ | — | — | — |
| 本地多人 | ✓ | ✓ | — | — | — |

**说明：**

- **上下文栈**：所有游戏类型的基础功能，管理 Gameplay / UI / 暂停等状态切换
- **Chord 复合键**：快速格斗（A+B 组合技）、FPS（Shift+W 冲刺）、平台跳跃（方向+跳跃 跳墙蹬）
- **录制回放**：格斗游戏练习模式、音游回放、动作 RPG 招数回顾
- **手势识别**：格斗游戏出招检测（波动拳、升龙拳等）
- **反作弊**：音游排位赛防止自动演奏外挂
