# InputSystem 示例

[English](README.md) | [快速上手](../Documents~/GettingStarted.SCH.md) | [配置指南](../Documents~/Configuration.SCH.md) | [Runtime 指南](../Documents~/RuntimeGuide.SCH.md)

这是一个按需启用的完整本地输入示例，演示加载 YAML 配置、创建 player、通过 context 路由 action、响应设备变化，以及释放全部受管资源。示例 assembly 不会被自动引用，scene 不在 Build Settings 中，fixture 也不会自动复制到 `StreamingAssets`。

## 学习目标

建议按以下顺序阅读与运行：

1. 使用已验证配置运行一个键盘 player。
2. 比较锁定设备与共享设备两种 join policy。
3. 从 `OnPlayerInputReady` 跟踪 input player 到场景 GameObject 的创建过程。
4. 在 gameplay context 中把 generated action identity 绑定到 command。
5. 验证 cancellation、subscription cleanup、context disposal 与 manager shutdown。
6. 让单个 player 在键鼠与手柄之间切换，无需重新 join。

## 文件内容

| 文件 | 职责 |
| --- | --- |
| `SampleScene.unity` | 配置 bootstrap、spawn point 与 player prefab。 |
| `GameInitializer_Sample.cs` | 持有 initialization、join policy、subscription、spawned player 与 shutdown。 |
| `SimplePlayerController.cs` | 接收 command，并持有自己的 runtime material instance。 |
| `Generated/InputActions.cs` | 从示例配置生成的确定性常量。 |
| `Fixtures/input_config.txt` | 仅供运行示例时使用的 versioned authoring fixture。 |
| `Fixtures/input_config_mixed.txt` | 单人 fixture，同时配对键盘、鼠标与手柄。 |

## 运行示例

1. 打开 `Samples/SampleScene.unity`。
2. 选中 `InputManagerSample`，确认 `Configuration` 与 `Mixed Configuration` 已引用 `Samples/Fixtures` 下的 fixture。
3. 选择一个 `Startup Mode`。
4. 确认 player prefab、spawn point 与 color 已赋值。
5. 连接所选模式需要的设备并进入 Play Mode。

场景通过 `TextAsset` 引用携带配置，不需要往 `StreamingAssets` 复制任何文件。产品也可以改用 `StreamingAssets` 文件、Addressables、remote source，或其他有预算边界的 `IInputConfigurationSource`。用户覆盖设置仍走 `FileInputConfigurationStore`，写入 `persistentDataPath`。

## 阶段 1：阅读配置

fixture 定义两个 control scheme、一个 `PlayerActions` action map，以及一个 `Gameplay` context。键盘移动使用显式 `2DVector` composite parts。startup mode 决定连接的设备是锁定给单个 player，还是由多个 player 共享。

示例使用以下 generated identity：

```csharp
InputActions.Contexts.Gameplay
InputActions.ActionMaps.PlayerActions
InputActions.Actions.Gameplay_Move
InputActions.Actions.Gameplay_Confirm
```

修改 context name、action-map name 或 action name 后，需要重新生成 `InputActions.cs`。

## 阶段 2：加载并初始化

`GameInitializer_Sample` 从 `TextAsset` 引用读取 authoring 配置，用户覆盖设置则由 `persistentDataPath` 上的 store 管理：

```csharp
var userStore = new FileInputConfigurationStore(
    Application.persistentDataPath);

InputSystemLoadResult result = await InputSystemLoader.LoadAndInitializeAsync(
    new TextAssetInputConfigurationSource(_configuration),
    _configuration.name,
    userStore,
    "user_input_settings.yaml",
    InputManager.Instance,
    false,
    cancellationToken);

if (!result.IsSuccess)
{
    throw new InvalidOperationException(result.Error);
}
```

只有加载成功后，bootstrap 才继续执行。cancellation token 归 bootstrap GameObject 所有，因此场景销毁会取消尚未完成的工作。

## 阶段 3：选择 Join Policy

