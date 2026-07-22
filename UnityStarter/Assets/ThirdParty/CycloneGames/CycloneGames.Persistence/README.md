# CycloneGames.Persistence

[English | 简体中文](README.SCH.md)

CycloneGames.Persistence is a serializer-neutral, Unity-free foundation for one bounded, versioned record. It coordinates a codec and a storage adapter, enforces a strict byte format, detects accidental corruption before deserialization, and returns structured runtime results.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)

## Overview

Persistence coordinates two external adapters — a serializer codec and a file/database storage provider — into a safe single-record pipeline. It wraps every payload with a versioned header, a stable codec identifier, and an xxHash64 checksum, then rejects records whose header, length, or integrity probe does not match before deserialization.

Runtime failures are returned as structured result types with explicit `PersistenceErrorCode` values. Invalid arguments and concurrent overlap throw immediately. Fatal runtime exceptions (`OutOfMemoryException`) are not converted into results.

The core assembly references only `CycloneGames.Hash.Core`. It has no Unity, System.IO, Settings, VYaml, MessagePack, DI, reflection discovery, or background-worker dependency.

### Key Features

- **Record V1 format** — canonical LF-only ASCII header, codec identity, content version, fixed `identity/1`, xxHash64 over dispatch metadata and payload
- **Structured results** — `PersistenceLoadResult<T>` distinguishes Loaded, Missing, and Failed; `PersistenceOperationResult` reports Succeeded or Failed with error codes
- **One operation at a time** — overlap or callback reentrancy throws `InvalidOperationException`
- **Cancellation support** — cancellation token observed at every boundary; commit outcome determines final result
- **Payload limits** — hard cap of 1 MiB; profile-level reduction to 256 KiB for settings use
- **No allocation leakage** — buffers are cleared after use; codecs write into borrowed `IBufferWriter<byte>`

## Architecture

| Assembly | Package | References |
| --- | --- | --- |
| `CycloneGames.Persistence.Core` | `com.cyclone-games.persistence` | `CycloneGames.Hash.Core` |

The package declares a dependency on `com.cyclone-games.hash` (1.0.0). It ships no `UnityEngine` reference (`noEngineReferences: true`) and requires only Unity 2022.3.

Storage and codec providers are separate packages. Persistence Core compiles and tests without them. Applications add the providers their composition root needs:

- `com.cyclone-games.persistence.systemio` — file-system storage
- `com.cyclone-games.persistence.vyaml` — human-readable YAML codec
- `com.cyclone-games.persistence.messagepack` — compact binary codec (optional, inactive by default)

## Quick Start

Install the packages needed by the composition root:

- `com.cyclone-games.persistence`
- one storage provider: `com.cyclone-games.persistence.systemio`
- one codec provider: e.g. `com.cyclone-games.persistence.vyaml`

The application owns the DTO and schema version. VYaml requires a generated resolver; reflection fallback is not accepted for IL2CPP builds.

```csharp
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;
using CycloneGames.Persistence.Unity;
using CycloneGames.Persistence.VYaml;

public static class LocalProfileComposition
{
    public static async Task SaveAsync(
        LocalProfile profile,
        CancellationToken cancellationToken)
    {
        IPersistenceStorage storage = UnityPersistentStorage.Create("profiles/local.yaml");
        var codec = new VYamlPersistenceCodec<LocalProfile>(GeneratedResolver.Instance);
        var limits = new PersistenceLimits(maximumPayloadBytes: 256 * 1024);
        var store = new PersistenceStore<LocalProfile>(
            storage,
            new PersistenceProfile<LocalProfile>(codec, limits));

        PersistenceOperationResult result = await store.SaveAsync(
            in profile,
            contentVersion: 2,
            cancellationToken);
        if (!result.IsSuccess)
        {
            // Route ErrorCode to product recovery policy. Sanitize Exception before telemetry.
        }
    }
}
```

Loading requires the newest content version the caller understands:

