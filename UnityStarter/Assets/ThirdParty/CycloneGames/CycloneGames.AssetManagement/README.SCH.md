# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

一个为 Unity 设计的 DI 优先、接口驱动的统一资源管理抽象层。游戏逻辑只面向 `IAssetModule` 与 `IAssetPackage`；Resources、YooAsset、Addressables 或未来 provider adapter 都隔离在独立程序集边界后。

基于 **W-TinyLFU** 启发式缓存，它提供确定性的空闲句柄驱逐、Tag/Owner 追踪元数据、下载文件内容可信校验，以及用于检查缓存压力、句柄、场景和运行时治理状态的 Editor 诊断工具。

## 目录

- [环境要求](#环境要求)
- [安装](#安装)
- [快速开始](#快速开始)
- [核心概念](#核心概念)
- [提供器对比](#提供器对比)
- [使用示例](#使用示例)
  - [YooAsset 提供器](#yooasset-提供器)
  - [Addressables 提供器](#addressables-提供器)
  - [Resources 提供器](#resources-提供器)
- [热更新工作流](#热更新工作流)
- [高级功能](#高级功能)
- [资源引用 (AssetRef / SceneRef)](#资源引用-assetref--sceneref)
- [API 参考](#api-参考)

---

## 环境要求

| 依赖项       | 必需    | 说明                                         |
| ------------ | ------- | -------------------------------------------- |
| Unity        | 2022.3+ | 最低版本要求                                 |
| UniTask      | 是      | `com.cysharp.unitask` - 异步支持             |
| R3           | 是      | `com.cysharp.r3` - 用于 `IPatchService` 事件 |
| CycloneGames.Hash | 是 | `com.cyclone-games.hash` - 确定性 fingerprint 与内容哈希 |
| CycloneGames.IO | 是 | `com.cyclone-games.io` - 文件哈希与路径安全辅助 |
| CycloneGames.Logger | 是 | `com.cyclone-games.logger` - 运行时诊断 |
| YooAsset     | 可选    | `com.tuyoogame.yooasset` - 可选 provider |
| Addressables | 可选    | `com.unity.addressables` - 可选 provider |
| Navigathena  | 可选    | `com.mackysoft.navigathena` - 可选场景导航桥接 |
| VContainer   | 可选    | `jp.hadashikick.vcontainer` - 可选 DI 集成 |

## 安装

1. 将包导入您的 Unity 项目
2. 如果不是通过 UPM 依赖解析导入，请确保上表中的核心依赖已存在
3. Provider 程序集通过 asmdef `versionDefines` 和 `defineConstraints` 自动启用
4. 不要手动向 PlayerSettings 添加 scripting define symbols

### 程序集布局

核心 Runtime 程序集依赖 UniTask、R3、CycloneGames.Hash、CycloneGames.IO 和 CycloneGames.Logger。Provider 与 integration 代码位于独立程序集。可选程序集使用 `versionDefines` 生成 `CYCLONEGAMES_HAS_*` capability symbols，再通过 `defineConstraints` 只在对应包存在时参与编译。可选 provider/integration assembly 不会自动引用到所有宿主程序集；宿主 asmdef 应只显式引用实际使用的 provider 或 bridge。

| 程序集 | 用途 | 可选依赖 |
| --- | --- | --- |
| `CycloneGames.AssetManagement.Runtime` | 核心接口、缓存、Resources 提供器、引用和诊断 | 除核心依赖外无额外依赖 |
| `CycloneGames.AssetManagement.Runtime.Providers.YooAsset` | YooAsset 提供器和 YooAsset 补丁工作流 | `com.tuyoogame.yooasset` |
| `CycloneGames.AssetManagement.Runtime.Providers.Addressables` | Addressables 提供器和版本文件辅助逻辑 | `com.unity.addressables` |
| `CycloneGames.AssetManagement.Runtime.Integrations.VContainer` | VContainer 组合辅助 | `jp.hadashikick.vcontainer` |
| `CycloneGames.AssetManagement.Runtime.Integrations.Navigathena` | 基于 `IAssetPackage` 的 Navigathena 场景桥接 | `com.mackysoft.navigathena` |
| `CycloneGames.AssetManagement.Runtime.CacheRetention` | 可选、需显式启用的缓存保留策略调度器，周期性应用空闲缓存回收规则 | 除核心依赖外无额外依赖 |

---

## 快速开始

本节将指导您快速上手加载第一个资源。

### 第一步：初始化（游戏启动时执行一次）

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;

public class GameBootstrap
{
    // 存储模块引用以便后续访问
    public static IAssetModule AssetModule { get; private set; }

    public async UniTask Initialize()
    {
        // 创建并初始化模块（只执行一次）
        AssetModule = new ResourcesModule();
        await AssetModule.InitializeAsync();

        // 创建并初始化资源包（每个包只执行一次）
        var package = AssetModule.CreatePackage("DefaultPackage");

        var initOptions = new AssetPackageInitOptions(
            AssetPlayMode.Offline,
            providerOptions: null
        );

        await package.InitializeAsync(initOptions);
    }
}
```

### 第二步：加载资源（在游戏任意位置）

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

public class PlayerSpawner
{
    public async UniTask SpawnPlayer()
    {
        // 获取已存在的资源包（不要重复创建！）
        var package = GameBootstrap.AssetModule.GetPackage("DefaultPackage");

        // 加载并使用资源
        using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/Player"))
        {
            await handle.Task;

            if (handle.Asset != null)
            {
                GameObject player = package.InstantiateSync(handle);
            }
        }
    }
}
```

> [!TIP]
> **CreatePackage 与 GetPackage 的区别**
>
> - `CreatePackage(name)` - 初始化时调用一次，创建新资源包
> - `GetPackage(name)` - 在其他任意位置调用，获取已存在的资源包

---

## 核心概念

### 架构概览

```
游戏逻辑
    |
    v
IAssetModule（接口）
    |
    +-- YooAssetModule
    +-- AddressablesModule
    +-- ResourcesModule
    |
    v
IAssetPackage（接口）
    |
    v
资源加载 / 实例化 / 场景管理
```

### 关键接口

| 接口              | 用途                                 |
| ----------------- | ------------------------------------ |
| `IAssetModule`    | 资源系统入口，创建和管理资源包       |
| `IAssetPackage`   | 处理所有资源操作：加载、实例化、场景 |
| `IAssetHandle<T>` | 表示已加载的资源，可释放以管理内存   |
| `IPatchService`   | 高层热更新工作流（仅 YooAsset）      |

### 句柄生命周期

句柄代表已加载的资源，必须正确释放：

```csharp
// 方式一：using 语句（推荐）
using (var handle = package.LoadAssetAsync<Texture2D>("Textures/Icon"))
{
    await handle.Task;
    // 在这里使用 handle.Asset
}
// 自动释放

// 方式二：手动释放
var handle = package.LoadAssetAsync<Texture2D>("Textures/Icon");
await handle.Task;
// ... 使用资源 ...
handle.Dispose(); // 不要忘记这一步！
```

---

## 提供器对比

| 功能         | YooAsset | Addressables | Resources |
| ------------ | -------- | ------------ | --------- |
| 同步加载     | 支持     | 不支持       | 支持      |
| 异步加载     | 支持     | 支持         | 支持      |
| 热更新       | 支持     | 有限         | 不支持    |
| 场景加载     | 支持     | 支持         | 不支持    |
| 原生文件加载 | 支持     | 不支持       | 不支持    |
| 常见适用场景 | 重补丁/热更新产品 | 已采用 Addressables 的项目 | 内置原型和小型工具 |

---

## 使用示例

### YooAsset 提供器

YooAsset 是可选 provider，适用于明确采用其 package 与 patch workflow 的项目。

#### 离线模式（单机游戏）

```csharp
public async UniTask InitializeOffline()
{
    // 1. 创建并初始化模块
    var assetModule = new YooAssetModule();
    await assetModule.InitializeAsync();

    // 2. 创建资源包
    var package = assetModule.CreatePackage("DefaultPackage");

    // 3. 初始化为离线模式
    var initOptions = new AssetPackageInitOptions(
        AssetPlayMode.Offline,
        new OfflinePlayModeParameters()
    );

    await package.InitializeAsync(initOptions);

    // 4. 加载资源
    using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/Enemy"))
    {
        await handle.Task;
        var enemy = package.InstantiateSync(handle);
    }
}
```

#### 主机模式（在线游戏带热更新）

```csharp
public async UniTask InitializeOnline()
{
    var assetModule = new YooAssetModule();
    await assetModule.InitializeAsync();

    var package = assetModule.CreatePackage("DefaultPackage");

    // 配置主机模式，指向您的 CDN
    var hostParams = new HostPlayModeParameters
    {
        BuildinQueryServices = new DefaultBuildinQueryServices(),
        RemoteServices = new DefaultRemoteServices("https://cdn.example.com/bundles")
    };

    var initOptions = new AssetPackageInitOptions(AssetPlayMode.Host, hostParams);
    await package.InitializeAsync(initOptions);
}
```

### Addressables 提供器

适用于已经使用 Unity Addressables 的项目。

```csharp
public async UniTask UseAddressables()
{
    // 1. 创建并初始化
    var assetModule = new AddressablesModule();
    await assetModule.InitializeAsync();

    // 2. 创建资源包
    var package = assetModule.CreatePackage("DefaultPackage");

    // 3. 加载资源（仅异步）
    using (var handle = package.LoadAssetAsync<GameObject>("MyAddressableKey"))
    {
        await handle.Task;
        if (handle.Asset != null)
        {
            var instance = await package.InstantiateAsync(handle).Task;
        }
    }
}
```

> [!NOTE]
> Addressables 的限制：
>
> - 不支持同步操作
> - 不支持 `IPatchService`
> - 不支持原生文件加载

### Resources 提供器

最适合快速原型开发或小型项目。

```csharp
public async UniTask UseResources()
{
    // 1. 创建并初始化（同步）
    var assetModule = new ResourcesModule();
    await assetModule.InitializeAsync();

    // 2. 创建资源包
    var package = assetModule.CreatePackage("DefaultPackage");

    // 3. 从 Resources 文件夹加载
    using (var handle = package.LoadAssetAsync<Sprite>("Icons/Coin"))
    {
        await handle.Task;
        myImage.sprite = handle.Asset;
    }
}
```

> [!WARNING]
> Resources 的限制：
>
> - 无法加载场景
> - 不支持热更新
> - 资源无法单独卸载
> - 不推荐用于正式项目

---

## 热更新工作流

### 高层 API（推荐）

`IPatchService` 提供完整的更新工作流，采用事件驱动架构：

```csharp
public async UniTask RunPatchFlow()
{
    // 获取补丁服务
    var patchService = assetModule.CreatePatchService("DefaultPackage");

    // 订阅事件
    patchService.PatchEvents.Subscribe(evt =>
    {
        var (eventType, args) = evt;

        switch (eventType)
        {
            case PatchEvent.FoundNewVersion:
                var versionArgs = (FoundNewVersionEventArgs)args;
                Debug.Log($"发现新版本！大小：{versionArgs.TotalDownloadSizeBytes} 字节");
                // 显示确认对话框，然后调用：
                // patchService.Download();
                break;

            case PatchEvent.DownloadProgress:
                var progressArgs = (DownloadProgressEventArgs)args;
                float progress = progressArgs.TotalDownloadSizeBytes <= 0
                    ? 0f
                    : (float)progressArgs.CurrentDownloadSizeBytes / progressArgs.TotalDownloadSizeBytes;
                Debug.Log($"进度：{progress:P0}");
                break;

            case PatchEvent.PatchDone:
                Debug.Log("更新完成！");
                break;

            case PatchEvent.PatchFailed:
                Debug.LogError("更新失败！");
                break;
        }
    });

    // 启动补丁流程
    await patchService.RunAsync(autoDownloadOnFoundNewVersion: false);
}
```

#### 调整下载参数

`RunAsync` 接受一个可选的 `PatchDownloadOptions` 用于调整下载阶段的参数。任何非正值字段都会回退到默认值，所以传 `default` 始终是安全的：

```csharp
await patchService.RunAsync(
    autoDownloadOnFoundNewVersion: true,
    downloadOptions: new PatchDownloadOptions
    {
        MaxConcurrentDownloads = 16,   // 默认 10
        FailedRetryCount       = 5,    // 默认 3
        RequestTimeoutSeconds  = 90,   // 默认 60
    });
```

#### 带内容可信校验的事务式 Patch

实现 `IAssetPatchTransactionService` 的 patch service 会提供更严格的事务 API。它保留旧的事件流，同时返回 `PatchRunResult`，并可以在下载完成后校验 provider-neutral 的 `ContentTrustManifest`：

```csharp
using CycloneGames.AssetManagement.Runtime.Trust;

var manifest = new ContentTrustManifest(
    version: "2026.07.09",
    entries: bundleEntries,
    rollbackVersion: "2026.07.08");

var trustOptions = new PatchContentTrustOptions(
    rootDirectory: downloadRoot,
    manifest: manifest,
    signatureVerifier: signatureVerifier,
    failurePolicy: PatchTrustFailurePolicy.RollbackManifestThenFail,
    clearUnusedCacheAfterRollback: true,
    failureBuffer: reusableFailureList);

var runOptions = new PatchRunOptions(
    autoDownloadOnFoundNewVersion: true,
    downloadOptions: PatchDownloadOptions.Default,
    trustOptions: trustOptions,
    appendTimeTicks: false);

if (patchService is IAssetPatchTransactionService transaction)
{
    PatchRunResult result = await transaction.RunAsync(runOptions, cancellationToken);
    if (result.Succeeded)
    {
        Debug.Log($"Patch applied: {result.PackageVersion}");
    }
}
```

事务状态机会通过 `PatchEvent.PatchStatesChanged` 报告公开的 `PatchWorkflowState` 值。下载前的 provider preflight 失败会返回结构化结果：package version 请求失败、manifest 更新失败和 downloader 创建失败分别返回 `PatchRunStatus.Failed` 与 `PatchFailureKind.PackageVersionRequestFailed`、`PatchFailureKind.ManifestUpdateFailed` 或 `PatchFailureKind.DownloaderCreationFailed`。Provider 下载完成但 `Succeed == false` 时，会返回 `PatchFailureKind.ProviderDownloadFailed`，保留 package version、下载数量、字节数和 provider error，供启动期恢复使用。内容可信校验失败仍会抛出 `PatchTrustVerificationException`；根据 `PatchTrustFailurePolicy`，服务可以直接失败、清理 unused cache、清理全部 cache、修复损坏 location，或将 active manifest 更新回 rollback version 后再失败。取消保持显式且由 provider 拥有：当服务已经拥有 downloader 时，cancellation token、`Cancel()` 和服务释放都会调用 `IDownloader.Cancel()`；显式取消会发布 `Cancelled` 状态，并让 task-based API 返回 `PatchRunStatus.Cancelled` 和 `PatchFailureKind.Cancelled` 的 `PatchRunResult`，而不是依赖非结构化异常路径。`Dispose()` 会取消服务拥有的 pending 或 active downloader，并在事件流释放后抑制后续事件发布。rollback 步骤刻意保持显式且 provider-neutral：它调用 `UpdatePackageManifestAsync(rollbackVersion)`，并可选调用 `ClearCacheFilesAsync(ClearCacheMode.Unused)`。

#### 崩溃安全 Patch Journal

移动操作系统可能在 downloader 活跃期间挂起或杀掉进程。桌面用户也可能在 manifest 更新、内容校验、修复或 cache 清理期间关闭进程。在这些情况下，C# `finally`、`Cancel()`、`Dispose()` 和 Unity application lifecycle callback 都不保证执行。因此 `AssetPatchService` 支持可选的 provider-neutral journal，将每个 transaction checkpoint 写入显式文件。

```csharp
string journalPath = AssetPatchJournalPaths.GetDefaultJournalPath(profile.PackageName);
var journalStore = new FileAssetPatchJournalStore(journalPath);
var patchService = new AssetPatchService(
    package,
    new AssetPatchJournalOptions(journalStore));

PatchRunResult result = await patchService.RunAsync(runOptions, cancellationToken);
```

下次启动时，产品 bootstrap 可以读取最后一个 checkpoint，并决定 resume、restart、verify、repair、rollback，或忽略已经完成的 transaction：

```csharp
if (journalStore.TryRead(out AssetPatchJournalRecord record, out string error))
{
    AssetPatchRecoveryRecommendation recovery =
        AssetPatchJournalRecovery.Analyze(record);

    if (recovery.Action == AssetPatchRecoveryAction.ResumeOrRestartDownload)
    {
        // Ask the active provider whether partial data can resume.
        // If the provider cannot resume safely, clear the partial payload and restart the patch.
    }
}
```

如果 bootstrap 不希望手写分支，可以使用 `AssetPatchRecoveryService` 作为可复用 executor。它的默认 policy 是 inspect-only：只读取和分类 journal，不会删除 checkpoint、清 cache、重启下载或 rollback manifest，除非产品层显式传入 policy。

```csharp
var recoveryPolicy = new AssetPatchRecoveryPolicy(
    rollbackFailedJournalWithVersion: true,
    clearUnusedCacheAfterRollback: true,
    clearJournalAfterSuccessfulRecovery: true);

var recoveryService = new AssetPatchRecoveryService(
    package,
    journalStore,
    recoveryPolicy);

AssetPatchRecoveryResult recoveryResult =
    await recoveryService.RecoverAsync(cancellationToken);

if (recoveryResult.Status == AssetPatchRecoveryStatus.RequiresOwnerAction)
{
    // Show repair UI, restart the patch flow, or ask the provider-specific layer
    // whether interrupted payloads can safely resume.
}
```

如果 active package 实现了 `IAssetPatchProviderReconciler`，`AssetPatchRecoveryService` 会自动把 provider capability 细节写入 `recoveryResult.ProviderReconciliation`。该 service 仍然保持保守：只有显式 recovery policy 和 provider capability matrix 同时允许某个精确操作时，才会执行 manifest rollback 或 cache cleanup。

| Provider | 指定版本 manifest update | Cache cleanup | 隔离版本预下载 | 恢复行为 |
| --- | --- | --- | --- | --- |
| YooAsset | 通过 `UpdatePackageManifestAsync(version)` 支持。 | adapter 支持 `All`、`Unused` 和 `ByTags`。 | 当前 adapter 不支持。 | 基于 active manifest 重启 patch；已完成 cache 的校验由 YooAsset 拥有。 |
| Addressables | 不支持 provider-neutral 的历史 catalog 指定版本 rollback。 | 仅支持 `ClearCacheMode.All`，并映射到 Unity 全局 cache 清理。 | 当前 adapter 不支持。 | 重新请求 dependency 并让 Unity cache 解析；不要假设安全 partial resume 或 scoped cleanup。 |
| Resources | 不支持。 | 不支持 provider-side patch cache。 | 不支持。 | 如果远程 patch 流程产生中断 journal，说明 provider 配置不匹配。 |

content-trust 失败处理也使用同一套 capability guard。trust policy 如果要求指定版本 rollback 或 unused-cache cleanup，而 active provider 无法按该语义执行，会显式失败。

journal 刻意保持很小，并以阶段为核心。它记录 schema version、sequence、package name、目标版本、rollback version、公开 patch stage、journal status、预期下载数量/字节数、内容可信状态、trust fingerprint、UTC ticks 和最后的终态错误。它不保存 CDN URL、凭据、账号标识、加密 key、原始 manifest document 或 provider SDK 对象。

默认路径 helper 写入：

```text
Application.persistentDataPath/CycloneGames/AssetManagement/PatchJournal/<package>.json
```

`FileAssetPatchJournalStore` 使用 `CycloneGames.IO` 原子文本写入。原子替换能防止正常元数据更新过程中生成 partial destination file，但平台耐久性限制仍然存在：在移动端或 WebGL 上，被杀进程可能丢失最新一次正在进行的写入；部分文件系统也会退回 delete-then-move。请把 journal 当作恢复 checkpoint，而不是唯一事实来源。真正激活下载内容前，仍必须 reconciliation provider cache、active manifest 和 content trust manifest。

#### Manifest 文档与签名 Payload

`ContentTrustManifestBuilder` 用于从构建产物或已下载文件创建 provider-neutral manifest。它会将相对 location 规范化为正斜杠，在确定性排序后校验重复 location，并通过 `CycloneGames.IO` 计算 SHA-256 或 XxHash64 条目。

`ContentTrustManifestCodec` 写入和读取紧凑 JSON 文档，用于传输与检查。JSON 文档不是签名安全边界。签名应基于 `ContentTrustManifestCanonicalPayload` bytes：该 payload 使用确定性 schema version、长度前缀 UTF-8 字符串、小端数字字段、排序后的 entries，并排除 `Signature` 字段。这样 JSON 空白、属性顺序、转义方式或 parser 行为都不会改变签名语义。

`ToJson` 和 `ToCanonicalPayloadBytes` 是 convenience API，会分配结果对象。大型构建、patch、CI 和 Editor 工具应优先使用 caller-owned 路径：`ContentTrustManifestCodec.AppendJson(builder, manifest, sortWorkspace: workspace)`、`ContentTrustManifestCanonicalPayload.WriteTo(manifest, stream, workspace)` 和 `IContentTrustManifestCanonicalSigner`。这些 API 允许调用方复用 `StringBuilder`、`Stream` 和排序 workspace，同时保持线程调度显式；manifest 层不会创建 worker thread，也不会保存隐藏全局 cache。

```csharp
ContentTrustManifest unsignedManifest = new ContentTrustManifestBuilder()
    .WithVersion("2026.07.09")
    .WithRollbackVersion("2026.07.08")
    .AddFile(contentRoot, "bundles/ui.bundle")
    .Build();

ContentTrustManifest signedManifest =
    ContentTrustManifestSignatureUtility.Sign(unsignedManifest, manifestSigner);

string json = ContentTrustManifestCodec.ToJson(signedManifest);
byte[] canonicalPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(signedManifest);
```

```csharp
var jsonBuilder = new StringBuilder(64 * 1024);
var sortWorkspace = new List<ContentTrustFileEntry>(1024);

ContentTrustManifestCodec.AppendJson(jsonBuilder, signedManifest, sortWorkspace: sortWorkspace);

using (var canonicalStream = new MemoryStream(64 * 1024))
{
    ContentTrustManifestCanonicalPayload.WriteTo(signedManifest, canonicalStream, sortWorkspace);
    // Pass the stream to the product signer, platform keystore, or CI signing tool.
}
```

#### 内容修复与自愈

内容修复保持 provider-neutral。`AssetRepairPlanner` 将内容可信校验失败转换成确定性的 `AssetRepairPlan`；`AssetRepairService` 执行该 plan：清理 unused cache，通过 `CreateDownloaderForLocations` 下载失败 location，并可选再次运行内容可信校验。该服务不知道 Addressables、YooAsset 或未来任何 provider SDK 类型。

取消是结构化且由 provider 拥有的流程。如果 repair `CancellationToken`、`Cancel()` 或 `Dispose()` 在 repair downloader 活跃期间中断流程，`AssetRepairService` 会调用 `IDownloader.Cancel()`，并返回 `AssetRepairRunStatus.Cancelled` 的 `AssetRepairRunResult`；调用方不需要从异常路径推断取消。服务释放后会抑制后续事件发布，避免 owner teardown 与已释放的 `R3` 事件流发生竞态。

```csharp
var repairService = new AssetRepairService(package);
var repairOptions = new AssetRepairOptions(
    downloadOptions: PatchDownloadOptions.Default,
    trustOptions: trustOptions,
    clearUnusedCacheBeforeDownload: true,
    recursiveDownloadLocations: true,
    verifyAfterRepair: true);

AssetRepairRunResult repair = await repairService.RepairAsync(
    manifest,
    reusableFailureList,
    repairOptions,
    cancellationToken);
```

只有 location-based 内容失败可修复：文件缺失、大小不匹配、hash 不匹配、hash 计算失败和 I/O 错误。manifest 层失败，例如 manifest 数据无效、签名被拒绝、不支持的 hash 算法或路径逃逸 trust root，仍然不可修复，应直接失败或 rollback。对于 patch transaction，`PatchTrustFailurePolicy.RepairLocationsThenReverify` 会修复 location 失败，并且只有修复后复验通过才允许 patch 成功。`RepairLocationsThenFail` 会执行同样的修复尝试，但 transaction 仍然失败，由调用方决定何时重试或重启。

#### Patch Profile 与产品策略

长期存在的产品策略应通过 profile 配置，而不是硬编码在 gameplay 代码里。`AssetPatchProfileAsset` 是 Unity authoring bridge；它会为当前平台或指定平台构建 `AssetPatchRuntimeProfile`。当产品层提供当前 trust manifest、签名校验器和可复用 failure buffer 后，runtime profile 再生成 `PatchRunOptions`。

```csharp
AssetPatchRuntimeProfile profile = patchProfileAsset.BuildRuntimeProfile();

PatchRunOptions runOptions = profile.CreateRunOptions(
    manifest: signedManifest,
    signatureVerifier: signatureVerifier,
    failureBuffer: reusableFailureList);

IPatchService patchService = assetModule.CreatePatchService(profile.PackageName);
PatchRunResult result = await ((IAssetPatchTransactionService)patchService)
    .RunAsync(runOptions, cancellationToken);
```

服务器、headless 或 DI composition 代码可以绕过 Unity asset，直接使用 builder：

```csharp
AssetPatchRuntimeProfile profile = new AssetPatchRuntimeProfileBuilder()
    .WithPackageName("Main")
    .WithPlatform(AssetPatchPlatform.Android)
    .WithDownloadPolicy(new AssetPatchDownloadPolicy(8, 3, 60))
    .WithTrustPolicy(new AssetPatchTrustPolicy(
        enabled: true,
        rootDirectory: contentRoot,
        PatchTrustFailurePolicy.RollbackManifestThenFail,
        rollbackVersionOverride: null,
        clearUnusedCacheAfterRollback: true))
    .Build();
```

profile 只拥有策略：package 名称、平台 override、下载并发/重试/超时、append-time 行为、content trust root、trust failure policy、rollback override 和 rollback 后 cache 清理。它不拥有 CDN 路由、登录状态、区域灰度规则、UI 决策或账号专属 entitlement 逻辑；这些应由产品层在创建 `PatchRunOptions` 前注入。

### 底层 API（精细控制）

用于自定义更新流程：

```csharp
// 检查更新
string latestVersion = await package.RequestPackageVersionAsync();

// 更新清单
bool updated = await package.UpdatePackageManifestAsync(latestVersion);

// 创建下载器
var downloader = package.CreateDownloaderForAll(downloadingMaxNumber: 10, failedTryAgain: 3);

// 监控进度
while (!downloader.IsDone)
{
    Debug.Log($"已下载：{downloader.CurrentDownloadBytes}/{downloader.TotalDownloadBytes}");
    await UniTask.Yield();
}

// 清理未使用的缓存
await package.ClearCacheFilesAsync(ClearCacheMode.Unused);
```

---

## 高级功能

### 内容可信校验

`CycloneGames.AssetManagement.Runtime.Trust` 提供 provider-neutral 的内容校验能力，用于下载后的 bundle、原生文件和外部 catalog payload 在进入运行时缓存前的可信检查。它适用于更新和补丁边界，不是 gameplay 热路径 API。

```csharp
using CycloneGames.AssetManagement.Runtime.Trust;

var entry = new ContentTrustFileEntry(
    "bundles/ui.bundle",
    sizeBytes: 1048576,
    ContentTrustHashAlgorithm.Sha256,
    expectedHashHex: "...");

ContentTrustVerificationResult result =
    ContentTrustVerifier.Shared.VerifyFile(downloadRoot, entry);

if (!result.Succeeded)
{
    // 拒绝更新、隔离文件，或触发重新下载。
}
```

支持的检查包括 manifest root containment、单文件路径穿越防护、文件大小、SHA-256、XxHash64，以及通过 `IContentTrustSignatureVerifier` 接入的可选签名策略。校验器使用 `CycloneGames.IO` 进行文件哈希与路径 containment，使用 `CycloneGames.Hash` 计算确定性 manifest fingerprint。`ContentTrustVerifier` 同时实现 `IContentTrustBufferVerifier`，因此 downloader 持有 pooled 或 sliced buffer 时可以直接校验 `ReadOnlySpan<byte>`，不需要复制到新的 byte array。成功的 hash 检查会直接比较 hash bytes 与 expected hex string，只有 mismatch 需要报告 actual hash 时才格式化字符串。它不写文件，也不持久化状态。

### 运行时缓存诊断

实现 `IAssetRuntimeDiagnostics` 的 package 会暴露无逐条枚举分配的聚合缓存快照，适用于 telemetry、压测 HUD 和自动内存治理：

```csharp
if (package is IAssetRuntimeDiagnostics diagnostics)
{
    AssetRuntimeCacheSnapshot snapshot = diagnostics.GetRuntimeCacheSnapshot();
    if (snapshot.IdleBudgetUsage > 0.8f)
    {
        package.TrimIdleCache(AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(30)));
    }
}
```

快照包含 package 名称、provider 名称、活跃句柄数、空闲句柄数、空闲字节估算、空闲字节预算和预算使用率。它刻意不枚举单个缓存条目；逐条分析仍应使用 Editor cache debugger。

### 运行时 Telemetry 记录器

`AssetRuntimeTelemetryRecorder` 会在内存中记录有容量上限的 `AssetRuntimeCacheSnapshot` 采样窗口。它由调用方持有，没有后台线程，也不会自行写文件。可在 player、压测构建、QA 构建或游戏内调试面板中使用，用于保留一小段本地资源压力记录：

```csharp
var recorder = new AssetRuntimeTelemetryRecorder(
    new AssetRuntimeTelemetryOptions(
        capacity: 512,
        minimumSampleInterval: TimeSpan.FromSeconds(1),
        includeZeroActivitySamples: false));

if (package is IAssetRuntimeDiagnostics diagnostics)
{
    recorder.TryRecord(diagnostics);
}
```

如果打包后的运行程序需要持久化当前有界窗口，使用 `AssetRuntimeTelemetryFileSink`，并由调用方传入可复用 scratch buffer：

```csharp
string path = AssetRuntimeTelemetryPaths.GetDefaultPersistentJsonLinesPath();
var sink = new AssetRuntimeTelemetryFileSink();
var samples = new AssetRuntimeTelemetrySample[recorder.Capacity];
var text = new StringBuilder(64 * 1024);

await sink.WriteJsonLinesAsync(path, recorder, samples, text, cancellationToken);
```

默认路径为：

```text
Application.persistentDataPath/CycloneGames/AssetManagement/Diagnostics/asset-runtime-telemetry.jsonl
```

该文件采用 JSON Lines 格式，每次 flush 都会以原子替换方式写入。这里刻意保存有界诊断窗口，而不是无限增长的日志流。删除该文件即可重置诊断记录；它不是事实来源，不应纳入 Git，并且可以通过重新记录恢复。重复 flush 会产生 JSON 序列化与 UTF-8 编码分配，因此应按产品定义的节奏写入，而不是每帧写入。

### 原生文件加载

加载非 Unity 文件，如 JSON、XML 或二进制数据：

```csharp
// 异步加载
using (var handle = package.LoadRawFileAsync("Config/settings.json"))
{
    await handle.Task;
    string jsonText = handle.ReadText();
    var settings = JsonUtility.FromJson<GameSettings>(jsonText);
}

// 同步加载
var handle = package.LoadRawFileSync("Data/level.bin");
byte[] data = handle.ReadBytes();
handle.Dispose();
```

### 场景管理

```csharp
// 加载场景
var sceneHandle = package.LoadSceneAsync("Assets/Scenes/Gameplay.unity");
await sceneHandle.Task;

// 场景现在已激活

// 卸载场景
await package.UnloadSceneAsync(sceneHandle);
```

当底层提供器支持延迟进入场景时，也可以使用手动激活模式：

```csharp
var sceneHandle = package.LoadSceneAsync(
    "Assets/Scenes/Gameplay.unity",
    LoadSceneMode.Single,
    SceneActivationMode.Manual);

// 等待提供器控制的预加载阶段。
await sceneHandle.Task;

// 当 FadeOut 或其他过渡完成后，再执行最终激活。
await sceneHandle.ActivateAsync();
```

说明：

- `SceneActivationState` 是跨 Provider 统一后的归一化状态，不保证与底层 SDK 的原始状态一一对应。
- Addressables 在加载完成后，通常可以报告 `WaitingForActivation`。
- YooAsset 的延迟进入本质上是“挂起加载再恢复”，因此在调用 `ActivateAsync()` 之前，句柄可能一直保持在 `Loading`。

### 批量加载

加载多个资源并追踪进度：

```csharp
using CycloneGames.AssetManagement.Runtime.Batch;

var group = new GroupOperation();

// 添加操作，可选权重
group.Add(package.LoadAssetAsync<Texture2D>("Tex1"), weight: 1f);
group.Add(package.LoadAssetAsync<Texture2D>("Tex2"), weight: 1f);
group.Add(package.LoadAssetAsync<AudioClip>("Music"), weight: 2f);

// 追踪进度
_ = TrackProgress(group);

await group.StartAsync();

async UniTask TrackProgress(GroupOperation op)
{
    while (!op.IsDone)
    {
        loadingBar.value = op.Progress;
        await UniTask.Yield();
    }
}
```

### 高性能资源缓存 (W-TinyLFU 架构)

资源管理系统使用低分配、三级分层（Active、Trial、Main）的缓存架构，用于提升缓存命中率并保持空闲内存驱逐的确定性。当前 API 形态允许的热路径会避免可规避分配；任何项目级 0GC 结论都应通过 Unity Profiler 或 allocation tests 验证。

- **Active (活跃池)**: 当前被游戏逻辑显式引用的资源（Refs > 0）。
- **Trial (试用池 - LRU)**: 被释放资产的观察期缓存池。
- **Main (主池 - LFU/LRU)**: 经过试用期且被频繁访问的"热"资源缓存。

你可以通过 **Bucket (内存桶)** 来实现确定性的分组内存释放：

```csharp
// 加载资源并将其分配到 "UI" 桶中
using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/MainMenu", bucket: "UI"))
{
    // ...
}

// 稍后，仅清理 "UI" 桶中的空闲资源来强制释放内存
package.ClearBucket("UI");
```

> [!NOTE]
> `ClearBucket` / `ClearBucketsByPrefix` 只会驱逐 `RefCount` 已归零的空闲句柄（位于 Trial 或 Main 池中）。仍在使用中的 Active 句柄**永远不会**被桶清理操作驱逐；这是为了从设计上避免悬垂引用。

#### 层级式桶路径

桶名称支持**以 `.` 分隔的层级命名**（如 `"UI.Scene.MainCity"`）。使用 `AssetBucketPath` 安全地组合和匹配路径：

```csharp
using CycloneGames.AssetManagement.Runtime;

// 使用稳定的点分隔约定组合层级桶名称
string bucket = AssetBucketPath.Combine("UI", "Scene");           // → "UI.Scene"
string sub    = AssetBucketPath.Combine("UI", "Scene", "MainCity"); // → "UI.Scene.MainCity"

// 清理单个精确匹配的桶
package.ClearBucket("UI.Scene.MainCity");

// 一次性清理某个桶及其所有后代
package.ClearBucketsByPrefix("UI");
// → 清理 "UI"、"UI.Scene"、"UI.Scene.MainCity" 等
```

#### AssetBucketScope（作用域加载）

`AssetBucketScope` 是一个轻量包装器，会为所有加载调用**预填充** `bucket`、`tag` 和 `owner`，消除重复的参数传递：

```csharp
// 创建作用域 —— 所有加载自动继承其 bucket/tag/owner
var uiScope = package.CreateBucketScope("UI", tag: "UIAsset", owner: "UIManager");

// 通过作用域加载 —— 无需重复传递 bucket/tag/owner
using (var handle = uiScope.LoadAssetAsync<GameObject>("Prefabs/MainMenu"))
{
    await handle.Task;
    var menu = uiScope.Package.InstantiateSync(handle);
}

// 为子系统创建子作用域（桶名变为 "UI.Shop"）
var shopScope = uiScope.CreateChild("Shop", owner: "ShopUI");
using (var handle = shopScope.LoadAssetAsync<Sprite>("Icons/Coin"))
{
    await handle.Task;
}

// 离开 UI 时，仅清理该作用域的桶层级
uiScope.ClearHierarchy();  // 清理 "UI" 及所有后代
// 或者只清理精确匹配的桶：
shopScope.Clear();          // 仅清理 "UI.Shop"
```

### 自动内存管理

除了条目数量上限，缓存还会执行**自动的、平台自适应的空闲内存预算**。当空闲（`RefCount == 0`）句柄的估算运行时占用超过预算时，即使条目数量未超限，它们也会被驱逐，从而在持续高压下保持内存有界：

| 设备档位（系统内存） | 空闲预算 |
| -------------------- | -------- |
| 桌面 / 主机 (>= 4 GB) | 512 MB   |
| 中端 (>= 2 GB)        | 256 MB   |
| 低端 / WebGL          | 96 MB    |

预算在启动时自动推导，无需配置。占用估算在 Editor / Development 构建中使用 `Profiler.GetRuntimeMemorySizeLong`，在正式包中使用零分配的启发式估算（贴图 / 网格 / 音频体积）。

缓存还订阅了 **`Application.lowMemory`**：收到操作系统内存压力信号时，会立即丢弃所有空闲句柄（绝不触碰仍在使用的 Active 句柄），在系统杀进程前争取出空间。

#### 覆写预算

默认是自动的，但宿主项目可以**在不修改包的前提下**覆写它 —— 模块级、按包、或运行时。优先级：单包覆盖 > 模块默认 > 自动。

```csharp
// 1) 模块级默认 —— 应用于该模块创建的每个包。
await module.InitializeAsync(new AssetManagementOptions(
    defaultIdleMemoryBudgetBytes: 256L * 1024 * 1024));   // 所有包 256 MB

// 2) 初始化时的单包覆盖（优先于模块默认）。
await package.InitializeAsync(new AssetPackageInitOptions(
    playMode, providerOptions,
    idleMemoryBudgetBytesOverride: 128L * 1024 * 1024));  // 128 MB

// 3) 运行时（初始化后任意时刻）—— 例如进重场景前收紧，之后放宽。
package.SetCacheIdleMemoryBudget(64L * 1024 * 1024);   // 64 MB
package.SetCacheIdleMemoryBudget(0);                   // 0 = 恢复平台自适应默认值
```

设置新预算后会立即驱逐空闲句柄以满足预算（绝不触碰 Active 句柄）。

### 查询缓存状态

使用零分配的 `IsAssetCached<T>` 在决定加载前检查资源是否已驻留：

```csharp
// 资源当前是否驻留（Active 或位于空闲 Trial/Main 池）。
if (package.IsAssetCached<GameObject>("Prefabs/Boss"))
{
    // LoadAssetAsync 将命中缓存 —— 无 bundle IO。
}
```

### 缓存保留策略与调度器

空闲（`RefCount == 0`）缓存被刻意设计为**策略中立**：它没有内置定时器，也没有写死的生命周期规则。空闲句柄只会在驱逐触发运行时被回收：条目数上限、空闲内存预算、`Application.lowMemory` 信号、显式 `ClearBucket` / `ClearBucketsByPrefix` / `UnloadUnusedAssetsAsync`、`DestroyAsync`，或调用者提供的缓存保留策略。

这让核心缓存保持确定性和可测试，同时仍能支持产品级差异化保留策略：UI 密集项目、开放世界流式加载、headless simulation、工具链、低内存设备和场景边界清理都可以使用不同规则，而不需要 fork provider。

#### 机制 hook：`TrimIdleCache`

```csharp
var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(60));
int evicted = package.TrimIdleCache(policy);
```

`TrimIdleCache` 不携带定时器，也没有帧驱动。由你决定何时调用它：HUD 按钮、场景边界、低内存事件、遥测驱动的内存治理器，或下面的调度器。

#### 组合保留规则

策略由 `IAssetCacheRetentionRule` 组成。常见规则通过 `AssetCacheRetentionRules` 创建；自定义规则可以读取 bucket、tag、owner、空闲时长、访问次数、估算内存和缓存层级。

```csharp
// 全局规则：驱逐空闲至少 120 秒的任意句柄。
// 场景规则：Scene.Battle 及其子 bucket 空闲 30 秒后即可驱逐。
var policy = AssetCacheRetentionPolicy.MatchingAny(
    AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(120)),
    AssetCacheRetentionRules.All(
        AssetCacheRetentionRules.Bucket("Scene.Battle", includeChildren: true),
        AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(30))));

int evicted = package.TrimIdleCache(policy);
```

当全局策略不应触碰某些常驻 bucket 时，使用 preserve rules：

```csharp
var policy = AssetCacheRetentionPolicy
    .IdleForAtLeast(TimeSpan.FromMinutes(5))
    .WithPreserveRules(AssetCacheRetentionRules.Bucket("UI.Persistent", includeChildren: true));
```

#### 方案 A：从代码或 DI 驱动

`AssetCacheRetentionScheduler`（程序集 `CycloneGames.AssetManagement.Runtime.CacheRetention`）是一个纯 C# 的 `IDisposable`，按固定真实时间间隔应用 `AssetCacheRetentionPolicy`。它使用 `UniTask`，拥有内部 `CancellationTokenSource`，且不会向宿主循环抛出异常。

```csharp
using CycloneGames.AssetManagement.Runtime.CacheRetention;

var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(120));
var scheduler = new AssetCacheRetentionScheduler(package, policy, TimeSpan.FromSeconds(30));
scheduler.Start();

// 关闭时：
scheduler.Dispose();

// package 可以延迟解析，便于 DI wiring。
var lazy = new AssetCacheRetentionScheduler(
    () => AssetManagementLocator.DefaultPackage,
    policy,
    TimeSpan.FromSeconds(30));
```

#### 方案 B：从场景对象驱动

对于 Inspector 配置的场景，把 `AssetCacheRetentionBehaviour` 挂到一个常驻 GameObject 上。它通过显式 `Bind(package)` 解析目标 package，并回退到 `AssetManagementLocator.DefaultPackage`。

| 字段 | 含义 | 默认值 |
| --- | --- | --- |
| `MinimumIdleSeconds` | 空闲年龄阈值；`0` 驱逐全部匹配的空闲句柄 | `120` |
| `CheckIntervalSeconds` | 每次扫描的间隔秒数（最小 1） | `30` |
| `Bucket` | 可选精确 bucket 或 bucket 根；空值表示全局应用 | 空 |
| `IncludeChildBuckets` | 设置 `Bucket` 时是否包含子 bucket | `true` |
| `LogEvictions` | 记录每次非空扫描驱逐的数量 | `false` |
| `AutoStartFromLocator` | 在 `OnEnable` 中使用 locator 的默认 package 启动 | `true` |

#### 方案 C：在场景切换时应用策略 (Navigathena)

当存在 Navigathena 集成（`CYCLONEGAMES_HAS_NAVIGATHENA`）时，把 `ApplyPackageCacheRetentionOperation` 作为切换的 `interruptOperation` 传入。它与 `UnloadPackageAssetsOperation` 互补，后者负责清理磁盘 bundle 文件。

```csharp
var policy = new AssetCacheRetentionPolicy(
    AssetCacheRetentionRules.Bucket("Scene.Battle", includeChildren: true));

var trim = new ApplyPackageCacheRetentionOperation(package, policy);
await navigator.Change(sceneId, interruptOperation: trim);
```

#### 方案 D：通过 VContainer 自动注册调度器

VContainer installer 可以替你注册并绑定 scheduler 生命周期，默认关闭。

```csharp
var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(120));
var installer = new AssetManagementVContainerInstaller(
    cacheRetentionOptions: new AssetCacheRetentionOptions(
        enabled: true,
        policy: policy,
        checkIntervalSeconds: 30));
installer.Install(builder);
```

### Bundle 加载并发

不设上限的 bundle 并发会在受限设备上引发 IO 抖动与内存尖峰。当并发参数未设置时，系统会应用**平台自适应默认值**（`AssetPlatformDefaults.BundleLoadingMaxConcurrency`）：WebGL = 4，Android / iOS = 8，桌面 / 主机 = `clamp(核心数 x 2, 8, 32)`。

```csharp
// 模块级默认值（应用于未单独覆盖的包）。
// int.MaxValue 或任何 <= 0 的值表示“使用平台自适应默认值”。
await module.InitializeAsync(new AssetManagementOptions(
    bundleLoadingMaxConcurrency: int.MaxValue));

// 单包覆盖（优先级高于模块级数值）。
await package.InitializeAsync(new AssetPackageInitOptions(
    playMode, providerOptions,
    bundleLoadingMaxConcurrencyOverride: 16));
```

### 资源追踪与元数据 (Metadata Tracking)

为了让运行时资源追踪更容易，所有加载 API 都接受 `tag` 和 `owner` 元数据参数。调用方传入已有字符串常量或缓存标识符时，这条路径可以保持无额外分配，同时仍能细粒度地追踪**是谁**加载了该资源，以及该资源的**用途**。

```csharp
// 加载资源并加上追踪标记
var handle = package.LoadAssetAsync<GameObject>("Prefabs/Hero",
    tag: "Character",
    owner: "PlayerSpawner"
);
```

**应用案例：UIFramework 集成**
`CycloneGames.UIFramework` 完美地应用了此设计。当打开 UI 窗口时，底层会自动为加载的资源打上标签：

- `owner`: 具体的 UI 窗口名称（如 `HomeUI`）
- `tag`: 资源分类（如 `UIConfig` 或 `UIPrefab`）

这样在调试器中，你可以一眼看出是哪个 UI 占用了内存。

### Provider Catalog 查询

部分 provider 暴露 catalog 侧的 tag 或 label，可以在真正加载资源前查询。需要把 provider tag 展开为具体加载位置时，使用可选能力 `IAssetCatalogQuery`：

```csharp
if (package is IAssetCatalogQuery catalogQuery)
{
    var locations = new List<string>(64);
    if (await catalogQuery.TryGetAssetLocationsByTagAsync("UI", locations))
    {
        for (int i = 0; i < locations.Count; i++)
        {
            // 基于 locations[i] 加载资源或生成计划。
        }
    }
}
```

这是低频规划 API，不是 gameplay 热路径 API。YooAsset 和 Addressables 可以把 provider catalog tag 或 label 映射为加载位置。Resources provider 没有运行时 catalog tag 系统，因此默认不实现该能力。Provider catalog tag 与加载 API 中用于缓存追踪和保留规则的运行时 `tag` metadata 是两个独立概念。

### 高级编辑器调试工具

`CycloneGames.AssetManagement` 提供了开发者友好的编辑器窗口，帮助你可视化缓存健康度并精准定位内存泄漏。

#### 1. 资源缓存调试器 (Asset Cache Debugger)

提供对整个 W-TinyLFU 缓存系统全局的透视。(`Tools/CycloneGames/AssetManagement/Asset Cache Debugger`)

- **层级 (Tier) 可视化**: 直观显示资源当前是处于 Active 状态、Trial 试用池，还是 Main 热缓存区。
- **可调整列宽**: 可在表头分隔处拖拽列宽，便于对齐较长的 location、provider、bucket、tag 和 owner。使用 **Reset Columns** 可恢复当前会话默认列宽。
- **元数据显示**: 直接显示和过滤 `Tag`、`Owner` 以及 `Bucket`。
- **引用计数异常警告**: 自动高亮显示引用计数异常偏高（> 8）的资源，警告你可能遗漏了 `Dispose()` 调用。
- **内存占用**: 表格提供单行估算内存列，Summary 页展示空闲池的实时内存占用与平台内存预算的对比。
- **选择与复制菜单**: 单击选择行，Ctrl/Cmd 单击切换多选，Shift 单击按当前可见顺序选择范围。右键行或表头可复制单个字段、选中行、完整行、TSV/JSON 输出，或复制当前可见的全部行；项目资源路径还可从菜单中 ping 到 Project 窗口。
- **稳定滚动位置**: 各缓存 tab 在切换时保留各自滚动位置，首次进入某个 tab 时从顶部开始。
- **智能汇总**: 按资源提供者、Tag 和 Owner 统计内存分布。

#### 2. 句柄泄漏追踪追踪器 (Handle Tracker)

微观级别监控每一个活动句柄的分配，并自动与缓存系统交叉比对。(`Tools/CycloneGames/AssetManagement/Asset Handle Tracker`)

- **智能状态识别**: 将每个长寿命句柄分类为 `Cached`（安全驻留于空闲池）、`Persistent`（开发者声明的长期驻留），或 `Leaked`（真正无法解释的泄漏）。
- **可调整列宽**: 可在表头分隔处拖拽列宽，按当前调试场景对齐 package、description、location、tag、owner、status 和 lifetime 等列。
- **Location 列**: 在可解析时从 handle description 中提取资源位置，方便与缓存行和项目资源进行对照。
- **选择与复制菜单**: 单击选择行，Ctrl/Cmd 单击切换多选，Shift 单击按当前可见顺序选择范围。右键行或表头可复制单个字段、选中行、完整行、TSV/JSON 输出、堆栈，或复制当前可见的全部行。
- **标记持久**: 右键任意行 -> **Mark Persistent**，为故意长期驻留的资源（DontDestroyOnLoad、引导 UI、主场景）消除误报泄漏。参见 [标记持久句柄](#标记持久句柄)。
- **内存泄漏堆栈**: 对已捕获堆栈的行右键选择 **Expand Stack Trace**，即可查看分配该句柄的 C# 调用堆栈（需先在工具栏启用 **Stack Traces**）。

#### 3. 场景追踪器 (Scene Tracker) (`Tools/CycloneGames/AssetManagement/Scene Tracker`)

实时查看每个被追踪的场景句柄：提供者、包、桶、激活状态（Loading / Waiting / Activated / Unload Pending / Error）、加载模式、激活模式、进度、引用数、存活时长和最近错误。用于捕捉卡在 `WaitingForActivation`、待卸载或 provider 操作失败的场景。

- **可调整列宽**: 可在表头分隔处拖拽 scene、provider、package、bucket、state、activation、progress、refs、age 和 error 等列宽。
- **选择与复制菜单**: 支持与缓存和句柄窗口一致的单击、Ctrl/Cmd 多选、Shift 范围选择，并提供选中行/可见行 TSV 和 JSON 导出。
- **场景资源定位**: 右键由 `Assets/` 或 `Packages/` 路径支持的行，可 ping 到对应 scene asset。

#### 4. 运行时治理面板 (Runtime Governance) (`Tools/CycloneGames/AssetManagement/Runtime Governance`)

将句柄、场景与缓存层级汇聚为单一仪表盘 —— 概览指标卡片、Top Buckets、最长寿的活动句柄、以及场景生命周期快照。压力测试时评估整体资源健康度最快的入口。

> 四个窗口都会先构建限速快照再重绘，使常规 Editor repaint 路径在大型项目中保持低分配和响应性。具体 Editor 布局的 allocation budget 应通过 Unity Profiler 验证。

#### 标记持久句柄

泄漏启发式会标记任何存活 > 5 分钟且未被缓存解释的句柄。对于故意长期驻留的资源（DontDestroyOnLoad、引导 UI、主场景基础设施），这是误报。将它们声明为持久，使其显示为 `Persistent` 而非 `Leaked`：

```csharp
using CycloneGames.AssetManagement.Runtime;

// 在启动时，为永久驻留的资源标记：
HandleTracker.MarkPersistent("Assets/.../UIFramework.prefab");
HandleTracker.MarkPersistent("Assets/.../EventSystem.prefab");

// 若某资源不再持久，可移除标记：
HandleTracker.UnmarkPersistent("Assets/.../UIFramework.prefab");
```

> 窗口里右键 -> **Mark Persistent** 很方便，但是**仅会话级**的（存于运行时静态状态，域重载 / 停止播放后重置）。如需永久生效，请在启动代码中调用 `HandleTracker.MarkPersistent`。

<img src="./Documents~/Doc_01.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_02.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_03.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_04.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_05.png" style="width: 100%; height: auto; max-width: 900px;" />

---

## 资源引用 (AssetRef / SceneRef)

### 为什么需要 AssetRef？

在实际项目中，使用裸字符串 `"Prefabs/Enemy"` 来引用资源是脆弱的：

- **没有类型安全** — 没有任何机制阻止你将一个 Texture 路径传给期望 Prefab 的方法。
- **无法追踪重命名** — 如果美术移动或重命名了资源，你只会在运行时得到静默失败。
- **不支持 Inspector** — 策划必须手动输入路径，无法拖拽。
- **直接的 `UnityEngine.Object` 引用** 会将资源拉入同一个 bundle，导致内存膨胀，并且无法实现按语言/按版本分包。

`AssetRef<T>` 和 `SceneRef` 解决了以上所有问题，同时在运行时保持轻量。它们是只存储 `location` 与 `guid` 的 `struct` 值，本身不会加载资源，也不会持有句柄。

### 设计原则

| 原则                        | 实现方式                                                                                                                |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| **纯数据键**                | `AssetRef` 存储 `location` + `guid`。它永远不加载、不缓存、不持有句柄。                                                 |
| **低分配**                  | `struct` 而非 `class`；数组和序列化 owner 仍会按常规分配，但每个引用值本身不会额外分配一个对象。                         |
| **通过 IAssetPackage 加载** | `package.LoadAsync(assetRef)` 返回 `IAssetHandle<T>`，复用已有的 ARC + W-TinyLFU 缓存。                                 |
| **GUID 自动修复**           | 编辑器 PropertyDrawer 每次显示时从 GUID 反查路径。如果资源被移动/重命名，存储的 location 会自动更新。                   |
| **SceneRef 独立类型**       | `SceneAsset` 是 editor-only 的，场景使用 `LoadSceneAsync` 而非 `LoadAssetAsync`。独立的 `SceneRef` 类型承载正确的语义。 |
| **构建时验证**              | `AssetRefValidator` 在发包前扫描所有 Prefab 和 ScriptableObject，检测失效的 GUID。                                      |

### 可用类型

| 类型          | 用途                                                                                        |
| ------------- | ------------------------------------------------------------------------------------------- |
| `AssetRef<T>` | 带类型的引用。Inspector 的 ObjectField 按 `T` 过滤（如 `AssetRef<AudioClip>` 只接受音频）。 |
| `AssetRef`    | 非泛型引用。用于数据驱动的配置表或运行时动态解析类型的场景。                                |
| `SceneRef`    | 场景引用。Inspector 的 ObjectField 过滤为 `SceneAsset`。                                    |

### 快速上手

#### 1. 在 Inspector 中声明引用

```csharp
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

public class EnemyConfig : ScriptableObject
{
    [Header("视觉")]
    [SerializeField] private AssetRef<GameObject> prefab;
    [SerializeField] private AssetRef<Material>   material;

    [Header("音频")]
    [SerializeField] private AssetRef<AudioClip> spawnSound;
    [SerializeField] private AssetRef<AudioClip> deathSound;

    [Header("场景")]
    [SerializeField] private SceneRef bossArena;

    // 供其他系统读取的只读访问器
    public AssetRef<GameObject> Prefab     => prefab;
    public AssetRef<AudioClip>  SpawnSound => spawnSound;
    public SceneRef             BossArena  => bossArena;
}
```

在 Inspector 中，每个字段都渲染为标准的 ObjectField —— 直接从 Project 窗口拖拽即可，按类型自动过滤。无需手动输入路径。

#### 2. 运行时加载资源

```csharp
public class EnemySpawner
{
    private readonly IAssetPackage package;
    private readonly EnemyConfig config;

    public async UniTask SpawnEnemy(Vector3 position)
    {
        // AssetRef<T> → IAssetHandle<T>，完全集成 ARC + 缓存
        using (var handle = package.LoadAsync(config.Prefab, bucket: "Gameplay"))
        {
            await handle.Task;
            var enemy = package.InstantiateSync(handle);
            enemy.transform.position = position;
        }
    }
}
```

#### 3. 加载场景（Navigathena 集成）

```csharp
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.Navigathena;

public class LevelLoader
{
    private readonly IAssetPackage package;
    private readonly ISceneNavigator navigator;

    public async UniTask LoadBossArena(EnemyConfig config)
    {
        const string sceneBucket = "Scene.BossArena";

        // 方式 A：通过 IAssetPackage 直接加载场景
        var sceneHandle = package.LoadSceneAsync(config.BossArena, bucket: sceneBucket);
        await sceneHandle.Task;

        // 方式 B：通过 Navigathena 进行场景导航（push/pop/change）
        await navigator.Push(config.BossArena.ToSceneIdentifier(package, bucket: sceneBucket));
    }
}
```

#### 4. 向后兼容

`AssetRef<T>`、`AssetRef` 和 `SceneRef` 都支持 `implicit operator string`，因此可以直接用于已有的基于 `string` 的 API：

```csharp
// 以下写法完全等价：
package.LoadAssetAsync<GameObject>(config.Prefab.Location);
package.LoadAssetAsync<GameObject>(config.Prefab);  // 隐式 string 转换
package.LoadAsync(config.Prefab);                    // 扩展方法（推荐）
```

#### 5. 支持所有资源类型

任何继承自 `UnityEngine.Object` 的类型都可使用：

```csharp
[SerializeField] AssetRef<GameObject>       prefab;          // 预制体
[SerializeField] AssetRef<ScriptableObject> config;          // 任意 ScriptableObject
[SerializeField] AssetRef<YarnProject>      dialogue;        // Yarn Spinner 对话项目
[SerializeField] AssetRef<Sprite>           icon;            // 精灵图
[SerializeField] AssetRef<AudioClip>        clip;            // 音频
[SerializeField] AssetRef<Material>         mat;             // 材质
[SerializeField] AssetRef<AnimationClip>    anim;            // 动画片段
[SerializeField] AssetRef<TextAsset>        textFile;        // 文本资源
[SerializeField] SceneRef                   scene;           // 场景
```

### 构建验证

发包前，通过菜单验证所有引用：

**`Tools > CycloneGames > AssetManagement > Validate All AssetRefs`**

该功能会扫描项目中的所有 Prefab 和 ScriptableObject：

- **失效引用**: GUID 无法解析 → 以 Error 形式输出日志。
- **过期路径**: 资源被移动/重命名但 GUID 仍然有效。Drawer 只显示警告图标，不会在绘制时修改资产；`ValidateAll()` 会显式修复 location。

CI 或报告流程如果不允许修改项目资产，请调用 `AssetRefValidator.ValidateAllReportOnly()`。允许自动修复过期 location 时，再调用 `AssetRefValidator.ValidateAll()`。

---

## API 参考

### IAssetModule

| 方法                       | 说明                        |
| -------------------------- | --------------------------- |
| `InitializeAsync(options)` | 初始化资源系统              |
| `DestroyAsync()`           | 确定性清理并释放资源        |
| `CreatePackage(name)`      | 创建新的资源包              |
| `GetPackage(name)`         | 获取已存在的资源包          |
| `RemovePackageAsync(name)` | 移除并销毁资源包            |
| `CreatePatchService(name)` | 创建由 provider 支撑的补丁服务 |

### IPatchService / IAssetPatchTransactionService

| 成员 | 说明 |
| --- | --- |
| `RunAsync(autoDownload, downloadOptions)` | 旧的事件驱动 patch 流程。 |
| `Download()` | 通过旧的 fire-and-forget 路径启动待处理下载。 |
| `Cancel()` | 取消当前 active 或 pending provider downloader，并将 transaction 标记为 cancelled。 |
| `Dispose()` | 取消服务拥有的 provider downloader 状态，并释放 patch 事件流。 |
| `RunAsync(PatchRunOptions)` | 带结果报告和可选内容可信校验的事务式 patch 流程。 |
| `DownloadAsync()` | 完成一个待处理事务并返回 `PatchRunResult`。 |
| `PatchFailureKind` | 对结构化 patch result 失败进行分类，例如 provider preflight 失败、provider 下载失败或显式取消。 |

### AssetPatch Journal

| 成员 | 说明 |
| --- | --- |
| `AssetPatchJournalOptions` | patch service 的可选 journal store 与写入失败策略配置。 |
| `FileAssetPatchJournalStore` | 使用原子替换把最新 patch checkpoint 存为显式 JSON 文件。 |
| `AssetPatchJournalCodec` | 将 `AssetPatchJournalRecord` 转换为 journal JSON schema，或从 JSON 读取。 |
| `AssetPatchJournalRecovery.Analyze(record)` | 将最后一个 checkpoint 映射成确定性的恢复建议。 |
| `AssetPatchRecoveryService` | 使用显式产品策略，从 journal 执行保守的启动期恢复。 |
| `AssetPatchRecoveryPolicy` | 控制是否清理终态 journal、是否 rollback 失败 manifest，或是否清理中断下载 cache。 |
| `IAssetPatchProviderReconciler` | 允许 provider 报告恢复能力和 provider-specific 的重启/续传建议，同时不泄漏 provider SDK 类型。 |
| `AssetPatchProviderReconciliationCapabilities` | 声明 provider 是否支持指定版本 manifest update、scoped cache cleanup、provider-owned download cache 和隔离版本预下载。 |
| `AssetPatchProviderReconciliationResult` | 在 `AssetPatchRecoveryResult` 中携带 provider reconciliation 状态、说明和 capability 数据。 |
| `AssetPatchJournalPaths.GetDefaultJournalPath(packageName)` | 为 package 构建默认 persistent-data journal 路径。 |

### AssetPatchProfileAsset / AssetPatchRuntimeProfile

| 成员 | 说明 |
| --- | --- |
| `BuildRuntimeProfile()` | 从 Unity authoring asset 构建当前平台的 runtime patch profile。 |
| `BuildRuntimeProfile(platform)` | 为指定平台构建 runtime patch profile。 |
| `CreateRunOptions(manifest, verifier, signatureVerifier, failureBuffer)` | 将 runtime profile 和产品层提供的 trust data 转换为 `PatchRunOptions`。 |
| `AssetPatchRuntimeProfileBuilder` | 不依赖 Unity asset 创建同等 runtime profile，适用于 DI/headless/server composition。 |

### ContentTrustManifestBuilder / Codec

| 成员 | 说明 |
| --- | --- |
| `ContentTrustManifestBuilder.AddFile(root, location, algorithm)` | 添加相对文件条目并计算内容 hash。 |
| `ContentTrustManifestBuilder.Build()` | 生成排序后的 provider-neutral trust manifest。 |
| `ContentTrustManifestCodec.ToJson(manifest)` | 将 manifest document 写为存储或传输用 JSON。 |
| `ContentTrustManifestCodec.AppendJson(builder, manifest, sortWorkspace)` | 将 JSON 追加到 caller-owned buffer，用于大型 build、Editor 和 CI 工具。 |
| `ContentTrustManifestCodec.FromJson(json)` | 从 JSON 读取 manifest document。 |
| `ContentTrustManifestCodec.ToCanonicalPayloadBytes(manifest)` | 生成排除 signature 字段的确定性签名 bytes。 |
| `ContentTrustManifestCanonicalPayload.WriteTo(manifest, stream, sortWorkspace)` | 将确定性签名 bytes 写入 caller-owned stream。 |
| `ContentTrustManifestSignatureUtility.Sign(manifest, signer)` | Convenience 路径，通过注入的产品/平台 signer 对已分配的 canonical bytes 签名。 |
| `ContentTrustManifestSignatureUtility.SignCanonical(manifest, signer)` | 大型 manifest 路径，允许 `IContentTrustManifestCanonicalSigner` 自行管理 stream、pooled buffer 或平台 crypto handle。 |

### IAssetRepairService

| 成员 | 说明 |
| --- | --- |
| `Cancel()` | 取消当前 active repair downloader，并将 repair 操作标记为 cancelled。 |
| `RepairAsync(manifest, failures, options)` | 从内容可信校验失败构建 repair plan，并执行 location repair。 |
| `RepairAsync(plan, options)` | 执行预先构建的 provider-neutral repair plan。 |
| `RepairEvents` | 报告阶段变化、plan 创建、下载进度、完成和失败。 |
| `Dispose()` | 取消服务拥有的 repair downloader 状态，并释放 repair 事件流。 |

### IAssetPackage

| 方法                           | 说明                                                 |
| ------------------------------ | ---------------------------------------------------- |
| `InitializeAsync(options)`     | 初始化资源包                                         |
| `DestroyAsync()`               | 销毁资源包                                           |
| `LoadAssetAsync<T>(...)`       | 异步加载资源 (支持 `bucket`/`tag`/`owner`)           |
| `LoadAssetSync<T>(...)`        | 同步加载资源 (支持 `bucket`/`tag`/`owner`)           |
| `LoadAllAssetsAsync<T>(...)`   | 加载指定位置的所有资源 (支持 `bucket`/`tag`/`owner`) |
| `IsAssetCached<T>(location)`   | 零分配驻留检查（Active 或空闲 Trial/Main 池）    |
| `InstantiateAsync(handle)`     | 异步实例化预制体                                     |
| `InstantiateSync(handle)`      | 同步实例化                                           |
| `LoadSceneAsync(location)`     | 加载场景                                             |
| `UnloadSceneAsync(handle)`     | 卸载场景                                             |
| `LoadRawFileAsync(location)`   | 加载原生文件                                         |
| `UnloadUnusedAssetsAsync()`    | 全局清扫所有未使用的资源                             |
| `SetCacheIdleMemoryBudget(bytes)` | 运行时覆写空闲内存预算（0 = 恢复自动）             |
| `TrimIdleCache(policy)` | 应用空闲缓存保留策略，返回驱逐数量 |
| `ClearBucket(bucket)`          | 驱逐精确匹配桶名的空闲句柄                           |
| `ClearBucketsByPrefix(prefix)` | 驱逐匹配桶前缀及其所有后代的空闲句柄                 |

### HandleTracker（诊断）

| 成员 | 说明 |
| --- | --- |
| `Enabled` | 句柄追踪总开关。 |
| `EnableStackTrace` | 捕获分配堆栈（较慢；排查泄漏时启用）。 |
| `MarkPersistent(location)` | 将故意长期驻留的资源排除出泄漏启发式。 |
| `UnmarkPersistent(location)` / `ClearPersistent()` | 移除持久标记。 |
| `IsPersistent(location)` | 查询某位置是否被标记为持久。 |

### IAssetRuntimeDiagnostics

| 成员 | 说明 |
| --- | --- |
| `GetRuntimeCacheSnapshot()` | 返回运行时聚合缓存计数，不进行逐条缓存枚举。 |

### AssetRuntimeTelemetryRecorder

| 成员 | 说明 |
| --- | --- |
| `TryRecord(snapshot)` / `TryRecord(diagnostics)` | 在采样间隔和空活动过滤允许时记录一个样本。 |
| `CopyTo(buffer)` | 将最新的有界窗口按从旧到新的顺序复制到调用方持有的 buffer。 |
| `TryGetLatest(out sample)` | 无分配读取最新样本。 |
| `Clear()` | 清空内存 telemetry 样本并重置序号。 |

### AssetRuntimeTelemetryFileSink

| 成员 | 说明 |
| --- | --- |
| `WriteJsonLinesAsync(path, recorder, samples, text, token)` | 使用调用方持有的 scratch buffer，将 recorder 当前有界窗口以 JSON Lines 原子写入。 |

### 脚本定义符号

这些符号会根据已安装的包自动定义：

| 符号                   | 定义时机               |
| ---------------------- | ---------------------- |
| `CYCLONEGAMES_HAS_YOOASSET`     | 通过 UPM 安装了 YooAsset 包 |
| `CYCLONEGAMES_HAS_ADDRESSABLES` | 通过 UPM 安装了 Addressables 包 |
| `CYCLONEGAMES_HAS_VCONTAINER`   | 通过 UPM 安装了 VContainer 包 |
| `CYCLONEGAMES_HAS_NAVIGATHENA`  | 通过 UPM 安装了 Navigathena 包 |
| `CYCLONEGAMES_HAS_VCONTAINER_UNITASK` | VContainer integration assembly 中可使用 UniTask |

---

## 最佳实践

1. **始终释放句柄** - 使用 `using` 语句或手动调用 `Dispose()`
2. **优先使用异步加载** - 同步加载会阻塞主线程
3. **按产品选择 provider** - Resources 适合内置原型，Addressables 适合已采用 Unity catalog pipeline 的项目，YooAsset 适合明确采用其 patch workflow 的项目
4. **开发时启用句柄追踪** - 帮助尽早发现内存泄漏
5. **使用 DI 容器** - 将 `IAssetModule` 注册为单例以保持架构清晰
6. **标记持久资源** - 通过 `HandleTracker.MarkPersistent` 声明 DontDestroyOnLoad / 引导 / 主场景资源，使泄漏检测保持高信号
7. **记录有界 player telemetry** - 在打包构建中使用 `AssetRuntimeTelemetryRecorder` 记录缓存压力，并由产品显式选择 flush 节奏和存储路径
