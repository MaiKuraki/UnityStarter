# VContainer Integration

Register `IAssetModule` and initialize an `IAssetPackage` via `AssetManagementVContainerInstaller` or your own LifetimeScope.

- Ensure VContainer is in the project (macro auto-defined: `VCONTAINER_PRESENT`).
- Option A: Add `AssetManagementVContainerInstaller` (LifetimeScope) to a scene and configure fields.
- Option B: Configure in your own LifetimeScope (example below).

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.AssetManagement;
using YooAsset;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // Register module
        builder.Register<IAssetModule, YooAssetModule>(Lifetime.Singleton);

        // Initialize package after container build (example)
        builder.RegisterBuildCallback(async resolver =>
        {
            var module = resolver.Resolve<IAssetModule>();
            module.Initialize(new AssetModuleOptions(16, int.MaxValue));
            var pkg = module.CreatePackage("Default");
            var host = new HostPlayModeParameters
            {
                BuildinFileSystemParameters = new FileSystemParameters(),
                CacheFileSystemParameters = new FileSystemParameters()
            };
            await pkg.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.Host, host, 8));
        });
    }
}
```

Notes:

- You can inherit `AssetManagementVContainerInstaller` and override its parameter factory to supply custom YooAsset parameters per scene/build.
- You can replace `YooAssetModule` with another provider implementation without changing consumers.
- Inject `IAssetPackage` by storing it after init into a locator or a scoped service.
