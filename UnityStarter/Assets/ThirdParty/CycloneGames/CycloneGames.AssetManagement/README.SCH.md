# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

一个以 DI 为先、接口驱动的统一 Unity 资源管理抽象层。它将您的游戏逻辑与底层资源系统（如 YooAsset、Addressables 或 Resources）解耦，让您可以编写更清晰、更易于移植的代码。包内包含一个 YooAsset 的默认实现。

## 依赖与环境

- Unity 2022.3+
- 可选：`com.tuyoogame.yooasset`
- 可选：`com.cysharp.unitask`、`jp.hadashikick.vcontainer`、`com.mackysoft.navigathena`、`com.cyclonegames.factory`、`com.cyclone-games.logger`、`com.harumak.addler`

## 快速上手

要使用本插件，您首先需要一个针对目标资源系统、实现了 `IAssetModule` 接口的提供器（Provider）。下面的示例将演示如何使用一个自定义模块从 Unity 的 `Resources` 文件夹加载资源。它清晰地展示了您的游戏代码如何与统一的 API 交互，并与底层的 `Resources.Load` 调用完全解耦。

```csharp
using CycloneGames.AssetManagement;
using UnityEngine;
using System.Threading.Tasks;

// 假设您已编写了一个用于 Resources 系统的 'ResourcesModule'。
// 一个最小化的实现可能如下所示：
async Task LoadMyPlayer()
{
    IAssetModule module = new ResourcesModule();
    module.Initialize(new AssetModuleOptions());

    var pkg = module.CreatePackage("MyResources");

    // 无论后端是什么，API 调用都是一样的！
    using (var handle = pkg.LoadAssetAsync<GameObject>("Prefabs/MyPlayer"))
    {
        // 在真实项目中，您应该异步等待此操作。
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

说明：

- 现已支持 `activateOnLoad`，该参数会映射到 YooAsset 的 `suspendLoad`（当 `activateOnLoad == false` 时挂起激活），需要时可在加载完成后通过 YooAsset API 手动激活。

## 集成 Navigathena（可选）

使用提供的 `YooAssetSceneIdentifier` 将 Navigathena 的场景加载切至 YooAsset：

```csharp
using CycloneGames.AssetManagement.Integrations.Navigathena;
using MackySoft.Navigathena.SceneManagement;

IAssetPackage pkg = module.GetPackage("Default");
ISceneIdentifier id = new YooAssetSceneIdentifier(pkg, "Assets/Scenes/Main.unity", LoadSceneMode.Additive, true, 100);
await GlobalSceneNavigator.Instance.Change(new LoadSceneRequest(id));
```

## Addressables 与 YooAsset 共存（简要）

- 支持共存。建议保持 Addressables Key 与 YooAsset Location 一致，便于运行时按配置切换标识符。
- 实现细节由业务自行决定，本模块无需额外设置。

### 双栈切换（Addressables <-> YooAsset）

建议保持 Addressables 的 Key 与 YooAsset 的 Location 一致。运行时按配置选择：

```csharp
ISceneIdentifier id;
if (useAddressables)
{
    // Addressables（需要 ENABLE_NAVIGATHENA_ADDRESSABLES）
    id = new MackySoft.Navigathena.SceneManagement.AddressableAssets.AddressableSceneIdentifier("Assets/Scenes/Main.unity");
}
else
{
    id = new CycloneGames.AssetManagement.Integrations.Navigathena.YooAssetSceneIdentifier(pkg, "Assets/Scenes/Main.unity");
}
await GlobalSceneNavigator.Instance.Change(new LoadSceneRequest(id));
```

## 用户确认的更新流程（推荐）

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

- 若只更新部分内容，可用标签或路径下载器替代全量预下载。
- 处理取消：捕获 `OperationCanceledException`，保留旧清单不切换。

## 额外选项

- 同步场景加载

```csharp
var handle = pkg.LoadSceneSync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
```

- 句柄跟踪（诊断）

```csharp
module.Initialize(new AssetModuleOptions(
  operationSystemMaxTimeSliceMs: 16,
  bundleLoadingMaxConcurrency: 8,
  logger: null,
  enableHandleTracking: true // 编辑器/开发版建议开启，正式可关闭
));
```

## Factory 集成

- Prefab 工厂示例：

```csharp
using CycloneGames.AssetManagement.Integrations.Factory;

