# CycloneGames.RPGFoundation Projectile

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.RPGFoundation.Projectile` provides a reusable projectile simulation foundation for RPG, action RPG, side-scroller, top-down 2D, 2.5D, and high-density bullet-pattern games. The module separates pure projectile rules from Unity scene binding, physics adapters, deterministic fixed-point simulation, presentation pooling, and networking.

The first implementation focuses on a stable production boundary: fixed-capacity runtime state, handle generation safety, allocation-free stepping after warm-up, non-alloc Unity Physics adapters, optional `DeterministicMath` simulation, and transport-neutral networking contracts in a separate package.

## Module Layout

```text
Projectile/
  Core/
    CycloneGames.RPGFoundation.Projectile.Core.asmdef
  Runtime/
    CycloneGames.RPGFoundation.Projectile.Runtime.asmdef
    Integrations/DeterministicMath/
  Editor/
    CycloneGames.RPGFoundation.Projectile.Editor.asmdef
  Tests/Editor/
```

| Area | Purpose |
| --- | --- |
| `Core/` | Unity-free projectile definitions, `ProjectileVector3`, spawn requests, handles, snapshots, collision contracts, hit events, target contracts, and `ProjectileWorld`. |
| `Runtime/` | Unity `ScriptableObject` authoring, `MonoBehaviour` lifecycle bridge, non-alloc Physics 2D/3D collision adapters, and pooled view helpers using `CycloneGames.Factory`. |
| `Runtime/Integrations/DeterministicMath/` | Optional fixed-point simulator and payload helpers for lockstep, rollback, replay, server verification, and seed-based bullet pattern reconstruction. |
| `Editor/` | Custom inspectors for projectile definitions and system capacity/profile authoring. |
| `Tests/Editor/` | EditMode tests for core stepping, homing, hit events, deterministic simulation, and integration boundaries. |

## Assembly Boundary

| Assembly | Role | UnityEngine dependency |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Projectile.Core` | Pure runtime contracts and fixed-capacity projectile simulation. | No |
| `CycloneGames.RPGFoundation.Projectile.Runtime` | Unity authoring, lifecycle, Physics 2D/3D adapters, and Factory-backed view pooling. | Yes |
| `CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath` | Fixed-point projectile state, payloads, simulator, and conversion helpers. | No UnityEngine |
| `CycloneGames.RPGFoundation.Projectile.Editor` | Inspectors and authoring support. | Editor only |
| `CycloneGames.RPGFoundation.Projectile.Tests.Editor` | Core and runtime EditMode tests. | Editor only |

The core assembly has no Unity package references and sets `noEngineReferences` to `true`. It uses `ProjectileVector3` instead of `UnityEngine.Vector3` or `Unity.Mathematics.float3`, so the same contracts can be used by Unity clients, CLI tests, headless servers, replay tools, and external simulation programs.

## Core Concepts

| Type | Purpose |
| --- | --- |
| `ProjectileDefinition` | Immutable runtime tuning: speed, acceleration, gravity scale, radius, lifetime, guidance, pierce, bounce, collision mask, and effect payload id. |
| `ProjectileDefinitionValidator` | Unity-free validation helper that writes issues into caller-owned arrays for Inspector, CI, batch asset checks, and command-line tooling. |
| `ProjectileVector3` | Unity-free 3D value type used by all Core contracts and snapshots. Unity-facing adapters convert at the boundary. |
| `ProjectileSpawnRequest` | Spawn-time command with owner id, projectile id, target id, tick, prediction key, seed, position, direction, and optional initial velocity. |
| `ProjectileHandle` | Slot + generation handle that protects callers from stale references after despawn and slot reuse. |
| `ProjectileWorld` | Fixed-capacity dense projectile store with allocation-free stepping after construction. |
| `ProjectileWorldStats` | Read-only runtime diagnostics for active count, peak count, spawn rejection, hit event overflow, collision query count, and iteration-limit pressure. |
| `ProjectileState` | Mutable per-projectile runtime state owned by `ProjectileWorld`. |
| `ProjectileSnapshot` | Transport- and presentation-friendly state projection. |
| `ProjectileHitEvent` | Hit result emitted into a fixed-capacity event buffer during `ProjectileWorld.Step`. |
| `IProjectileCollisionWorld` | Non-alloc sweep query boundary. Unity Physics, deterministic worlds, or server collision maps implement this interface. |
| `IProjectileTargetProvider` | Target position and velocity lookup for homing and lead-homing projectiles. |
| `ProjectileSpaceProfile` | Plane mapping for 3D, side-scroller 2D, top-down 2D, and 2.5D style gameplay spaces. |

## Simulation Modes

`ProjectileSpaceProfile` keeps the same projectile logic usable across common RPG camera and world layouts.

