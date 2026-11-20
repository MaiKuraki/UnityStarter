# UIFramework 示例

[English](README.md) | 简体中文

这些示例展示不使用 Provider、直接引用 `UIWindowConfiguration` 的显式组合方式。示例脚本只依赖 `UniTask` 与 `CycloneGames.UIFramework.Runtime`。可运行场景使用显式相机引用：`UIFramework` Prefab 持有专用的 URP Overlay `UICamera`，Scene 持有的 Base Camera 则在自己的 URP camera stack 中序列化引用该相机。该组合不使用全局相机 service 或运行时相机查找。

## 文件说明

| 文件 | 用途 |
| --- | --- |
| `SampleScene.unity` | 使用显式 URP Base-to-UI camera stack 的可运行 Classic Window 场景 |
| `UIFrameworkSampleBootstrap.cs` | 创建 `UIService`、打开直接配置并等待 Shutdown |
| `UIFrameworkMvpSampleBootstrap.cs` | 使用实例级 `UIPresenterBinder` 的可选组合 |
| `UIWindow_SampleUI.cs` | Window、强类型 Sample View、Listener 与 Presenter |
| `Resources/UIWindow_SampleUI.prefab` | 示例窗口 Prefab |
| `Resources/UIWindow_SampleUI_Config.asset` | 稳定 ID 为 `UIWindow_SampleUI` 的配置 |

Sample 程序集的 `autoReferenced` 为 `false`，不会成为无关项目程序集的默认依赖。

## 运行 Classic 示例

1. 从 Unity 项目根目录打开 `Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/Samples/SampleScene.unity`。
2. 选择 `Boot` GameObject。
3. 确认 `UI Root` 引用了场景中的 `UIRoot` 组件。
4. 确认 `First Window Configuration` 引用了 `UIWindow_SampleUI_Config`。
5. 选择 Scene 持有的 Base Camera，确认其 URP camera stack 只包含一次 `UIFramework` Prefab 的 `UICamera`。
6. 进入 Play Mode。

Base Camera 由 Scene 持有；Overlay `UICamera` 由 `UIFramework` Prefab 持有，其 `UIRoot` 与 Root Canvas 都显式引用该相机。Base-to-Overlay stack 关系直接序列化在场景中；Bootstrap 不使用 `Camera.main`、场景查找、全局相机 singleton 或 service locator。把示例适配到其他渲染管线时，应使用该管线对应的显式相机组合替换此处的场景专用配置。

Bootstrap 会：

1. 创建有界的 `UIServiceOptions`；
2. 使用显式 Root 且不提供 Asset Provider 来构造 `UIService`；
3. 等待 `OpenAsync(firstWindowConfiguration, token)`；
4. 保持运行，直到 `GetCancellationTokenOnDestroy()` 被取消；
5. 等待 `ShutdownAsync(UIShutdownMode.Immediate, CancellationToken.None)`。

生命周期方法不使用 `async void`。

## 运行 MVP 组合

在独立场景中使用，或替换 `Boot` 上的组件：

1. 移除 `UIFrameworkSampleBootstrap`；
2. 添加 `UIFrameworkMvpSampleBootstrap`；
3. 指定同一个 `UIRoot` 与 `UIWindow_SampleUI_Config`；
4. 进入 Play Mode。

MVP Bootstrap 创建一个 `UIPresenterBinder`，为 `UIWindow_SampleUI` 注册 `SampleUIPresenter`，把 Binder 传入 `UIService` 构造函数，并使用相同的取消与 Shutdown 所有权。

可以把 `UIWindow_SampleUI.UICmd_PrimaryAction` 直接连接到 Button 的 `OnClick`。View 通过 `ISampleUIViewListener` 转发命令，Presenter 不需要全局查找即可处理。

## 扩展示例

- 配置已由 Scene 或 Composition Asset 引用时，保留 `PrefabReference` 并调用 `OpenAsync(configuration)`。
- 使用运行时内容时，构造 `IUIWindowAssetProvider` 并调用 `OpenAsync(windowId)`。`DelegatedUIWindowAssetProvider` 是零依赖参考实现：由 composition root 注入两个加载委托。使用私有资源管理框架的宿主可以直接使用它，或实现同样两个方法的契约。
- 需要 Opener/Back 关系时，通过 `UIServiceOptions.NavigationService` 添加 `UINavigationService`。
- 在打开任何窗口前，为 MVP、DI、分析或无障碍添加彼此独立的 Binder。
- Composition root 始终负责等待 `ShutdownAsync`。

## 验证边界

运行 Window 场景只能检查它在当前 Editor 中的聚焦行为，不能作为 Player、IL2CPP、目标平台或长期运行的性能证据。其他范围应按包 README 中的验证矩阵执行。
