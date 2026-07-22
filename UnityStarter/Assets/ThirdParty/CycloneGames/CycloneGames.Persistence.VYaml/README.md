# CycloneGames.Persistence.VYaml

[English | 简体中文](README.SCH.md)

Human-readable YAML codec that bridges `CycloneGames.Persistence` to VYaml. Converts typed persistence DTOs to UTF-8 YAML through an explicitly supplied generated resolver. Persistence Core owns record framing, content version metadata, byte limits, checksum verification, and storage orchestration.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)

## Overview

`VYamlPersistenceCodec<T>` implements `IPersistenceCodec<T>` with the stable identifier `vyaml/1`. It serializes through an `IYamlFormatterResolver` using a composite resolver chain: the caller's generated resolver, VYaml standard formatters, and VYaml Unity formatters (only when the first two resolvers do not own the type).

Use this codec for settings and diagnostic data where human reviewability matters more than binary compactness. YAML readability is a diagnostic convenience — it does not bypass record integrity, migration, validation, or the record byte budget.

### Key Features

- Stable codec ID `vyaml/1`
- Composite resolver chain — primary resolver + `StandardResolver` + `UnityResolver` fallback
- UTF-8 without BOM, canonical LF line endings
- Direct `IBufferWriter<byte>` serialization — no intermediate array allocation
- `ReadOnlyMemory<byte>` deserialization — no retained borrow after return
- Resolver validation at construction — rejects resolvers that lack a formatter for `T`

## Architecture

| Assembly | Package | References |
| --- | --- | --- |
| `CycloneGames.Persistence.VYaml` | `com.cyclone-games.persistence.vyaml` | `CycloneGames.Persistence.Core`, `VYaml.Core` |

The assembly uses `autoReferenced: false` and `noEngineReferences: true`. Persistence Core has no VYaml dependency. Applications that do not reference this provider do not compile its codec into their dependency graph.

VYaml installation requires two coordinated parts:
1. NuGetForUnity installs `VYaml`, `VYaml.Annotations`, and source-generator artifacts.
2. Unity Package Manager installs `jp.hadashikick.vyaml`, which supplies `VYaml.Core` and Unity formatters while referencing NuGet `VYaml.dll`.

Keep the binary, annotations, source generator, and Unity bridge on the same version. The supported baseline is VYaml `1.4.0`.

## Quick Start

Define an explicit VYaml contract:

```csharp
using VYaml.Annotations;

[YamlObject]
public sealed partial class AudioSettings
{
    public float MasterVolume { get; set; } = 1f;
    public bool Muted { get; set; }
}
```

Create the codec with the source-generated resolver:

```csharp
using CycloneGames.Persistence.VYaml;
using VYaml.Serialization;

var codec = new VYamlPersistenceCodec<AudioSettings>(
    GeneratedResolver.Instance);
```

Bind into `PersistenceProfile<AudioSettings>` and create `PersistenceStore<AudioSettings>` with the selected storage provider. The composition root owns the profile and store. This package does not use a global resolver or service locator.

## Core Concepts

### Composite resolver

The codec builds a custom resolver chain at construction time:

```csharp
private sealed class PersistenceYamlResolver : IYamlFormatterResolver
{
    private readonly IYamlFormatterResolver _primaryResolver;

    public IYamlFormatter<TValue> GetFormatter<TValue>()
    {
        // 1. Caller's generated resolver
        IYamlFormatter<TValue> formatter = _primaryResolver.GetFormatter<TValue>();
        if (formatter != null) return formatter;

        // 2. VYaml standard formatters (primitives, collections)
        formatter = StandardResolver.Instance.GetFormatter<TValue>();
        if (formatter != null) return formatter;

        // 3. VYaml Unity formatters (UnityEngine types)
        return UnityResolver.Instance.GetFormatter<TValue>();
    }
}
```

There is no reflection discovery, resolver registry, mutable global default, or runtime type-name contract.

### Serialization and deserialization