var factory = new YooAssetPrefabFactory<MyMono>(pkg, "Assets/Prefabs/My.prefab");
var instance = factory.Create();
factory.Dispose();
```

## 宏说明（Macro Notes）

- `NAVIGATHENA_PRESENT`：安装 `com.mackysoft.navigathena` 时自动定义
- `NAVIGATHENA_YOOASSET`：安装 `com.tuyoogame.yooasset` 时自动定义（启用 Navigathena + YooAsset 集成）
- `ENABLE_NAVIGATHENA_ADDRESSABLES`：Navigathena 官方 Addressables 集成
- `VCONTAINER_PRESENT`：安装 `jp.hadashikick.vcontainer` 时自动定义
- `ADDLER_PRESENT`：安装 `com.harumak.addler` 时自动定义（启用可选 Addler 适配）

## 场景预热（可选）

### 设置

1) 创建一个或多个 `PreloadManifest` 资源（记录 location + weight）
2) 创建 `ScenePreloadRegistry`，将 `sceneKey`（可用场景 location/name）映射到一组 manifests
3) 启动时设置 `NavigathenaYooSceneFactory.DefaultPackage = pkg`
4) 在 `NavigathenaNetworkManager`（来自 NavigathenaMirror）上指定 `scenePreloadRegistry`
5) 确保已安装 Navigathena 与 YooAsset；宏由 asmdef 自动注入

### Mirror + Navigathena 流程

- 服务器：
  - 通知客户端前，调用 `_preloadManager.OnBeforeLoadSceneAsync(sceneKey)`
  - 发送场景消息给客户端
  - 通过 Navigathena 加载场景，然后调用 `_preloadManager.OnAfterLoadScene(sceneKey)`
- 客户端：
  - 收到场景消息后，依次执行 `OnBeforeLoadSceneAsync(sceneKey)` → Navigathena Replace → `OnAfterLoadScene(sceneKey)`

### 手动使用（非网络）

```csharp
using CycloneGames.AssetManagement.Integrations.Navigathena;
using CycloneGames.AssetManagement.Preload;

var registry = /* 加载 ScenePreloadRegistry */;
var preload = new ScenePreloadManager(pkg, registry);
await preload.OnBeforeLoadSceneAsync("Assets/Scenes/Main.unity");
// ... 通过 Navigathena 切换场景
preload.OnAfterLoadScene("Assets/Scenes/Main.unity");
```

### 说明

- 关于 PreloadManifest 条目的进度计算与行为，请参见 `PreloadManifest` 源码中的中英注释与 Tooltip。

## 提供器示例：使用 YooAsset 适配器

如果您的项目中已包含 `com.tuyoogame.yooasset`，则可以直接使用内置的适配器，它提供了一套功能强大、生产环境可用的资源解决方案。其设置与旧版的快速上手类似。

```csharp
using CycloneGames.AssetManagement;
using YooAsset;

// 1) 初始化具体的 YooAssetModule
IAssetModule module = new YooAssetModule();
module.Initialize(new AssetModuleOptions(operationSystemMaxTimeSliceMs: 16));

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
    var go = pkg.InstantiateSync(handle); // 注意: InstantiateSync 是 YooAsset 适配器特有的扩展方法
}
```
关于使用 YooAsset 提供器进行更新、下载和场景管理的详细信息，请参阅本文档中的相应章节。

## 其他用法

### 缓存

```csharp
var cache = new CycloneGames.AssetManagement.Cache.AssetCacheService(pkg, maxEntries: 128);
var icon = cache.Get<Sprite>("Assets/Art/UI/Icons/Abilities/Fireball.png");
cache.TryRelease("Assets/Art/UI/Icons/Abilities/Fireball.png");
```

### 重试

```csharp
using CycloneGames.AssetManagement.Retry;
var policy = new RetryPolicy(3, 0.5, 2.0);
var handle = await pkg.LoadAssetWithRetryAsync<Sprite>("Assets/Art/.../Icon.png", policy, ct);
```

### 进度

```csharp
using CycloneGames.AssetManagement.Progressing;
var agg = new ProgressAggregator();
agg.Add(groupOp1, 2f);
agg.Add(groupOp2, 1f);
var p = agg.GetProgress(); // 0..1
```
