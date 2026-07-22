# Provider 与集成

[English](Providers.md) | 简体中文

AssetManagement 提供三个 provider（Resources、Addressables、YooAsset）与两个集成（Navigathena、VContainer）。Provider-neutral 消费者依赖 `IAssetPackage`；只有 composition 与 release-operation assembly 才引用 provider assembly。

## 目录

- [概述](#概述)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [进阶主题](#进阶主题)
- [故障排查](#故障排查)

## 概述

每个 provider 作为 `IAssetModule` 暴露，各有不同的能力面。Addressables 与 YooAsset 不能同时通过这些 adapter 激活 —— 框架的 AssetBundle runtime guard 建立单一 provider authority，直到共存、shutdown 顺序与内存行为作为完整产品配置得到验证。

### 主要特性

- **Resources**：始终可用，适合 bootstrap，仅加载。
- **Addressables**：catalog 维护、Tags/Locations downloader、scene 加载、bulk 加载。
- **YooAsset**：manifest 激活、All/Tags/Locations downloader、raw-file 访问、scene 加载、bulk 加载。
- **Navigathena**：基于 `IAssetSceneLoader` 的显式 scene 导航。
- **VContainer**：module 与 retention scheduler 的 DI installer。

### 选择 provider

| 需求 | Resources | Addressables | YooAsset |
| --- | ---: | ---: | ---: |
| 异步资产加载与 prefab instance | yes | yes | yes |
| 同步资产加载 | yes | no | yes |
| Bulk/sub-asset 加载 | no | yes | yes |
| Raw-file 访问 | no | no | yes |
| 异步 scene 加载 | no | yes | yes |
| 远程 downloader | no | Tags、Locations | All、Tags、Locations |
| Provider maintenance | no | latest catalog、unused-bundle cleanup、全局 Unity cache | product-version manifest activation、package cache |
| 可靠内置存储探测 | no | 仅 desktop/Editor 缓存卷 | 特定 desktop/Editor Host 配置 |
| Provider runtime 所有权 | 每个 module 一个 owner | 一个进程 owner、一个逻辑 package | 一个进程 owner、多个 package |

例如，UIFramework 的 dynamic-atlas sample 需要 `IAssetSyncOperations`，DataTable raw 加载需要 `IAssetRawFileLoader`，Navigathena 需要 `IAssetSceneLoader`。

### 激活可选 assembly

当依赖已安装到 `UnityStarter/Packages/manifest.json` 并在 `packages-lock.json` 中解析、版本满足 asmdef 的 `versionDefines` 范围，且所有 `defineConstraints` 都满足时，可选 assembly 才具备编译资格。这些 assembly 使用 `autoReferenced: false`，因此 consumer asmdef 还必须引用所选 provider 或 integration assembly 才能访问其 API；该引用并不控制可选 assembly 本身是否编译。

| Assembly | 支持的版本条件 |
| --- | --- |
| `CycloneGames.AssetManagement.Runtime.Providers.Addressables` | `com.unity.addressables` `[2.11.1,2.11.2)` |
| `CycloneGames.AssetManagement.Runtime.Providers.YooAsset` | `com.tuyoogame.yooasset` `[3.0.5,4.0.0)` |
| `CycloneGames.AssetManagement.Runtime.Integrations.Navigathena` | `com.mackysoft.navigathena` `[1.1.0,1.1.1)` |
| `CycloneGames.AssetManagement.Runtime.Integrations.VContainer` | 已安装 `jp.hadashikick.vcontainer` |

不要在 PlayerSettings 中手工添加 `CYCLONEGAMES_HAS_*` 符号。

## 核心概念

### Runtime-location 契约

`AssetRef`、`SceneRef` 与 load 方法使用确切的 provider runtime key。Editor 资产路径不是 provider-neutral 地址。

| Provider | 项目中的示例资产 | Runtime location 示例 |
| --- | --- | --- |
| Resources | `Assets/Game/Resources/UI/Inventory.png` | `UI/Inventory` |
| Addressables | 任意 addressable 条目 | 配置的 Addressables 地址 |
| YooAsset | 已收集资产 | 配置的 YooAsset 地址 |

Property drawer 存储 Editor GUID 以显示 authoring 对象，并把 `Runtime Location` 作为独立字段暴露。选择另一个对象会清除前一个 runtime location。Validator 报告缺失的 GUID 目标与空 location，但绝不重写自定义 provider key。

### Scene provider 契约

Addressables 与 YooAsset 只暴露异步 `IAssetSceneLoader` 能力。高级重载携带 Unity `LoadSceneParameters`，支持 `LocalPhysicsMode.None`、`Physics2D`、`Physics3D` 与合法的 `Physics2D | Physics3D` 组合。加载的 scene 持有其 local physics world，卸载时销毁。

每个 package 持有 active-scene registry 与私有 origin token。Active unload 校验确切 handle 与 registry 条目。成功 unload、`LoadSceneMode.Single` 替换或外部 `SceneManager` unload 后，通过同一 package generation 重复 unload 是 terminal no-op；另一个 package 或重建的 generation 会被拒绝。

Activation 与 unload 是主线程受限的 single-flight 迁移。取消只在首次 provider mutation 前接受。一旦 unload 开始，后续调用即使 token 已取消也会加入。失败的 unload 保留 handle 注册、移除诊断 `Unloading` 状态、记录生命周期错误，并允许新尝试。

Scene 不是 asset-cache 条目：SLRU admission、cache trim、bucket clear 与低内存缓存维护不会卸载它们。`ISceneHandle.Dispose` 只是幂等的 caller-wrapper 释放。Originating package 在 terminal unload 或 package shutdown 前仍是卸载权威。

## 使用指南

### Resources 所有权组合

Resources 始终可用。把 module 放在应用 owner 中；只返回 package 会丢失 shutdown 权威。

```csharp
public sealed class ResourcesAssetOwner
{
    private IAssetModule _module;

    public IAssetPackage Package { get; private set; }

    public async UniTask InitializeAsync(CancellationToken cancellationToken)
    {
        _module = new ResourcesModule();
        try
        {
            Package = await AssetManager.InitializeDefaultPackageAsync(
                _module, "BuiltIn",
                new AssetManagementOptions(), new AssetPackageInitOptions(),
                cancellationToken);
        }
        catch
        {
            await _module.DestroyAsync();
            throw;
        }
    }

    public async UniTask ShutdownAsync()
    {
        if (_module == null) return;
        await _module.DestroyAsync();
        Package = null;
        _module = null;
    }
}
```

Resources 暴露异步与同步单资产加载以及 runtime 诊断。它不暴露 bulk、raw-file、scene、catalog、downloader、storage-preflight 或 provider-maintenance 能力。释放 handle 释放 AssetManagement 所有权，但 Unity 可能保留 native Resources 数据。`UnloadUnusedAssetsAsync` 清理 idle wrapper 并调用 Unity 全局未使用资源扫描；在测量的阶段边界调度。

### Addressables 组合

Addressables provider 目标为 `com.unity.addressables` `[2.11.1,2.11.2)`。安装兼容包并从 composition assembly 引用 provider assembly。

```csharp
IAssetModule module = new AddressablesModule();

IAssetPackage package = await AssetManager.InitializeDefaultPackageAsync(
    module, "Default",
    new AssetManagementOptions(),
    new AssetPackageInitOptions(providerOptions: null),
    cancellationToken);
```

Adapter 强制一个 AssetManagement Addressables owner 与一个逻辑 package。Module 初始化与 package 初始化是独立门；`ProviderOptions` 必须为 null。Addressables group、profile、远程路径与 Player 内容必须已由产品管线构建。

```csharp
if (package is not IAddressablesCatalogMaintenance maintenance)
{
    throw new System.NotSupportedException("Addressables maintenance is unavailable.");
}

bool updated = await maintenance.UpdateLatestCatalogsAsync(cancellationToken);
bool cleaned = await maintenance.CleanUnusedBundleCacheAsync(cancellationToken);
```

同一 package 上已有 mutation 活动时，catalog 与 cache mutation fail-fast。Catalog 检查开始后，调用方取消只在 provider 到达安全 terminal 边界后、activation 开始前观察。Catalog 尝试即使 provider 失败也会推进 wrapper 缓存 generation。Idle 条目被 Dispose；active handle 保持有效并计为 Active，但从 keyed SLRU 查询中 generation-detach，直到最终释放或 shutdown。

`CleanUnusedBundleCacheAsync` 是常规维护操作。`ClearAllCacheFilesAsync` 调用 Unity 进程全局 `Caching.ClearCache`；它是破坏性的、不限定于逻辑 package。`ReadReleaseMetadataVersionAsync` 读取有界产品 metadata 用于关联；它不是已认证授权或防回滚证据。

Addressables Tags/Locations downloader 使用 provider 全局调度；不暴露 per-downloader 并发与重试。Key 数组接受 1-65,536 个值，每个最多 4,096 字符，总计 8 Mi 字符。每个 package 最多保留 128 个已注册 downloader wrapper 与 262,144 个显式 scope 值。Addressables 无法中止在途 `DownloadDependenciesAsync`；`Cancel`/`Dispose` 取消每个加入的调用方可见等待，同时 adapter 保留 pending handle、排空到 terminal、记录最终 snapshot，并只释放一次 provider handle。`CurrentDownloadBytes` 缓存 provider 状态，最多每秒刷新 4 次。所有 downloader 状态属性受主线程约束。

Pending Addressables 操作在每个平台拒绝 `WaitForAsyncComplete`。应 await `Task`。

### YooAsset 组合

YooAsset provider 目标为 `[3.0.5,4.0.0)` 范围内的稳定版 `com.tuyoogame.yooasset`。asmdef 范围是 compilation envelope；由于 SemVer prerelease 排在对应正式版之前，范围内的 prerelease 可能进入编译，但 activation test 会把它拒绝为不受支持。`YooAssetModule` 独占进程全局 Yoo runtime。每个需要隔离可写缓存的 client instance，都必须通过 file-system parameters 传入各自独立且显式的 `PackageRoot`。

```csharp
using YooAsset;

var module = new YooAssetModule(asyncOperationMaxTimeSliceMs: 16L);
await module.InitializeAsync(new AssetManagementOptions());

IAssetPackage package = module.CreatePackage("DefaultPackage");
var options = new OfflinePlayModeOptions
{
    BuiltinFileSystemParameters =
        FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
    BundleLoadingMaxConcurrency = 4,
};

bool initialized = await package.InitializeAsync(
    new AssetPackageInitOptions(providerOptions: options),
    cancellationToken);
```

为每个 package 创建新的 `InitializePackageOptions` 子类型。Adapter 接受 `BundleLoadingMaxConcurrency` 1-64；`int.MaxValue` 选择平台 fallback。

| Mode | 所需产品持有的 option |
| --- | --- |
| EditorSimulate | `EditorSimulateModeOptions` 与模拟 package root 的 Editor 文件系统参数 |
| Offline | `OfflinePlayModeOptions` 与 built-in 文件系统参数 |
| Host | `HostPlayModeOptions`、built-in/cache 文件系统参数，以及产品持有的 `IRemoteService` |
| Web | `WebPlayModeOptions` 与 web-server 和/或 web-network 文件系统参数 |
| Custom | `CustomPlayModeOptions` 与有序的产品持有文件系统参数列表 |

`asyncOperationMaxTimeSliceMs` 接受 10-100 ms。它控制 Yoo 主线程异步操作预算，不是网络并发。默认 16 ms 不是 60/120 fps 保证。

`IYooAssetPackageMaintenance` 接受产品提供的 version 进行类型化 manifest 加载，并暴露类型化缓存清理与 All/Tags/Locations downloader。Package 名称与 manifest version 是 1-128 字符的 ASCII token，带 path-safe 规则。Downloader 并发 1-32，重试 0-16，维护超时 1-3,600 秒。Tag/location 数组使用与 Addressables 相同的边界。Downloader `Cancel`/`Dispose` 取消每个加入的调用方可见等待，请求 provider-native `CancelDownload`，并保持 wrapper 注册直到观察 provider terminal 状态。

Desktop/Editor 存储预检只在 Host mode 使用默认 sandbox cache 文件系统且 `PackageRoot` 非空显式时才可靠。其他 mode、自定义文件系统、隐式 root、移动端、WebGL 与主机报告 `Unknown`。

Raw 内容加载为 `RawFileObject`。Adapter 在操作发布成功前在 Unity 主线程快照其文本与字节。完成的 `ReadText` 与 `ReadBytes` 调用随后 worker-safe；每次 `ReadBytes` 返回新的防御性副本。`FilePath` 保持为空，因为 provider bundle/archive 路径不是可移植 raw-file 路径。

WebGL 上通过 `WebPlayModeOptions` 使用 `WebServerFileSystem` 和/或 `WebNetworkFileSystem`；`SandboxFileSystem` 不受支持。Provider `link.xml` 保留 YooAsset 内置反射文件系统实现。

### 消费者中的能力协商

```csharp
if (package is IAssetBulkLoader bulkLoader)
{
    using IAllAssetsHandle<UnityEngine.Sprite> handle =
        bulkLoader.LoadAllAssetsAsync<UnityEngine.Sprite>("UI/Icons");
    await handle.Task;
    UseSprites(handle.Assets);
}

if (package is IAssetRawFileLoader rawLoader)
{
    using IRawFileHandle raw = rawLoader.LoadRawFileAsync(
        "Config/Balance.bytes", cancellationToken: cancellationToken);
    await raw.Task;
    byte[] bytes = raw.ReadBytes();
    if (bytes == null) throw new System.IO.IOException(raw.Error);
}
```

Resources 与 Addressables 不实现 `IAssetRawFileLoader`；需要可移植数据加载的产品可以通过普通资产契约编写 `TextAsset` 路径。

## 进阶主题

### VContainer

`AssetManagementVContainerInstaller` 注册 `IAssetModule`，通过 `IAsyncStartable` 启动 module 初始化，并可注册 retention scheduler。它不创建 package，也无法从同步 scope dispose 等待 module shutdown。

```csharp
var installer = new AssetManagementVContainerInstaller(
    moduleFactory: _ => new ResourcesModule(),
    options: new AssetManagementOptions(),
    cacheRetentionOptions: default,
    packageResolver: null);

installer.Install(builder);
```

VContainer 取消只在 provider 全局初始化开始前检查；无法中断已在进行的初始化。单独创建并初始化 package。Dispose 持有 `LifetimeScope` 前，显式 await `module.DestroyAsync()` 并处理失败。

### Navigathena

Navigathena 需要显式 scene 能力：

```csharp
IAssetSceneLoader sceneLoader = package as IAssetSceneLoader ??
    throw new System.NotSupportedException("The selected provider has no scene capability.");

var loadParameters = new UnityEngine.SceneManagement.LoadSceneParameters(
    UnityEngine.SceneManagement.LoadSceneMode.Additive)
{
    localPhysicsMode = UnityEngine.SceneManagement.LocalPhysicsMode.Physics2D
};

var identifier = sceneRef.ToSceneIdentifier(
    sceneLoader, loadParameters, SceneActivationMode.ActivateOnLoad,
    bucket: "Gameplay.Scene");
```

`CreateHandle` 是惰性的，不获取 provider scene。首次 `Load` 执行取消预检、获取 provider handle，并在所有权交回前 load 或 activation 失败时启动权威清理。重复 `Load` fail-fast；重复或并发 `Unload` 加入一个可重试操作。StandardSceneNavigator 只对 additive 加载验证，这是集成默认。

Navigathena 1.1.0 在 `ISceneHandle.Load` 返回后、`SceneState` 接管所有权前，若取消、blank-scene unload 或 entry-point discovery 失败，不会清理目标 handle。生产使用需要通过取消与故障 fixture 关闭 load 后所有权交接与异步 teardown 边界的 upstream、fork 或自定义 navigator。

### 自定义 provider 边界

自定义 provider 属于引用核心 runtime 的独立 assembly。把 SDK 类型排除在 provider-neutral 公共契约之外。生产 adapter 必须定义并测试：module、package、provider handle、instance、scene 与 downloader 的唯一 owner；主线程约束与任何窄 worker-safe 操作；memoized 多 await completion 与 provider-fault 传播；调用方取消与共享调用方可见取消、物理中止能力、pending 操作所有权与 terminal 释放；内容 mutation 后的 cache key 生成与失效；确定性 shutdown、泄漏遏制与可重试 vs terminal 失败；以及平台、IL2CPP/AOT、stripping、WebGL、文件系统与 suspend/resume 行为。

除非所有 provider 能实现相同语义，否则不要向 `IAssetPackage` 添加能力。Provider 专属维护保留在 provider assembly 中。

## 故障排查

| 现象 | 可能原因 | 解决方法 |
| --- | --- | --- |
| Provider assembly 不编译 | 依赖缺失或版本超出范围 | 安装并锁定受支持的稳定版 SDK；确认 `versionDefines` 并从 composition asmdef 引用 assembly |
| Addressables `ProviderOptions` 被拒 | 传入了非 null option | Addressables 的 `ProviderOptions` 必须为 null |
| YooAsset package 初始化失败 | `InitializePackageOptions` 子类型错误 | 为所选 play mode 构造确切子类型并通过 `AssetPackageInitOptions.ProviderOptions` 传入 |
| Addressables catalog 更新分裂 authority | 直接调用 `Addressables.UpdateCatalogs` | 所有 catalog mutation 通过 owning package adapter |
| Downloader 并发无效 | Provider 全局调度 | Addressables：并发由 provider 管理；YooAsset：每个 downloader 显式 1-32，但多个 downloader 与按需 I/O 会叠加 |
| `WaitForAsyncComplete` 抛出 | Pending Addressables 操作 | 改为 await `Task` |
| YooAsset raw `FilePath` 为空 | 设计如此 | `FilePath` 刻意为空；await `Task` 并使用 `ReadText`/`ReadBytes` |
| Scene unload 被拒 | 不同 package 或重建 generation | 使用 originating package generation；重建的 generation 被拒 |
| VContainer shutdown 不 await | Scope dispose 是同步的 | Dispose `LifetimeScope` 前显式 await `module.DestroyAsync()` |

## Provider 验证清单

对每个声明的 provider/平台组合：

1. 锁定确切 SDK 版本并确认条件 assembly 激活。
2. 编译 provider 及其 consumer composition asmdef。
3. 验证成功、失败、取消、重复 await、dispose、每个状态读取的主线程拒绝、跨 package 拒绝与 shutdown。Scene 需覆盖 `None`/`Physics2D`/`Physics3D` world、非法 enum、重复/并发 activation 与 unload、mutation 前取消与 commit 后 join、失败 load/unload 恢复、Single/外部 unload 与 exactly-once provider 释放。
4. 演练干净 Player 缓存与内容 build，不仅是 Editor 模拟。
5. 测试 Mono 与 IL2CPP、stripping、禁用 domain reload、网络丢失、存储满、低内存、suspend/resume 与进程终止。
6. 记录 workload、设备、内容 build 与原始性能证据；不要把 Editor 结果推广到其他平台。
