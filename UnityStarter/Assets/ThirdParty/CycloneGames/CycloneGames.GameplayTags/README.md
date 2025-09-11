# CycloneGames.GameplayTags
English | [简体中文](./README.SCH.md)

Source: `https://github.com/BandoWare/GameplayTags`

This package adapts and extends the upstream Gameplay Tags for Unity with runtime-friendly features and minor API changes, keeping an Unreal-like tag workflow.

This repository adds static-class registration and runtime dynamic tag creation to better support hot-update games and online/networked games.

## Overview

- Purpose: better compatibility with hot-update workflows and flexible configuration.
- Cyclone-only extensions: static-class tag registration, runtime dynamic registration, early runtime initialization.

## Registration Methods

### 1) Declare tags via attributes (assembly-level)

```csharp
[assembly: GameplayTag("Damage.Fatal")]
[assembly: GameplayTag("Damage.Miss")]
[assembly: GameplayTag("CrowdControl.Stunned")]
[assembly: GameplayTag("CrowdControl.Slow")]
```

### 2) Or declare tags via a static class of const strings (repository-specific extension, recommended for project use)

- Add an assembly-level attribute pointing to a static class:

```csharp
using CycloneGames.GameplayTags.Runtime;

[assembly: RegisterGameplayTagsFrom(typeof(ProjectGameplayTags))]

public static class ProjectGameplayTags
{
    public const string Damage_Fatal = "Damage.Fatal";
    public const string Damage_Miss = "Damage.Miss";
}
```

- The manager will scan the class for public const string fields and register them as tags at init.
- Strongly recommended for hot-update friendly projects: tags can be consolidated in generated/static classes and loaded without asset edits.

### 3) Register tags dynamically at runtime (repository-specific extension; intended to support runtime temporary dynamic tags, possibly server-driven)

```csharp
GameplayTagManager.RegisterDynamicTag("Runtime.Dynamic.Tag");
GameplayTagManager.RegisterDynamicTags(new [] { "A.B", "A.C" });
```

### 4) Declare tags in JSON files

- Create `.json` files inside the `ProjectSettings/GameplayTags/` directory.
- The manager will automatically scan these files and register the tags.
- This method is ideal for designers or for managing large sets of tags without modifying code.

Example `ProjectSettings/GameplayTags/DamageTags.json`:
```json
{
  "Damage.Physical.Slash": {
    "Comment": "Damage from a slashing weapon."
  },
  "Damage.Magical.Fire": {
    "Comment": "Damage from a fire spell."
  }
}
```

## Editor Features

The Inspector for `GameplayTagContainer` has been enhanced for a better workflow:
- **Edit Tags Button**: Opens a popup window with a searchable and filterable tree view of all available tags.
- **Add/Remove Directly**: Check or uncheck tags in the tree view to add or remove them from the container.
- **Clear All**: A button to quickly remove all tags from the container.
- **Live Reloading**: The editor automatically watches for changes in `.json` tag files and reloads the tag database.

## Usage

### Request and use tags

```csharp
var tag = GameplayTagManager.RequestTag("Damage.Fatal");
GameplayTagCountContainer container = new GameplayTagCountContainer();
container.AddTag(tag);

container.RegisterTagEventCallback(tag, GameplayTagEventType.AnyCountChange, (t, count) =>
{
    UnityEngine.Debug.Log($"{t.Name} count -> {count}");
});
```

### Containers and queries

- `GameplayTagContainer`: serializable set of tags, with custom inspectors and utilities.
- `GameplayTagCountContainer`: maintains counts and tag change callbacks.
- `GameplayTagQuery` and `GameplayTagQueryExpression`: build and evaluate complex tag requirements.

## Differences vs Upstream (BandoWare/GameplayTags)

This package now incorporates many features from the upstream repository while retaining its unique extensions.

- **Unified Registration**: Now supports four registration methods: assembly attributes, static classes, runtime dynamic registration, and **JSON files**.
- **Enhanced Editor Workflow**: A significantly improved Inspector UI for `GameplayTagContainer` with a popup tree view for direct tag management.
- **Added `GameplayTag.IsValid` Property**: A new boolean property on `GameplayTag` to check if a tag is registered and valid, which is useful for handling tags that may have been renamed or deleted.
- **Performance & Stability**:
  - Includes `GameplayTagContainerPool` to reduce runtime garbage collection.
  - Tags are compiled into an efficient binary format during the build process for faster loading in standalone players.
- **Retained CycloneGames Extensions**:
  - Static-class registration (`[assembly: RegisterGameplayTagsFrom(...)]`).
  - Runtime dynamic registration (`RegisterDynamicTag`, `RegisterDynamicTags`).
  - Early runtime initialization via `GameplayTagManagerRuntimeInitialization`.
- **Consistent API Behavior**: `RequestTag` still returns `GameplayTag.None` for missing tags, and `TryRequestTag` is available.
