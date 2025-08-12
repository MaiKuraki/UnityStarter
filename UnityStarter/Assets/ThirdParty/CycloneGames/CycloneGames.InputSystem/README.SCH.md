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
- 热插拔：自动配对所需设备

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
          - type: Button
            action: Confirm
            deviceBindings:
              - "<Gamepad>/buttonSouth"
              - "<Keyboard>/space"
```

## 最小示例（新手友好）

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
      .AddBinding(_input.GetButtonObservable("PlayerActions", "Confirm"), new ActionCommand(OnConfirm));

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
}
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

## API 概览

- IInputService
  - `ReadOnlyReactiveProperty<string>` ActiveContextName；`event OnContextChanged`
  - GetVector2Observable(map, action) | GetVector2Observable(action)
  - GetButtonObservable(map, action) | GetButtonObservable(action)
  - GetScalarObservable(map, action) | GetScalarObservable(action)
  - RegisterContext, PushContext, PopContext, BlockInput, UnblockInput