| Profile | Gameplay plane | Typical use |
| --- | --- | --- |
| `Full3D` | XYZ | 3D RPG, MMO, action combat, flying missiles. |
| `SideScroller2D` | XY with locked Z | Platformer, Contra-like side-view shooting. |
| `TopDown2D` | XZ with locked Y | Top-down RPG, arena shooter, MOBA-like combat. |

2.5D games usually use `TopDown2D` for authority and layer a visual height/arc in presentation. Do not mix cosmetic height into authoritative collision unless the project explicitly needs true 3D hit volumes.

## Basic Usage

Create a definition asset through:

```text
Create > CycloneGames > RPGFoundation > Projectile > Definition
```

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

Subscribe to hit events at the gameplay composition root and forward them to damage, ability, or cue systems:

```csharp
projectileSystem.Hit += hit =>
{
    // Resolve target id, apply GameplayAbilities effects, then dispatch cues.
};
```

## GameplayAbilities Integration

Projectile does not directly reference `CycloneGames.GameplayAbilities` from Core. A fireball-style ability should be composed at the project or optional integration layer:

1. A `GameplayAbility` validates cost, cooldown, tags, targeting, ownership, and prediction.
2. The ability creates one or more `ProjectileSpawnRequest` values with a stable definition id, owner id, projectile network id, target id, start tick, prediction key, and seed.
3. `ProjectileWorld` owns movement, sweep collision, lifetime, pierce, bounce, and hit event emission.
4. The gameplay composition root maps `ProjectileHitEvent.EffectPayloadId` or `ProjectileDefinitionId` to a `GameplayEffect` / cue policy.
5. The server applies authoritative effects and replicates hit/despawn/correction through the networking package.

This keeps projectile simulation reusable for non-GAS games while still allowing Diablo-like fireballs, missiles, traps, and area projectile abilities to cooperate with GameplayAbilities.

## Homing And Bullet Patterns

`ProjectileGuidanceMode.Homing` rotates the projectile velocity toward a target position with a bounded turn rate. `LeadHoming` adds target velocity times `LeadPredictionTime`, which is useful for guided missiles that should intercept moving enemies.

Examples:

| Ability | Suggested setup |
| --- | --- |
| Arcane missiles | Multiple spawn requests over a short schedule, `Homing`, moderate turn rate, server-authoritative hit. |
| Contra-style homing missile | `LeadHoming`, higher lifetime, lower turn rate, visible turn radius, optional bounce disabled. |
| Bullet hell ring | Project-owned emitter creates many `ProjectileSpawnRequest` values from `seed`, angle step, definition id, and start tick. |

High-density bullet patterns should avoid GameObject-per-projectile presentation. Use `ProjectileWorld` for authority and a project-specific instanced renderer or Native/Burst path for presentation.

## Collision And Tunneling

`ProjectileWorld` expects collision worlds to perform swept queries from `PreviousPosition` to `Position`. The Unity adapters use `SphereCastNonAlloc` for 3D and `CircleCastNonAlloc` for 2D, so fast projectiles are not limited to overlap checks at the final position.

When a projectile bounces or pierces, the world continues along the remaining sweep distance with a fixed collision-iteration budget. The default budget is `4`, which keeps the hot path bounded while covering common cases such as one-frame bounce and multi-hit pierce. Projects with extremely fast bullets, very thin colliders, or many pierce targets should tune fixed timestep, projectile radius, collision adapter filtering, and iteration budget together.

The Core interface intentionally does not own Unity collider filtering. Server worlds, deterministic maps, and project-specific Unity adapters should add target ignore lists, owner filtering, friendly-fire rules, and duplicate-hit suppression where required.

## DeterministicMath Integration

The optional deterministic assembly is enabled by `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH` and references `CycloneGames.DeterministicMath.Core`.

The integration chooses explicit zero-vector fallbacks at projectile boundaries: a zero spawn direction creates zero initial velocity, and a homing target located exactly at the projectile position preserves the current travel direction. This policy is local to projectile simulation; the base `FPVector3.Normalized` contract remains fail-fast.

Use it when the product needs:

- Lockstep or rollback projectile simulation.
- Replay or combat-record verification.
- Server validation of predicted projectile paths.
- Seed-based bullet pattern reconstruction instead of per-projectile snapshot replication.
- Bit-identical fixed-point homing results across platforms.

Do not use fixed-point simulation for purely cosmetic missiles or normal server-authoritative RPG projectiles unless deterministic replay is a product requirement.

## Numeric Model

The default `ProjectileWorld` uses `float` through `ProjectileVector3`. This is appropriate for server-authoritative multiplayer, where the server is the source of truth and clients use prediction plus correction for presentation.

Use the `DeterministicMath` integration when the same projectile path must be bit-identical across machines, builds, or replays. Typical cases include lockstep, rollback, authoritative combat record playback, deterministic bullet-pattern reconstruction from `seed` and `startTick`, and anti-cheat verification that compares simulated results instead of only accepting server snapshots.

