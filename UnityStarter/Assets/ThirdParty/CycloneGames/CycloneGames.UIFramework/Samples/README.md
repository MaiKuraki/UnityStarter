# UIFramework Samples

English | [简体中文](README.SCH.md)

This sample demonstrates using `CycloneGames.UIFramework` with the `CycloneGames.AssetManagement` abstraction.

Important: This sample depends on Unity Addressables to provide an `IAssetPackage` implementation. If you use other systems (e.g., YooAsset), please refer to `CycloneGames.AssetManagement/README.md` for integrations and set your own package via `AssetManagementLocator.DefaultPackage`.

## Contents

- `AddressablesAssetHandle.cs`: Minimal Addressables adapter implementing `IAssetPackage` and `IAssetHandle<T>` for the sample.
- `UIFrameworkSampleBootstrap.cs`: Bootstraps `UIService` and opens the first window. Supports auto-setup of Addressables-based package via `autoSetupAddressablesPackage`.
- `UIAssetFactory.cs`: Simple `IAssetPathBuilderFactory` implementation used by the sample.
- `UIWindow_SampleUI.asset` + `UIWindow_SampleUI.prefab` + `UIWindow_SampleUI.cs`: A basic window configuration and prefab for demonstration.
- `SampleScene.unity`: Scene including `UIRoot` and the bootstrap component.

## How to Run

1. Ensure Addressables is installed and configured in your project.
2. Open `CycloneGames.UIFramework/Samples/SampleScene.unity`.
3. Select the `UIFrameworkSampleBootstrap` object and verify:
   - `firstWindowName` matches an available `UIWindowConfiguration` name (e.g., `UIWindow_SampleUI`).
   - `autoSetupAddressablesPackage` is enabled (or set `AssetManagementLocator.DefaultPackage` manually in your own boot script).
4. Press Play. The sample will initialize `UIService` and open the first window.

## Using Other Asset Systems

If you use YooAsset or another system, create an adapter that implements `IAssetPackage`/`IAssetHandle<T>` and assign it during boot:

```csharp
AssetManagementLocator.DefaultPackage = myCustomPackage; // your adapter
```

UIFramework only depends on the abstraction; no direct Addressables/YooAsset API usage.

## Notes

- For transitions, inject an `IUIWindowTransitionDriver` (e.g., LitMotion driver) via `UIManager.Initialize(..., package, driver)`.
- Ensure your `UIRoot` and `UILayer` setup matches the layer names referenced by window configurations.
- If you need safe area support on mobile, use `AdaptiveSafeAreaFitter` from `CycloneGames.Utility.Runtime`.
