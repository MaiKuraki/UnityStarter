# CycloneGames.Networking.GAS.Integrations.GameplayAbilities

English | [简体中文](./README.SCH.md)

Integration package that bridges `CycloneGames.Networking.GAS` with `CycloneGames.GameplayAbilities`.

## What This Package Provides

- `GameplayAbilitiesNetworkedASCAdapter`:
  - Implements `INetworkedASC`.
  - Captures ASC full-state datas for reconnect/late-join.
  - Captures refined effect deltas as add/update/stack-change/remove groups.
  - Applies replicated attribute/tag/effect updates to an ASC.
- `IGASNetIdRegistry`:
  - Central registry for stable IDs (ability/effect/attribute/tag + ASC network mapping).
- `DefaultGASNetIdRegistry`:
  - Runtime default implementation based on deterministic FNV-1a hashing + lookup tables.
- `IGASReplicatedEffectMutationHandler`:
  - Generic strategy for remote effect remove/stack/update mutation application.
- `GASBridgeGameplayAbilitiesExtensions`:
  - One-line bridge registration for a `GameplayAbilitiesNetworkedASCAdapter`.
  - One-line pending-delta replication using your observer resolver.
  - One-line full-state send for reconnect/late-join clients.
- `IGASFullStateAuthorizationPolicy` + `GASBridgeSecurityExtensions`:
  - Generic policy model for full-state request authorization.
- `IConnectionRateLimiter` + `InMemoryTokenBucketRateLimiter`:
  - Reusable connection-scoped request throttling.
- `IGASSecurityAuditSink` + `UnityLogGASSecurityAuditSink`:
  - Security event audit abstraction and default Unity log sink.

## Important Notes

- Adapter now has a built-in fallback path for replicated effect remove/stack/update application through the public ASC mutation APIs added in `CycloneGames.GameplayAbilities`.
- `EffectMutationHandler` and the callback hooks (`TryRemoveReplicatedEffect`, `TryApplyReplicatedStackChange`, `TryApplyReplicatedEffectUpdate`) remain available when projects need stricter authority rules or custom side effects.
- Time values remain `float` across the public GAS surface to preserve Unity ergonomics and UE-style authoring flow; precision-sensitive networking should prefer authoritative datas/deltas over widening the authoring API to `double`.

## Mainline Wiring Example

```csharp
var adapter = bridge.RegisterGameplayAbilitiesASC(
  asc,
  networkId,
  ownerConnectionId,
  idRegistry);

bridge.ReplicatePendingState(adapter, GetObservers);

// Reconnect or late join
bridge.SendGameplayAbilitiesFullState(adapter, clientConnection);
```

## Generic Policy Wiring Example

```csharp
var limiter = new InMemoryTokenBucketRateLimiter(capacity: 2f, refillPerSecond: 0.5f);
var auditSink = UnityLogGASSecurityAuditSink.Instance;
var policy = new OwnerOrObserverWithRateLimitPolicy(limiter, auditSink);

bridge.ConfigureFullStateAuthorization(
  policy,
  getOwnerConnectionId: GetOwnerConnectionId,
  getObservers: GetObservers);
```