Do not make `DeterministicMath` a required Core dependency for all games. It is more expensive to author and reason about than float simulation, and many RPG projectiles only need server authority, stable ids, reliable hit messages, and occasional corrections.

## Networking

Networking lives in the separate `CycloneGames.RPGFoundation.Projectile.Networking` package. The base Projectile module remains network-agnostic.

Recommended multiplayer policy:

- Server owns authoritative spawn, hit, despawn, and correction.
- Clients may spawn predicted visuals using `PredictionKey`.
- Bullet patterns replicate stable definition ids, seeds, and start ticks when project policy allows reconstruction.
- Long-lived or target-seeking projectiles receive periodic authoritative snapshots.
- Hit messages are reliable and should be applied only after server validation.

The networking package integrates with `CycloneGames.Networking` through protocol manifests, `NetworkVector3`, `NetworkActionResult`, snapshot history, message validators, reconciliation policies, and authority bridge interfaces. It remains transport-neutral and does not bind Projectile.Core to a concrete network backend.

## Performance Model

`ProjectileWorld` allocates its state arrays, slot maps, free-list, collision hit buffer, and hit event buffer at construction. This is world storage, not a general-purpose object pool. It uses a dense store and generation-safe handles because projectiles are short-lived simulation entities. GameObject presentation pooling remains delegated to `CycloneGames.Factory`.

After warm-up:

- Spawn and despawn are O(1).
- Stepping active projectiles is O(n).
- Despawn uses swap-remove and generation-safe handles.
- Hit events are written into a fixed-capacity ring-like buffer for the current step.
- Unity Physics adapters use `SphereCastNonAlloc` and `CircleCastNonAlloc`.
- Bounce and pierce continuation use a fixed iteration budget and do not allocate.
- `ProjectileWorldStats` records runtime pressure without allocating, so inspectors, debug overlays, or server telemetry can observe capacity and event-buffer issues.

Capacity misses return `false`; they do not silently resize hot-path storage. Size the world per encounter, room, server shard, or bullet-pattern profile.

## Editor Tooling

`ProjectileDefinitionAssetEditor` and `ProjectileSystemBehaviourEditor` use the shared `CycloneGames.RPGFoundation.Editor` inspector UI utilities. They provide grouped authoring panels, validation messages, runtime status, and capacity hints.

The inspectors are designed for business extension:

- `ProjectileDefinitionAsset` and `ProjectileSystemBehaviour` are inheritable.
- `ProjectileDefinitionAsset.BuildDefinition` is virtual so product code can extend authoring data without changing `Projectile.Core`.
- `ProjectileDefinitionAsset.BuildAuthoringDefinition` exposes raw authoring values for validation before runtime sanitization.
- `ProjectileSystemBehaviour` exposes protected virtual creation hooks for the space profile, projectile world, collision world, and hit dispatch.
- Custom inspectors use `[CustomEditor(typeof(...), true)]` and draw unhandled serialized fields after the built-in sections. Derived classes can add `[SerializeField]` fields without losing them in the Inspector.

Projectile definition inspectors include Fireball, Arcane Missile, Homing Missile, and Ricochet presets. Presets adjust only base projectile tuning and preserve project identity fields such as definition id, collision mask, effect payload id, and derived-class extension fields.

Validation is powered by the Unity-free `ProjectileDefinitionValidator`, so the same rules can be reused by custom Editor windows, CI asset scans, import processors, or external tooling. Runtime code still clamps unsafe numeric values before constructing Core definitions.

In Play Mode, `ProjectileSystemBehaviourEditor` shows `ProjectileWorldStats`, including active count, peak active count, spawn rejection pressure, hit overflow, and collision iteration pressure.

## Threading

`ProjectileWorld` is single-writer by design. Own it from one simulation thread and feed it commands at a known simulation barrier. Other threads should pass spawn/despawn/target updates through project-owned command queues.

Unity-facing `ProjectileSystemBehaviour`, Physics adapters, and `ProjectileViewPool` must run on the Unity main thread. Pure Core and DeterministicMath state can run in headless or server contexts that do not touch Unity objects.

## Persistence

This module does not write files, assets, `PlayerPrefs`, `EditorPrefs`, `SessionState`, hidden caches, or runtime save data. `ProjectileDefinitionAsset` authoring data is stored in Unity assets explicitly created by the project. Runtime projectile state is in memory and should be captured by project save/replay systems only when required.

## Validation

Run these checks after changing the module:

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.DeterministicMath.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
```

When testing Unity Physics adapters, create small 2D and 3D scenes with a `ProjectileSystemBehaviour`, a definition asset, and colliders on the configured collision layer. Confirm hit events fire once, stale handles fail after terminal hits, and pooled views are returned during cleanup.
