# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

一个以 DI 为先、接口驱动的统一 Unity 资源管理抽象层。它将您的游戏逻辑与底层资源系统（如 YooAsset、Addressables 或 Resources）解耦，让您可以编写更清晰、更易于移植的高性能代码。包内包含一个 YooAsset 的零 GC 默认实现。

## 依赖与环境

- Unity 2022.3+
- 可选: `com.tuyoogame.yooasset`
- 可选: `com.unity.addressables`
- 可选: `com.cysharp.unitask`, `com.cysharp.r3`
- 可选: `jp.hadashikick.vcontainer`

## 快速上手

要使用本插件，您需要一个 `IAssetModule` 接口的实现。下面的示例将演示如何使用 `YooAssetManagementModule` 并加载一个资源，它清晰地展示了如何通过统一的 API 进行交互。

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

public class MyGameManager
{
    private IAssetModule assetModule;

    public async UniTaskVoid Start()
    {
        // 1. 初始化模块 (最好在 DI 容器中完成)
        assetModule = new YooAssetManagementModule();
        assetModule.Initialize(new AssetManagementOptions());

        // 2. 创建并初始化资源包
        var package = assetModule.CreatePackage("DefaultPackage");
        var initOptions = new AssetPackageInitOptions(
            AssetPlayMode.Host,
            new HostPlayModeParameters() // 在此配置您的 YooAsset 参数
        );
        await package.InitializeAsync(initOptions);

        // 3. 使用统一 API 加载资源
        await LoadMyPlayer(package);
    }

    private async UniTask LoadMyPlayer(IAssetPackage package)
    {
        using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/MyPlayer"))
        {
            await handle.Task; // 异步等待资源加载完成

            if (handle.Asset)
            {
                // 使用特定于提供商的扩展方法实现零 GC 实例化
                var go = package.InstantiateSync(handle);
            }
        }
    }
}
```

## 更多用法示例

### 离线运行模式 (YooAsset)

以下示例展示了如何初始化资源包以在 `OfflinePlayMode`（离线模式）下运行。这种模式常用于所有资源都已包含在初始安装包内的单机游戏。

```csharp
// 1. 像往常一样初始化模块
assetModule = new YooAssetModule();
assetModule.Initialize(new AssetManagementOptions());

// 2. 创建资源包
var package = assetModule.CreatePackage("DefaultPackage");

// 3. 创建 YooAsset 的离线模式特定参数
var yooAssetOfflineParams = new OfflinePlayModeParameters(); 
// 在此模式下，YooAsset 通过一个内置的文件查询服务来定位资源，
// 该服务通常在 YooAsset 编辑器中进行配置。

// 4. 将提供者专属的参数包装进通用的 InitOptions
var initOptions = new AssetPackageInitOptions(
    AssetPlayMode.Offline,      // a. 设置运行模式
    yooAssetOfflineParams       // b. 传入提供者专属的参数对象
);

// 5. 初始化资源包
bool success = await package.InitializeAsync(initOptions);
if (success)
{
    // 资源包现在已准备好从本地加载资源。
}
```

### Addressables 提供器

Addressables 提供器的初始化流程更简单，因为它是全局初始化的。请注意，Addressables 提供器不支持同步操作。

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MyAddressablesManager
{
    private IAssetModule assetModule;

    public async UniTaskVoid Start()
    {
        // 1. 创建模块
        assetModule = new AddressablesModule();
        
        // 2. 初始化并等待其就绪
        // Addressables 在后台异步初始化。
        // 我们必须等待 'Initialized' 标志位变为 true。
        assetModule.Initialize();
        await UniTask.WaitUntil(() => assetModule.Initialized);

        Debug.Log("Addressables 模块初始化完成。");

        // 3. 创建一个“资源包”（对于 Addressables 来说，这是一个逻辑分组）
        var package = assetModule.CreatePackage("DefaultPackage");
        
        // 4. 加载资源
        // 注意：Addressables 提供器仅支持异步操作。
        using (var handle = package.LoadAssetAsync<GameObject>("MyAddressablePrefab"))
        {
            await handle.Task;
            if (handle.Asset)
            {
                var go = await package.InstantiateAsync(handle).Task;
            }
        }
    }
}
```
> [!NOTE]
> 与 YooAsset 提供器相比，Addressables 提供器存在一些限制：
> - 不支持同步加载或实例化。
> - 不支持 `IPatchService` 热更新工作流。
> - 无法使用版本查询、预下载指定版本等高级功能。

### Resources 提供器

`Resources` 提供器是最简单的，适用于快速原型开发或访问直接打包到游戏中的资源。它不需要任何特殊的初始化。

