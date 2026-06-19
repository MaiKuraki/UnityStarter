# CycloneGames.Hash

CycloneGames.Hash provides pure C# deterministic hashing primitives for CycloneGames foundation modules. It is intended for stable identifiers, protocol manifests, data-table schema hashes, build cache checks, and hot-update compatibility validation.

This package is deliberately separate from `CycloneGames.Utility`: it has no Unity API dependency, no logger dependency, no Burst dependency, and no unsafe code requirement.

## Design Goals

- Deterministic output across Unity Editor, player builds, headless servers, CI, and tools.
- Explicit endian handling for serialized hash values.
- Zero-GC hot-path APIs based on `ReadOnlySpan<byte>` and stack-friendly structs.
- Small dependency surface suitable for low-level modules such as GameplayTags, Networking, GameplayAbilities, and DataTable.
- Clear distinction between non-cryptographic consistency checks and tamper-proof security.

## Assemblies

| Assembly | Path | Purpose |
| --- | --- | --- |
| `CycloneGames.Hash.Core` | `Core/` | Pure C# deterministic hash algorithms and byte-order helpers. |
| `CycloneGames.Hash.Tests.Editor` | `Tests/Editor/` | EditMode tests for known vectors and streaming consistency. |

`CycloneGames.Hash.Core` uses `noEngineReferences=true` and has no assembly references.

## Core Types

| Type | Use case |
| --- | --- |
| `Fnv1a64` | Stable IDs, ordered manifests, small protocol fingerprints. |
| `StableHash64` | Non-zero stable hash helpers for module-level identifiers and manifest accumulation. |
| `XxHash64` | Fast content hashing for build cache checks, payload checks, and large byte buffers. |
| `HashByteOrder` | Explicit little-endian and big-endian `ulong` read/write helpers. |

## Usage

Stable IDs and manifests:

```csharp
ulong tagId = StableHash64.ComputeUtf16Ordinal("Ability.Damage.Fire");

ulong manifest = Fnv1a64.OffsetBasis;
manifest = StableHash64.CombineUInt64LittleEndian(manifest, tagId);
```

Fast content checks:

```csharp
ulong payloadHash = XxHash64.Compute(payloadBytes);

XxHash64 streaming = XxHash64.Create();
streaming.Append(chunkA);
streaming.Append(chunkB);
ulong finalHash = streaming.GetDigest();
```

Serialized hash values:

```csharp
Span<byte> buffer = stackalloc byte[8];
HashByteOrder.WriteUInt64LittleEndian(buffer, payloadHash);
```

## Algorithm Policy

`Fnv1a64` and `XxHash64` are non-cryptographic. They are appropriate for detecting accidental differences, schema mismatches, manifest incompatibility, cache invalidation, and protocol handshakes.

Do not use these algorithms as the only defense against malicious tampering. Hot-update packages, CDN downloads, paid content, account data, and anti-cheat-sensitive payloads should use a signed manifest or a cryptographic hash such as SHA-256 at the security boundary.

`ComputeUtf16Ordinal` intentionally hashes .NET UTF-16 code units. This preserves legacy stable ID semantics for systems that already used ordinal string code-unit hashing. Use byte-based APIs when the serialized byte representation is the source of truth.

## Persistence

This package does not write files, assets, registry entries, `EditorPrefs`, `PlayerPrefs`, or hidden global state. Consumers own all persisted manifests, cache files, protocol packets, and schema hash records.

## Integration Guidance

- GameplayTags should use `StableHash64` for tag stable IDs and manifest hashes.
- Networking can use `HashByteOrder` and `StableHash64` for protocol version manifests and compatibility handshakes.
- DataTable can use `XxHash64` for generated payload checks and `StableHash64` for schema fingerprints.
- GameplayAbilities can use stable hashes for ability-set manifests, but ability logic should still use explicit IDs or GameplayTags as its domain contract.

## Validation

CLI build checks:

```bash
dotnet build CycloneGames.Hash.Core.csproj -v:minimal
dotnet build CycloneGames.Hash.Tests.Editor.csproj -v:minimal
```

Unity Editor checks:

1. Open `<repo-root>/UnityStarter`.
2. Run the EditMode tests in `CycloneGames.Hash.Tests.Editor`.
3. Confirm known-vector tests pass on every target platform used for gameplay simulation or build tooling.