| Startup mode | 结果 | 常见用途 |
| --- | --- | --- |
| `AutoJoinLockedSinglePlayer` | 创建 player `0`，并锁定选中的 scheme。 | 单人 gameplay。 |
| `AutoJoinSharedKeyboard` | 在共享设备上创建 player `0` 与 `1`。 | 双人键盘测试。 |
| `LobbyWithDeviceLocking` | 监听 join action，并锁定参与设备。 | 本地多人 lobby。 |
| `LobbyWithSharedDevices` | 监听 join action，同时允许共享设备。 | 共享设备的聚会控制。 |
| `AutoJoinMixedDevices` | 创建 player `0`，每个被引用的 layout 配对一台设备，且不激活 control scheme。 | 单个 player 在手柄与键鼠之间切换。 |
| `AutoJoinKeyboardMouseVsGamepad` | 用键鼠创建 player `0`，用剩余手柄创建 player `1`。 | 混合设备类型的本地多人。 |

示例把 join failure 视为 initialization failure。产品可以提供有次数限制的重试 UI、设备选择界面或显式取消操作。

**配置来源隔离。** 每个 startup mode 派生各自的 user configuration key（`user_input_settings_<Mode>.yaml`），并通过 `InputSystemBootstrapOptions` 设置 `persistDefaultToUser: false`。因此每次运行真正生效的都是 Inspector 里选中的 `TextAsset`。此前所有模式共用同一个 key，而六参数 loader 重载会隐式持久化 authored fixture：一旦保存过 mixed 配置，切回 `AutoJoinLockedSinglePlayer` 仍会加载 mixed 文件，导致两种模式看起来完全一样。Bootstrap 会在 Console 输出 `mode`、`fixture`、`userKey` 和 `InputSystemLoadResult.Status`，让生效来源可见。

## 阶段 4：在 Player Ready 时创建对象

player 获得 action asset 后，manager 触发 `OnPlayerInputReady`。handler 在实例化 prefab 前验证 player ID 与序列化引用。dictionary 防止重复 ready notification 产生重复 player object。

```csharp
InputManager.Instance.OnPlayerInputReady += HandlePlayerInputReady;

private void HandlePlayerInputReady(IInputPlayer inputPlayer)
{
    int playerId = inputPlayer.PlayerId;
    // Validate references, instantiate once, then create the context.
}
```

presentation object 的创建应留在 `InputPlayer` 外部。input player 持有输入状态；scene owner 决定由哪个 GameObject 表现该 player。

## 阶段 5：通过 Context 路由 Action

示例为每个 player 创建 gameplay context，并将 typed observable 映射到 command：

```csharp
var gameplayContext = new InputContext(
        InputActions.ActionMaps.PlayerActions,
        InputActions.Contexts.Gameplay)
    .AddBinding(
        inputPlayer.GetVector2Observable(InputActions.Actions.Gameplay_Move),
        new MoveCommand(controller.OnMove))
    .AddBinding(
        inputPlayer.GetButtonObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(controller.OnConfirm))
    .AddBinding(
        inputPlayer.GetLongPressObservable(InputActions.Actions.Gameplay_Confirm),
        new ActionCommand(controller.OnConfirmLongPress));

gameplayContext.AddTo(controller.destroyCancellationToken);
inputPlayer.PushContext(gameplayContext);
```

这里分离了三项职责：

- YAML 描述可用的 action 与 binding。
- `InputContext` 决定当前状态下哪些 action 生效。
- Command 调用 gameplay 或 presentation 代码，不让后者直接依赖 Unity Input System callback。

实现 pause menu 时，可以创建高优先级 menu context，在菜单打开时 push，在菜单关闭时 dispose 或 pop。

## 阶段 6：观察活动设备

`ActiveDeviceKind` 可让提示与图标跟随 player 最近使用的设备：

```csharp
inputPlayer.ActiveDeviceKind
    .Subscribe(kind => UpdatePromptSet(kind))
    .AddTo(controller.destroyCancellationToken);
```

示例改为给 player 对象染色而不是切换提示集，这样接管在场景里就能看到：

```csharp
if (controller != null) controller.ApplyDeviceTint(kind);
```

