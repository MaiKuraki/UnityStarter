# CycloneGames.GameplayFramework.GameplayTags

[Simplified Chinese](README.SCH.md)

## Overview

This package connects GameplayFramework Actors to CycloneGames GameplayTags without adding GameplayTags types to the GameplayFramework Runtime assembly. It provides focused helpers for locating a `GameObjectGameplayTagContainer` on an Actor and operating on its `GameplayTagCountContainer`.

The integration does not create or own the tag container. Add and configure the GameplayTags component through normal scene, prefab, or runtime composition.

## Assemblies and Dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags` | Provides Actor-to-GameplayTags extension methods. | Explicit |
| `CycloneGames.GameplayFramework.Integrations.GameplayTags.Tests.Editor` | Verifies missing-container behavior and tag count operations. | Test Runner only |

The package declares direct UPM dependencies on GameplayFramework and GameplayTags. The Runtime assembly is `autoReferenced: false`; a project assembly that calls the extensions must reference it explicitly.

## Installation

### UPM

Install `com.cyclone-games.gameplay-framework-gameplay-tags`. Package Manager resolves the two declared module dependencies. No scripting define symbol is required.

### Embedded under Assets

Place this package, `CycloneGames.GameplayFramework`, and `CycloneGames.GameplayTags` under the project's Assets tree. Direct asmdef references activate the integration when those package roots are present. No PlayerSettings symbol or generated capability file is used.

## Actor Setup

Attach `GameObjectGameplayTagContainer` to the same GameObject as the Actor. The integration searches that GameObject only; it does not search parent or child objects.

~~~csharp
if (actor.TryGetGameplayTagContainer(out GameplayTagCountContainer tags))
{
    bool hasState = tags.HasTag(stateTag);
}
~~~

The convenience methods are:

- `TryGetGameplayTagContainer`
- `ActorHasGameplayTag`
- `AddGameplayTag`
- `RemoveGameplayTag`

Missing Actor or component references return `false`. Once a container is found, validation and count semantics are delegated to `GameplayTagCountContainer`, including its handling of invalid tags.

## Ownership and Lifecycle

- `GameObjectGameplayTagContainer` owns the runtime tag-count container exposed by its component API.
- The integration never disposes, replaces, or serializes that container.
- Actor lifetime and tag-container lifetime should be aligned by the scene or prefab composition owner.
- GameplayFramework Actor string tags and GameplayTags storage remain separate stores; this package does not synchronize them.

## Performance and Threading

Each convenience operation performs component discovery. Use the helpers during composition and cache the returned `GameplayTagCountContainer` for repeated queries or mutations. Hot-path gameplay code should call the container API directly after caching.

Unity component discovery must run on the Unity main thread. Tag mutation must follow the GameplayTags owner-thread contract. This bridge introduces no locks, copies, event stream, or cross-thread queue.

## Persistence

This integration writes no files and saves no preferences. Tag definitions, generated tag data, and any gameplay save representation belong to GameplayTags and the application save system.

## Validation

Run the following EditMode assembly after both required packages compile:

~~~text
CycloneGames.GameplayFramework.Integrations.GameplayTags.Tests.Editor
~~~

Runtime validation should cover a missing component, one configured container, repeated add/remove count behavior, invalid tags, prefab instantiation, and Actor destruction.
