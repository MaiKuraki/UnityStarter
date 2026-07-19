# 快速上手

[English | 简体中文](GettingStarted.md)

相关：[配置指南](Configuration.SCH.md) | [Runtime 指南](RuntimeGuide.SCH.md) | [模块参考](../README.SCH.md)

本指南从配置 authoring 开始，完成一个 active player 和一个 Gameplay context。第一个接入示例使用 serialized `TextAsset`，这样可以先理解输入模型，再决定产品采用哪种 storage policy。

## 前置条件

- 项目已安装 Unity Input System package，并包含本模块。
- consumer assembly 已引用 `CycloneGames.InputSystem.Runtime`。
- 场景运行时存在 selected control scheme 所需的设备。
- 配置规模保持在模块 validation budget 内。

## 第一步：理解最小模型

一份可用配置需要四个 identity：

1. Player slot，例如 player `0`。
2. Context，例如 `Gameplay`。
3. Action map，例如 `PlayerActions`。
4. Action，例如 `Confirm` 或 `Move`。

完整 runtime identity 是 `context/actionMap/action`。Generated action ID 是该 identity 的确定性 hash。

## 第二步：创建配置

打开 `Tools > CycloneGames > Input System Editor`，选择 `Generate Default`。生成的 working copy 包含两个 player slot、keyboard/mouse 和 gamepad scheme、`Gameplay` context、`Move` 与 `Confirm`。

第一个接入也可以使用以下精简配置：

```yaml
schemaVersion: 1
schemaFingerprint: ""
playerSlots:
  - playerId: 0
    controlSchemes:
      - name: KeyboardMouse
        bindingGroup: KeyboardMouse
        deviceRequirements:
          - controlPath: "<Keyboard>"
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: PlayerActions
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: KeyboardMouse
            deviceBindings:
              - "<Keyboard>/enter"
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
```

将它保存为项目 `TextAsset`，例如 `Assets/Game/Input/input_config.yaml`。`TextAsset` 适合 scene-owned setup 与测试。StreamingAssets 是由产品 composition root 选择的可选 source。

## 第三步：初始化一个 manager

Owner 负责构造、初始化并 dispose manager：

```csharp
using CycloneGames.InputSystem.Runtime;
using UnityEngine;

public sealed class InputBootstrap : MonoBehaviour
{
    [SerializeField] private TextAsset _configuration;

    private InputManager _manager;
    private IInputPlayer _player;
    private InputContext _gameplay;

    private void Awake()
    {
        _manager = new InputManager();
        InputManagerInitializationResult result =
            _manager.InitializeWithResult(_configuration.text);

        if (!result.IsSuccess)
        {
            Debug.LogError($"Input initialization failed: {result.Status}: {result.Message}", this);
            enabled = false;
            return;
        }

        _player = _manager.JoinSinglePlayer(0);
        if (_player == null)
        {
            Debug.LogError("Player 0 could not acquire a declared control scheme.", this);
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        _gameplay?.Dispose();
        _manager?.Dispose();
    }
}
```

同一个 input session 只应有一个 manager owner，不要为同一 session 创建第二个 manager。

## 第四步：通过 context 绑定 action

Context 持有 active command subscription。在 player 创建后加入：

```csharp
_gameplay = new InputContext("PlayerActions", "Gameplay")
    .AddBinding(
        _player.GetButtonObservable("Gameplay", "PlayerActions", "Confirm"),
        new ActionCommand(OnConfirm));

_player.PushContext(_gameplay);
```

Command target 保持为普通产品代码：

```csharp
private void OnConfirm()
{
    Debug.Log("Confirm received.");
}
```

Dispose context 会将它从所有正在使用它的 player 上移除，并释放 subscription。

## 第五步：使用 generated identity

在 Input System Editor 中：

1. 选择 `Assets` 下的 code output folder。
2. 输入 generated namespace。
3. 选择 `Save User + Generate Code`，或在保存目标配置后单独生成。
4. Review 并 compile 生成的 `InputActions.cs`。

Call site 随后可以使用 stable generated ID：

```csharp
using CycloneGames.InputSystem.Runtime.Generated;

_gameplay = new InputContext(
        InputActions.ActionMaps.PlayerActions,
        InputActions.Contexts.Gameplay)
    .AddBinding(
        _player.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(OnConfirm));
```

Context、action-map 或 action 名称变化后应重新生成。

## 第六步：选择配置 owner

| 模式 | Source | 适用场景 |
| --- | --- | --- |
| Scene-owned | Serialized `TextAsset` | 小型项目、显式 scene setup、测试 |
| Packaged file | `UriInputConfigurationSource` 与 StreamingAssets | 随 build 提供的产品配置 |
| User settings | `IInputConfigurationStore` | 本地 binding 与产品管理的输入设置 |
| Asset package | 产品 adapter | 下载内容或 package-owned configuration |
| In-memory | 产品 source implementation | 测试、server tool、generated configuration |

Foundation project 不要求存在 project-level StreamingAssets file。

## 第七步：验证首次接入

1. 在 Input System Editor 中加载 YAML，确认状态为 `VALID`。
2. 连接所需设备并进入 Play Mode。
3. 确认 manager 初始化成功。
4. 确认 player `0` 只 join 一次。
5. 触发配置的 control，确认 command 被执行。
6. 销毁 bootstrap object，确认 context 与 manager 被 dispose。

字段语义参见[配置指南](Configuration.SCH.md)，context、多人、持久化与生产 ownership 参见[Runtime 指南](RuntimeGuide.SCH.md)。
