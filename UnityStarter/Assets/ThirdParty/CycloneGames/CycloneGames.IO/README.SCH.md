# CycloneGames.IO

CycloneGames.IO 为 CycloneGames foundation modules 提供托管、面向 Unity 的文件与路径工具。它用显式的 `System.IO` 和 `FileStream` API 替代此前对 embedded `jp.hadashikick.unio` 包的直接依赖。

命名空间：`CycloneGames.IO.Runtime`

## 设计目标

- 让文件访问保持托管、可审计、容易排查。
- 避免把 unsafe/native-memory 文件工具作为基础框架默认依赖。
- 为构建工具、缓存、设置和资源元数据提供 low-GC 的哈希、比较和复制 API。
- 通过 `FilePathUtility` 集中处理 Unity 平台路径 URI。
- 显式声明依赖：`CycloneGames.Hash.Core` 提供 `XxHash64`，`CycloneGames.Logger` 提供诊断输出。

## 程序集

| Assembly | 路径 | 用途 |
| --- | --- | --- |
| `CycloneGames.IO.Runtime` | `Runtime/` | 文件读写、哈希、比较、复制和 Unity path URI helper。 |
| `CycloneGames.IO.Editor` | `Editor/` | 用于正确性、吞吐和 GC 检查的 Editor benchmark window。 |

`CycloneGames.IO.Runtime` 引用 UnityEngine，因为 `FilePathUtility` 使用 `Application.streamingAssetsPath` 和 `Application.persistentDataPath`。

## 核心类型

| Type | 用途 |
| --- | --- |
| `FileUtility` | 托管文件读写、stream/file hashing、byte-array hashing、文件比较和带比较的复制。 |
| `HashAlgorithmType` | 选择 `MD5`、`SHA256` 或 `XxHash64`。 |
| `FilePathUtility` | 为 `UnityWebRequest` 构建平台正确的 URI string。 |
| `UnityPathSource` | 描述路径来源：StreamingAssets、persistent data 或 absolute/full URI。 |
| `IFileService` / `FileService` | 编码感知的文件 API，支持 DI 与非 DI 组合。 |
| `IFileStorageBackend` / `SystemIOFileStorageBackend` | 可插拔字节存储 seam，用于平台后端和测试替身。 |
| `IStreamingFileStorageBackend` | 可选的流式能力（`OpenRead`/`OpenWrite`），供支持原始流的后端实现。 |
| `FilePathSecurity` | opt-in 的路径标准化与 sandbox 根目录校验。 |
| `FileIORetry` / `FileIORetryPolicy` | opt-in 的瞬时 I/O 失败退避重试（sharing violation、索引/杀软锁文件）。 |

## 组合方式（DI 与非 DI）

`FileUtility` 仍是简单调用点的静态便利入口。需要可测试或平台可插拔时，依赖 `IFileService`，它由 `IFileStorageBackend` 支撑。

```csharp
using CycloneGames.IO.Runtime;

// 非 DI：静态 facade，或默认 service 实例。
string a = FileUtility.ReadAllText(path);
string b = FileService.Default.ReadAllText(path);

// DI：注册接口；在 WebGL/主机平台注入对应平台后端。
// builder.Register<IFileStorageBackend, SystemIOFileStorageBackend>(Lifetime.Singleton);
// builder.Register<IFileService, FileService>(Lifetime.Singleton);

sealed class SaveSystem
{
    private readonly IFileService _files;
    public SaveSystem(IFileService files) => _files = files;
    public Task SaveAsync(string path, string json, CancellationToken ct)
        => _files.WriteAllTextAtomicAsync(path, json, ct);
}
```

存储后端只处理原始字节，因此 `WebGL IndexedDB` 或主机平台 save-data 后端可以直接注入，而不影响编码、哈希或路径策略。当路径来自不可信输入时，应显式调用 `FilePathSecurity.EnsureWithinRoot(root, path)`。

## 流式读写与瞬时重试

大文件优先流式处理，避免一次性载入内存：

```csharp
using (var source = FileUtility.OpenRead(sourcePath))
{
    await FileUtility.WriteFromStreamAsync(destinationPath, source, cancellationToken);
}
```

后端在支持时通过 `IStreamingFileStorageBackend` 暴露流式能力：

```csharp
if (backend is IStreamingFileStorageBackend streaming)
{
    using var read = streaming.OpenRead(path);
}
```

在 Windows 上，杀软和索引器可能短暂锁住文件。可将关键写入包裹在 opt-in 退避重试中：

```csharp
await FileIORetry.ExecuteAsync(
    () => FileUtility.WriteAllTextAtomicAsync(path, json, cancellationToken),
    FileIORetryPolicy.Default,
    cancellationToken);
```

重试只针对瞬时 `IOException`。缺失文件、缺失目录和 path-too-long 被视为永久错误，不会重试。

## 托管文件 API

模块需要普通托管文件访问时，使用这些方法：

```csharp
using CycloneGames.IO.Runtime;

byte[] payload = FileUtility.ReadAllBytes(path);
string json = FileUtility.ReadAllText(path);

await FileUtility.WriteAllBytesAsync(path, payload, cancellationToken);
await FileUtility.WriteAllTextAtomicAsync(path, json, cancellationToken);
```

