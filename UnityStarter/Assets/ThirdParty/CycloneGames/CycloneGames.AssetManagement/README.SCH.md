# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

一个以 DI 为先、接口驱动的统一 Unity 资源管理抽象层。它将您的游戏逻辑与底层资源系统（如 YooAsset、Addressables 或 Resources）解耦，让您可以编写更清晰、更易于移植的代码。包内包含一个 YooAsset 的默认实现。

## 依赖与环境

- Unity 2022.3+
- 可选: `com.tuyoogame.yooasset`
- 可选: `com.unity.addressables`
- 可选: `com.cysharp.unitask`, `jp.hadashikick.vcontainer`, `com.mackysoft.navigathena`, `com.cyclonegames.factory`, `com.cyclone-games.logger`

## 快速上手

要使用本插件，您首先需要一个针对目标资源系统、实现了 `IAssetModule` 接口的提供器（Provider）。下面的示例将演示如何使用一个自定义模块从 Unity 的 `Resources` 文件夹加载资源。它清晰地展示了您的游戏代码如何与统一的 API 交互，并与底层的 `Resources.Load` 调用完全解耦。

```csharp
using CycloneGames.AssetManagement;
using UnityEngine;
using System.Threading.Tasks;

// 假设您已编写了一个用于 Resources 系统的 'ResourcesModule'(可能是 Resources.Load / Addressable / YooAsset)。
// 一个最小化的实现可能如下所示：
async Task LoadMyPlayer()
{
    IAssetModule module = new ResourcesModule();
    module.Initialize(new AssetManagementOptions());

    var pkg = module.CreatePackage("MyResources");

    using (var handle = pkg.LoadAssetAsync<GameObject>("Prefabs/MyPlayer"))
    {
        // 在实际项目中，推荐使用 UniTask 异步等待
        while (!handle.IsDone)
        {
            await Task.Yield(); // 或者在协程中 yield return null
        }

        if (handle.Asset)
        {
            var go = Object.Instantiate(handle.Asset);
        }
    }
}
```

## 核心特性

- **接口优先设计**：将您的游戏逻辑与底层资源系统解耦。面向稳定的接口编程，随时可以切换后端实现，无需大规模重构。
- **DI 友好**：为依赖注入而生，让您能以清晰、可测试的方式管理资源加载服务。
- **统一 API**：为所有资源操作提供单一、一致的 API。无论您使用 `YooAsset`、`Addressables` 还是自定义的 `Resources.Load` 封装，调用代码都保持不变。
- **高可扩展性**：通过实现 `IAssetModule` 和 `IAssetPackage` 接口，轻松创建自己的 Provider 来支持任何资源管理系统。
- **强大的工具集**：内置了对批量下载、重试、缓存和进度聚合等通用需求的支持。

## 更新与下载

- 请求最新版本：

```csharp
string version = await pkg.RequestPackageVersionAsync();
```

- 更新活动清单：

```csharp
bool ok = await pkg.UpdatePackageManifestAsync(version);
```

- 预下载指定版本（不切换活动清单）：

```csharp
var downloader = await pkg.CreatePreDownloaderForAllAsync(version, downloadingMaxNumber: 8, failedTryAgain: 2);
await downloader.StartAsync();
```

- 标签/路径下载：

```csharp
IDownloader d1 = pkg.CreateDownloaderForTags(new[]{"Base","UI"}, 8, 2);
IDownloader d2 = pkg.CreateDownloaderForLocations(new[]{"Assets/Prefabs/Hero.prefab"}, true, 8, 2);
d1.Combine(d2);
d1.Begin();
await d1.StartAsync();
```

- 清理缓存：

```csharp
await pkg.ClearCacheFilesAsync(clearMode: "All");
```

## 场景（基础）

```csharp
var scene = pkg.LoadSceneAsync("Assets/Scenes/Main.unity");
scene.WaitForAsyncComplete();
await pkg.UnloadSceneAsync(scene);
```

## 集成 Navigathena（可选）

要将 Navigathena 与本资源管理系统支持的场景一起使用，请使用与提供者无关的 `AssetManagementSceneIdentifier`。无论您底层使用的是 YooAsset 还是 Addressables，它都可以正常工作。

```csharp
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.Navigathena;
using MackySoft.Navigathena.SceneManagement;

// 从您的 IAssetModule 获取 IAssetPackage
IAssetPackage pkg = assetModule.GetPackage("DefaultPackage");

// 创建标识符。它将使用指定的包来加载场景。
ISceneIdentifier id = new AssetManagementSceneIdentifier(pkg, "Assets/Scenes/Main.unity", LoadSceneMode.Additive, true);
await GlobalSceneNavigator.Instance.Push(id);
```

## 用户确认的更新流程

模块支持“检查 → 用户确认 → 执行更新”的交互流程。

