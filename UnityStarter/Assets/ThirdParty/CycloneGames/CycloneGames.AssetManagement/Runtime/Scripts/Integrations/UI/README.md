# UI Integration (UI Prefab Loader)

- Create a `UIPrefabRegistry` asset (key -> location mapping)
- Assign keys (e.g., "MainMenu", "HUD") to YooAsset locations
- At boot, set a default package (via DI or AssetManagementLocator)

```csharp
using CycloneGames.AssetManagement;
using CycloneGames.AssetManagement.Integrations.UI;
using CycloneGames.AssetManagement.Integrations.Common;

// During boot
IAssetPackage pkg = /* resolve or build */ null;
AssetManagementLocator.DefaultPackage = pkg;

// When opening a UI panel
var registry = Resources.Load<UIPrefabRegistry>("UIPrefabRegistry");
using var loader = new UIPrefabLoaderService(AssetManagementLocator.DefaultPackage, registry);
var panel = loader.LoadAndInstantiate("MainMenu");
```

Notes:
- Loader caches only the last handle; if you need multiple panels cached, keep multiple loader instances or extend it.
- Prefer DI to inject `IAssetPackage` and registry where possible.