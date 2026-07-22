# CycloneGames.Persistence.MessagePack

[English | 简体中文](README.SCH.md)

Optional compact binary codec that bridges `CycloneGames.Persistence` to MessagePack-CSharp. Converts typed persistence DTOs through an explicitly supplied source-generated resolver and a mandatory untrusted-data security policy.

The provider source ships with the package but compiles only when `com.github.messagepack-csharp` `3.1.8` is installed. The current UnityStarter checkout does not activate MessagePack. The assembly is gated by `versionDefines` producing `CYCLONEGAMES_HAS_MESSAGEPACK`.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)

## Overview

`MessagePackPersistenceCodec<T>` implements `IPersistenceCodec<T>` with the stable identifier `messagepack/1`. It serializes through an explicit `IFormatterResolver` and a `MessagePackSecurity` policy derived from `UntrustedData`. Persistence Core owns record framing, content version metadata, byte limits, checksum verification, and storage orchestration.

The codec enforces: no compression (compression changes the wire profile and requires a new identifier), no runtime type-name resolution, no reflection-based formatter discovery, no trailing bytes after one decoded value, and no assembly-version loosening (`OmitAssemblyVersion` and `AllowAssemblyVersionMismatch` are disabled). The constructor rejects a resolver that lacks an explicit formatter for `T` or a security policy without collision-resistant hashing, positive graph depth, and positive decompressed-size limit.

### Key Features

- Stable codec ID `messagepack/1` — uncompressed current-spec option profile with explicit generated formatter schema
- Security-first constructor — enforced collision-resistant hashing, graph-depth limit, decompressed-size cap
- Trailing-byte rejection — decoded value must consume the entire payload
- Source-generated formatters only — no reflection or typeless resolvers
- Direct `IBufferWriter<byte>` serialization — no intermediate array allocation

## Architecture

| Assembly | Package | References |
| --- | --- | --- |
| `CycloneGames.Persistence.MessagePack` | `com.cyclone-games.persistence.messagepack` | `CycloneGames.Persistence.Core`, `MessagePack.dll` |

The assembly uses `defineConstraints: ["CYCLONEGAMES_HAS_MESSAGEPACK"]` and `versionDefines` for `com.github.messagepack-csharp` `[3.1.8,4.0.0)`. Without the Unity package installed, the assembly does not compile. Do not add `CYCLONEGAMES_HAS_MESSAGEPACK` manually.

### Activation state

| Item | Current checkout |
| --- | --- |
| Provider source | Present |
| `CycloneGames.Persistence.MessagePack` assembly | Inactive |
| Provider tests | Inactive |
| `com.github.messagepack-csharp` | Not installed |
| NuGet `MessagePack` binary, annotations, and analyzer | Not installed |

## Quick Start

Before using this provider, install the full dependency set:

1. Install the exact `MessagePack` NuGet package with matching annotations and analyzer/source generator.
2. Install the official `com.github.messagepack-csharp` Unity package at the same version, pinned to an immutable tag or commit.
3. Let Unity resolve `CYCLONEGAMES_HAS_MESSAGEPACK`.
4. Reference `CycloneGames.Persistence.MessagePack` from the consumer asmdef.

Define a stable binary contract:

```csharp
using MessagePack;

[MessagePackObject]
public sealed class PlayerPreferences
{
    [Key(0)] public float LookSensitivity { get; set; }
    [Key(1)] public bool InvertY { get; set; }
}
```

Construct the codec:

```csharp
MessagePackSecurity security = MessagePackSecurity.UntrustedData
    .WithMaximumObjectGraphDepth(64)
    .WithMaximumDecompressedSize(1024 * 1024);

var codec = new MessagePackPersistenceCodec<PlayerPreferences>(
    GeneratedMessagePackResolver.Instance,
    security);
```

Bind into `PersistenceProfile<PlayerPreferences>` and create the Store as usual. Persistence Core handles record framing, limits, versioning, and checksums.

## Core Concepts

### Codec construction

