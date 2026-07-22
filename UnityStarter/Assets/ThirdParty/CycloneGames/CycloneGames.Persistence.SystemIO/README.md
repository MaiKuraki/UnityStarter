# CycloneGames.Persistence.SystemIO

[English | 简体中文](README.SCH.md)

File-system storage provider for `CycloneGames.Persistence`. Binds one fully qualified file path to the asynchronous persistence storage contract, delegates bounded reads and atomic writes to `CycloneGames.IO`, and ships a separate Unity assembly that resolves relative paths below `Application.persistentDataPath`.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)

## Overview

`SystemFilePersistenceStorage` implements `IPersistenceStorage` for desktop, mobile, and dedicated-server environments. It performs missing detection and bounded reads in one operation, borrows caller buffers for atomic writes, and treats an already-absent record as success for deletion. The provider owns no worker, cache, queue, retry loop, or timer.

`UnityPersistentStorage` is a separate static factory in the `CycloneGames.Persistence.Unity` assembly that maps portable relative paths (e.g. `"Settings/audio.cgp"`) to `SystemFilePersistenceStorage.CreateSandboxed` below `Application.persistentDataPath`. Call it on the Unity main thread; the returned storage no longer touches Unity APIs.

Neither assembly selects a codec or understands settings, save slots, migrations, encryption, or game state.

### Key Features

- **`IPersistenceStorage` contract** — bounded read with missing detection, atomic borrow-write, idempotent delete
- **Atomic replacement** — writes use a same-directory temporary file (`WriteBytesAtomicallyAsync` from `CycloneGames.IO`), then replace the destination
- **Sandboxed path binding** — `CreateSandboxed` rejects traversal, rooted paths, empty segments, reserved device names, and lexical root escape
- **Platform guard** — WebGL and unqualified console targets throw `PlatformNotSupportedException`; desktop, mobile, and server are pass-through
- **Unity path adapter** — `UnityPersistentStorage.Create` resolves below `Application.persistentDataPath`; requires Unity main thread at construction only

## Architecture

| Assembly | References | `autoReferenced` |
| --- | --- | --- |
| `CycloneGames.Persistence.SystemIO` | `CycloneGames.Persistence.Core`, `CycloneGames.IO.Core`, `CycloneGames.IO.SystemIO` | `false` |
| `CycloneGames.Persistence.Unity` | `CycloneGames.Persistence.Core`, `CycloneGames.Persistence.SystemIO` | `false` |

The SystemIO assembly has `noEngineReferences: true`. The Unity assembly has `noEngineReferences: false` (it references `UnityEngine` for `Application.persistentDataPath`). Consumers reference only the assembly their composition root uses.

The package has no VYaml, MessagePack, Settings, AssetManagement, DI-container, or PlayerPrefs dependency.

## Quick Start

Reference from the composition root that owns the record:

```csharp
using CycloneGames.Persistence;
using CycloneGames.Persistence.Unity;

IPersistenceStorage storage = UnityPersistentStorage.Create(
    "Settings/audio.cgp");
```

For a pure C# host, supply a fully qualified path:

```csharp
using System.IO;
using CycloneGames.Persistence;
using CycloneGames.Persistence.SystemIO;

IPersistenceStorage storage = new SystemFilePersistenceStorage(
    Path.GetFullPath("./Data/player.cgp"));
```

Pass the storage to a `PersistenceStore<T>`. One storage instance represents one record. Use a different trusted path for each independent record or save slot.

## Core Concepts

### Storage contract

| Method | Behavior |
| --- | --- |
| `ReadAsync(int maxByteCount, CancellationToken)` | Missing detection + bounded read in one operation. Returns `PersistenceStorageReadResult.Found(byte[])` or `Missing()`. |
| `WriteAtomicallyAsync(byte[] content, CancellationToken)` | Borrows caller array until task completes. Provider does not retain or mutate the buffer. |
| `DeleteAsync(CancellationToken)` | Idempotent. Already-missing record is treated as success. |
| `Location` | Diagnostic metadata. Redact the absolute path before telemetry or player-facing output. |

```csharp
await storage.WriteAtomicallyAsync(recordBytes, cancellationToken);
// The array is safe to clear after the task completes.
```

