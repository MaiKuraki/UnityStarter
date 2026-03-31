# File Utilities

[**English**] | [**简体中文**](README.SCH.md)

**Namespace:** `CycloneGames.Utility.Runtime`  
**Target:** .NET Standard 2.1+ · Unity 2022 LTS / Unity 6000  
**Platforms:** Windows, macOS, Linux, iOS, Android, WebGL

---

## Overview

This module provides two static utility classes:

| Class             | Purpose                                                                                |
| ----------------- | -------------------------------------------------------------------------------------- |
| `FileUtility`     | Hash computation, file/stream comparison, and smart file copy with zero-GC design      |
| `XxHash64`        | Pure C# struct-based xxHash64 implementation — zero heap allocation, non-cryptographic |
| `FilePathUtility` | Platform-correct URI generation for `UnityWebRequest`                                  |

---

## Table of Contents

- [File Utilities](#file-utilities)
  - [Overview](#overview)
  - [Table of Contents](#table-of-contents)
  - [FileUtility](#fileutility)
    - [Hash Computation](#hash-computation)
      - [Async Methods](#async-methods)
      - [Synchronous Methods](#synchronous-methods)
      - [Usage Examples](#usage-examples)
    - [File Comparison](#file-comparison)
    - [Stream Operations](#stream-operations)
    - [Byte Array Comparison](#byte-array-comparison)
    - [File Copy](#file-copy)
    - [Hex Conversion](#hex-conversion)
    - [Memory Strategy](#memory-strategy)
    - [Platform Behavior](#platform-behavior)
    - [Performance Benchmarks](#performance-benchmarks)
  - [XxHash64 Struct](#xxhash64-struct)
    - [Why xxHash64](#why-xxhash64)
    - [API Reference](#api-reference)
    - [Cryptographic vs Non-Cryptographic](#cryptographic-vs-non-cryptographic)
  - [FilePathUtility](#filepathutility)
    - [UnityPathSource Enum](#unitypathsource-enum)
    - [GetUnityWebRequestUri](#getunitywebrequesturi)
      - [Usage Examples](#usage-examples-1)
    - [Platform URI Formats](#platform-uri-formats)
  - [Game Development Use Cases](#game-development-use-cases)
    - [Hot Update / Asset Patching](#hot-update--asset-patching)
    - [AssetBundle Integrity Verification](#assetbundle-integrity-verification)
    - [Save System](#save-system)
    - [Build Pipeline / Editor Tools](#build-pipeline--editor-tools)
    - [Fast Local Change Detection](#fast-local-change-detection)
    - [Direct XxHash64 Usage](#direct-xxhash64-usage)
    - [Cross-Platform Resource Loading](#cross-platform-resource-loading)
  - [Large File Handling \& WebGL](#large-file-handling--webgl)
  - [Dependencies](#dependencies)

---

## FileUtility

### Hash Computation

Supports **MD5** (16 bytes), **SHA256** (32 bytes), and **XxHash64** (8 bytes) via the `HashAlgorithmType` enum.

| Algorithm  | Output Size | Type              | Use Case                                           |
| ---------- | ----------- | ----------------- | -------------------------------------------------- |
| `MD5`      | 128 bit     | Cryptographic     | Legacy compatibility, fast integrity check         |
| `SHA256`   | 256 bit     | Cryptographic     | Tamper-proof verification, CDN manifests           |
| `XxHash64` | 64 bit      | Non-cryptographic | Fast change detection, deduplication, editor tools |

#### Async Methods

```csharp
// Low-level: writes hash bytes into a caller-provided buffer
Task<bool> ComputeFileHashAsync(
    string filePath,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    CancellationToken cancellationToken)

// With progress reporting (0.0 to 1.0)
Task<bool> ComputeFileHashAsync(
    string filePath,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    IProgress<float> progress,
    CancellationToken cancellationToken)

// Convenience: returns lowercase hex string (e.g., "a1b2c3d4...")
Task<string> ComputeFileHashToHexStringAsync(
    string filePath,
    HashAlgorithmType algorithmType = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// Convenience with progress reporting
Task<string> ComputeFileHashToHexStringAsync(
    string filePath,
    HashAlgorithmType algorithmType,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

#### Synchronous Methods

For use in Editor scripts, `OnValidate`, or contexts where async is unavailable. Blocks the calling thread — avoid on the main thread for large files.

```csharp
bool ComputeFileHash(
    string filePath,
    HashAlgorithmType algorithmType,
    Span<byte> hashBuffer)

string ComputeFileHashToHexString(
    string filePath,
    HashAlgorithmType algorithmType = HashAlgorithmType.SHA256)
```

#### Usage Examples

```csharp
// Async — compute SHA256 hex string
string hash = await FileUtility.ComputeFileHashToHexStringAsync(
    "path/to/file.bin", HashAlgorithmType.SHA256, cancellationToken);

// Async — fast xxHash64 for change detection (non-cryptographic)
string hash = await FileUtility.ComputeFileHashToHexStringAsync(
    "path/to/file.bin", HashAlgorithmType.XxHash64, cancellationToken);

// Sync — compute MD5 hex string in Editor
string hash = FileUtility.ComputeFileHashToHexString(
    "path/to/file.bin", HashAlgorithmType.MD5);

// Low-level — write hash into a stack-allocated buffer
Span<byte> buffer = stackalloc byte[32];
bool success = FileUtility.ComputeFileHash("path/to/file.bin", HashAlgorithmType.SHA256, buffer);
```

### File Comparison

```csharp
Task<bool> AreFilesEqualAsync(
    string filePath1,
    string filePath2,
    HashAlgorithmType algorithm = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// With progress reporting (0.0 to 1.0)
Task<bool> AreFilesEqualAsync(
    string filePath1,
    string filePath2,
    HashAlgorithmType algorithm,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

**Comparison Strategy (automatic):**

| Step | Condition                  | Action                                                      |
| ---- | -------------------------- | ----------------------------------------------------------- |
| 1    | Same path string (ordinal) | Return `true` immediately                                   |
| 2    | File does not exist        | Return `false`                                              |
| 3    | Different file sizes       | Return `false`                                              |
| 4    | Both files empty           | Return `true`                                               |
| 5    | File size ≤ 10 MB          | Compare by hash (two full reads, one comparison)            |
| 6    | File size > 10 MB          | Compare by chunks (streaming, early exit on first mismatch) |

The 10 MB threshold balances hash overhead against chunk comparison's early-exit advantage. For small files, hashing is more efficient because the hash comparison is a single fixed-size operation. For large files, chunk comparison can exit early without reading the entire file.

### Stream Operations

Mirror the file-based APIs but operate on arbitrary `Stream` instances:

```csharp
// Stream comparison (same strategy as file comparison)
Task<bool> AreStreamsEqualAsync(
    Stream stream1, Stream stream2,
    long length1, long length2,
    HashAlgorithmType algorithm = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// Stream comparison with progress reporting
Task<bool> AreStreamsEqualAsync(
    Stream stream1, Stream stream2,
    long length1, long length2,
    HashAlgorithmType algorithm,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)

// Stream hash computation
Task<bool> ComputeStreamHashAsync(
    Stream stream,
    HashAlgorithmType algorithmType,
    Memory<byte> hashBuffer,
    CancellationToken cancellationToken)

// Stream hash computation with progress (requires seekable stream)
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

// With progress reporting
Task<string> ComputeStreamHashToHexStringAsync(
    Stream stream,
    HashAlgorithmType algorithmType,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

### Byte Array Comparison

Compare in-memory byte arrays by hash. Useful for verifying data integrity without file I/O.

```csharp
// Synchronous — uses stackalloc for hash buffers
bool AreByteArraysEqualByHash(
    ReadOnlySpan<byte> content1,
    ReadOnlySpan<byte> content2,
    HashAlgorithmType algorithmType)

// Async — offloads to thread pool via Task.Run
Task<bool> AreByteArraysEqualByHashAsync(
    byte[] content1,
    byte[] content2,
    HashAlgorithmType algorithmType,
    CancellationToken cancellationToken = default)
```

### File Copy

```csharp
// Without progress reporting
Task CopyFileWithComparisonAsync(
    string sourceFilePath,
    string destinationFilePath,
    HashAlgorithmType comparisonAlgorithm = HashAlgorithmType.SHA256,
    CancellationToken cancellationToken = default)

// With progress reporting (0.0 to 1.0)
Task CopyFileWithComparisonAsync(
    string sourceFilePath,
    string destinationFilePath,
    HashAlgorithmType comparisonAlgorithm,
    IProgress<float> progress,
    CancellationToken cancellationToken = default)
```

**Behavior:**

1. If the destination file already exists and is identical to the source (verified by hash), the copy is **skipped**.
2. Creates destination directory if it does not exist.
3. Reports progress via `IProgress<float>` (0.0 → 1.0).
4. On cancellation, **deletes the partial destination file** to prevent corrupt data.

### Hex Conversion

```csharp
string ToHexString(ReadOnlySpan<byte> hashBytes)
```

Converts a byte span to a lowercase hexadecimal string. Uses `stackalloc` for the character buffer (up to 64 chars for SHA256) and a pre-computed lookup table. Produces exactly one `string` allocation — no `StringBuilder`, no per-byte `ToString("x2")`.

### Memory Strategy

| Resource                | Allocation Strategy                  | GC Pressure                                         |
| ----------------------- | ------------------------------------ | --------------------------------------------------- |
| Read buffer (64–128 KB) | `ArrayPool<byte>.Shared` rent/return | Zero                                                |
| Hash buffer (async)     | `ArrayPool<byte>.Shared` rent/return | Zero                                                |
| Hash buffer (sync)      | `stackalloc byte[]`                  | Zero                                                |
| Hex char buffer         | `stackalloc char[]` (≤ 64 chars)     | Zero                                                |
| Hex result string       | `new string(Span<char>)`             | 1 allocation (unavoidable)                          |
| `XxHash64` (struct)     | Stack-allocated, inline state        | **Zero**                                            |
| `IncrementalHash`       | `CreateHash()` per call              | ~275 bytes/call (class instance)                    |
| `FileStream`            | `new FileStream(...)` per call       | ~1,800 bytes/call (managed wrapper + kernel handle) |

The `FileStream` internal buffer is set to 4096 bytes (`AsyncFileStreamBufferSize`) to avoid double-buffering with the external `ArrayPool` read buffer, saving ~60 KB per `FileStream` instance.

### Platform Behavior

| Platform                | Read Buffer Size | Notes                                                            |
| ----------------------- | ---------------- | ---------------------------------------------------------------- |
| iOS / Android           | 81,920 bytes     | Larger buffer reduces syscall overhead on flash storage          |
| WebGL                   | 131,072 bytes    | Async runs on main thread; only `persistentDataPath` is reliable |
| Desktop (Win/Mac/Linux) | 65,536 bytes     | Standard buffer size                                             |

**Platform-specific warnings:**

- **WebGL:** Methods log a warning when paths outside `Application.persistentDataPath` are used. Direct file I/O to `StreamingAssets` or arbitrary paths is unreliable on WebGL.
- **Android:** `StreamingAssets` files reside inside the APK and cannot be accessed via `FileStream`. Use `FilePathUtility` with `UnityWebRequest` instead.

**Yield interval (anti-freeze):**

| Platform | `YieldIntervalChunks` | Yield every |
| -------- | --------------------- | ----------- |
| WebGL    | 4                     | ~512 KB     |
| Other    | 32                    | ~2 MB       |

### Performance Benchmarks

Measured in Unity Editor (Windows, single-threaded, files cached in OS page cache):

| Operation                             | Throughput | GC per Call     |
| ------------------------------------- | ---------- | --------------- |
| SHA256 Hash (async, 50 MB)            | ~17 MB/s   | ~8,400 bytes    |
| MD5 Hash (async, 50 MB)               | ~244 MB/s  | ~8,400 bytes    |
| XxHash64 Hash (async, 50 MB)          | ~161 MB/s  | ~8,000 bytes    |
| SHA256 Hash (sync, 1 MB)              | ~17 MB/s   | ~2,000 bytes    |
| XxHash64 Hash (sync, 1 MB)            | ~188 MB/s  | ~1,800 bytes    |
| Chunk Comparison (async, 50 MB)       | ~193 MB/s  | N/A             |
| `ToHexString`                         | N/A        | ~26 bytes       |
| `AreByteArraysEqualByHash` (SHA256)   | N/A        | ~443 bytes      |
| `AreByteArraysEqualByHash` (XxHash64) | N/A        | **~4 bytes** \* |
| `XxHash64.HashToUInt64` (one-shot)    | N/A        | **~4 bytes** \* |

\* The ~4 bytes is profiler measurement noise — XxHash64 in-memory operations are true zero-allocation.

> **Key takeaways:**
>
> - **Async GC** (~8,000–8,400 bytes/call) is dominated by `FileStream` (~1,800 bytes), `Task` state machine, and string allocation. XxHash64 saves ~400 bytes per async call by avoiding `IncrementalHash`.
> - **Sync XxHash64 is ~10× faster than sync SHA256** on cached files because hash computation dominates I/O time.
> - **Sync vs Async for XxHash64**: Sync (5.3 ms / 1 MB) is ~10× faster than async (51.6 ms) because `Task` overhead dominates the near-instant hash computation. Prefer the sync API in Editor tools and build scripts.
> - **In-memory XxHash64** (`AreByteArraysEqualByHash`, `HashToUInt64`) achieves true zero GC.
> - This is a **pure C# implementation without SIMD**. Native xxHash64 reaches ~19 GB/s; this implementation delivers ~160–190 MB/s — competitive with native MD5 (~244 MB/s via Windows CNG) and ~10× faster than SHA256.

---

## XxHash64 Struct

A pure C# implementation of the [xxHash64](https://github.com/Cyan4973/xxHash) non-cryptographic hash algorithm, designed as a value-type `struct` for zero heap allocation.

### Why xxHash64

| Property                 | xxHash64             | MD5                | SHA256             |
| ------------------------ | -------------------- | ------------------ | ------------------ |
| Speed (native)           | ~19 GB/s             | ~0.6 GB/s          | ~0.3 GB/s          |
| Output size              | 64 bit               | 128 bit            | 256 bit            |
| GC per call (this impl.) | **0 bytes** (struct) | ~275 bytes (class) | ~275 bytes (class) |
| Cryptographic            | No                   | Broken             | Yes                |
| Use case                 | Change detection     | Legacy compat      | Security           |

xxHash64 is ~30× faster than MD5 in native code. In this pure C# implementation (no SIMD), XxHash64 delivers ~160–190 MB/s — roughly on par with native MD5 (~244 MB/s via Windows CNG) and ~10× faster than SHA256 (~17 MB/s) for sync workloads. More importantly, the `struct` design eliminates all managed allocation overhead, making it the clear choice for in-memory and sync operations.

### API Reference

```csharp
// One-shot (most common usage)
ulong hash = XxHash64.HashToUInt64(dataSpan);                   // returns u64
ulong hash = XxHash64.HashToUInt64(dataSpan, seed: 42);         // with custom seed

// Streaming / incremental (for large data in chunks)
var hasher = XxHash64.Create(seed: 0);
hasher.Append(chunk1);
hasher.Append(chunk2);                                          // can be called N times
ulong result = hasher.GetDigest();                              // final hash

// Write hash to byte buffer (big-endian, matches xxhsum display order)
bool ok = hasher.TryWriteHash(destinationSpan);                 // writes 8 bytes

// Byte array overload (avoids Span in async callers)
hasher.Append(byteArray, offset, count);
```

### Cryptographic vs Non-Cryptographic

| Scenario                          | Recommended Algorithm                                      |
| --------------------------------- | ---------------------------------------------------------- |
| Hot update manifests (CDN)        | `SHA256` — tamper-proof                                    |
| AssetBundle download verification | `SHA256` or `MD5` — integrity + interop                    |
| Local file change detection       | `XxHash64` — fast, zero GC                                 |
| Editor incremental build          | `XxHash64` — fast, zero GC                                 |
| Save file corruption check        | `XxHash64` — sufficient for bit-flip detection             |
| Content deduplication             | `XxHash64` — 64-bit collision space adequate for local use |
| Mod file authentication           | `SHA256` — must resist intentional collision               |

> **Rule of thumb:** Use `XxHash64` when you're asking "did this file change?" Use `SHA256` when you're asking "was this file tampered with?"

---

## FilePathUtility

Generates platform-correct URIs for use with `UnityWebRequest`. Handles the cross-platform differences in how Unity exposes `StreamingAssets`, `persistentDataPath`, and absolute file paths.

### UnityPathSource Enum

```csharp
public enum UnityPathSource
{
    StreamingAssets,    // Relative to Assets/StreamingAssets
    PersistentData,     // Relative to Application.persistentDataPath
    AbsoluteOrFullUri   // Absolute file path or pre-formatted URI (http/https/file/jar)
}
```

### GetUnityWebRequestUri

```csharp
string FilePathUtility.GetUnityWebRequestUri(string path, UnityPathSource pathSource)
```

Returns a URI string suitable for `UnityWebRequest`, or `null` if inputs are invalid.

#### Usage Examples

```csharp
// Load from StreamingAssets
string uri = FilePathUtility.GetUnityWebRequestUri(
    "Config/settings.json", UnityPathSource.StreamingAssets);
UnityWebRequest request = UnityWebRequest.Get(uri);

// Load from persistent data
string uri = FilePathUtility.GetUnityWebRequestUri(
    "Saves/save01.dat", UnityPathSource.PersistentData);

// Pass through an existing URL
string uri = FilePathUtility.GetUnityWebRequestUri(
    "https://cdn.example.com/bundle.ab", UnityPathSource.AbsoluteOrFullUri);

// Convert an absolute path to a file URI
string uri = FilePathUtility.GetUnityWebRequestUri(
    @"C:\Game\Data\level.bin", UnityPathSource.AbsoluteOrFullUri);
// Result: "file:///C:/Game/Data/level.bin"
```

### Platform URI Formats

| Platform       | StreamingAssets Format                        | Conversion Method    |
| -------------- | --------------------------------------------- | -------------------- |
| **Editor**     | `file:///` + absolute path                    | `System.Uri`         |
| **Android**    | `jar:file:///...apk!/assets/` + relative path | Direct concatenation |
| **iOS**        | `file:///` + app bundle path                  | `System.Uri`         |
| **WebGL**      | Relative or absolute URL                      | `Path.Combine`       |
| **Standalone** | `file:///` + `_Data/StreamingAssets/` path    | `System.Uri`         |

**Key behaviors:**

- Leading slashes in relative paths are automatically trimmed.
- Spaces and special characters in file paths are percent-encoded via `System.Uri`.
- Pre-formatted URIs (`http://`, `https://`, `jar:file://`) are passed through unchanged.
- Invalid paths return `null` with a `Debug.LogError` message.

---

## Game Development Use Cases

### Hot Update / Asset Patching

```csharp
// Compare local asset hash against server manifest (with progress)
var progress = new Progress<float>(p => loadingBar.value = p);
string localHash = await FileUtility.ComputeFileHashToHexStringAsync(
    localPath, HashAlgorithmType.SHA256, progress, cts.Token);
if (localHash != serverManifestHash)
{
    // Download and copy with progress
    await FileUtility.CopyFileWithComparisonAsync(
        downloadedTempPath, localPath,
        HashAlgorithmType.SHA256, progress, cts.Token);
}
```

### AssetBundle Integrity Verification

```csharp
// Verify downloaded bundle is not corrupt
string hash = await FileUtility.ComputeFileHashToHexStringAsync(bundlePath, HashAlgorithmType.MD5);
if (hash != expectedHash)
    Debug.LogError("Bundle corrupted, re-downloading...");
```

### Save System

```csharp
// Check if local save matches cloud save before uploading
bool identical = await FileUtility.AreFilesEqualAsync(localSavePath, cloudSavePath);
if (!identical)
    await UploadSaveToCloud(localSavePath);
```

### Build Pipeline / Editor Tools

```csharp
// Detect changed assets with xxHash64 (fast, zero GC, sync API safe for Editor)
string hash = FileUtility.ComputeFileHashToHexString(assetPath, HashAlgorithmType.XxHash64);
if (hash != cachedHash)
    ReprocessAsset(assetPath);
```

### Fast Local Change Detection

```csharp
// Use xxHash64 for maximum speed when cryptographic strength is unnecessary
bool changed = !await FileUtility.AreFilesEqualAsync(
    cachedPath, currentPath, HashAlgorithmType.XxHash64, cts.Token);
```

### Direct XxHash64 Usage

```csharp
// One-shot hash of in-memory data (zero allocation)
ulong hash = XxHash64.HashToUInt64(dataSpan);

// Streaming hash of large data
var hasher = XxHash64.Create();
foreach (var chunk in dataChunks)
    hasher.Append(chunk);
ulong result = hasher.GetDigest();
```

### Cross-Platform Resource Loading

```csharp
// Generate correct URI for any platform
string uri = FilePathUtility.GetUnityWebRequestUri("Audio/bgm.ogg", UnityPathSource.StreamingAssets);
using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
{
    await request.SendWebRequest();
    // ...
}
```

---

## Large File Handling & WebGL

WebGL has **no thread pool** — all `async/await` runs synchronously on the main thread. Without intervention, hashing a 1 GB file would freeze the browser for the entire duration (7,800+ loop iterations without yielding to the event loop).

The solution: **every async loop in FileUtility performs a `Task.Yield()` every N chunks**, returning control to the caller/event loop. On threaded platforms this simply re-queues the continuation on the thread pool (negligible cost); on WebGL it allows the browser to render frames and process input.

```
1 GB file, XxHash64 async, WebGL (128 KB buffer, yield every 4 chunks = 512 KB):
  → ~1,953 yields → browser stays responsive
  → Total: I/O-bound, same throughput as without yields

1 GB file, SHA256 async, Desktop (64 KB buffer, yield every 32 chunks = 2 MB):
  → ~500 yields → negligible overhead on thread pool
```

**Recommendations for 500 MB+ files:**

| Platform | Sync API                      | Async API                   |
| -------- | ----------------------------- | --------------------------- |
| Desktop  | Safe (blocks calling thread)  | Safe (runs off main thread) |
| Android  | Avoid on UI thread (ANR risk) | Safe (thread pool)          |
| WebGL    | **Will freeze** — do not use  | Safe with periodic yield    |

> On WebGL, always use the async API. The sync methods (`ComputeFileHash`, `ComputeFileHashToHexString`) have no yield mechanism and will freeze the browser.

---

## Dependencies

| Dependency                       | Usage                                                                                       |
| -------------------------------- | ------------------------------------------------------------------------------------------- |
| `CycloneGames.Logger`            | `CLogger.LogDebug/LogInfo/LogWarning/LogError` for all diagnostic output                    |
| `UnityEngine`                    | `Application.platform`, `Application.persistentDataPath`, `Application.streamingAssetsPath` |
| `System.Buffers`                 | `ArrayPool<byte>.Shared`                                                                    |
| `System.Buffers.Binary`          | `BinaryPrimitives` (used by `XxHash64` for endian-safe reads/writes)                        |
| `System.Runtime.InteropServices` | `MemoryMarshal` (used by `XxHash64` for inline buffer access)                               |
| `System.Security.Cryptography`   | `IncrementalHash`, `HashAlgorithmName` (MD5/SHA256 only)                                    |

No third-party packages required. Pure C# implementation, fully cross-platform including WebGL.
