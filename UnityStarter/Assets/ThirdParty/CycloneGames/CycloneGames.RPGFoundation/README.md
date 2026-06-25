# CycloneGames.RPGFoundation

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.RPGFoundation` provides reusable RPG-oriented foundation modules for Unity projects. It groups gameplay-facing systems by domain, keeps core contracts independent from Unity runtime objects, and exposes Unity-facing components through dedicated runtime, editor, test, and integration assemblies.

The package currently includes Interaction and Movement modules. Optional networking and third-party integrations are hosted in separate assemblies or packages so product projects can enable only the dependencies they use.

## Quick Start

1. Reference the module assembly that matches the feature you need, such as `CycloneGames.RPGFoundation.Movement.Runtime` for Unity movement components or `CycloneGames.RPGFoundation.Interaction.Runtime` for interaction components.
2. Use `Core` contracts for server, headless, simulation, CLI, or EditMode test code.
3. Use `Runtime` components for Unity scene binding, ScriptableObject configuration, and adapter-facing behavior.
4. Use optional integration assemblies only when the corresponding dependency is installed and enabled through its asmdef conditions.
5. Run the module EditMode tests after changing contracts, asmdefs, serialized data, or integration boundaries.

## Module Layout

Long-lived modules use this layout:

```text
<Module>/
  README.md
  README.SCH.md
  Core/
  Runtime/
  Editor/
  Tests/
  Runtime/Integrations/
```

| Directory | Purpose |
| --- | --- |
| `Core/` | Unity-free contracts, value objects, validation logic, deterministic data, and services for server, headless, CLI, and Unity test contexts. |
| `Runtime/` | Unity-facing components, ScriptableObject authoring bridges, runtime adapters, and default Unity implementations. |
| `Editor/` | Inspectors, windows, validators, drawers, and authoring tools. |
| `Tests/` | EditMode and PlayMode coverage for module contracts and runtime behavior. |
| `Runtime/Integrations/` | Optional third-party or Cyclone module adapters isolated behind their own asmdefs. |

Current modules:

| Module | Purpose |
| --- | --- |
| `Interaction/` | Interaction contracts, local runtime components, authority validation, deterministic bridges, inspectors, and tests. |
| `Movement/` | Movement core contracts, 2D/3D Unity movement components, pathfinding adapters, animation adapters, inspectors, and tests. |

## Assembly Boundary

| Assembly | Role |
| --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Core` | Unity-free interaction contracts, value objects, validation, rate limiting, and authority services. |
| `CycloneGames.RPGFoundation.Interaction.Runtime` | Unity-facing interaction components and runtime services. |
| `CycloneGames.RPGFoundation.Interaction.Editor` | Interaction inspectors, validators, and editor tools. |
| `CycloneGames.RPGFoundation.Interaction.Tests.Editor` | Interaction EditMode tests. |
| `CycloneGames.RPGFoundation.Movement.Core` | Unity-free movement contracts, attributes, state identifiers, snapshots, and helper types. |
| `CycloneGames.RPGFoundation.Movement.Runtime` | Unity-facing 2D/3D movement components, ScriptableObject configs, animation abstraction, and pathfinding abstraction. |
| `CycloneGames.RPGFoundation.Movement.Editor` | Movement inspectors and authoring validation. |
| `CycloneGames.RPGFoundation.Movement.Tests.Editor` | Movement EditMode tests. |

Movement state changes use `MovementStateRequestContext` to carry source, payload, tick, sequence, prediction key, and custom flags without making `Movement.Core` depend on GameplayAbilities, GameplayTags, or Networking. Ability, input, pathfinding, AI, and network integrations share the same state gate through isolated assemblies.

## Optional Integrations

Optional integrations are isolated in their own assemblies so the base package compiles without optional packages installed. Cyclone networking bridges are provided by separate optional packages.

| Integration Assembly | Dependency |
| --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath` | `CycloneGames.DeterministicMath.Core` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework` | `CycloneGames.GameplayFramework.Runtime` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework` | DeterministicMath + GameplayFramework |
| `CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath` | `CycloneGames.DeterministicMath.Core` |
| `CycloneGames.RPGFoundation.Movement.Integrations.Animancer` | `Kybernetik.Animancer` |
| `CycloneGames.RPGFoundation.Movement.Integrations.UnityNavigation` | `Unity.AI.Navigation` |
| `CycloneGames.RPGFoundation.Movement.Integrations.AStar` | `AstarPathfindingProject` |
| `CycloneGames.RPGFoundation.Movement.Integrations.AgentsNavigation` | ProjectDawn Agents Navigation |
| `CycloneGames.RPGFoundation.Movement.Integrations.GameplayAbilities` | `CycloneGames.GameplayAbilities.Runtime` + `CycloneGames.GameplayTags.Core` |

Optional networking packages:

| Package | Dependency | Purpose |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Networking` | `CycloneGames.Networking.Core` | Transport-neutral interaction request, result, cancel, and authority validation contracts. |
| `CycloneGames.RPGFoundation.Movement.Networking` | `CycloneGames.Networking.Core` | Transport-neutral movement input, authoritative snapshot, correction, teleport, full-state request, authority transfer, input validation, history, and reconciliation contracts. |

## Defines

These symbols are generated by integration asmdefs through `versionDefines` or define constraints. They are diagnostics and integration-local compile switches, not project-wide requirements.

| Symbol | Enables |
| --- | --- |
| `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH` | Interaction and Movement DeterministicMath integrations. |
| `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_FRAMEWORK` | Interaction GameplayFramework integration. |
| `CYCLONE_RPGFOUNDATION_HAS_ANIMANCER` | Movement Animancer integration. |
| `CYCLONE_RPGFOUNDATION_HAS_UNITY_AI_NAVIGATION` | Movement Unity AI Navigation integration. |
| `CYCLONE_RPGFOUNDATION_HAS_ASTAR_PATHFINDING` | Movement A* Pathfinding integration. |
| `CYCLONE_RPGFOUNDATION_HAS_AGENTS_NAVIGATION` | Movement Agents Navigation integration. |
| `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_ABILITIES` | Movement GameplayAbilities integration with GameplayAbilities and GameplayTags assemblies. |

## Persistence

This package does not define runtime save files, editor preferences, PlayerPrefs, EditorPrefs, SessionState data, registry entries, or hidden caches. Configuration and persistent gameplay state are owned by the consuming project or by the specific optional module that declares them.

## Validation

Run these checks after changing assemblies, moving files, updating integration references, or changing serialized contracts:

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Interaction.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Movement.Tests.Editor
Unity Test Runner > EditMode > optional RPGFoundation networking package tests when present
```

For CLI-oriented checks after Unity refreshes generated project files:

```text
dotnet build UnityStarter/CycloneGames.RPGFoundation.Movement.Core.csproj --nologo
dotnet build UnityStarter/CycloneGames.RPGFoundation.Movement.Runtime.csproj --nologo
dotnet build UnityStarter/CycloneGames.RPGFoundation.Movement.Tests.Editor.csproj --nologo
```