### Path binding

The direct constructor accepts only fully qualified paths:

```csharp
var storage = new SystemFilePersistenceStorage(
    Path.GetFullPath("./Data/player.cgp"));
```

`CreateSandboxed` accepts a portable relative path under a trusted root:

```csharp
var storage = SystemFilePersistenceStorage.CreateSandboxed(
    "/home/app/data",
    "profiles/user.cgp");
```

The sandbox rejects: `../..` traversal, rooted paths, empty segments, reserved device names (`CON`, `NUL`, etc.), and lexical root escape.

## Usage Guide

### Atomic write and cancellation

Writes create a temporary file in the destination directory. The temp file is written and flushed before replacement. If the platform supplies the atomic primitive, a failed replacement preserves the previous complete destination.

Cancellation is observed during asynchronous transfer. A cancellation request while another writer owns the per-path commit coordinator is observed immediately after this writer enters the coordinator; waiting for the monitor is not cancellable. Once the final cancellation check succeeds inside the coordinator, replacement is the non-cancellable commit point. The operation reports the actual commit outcome — not cancellation after a successful replacement.

Atomic replacement is not backup rotation and does not guarantee survival of every power-loss or storage-controller failure.

### Temp file cleanup

Abrupt process termination can leave a completed `.cyclone-*.tmp` file before commit or cleanup. This provider does not scan or delete temp files at startup. Any cleanup policy must have an explicit owner and validate directory scope, filename pattern, age, and active-writer exclusion before deletion.

### Application ownership

The application owns:
- the trusted root and relative record name
- the storage and `PersistenceStore<T>` lifetime
- serialization, migration, validation, and save timing
- error reporting and user-facing recovery policy

The provider never deletes a damaged record automatically and uses only its normalized bound path.

## Advanced Topics

### Platform behavior

The System.IO provider targets Windows, Linux, macOS, iOS, Android, and dedicated-server environments after the target Player has verified file-system and atomic-replacement behavior.

WebGL and unqualified console targets fail closed via the `UNITY_SERVER`-based preprocessor guard in `EnsurePlatformSupported()`:

```csharp
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR && !UNITY_STANDALONE \
    && !UNITY_IOS && !UNITY_ANDROID && !UNITY_SERVER
    throw new PlatformNotSupportedException("...");
#endif
```

They require separate asynchronous providers for IndexedDB or the platform save-data SDK.

### Path security

The lexical path check assumes a trusted local filesystem where another actor cannot replace a directory with a symlink or reparse point between binding and an operation. It is not a hostile-filesystem sandbox boundary. Products with adversarial local writers require a platform provider built around directory handles or equivalent no-follow primitives.

### Performance

The provider is a cold-path buffered implementation. Reads allocate one primary bounded record array plus bounded adapter overhead. Writes borrow an already-materialized record array — no provider-level content clone. Work is O(record bytes). Do not call persistence from a frame loop or input callback.

## Troubleshooting

| Symptom | Cause | Solution |
| --- | --- | --- |
| `ArgumentNullException` on write | Passed `null` content array | Always pass the exact record array from `PersistenceStore`. |
| `ArgumentException` on construction | Path is not fully qualified | Use `Path.GetFullPath` or `CreateSandboxed`. |
| `ArgumentException` on `CreateSandboxed` | Relative path contains `..` or root escape | Use a portable relative path without traversal. |
| `PlatformNotSupportedException` on WebGL | SystemIO provider is not compiled for WebGL | Inject an IndexedDB-backed `IPersistenceStorage` adapter. |
| Write succeeds but old file remains | Atomic replace failed on platform missing the primitive | Verify platform file-system capabilities; test on target hardware. |
| `FileNotFoundException` wrapping a valid path | Parent directory does not exist | Create directory structure before first write; the provider does not create directories. |
| `.cyclone-*.tmp` files accumulate | Process terminated during write | Implement a startup cleanup policy with directory scope, pattern, age, and active-writer exclusion checks. |
| `UnityPersistentStorage.Create` throws on background thread | `Application.persistentDataPath` requires main thread | Call from `Awake` or `Start`; pass the constructed storage as a dependency. |
