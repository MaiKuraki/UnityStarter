# CycloneGames.Networking.GAS.Integrations.GameplayAbilities

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

Integration package that bridges `CycloneGames.Networking.GAS` with `CycloneGames.GameplayAbilities`.

## What This Package Provides

- `GameplayAbilitiesNetworkedASCAdapter`:
  - Implements `INetworkedASC`.
  - Captures ASC full-state snapshots for reconnect/late-join.
  - Applies replicated attribute/tag/effect updates to an ASC.
- `IGasNetIdRegistry`:
  - Central registry for stable IDs (ability/effect/attribute/tag + ASC network mapping).
- `DefaultGasNetIdRegistry`:
  - Runtime default implementation based on deterministic FNV-1a hashing + lookup tables.
- `IGasReplicatedEffectMutationHandler`:
  - Generic strategy for remote effect remove/stack mutation application.
- `IGasFullStateAuthorizationPolicy` + `GasBridgeSecurityExtensions`:
  - Generic policy model for full-state request authorization.
- `IConnectionRateLimiter` + `InMemoryTokenBucketRateLimiter`:
  - Reusable connection-scoped request throttling.
- `IGasSecurityAuditSink` + `UnityLogGasSecurityAuditSink`:
  - Security event audit abstraction and default Unity log sink.

## Important Notes

- `AbilitySystemComponent` currently does not expose public APIs for exact effect removal/stack mutation by external instance ID.
- Adapter exposes generic strategy hook (`EffectMutationHandler`) and callback hooks (`TryRemoveReplicatedEffect`, `TryApplyReplicatedStackChange`) so projects can wire custom logic now.
- If you add explicit ASC public APIs later, you can plug them into these callbacks without changing network protocol code.

## Generic Policy Wiring Example

```csharp
var limiter = new InMemoryTokenBucketRateLimiter(capacity: 2f, refillPerSecond: 0.5f);
var auditSink = UnityLogGasSecurityAuditSink.Instance;
var policy = new OwnerOrObserverWithRateLimitPolicy(limiter, auditSink);

bridge.ConfigureFullStateAuthorization(
  policy,
  getOwnerConnectionId: GetOwnerConnectionId,
  getObservers: GetObservers);
```
