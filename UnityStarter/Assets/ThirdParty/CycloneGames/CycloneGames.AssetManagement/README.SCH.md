# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

一个为 Unity 设计的 DI 优先、接口驱动的统一资源管理抽象层。它将游戏逻辑与底层资源系统（YooAsset、Addressables 或 Resources）解耦，让您编写更清晰、更易移植的高性能代码。

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
- [API 参考](#api-参考)

---

## 环境要求

| 依赖项 | 必需 | 说明 |
|--------|------|------|
| Unity | 2022.3+ | 最低版本要求 |
| UniTask | 是 | `com.cysharp.unitask` - 异步支持 |
| YooAsset | 可选 | `com.tuyoogame.yooasset` - 推荐的提供器 |
| Addressables | 可选 | `com.unity.addressables` - 备选提供器 |
| VContainer | 可选 | `jp.hadashikick.vcontainer` - DI 集成 |
| R3 | 可选 | `com.cysharp.r3` - 用于 `IPatchService` 事件 |

## 安装

1. 将包导入您的 Unity 项目
2. 模块会通过程序集定义引用自动检测可用的提供器
3. 无需手动配置脚本定义符号

---

## 快速开始

本节将指导您快速上手加载第一个资源。

### 第一步：初始化（游戏启动时执行一次）

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using YooAsset;

public class GameBootstrap
{
    // 存储模块引用以便后续访问
    public static IAssetModule AssetModule { get; private set; }
    
    public async UniTask Initialize()
    {
        // 创建并初始化模块（只执行一次）
        AssetModule = new YooAssetModule();
        await AssetModule.InitializeAsync();

        // 创建并初始化资源包（每个包只执行一次）
        var package = AssetModule.CreatePackage("DefaultPackage");
        
        var initOptions = new AssetPackageInitOptions(
            AssetPlayMode.Offline,
            new OfflinePlayModeParameters()
        );
        
        await package.InitializeAsync(initOptions);
    }
}
```

### 第二步：加载资源（在游戏任意位置）

```csharp
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
    +-- YooAssetModule（推荐）
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

| 接口 | 用途 |
|------|------|
| `IAssetModule` | 资源系统入口，创建和管理资源包 |
| `IAssetPackage` | 处理所有资源操作：加载、实例化、场景 |
| `IAssetHandle<T>` | 表示已加载的资源，可释放以管理内存 |
| `IPatchService` | 高层热更新工作流（仅 YooAsset） |

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

| 功能 | YooAsset | Addressables | Resources |
|------|----------|--------------|-----------|
| 同步加载 | 支持 | 不支持 | 支持 |
| 异步加载 | 支持 | 支持 | 支持 |
| 热更新 | 支持 | 有限 | 不支持 |
| 场景加载 | 支持 | 支持 | 不支持 |
| 原生文件加载 | 支持 | 不支持 | 不支持 |
| 推荐用途 | 正式项目 | 已有项目 | 原型开发 |

---

## 使用示例

### YooAsset 提供器

YooAsset 是推荐的提供器，具有完整的功能支持。

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
                Debug.Log($"进度：{progressArgs.Progress:P0}");
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

### LRU 缓存

带 LRU 淘汰策略的自动缓存：

```csharp
using CycloneGames.AssetManagement.Runtime.Cache;

var cache = new AssetCacheService(package, maxEntries: 100);

// 从缓存获取（未缓存时自动加载）
var sprite = cache.Get<Sprite>("Icons/Coin");

// 释放特定项
cache.TryRelease("Icons/Coin");

// 清空全部
cache.Clear();
```

### 句柄追踪（调试）

追踪活动句柄以检测泄漏：

```csharp
// 启用追踪（在加载资源前执行）
HandleTracker.Enabled = true;
HandleTracker.EnableStackTrace = true; // 用于详细泄漏分析

// 稍后检查泄漏
var report = HandleTracker.GetActiveHandlesReport();
Debug.Log(report);
```

---

## API 参考

### IAssetModule

| 方法 | 说明 |
|------|------|
| `InitializeAsync(options)` | 初始化资源系统 |
| `Destroy()` | 清理并释放资源 |
| `CreatePackage(name)` | 创建新的资源包 |
| `GetPackage(name)` | 获取已存在的资源包 |
| `RemovePackageAsync(name)` | 移除并销毁资源包 |
| `CreatePatchService(name)` | 创建补丁服务（仅 YooAsset） |

### IAssetPackage

| 方法 | 说明 |
|------|------|
| `InitializeAsync(options)` | 初始化资源包 |
| `DestroyAsync()` | 销毁资源包 |
| `LoadAssetAsync<T>(location)` | 异步加载资源 |
| `LoadAssetSync<T>(location)` | 同步加载资源 |
| `LoadAllAssetsAsync<T>(location)` | 加载指定位置的所有资源 |
| `InstantiateAsync(handle)` | 异步实例化预制体 |
| `InstantiateSync(handle)` | 同步实例化（零 GC） |
| `LoadSceneAsync(location)` | 加载场景 |
| `UnloadSceneAsync(handle)` | 卸载场景 |
| `LoadRawFileAsync(location)` | 加载原生文件 |
| `UnloadUnusedAssets()` | 卸载未使用的资源 |

### 脚本定义符号

这些符号会根据已安装的包自动定义：

| 符号 | 定义时机 |
|------|----------|
| `YOOASSET_PRESENT` | 已安装 YooAsset 包 |
| `ADDRESSABLES_PRESENT` | 已安装 Addressables 包 |
| `VCONTAINER_PRESENT` | 已安装 VContainer 包 |

---

## 最佳实践

1. **始终释放句柄** - 使用 `using` 语句或手动调用 `Dispose()`
2. **优先使用异步加载** - 同步加载会阻塞主线程
3. **选择合适的提供器** - 正式项目用 YooAsset，原型开发用 Resources
4. **开发时启用句柄追踪** - 帮助尽早发现内存泄漏
5. **使用 DI 容器** - 将 `IAssetModule` 注册为单例以保持架构清晰
