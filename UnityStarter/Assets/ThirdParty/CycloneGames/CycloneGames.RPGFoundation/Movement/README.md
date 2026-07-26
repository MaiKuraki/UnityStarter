# RPG Foundation Movement

[Simplified Chinese](README.SCH.md)

Unity runtime movement components, pure movement contracts, state machines, animation bridges, and optional integrations for RPGFoundation.

## Bounded Runtime Retention

The process-wide `AnimationParameterCache` retains at most `MaximumEntryCount` (`65,536`) parameter names. Each 3D `MovementComponent` retains at most `MaximumIgnoredColliderCount` (`65,536`) explicit ignored-collider entries. These are implementation safety ceilings, not recommended product budgets. Existing cache hits, ignored-collider removals, and owner shutdown remain available at capacity. No active collision policy is automatically evicted.

New cache code should use `TryGetOrAddHash` or `TryPreWarm`; new collision-policy code should use `TryIgnoreCollision`. A `false` result means a new entry was not retained. `TryGetOrAddHash` still returns the deterministic Animator hash through its `out` value, allowing an explicitly uncached operation. Legacy `GetHash`, `PreWarm`, and `IgnoreCollision` preserve successful behavior but fail fast with `InvalidOperationException` at the ceiling.

`AnimationParameterCache.GetMemorySnapshot()` and `MovementComponent.GetMemorySnapshot()` expose count, capacity, and monotonic rejection counters in O(1). The reconstructible animation cache may be cleared explicitly; ignored-collider entries are active gameplay policy and are never pressure-trimmed generically.

Migration is additive: replace capacity-sensitive legacy calls with the matching `Try*` method and route rejection through the product policy. To roll back an individual call site, use the legacy API only when fail-fast behavior is intended. Workloads that genuinely exceed one owner ceiling should partition parameter namespaces or movement owners; changing a constant requires a reviewed framework build plus representative animation/physics load validation.

## Persistence and Serialization

This contract adds no serialized field, renames no type or field, changes no prefab/scene/`ScriptableObject` data, and writes no persistent state. No asset or save migration is required.

## Validation

Run `CycloneGames.RPGFoundation.Movement.Tests.Editor` in EditMode. Validate representative movement, ignored-collider, animation, Player, IL2CPP, and stripping paths on each target platform.