```csharp
// Serialize: write directly to bounded IBufferWriter<byte>
public void Serialize(in T value, IBufferWriter<byte> destination,
    in PersistenceWriteContext context)
{
    var emitter = new Utf8YamlEmitter(destination);
    YamlSerializer.Serialize(ref emitter, value, _serializerOptions);
}

// Deserialize: read borrowed ReadOnlyMemory<byte>
public T Deserialize(ReadOnlyMemory<byte> payload,
    in PersistenceReadContext context)
{
    return YamlSerializer.Deserialize<T>(payload, _serializerOptions);
}
```

## Usage Guide

### Generated formatter namespace safety

The supported VYaml source generator emits formatter code with references such as `VYaml.Annotations` that are not prefixed with `global::`. A DTO namespace that introduces a local `VYaml` namespace can shadow the dependency's root namespace, producing misleading missing-namespace compiler errors.

Keep generated persistence DTOs in a neutral domain namespace:

```csharp
namespace MyGame.Settings.Contracts;
```

Avoid placing `[YamlObject]` DTOs in namespaces such as `MyGame.Persistence.VYaml`. If generated code reports that `Annotations` or `Serialization` does not exist below a project-local `VYaml` namespace, move the DTO to a neutral namespace and rebuild the resolver.

### Readability and integrity

The record header is a valid YAML comment preamble followed by the exact YAML payload. The payload is readable for diagnosis, but runtime integrity remains strict. Manual edits invalidate xxHash64 and are rejected.

If a product needs editable imports, implement an Editor/import workflow that parses untrusted YAML, validates the domain object, and rewrites it through the official save pipeline. Do not add a runtime checksum bypass.

## Advanced Topics

### AOT and IL2CPP

The constructor requires `IYamlFormatterResolver`. Pass the generated resolver from the assembly that owns the serialized DTO. It checks that a formatter exists for `T` before accepting the resolver.

Editor compilation of a generated resolver is not IL2CPP evidence. Every released DTO set needs a Player/AOT smoke test.

### Hostile YAML safety

The current provider is qualified for trusted local settings. The 1 MiB record limit bounds bytes and allocation, but it does not prove bounded parser depth, alias/anchor expansion, CPU time, or stack usage. Do not feed attacker-controlled YAML to this codec until the selected VYaml version has reproducible hostile-input fixtures and an enforced parser budget. An editable import workflow must impose those limits before domain validation.

### Platform and threading

The codec is synchronous managed code and exposes no Unity types in its public contract. Generated DTOs containing only standard managed types do not initialize the Unity resolver. A DTO containing a Unity type enters the `VYaml.Core` Unity formatter path and requires a Unity host.

The codec may run on the thread selected by the owning persistence workflow. Do not share a mutable resolver or DTO across threads without an explicit ownership policy.

## Troubleshooting

| Symptom | Cause | Solution |
| --- | --- | --- |
| Constructor throws `ArgumentNullException` | Null resolver passed | Pass `GeneratedResolver.Instance` or a concrete `IYamlFormatterResolver`. |
| Constructor throws `ArgumentException` | Resolver has no formatter for `T` | Add `[YamlObject]` attribute to the DTO and regenerate the resolver. |
| Compiler error: missing namespace `VYaml.Annotations` | DTO namespace shadows the VYaml dependency | Move DTO to a neutral namespace (e.g. `MyGame.Settings.Contracts`). |
| Malformed YAML not caught by Persistence Store | Record-level integrity checks pass but YAML is broken | Persistence Core rejects checksum failures; use VYaml tests for structural YAML issues. |
| `MissingFormatterException` in IL2CPP Player | Reflection-based resolver or missing generated formatter | Verify all DTOs have `[YamlObject]` and the generated resolver is included in the Player build. |
| Encoding contains carriage returns | VYaml is not emitting canonical LF | Verify VYaml version is `1.4.0`; provider tests validate LF-only output. |
| NuGet/Unity version mismatch | VYaml binary and Unity bridge versions differ | Pin both to the same immutable tag or commit; mismatched artifacts are install errors. |
