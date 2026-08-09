# CycloneGames.GameplayTags.DataTable

English | [简体中文](./README.SCH.md)

CycloneGames.GameplayTags.DataTable bridges typed DataTable rows into the GameplayTags registration pipeline. Definition rows supply the authoritative tag catalog; reference rows report tag names consumed by abilities, effects, and items. Pure C# — the product composition root owns file I/O, workbook generation, and byte decoding.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Performance and Memory](#performance-and-memory)
- [Troubleshooting](#troubleshooting)

## Overview

Definition rows supply the authoritative set of tag names, descriptions, flags, and enabled state. Reference rows report which tag names are consumed by gameplay configuration. Both source adapters consume `IDataTableRows<TRow>` or `IReadOnlyList<TRow>` without depending on a specific serializer or generated table-set type.

Use this module when gameplay configuration lives in generated tables (including Luban-generated Excel data) and you want the same tag vocabulary to drive both authoring and runtime.

### Key Features

- **Definition sources** map table rows to authoritative tag registrations with name, description, flags, and enabled state.
- **Reference sources** enumerate tag names from one or more fields in gameplay rows.
- **Luban/Excel integration** with explicit key selectors for generated row types.
- **DataTableCatalog** composition and instance-owned `DataTableStore` generation publication.
- **Coordinated reload** with rollback on failure.

## Architecture

| Assembly | Role |
| --- | --- |
| `CycloneGames.GameplayTags.DataTable` | Row-to-tag adapters. `autoReferenced: false`. |
| `CycloneGames.DataTable.Core` (dependency) | Typed immutable tables and catalog publication |
| `CycloneGames.GameplayTags.Core` (dependency) | Tag registration, validation, snapshots, and queries |
| `CycloneGames.Logging.Core` (test assembly dependency) | Structured failure capture for package tests; the production integration assembly does not reference it |

Add explicit asmdef references:

```json
{
  "references": [
    "CycloneGames.DataTable.Core",
    "CycloneGames.GameplayTags.Core",
    "CycloneGames.GameplayTags.DataTable"
  ]
}
```

```csharp
using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;
```

## Quick Start

### Register an authoritative tag table

```csharp
// 1. Define the row model
public sealed class GameplayTagRow : IDataRow
{
    public int Id { get; }
    public string Name { get; }
    public string Description { get; }
    public GameplayTagFlags Flags { get; }
    public bool Enabled { get; }

    public GameplayTagRow(int id, string name, string description,
        GameplayTagFlags flags, bool enabled)
    {
        Id = id; Name = name; Description = description;
        Flags = flags; Enabled = enabled;
    }
}

// 2. Build the table
var table = new DataTable<GameplayTagRow>(new[]
{
    new GameplayTagRow(1, "Ability.Fireball", "Casts a fireball.",
        GameplayTagFlags.None, true),
    new GameplayTagRow(2, "Effect.Burning", "Deals damage over time.",
        GameplayTagFlags.None, true),
});

// 3. Create and register the source
var source = new GameplayTagDataTableSource<GameplayTagRow>(
    "00.GameplayTags.Definitions",
    table,
    static row => row.Name,
    static row => row.Description,
    static row => row.Flags,
    static row => row.Enabled);

GameplayTagRuntimePlatform.RegisterProjectTagSource(source);

// 4. Initialize and query
GameplayTagManager.InitializeIfNeeded();

GameplayTag fireball = GameplayTagManager.RequestTag("Ability.Fireball");
```

Register every startup source before the first call to `InitializeIfNeeded()`.

## Core Concepts

| Type | Responsibility |
| --- | --- |
| `DataTable<TKey, TRow>` | Structurally immutable row sequence with key index |
| `GameplayTagDataTableSource<TRow>` | Maps one definition row to one authoritative tag registration |
| `GameplayTagDataTableReferenceSource<TRow>` | Enumerates tag names referenced by fields in each gameplay row |
| `GameplayTagRuntimePlatform` | Registers named project sources with GameplayTags |
| `DataTableCatalog` | Groups typed tables into one immutable catalog |
| `DataTableStore` / `DataTableReader` | Optionally publish versioned Catalog generations and pin a consumer to one snapshot |

Definition and reference rows serve different purposes. A definition row owns tag metadata. A reference row reports that a tag name is used. Projects that require a closed vocabulary should validate every reference against the definition table before registration.

## Usage Guide

### Luban/Excel Definition Table

| Field | Suggested type | Required | Meaning |
| --- | --- | ---: | --- |
| `id` | `int` | Yes | Stable DataTable key (not the tag identity) |
| `name` | `string` | Yes | Hierarchical tag identity |
| `description` | `string` | No | Developer-facing explanation |
| `flags` | `int` or enum | Yes | `0` = `None`, `1` = `HideInEditor` |
| `enabled` | `bool` | Yes | Controls registration participation |

Parent tags do not need separate rows. Registering `Ability.Elemental.Fire` creates missing `Ability` and `Ability.Elemental` parents, which consume registry capacity.

Map generated rows with an explicit key selector:

```csharp
Generated.GameplayTagRow[] generatedRows = LoadGeneratedGameplayTagRows();

var table = new DataTable<int, Generated.GameplayTagRow>(
    generatedRows,
    static row => row.Id,
    limits);

var source = new GameplayTagDataTableSource<Generated.GameplayTagRow>(
    "00.GameplayTags.Definitions",
    table,
    static row => row.Name,
    static row => row.Description,
    static row => (GameplayTagFlags)row.Flags,
    static row => row.Enabled);
```

### Reference Sources

```csharp
var abilityReferences = new GameplayTagDataTableReferenceSource<AbilityRow>(
    "10.GameplayTags.AbilityReferences",
    abilityTable,
    static row => row.AbilityTags,
    static row => row.RequiredTags,
    static row => row.BlockedTags);

GameplayTagRuntimePlatform.RegisterProjectTagSource(abilityReferences);
```

Null rows and rows rejected by `isEnabled` are skipped. Null accessors or null collections are skipped. Every non-null name is submitted; duplicate names resolve to one definition but still consume registration attempts.

### Validating References Against Definitions

```csharp
var definedNames = new HashSet<string>(StringComparer.Ordinal);
for (int i = 0; i < tagTable.All.Count; i++)
{
    if (tagTable.All[i].Enabled)
        definedNames.Add(tagTable.All[i].Name);
}

for (int i = 0; i < abilityTable.All.Count; i++)
{
    string[] names = abilityTable.All[i].AbilityTags;
    if (names == null) continue;
    for (int j = 0; j < names.Length; j++)
    {
        if (!definedNames.Contains(names[j]))
            throw new InvalidOperationException($"Undefined gameplay tag: {names[j]}");
    }
}
```

Perform this validation during candidate generation, not during per-frame gameplay.

### Catalog Publication

```csharp
var catalogBuilder = new DataTableCatalogBuilder(limits, capacity: 2);
catalogBuilder.Add<IDataTableRows<GameplayTagRow>>(tagTable);
catalogBuilder.Add<IDataTableRows<AbilityRow>>(abilityTable);
DataTableCatalog catalog = catalogBuilder.Build();

GameplayTagManager.InitializeIfNeeded();
using var store = new DataTableStore(revisionSequenceFloor: 0);
using DataTableReader reader = store.RegisterReader();
using var candidate = new DataTableCandidate(
    catalog,
    new DataTableRevision(sequence: 1, id: contentSha256));

DataTablePublishResult result = store.TryPublish(candidate, expectedGeneration: 0);
if (!result.IsCommitted)
    throw new InvalidOperationException($"Catalog publication rejected: {result.Status}");

reader.Refresh(); // only at a point where no operation uses the old snapshot
```

### Source Ordering

Sources are processed in ordinal name order. Use numeric prefixes:

| Prefix | Role | Example |
| --- | --- | --- |
| `00` | Authoritative definitions | `00.GameplayTags.Definitions` |
| `10` | Ability references | `10.GameplayTags.AbilityReferences` |
| `20` | Item/effect references | `20.GameplayTags.ItemReferences` |

When a name is registered more than once, the first definition's flags are retained; a later non-empty description fills an empty description.

## Advanced Topics

### Coordinated Reload

Build and validate a complete candidate before modifying active sources:

1. Load bounded payloads into a candidate-owned resource scope.
2. Decode rows and validate keys, names, flags, and references.
3. Build all candidate tables and the candidate catalog.
4. Create definition and reference sources over the candidate tables.
5. Register each candidate source with the stable name of the source it replaces.
6. Call `GameplayTagManager.ReloadTags()`.
7. Call `TryPublish` with the DataTable generation captured before candidate work began.
8. If publication is rejected, restore the previous tag sources and reload tags again; the DataTable candidate remains caller-owned.
9. If publication commits, refresh each DataTable reader at a coordinated safe point.
10. Resume dependent consumers. The Store releases the retired backing owner after its final reader leaves.

On tag reload failure, roll back:

```csharp
long expectedDataTableGeneration = _dataTableStore.Metadata.Generation;
IGameplayTagSource previousDefinitionSource = _activeDefinitionSource;
IGameplayTagSource nextDefinitionSource = BuildDefinitionSource(candidateCatalog);
using var tableCandidate = new DataTableCandidate(
    candidateCatalog,
    trustedRevision,
    candidateResourceOwner);

GameplayTagRuntimePlatform.RegisterProjectTagSource(nextDefinitionSource);
try
{
    GameplayTagManager.ReloadTags();
}
catch
{
    if (previousDefinitionSource != null)
        GameplayTagRuntimePlatform.RegisterProjectTagSource(previousDefinitionSource);
    else
        GameplayTagRuntimePlatform.UnregisterProjectTagSource(nextDefinitionSource.Name);
    throw;
}

DataTablePublishResult publish = _dataTableStore.TryPublish(
    tableCandidate,
    expectedDataTableGeneration);
if (!publish.IsCommitted)
{
    if (previousDefinitionSource != null)
        GameplayTagRuntimePlatform.RegisterProjectTagSource(previousDefinitionSource);
    else
        GameplayTagRuntimePlatform.UnregisterProjectTagSource(nextDefinitionSource.Name);

    GameplayTagManager.ReloadTags();
    HandleRejectedTableCandidate(publish);
    return;
}

RequestDataTableReaderRefreshAtSafePoint();
_activeDefinitionSource = nextDefinitionSource;
```

`GameplayTagManager.ReloadTags()` and `DataTableStore.TryPublish()` remain two separate publication seams; neither can make the other atomic. Pause dependent consumers around both transitions when they must never observe tags and tables from different generations. Treat `Superseded` and `NonMonotonicRevision` as expected rejection statuses and restore the previous tag source before resuming. Process-fatal exceptions such as `OutOfMemoryException` are fail-stop conditions, not a normal rollback branch.

### Ownership and Lifetime

Adapters borrow their rows:

- `DataTable` copies the top-level input array but does not deep-clone row objects or nested collections.
- `DataTableCatalog` does not own or dispose its table instances.
- A caller-owned `DataTableCandidate` owns its optional backing resource until publication commits; rejected publication does not transfer ownership.
- A committed candidate's owner belongs to `DataTableStore`. Each `DataTableReader` pins one generation until it refreshes or disposes.

Keep rows, nested tag collections, and any backing resource stable during registration and reload. Do not retain a borrowed `DataTableSnapshot` across `DataTableReader.Refresh()`. Thread-affine backing resources require an owner-thread disposal adapter because the final reader transition may trigger retirement on its calling thread.

## Performance and Memory

Adapter registration is a cold-path linear scan over rows and referenced names. Do not call source registration from an update loop.

For predictable registration cost:

- Use static, non-capturing accessors.
- Expose arrays or stable indexed lists from generated rows.
- Validate and deduplicate large collections before registration.
- Avoid reflection, LINQ, and iterator state machines in accessors.

Runtime tag queries read the immutable GameplayTags snapshot and do not call these adapters. Use one composition owner for load, validation, source replacement, reload, catalog publication, and retirement.

The runtime assembly has `noEngineReferences: true`. This integration writes no workbook, generated source, payload, cache, asset, or preferences. The product owns all generated artifacts.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| A tag cannot be requested | Source not registered before initialization | Verify source is registered before `InitializeIfNeeded()`; check `isEnabled` returns `true` |
| Authoritative flags are missing | Definition source sorts after reference source | Ensure `00` prefix comes before `10`/`20`; validate workbook `flags` range |
| Reload keeps a removed tag during play | Runtime-index preservation policy | Removed authoring tags stay valid until next runtime reset |
| Generated row doesn't implement `IDataRow` | Luban-generated rows use arbitrary base types | Use `DataTable<TKey, TRow>` with explicit key selector |
| Registration allocates more than expected | Iterator accessors, LINQ, captured closures | Measure registration separately from runtime queries; use static accessors |
| Reload failed but source registration changed | Rollback incomplete | Re-register the previous source object, dispose the failed candidate, keep the catalog unchanged |

## Validation

```text
CycloneGames.GameplayTags.DataTable.Tests.Editor (EditMode)
CycloneGames.GameplayTags.Tests.Editor            (EditMode)
```

Cover enabled, disabled, duplicate, invalid, null collection, accessor exception, and budget-overflow cases. Verify with production-scale rows and in each supported Player configuration including IL2CPP/AOT.
