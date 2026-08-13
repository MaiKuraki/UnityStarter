# CycloneGames.GameplayFramework.GameplayAbilities

[Simplified Chinese](README.SCH.md)

## Overview

This package connects GameplayFramework Actors to CycloneGames GameplayAbilities without adding a GameplayAbilities dependency to the GameplayFramework Runtime assembly. It defines a provider contract and focused composition helpers for resolving an `AbilitySystemComponent` and initializing its owner/avatar information.

The package does not create, tick, reset, or dispose an ability system. Those responsibilities remain with the component's application-level owner.

## Assemblies and Dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | Defines `IAbilitySystemProvider` and Actor composition helpers. | Explicit |
| `CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor` | Verifies provider discovery and actor-info initialization. | Test Runner only |

The package declares direct UPM dependencies on GameplayFramework and GameplayAbilities. The Runtime assembly is `autoReferenced: false`; a project assembly that calls the bridge must reference it explicitly.

## Installation

### UPM

Install `com.cyclone-games.gameplay-framework-gameplay-abilities`. Package Manager resolves the two declared module dependencies. No scripting define symbol is required.

### Embedded under Assets

Place this package, `CycloneGames.GameplayFramework`, and `CycloneGames.GameplayAbilities` under the project's Assets tree. Direct asmdef references activate the integration when those package roots are present. No PlayerSettings symbol or generated capability file is used.

## Providing an Ability System

Implement `IAbilitySystemProvider` on an Actor subclass or on a component attached to the same GameObject:

~~~csharp
public sealed class AbilitySystemProvider : MonoBehaviour, IAbilitySystemProvider
{
    public AbilitySystemComponent AbilitySystem { get; private set; }

    public void Initialize(AbilitySystemComponent abilitySystem)
    {
        AbilitySystem = abilitySystem;
    }
}
~~~

Resolve and initialize the relationship during composition or Actor startup:

~~~csharp
if (!actor.InitializeAbilityActorInfo())
{
    // The Actor has no initialized IAbilitySystemProvider.
}
~~~

Without overrides, the helper uses `Actor.GetOwner()` when available, falls back to the Actor as owner, and uses the Actor as avatar. The overload accepting owner and avatar Actors allows an application to model persistent owner and replaceable avatar objects explicitly.

## Ownership and Lifecycle

- `IAbilitySystemProvider` exposes an existing `AbilitySystemComponent`; it does not transfer ownership.
- `InitializeAbilityActorInfo` does not schedule `AbilitySystemComponent.Tick`.
- The ability-system owner selects its clock, forwards Tick, and disposes the component.
- Reinitialization applies the owner/avatar values passed to the helper at that time.
- Actor destruction does not implicitly dispose a separately owned ability system.

## Performance and Threading

`TryGetAbilitySystem` first checks whether the Actor implements the provider, then performs one `GetComponent<IAbilitySystemProvider>` lookup. Use it during composition or another cold path. Cache the resolved `AbilitySystemComponent` for repeated ability activation, tag checks, effect processing, and Tick forwarding.

Actor and Unity component discovery must run on the Unity main thread. Follow the GameplayAbilities threading contract for work performed inside the ability system; this bridge introduces no locks or cross-thread queue.

## Persistence

This integration writes no files and owns no serialized runtime state. Ability definitions, granted abilities, effects, and save behavior belong to GameplayAbilities and the application that owns the ability system.

## Validation

Run the following EditMode assembly after both required packages compile:

~~~text
CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor
~~~

For runtime validation, cover an Actor-subclass provider, a component provider, a missing provider, explicit owner/avatar overrides, avatar replacement, and disposal by the application owner.