文本读取会识别 UTF-8、UTF-16 和 UTF-32 BOM。没有 BOM 的文本按 strict UTF-8 without BOM 处理，因此坏字节会显式失败，而不是静默变成替换字符。文本写入默认使用 UTF-8 without BOM。只有读取明确已知的 legacy data 时才传入显式 `Encoding`。

对于编码来源不确定的外部文本，使用 smart decoding API。它会先尊重 BOM，再检测强特征的 UTF-16/UTF-32 no-BOM 字节模式，然后尝试 strict UTF-8 和调用方提供的 fallback encodings。

```csharp
using System.Text;

Encoding[] legacyCandidates = { Encoding.Unicode };
string text = FileUtility.ReadAllTextSmart(path);

if (!FileUtility.TryDecodeTextSmart(bytes, FileUtility.Utf8NoBom, legacyCandidates, out text))
{
    // Ask the caller/importer to specify the source encoding.
}
```

只靠字节本身无法完美识别所有 legacy encoding。游戏内容建议在 import/build 阶段统一转换为 UTF-8。只有来源系统明确、目标平台支持对应 decoder 时，才使用 fallback encodings。

写入 helper 会在需要时创建父目录。Atomic write helper 会先写临时文件，再替换目标文件，更适合 settings、manifest、version file 和其它重要元数据。它们不会写 registry、`EditorPrefs`、`PlayerPrefs` 或隐藏全局状态。

## 哈希与比较

信任边界使用 `SHA256`；本地缓存、变更检测和非安全场景可使用 `XxHash64`。

```csharp
using CycloneGames.IO.Runtime;

string secureHash = await FileUtility.ComputeFileHashToHexStringAsync(
    filePath,
    HashAlgorithmType.SHA256,
    cancellationToken);

string fastHash = FileUtility.ComputeFileHashToHexString(
    filePath,
    HashAlgorithmType.XxHash64);

bool unchanged = await FileUtility.AreFilesEqualAsync(
    cachedPath,
    currentPath,
    HashAlgorithmType.XxHash64,
    cancellationToken);
```

`XxHash64` 不是密码学哈希。不要把它作为防恶意篡改、付费内容保护、反作弊敏感 payload 或不可信下载校验的唯一防线。

## UnityWebRequest 路径

当路径需要通过 `UnityWebRequest` 加载时使用 `FilePathUtility`，尤其是 Android 和 WebGL 上的 StreamingAssets。

```csharp
using CycloneGames.IO.Runtime;

string uri = FilePathUtility.GetUnityWebRequestUri(
    "Config/input_config.yaml",
    UnityPathSource.StreamingAssets);
```

平台说明：

- Windows、macOS、Linux：普通磁盘文件可以使用直接 `System.IO` 路径。
- Android：StreamingAssets 位于 APK/AAB 内部，应通过 `UnityWebRequest` URI 读取。
- WebGL：直接文件 I/O 受限。persistent data 是可靠的可写位置；长异步循环会周期性 yield。
- iOS 和主机平台：需要针对可写位置、存储额度和 sandbox 行为做显式平台验证。

## 持久化

CycloneGames.IO 不拥有存档数据。它只在调用方提供的路径上执行读写。

| 数据 | Owner | 路径 | 版本策略 |
| --- | --- | --- | --- |
| Runtime settings | 调用模块 | 通常位于 `Application.persistentDataPath` | 调用方负责 schema 和 migration。 |
| Editor user settings | 调用方 editor tool | 优先使用 `<repo-root>/UnityStarter/UserSettings/` 或等价显式 user-local 文件 | 调用方负责。 |
| Build/cache artifacts | 调用方 build 或 asset module | 显式 cache/build output path | 调用方负责，且应可重建。 |

普通写入方法会创建父目录，但不会负责 atomic replace、schema migration、加密、压缩或 rollback。重要持久化数据应由上层模块拥有这些策略。

Atomic write 方法只防止正常替换过程中的 partial destination file。它会先写临时文件，在平台支持时 best-effort 落盘（`Flush(true)`），再替换目标文件。在 `File.Replace` 不可用的文件系统或平台上（部分网络共享、FAT、某些移动/WebGL 后端），实现会退回到 delete-then-move，该路径不是 crash-atomic，落盘也可能是 no-op。schema version、校验、损坏恢复、加密和跨设备同步仍由调用方负责。

## 从 Unio 迁移

项目不应再引用 `Unio`、`NativeFile` 或 `SynchronizationStrategy`。典型替换：

```csharp
// Before:
// using Unio;
// NativeFile.WriteAllBytes(path, nativeBytes);

// After:
using CycloneGames.IO.Runtime;

FileUtility.WriteAllBytes(path, bytes);
```

```csharp
string text = await FileUtility.ReadAllTextAsync(path, cancellationToken);
```

## 验证

改动后建议检查：

1. 在 Unity 中打开 `<repo-root>/UnityStarter`。
2. 确认 `CycloneGames.IO.Runtime` 和直接消费者没有 compiler errors。
3. 运行 `Window > CycloneGames > FileUtility Benchmark` 检查 IO 正确性、吞吐和 GC allocation。
4. 搜索项目中的 `Unio`、`NativeFile` 和 `SynchronizationStrategy`；active packages 中不应再有引用。
5. 对使用 StreamingAssets、persistent data 或 WebGL storage 的目标平台执行代表性路径测试。