```csharp
PersistenceLoadResult<LocalProfile> result = await store.LoadAsync(
    maximumSupportedContentVersion: 2,
    cancellationToken);

switch (result.Status)
{
    case PersistenceLoadStatus.Loaded:
        LocalProfile profile = result.Value;
        break;
    case PersistenceLoadStatus.Missing:
        // Create product defaults; Missing is not an error.
        break;
    case PersistenceLoadStatus.Failed:
        // Keep the previous authoritative state and apply recovery policy.
        break;
    default:
        throw new InvalidOperationException("The load result was not initialized.");
}
```

## Core Concepts

| Type | Responsibility |
| --- | --- |
| `IPersistenceCodec<T>` | Synchronously encode/decode one borrowed payload using a stable `PersistenceCodecId`. |
| `IPersistenceStorage` | Own one location; perform bounded read, atomic write, and idempotent delete. |
| `PersistenceProfile<T>` | Bind one codec and immutable allocation limits. |
| `PersistenceStore<T>` | Serialize, encode Record V1, verify, deserialize, classify failures, and enforce one active operation. |
| `PersistenceLimits` | Bound plaintext payload, record read, and pooled writer growth. |
| `PersistenceLoadResult<T>` | Separate `Loaded`, `Missing`, `Failed`, and an uninitialized default struct. |
| `PersistenceOperationResult` | Report `Succeeded` or `Failed`. `ErrorCode.None` alone is not a success test. |

### Codec contract

Codecs write into a bounded `IBufferWriter<byte>` supplied by the Store. They must not allocate a result array, retain borrowed inputs, discover types through runtime reflection, or write beyond `context.Limits`. Contexts expose the caller token so a synchronous codec can check cancellation at bounded work boundaries. `T` is a pure DTO; it must not own `IDisposable` resources, unmanaged handles, Unity object lifetimes, or thread-affine state — failed and cancelled candidates are discarded without disposal.

```csharp
public sealed class ExampleCodec<T> : IPersistenceCodec<T>
{
    public PersistenceCodecId CodecId { get; } = new PersistenceCodecId("example/1");

    public void Serialize(
        in T value,
        IBufferWriter<byte> destination,
        in PersistenceWriteContext context)
    {
        // Write a bounded, deterministic representation to destination.
    }

    public T Deserialize(
        ReadOnlyMemory<byte> payload,
        in PersistenceReadContext context)
    {
        // Do not retain payload. Use context.ContentVersion when old wire shapes remain readable.
        throw new NotImplementedException();
    }
}
```

`ReadOnlyMemory<byte>` lets VYaml and MessagePack consume memory directly, avoiding a whole-payload adapter copy. The ownership contract still prohibits retention after return.

## Usage Guide

### Save flow

1. Codec writes to a clear-on-return pooled buffer.
2. Store wraps the payload with Record V1 header and xxHash64.
3. An exact record array is created and handed to the storage adapter.
4. The array is cleared when the write task completes.

### Load flow

1. Storage transfers one exact record array.
2. Store parses in place: validates header, codec ID, content version, and checksum.
3. A `ReadOnlyMemory<byte>` slice is lent to the codec.
4. The array is cleared after deserialization.

### Error codes

| ErrorCode | Condition |
| --- | --- |
| `None` | Default; never indicates success. |
| `ReadFailed` | Storage read exception. |
| `PayloadTooLarge` | Codec or record exceeds configured limit. |
| `RecordFormatMismatch` | Unknown magic or non-Record-V1 format. |
| `UnsupportedRecordVersion` | Record version beyond what the Store knows. |
| `MalformedRecord` | Structural header error. |
| `IntegrityCheckFailed` | xxHash64 mismatch. |
| `CodecMismatch` | Codec ID in record does not match profile codec. |
| `TransformMismatch` | Transform identity mismatch (reserved). |
| `FutureContentVersion` | Content version exceeds maximum supported. |
| `DeserializeFailed` | Codec deserialization exception. |
| `SerializationFailed` | Codec serialization exception. |
| `WriteFailed` | Storage write exception. |
| `DeleteFailed` | Storage delete exception. |
| `Cancelled` | Caller token cancelled. |

