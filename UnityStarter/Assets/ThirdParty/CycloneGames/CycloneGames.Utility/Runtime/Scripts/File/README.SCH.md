# 文件工具库

线程安全、分配优化的 Unity 文件工具集。  
隶属于 **CycloneGames.Utility.Runtime** 包。

**命名空间：** `CycloneGames.Utility.Runtime`  
**目标：** .NET Standard 2.1+ · Unity 2022 LTS / Unity 6000  
**平台：** Windows、macOS、Linux、iOS、Android、WebGL

---

## 目录

- [概述](#概述)
- [FileUtility](#fileutility)
  - [设计原则](#设计原则)
  - [哈希计算](#哈希计算)
  - [文件比较](#文件比较)
  - [流操作](#流操作)
  - [字节数组比较](#字节数组比较)
  - [文件复制](#文件复制)
  - [十六进制转换](#十六进制转换)
  - [内存策略](#内存策略)
  - [平台行为](#平台行为)
  - [性能基准](#性能基准)
- [XxHash64 结构体](#xxhash64-结构体)
  - [为什么选择 xxHash64](#为什么选择-xxhash64)
  - [API 参考](#api-参考)
  - [密码学 vs 非密码学](#密码学-vs-非密码学)
- [FilePathUtility](#filepathutility)
  - [UnityPathSource 枚举](#unitypathsource-枚举)
  - [GetUnityWebRequestUri](#getunitywebrequesturi)
  - [平台 URI 格式](#平台-uri-格式)
- [游戏开发应用场景](#游戏开发应用场景)
- [大文件处理与 WebGL](#大文件处理与-webgl)
- [依赖项](#依赖项)

---

## 概述

本模块提供两个静态工具类：

| 类 | 用途 |
|---|---|
| `FileUtility` | 哈希计算、文件/流比较、带校验的智能文件复制，零 GC 设计 |
| `XxHash64` | 纯 C# struct 实现的 xxHash64 —— 零堆分配、非密码学哈希 |
| `FilePathUtility` | 为 `UnityWebRequest` 生成跨平台正确的 URI |

---

## FileUtility

### 设计原则

- **线程安全** — 所有公开方法均可安全地从多线程并发调用。`IncrementalHash` 采用按调用创建（非 `ThreadLocal` 缓存），以避免异步续延（continuation）跨线程时的状态污染。`XxHash64` 是值类型 struct —— 每个副本天然独立。
- **最小化 GC** — 读缓冲区使用 `ArrayPool<byte>.Shared` 租借/归还；同步方法的哈希缓冲区使用 `stackalloc`，异步方法使用 `ArrayPool`；十六进制转换使用 `stackalloc char[]` + 查找表。`XxHash64` 基于 struct，零堆分配。
- **取消支持** — 所有异步方法接受 `CancellationToken`，正确传播 `OperationCanceledException`。
- **ConfigureAwait(false)** — 全链路使用，避免不必要的同步上下文捕获。
- **分帧让出** — 所有异步循环每 N 个 chunk 通过 `Task.Yield()` 让出控制权，防止 WebGL 和单线程运行时在处理大文件（如 1 GB）时冻结浏览器/UI。

### 哈希计算

支持 **MD5**（16 字节）、**SHA256**（32 字节）和 **XxHash64**（8 字节），通过 `HashAlgorithmType` 枚举选择。

| 算法 | 输出大小 | 类型 | 适用场景 |
|------|---------|------|----------|
| `MD5` | 128 bit | 密码学 | 向后兼容、快速完整性检查 |
| `SHA256` | 256 bit | 密码学 | 防篡改验证、CDN 清单 |
| `XxHash64` | 64 bit | 非密码学 | 快速变更检测、去重、编辑器工具 |

#### 异步方法

```csharp
// 底层接口：将哈希字节写入调用者提供的缓冲区
Task<bool> ComputeFileHashAsync(
    string filePath,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    CancellationToken cancellationToken)

// 带进度报告（0.0 到 1.0）
Task<bool> ComputeFileHashAsync(
    string filePath,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    IProgress<float> progress,
    CancellationToken cancellationToken)

// 便捷接口：返回小写十六进制字符串（如 "a1b2c3d4..."）
Task<string> ComputeFileHashToHexStringAsync(
    string filePath,
    HashAlgorithmType algorithmType = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// 便捷接口带进度报告
Task<string> ComputeFileHashToHexStringAsync(
    string filePath,
    HashAlgorithmType algorithmType,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

#### 同步方法

用于 Editor 脚本、`OnValidate` 或无法使用异步的场景。会阻塞调用线程——大文件应避免在主线程使用。

```csharp
bool ComputeFileHash(
    string filePath,
    HashAlgorithmType algorithmType,
    Span<byte> hashBuffer)

string ComputeFileHashToHexString(
    string filePath,
    HashAlgorithmType algorithmType = HashAlgorithmType.SHA256)
```

#### 使用示例

```csharp
// 异步 — 计算 SHA256 十六进制字符串
string hash = await FileUtility.ComputeFileHashToHexStringAsync(
    "path/to/file.bin", HashAlgorithmType.SHA256, cancellationToken);

// 异步 — 使用 xxHash64 快速变更检测（非密码学）
string hash = await FileUtility.ComputeFileHashToHexStringAsync(
    "path/to/file.bin", HashAlgorithmType.XxHash64, cancellationToken);

// 同步 — 在 Editor 中计算 MD5 十六进制字符串
string hash = FileUtility.ComputeFileHashToHexString(
    "path/to/file.bin", HashAlgorithmType.MD5);

// 底层 — 将哈希写入栈分配的缓冲区
Span<byte> buffer = stackalloc byte[32];
bool success = FileUtility.ComputeFileHash("path/to/file.bin", HashAlgorithmType.SHA256, buffer);
```

### 文件比较

```csharp
Task<bool> AreFilesEqualAsync(
    string filePath1,
    string filePath2,
    HashAlgorithmType algorithm = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// 带进度报告（0.0 到 1.0）
Task<bool> AreFilesEqualAsync(
    string filePath1,
    string filePath2,
    HashAlgorithmType algorithm,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

**比较策略（自动选择）：**

| 步骤 | 条件 | 操作 |
|------|------|------|
| 1 | 路径字符串相同（Ordinal 比较） | 立即返回 `true` |
| 2 | 文件不存在 | 返回 `false` |
| 3 | 文件大小不同 | 返回 `false` |
| 4 | 两个文件均为空文件 | 返回 `true` |
| 5 | 文件大小 ≤ 10 MB | 哈希比较（两次完整读取，一次固定长度比较） |
| 6 | 文件大小 > 10 MB | 逐块比较（流式读取，首次不匹配即提前退出） |

10 MB 阈值在哈希开销与逐块比较的提前退出优势之间取得平衡。小文件使用哈希更高效，因为最终只需一次固定大小的比较；大文件使用逐块比较可以在不读取整个文件的情况下提前退出。

### 流操作

与文件 API 对称，但操作任意 `Stream` 实例：

```csharp
// 流比较（与文件比较相同的策略）
Task<bool> AreStreamsEqualAsync(
    Stream stream1, Stream stream2,
    long length1, long length2,
    HashAlgorithmType algorithm = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// 流比较带进度报告
Task<bool> AreStreamsEqualAsync(
    Stream stream1, Stream stream2,
    long length1, long length2,
    HashAlgorithmType algorithm,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)

// 流哈希计算
Task<bool> ComputeStreamHashAsync(
    Stream stream,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    CancellationToken cancellationToken)

// 流哈希计算带进度（需要可寻址的流）
Task<bool> ComputeStreamHashAsync(
    Stream stream,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    IProgress<float> progress,
    CancellationToken cancellationToken)

Task<string> ComputeStreamHashToHexStringAsync(
    Stream stream,
    HashAlgorithmType algorithmType = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// 带进度报告
Task<string> ComputeStreamHashToHexStringAsync(
    Stream stream,
    HashAlgorithmType algorithmType,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

### 字节数组比较

通过哈希比较内存中的字节数组。适用于无需文件 I/O 的数据完整性验证场景。

```csharp
// 同步 — 哈希缓冲区使用 stackalloc
bool AreByteArraysEqualByHash(
    ReadOnlySpan<byte> content1,
    ReadOnlySpan<byte> content2,
    HashAlgorithmType algorithmType)

// 异步 — 通过 Task.Run 卸载到线程池
Task<bool> AreByteArraysEqualByHashAsync(
    byte[] content1,
    byte[] content2,
    HashAlgorithmType algorithmType,
    CancellationToken cancellationToken = default)
```

### 文件复制

```csharp
// 不带进度报告
Task CopyFileWithComparisonAsync(
    string sourceFilePath,
    string destinationFilePath,
    HashAlgorithmType comparisonAlgorithm = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// 带进度报告（0.0 到 1.0）
Task CopyFileWithComparisonAsync(
    string sourceFilePath,
    string destinationFilePath,
    HashAlgorithmType comparisonAlgorithm,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

**行为特点：**

1. 若目标文件已存在且与源文件相同（通过哈希验证），则**跳过复制**。
2. 自动创建目标目录（若不存在）。
3. 通过 `IProgress<float>` 报告进度（0.0 → 1.0）。
4. 取消时**自动删除不完整的目标文件**，防止残留损坏数据。

### 十六进制转换

```csharp
string ToHexString(ReadOnlySpan<byte> hashBytes)
```

将字节跨度转换为小写十六进制字符串。使用 `stackalloc` 分配字符缓冲区（SHA256 最多 64 个字符）和预计算的查找表。仅产生一次 `string` 分配——不使用 `StringBuilder`，不使用逐字节 `ToString("x2")`。

### 内存策略

| 资源 | 分配策略 | GC 压力 |
|------|---------|---------|
| 读缓冲区（64–128 KB） | `ArrayPool<byte>.Shared` 租借/归还 | 零 |
| 哈希缓冲区（异步） | `ArrayPool<byte>.Shared` 租借/归还 | 零 |
| 哈希缓冲区（同步） | `stackalloc byte[]` | 零 |
| 十六进制字符缓冲区 | `stackalloc char[]`（≤ 64 字符） | 零 |
| 十六进制结果字符串 | `new string(Span<char>)` | 1 次分配（不可避免） |
| `XxHash64`（struct） | 栈分配，内联状态 | **零** |
| `IncrementalHash` | 每次调用 `CreateHash()` | ~275 字节/次（类实例） |
| `FileStream` | 每次调用 `new FileStream(...)` | ~1,800 字节/次（托管包装器 + 内核句柄） |

`FileStream` 内部缓冲区设为 4096 字节（`AsyncFileStreamBufferSize`），避免与外部 `ArrayPool` 读缓冲区产生双重缓冲，每个 `FileStream` 实例节省约 60 KB。

### 平台行为

| 平台 | 读缓冲区大小 | 备注 |
|------|-------------|------|
| iOS / Android | 81,920 字节 | 较大的缓冲区减少闪存介质上的系统调用开销 |
| WebGL | 131,072 字节 | 异步操作在主线程运行；仅 `persistentDataPath` 可靠 |
| 桌面端（Win/Mac/Linux） | 65,536 字节 | 标准缓冲区大小 |

**平台特定警告：**

- **WebGL：** 使用 `persistentDataPath` 以外的路径时，方法会输出警告日志。WebGL 上对 `StreamingAssets` 或任意路径的直接文件 I/O 不可靠。
- **Android：** `StreamingAssets` 文件位于 APK 内部，无法通过 `FileStream` 访问。请改用 `FilePathUtility` 配合 `UnityWebRequest`。

**让出间隔（防冻结）：**

| 平台 | `YieldIntervalChunks` | 每次让出间隔 |
|------|----------------------|-------------|
| WebGL | 4 | ~512 KB |
| 其他 | 32 | ~2 MB |

### 性能基准

在 Unity Editor 中测量（Windows，单线程，文件已缓存到 OS 页面缓存）：

| 操作 | 吞吐量 | 每次调用 GC 分配 |
|------|--------|------------------|
| SHA256 哈希（异步，50 MB） | ~17 MB/s | ~8,400 字节 |
| MD5 哈希（异步，50 MB） | ~244 MB/s | ~8,400 字节 |
| XxHash64 哈希（异步，50 MB） | ~161 MB/s | ~8,000 字节 |
| SHA256 哈希（同步，1 MB） | ~17 MB/s | ~2,000 字节 |
| XxHash64 哈希（同步，1 MB） | ~188 MB/s | ~1,800 字节 |
| 逐块比较（异步，50 MB） | ~193 MB/s | N/A |
| `ToHexString` | N/A | ~26 字节 |
| `AreByteArraysEqualByHash`（SHA256） | N/A | ~443 字节 |
| `AreByteArraysEqualByHash`（XxHash64） | N/A | **~4 字节** \* |
| `XxHash64.HashToUInt64`（一次性） | N/A | **~4 字节** \* |

\* ~4 字节是 Profiler 测量噪声 —— XxHash64 的纯内存操作是真正的零分配。

> **关键发现：**
> - **异步 GC**（~8,000–8,400 字节/次）主要来自 `FileStream`（~1,800 字节）、`Task` 状态机和字符串分配。XxHash64 通过避免 `IncrementalHash`，每次异步调用节省约 400 字节。
> - **同步 XxHash64 比同步 SHA256 快约 10 倍**（在已缓存文件上，哈希计算时间主导 I/O 时间）。
> - **同步 vs 异步 XxHash64**：同步（1 MB 仅 5.3 ms）比异步（51.6 ms）快约 10 倍，因为 `Task` 开销远超近乎瞬时的哈希计算。在 Editor 工具和构建脚本中，优先使用同步 API。
> - **纯内存 XxHash64**（`AreByteArraysEqualByHash`、`HashToUInt64`）实现真正的零 GC。
> - 本实现为**纯 C# 实现，无 SIMD**。原生 xxHash64 可达 ~19 GB/s；此实现约 ~160–190 MB/s —— 与原生 MD5（~244 MB/s，经由 Windows CNG）相当，比 SHA256 快约 10 倍。

---

## XxHash64 结构体

[xxHash64](https://github.com/Cyan4973/xxHash) 非密码学哈希算法的纯 C# 实现，设计为值类型 `struct`，零堆分配。

### 为什么选择 xxHash64

| 特性 | xxHash64 | MD5 | SHA256 |
|------|----------|-----|--------|
| 速度（原生） | ~19 GB/s | ~0.6 GB/s | ~0.3 GB/s |
| 输出大小 | 64 bit | 128 bit | 256 bit |
| 每次调用 GC（本实现） | **0 字节**（struct） | ~275 字节（class） | ~275 字节（class） |
| 密码学安全 | 否 | 已破解 | 是 |
| 适用场景 | 变更检测 | 向后兼容 | 安全校验 |

xxHash64 在原生代码中比 MD5 快约 30 倍。在此纯 C# 实现（无 SIMD）中，XxHash64 实测吞吐约 ~160–190 MB/s —— 与原生 MD5（~244 MB/s，经由 Windows CNG）相当，比 SHA256（~17 MB/s）快约 10 倍。更重要的是，`struct` 设计彻底消除了所有托管分配开销，使其成为纯内存和同步操作的最优选择。

### API 参考

```csharp
// 一次性计算（最常用）
ulong hash = XxHash64.HashToUInt64(dataSpan);                   // 返回 u64
ulong hash = XxHash64.HashToUInt64(dataSpan, seed: 42);         // 自定义 seed

// 流式 / 增量计算（处理分块大数据）
var hasher = XxHash64.Create(seed: 0);
hasher.Append(chunk1);
hasher.Append(chunk2);                                          // 可调用 N 次
ulong result = hasher.GetDigest();                              // 获取最终哈希

// 将哈希写入字节缓冲区（大端序，匹配 xxhsum 显示顺序）
bool ok = hasher.TryWriteHash(destinationSpan);                 // 写入 8 字节

// byte[] 重载（避免异步调用者中出现 Span）
hasher.Append(byteArray, offset, count);
```

### 密码学 vs 非密码学

| 场景 | 推荐算法 |
|------|---------|
| 热更新清单（CDN） | `SHA256` —— 防篡改 |
| AssetBundle 下载校验 | `SHA256` 或 `MD5` —— 完整性 + 互操作 |
| 本地文件变更检测 | `XxHash64` —— 快速、零 GC |
| 编辑器增量构建 | `XxHash64` —— 快速、零 GC |
| 存档损坏检测 | `XxHash64` —— 足以检测位翻转 |
| 内容去重 | `XxHash64` —— 64-bit 碰撞空间对本地使用足够 |
| Mod 文件认证 | `SHA256` —— 必须抵抗刻意碰撞 |

> **经验法则：** 问「文件是否变了」→ 用 `XxHash64`；问「文件是否被篡改了」→ 用 `SHA256`。

---

## FilePathUtility

为 `UnityWebRequest` 生成跨平台正确的 URI。封装了 Unity 在不同平台上暴露 `StreamingAssets`、`persistentDataPath` 和绝对路径的差异。

### UnityPathSource 枚举

```csharp
public enum UnityPathSource
{
    StreamingAssets,    // 相对于 Assets/StreamingAssets 的路径
    PersistentData,     // 相对于 Application.persistentDataPath 的路径
    AbsoluteOrFullUri   // 绝对文件路径或预格式化的 URI（http/https/file/jar）
}
```

### GetUnityWebRequestUri

```csharp
string FilePathUtility.GetUnityWebRequestUri(string path, UnityPathSource pathSource)
```

返回适用于 `UnityWebRequest` 的 URI 字符串，输入无效时返回 `null`。

#### 使用示例

```csharp
// 从 StreamingAssets 加载
string uri = FilePathUtility.GetUnityWebRequestUri(
    "Config/settings.json", UnityPathSource.StreamingAssets);
UnityWebRequest request = UnityWebRequest.Get(uri);

// 从持久化数据目录加载
string uri = FilePathUtility.GetUnityWebRequestUri(
    "Saves/save01.dat", UnityPathSource.PersistentData);

// 透传已有的 URL
string uri = FilePathUtility.GetUnityWebRequestUri(
    "https://cdn.example.com/bundle.ab", UnityPathSource.AbsoluteOrFullUri);

// 将绝对路径转换为 file URI
string uri = FilePathUtility.GetUnityWebRequestUri(
    @"C:\Game\Data\level.bin", UnityPathSource.AbsoluteOrFullUri);
// 结果: "file:///C:/Game/Data/level.bin"
```

### 平台 URI 格式

| 平台 | StreamingAssets 格式 | 转换方式 |
|------|---------------------|---------|
| **Editor** | `file:///` + 绝对路径 | `System.Uri` |
| **Android** | `jar:file:///...apk!/assets/` + 相对路径 | 直接拼接 |
| **iOS** | `file:///` + 应用包路径 | `System.Uri` |
| **WebGL** | 相对或绝对 URL | `Path.Combine` |
| **Standalone** | `file:///` + `_Data/StreamingAssets/` 路径 | `System.Uri` |

**关键行为：**

- 相对路径中的前导斜杠会被自动去除。
- 文件路径中的空格和特殊字符通过 `System.Uri` 进行百分号编码。
- 预格式化的 URI（`http://`、`https://`、`jar:file://`）原样透传。
- 无效路径返回 `null`，并通过 `Debug.LogError` 输出错误消息。

---

## 游戏开发应用场景

### 热更新 / 资源补丁

```csharp
// 将本地资源哈希与服务器清单比对（带进度报告）
var progress = new Progress<float>(p => loadingBar.value = p);
string localHash = await FileUtility.ComputeFileHashToHexStringAsync(
    localPath, HashAlgorithmType.SHA256, progress, cts.Token);
if (localHash != serverManifestHash)
{
    // 下载并复制，带进度显示
    await FileUtility.CopyFileWithComparisonAsync(
        downloadedTempPath, localPath,
        HashAlgorithmType.SHA256, progress, cts.Token);
}
```

### AssetBundle 完整性校验

```csharp
// 验证下载的 Bundle 文件是否损坏
string hash = await FileUtility.ComputeFileHashToHexStringAsync(bundlePath, HashAlgorithmType.MD5);
if (hash != expectedHash)
    Debug.LogError("Bundle 已损坏，正在重新下载...");
```

### 存档系统

```csharp
// 上传前检查本地存档与云端存档是否一致
bool identical = await FileUtility.AreFilesEqualAsync(localSavePath, cloudSavePath);
if (!identical)
    await UploadSaveToCloud(localSavePath);
```

### 构建管线 / 编辑器工具

```csharp
// 使用 xxHash64 检测资源变更（快速、零 GC、同步 API 在 Editor 中安全使用）
string hash = FileUtility.ComputeFileHashToHexString(assetPath, HashAlgorithmType.XxHash64);
if (hash != cachedHash)
    ReprocessAsset(assetPath);
```

### 快速本地变更检测

```csharp
// 不需要密码学强度时使用 xxHash64 获取最大速度
bool changed = !await FileUtility.AreFilesEqualAsync(
    cachedPath, currentPath, HashAlgorithmType.XxHash64, cts.Token);
```

### 直接使用 XxHash64

```csharp
// 一次性计算内存数据的哈希（零分配）
ulong hash = XxHash64.HashToUInt64(dataSpan);

// 流式哈希大块数据
var hasher = XxHash64.Create();
foreach (var chunk in dataChunks)
    hasher.Append(chunk);
ulong result = hasher.GetDigest();
```

### 跨平台资源加载

```csharp
// 为任意平台生成正确的 URI
string uri = FilePathUtility.GetUnityWebRequestUri("Audio/bgm.ogg", UnityPathSource.StreamingAssets);
using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
{
    await request.SendWebRequest();
    // ...
}
```

---

## 大文件处理与 WebGL

WebGL **没有线程池** —— 所有 `async/await` 在主线程同步执行。如不干预，对 1 GB 文件做哈希会冻结浏览器整个过程（7,800+ 次循环迭代不让出事件循环）。

解决方案：**FileUtility 的所有异步循环每 N 个 chunk 执行一次 `Task.Yield()`**，将控制权返还调用者/事件循环。在多线程平台上仅是将续延重新入队到线程池（开销可忽略）；在 WebGL 上则允许浏览器渲染帧和处理输入。

```
1 GB 文件, XxHash64 异步, WebGL (128 KB 缓冲, 每 4 chunk 让出 = 512 KB):
  → ~1,953 次让出 → 浏览器保持响应
  → 总耗时：I/O 瓶颈，与无让出时吞吐量相同

1 GB 文件, SHA256 异步, 桌面端 (64 KB 缓冲, 每 32 chunk 让出 = 2 MB):
  → ~500 次让出 → 线程池上开销可忽略
```

**500 MB+ 文件建议：**

| 平台 | 同步 API | 异步 API |
|------|---------|----------|
| 桌面端 | 安全（阻塞调用线程） | 安全（线程池执行） |
| Android | 避免在 UI 线程使用（ANR 风险） | 安全（线程池） |
| WebGL | **会冻结** —— 不要使用 | 安全（有分帧让出） |

> 在 WebGL 上，始终使用异步 API。同步方法（`ComputeFileHash`、`ComputeFileHashToHexString`）没有让出机制，会冻结浏览器。

---

## 依赖项

| 依赖 | 用途 |
|------|------|
| `CycloneGames.Logger` | 通过 `CLogger.LogDebug/LogInfo/LogWarning/LogError` 输出所有诊断信息 |
| `UnityEngine` | `Application.platform`、`Application.persistentDataPath`、`Application.streamingAssetsPath` |
| `System.Buffers` | `ArrayPool<byte>.Shared` |
| `System.Buffers.Binary` | `BinaryPrimitives`（`XxHash64` 用于端序安全的读写） |
| `System.Runtime.InteropServices` | `MemoryMarshal`（`XxHash64` 用于内联缓冲区访问） |
| `System.Security.Cryptography` | `IncrementalHash`、`HashAlgorithmName`（仅 MD5/SHA256） |

无需任何第三方包。纯 C# 实现，全平台可用（包括 WebGL）。