```csharp
// 1) 检查最新版本
string latest = await pkg.RequestPackageVersionAsync();
bool hasUpdate = !string.IsNullOrEmpty(latest) && latest != currentVersion;
if (!hasUpdate) return;

// 2) 预下载统计体量，用户确认
var pre = await pkg.CreatePreDownloaderForAllAsync(latest, downloadingMaxNumber: 8, failedTryAgain: 2);
long totalBytes = (pre?.TotalDownloadBytes) ?? 0;
int totalFiles = (pre?.TotalDownloadCount) ?? 0;
// 弹窗提示：显示更新大小、文件数，用户确认后继续
await pre.StartAsync(); // 支持取消

// 3) 切换清单
bool switched = await pkg.UpdatePackageManifestAsync(latest);
if (switched) { currentVersion = latest; /* 持久化保存 */ }

// 可选：清理旧缓存
// await pkg.ClearCacheFilesAsync(clearMode: "All");
```

## 额外选项

- 同步场景加载

```csharp
var handle = pkg.LoadSceneSync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
```

- 句柄跟踪（诊断）

```csharp
module.Initialize(new AssetManagementOptions(
  operationSystemMaxTimeSliceMs: 16,
  bundleLoadingMaxConcurrency: 8,
  logger: null,
  enableHandleTracking: true // 编辑器监控
));
```

## 脚本定义符号

本包使用程序集定义文件（`.asmdef`）来根据项目中存在的其他包自动定义宏。

- `YOOASSET_PRESENT`: 当安装了 `com.tuyoogame.yooasset` 时定义。启用 YooAsset 提供器。
- `ADDRESSABLES_PRESENT`: 当安装了 `com.unity.addressables` 时定义。启用 Addressables 提供器。
- `VCONTAINER_PRESENT`: 当安装了 `jp.hadashikick.vcontainer` 时定义。启用 VContainer 集成。
- `NAVIGATHENA_PRESENT`: 当安装了 `com.mackysoft.navigathena` 时定义。启用 Navigathena 集成。

通常您不需要直接与这些宏交互。

## 场景预热（可选）

预热每个场景的内容，以减少场景切换期间的峰值。

### 设置

1) 创建一个或多个 `PreloadManifest` 资产（位置+权重）
2) 创建一个 `ScenePreloadRegistry` 资产，将 `sceneKey` 映射到清单列表
3) 在启动时设置 `DefaultPackage`
4) 在 `NavigathenaNetworkManager` 中，分配 `scenePreloadRegistry`
5) 确保已安装 Navigathena 和 YooAsset

## 使用 YooAsset 适配器

如果您的项目中已包含 `com.tuyoogame.yooasset`，则可以直接使用内置的适配器。

```csharp
using CycloneGames.AssetManagement;
using YooAsset;

// 1) 初始化具体的 YooAssetModule
IAssetModule module = new YooAssetModule();
module.Initialize(new AssetManagementOptions(operationSystemMaxTimeSliceMs: 16));

// 2) 创建并初始化资源包
var pkg = module.CreatePackage("Default");
var hostParams = new HostPlayModeParameters
{
    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices: null)
};
await pkg.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.Host, hostParams, bundleLoadingMaxConcurrencyOverride: 8));

// 3) 使用统一 API 加载并实例化
using (var handle = pkg.LoadAssetAsync<UnityEngine.GameObject>("Assets/Prefabs/My.prefab"))
{
    handle.WaitForAsyncComplete();
    var go = pkg.InstantiateSync(handle);
}
```

## 使用 Addressables 适配器

如果您的项目中已包含 `com.unity.addressables`，则可以直接使用为其提供的适配器。

```csharp
using CycloneGames.AssetManagement;

// 1) 初始化 AddressableAssetModule
IAssetModule module = new AddressableAssetModule();
module.Initialize(new AssetManagementOptions());

// 2) 创建一个资源包
var pkg = module.CreatePackage("Default");

// 3) 使用统一 API 加载资源
using (var handle = pkg.LoadAssetAsync<UnityEngine.GameObject>("Assets/Prefabs/MyCharacter.prefab"))
{
    await handle.Task;
    if (handle.Asset)
    {
        var go = UnityEngine.Object.Instantiate(handle.Asset);
    }
}
```

> [!NOTE]
> 某些功能（例如软件包版本控制和预下载）是 YooAsset 特有的，在 Addressables 适配器中没有直接对应的功能。

## 其他用法

### 缓存

```csharp
var cache = new CycloneGames.AssetManagement.Runtime.Cache.AssetCacheService(pkg, maxEntries: 128);
var icon = cache.Get<Sprite>("Assets/Art/UI/Icons/Abilities/Fireball.png");
cache.TryRelease("Assets/Art/UI/Icons/Abilities/Fireball.png");
```

### 重试

```csharp
using CycloneGames.AssetManagement.Runtime.Retry;
var policy = new RetryPolicy(3, 0.5, 2.0);
var handle = await pkg.LoadAssetWithRetryAsync<Sprite>("Assets/Art/.../Icon.png", policy, ct);
```

### 进度

```csharp
using CycloneGames.AssetManagement.Runtime.Progressing;
var agg = new ProgressAggregator();
agg.Add(groupOp1, 2f);
agg.Add(groupOp2, 1f);
var p = agg.GetProgress(); // 0..1
