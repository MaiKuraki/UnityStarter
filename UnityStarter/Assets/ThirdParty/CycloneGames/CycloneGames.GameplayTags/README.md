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

- Namespace/package: `CycloneGames.GameplayTags` vs `BandoWare.GameplayTags`.
- Added runtime dynamic registration APIs:
  - `GameplayTagManager.RegisterDynamicTag(string name, string? description = null, GameplayTagFlags flags = GameplayTagFlags.None)`
  - `GameplayTagManager.RegisterDynamicTags(IEnumerable<string> tags)`
- Static-class registration support:
  - `[assembly: RegisterGameplayTagsFrom(typeof(YourStaticClass))]` scans const string fields and registers them.
- Startup safety:
  - `GameplayTagManagerRuntimeInitialization` calls `InitializeIfNeeded` at `BeforeSceneLoad` to avoid build-time deserialization issues.
- Behavioral tweaks:
  - `RequestTag` no longer logs warnings for missing tags; returns `GameplayTag.None`. Use `TryRequestTag` to probe without allocations.
- Minor utilities and naming alignment in runtime/editor folders.

