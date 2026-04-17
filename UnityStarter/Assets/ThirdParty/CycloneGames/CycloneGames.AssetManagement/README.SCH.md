# CycloneGames.AssetManagement

[English](./README.md) | 简体中文

一个为 Unity 设计的 DI 优先、接口驱动的统一资源管理抽象层。它将游戏逻辑与底层资源系统（YooAsset、Addressables 或 Resources）解耦，让您编写更清晰、更易移植的高性能代码。

基于 **W-TinyLFU** 架构，它提供了确定性的内存管理、细粒度的资源追踪（通过 Tag/Owner 元数据），并配备了强大的可视化编辑器调试工具（缓存调试器与句柄泄漏追踪器），助您彻底告别内存泄漏。

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
| YooAsset     | 可选    | `com.tuyoogame.yooasset` - 推荐的提供器      |
| Addressables | 可选    | `com.unity.addressables` - 备选提供器        |
| VContainer   | 可选    | `jp.hadashikick.vcontainer` - DI 集成        |
| R3           | 可选    | `com.cysharp.r3` - 用于 `IPatchService` 事件 |

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
| 推荐用途     | 正式项目 | 已有项目     | 原型开发  |

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

### 高性能资源缓存 (W-TinyLFU 架构)

资源管理系统内置了零 GC、三级分层（Active、Trial、Main）的缓存架构，最大化缓存命中率，同时提供确定性的内存管理策略，且没有任何运行时开销：

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
> `ClearBucket` / `ClearBucketsByPrefix` 只会驱逐 `RefCount` 已归零的空闲句柄（位于 Trial 或 Main 池中）。仍在使用中的 Active 句柄**永远不会**被桶清理操作驱逐 —— 这是为了从设计上避免悬垂引用。

#### 层级式桶路径

桶名称支持**以 `.` 分隔的层级命名**（如 `"UI.Scene.MainCity"`）。使用 `AssetBucketPath` 安全地组合和匹配路径：

```csharp
using CycloneGames.AssetManagement.Runtime;

// 组合层级桶名称（非空段时零分配）
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

### 资源追踪与元数据 (Metadata Tracking)

为了让运行时资源的追踪变得极其简单，所有加载 API 均支持零 GC 的 `tag` 和 `owner` 参数。这让你能够细粒度地追踪**是谁**加载了该资源，以及该资源的**用途**。

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

### 高级编辑器调试工具

`CycloneGames.AssetManagement` 提供了开发者友好的编辑器窗口，帮助你可视化缓存健康度并精准定位内存泄漏。

#### 1. 资源缓存调试器 (Asset Cache Debugger)

提供对整个 W-TinyLFU 缓存系统全局的透视。(`Tools/CycloneGames/AssetManagement/Asset Cache Debugger`)

- **层级 (Tier) 可视化**: 直观显示资源当前是处于 Active 状态、Trial 试用池，还是 Main 热缓存区。
- **元数据显示**: 直接显示和过滤 `Tag`、`Owner` 以及 `Bucket`。
- **引用计数异常警告**: 自动高亮显示引用计数异常偏高（> 8）的资源，警告你可能遗漏了 `Dispose()` 调用。
- **智能汇总**: 按资源提供者、Tag 和 Owner 统计内存分布。

#### 2. 句柄泄漏追踪追踪器 (Handle Tracker)

微观级别监控每一个活动句柄的分配，并自动与缓存系统交叉比对。(`Tools/CycloneGames/AssetManagement/Asset Handle Tracker`)

- **智能状态识别**: 自动判断引用计数为 0 的句柄是安全驻留在空闲缓存中 (`Cached`)，还是真正的内存泄漏 (`Leaked`)。
- **内存泄漏堆栈**: 点击展开任何泄露的句柄，直接跳转到分配该句柄的精准 C# 代码行。

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

`AssetRef<T>` 和 `SceneRef` 解决了以上所有问题，同时在**运行时零开销**（struct 类型，仅存储两个字符串）。

### 设计原则

| 原则                        | 实现方式                                                                                                                |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| **纯数据键**                | `AssetRef` 存储 `location` + `guid`。它永远不加载、不缓存、不持有句柄。                                                 |
| **零 GC**                   | `struct` 而非 `class`。10 万个引用 = 零堆对象。                                                                         |
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
        // 方式 A：通过 IAssetPackage 直接加载场景
        var sceneHandle = package.LoadSceneAsync(config.BossArena);
        await sceneHandle.Task;

        // 方式 B：通过 Navigathena 进行场景导航（push/pop/change）
        await navigator.Push(config.BossArena.ToSceneIdentifier(package));
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
- **过期路径**: 资源被移动/重命名但 GUID 仍然有效 → 自动修复 location。

可通过在构建脚本中调用 `AssetRefValidator.ValidateAll()` 将其集成到 CI/CD 流水线。

---

## API 参考

### IAssetModule

| 方法                       | 说明                        |
| -------------------------- | --------------------------- |
| `InitializeAsync(options)` | 初始化资源系统              |
| `Destroy()`                | 清理并释放资源              |
| `CreatePackage(name)`      | 创建新的资源包              |
| `GetPackage(name)`         | 获取已存在的资源包          |
| `RemovePackageAsync(name)` | 移除并销毁资源包            |
| `CreatePatchService(name)` | 创建补丁服务（仅 YooAsset） |

### IAssetPackage

| 方法                           | 说明                                                 |
| ------------------------------ | ---------------------------------------------------- |
| `InitializeAsync(options)`     | 初始化资源包                                         |
| `DestroyAsync()`               | 销毁资源包                                           |
| `LoadAssetAsync<T>(...)`       | 异步加载资源 (支持 `bucket`/`tag`/`owner`)           |
| `LoadAssetSync<T>(...)`        | 同步加载资源 (支持 `bucket`/`tag`/`owner`)           |
| `LoadAllAssetsAsync<T>(...)`   | 加载指定位置的所有资源 (支持 `bucket`/`tag`/`owner`) |
| `InstantiateAsync(handle)`     | 异步实例化预制体                                     |
| `InstantiateSync(handle)`      | 同步实例化（零 GC）                                  |
| `LoadSceneAsync(location)`     | 加载场景                                             |
| `UnloadSceneAsync(handle)`     | 卸载场景                                             |
| `LoadRawFileAsync(location)`   | 加载原生文件                                         |
| `UnloadUnusedAssetsAsync()`    | 全局清扫所有未使用的资源                             |
| `ClearBucket(bucket)`          | 驱逐精确匹配桶名的空闲句柄                           |
| `ClearBucketsByPrefix(prefix)` | 驱逐匹配桶前缀及其所有后代的空闲句柄                 |

### 脚本定义符号

这些符号会根据已安装的包自动定义：

| 符号                   | 定义时机               |
| ---------------------- | ---------------------- |
| `YOOASSET_PRESENT`     | 已安装 YooAsset 包     |
| `ADDRESSABLES_PRESENT` | 已安装 Addressables 包 |
| `VCONTAINER_PRESENT`   | 已安装 VContainer 包   |

---

## 最佳实践

1. **始终释放句柄** - 使用 `using` 语句或手动调用 `Dispose()`
2. **优先使用异步加载** - 同步加载会阻塞主线程
3. **选择合适的提供器** - 正式项目用 YooAsset，原型开发用 Resources
4. **开发时启用句柄追踪** - 帮助尽早发现内存泄漏
5. **使用 DI 容器** - 将 `IAssetModule` 注册为单例以保持架构清晰