```csharp
public sealed class MessagePackPersistenceCodec<T> : IPersistenceCodec<T>
{
    public MessagePackPersistenceCodec(
        IFormatterResolver resolver,
        MessagePackSecurity security)
    {
        // Validates resolver has formatter for T
        // Validates security: collision-resistant hash,
        //   positive graph depth, positive decompressed-size limit
        // Builds locked options: No compression, current spec,
        //   OmitAssemblyVersion=false, AllowAssemblyVersionMismatch=false
    }

    public PersistenceCodecId CodecId => new PersistenceCodecId("messagepack/1");
}
```

The options are locked at construction. Callers cannot substitute a different option profile under the same codec identifier. Changing option-level wire behavior requires a new identifier and explicit format decision.

### Serialization and deserialization

- **Serialize** — writes directly to `IBufferWriter<byte>` via `MessagePackSerializer.Serialize`, passing the cancellation token from `PersistenceWriteContext`
- **Deserialize** — reads borrowed `ReadOnlyMemory<byte>`, validates that `bytesRead == payload.Length`, rejecting trailing bytes

## Usage Guide

### Schema policy

- Use `[MessagePackObject]` with explicit integer `[Key]` values.
- New fields use new optional keys.
- Removed keys remain permanently reserved — never reuse them.
- Contractless, typeless, and reflection-generated resolvers fall outside the `messagepack/1` contract.
- Do not persist runtime type names.
- Schema migration is the responsibility of the owning domain, not this provider.

### Security constraints

The constructor requires `MessagePackSecurity.UntrustedData` as the base policy and rejects:
- Policies without collision-resistant hashing
- Zero or negative `MaximumObjectGraphDepth`
- Zero or negative `MaximumDecompressedSize`
- Decompressed size exceeding `PersistenceLimits.HardMaximumPayloadBytes`

Persistence Core verifies record length, codec identity, transform identity, and xxHash64 before invoking the codec. xxHash64 detects accidental modification but is not authentication.

## Advanced Topics

### Compression and wire profile

`messagepack/1` disables MessagePack compression. Enabling compression creates a different wire profile and requires a new codec identifier. Compression is not encryption. Authenticated encryption belongs in a separately reviewed persistence security provider.

### AOT and IL2CPP

Unity IL2CPP requires source-generated or explicitly registered formatters for every persisted DTO. Editor/Mono reflection fallback is not release evidence. Keep the MessagePack binary, annotations, analyzer, generated resolver, and Unity package on one version.

Before enabling in a released product, perform an IL2CPP round trip with the exact DTO assembly and stripping configuration.

### Memory and performance

Serialization writes directly to Persistence Core's bounded `IBufferWriter<byte>`. Deserialization consumes borrowed `ReadOnlyMemory<byte>`. Both are synchronous CPU work. Allocation depends on DTO and MessagePack formatter behavior. Persistence is a bounded cold path — do not call it per frame.

## Troubleshooting

| Symptom | Cause | Solution |
| --- | --- | --- |
| Assembly does not compile | `com.github.messagepack-csharp` not installed or version mismatch | Install `3.1.8` pinned to immutable tag; verify `versionDefines` resolves. |
| Constructor throws `ArgumentException` | Resolver lacks explicit formatter for `T` | Add `[MessagePackObject]` and `[Key]` attributes; regenerate the resolver. |
| Constructor throws `ArgumentException` | Security policy has zero depth or size limits | Use `MessagePackSecurity.UntrustedData` with positive limits. |
| `InvalidDataException` with trailing bytes | Payload contains extra data after one value | Verify serialized output; check for accidental double-encoding. |
| Deserialization returns wrong data | Key values changed or reused | Preserve removed keys; add new fields with new optional keys. |
| IL2CPP Player throws `MissingFormatterException` | Reflection-based or typeless resolver used | Switch to source-generated resolver with explicit formatters. |
| Settings file becomes unreadable | Compression was toggled on a live profile | Compression changes wire format; old records need migration or a new codec ID. |
