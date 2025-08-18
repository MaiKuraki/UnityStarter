# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

以接口为先、DI 友好的 Unity 资源管理插件。默认实现基于 YooAsset，兼容 Navigathena 场景管理插件。

## 依赖与环境

- Unity 2022.3+
- 必需：`com.tuyoogame.yooasset`
- 可选：`com.cysharp.unitask`、`jp.hadashikick.vcontainer`、`com.mackysoft.navigathena`、`com.cyclonegames.factory`、`com.cyclone-games.logger`、`com.harumak.addler`

## 快速上手

```csharp
using CycloneGames.AssetManagement;
using YooAsset;

IAssetModule module = new YooAssetModule();
module.Initialize(new AssetModuleOptions(operationSystemMaxTimeSliceMs: 16));

var pkg = module.CreatePackage("Default");
var hostParams = new HostPlayModeParameters
{
    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices: null)
};
await pkg.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.Host, hostParams, bundleLoadingMaxConcurrencyOverride: 8));

using (var handle = pkg.LoadAssetAsync<UnityEngine.GameObject>("Assets/Prefabs/My.prefab"))
{
    handle.WaitForAsyncComplete();
    var go = pkg.InstantiateSync(handle);
}
```

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
