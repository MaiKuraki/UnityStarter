# CycloneGames.IO

CycloneGames.IO provides managed, Unity-aware file and path utilities for CycloneGames foundation modules. It replaces previous direct usage of the embedded `jp.hadashikick.unio` package with explicit `System.IO` and `FileStream` based APIs.

Namespace: `CycloneGames.IO.Runtime`

## Design Goals

- Keep file access managed and easy to audit.
- Avoid unsafe/native-memory file helpers as a default foundation dependency.
- Provide low-GC hash, comparison, and copy APIs for build tools, caches, settings, and asset metadata.
- Keep Unity platform path handling in one package via `FilePathUtility`.
- Make dependencies explicit: `CycloneGames.Hash.Core` for `XxHash64` and `CycloneGames.Logger` for diagnostics.

## Assemblies

| Assembly | Path | Purpose |
| --- | --- | --- |
| `CycloneGames.IO.Runtime` | `Runtime/` | File read/write, hash, comparison, copy, and Unity path URI helpers. |
| `CycloneGames.IO.Editor` | `Editor/` | Editor benchmark window for correctness, throughput, and GC checks. |

`CycloneGames.IO.Runtime` references UnityEngine because `FilePathUtility` uses `Application.streamingAssetsPath` and `Application.persistentDataPath`.

## Core Types

| Type | Purpose |
| --- | --- |
| `FileUtility` | Managed file read/write helpers, stream/file hashing, byte-array hashing, file comparison, and copy-with-comparison. |
| `HashAlgorithmType` | Selects `MD5`, `SHA256`, or `XxHash64`. |
| `FilePathUtility` | Builds platform-correct URI strings for `UnityWebRequest`. |
| `UnityPathSource` | Describes whether a path comes from StreamingAssets, persistent data, or an absolute/full URI. |
| `IFileService` / `FileService` | Encoding-aware file API for DI and non-DI composition. |
| `IFileStorageBackend` / `SystemIOFileStorageBackend` | Pluggable byte storage seam for platform backends and test doubles. |
| `IStreamingFileStorageBackend` | Optional streaming capability (`OpenRead`/`OpenWrite`) for backends that support raw streams. |
| `FilePathSecurity` | Opt-in path normalization and sandbox-root validation. |
| `FileIORetry` / `FileIORetryPolicy` | Opt-in backoff retry for transient I/O failures (sharing violations, indexer/AV locks). |

## Composition (DI and non-DI)

`FileUtility` remains a static convenience facade for simple call sites. For testable or platform-pluggable code, depend on `IFileService`, which is backed by an `IFileStorageBackend`.

```csharp
using CycloneGames.IO.Runtime;

// Non-DI: static facade, or the default service instance.
string a = FileUtility.ReadAllText(path);
string b = FileService.Default.ReadAllText(path);

// DI: register the interfaces; inject a platform backend on WebGL/console.
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

The storage backend only handles raw bytes, so a `WebGL IndexedDB` or console save-data backend can be injected without touching encoding, hashing, or path policy. `FilePathSecurity.EnsureWithinRoot(root, path)` should be called explicitly when a path is derived from untrusted input.

## Streaming And Transient Retry

For large payloads, stream instead of loading the whole file into memory:

```csharp
using (var source = FileUtility.OpenRead(sourcePath))
{
    await FileUtility.WriteFromStreamAsync(destinationPath, source, cancellationToken);
}
```

Backends expose streaming through `IStreamingFileStorageBackend` when supported:

```csharp
if (backend is IStreamingFileStorageBackend streaming)
{
    using var read = streaming.OpenRead(path);
}
```

On Windows, antivirus and search indexers can briefly lock files. Wrap a critical write in an opt-in backoff retry:

```csharp
await FileIORetry.ExecuteAsync(
    () => FileUtility.WriteAllTextAtomicAsync(path, json, cancellationToken),
    FileIORetryPolicy.Default,
    cancellationToken);
```

Retries apply only to transient `IOException`. Missing-file, missing-directory, and path-too-long errors are treated as permanent and are not retried.

## Managed File API

Use these methods when a module needs normal managed file access:

```csharp
using CycloneGames.IO.Runtime;

byte[] payload = FileUtility.ReadAllBytes(path);
string json = FileUtility.ReadAllText(path);