只有设备被配对，接管才能生效。锁定 scheme 时只配对该 scheme 需要的设备，手柄无论 binding 怎么写都驱动不了任何 action。在 player slot 上设置 `pairAllMatchingDevices: true` 会为每个被引用的 layout 认领一台设备并保持 scheme 未激活，`Fixtures/input_config_mixed.txt` 用的就是这种方式。每个 player 仍会把 action asset 关联到自己的 `InputUser`，因此 mixed player 的 action 只作用于自己的配对设备白名单：键鼠和手柄都可用，但属于其他 player 的设备不会串进来。`maxDevicesPerLayout: 1` 为其他 player 留出剩余设备，`deviceSwitchDebounceMs: 120` 抑制快速交替时的提示抖动。

**为什么 SingleLock 与 Mixed 现在表现不同。** `AutoJoinLockedSinglePlayer` 加载 `Fixtures/input_config.txt`，激活 `KeyboardMouse` scheme，其 action asset 只能看到配对的键盘和鼠标；`AutoJoinMixedDevices` 加载 `Fixtures/input_config_mixed.txt`，不激活任何 scheme，其 action asset 能看到键盘、鼠标和手柄。同时插着键鼠和手柄时，只有 mixed 模式下按手柄才会驱动 player `0`。如果两种模式看起来仍然一样，请查看 bootstrap 日志：状态为 `SuccessFromUserConfiguration` 说明该模式的 user override 覆盖了 fixture。

`ActiveDeviceKind` 属于 presentation state。设备所有权与 join policy 仍由 manager 负责。

## 阶段 7：释放受管状态

shutdown 按照与创建相反的顺序执行：

1. 取消订阅 `OnPlayerInputReady`。
2. 停止 join listening。
3. 销毁 spawned player object；其 cancellation token 会 dispose context 与 subscription。
4. Dispose 当前 owner 持有的 manager。
5. 清理 static ownership marker。

`SubsystemRegistration` 会在进入新的 Play session 时重置示例 static state，也适用于关闭 Domain Reload 的配置。

## 持久化

示例通过 `FileInputConfigurationStore` 持有 `Application.persistentDataPath` 下的 `user_input_settings.yaml`。该文件属于用户本机，不应提交。user key 缺失时会选用已验证的 default fixture，并可根据所选加载策略保存到 user store。无效 user content 会保留用于诊断，本次 session 仍可使用已验证的 default configuration。

只有在明确重置示例时才删除该用户文件。生产应用应通过 settings 或 save service 暴露 reset，并定义 backup、retention 与 recovery 行为。

## 练习

### 入门

1. 将 `Gameplay/PlayerActions/Confirm` 从 `<Keyboard>/enter` 改为 `<Keyboard>/space`。
2. 打开 Input System Editor，加载复制后的配置，完成验证并生成常量。
3. 进入 Play Mode，分别验证 short press 与 long press。

### 进阶

1. 新增包含 `Navigate`、`Submit` 与 `Cancel` action 的 `Menu` context。
2. 生成常量，并创建 menu `InputContext`。
3. 将 menu context push 到 gameplay 之上，在菜单关闭时释放。

### 深入

1. 新增执行 interactive rebind 的 settings screen。
2. 通过产品持有的 store 导出 manager binding profile。
3. 重启 session，在 join player 前导入 profile，并验证 reset 行为。
4. 为 layout 缺失、path 无效与 profile rejection 增加有界 diagnostic。

## 验证

1. 运行 `CycloneGames.InputSystem.Tests.Editor` EditMode tests。
2. 分别在开启和关闭 Domain Reload 时运行 scene。
3. 使用相应设备执行每个 startup mode。
4. 重复触发 player-ready refresh，确认每个 player ID 只有一个 scene object。
5. 在加载期间销毁 bootstrap，确认没有遗留 player 或 subscription。
6. 在 Unity Input Debugger 中验证 keyboard、mouse 与 gamepad path。
7. 同时接上键鼠与手柄，在 `AutoJoinLockedSinglePlayer` 与 `AutoJoinMixedDevices` 之间切换。确认 bootstrap 日志报告不同的 fixture，且只有 mixed 模式为 player `0` 解析到 gamepad control。
8. 至少接一个手柄运行 `AutoJoinKeyboardMouseVsGamepad`，确认两个 player 生成且设备集合不相交。

这些检查覆盖 Editor workflow 与示例 ownership。Target Player、IL2CPP、console、reconnect、suspend/resume 与长时间设备行为需要在目标平台单独验证。
