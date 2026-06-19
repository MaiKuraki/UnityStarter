# CycloneGames.GameplayTags.DataTable

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.GameplayTags.DataTable` is the optional integration package that bridges `CycloneGames.GameplayTags` with `CycloneGames.DataTable` and Luban-generated configuration rows. It is intended for projects where gameplay tag catalogs, ability definitions, effect definitions, or tag references are generated from tabular data instead of authored only as Unity `ScriptableObject` assets.

This guide explains how to install the integration package, register generated tag data during startup, convert generated tag names into runtime containers, and validate the setup. The base `CycloneGames.GameplayTags` package has no dependency on DataTable, so projects that do not use DataTable can import GameplayTags without missing assembly references or ProjectSettings scripting define symbols.

## Package Layout

```text
CycloneGames.GameplayTags.DataTable/
  Runtime/
    CycloneGames.GameplayTags.DataTable.asmdef
    GameplayTagDataTableSource.cs
    GameplayTagDataTableReferenceSource.cs
  Tests/Editor/
    CycloneGames.GameplayTags.DataTable.Tests.Editor.asmdef
    GameplayTagsDataTableIntegrationTests.cs
  README.md
  README.SCH.md
  package.json
```

## Assembly Boundary

| Assembly | Responsibility | Unity dependency | Auto-referenced |
| --- | --- | --- | --- |
| `CycloneGames.GameplayTags.DataTable` | Pure C# bridge from DataTable rows into GameplayTags registration sources | No | No |
| `CycloneGames.GameplayTags.DataTable.Tests.Editor` | EditMode coverage for tag catalog rows and generated ability-row references | Editor test runner | No |

The runtime assembly references:

- `CycloneGames.GameplayTags.Core`
- `CycloneGames.DataTable.Core`

Consumer assemblies should explicitly reference `CycloneGames.GameplayTags.DataTable` when they build startup adapters or generated-row composition code. Keeping the assembly non-auto-referenced avoids silently exposing DataTable APIs to projects that only want the base tag runtime.

## Installation Models

For UPM distribution, install:

```text
com.cyclone-games.gameplay-tags.data-table
```

Its `package.json` declares dependencies on `com.cyclone-games.gameplay-tags` and `com.cyclone-games.data-table`.

For Assets-direct distribution, include this folder only when the project also includes:

```text
UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags/
UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/
```

Do not enable this package through ProjectSettings scripting define symbols. Package presence is the composition boundary.

## Core Types

### `GameplayTagDataTableSource<TRow>`

Use this source when one generated row represents one gameplay tag definition.

```csharp
DataTable<TagRow> tagTable = DataTableRegistry.Get<DataTable<TagRow>>();

GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableSource<TagRow>(
    "Design.GameplayTags",
    tagTable,
    static row => row.Name,
    static row => row.Comment,
    static row => row.Flags,
    static row => row.Enabled));

GameplayTagManager.InitializeIfNeeded();
```

Recommended columns:

| Column | Purpose |
| --- | --- |
| `Id` | DataTable primary key only. It is not the GameplayTags stable ID. |
| `Name` | Full tag name, for example `Ability.Fireball`. |
| `Comment` | Authoring description surfaced by validation and editor tools. |
| `Flags` | Optional `GameplayTagFlags` value. |
| `Enabled` | Optional staged-content or deprecation switch. |
| `Owner` | Optional owner/team metadata for project validation tools. |
| `Deprecated` / `RedirectTo` | Optional migration metadata handled by project-specific validation. |

### `GameplayTagDataTableReferenceSource<TRow>`

Use this source when ability, effect, item, quest, or other generated rows reference tag names but are not themselves the authoritative tag catalog.

```csharp
DataTable<AbilityRow> abilityTable = DataTableRegistry.Get<DataTable<AbilityRow>>();

GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityRow>(
    "Design.Abilities",
    abilityTable,
    static row => row.AbilityTags,
    static row => row.ActivationRequiredTags,
    static row => row.ActivationBlockedTags,
    static row => row.ActivationOwnedTags));
```

This is useful for `CycloneGames.GameplayAbilities` projects where generated ability/effect rows create runtime definitions directly instead of creating intermediate `ScriptableObject` assets.

## Startup Order

Use a deterministic startup sequence:

1. Load Luban bytes and register generated tables into `DataTableRegistry`.
2. Register `GameplayTagDataTableSource<TRow>` for the authoritative tag catalog.
3. Optionally register `GameplayTagDataTableReferenceSource<TRow>` for generated ability/effect/item rows.
4. Call `GameplayTagManager.InitializeIfNeeded()`.
5. Build GameplayAbilities, GameplayEffects, or gameplay definitions from generated rows.
6. Convert tag-name fields into `GameplayTagContainer` or `GameplayTagRequirements` during definition construction.

Example conversion:

```csharp
GameplayTagContainer abilityTags = GameplayTagContainerNameExtensions.FromTagNames(row.AbilityTags);
GameplayTagRequirements activationRequirements = GameplayTagContainerNameExtensions.CreateRequirementsFromTagNames(
    row.ActivationBlockedTags,
    row.ActivationRequiredTags);
```

The default conversion mode is strict. Unknown or empty tag names throw so broken design data fails during startup or validation, not during combat.

## Production Guidance

- Keep a dedicated GameplayTag catalog table as the authoritative source for stable live-service projects.
- Treat ability/effect row references as validation inputs unless the project intentionally wants referenced tags to be auto-registered.
- Bake static tag manifests into `GameplayTags.bytes` for production clients when possible.
- If a hot-update or server-config flow changes tags at runtime, compare `GameplayTagManager.CurrentManifestHash` and resynchronize replicated tag containers before applying new gameplay state.
- Do not persist runtime indices. Store tag names, stable IDs, or the GameplayTags network serializer payloads.

## Persistence

This package does not write files, assets, `EditorPrefs`, `PlayerPrefs`, registry entries, or hidden global preferences.

It only reads DataTable rows that the host project has already loaded and registers tags into the in-memory `GameplayTagManager` snapshot. Persistence, cache invalidation, schema migration, and generated table storage remain owned by `CycloneGames.DataTable` or the host project.

## Validation

CLI checks after Unity has regenerated project files:

```bash
dotnet build CycloneGames.GameplayTags.DataTable.csproj -v:minimal
dotnet build CycloneGames.GameplayTags.DataTable.Tests.Editor.csproj -v:minimal
```

Recommended Unity Editor validation:

1. Open the Unity project from `<repo-root>/UnityStarter`.
2. Confirm `CycloneGames.GameplayTags`, `CycloneGames.DataTable`, and `CycloneGames.GameplayTags.DataTable` are all present.
3. Run EditMode tests in `CycloneGames.GameplayTags.DataTable.Tests.Editor`.
4. Register a small generated-style tag catalog table and verify `GameplayTagManager.RequestTag(...)` resolves the generated names.
5. Register a generated ability/effect reference source and verify containers created by `GameplayTagContainerNameExtensions` match the row data.