await FileUtility.WriteAllBytesAsync(path, payload, cancellationToken);
await FileUtility.WriteAllTextAtomicAsync(path, json, cancellationToken);
```

Text reads detect UTF-8, UTF-16, and UTF-32 byte-order marks. Unmarked text uses strict UTF-8 without BOM, so malformed bytes fail instead of silently becoming replacement characters. Text writes default to UTF-8 without BOM. Pass an explicit `Encoding` only for known legacy data.

For external text whose encoding is not guaranteed, use the smart decoding API. It first honors BOM, then detects strong UTF-16/UTF-32 no-BOM byte patterns, then tries strict UTF-8 and caller-provided fallback encodings.

```csharp
using System.Text;

Encoding[] legacyCandidates = { Encoding.Unicode };
string text = FileUtility.ReadAllTextSmart(path);

if (!FileUtility.TryDecodeTextSmart(bytes, FileUtility.Utf8NoBom, legacyCandidates, out text))
{
    // Ask the caller/importer to specify the source encoding.
}
```

No byte-only detector can perfectly identify every legacy encoding. For game content, prefer converting authoring files to UTF-8 during import/build. Use fallback encodings only when the source system is known and the target platform supports that decoder.

Write helpers create the parent directory when needed. Atomic write helpers write to a temporary file first and then replace the destination, which is preferred for settings, manifests, version files, and other important metadata. They do not write registry entries, `EditorPrefs`, `PlayerPrefs`, or hidden global state.

## Hashing And Comparison

Use `SHA256` at trust boundaries and `XxHash64` for local cache/change detection where cryptographic strength is not required.

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

`XxHash64` is non-cryptographic. Do not use it as the only defense against malicious tampering, paid-content protection, anti-cheat-sensitive payloads, or untrusted downloads.

## UnityWebRequest Paths

Use `FilePathUtility` when a path must be loaded through `UnityWebRequest`, especially StreamingAssets on Android and WebGL.

```csharp
using CycloneGames.IO.Runtime;

string uri = FilePathUtility.GetUnityWebRequestUri(
    "Config/input_config.yaml",
    UnityPathSource.StreamingAssets);
```

Platform notes:

- Windows, macOS, and Linux: direct `System.IO` file paths work for normal disk files.
- Android: StreamingAssets live inside the APK/AAB and should be read through `UnityWebRequest` URI paths.
- WebGL: direct file I/O is limited. Persistent data is the only reliable writable location; long async loops yield periodically.
- iOS and consoles: prefer explicit platform validation for writable locations, storage quotas, and sandbox behavior.

## Persistence

CycloneGames.IO does not own saved data. It only performs requested reads/writes at caller-provided paths.

| Data | Owner | Path | Versioning |
| --- | --- | --- | --- |
| Runtime settings | Calling module | Usually under `Application.persistentDataPath` | Caller-owned schema and migration. |
| Editor user settings | Calling editor tool | Prefer `<repo-root>/UnityStarter/UserSettings/` or equivalent explicit user-local file | Caller-owned. |
| Build/cache artifacts | Calling build or asset module | Explicit cache/build output path | Caller-owned and rebuildable. |

Regular write methods create parent directories but do not perform atomic replace, schema migration, encryption, compression, or rollback. Modules that persist important data should own those policies above this package.

Atomic write methods only protect against partial destination files during normal replacement. They write to a temporary file, flush to disk on a best-effort basis (`Flush(true)` where the platform supports it), and then replace the destination. On filesystems or platforms where `File.Replace` is unavailable (some network shares, FAT, certain mobile/WebGL backends), the implementation falls back to delete-then-move, which is not crash-atomic, and flush-to-disk may be a no-op. Callers still own schema versioning, validation, corruption recovery, encryption, and cross-device synchronization.

## Migration From Unio

The project should not reference `Unio`, `NativeFile`, or `SynchronizationStrategy`. Typical replacements:

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

## Validation

Recommended checks after changes:

1. Open `<repo-root>/UnityStarter` in Unity.
2. Confirm there are no compiler errors for `CycloneGames.IO.Runtime` and direct consumers.
3. Run `Window > CycloneGames > FileUtility Benchmark` for IO correctness, throughput, and GC allocation checks.
4. Search the project for `Unio`, `NativeFile`, and `SynchronizationStrategy`; only historical documentation outside active packages should remain.
5. Test representative paths on each target platform that uses StreamingAssets, persistent data, or WebGL storage.
