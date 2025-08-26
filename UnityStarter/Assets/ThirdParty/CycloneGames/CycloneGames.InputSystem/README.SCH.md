# CycloneGames.InputSystem

>注意：CycloneGames.InputSystem 代码由作者编写，文档由AI辅助编写

一个基于 Unity 新输入系统的响应式输入高级封装：支持上下文栈（Action Map）、本地多人、设备锁定、基于 YAML 的可视化配置与编辑器工具。

[English](./README.md) | 简体中文

## 功能特点

- 上下文栈：Push/Pop 切换，按上下文启用对应 Action Map
- 多人加入模式：单人锁定、共享设备、监听绑定（可开启设备锁定）
- YAML 配置，显式声明动作类型（Button / Vector2 / Float）
- 编辑器窗口：生成/加载/保存配置；绑定路径下拉常量选择
- 响应式 API（R3）：为每个动作提供 Observable 流
  - 按钮：短按事件与可选的长按事件
  - 浮点（如手柄 Trigger）：可选长按（带阈值）
- 热插拔：自动配对所需设备
  - 设备检测：获取最近一次活跃输入设备类型（键鼠/手柄/其他）

## 安装依赖

- Unity 2022.3+
- Unity Input System
- 依赖：UniTask、R3、VYaml、CycloneGames.Utility、CycloneGames.Logger

## 快速上手

1) 生成默认配置：Tools → CycloneGames → Input System Editor → Generate Default Config
2) 启动时初始化：

```csharp
var defaultUri = FilePathUtility.GetUnityWebRequestUri("input_config.yaml", UnityPathSource.StreamingAssets);
var userUri = FilePathUtility.GetUnityWebRequestUri("user_input_settings.yaml", UnityPathSource.PersistentData);
await InputSystemLoader.InitializeAsync(defaultUri, userUri);
```

1) 加入并设置上下文：

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

- Button 显示 “Long Press (ms)”；Float 显示 “Long Press (ms)” 与 “Long Press Threshold (0-1)”。
- 非 Button/Float 类型在保存时会忽略 `longPressMs`。
- Vector2 来源：可用 Mouse Delta、摇杆、DPad 或 2DVector 复合。常量位于 `InputBindingConstants.Vector2Sources`。
- 鼠标 Delta 在选择器中显示为 “Mouse/Delta(Vector2)”，实际绑定路径为 `<Mouse>/delta`。

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

- IInputService
  - `ReadOnlyReactiveProperty<string>` ActiveContextName；`event OnContextChanged`
  - `ReadOnlyReactiveProperty<InputDeviceKind>` ActiveDeviceKind（键鼠/手柄/其他）
  - GetVector2Observable(map, action) | GetVector2Observable(action)
  - GetButtonObservable(map, action) | GetButtonObservable(action)
  - GetLongPressObservable(map, action) | GetLongPressObservable(action)
  - GetPressStateObservable(map, action) | GetPressStateObservable(action)
  - GetScalarObservable(map, action) | GetScalarObservable(action)
  - RegisterContext, PushContext, PopContext, BlockInput, UnblockInput