```csharp
// 1. 创建并初始化模块
assetModule = new ResourcesModule();
assetModule.Initialize(); // 初始化是同步的

// 2. 创建资源包
var package = assetModule.CreatePackage("DefaultPackage");

// 3. 加载资源 (同步和异步均支持)
using (var handle = package.LoadAssetAsync<GameObject>("Path/In/Resources/Folder"))
{
    await handle.Task;
    if (handle.Asset)
    {
        var go = package.InstantiateSync(handle);
    }
}
```
> [!WARNING]
> `Resources` 提供器有诸多限制：
> - 无法加载场景。
> - 不支持任何下载或热更新功能。
> - `LoadAllAssetsAsync` 是一个阻塞主线程的同步操作。
> - 从 `Resources` 加载的资源无法被单独卸载，可能导致内存占用过高。通常不建议在大型项目的正式产品中使用。

## 核心特性

- **接口优先设计**: 将您的游戏逻辑与底层资源系统解耦。面向稳定的接口编程，随时可以切换后端实现，无需大规模重构。
- **DI 友好**: 为依赖注入 (`VContainer`, `Zenject` 等) 而生，让您能以清晰、可测试的方式管理资源加载服务。
- **统一 API**: 为所有资源操作提供单一、一致的 API。无论您使用 `YooAsset`、`Addressables` 还是自定义的 `Resources` 封装，调用代码都保持不变。
- **热路径零 GC**: 默认的 `YooAsset` 提供器支持在关键操作（如资源实例化）上实现零垃圾回收。
- **UniTask 驱动**: 完全基于 `UniTask` 的异步 API，为 Unity 提供极致的性能和最小的开销。
- **支持单机模式**: 通过配置提供器（例如 YooAsset 的 `OfflinePlayMode`），可以实现所有资源均从本地加载，完美支持纯单机游戏。
- **高可扩展性**: 通过实现 `IAssetModule` 和 `IAssetPackage` 接口，轻松创建自己的 Provider。

## 高层更新工作流 (YooAsset 提供器)

为了简化更新流程，`YooAsset` 提供器内置了一个高层的 `IPatchService` 服务。它封装了整个更新状态机，让你无需关心底层细节。

```csharp
// 1. 从模块获取补丁服务
IPatchService patchService = assetModule.CreatePatchService("DefaultPackage");

// 2. 订阅补丁事件
patchService.PatchEvents
    .Subscribe(evt =>
    {
        var (patchEvent, args) = evt;
        if (patchEvent == PatchEvent.FoundNewVersion)
        {
            var eventArgs = (FoundNewVersionEventArgs)args;
            // 弹窗提示用户："发现新版本，大小为 {eventArgs.TotalDownloadSizeBytes}"
            // 如果用户确认，则调用 patchService.Download();
        }
        else if (patchEvent == PatchEvent.PatchDone)
        {
            // 更新完成，进入游戏
        }
    });

// 3. 运行补丁流程
await patchService.RunAsync(autoDownloadOnFoundNewVersion: false);
```

## 底层更新与下载 API (YooAsset 提供器)

如果你需要更精细的控制，可以使用底层的 `IAssetPackage` API。

- **请求最新版本**:
  ```csharp
  string version = await package.RequestPackageVersionAsync();
  ```

- **预下载指定版本** (不切换活动清单):
  ```csharp
  var downloader = await package.CreatePreDownloaderForAllAsync(version, downloadingMaxNumber: 10, failedTryAgain: 3);
  if (downloader != null)
  {
      await downloader.StartAsync(); // 支持取消操作
  }
  ```

- **更新活动清单**:
  ```csharp
  bool manifestUpdated = await package.UpdatePackageManifestAsync(version);
  ```

- **按标签下载**:
  ```csharp
  IDownloader downloader = package.CreateDownloaderForTags(new[]{"Base", "UI"}, 10, 3);
  downloader.Begin();
  await downloader.StartAsync();
  ```

- **清理缓存**:
  ```csharp
  await package.ClearCacheFilesAsync(ClearCacheMode.Unused);
  ```

## 场景管理

```csharp
// 异步加载
var sceneHandle = package.LoadSceneAsync("Assets/Scenes/Main.unity");
await sceneHandle.Task;

// 异步卸载
await package.UnloadSceneAsync(sceneHandle);
```
> [!WARNING]
> 同步场景加载 (`LoadSceneSync`) 已被弃用，因为它可能导致严重的性能问题。请始终优先使用异步版本。

## 脚本定义符号

本包使用程序集定义文件 (`.asmdef`) 来根据项目中存在的其他包自动定义宏。

- `YOOASSET_PRESENT`: 启用 YooAsset 提供器。
- `ADDRESSABLES_PRESENT`: 启用 Addressables 提供器。
- `VCONTAINER_PRESENT`: 启用 VContainer 集成辅助类。

您不需要手动管理这些宏。
