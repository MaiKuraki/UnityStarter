# UIFramework 示例

[English](README.md) | 简体中文

这些示例展示不使用 Provider、直接引用 `UIWindowConfiguration` 的显式组合方式。示例只依赖 `UniTask` 与 `CycloneGames.UIFramework.Runtime`。

## 文件说明

| 文件 | 用途 |
| --- | --- |
| `SampleScene.unity` | 可直接运行的 Classic Window 场景 |
| `UIFrameworkSampleBootstrap.cs` | 创建 `UIService`、打开直接配置并等待 Shutdown |
| `UIFrameworkMvpSampleBootstrap.cs` | 使用实例级 `UIPresenterBinder` 的可选组合 |
| `UIWindow_SampleUI.cs` | Window、强类型 Sample View、Listener 与 Presenter |
| `DynamicAtlasLeaseSample.cs` | 使用稳定 key 与显式 Lease 释放的有界 Dynamic Atlas 所有权示例 |
| `Resources/UIWindow_SampleUI.prefab` | 示例窗口 Prefab |
| `Resources/UIWindow_SampleUI_Config.asset` | 稳定 ID 为 `UIWindow_SampleUI` 的配置 |

Sample 程序集的 `autoReferenced` 为 `false`，不会成为无关项目程序集的默认依赖。

## 运行 Classic 示例

1. 从 Unity 项目根目录打开 `Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/Samples/SampleScene.unity`。
2. 选择 `Boot` GameObject。
3. 确认 `UI Root` 引用了场景中的 `UIRoot` 组件。
4. 确认 `First Window Configuration` 引用了 `UIWindow_SampleUI_Config`。
5. 进入 Play Mode。

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

## 运行 Dynamic Atlas Lease 示例

Dynamic Atlas 示例是独立组件，不修改 `SampleScene.unity`：

1. 创建或选择一个包含 `Image` 组件的 Canvas。
2. 在 GameObject 上添加 `DynamicAtlasLeaseSample`。
3. 指定目标 `Image` 与一个矩形源 `Sprite`。
4. 源 Sprite 属于 `SpriteAtlas` 时，关闭该图集的 rotation 与 Tight Packing。
5. 保留默认 stable key，或替换为带命名空间的内容 identity。
6. 进入 Play Mode，然后 Disable 并重新 Enable 组件，以检查释放与重新获取。
7. 打开 `Tools > CycloneGames > UI Framework > Dynamic Atlas Debugger`，检查页面、Lease 引用数、复制路径与纹理估算字节数。

该组件拥有一个 512 像素页面，纹理估算预算为 2 MiB。它在 `OnEnable` 获取 `DynamicAtlasSpriteLease`，在 `OnDisable` 清空 `Image` 并 Dispose Lease，最后在 `OnDestroy` Dispose 自己拥有的 Service。它直接获取 `Sprite`，因此不需要 location loader。示例不使用全局 Manager 或隐藏缓存。

需要 Scene Host 组合时，可以添加 `DynamicAtlasManager`，并通过统一风格的 Inspector 验证容量、页面内存预算、Active BuildTarget 上下文，以及仅在运行时提供的 loader/unloader 所有权组合。Inspector 不会根据 BuildTarget 名称推断目标设备 Copy 支持。

合批约束、复制路径、Retention、诊断、容量规划与目标设备验证详见[动态 UI 图集指南](../Documents~/DynamicAtlas.SCH.md)。

## 扩展示例

- 配置已由 Scene 或 Composition Asset 引用时，保留 `PrefabReference` 并调用 `OpenAsync(configuration)`。
- 使用运行时内容时，构造 `IUIWindowAssetProvider` 并调用 `OpenAsync(windowId)`。
- 需要 Opener/Back 关系时，通过 `UIServiceOptions.NavigationService` 添加 `UINavigationService`。
- 在打开任何窗口前，为 MVP、DI、分析或无障碍添加彼此独立的 Binder。
- Composition root 始终负责等待 `ShutdownAsync`。

## 验证边界

运行 Window 场景和 Dynamic Atlas 组件只能检查它们在当前 Editor 中的聚焦行为，不能作为 Player、IL2CPP、目标平台、长期运行、DrawCall 降低或性能证据。其他范围应按包 README 与 Dynamic Atlas 指南中的验证矩阵执行。
