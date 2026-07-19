# CycloneGames.RPGFoundation Projectile

[English | 简体中文](README.SCH.md)

A reusable projectile simulation foundation for RPG, action RPG, side-scroller, top-down 2D, 2.5D, and high-density bullet-pattern games. Separates pure projectile rules from Unity scene binding, physics adapters, deterministic fixed-point simulation, presentation pooling, and networking.

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

`ProjectileWorld` is a fixed-capacity dense projectile store that steps through spawned projectiles each tick, running velocity integration, homing guidance, swept collision queries, lifetime management, and hit event emission. The Core assembly has no Unity dependencies (`noEngineReferences: true`) and uses `ProjectileVector3` instead of `UnityEngine.Vector3`.

### Key Features

- **Fixed-capacity simulation** — Dense store with generation-safe handles, O(1) spawn/despawn
- **Three space profiles** — Full3D, SideScroller2D, TopDown2D
- **Homing and lead-homing** — Configurable turn rate and target prediction
- **Bounce and pierce** — Bounded iteration budget, non-alloc sweep continuation
- **Non-alloc collision adapters** — `SphereCastNonAlloc` (3D), `CircleCastNonAlloc` (2D)
- **Optional DeterministicMath** — Fixed-point simulation for lockstep, rollback, replay
- **GameplayAbilities integration** — Fireball/ability pattern without hard dependency
- **Editor tooling** — Presets, validation, runtime stats overlay

## Architecture

### Module Layout

```text
Projectile/
  Core/              CycloneGames.RPGFoundation.Projectile.Core.asmdef
  Runtime/           CycloneGames.RPGFoundation.Projectile.Runtime.asmdef
    Integrations/DeterministicMath/
  Editor/            CycloneGames.RPGFoundation.Projectile.Editor.asmdef
  Tests/Editor/
```

### Assembly Boundary

| Assembly | Role | UnityEngine |
| --- | --- | --- |
| `Projectile.Core` | Unity-free contracts, `ProjectileWorld`, snapshots, collision contracts, hit events | No |
| `Projectile.Runtime` | Unity authoring, lifecycle, Physics 2D/3D adapters, view pooling | Yes |
| `Projectile.Integrations.DeterministicMath` | Fixed-point state, payloads, simulator | No |
| `Projectile.Editor` | Inspectors and authoring support | Editor only |

## Quick Start

Create a definition asset via `Create > CycloneGames > RPGFoundation > Projectile > Definition`.

Spawn through a scene-owned `ProjectileSystemBehaviour`:

```csharp
using CycloneGames.RPGFoundation.Projectile.Core;
using CycloneGames.RPGFoundation.Projectile.Runtime;
using UnityEngine;

public sealed class WandAttack : MonoBehaviour
{
    [SerializeField] private ProjectileSystemBehaviour Projectiles;
    [SerializeField] private ProjectileDefinitionAsset ArcaneMissile;

    public bool Fire(ulong ownerId, ulong projectileId, Vector3 muzzle, Vector3 direction, ulong targetId)
    {
        return Projectiles.TrySpawn(
            ArcaneMissile,
            muzzle,
            direction,
            ownerId,
            projectileId,
            out ProjectileHandle handle,
            targetId);
    }
}
```

Subscribe to hit events:

```csharp
projectileSystem.Hit += hit =>
{
    // Resolve target id, apply GameplayAbilities effects, dispatch cues.
};
```

## Core Concepts

| Type | Purpose |
| --- | --- |
| `ProjectileDefinition` | Immutable tuning: speed, acceleration, gravity scale, radius, lifetime, guidance, pierce, bounce, collision mask, effect payload id. |
| `ProjectileVector3` | Unity-free 3D value type used by all Core contracts. |
| `ProjectileSpawnRequest` | Spawn command with owner id, projectile id, target id, tick, prediction key, seed, position, direction. |
| `ProjectileHandle` | Slot + generation handle protecting callers from stale references after despawn. |
| `ProjectileWorld` | Fixed-capacity dense store; step operates on preallocated world storage. |
| `ProjectileState` | Mutable per-projectile runtime state. |
| `ProjectileHitEvent` | Hit result emitted into a fixed-capacity buffer during `ProjectileWorld.Step`. |
| `IProjectileCollisionWorld` | Non-alloc sweep query boundary — Unity Physics, deterministic worlds, or server collision maps. |
| `IProjectileTargetProvider` | Target position/velocity lookup for homing. |
| `ProjectileSpaceProfile` | Plane mapping for 3D, side-scroller 2D, top-down 2D, and 2.5D gameplay spaces. |

### Simulation Modes

| Profile | Gameplay plane | Typical use |
| --- | --- | --- |
| `Full3D` | XYZ | 3D RPG, MMO, action combat, flying missiles |
| `SideScroller2D` | XY with locked Z | Platformer, Contra-like side-view shooting |
| `TopDown2D` | XZ with locked Y | Top-down RPG, arena shooter, MOBA-like combat |

2.5D games usually use `TopDown2D` for authority and layer a visual height in presentation.

## Usage Guide

### GameplayAbilities Integration

Projectile Core does not directly reference `CycloneGames.GameplayAbilities`. Compose fireball-style abilities at the project or optional integration layer:

