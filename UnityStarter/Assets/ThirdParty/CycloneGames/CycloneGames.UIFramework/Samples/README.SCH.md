# UIFramework 示例说明

[English](README.md) | 简体中文

本示例演示如何通过 `CycloneGames.AssetManagement` 抽象来使用 `CycloneGames.UIFramework`。

注意：本示例依赖 Unity `Resources.Load` 作为 `IAssetPackage` 的实现。如果你的项目使用其他资源管理系统（如 `Addressable`, `YooAsset`），请参考 `CycloneGames.AssetManagement/README.md` 中的介绍，使用你自己的适配器并在启动阶段设置：

## 目录说明

- `UIFrameworkSampleBootstrap.cs`：初始化 `UIService` 并打开首个 UI 窗口；支持自动创建 `Addressables` 包（`autoSetupAddressablesPackage`）。
- `UIAssetFactory.cs`：示例用的 `IAssetPathBuilderFactory` 实现。
- `UIWindow_SampleUI.asset` + `UIWindow_SampleUI.prefab` + `UIWindow_SampleUI.cs`：示例窗口配置与预制体。
- `SampleScene.unity`：包含 `UIRoot` 与引导脚本的示例场景。

## 运行步骤

1. 打开 `CycloneGames.UIFramework/Samples/SampleScene.unity`。
2. 选中场景中的 `UIFrameworkSampleBootstrap`，确认：
   - `firstWindowName` 填写有效的 `UIWindowConfiguration` 名称（如 `UIWindow_SampleUI`）。
3. 点击 Play，示例会初始化 UIService 并打开首个窗口。

## 使用其他资源系统

如使用 `YooAsset` 或`Addressable` 或其他系统，请实现 `IAssetPackage`/`IAssetHandle<T>` 的适配器，并在启动时赋值到定位器：

```csharp
AssetManagementLocator.DefaultPackage = myCustomPackage;
```

`UIFramework` 仅依赖抽象接口，不直接调用 `Addressables`/`YooAsset` API。

## 备注

- 如需过渡动画，可通过 `UIManager.Initialize(..., package, driver)` 注入 `IUIWindowTransitionDriver`（例如基于 `LitMotion` 的驱动）。
- 请确保场景中的 `UIRoot`、`UILayer` 配置与各窗口配置引用的层名一致。
- 如果移动端需要安全区，请在 `CycloneGames.Utility.Runtime` 中使用 `AdaptiveSafeAreaFitter`
