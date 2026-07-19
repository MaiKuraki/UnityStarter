# CycloneGames.DataTable

[English | 简体中文](README.SCH.md)

CycloneGames.DataTable loads typed configuration data — item definitions, gameplay tags, ability stats, progression curves, localization metadata — as immutable table snapshots indexed for O(1) key lookup. Every payload is bounded by `DataTableLoadLimits` and verified against a SHA-256 manifest before anyone reads it. The core assembly has no `UnityEngine` dependency; Luban and MessagePack adapters handle serialization in separate integration assemblies.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Common Scenarios](#common-scenarios)
- [Performance and Memory](#performance-and-memory)
- [Troubleshooting](#troubleshooting)

## Overview

The module exposes `DataTable<TKey, TRow>` for source-ordered rows, `DataTableCatalog` for grouping multiple typed tables into one snapshot, and `DataTableRegistry` for atomic process-wide publication. Mutable game state, save transactions, network replication, database queries, and schema-specific business rules live in their own systems.

### Key Features

- **Immutable table snapshots** with source-ordered rows and expected `O(1)` key lookup.
- **Typed catalogs** grouping multiple tables under exact contract types.
- **Atomic process-wide publication** via `DataTableRegistry` with volatile snapshot reads.
- **Bounded payload loading** through `DataTableBytesCache` and `DataTableLoadLimits`.
- **Integrity metadata** via `DataTableManifest` (schema version, required tables, byte length, SHA-256).
- **AOT-safe registration** of generated table sets via explicit `TableDescriptor<TTableSet>` — no runtime reflection.
- **Luban and MessagePack adapters** isolated from the pure C# Core assembly.
- **Unity Editor integration** with `DataTableLubanSettings`, custom Inspector, and a guarded external-process runner.

## Architecture

| Assembly | Namespace | Responsibility |
| --- | --- | --- |
| `CycloneGames.DataTable.Core` | `CycloneGames.DataTable` | Tables, catalogs, registry, limits, manifests, hashes, byte cache, locations, logging, scopes. Pure C# with `noEngineReferences: true`. |
| `CycloneGames.DataTable.Unity.Runtime` | `CycloneGames.DataTable.Unity` | Unity runtime logging bootstrap. |
| `CycloneGames.DataTable.Unity.Editor` | `CycloneGames.DataTable.Unity.Editor` | `DataTableLubanSettings`, custom Inspector, request validation, external-process execution. Editor only. |
| `CycloneGames.DataTable.Unity.Runtime.Integrations.Luban` | `CycloneGames.DataTable.Unity.Integrations.Luban` | Bounded Luban `ByteBuf` creation and generated table-set construction. |
| `CycloneGames.DataTable.Unity.Runtime.Integrations.MessagePack` | `CycloneGames.DataTable.Unity.Integrations.MessagePack` | Bounded MessagePack row-array decoding. |
| `CycloneGames.DataTable.Unity.Runtime.Integrations.AssetManagement` | `CycloneGames.DataTable.Unity.Integrations.AssetManagement` | Optional UniTask-based `TextAsset` and raw-file payload loaders; inactive in the asset-style installation. |

Core and Unity Runtime are auto-referenced. Editor and integration assemblies use `autoReferenced: false`; a consumer asmdef must reference each assembly it actually uses. Luban and MessagePack integrations also require their declared packages and version constraints to be satisfied. The asset-style AssetManagement module does not generate the UPM `versionDefines` capability required by its DataTable integration, so that integration remains inactive — adding an asmdef reference alone does not enable it.

```mermaid
flowchart LR
    A[Generated rows or payload bytes] --> B[Apply limits and integrity checks]
    B --> C[Decode and validate rows]
    C --> D[Build typed DataTable instances]
    D --> E[Build candidate DataTableCatalog]
    E --> F[Validate cross-table references]
    F --> G[Inject catalog or publish atomically]
    G --> H[Read-only gameplay consumers]

    classDef input fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef validation fill:#fef3c7,stroke:#d97706,color:#451a03
    classDef snapshot fill:#dcfce7,stroke:#16a34a,color:#052e16
    class A input
    class B,C,F validation
    class D,E,G,H snapshot
```

Construction, decoding, hashing, and validation are cold-path work. Gameplay code reads from an already published table or catalog.

## Quick Start

Add `CycloneGames.DataTable.Core` to the consuming asmdef. Pure C# consumers do not need the Unity Runtime assembly.

```json
{
  "references": [
    "CycloneGames.DataTable.Core"
  ]
}
```

### Define an integer-keyed row

Implement `IDataRow` when the primary key is an `int`. Keep published row values immutable.

```csharp
using CycloneGames.DataTable;

public sealed class ItemRow : IDataRow
{
    public ItemRow(int id, string name, int maxStack)
    {
        Id = id;
        Name = name;
        MaxStack = maxStack;
    }

    public int Id { get; }
    public string Name { get; }
    public int MaxStack { get; }
}
```

### Build a table

```csharp
var items = new DataTable<ItemRow>(new[]
{
    new ItemRow(1001, "Health Potion", 20),
    new ItemRow(1002, "Mana Potion", 20),
});
```

The constructor copies the source array, preserves its row order, and builds a key-to-index dictionary. Construction rejects a null row, a null key, or a duplicate key.

### Query rows

```csharp
ItemRow healthPotion = items.Get(1001);

if (items.TryGet(1002, out ItemRow manaPotion))
{
    UseItem(manaPotion);
}

ItemRow missing = items.GetOrDefault(9999); // null for this class row

for (int i = 0; i < items.Count; i++)
{
    ItemRow row = items.All[i];
    RegisterItem(row);
}
```

Use `Get` when a missing key is a data-contract failure — it throws `KeyNotFoundException` when absent. Use `TryGet` for expected optional lookup. Use `GetOrDefault` only when `default(TRow)` has an unambiguous meaning for the row type.

## Core Concepts

### Custom key types and generated models

A row does not need to implement a DataTable interface. Pass a key selector when the generated model cannot be changed or when the key type is not `int`.

```csharp
using System;
using CycloneGames.DataTable;

public sealed class LocalizedTextRow
{
    public LocalizedTextRow(string key, string text)
    {
        Key = key;
        Text = text;
    }

    public string Key { get; }
    public string Text { get; }
}

var texts = new DataTable<string, LocalizedTextRow>(
    new[]
    {
        new LocalizedTextRow("ui.play", "Play"),
        new LocalizedTextRow("ui.quit", "Quit"),
    },
    static row => row.Key,
    StringComparer.Ordinal);
```

Select key semantics explicitly:

- use stable integer, enum, GUID, or string values that survive serialization and content rebuilds;
- use `StringComparer.Ordinal` for case-sensitive identifiers;
- use `StringComparer.OrdinalIgnoreCase` only when the content contract defines identifiers as case-insensitive;
- do not use culture-sensitive comparison, Unity object identity, or transient runtime handles as persistent content keys.

The key selector runs once per row during construction, not on every lookup.

### Row storage and ownership

`DataTable<TKey, TRow>` is structurally immutable: rows cannot be added, removed, or replaced after construction. `All` exposes a read-only view in source order, while the lookup dictionary stores integer row indices rather than a second copy of each row.

Structural immutability does not deep-clone row objects. A class row and every mutable object it references must remain unchanged for the table lifetime. A comparer supplied by the caller must also remain safe for concurrent reads.

Choose the construction API according to source ownership:

| API | Source handling | Recommended use |
| --- | --- | --- |
| `new DataTable(... array ...)` | Copies the array | Safe default when the caller retains the source. |
| `new DataTable(... list ...)` | Copies list elements into an owned array | Safe default for an existing `List<TRow>`. |
| `FromEnumerable` | Materializes once with a row-count guard | Streaming or computed cold-path input. |
| `FromOwnedArray` | Takes the array without copying | Decoder-produced array with no remaining writable aliases. |

```csharp
ItemRow[] decodedRows = DecodeAndValidateItems();

DataTable<ItemRow> items = DataTable<ItemRow>.FromOwnedArray(
    decodedRows,
    limits);

decodedRows = null; // ownership has moved to the table
```

After a successful `FromOwnedArray` call, no code may modify the transferred array. Ownership transfer applies to the array container; referenced class instances still follow the product's immutable-row contract.

### Catalogs and publication

`DataTableCatalog` groups related tables under exact contract types. Build the full catalog before making it visible to gameplay systems.

```csharp
DataTable<ItemRow> items = BuildItems();
DataTable<string, PriceRow> prices = new DataTable<string, PriceRow>(
    DecodePrices(),
    static row => row.ItemCode,
    StringComparer.Ordinal);

DataTableCatalog catalog = new DataTableCatalogBuilder(limits, capacity: 2)
    .Add<IDataTable<ItemRow>>(items)
    .Add<IDataTable<string, PriceRow>>(prices)
    .Build();

ValidateItemReferences(catalog);
```

Catalog lookup uses the exact type passed to `Add`. Retrieve the same contract type:

```csharp
IDataTable<ItemRow> itemTable = catalog.Get<IDataTable<ItemRow>>();

if (catalog.TryGet<IDataTable<string, PriceRow>>(out var priceTable))
{
    ShowPrice(priceTable.Get("potion.health"));
}
```

`DataTableCatalogBuilder` is one-shot. `Build()` transfers its internal map to the immutable catalog; subsequent builder operations fail. Duplicate contract types, null entries, incompatible instances, and a table count beyond `DataTableLoadLimits.MaxTableCount` fail before a catalog is created.

For process-wide publication, `DataTableRegistry.Publish(catalog)` swaps the whole catalog atomically. Capture `Current` once for a multi-table operation — a captured reference remains internally consistent even if another generation is published later. `Generation` can be recorded in diagnostics. `Reset()` removes the process-wide reference; it does not dispose tables or backing resources.

## Usage Guide

### Bounded payload loading

Create one limit profile for a content set. Values should come from actual generated content, reload peak-memory measurements, and the lowest supported hardware tier.

```csharp
var limits = new DataTableLoadLimits(
    maxTableCount: 128,
    maxBytesPerTable: 8 * 1024 * 1024,
    maxTotalBytes: 64L * 1024 * 1024,
    maxRowsPerTable: 250_000,
    maxTableNameLength: 96);
```

`DataTableLoadLimits.Default` is a broad fail-fast guardrail. A shipping product should normally use a tighter profile at each untrusted or memory-sensitive boundary.

Store payloads in a bounded cache:

```csharp
using var payloadCache = new DataTableBytesCache(
    limits,
    capacity: 16,
    dataExtension: ".bytes",
    clearBytesOnDispose: false);

payloadCache.Add("Items", itemPayload);       // copies ReadOnlyMemory<byte>
payloadCache.AddOwned("Prices", priceBytes); // transfers the byte[]
priceBytes = null;

payloadCache.Seal();

ReadOnlyMemory<byte> bytes = payloadCache.GetBytes("Items.bytes");
```

Table names are normalized, so both `Items` and `Items.bytes` address the same entry. Cache identity is case-insensitive to prevent two entries from collapsing to one native path on case-insensitive file systems. `Add` and `Set` copy the payload. `AddOwned` and `SetOwned` avoid the copy and require exclusive array ownership. `Seal()` prevents mutation. The cache has no automatic eviction policy; dispose the owning content scope when its readers have stopped.

### Manifest validation

Use a versioned manifest before decoding payloads:

```csharp
var manifest = new DataTableManifest(
    schemaVersion: 3,
    entries: new[]
    {
        new DataTableManifestEntry(
            tableName: "Items",
            location: "Config/Items.bytes",
            expectedByteLength: itemPayload.Length,
            sha256Hex: expectedItemsSha256,
            required: true),
        new DataTableManifestEntry(
            tableName: "Prices",
            location: "Config/Prices.bytes",
            expectedByteLength: pricePayloadLength,
            sha256Hex: expectedPricesSha256,
            required: true),
    },
    limits,
    requireKnownTables: true);

manifest.EnsureSchemaVersionSupported(minimumVersion: 3, maximumVersion: 3);
manifest.ValidateRequiredTables(payloadCache);
manifest.ValidateBytes("Items", payloadCache.GetBytes("Items"));
manifest.ValidateBytes("Prices", payloadCache.GetBytes("Prices"));
```

`ValidateRequiredTables` checks presence. `ValidateBytes` applies the configured byte limit and the entry's expected length and SHA-256 value. SHA-256 identifies content and detects corruption; content received from an untrusted source also needs an authenticated signature and a product-owned trust policy.

An entry without `Sha256Hex` intentionally skips hash validation. `DataTableHashUtility.Sha256Matches` requires a non-empty expected hash and returns `false` when the expected value is absent.

### Resource lifetime

Use `DataTableSetScope` when generated tables or row views depend on a disposable payload owner:

```csharp
var scope = new DataTableSetScope(
    root: generatedTables,
    catalog: catalog,
    resourceOwner: payloadCache);

IDataTable<ItemRow> items = scope.Get<IDataTable<ItemRow>>();

// After every reader has stopped:
scope.Dispose();
```

The scope disposes only the supplied `resourceOwner`. It clears references to its root and catalog but does not infer ownership of arbitrary table instances. Do not dispose a backing owner while published rows or generated views can still access its memory.

### Luban integration

Reference `CycloneGames.DataTable.Unity.Runtime.Integrations.Luban` from the composition assembly. The `com.code-philosophy.luban` package must satisfy the integration asmdef's declared version range.

Prepare a bounded `IDataTableBytesProvider`, then create the generated Luban root:

```csharp
using CycloneGames.DataTable.Unity.Integrations.Luban;

cfg.Tables generatedTables = LubanDataTableSetFactory.Create(
    payloadCache,
    getBytes => new cfg.Tables(getBytes),
    limits);
```

The callback is valid only during the synchronous factory call and on the calling thread. Each requested payload is copied into a private `Luban.ByteBuf` array, allowing the generated parser to retain its buffer without borrowing writable cache memory.

For a generated parser that accepts one `ByteBuf` directly:

```csharp
Luban.ByteBuf itemBuffer = LubanDataTableSetFactory.CreateOwnedByteBuf(
    payloadCache,
    "Items",
    limits);
```

Validate generated row counts, ranges, stable IDs, and cross-table references before catalog publication. Full pipeline setup, Windows/macOS/Linux generation commands, output ownership, and recovery procedures are documented in the [Luban guide](../../../../../DataTable/Luban/README.md).

### MessagePack integration

Reference `CycloneGames.DataTable.Unity.Runtime.Integrations.MessagePack`. Keep the Unity client package, `MessagePack.dll`, annotations, and analyzer on matching versions, and use source-generated row formatters for IL2CPP/AOT.

Define a MessagePack row contract:

```csharp
using CycloneGames.DataTable;
using MessagePack;

[MessagePackObject]
public sealed class PackedItemRow : IDataRow
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string Name { get; set; }
}
```

Deserialize a top-level row array with explicit resolver and security settings:

```csharp
using System.Threading;
using CycloneGames.DataTable.Unity.Integrations.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

MessagePackSerializerOptions options =
    MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);

MessagePackSecurity security = MessagePackSecurity.UntrustedData
    .WithMaximumObjectGraphDepth(64)
    .WithMaximumDecompressedSize(limits.MaxBytesPerTable);

DataTable<PackedItemRow> items = MessagePackConfigProvider.Build<PackedItemRow>(
    itemPayload,
    options,
    security,
    limits,
    CancellationToken.None);
```

The adapter requires a bounded untrusted-data policy, validates payload size and the top-level array count before row materialization, observes cancellation, and rejects corrupt, truncated, or trailing bytes. It uses only the options supplied to `Build`; configure resolver composition explicitly at the call site.

For custom keys, call `Build<TKey, TRow>` with a key selector and comparer. When a decoder already owns a validated `TRow[]`, call `BuildRows` to transfer that array directly into a table.

### Unity Editor and Luban generation

Create `DataTableLubanSettings` from `Assets > Create > CycloneGames > DataTable > Luban Settings`, or run `Tools > CycloneGames > DataTable > Run Luban Build`. If no settings asset exists, the runner creates one under `Assets/Editor/DataTable/DataTableLubanSettings.asset`. Keep one authoritative settings asset per settings type.

| Field | Meaning |
| --- | --- |
| `LubanProjectDir` | Luban directory relative to the Unity project root. The default points to `../DataTable/Luban`. |
| `LubanScriptName` | Script name without extension. The runner appends `.bat` on Windows and `.sh` on macOS/Linux. |
| `LubanScriptArguments` | Optional arguments appended to the generation command. |
| `LubanTimeoutSeconds` | Maximum external-process duration; invalid serialized values fall back to 300 seconds. |
| `RefreshAssetsAfterLubanBuild` | Calls `AssetDatabase.Refresh()` only after a successful run. |

The Inspector shows resolved paths and validation status and provides refresh, reveal, validate, and build actions. Script execution validates the project root, working directory, script path, arguments, and timeout before starting the process. Standard output and standard error are captured into a bounded result.

The runner permits one in-Editor writer. The generation wrapper also uses the directory writer lock so Editor, terminal, and CI invocations cannot publish concurrently. If timeout or cancellation cannot confirm that the child process exited, stop all generator processes, inspect the directory lock and generated output, complete recovery, and restart the Editor before starting another run. Projects with derived generation profiles can subclass `DataTableLubanSettings` and override its virtual methods, including `CreateLubanRunRequest()`, without modifying this package.

## Advanced Topics

### AOT-safe generated table-set registration

Use explicit descriptors when a generator produces one root object containing several table properties. Replace the example generated types and properties with the names emitted by the project's generator.

```csharp
using CycloneGames.DataTable;

var descriptors = new[]
{
    new DataTableGeneratedTableCollector.TableDescriptor<MyGeneratedTables>(
        typeof(MyGeneratedItemTable),
        static tables => tables.Items),
    new DataTableGeneratedTableCollector.TableDescriptor<MyGeneratedTables>(
        typeof(MyGeneratedPriceTable),
        static tables => tables.Prices),
};

DataTableCatalog generatedCatalog =
    DataTableGeneratedTableCollector.CreateCatalog(generatedTables, descriptors);
```

The descriptor array defines the exact catalog contract type and property accessor. This path performs no runtime assembly scan or reflection-based discovery, making the registration graph explicit for IL2CPP and managed stripping.

### Production loading and hot reload

A complete reload sequence is:

1. Allocate a candidate payload owner with product-specific `DataTableLoadLimits`.
2. Load required payloads and reject missing, empty, oversized, or unknown entries.
3. Validate manifest schema, byte lengths, hashes, and content trust.
4. Decode every table into a candidate generation.
5. Validate row fields, stable IDs, ranges, uniqueness, and cross-table references.
6. Build one `DataTableCatalog`.
7. Inject the catalog into a new composition scope or call `DataTableRegistry.Publish`.
8. Wait until readers of the previous generation have finished.
9. Dispose the previous generation's backing resources.

Any failure before publication should discard the candidate and leave the active generation unchanged. Resource retirement must be coordinated by the application because the registry does not track reader leases.

### Explicit injection vs. process-wide publication

Pass the catalog through constructors when the consumer has a clear owner and lifetime:

```csharp
public sealed class ItemService
{
    private readonly IDataTable<ItemRow> _items;

    public ItemService(DataTableCatalog catalog)
    {
        _items = catalog.Get<IDataTable<ItemRow>>();
    }

    public ItemRow GetItem(int id) => _items.Get(id);
}
```

This works with direct construction and with any DI composition root; Core has no container dependency. Use `DataTableRegistry` only when the application intentionally provides one process-wide catalog generation.

## Common Scenarios

### Item and price lookup at runtime

A gameplay system needs to look up item definitions and prices by different keys. Build both tables, compose them into a catalog, and inject the catalog into the service:

```csharp
public sealed class ShopService
{
    private readonly IDataTable<ItemRow> _items;
    private readonly IDataTable<string, PriceRow> _prices;

    public ShopService(DataTableCatalog catalog)
    {
        _items = catalog.Get<IDataTable<ItemRow>>();
        _prices = catalog.Get<IDataTable<string, PriceRow>>();
    }

    public int GetSellPrice(int itemId)
    {
        ItemRow item = _items.Get(itemId);
        return _prices.Get(item.Name).SoftCurrency;
    }
}
```

Both lookups are expected `O(1)`. The catalog captures a single internally consistent snapshot — even if another generation is published later, this service continues to read the snapshot it was constructed with.

### Localization with string keys

A localization system uses string keys (`"ui.play"`, `"ui.quit"`) instead of integer IDs:

```csharp
DataTable<string, LocalizedTextRow> texts = new DataTable<string, LocalizedTextRow>(
    LoadLocalizedRows(),
    static row => row.Key,
    StringComparer.Ordinal);

public string Localize(string key)
{
    return texts.TryGet(key, out LocalizedTextRow row) ? row.Text : key;
}
```

`StringComparer.Ordinal` gives case-sensitive identifier matching. The key selector runs once per row during construction; lookup does not invoke it again.

### Atomic catalog hot-swap

A live game downloads a new content generation and needs to swap the catalog without restarting. Build the candidate generation on a loading owner, validate it, then publish atomically:

```csharp
public async Task ReloadContentAsync(byte[] manifestBytes, CancellationToken ct)
{
    DataTableBytesCache candidateCache = LoadPayloads(manifestBytes, ct);
    DataTableManifest manifest = ParseManifest(manifestBytes, candidateCache);
    manifest.ValidateRequiredTables(candidateCache);

    DataTableCatalog candidateCatalog = await BuildCatalogAsync(candidateCache, manifest, ct);
    ValidateCrossTableReferences(candidateCatalog);

    DataTableRegistry.Publish(candidateCatalog);

    // After readers of the previous generation finish:
    _previousCache?.Dispose();
    _previousCache = candidateCache;
}
```

`DataTableRegistry.Publish` swaps the whole catalog atomically; readers that captured `Current` before the swap continue to see the previous snapshot. The application coordinates retirement of the previous backing resources.

### Generated table set without reflection

A code generator emits a `cfg.Tables` root containing typed table properties. Register them with explicit descriptors so the registration graph is preserved under IL2CPP and managed stripping:

```csharp
var descriptors = new[]
{
    new DataTableGeneratedTableCollector.TableDescriptor<cfg.Tables>(
        typeof(cfg.TbItem),
        static tables => tables.TbItem),
    new DataTableGeneratedTableCollector.TableDescriptor<cfg.Tables>(
        typeof(cfg.TbPrice),
        static tables => tables.TbPrice),
};

DataTableCatalog catalog =
    DataTableGeneratedTableCollector.CreateCatalog(generatedTables, descriptors);
```

No runtime assembly scan or reflection-based discovery is performed.

## Performance and Memory

| Operation | Runtime characteristics |
| --- | --- |
| Successful `Get`, `GetOrDefault`, `TryGet` after construction | Expected `O(1)` dictionary lookup with no intentional managed allocation. |
| Missing-key `Get` | Creates and throws `KeyNotFoundException`; use outside normal hot-path control flow. |
| `All[index]` | `O(1)` access through a read-only view. |
| Table construction | Cold path; array copy unless ownership is transferred, plus row view and key-index allocation. |
| Catalog typed lookup | Expected `O(1)` type-keyed dictionary lookup. |
| Registry read | Volatile snapshot read without a reader lock. |
| Registry publication | Serialized writer path; allocates a state object and diagnostic message. |
| Byte cache lookup | Loading path with name normalization and dictionary lookup. |
| Hashing, manifest validation, decoding | Cold path; backend-specific allocation and processing cost. |

Estimate reload peak memory from all simultaneously live components:

```text
source payloads
+ cache-owned payloads
+ adapter copies, decompression, and decoder scratch memory
+ row arrays and referenced row objects
+ key-to-index dictionaries
+ old and candidate generations during publication
```

Use `FromOwnedArray` and `AddOwned` only when ownership is provable; a saved copy is not worth a writable alias or use-after-dispose risk. Keep caches scoped to a content generation instead of retaining every payload process-wide. For large value-type rows, include row-copy cost in profiling. Add specialized access only when representative benchmarks identify it as a material bottleneck. Performance budgets should record row shape, key type, hit/miss distribution, table size, backend, Unity scripting backend, target hardware, warm-up method, and GC measurement window.

### Threading and lifetime rules

- Construct tables, catalogs, manifests, and caches on one loading owner outside gameplay hot paths.
- Published table and catalog reads may run concurrently when rows, referenced objects, and comparers are immutable.
- `DataTableCatalogBuilder` and unsealed `DataTableBytesCache` are single-owner mutable objects.
- `Seal()` prevents cache mutation; it is not a memory-publication barrier by itself.
- Do not race `Dispose()` with cache readers or table views that depend on the disposed owner.
- `DataTableRegistry` serializes writers and exposes a volatile immutable snapshot to readers.
- Luban payload requests are synchronous and remain on the factory owner thread.
- Unity objects and AssetManagement loaders follow Unity main-thread affinity.

Thread safety belongs to the published immutable snapshot and its ownership protocol. It does not make mutable row objects or third-party parser state safe for concurrent access.

### Platform guidance

- **Windows, Linux, macOS:** use the platform generation wrapper documented in the Luban guide. Keep table identities portable across case-sensitive and case-insensitive file systems.
- **IL2CPP and managed stripping:** use source-generated serializers and explicit `TableDescriptor<TTableSet>` registration. Preserve only generated backend types that require it.
- **iOS and Android:** measure cold-load duration and peak memory with source buffers, adapter copies, decompression, decoded rows, index dictionaries, and generation overlap present.
- **WebGL:** do not rely on background threads. Keep synchronous decode work and peak managed memory within a measured frame/loading-screen budget.
- **Dedicated Server:** reference Core and pure C# adapters from the server composition; exclude Unity asset-loading and Editor assemblies.
- **Console platforms:** validate native dependency import, file-name rules, memory limits, AOT behavior, and platform certification requirements with the platform holder's toolchain.

Run clean Player builds and representative content tests for every supported scripting backend and platform profile. Editor results are useful for development but are not a substitute for target-runtime profiling.

### Security, persistence, and logging

Treat files, remote configuration, patches, mods, and command-line-selected content as untrusted input. Bound payload bytes, total bytes, row counts, table counts, decompression, nested collections, recursion depth, strings, processing time, and diagnostics. Validate stable IDs, value ranges, references, schema versions, permissions, and signatures before publication.

Core performs no file writes and does not use `EditorPrefs` or `PlayerPrefs`.

| Data | Owner and lifecycle |
| --- | --- |
| Workbook, schema, and generator configuration | Version-controlled content source. |
| Generated C# and binary payloads | Rebuildable content-pipeline output; commit or publish according to the product pipeline. |
| Manifest and schema version | Version and publish together with matching payloads. |
| `DataTableLubanSettings.asset` | Visible Unity project configuration; keep one authoritative asset. |
| Runtime byte cache | Owned by a runtime content scope and disposed after readers retire. |

`DataTableLogger` defaults to `System.Console` in Core. A composition root may assign `LogInfo`, `LogWarning`, and `LogError`; `CycloneGames.DataTable.Unity.Runtime` installs Unity logging. Reset or rebind custom delegates during subsystem registration when domain reload is disabled. Logs should include table identity, generation, stage, limits, and failure category, but not secrets or complete hostile payloads.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Constructor reports a duplicate key | Two rows share the same key | Correct the content source or key selector; every key must be unique within one table |
| `Get<TTable>()` cannot find a catalog entry | Wrong contract type | Retrieve the exact contract type used by `DataTableCatalogBuilder.Add<TTable>` |
| `DataTableRegistry.Get<TTable>()` returns `null` | Catalog not published, or missing contract | Ensure a complete catalog containing that contract has been published; inspect `IsInitialized`/`Generation` |
| Cache reports an existing table for a differently cased name | Case-insensitive identity | Use one canonical portable table identity; cache and manifest identities are case-insensitive |
| Cache mutation throws after loading | Cache is sealed | Create a new candidate cache for a new content generation |
| Manifest rejects a payload | Schema, length, or hash mismatch | Check schema version, normalized table name, expected byte length, SHA-256, and release identity |
| Luban callback throws for thread or lifetime | Callback stored or dispatched off-thread | Invoke every generated payload request synchronously inside `LubanDataTableSetFactory.Create`; do not store or dispatch the callback |
| MessagePack rejects the security policy | Insufficient security bounds | Start with `MessagePackSecurity.UntrustedData`, keep collision-resistant hashing enabled, bound decompressed size to `MaxBytesPerTable` |
| MessagePack cannot deserialize a row | Wrong payload shape or missing formatter | Confirm the payload is a top-level `TRow[]`, the row formatter was generated, and the explicit resolver contains it |
| Luban Inspector reports an invalid path | Misconfigured directory or script | Validate the Unity-project-relative directory, platform script extension, script existence, and unique settings asset |
| Luban run remains blocked after timeout | Child process did not exit cleanly | Stop generator processes, inspect `.cyclonegames-datatable-writer.lock` and outputs, recover the directory, then restart Unity |
| Reload memory is higher than the cache total | Overlapping generations and decoder scratch | Profile payload sources, copies, decompression, decoder objects, row objects, dictionaries, and old/new generation overlap |

## Validation

### Core and integration tests

Run these EditMode test assemblies from Unity Test Runner or the project's batchmode test entry point:

- `CycloneGames.DataTable.Tests.Editor`
- `CycloneGames.DataTable.Tests.Editor.Integrations.Luban` when Luban is enabled
- `CycloneGames.DataTable.Tests.Editor.Integrations.MessagePack` when MessagePack is enabled
- `CycloneGames.DataTable.Tests.Performance`

Add product fixtures for duplicate and missing keys, malformed payloads, excessive counts, schema mismatch, cross-table reference failure, cancellation, reload rollback, and backing-owner retirement.

### Generator validation

Validate the directory before generating content, run the platform wrapper, inspect its summary, and review generated changes before committing. The wrapper commands and recovery procedure are in the [Luban guide](../../../../../DataTable/Luban/README.md). The package-local CodeGen tool provides its own build and self-test instructions in [Tools~/CodeGen/README.md](./Tools~/CodeGen/README.md).

### Player validation

For each supported build profile:

1. build from a clean checkout;
2. verify asmdef conditions and serializer registration;
3. load representative minimum, typical, and maximum content sets;
4. record cold-load time, peak memory, retained memory, and hot lookup allocations;
5. exercise corrupt, missing, cancelled, and rollback paths;
6. repeat with managed stripping and the shipping scripting backend.

## API Navigation

| Type | Primary use |
| --- | --- |
| `IDataRow<TKey>` / `IDataRow` | Optional stable primary-key contract for generated or handwritten rows. |
| `IDataTableRows<TRow>` | Minimal source-ordered read-only row view. |
| `IDataTable<TKey, TRow>` / `IDataTable<TRow>` | Read-only keyed table contract. |
| `DataTable<TKey, TRow>` / `DataTable<TRow>` | Immutable row storage and key-to-index lookup. |
| `DataTableLoadLimits` | Explicit table-count, byte, row, and name budgets. |
| `DataTableCatalog` | Immutable type-indexed table snapshot. |
| `DataTableCatalogBuilder` | One-shot candidate catalog construction. |
| `DataTableRegistry` | Optional process-wide atomic publication. |
| `DataTableGeneratedTableCollector` | AOT-safe generated table-set collection. |
| `IDataTableBytesProvider` | Borrowed read-only payload access contract. |
| `DataTableBytesCache` | Bounded owner of materialized payload arrays. |
| `DataTableManifest` / `DataTableManifestEntry` | Schema, presence, byte-length, location, and SHA-256 metadata. |
| `DataTableHashUtility` | SHA-256 computation, normalization, and strict matching. |
| `DataTableNameUtility` | Portable table-name, extension, and path normalization. |
| `DataTableLocationResolver` | Portable relative location construction. |
| `DataTableSetScope` | Generated root, catalog, and optional backing-owner lifetime. |
| `DataTableLogger` | Replaceable logging boundary. |
| `LubanDataTableSetFactory` | Bounded, privately owned Luban buffer creation. |
| `MessagePackConfigProvider` | Bounded MessagePack row-array decoding and table construction. |
| `DataTableLubanSettings` | Project-visible Unity Editor generation settings. |
| `DataTableLubanRunner` | Validated single-writer external generation process. |

## Related Documentation

- [Luban directory and generation tutorial](../../../../../DataTable/Luban/README.md)
- [DataTable CodeGen tutorial](./Tools~/CodeGen/README.md)
- [GameplayTags DataTable integration](../CycloneGames.GameplayTags.DataTable/README.md)