### Concurrency and memory

- Payload hard limit: 1 MiB; profiles may lower it; settings use 256 KiB.
- Complexity: O(n) time, bounded O(n) temporary memory.
- One Store accepts one operation at a time. Overlap throws `InvalidOperationException`.
- The guard is scoped to that Store instance. Multiple Stores sharing a codec, resolver, or storage instance are not serialized automatically.
- Store creates no worker threads, does not use `Task.Run`, and uses `ConfigureAwait(false)` internally.
- Only an `OperationCanceledException` associated with a cancelled caller token is classified as `Cancelled`. Unsolicited codec or provider cancellation is a stage failure.
- The storage commit boundary decides cancellation semantics. A successful commit reports success even if the token is cancelled afterward.

Persistence is a cold-path facility. Do not call it every frame or use it as a per-entity update mechanism.

## Advanced Topics

### Record format

Record V1 uses a canonical LF-only ASCII header, exact payload bytes, a stable codec ID, fixed `identity/1`, and xxHash64 over dispatch metadata plus payload. See [PersistenceRecordV1.md](Documentation~/PersistenceRecordV1.md) for the byte contract and parser precedence.

xxHash64 detects accidental corruption only. It does not authenticate an attacker-controlled file. Do not store secrets or trust local values as payment, entitlement, or server authority.

### Platform matrix

| Layer | Editor/desktop/mobile/server | WebGL | Console |
| --- | --- | --- | --- |
| Persistence Core | Static compatibility; pure managed code | Static compatibility | Static compatibility |
| SystemIO provider | Validated file-system targets | Fail closed | Fail closed until qualified |
| Unity SystemIO path adapter | `Application.persistentDataPath` composition | Fail closed | Fail closed until qualified |
| Custom provider | Optional | IndexedDB/JavaScript async provider required | Platform SDK save-data provider required |

The matrix records static boundaries. IL2CPP, stripping, mobile, WebGL, console, filesystem durability, and long-running stability require separate reproducible Player evidence.

### Storage behavior

Core writes no file and knows no path. The `IPersistenceStorage` implementation owns location, lifecycle, atomicity, deletion, quota, and recovery. The SystemIO provider writes one explicit file below a consumer-selected root, using a same-directory temporary file and atomic replace. That file is product runtime data and may be deleted only according to product recovery policy.

### Migration

The Store never guesses old formats. Prototype records, raw YAML, and unknown magic return `RecordFormatMismatch`. Product-owned migration must run before adopting Record V1. See [ADR-001](Documentation~/ADR-001-Settings-Persistence-Boundaries.md) for package boundaries, ownership, and the breaking migration from the retired combined settings/persistence package.

## Troubleshooting

| Symptom | Cause | Solution |
| --- | --- | --- |
| `RecordFormatMismatch` on load | File was written without Record V1 wrapper | Run product migration before adopting the Store. |
| `CodecMismatch` on load | Record was written with a different codec | Verify codec ID matches; orphaned records need migration. |
| `FutureContentVersion` on load | Record content version exceeds `maximumSupportedContentVersion` | Bump the maximum or add a migration step for the newer version. |
| `IntegrityCheckFailed` on load | File was manually edited or corrupted | Restore from a known-good backup; never bypass checksums. |
| `PayloadTooLarge` on save | DTO exceeds configured limit | Increase `PersistenceLimits.MaximumPayloadBytes` or split the record. |
| `InvalidOperationException` on concurrent calls | Overlapping Save/Load/Delete on one Store | Serialize calls; one Store = one active operation. |
| `OperationCanceledException` not caught | Catching only `Exception` in the composition root | Catch or check `result.ErrorCode == PersistenceErrorCode.Cancelled`. |
| Tests fail on IL2CPP Player | Reflection fallback used instead of generated resolver | Replace all reflection-based formatters with source-generated or registered ones. |