1. A `GameplayAbility` validates cost, cooldown, tags, targeting, and prediction.
2. The ability creates `ProjectileSpawnRequest` values with stable ids, target id, start tick, prediction key, and seed.
3. `ProjectileWorld` owns movement, sweep collision, lifetime, pierce, bounce, and hit emission.
4. The gameplay composition root maps `ProjectileHitEvent.EffectPayloadId` to `GameplayEffect` / cue policy.
5. The server applies authoritative effects and replicates hit/despawn/correction.

### Homing and Bullet Patterns

`Homing` rotates velocity toward target with bounded turn rate. `LeadHoming` adds `TargetVelocity × LeadPredictionTime` for intercepting moving targets.

| Ability | Setup |
| --- | --- |
| Arcane missiles | Multiple spawn requests over a short schedule, `Homing`, moderate turn rate |
| Contra-style homing missile | `LeadHoming`, higher lifetime, lower turn rate, visible turn radius |
| Bullet hell ring | Emitter creates many requests from `seed`, angle step, definition id, start tick |

High-density bullet patterns should avoid GameObject-per-projectile presentation. Use `ProjectileWorld` for authority and an instanced renderer or Native/Burst path for presentation.

### Collision and Tunneling

`ProjectileWorld` expects collision worlds to perform swept queries from `PreviousPosition` to `Position`. Unity adapters use `SphereCastNonAlloc` (3D) and `CircleCastNonAlloc` (2D). When a projectile bounces or pierces, the world continues with a fixed collision-iteration budget (default `4`).

Core does not own Unity collider filtering. Add target ignore lists, owner filtering, friendly-fire rules, and duplicate-hit suppression in project-specific adapters.

## Advanced Topics

### DeterministicMath Integration

Enabled by `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH`. The integration chooses explicit zero-vector fallbacks at projectile boundaries: a zero spawn direction creates zero initial velocity, and a homing target at the projectile's position preserves travel direction.

Use for lockstep/rollback simulation, replay verification, seed-based bullet pattern reconstruction, or bit-identical fixed-point homing across platforms.

### Numeric Model

Default `ProjectileWorld` uses `float` via `ProjectileVector3` — suitable for server-authoritative multiplayer where the server is the source of truth.

Use the `DeterministicMath` integration when the same projectile path must be bit-identical across machines, builds, or replays. Do not make `DeterministicMath` a required Core dependency — many RPG projectiles only need server authority, stable ids, and reliable hit messages.

### Networking

Networking lives in the separate `CycloneGames.RPGFoundation.Projectile.Networking` package. Recommended policy:

- Server owns authoritative spawn, hit, despawn, and correction.
- Clients may spawn predicted visuals using `PredictionKey`.
- Bullet patterns replicate stable definition ids, seeds, and start ticks.
- Long-lived projectiles receive periodic authoritative snapshots.
- Hit messages are reliable and applied only after server validation.

### Threading

`ProjectileWorld` is single-writer. Own it from one simulation thread and feed commands at a known simulation barrier. Unity-facing adapters must run on the main thread. Pure Core and DeterministicMath state can run in headless/server contexts.

## Common Scenarios

### Editor Authoring

`ProjectileDefinitionAssetEditor` and `ProjectileSystemBehaviourEditor` provide grouped authoring panels, validation messages, and presets (Fireball, Arcane Missile, Homing Missile, Ricochet). `ProjectileDefinitionAsset` and `ProjectileSystemBehaviour` are inheritable. In Play Mode, `ProjectileWorldStats` shows active count, peak count, spawn rejection, hit overflow, and collision iteration pressure.

### Persistence

This module writes no files, assets, `PlayerPrefs`, or runtime save data. `ProjectileDefinitionAsset` data is stored in project-created Unity assets. Runtime state is in memory and should be captured by project save/replay systems when required.

### Validation

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.DeterministicMath.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
```

When testing physics adapters, create small scenes with `ProjectileSystemBehaviour`, a definition asset, and colliders on the configured layer. Confirm hit events fire once, stale handles fail after terminal hits, and pooled views return to pool on cleanup.

## Performance and Memory

`ProjectileWorld` allocates state arrays, slot maps, free-list, and hit event buffers at construction — this is world storage, not a general-purpose pool.

After warm-up:

- Spawn and despawn: O(1).
- Stepping: O(n) for active projectiles.
- Despawn uses swap-remove and generation-safe handles.
- Hit events write into a fixed-capacity buffer.
- Unity adapters use `SphereCastNonAlloc` / `CircleCastNonAlloc`.
- Bounce/pierce use fixed iteration budget — no allocation.
- `ProjectileWorldStats` records pressure without allocating.

Capacity misses return `false` without silent resizing. Size the world per encounter, room, server shard, or bullet-pattern profile.

## Troubleshooting

| Symptom | Cause | Resolution |
| --- | --- | --- |
| Projectile not spawning | World at capacity | Check `ProjectileWorldStats` for spawn rejection; increase capacity |
| Stale handle after despawn | Using handle after projectile destroyed | Generation check prevents reuse; get fresh handle on spawn |
| Missed collision | Probe radius too small or tunneling | Increase radius, tune fixed timestep, or adjust iteration budget |
| Hit event fired twice | Missing duplicate-hit suppression | Add target ignore list in project-specific collision adapter |
| Deterministic mismatch | Floating-point discrepancies | Switch to `DeterministicMath` integration for bit-identical results |
| Performance issues | Too many active projectiles | Profile stepping cost; use instanced renderer for high-density patterns |
