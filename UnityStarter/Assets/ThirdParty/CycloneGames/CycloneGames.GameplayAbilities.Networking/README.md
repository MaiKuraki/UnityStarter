# CycloneGames.GameplayAbilities.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.GameplayAbilities.Networking` connects `CycloneGames.GameplayAbilities` to `CycloneGames.Networking`. It provides transport-neutral replication for ability activation, prediction confirmation/rejection, active gameplay effects, attributes, tags, state metadata, and full-state recovery.

The package does not bind gameplay abilities directly to Mirror, Mirage, Nakama, or any other SDK. It depends on Cyclone networking interfaces, so the same GAS networking layer can run on top of any backend that supplies `INetworkManager` and the required runtime services.

## What This Package Provides

- `NetworkedAbilityBridge` for ability RPC and replicated GAS messages.
- `INetworkedASC` as the network-facing contract for Ability System Component state.
- `GameplayAbilitiesNetworkedASCAdapter` for the Unity runtime `AbilitySystemComponent`.
- Compact message structs for ability activation, effect replication, attribute updates, tag updates, multicast events, full state, and sync metadata.
- `GASNetworkSerializer` and `GASNetworkSerializerOptions` for bounded deterministic serialization.
- `GASNetFixed` for raw fixed-point network values.
- Full-state authorization policies and rate limiting hooks.
- State checksum helpers for drift detection and reconnect validation.
- Editor diagnostics preset and diagnostics window.

## Package Layout

```text
CycloneGames.GameplayAbilities.Networking/
  Core/             Pure C# bridge, messages, serializer, security policies
  Unity.Runtime/    Unity AbilitySystemComponent adapter
  Editor/           Editor-only diagnostics and inspectors
  Tests/Editor/     Bridge, serialization, policy, and adapter tests
```

Important assemblies:

| Assembly | Purpose |
| --- | --- |
| `CycloneGames.GameplayAbilities.Networking.Core` | Pure C# network bridge, message structs, serializer, checksums, security policies, and interfaces. |
| `CycloneGames.GameplayAbilities.Networking.Unity.Runtime` | Unity runtime adapter for `AbilitySystemComponent`. |
| `CycloneGames.GameplayAbilities.Networking.Unity.Editor` | Editor diagnostics window, preset, and inspector. |
| `CycloneGames.GameplayAbilities.Networking.Tests.Editor` | Editor tests for serializer, bridge, security, and runtime adapter behavior. |

## Requirements

- `com.cyclone-games.networking`.
- `com.cyclone-games.gameplay-abilities`.
- `com.cyclone-games.gameplay-tags`.
- A concrete networking backend registered through `CycloneGames.Networking`.

Optional SDKs such as Mirror or Nakama are detected by editor diagnostics, but this package should still connect through Cyclone interfaces.

## Quick Start

### 1. Create the bridge

Create a `NetworkedAbilityBridge` during network bootstrap. Pass the active `INetworkManager` from Mirror, Mirage, Nakama, or your custom backend adapter.

```csharp
using CycloneGames.GameplayAbilities.Networking;
using CycloneGames.Networking;

public sealed class GASNetworkBootstrap : System.IDisposable
{
    private readonly NetworkedAbilityBridge _bridge;

    public GASNetworkBootstrap(INetworkManager networkManager)
    {
        _bridge = new NetworkedAbilityBridge(networkManager);
        _bridge.RegisterHandlers();
    }

    public void Dispose()
    {
        _bridge.Dispose();
    }
}
```

### 2. Register networked Ability System Components

Each networked actor that owns an Ability System Component should expose one stable `networkId` and one owner connection id.

```csharp
using CycloneGames.GameplayAbilities.Networking;
using CycloneGames.GameplayAbilities.Runtime;

public sealed class NetworkedAbilityOwner : System.IDisposable
{
    private readonly NetworkedAbilityBridge _bridge;
    private readonly GameplayAbilitiesNetworkedASCAdapter _adapter;

    public NetworkedAbilityOwner(
        NetworkedAbilityBridge bridge,
        AbilitySystemComponent asc,
        uint networkId,
        int ownerConnectionId)
    {
        _bridge = bridge;
        _adapter = new GameplayAbilitiesNetworkedASCAdapter(asc, networkId, ownerConnectionId);
        _bridge.RegisterASC(networkId, ownerConnectionId, _adapter);
    }

    public void Dispose()
    {
        _bridge.UnregisterASC(_adapter.NetworkId, _adapter.OwnerConnectionId);
        _adapter.Dispose();
    }
}
```

### 3. Request ability activation from the owning client

```csharp
using CycloneGames.Networking;

public void ActivateAbility(
    NetworkedAbilityBridge bridge,
    int abilityIndex,
    int predictionKey,
    NetworkVector3 targetPosition,
    NetworkVector3 direction,
    uint targetNetworkId)
{
    bridge.ClientRequestActivateAbility(
        abilityIndex,
        predictionKey,
        targetPosition,
        direction,
        targetNetworkId);
}
```

The server validates the request in gameplay code, then calls `ServerConfirmActivation` or `ServerRejectActivation`. Clients use the confirmation or rejection to keep or roll back predicted local state.

### 4. Replicate effects, attributes, and tags from the server

The server sends replicated state to owner or observer connections through the bridge:

```csharp
bridge.ServerReplicateEffectApplied(observers, targetNetworkId, effectData);
bridge.ServerBroadcastAttributes(observers, targetNetworkId, attributeData);
bridge.ServerSyncTags(observers, targetNetworkId, tagData);
```

Use the observer list produced by your game framework, interest system, or backend room/zone logic.

## Main Runtime Types

### `NetworkedAbilityBridge`

`NetworkedAbilityBridge` is the central GAS network bridge. It owns message registration, typed RPC send/receive, ASC lookup by connection id or network id, full-state request handling, and event forwarding to gameplay code.

Default message ids:

| Message | Id |
| --- | ---: |
| `MsgAbilityActivateRequest` | 200 |
| `MsgAbilityActivateConfirm` | 201 |
| `MsgAbilityActivateReject` | 202 |
| `MsgAbilityEnd` | 203 |
| `MsgAbilityCancel` | 204 |
| `MsgEffectApplied` | 210 |
| `MsgEffectRemoved` | 211 |
| `MsgEffectStackChanged` | 212 |
| `MsgEffectUpdated` | 213 |
| `MsgAttributeUpdate` | 220 |
| `MsgTagUpdate` | 225 |
| `MsgAbilityMulticast` | 230 |
| `MsgFullState` | 240 |
| `MsgFullStateRequest` | 241 |
| `MsgStateSyncMetadata` | 242 |

Projects should keep their own message id allocation table and avoid collisions with gameplay messages.

### `INetworkedASC`

`INetworkedASC` is the network-facing contract for an Ability System Component. It receives server confirmations, rejections, replicated effects, attribute updates, tag updates, multicast events, full state, and state metadata.

Implement this directly for server-side pure C# tests or custom ability runtimes. Use `GameplayAbilitiesNetworkedASCAdapter` for the Unity runtime `AbilitySystemComponent`.

### `GameplayAbilitiesNetworkedASCAdapter`

`GameplayAbilitiesNetworkedASCAdapter` bridges a Unity `AbilitySystemComponent` to `INetworkedASC`.

It supports:

- Registration with `IGASNetIdRegistry`.
- Prediction key confirmation and rejection callbacks.
- Replicated active effect apply, remove, stack change, and update.
- Attribute id registration and observer filtering.
- Tag replication.
- Full-state capture and application.
- State delta creation.
- Strict checksum validation.
- Optional runtime thread policy checks.

The adapter is disposable. Dispose it when the owning actor or ability component is destroyed.

## Full-State Recovery

Full-state messages are used when a client joins late, reconnects, detects drift, or needs a baseline reset.

Typical flow:

1. Client calls `ClientRequestFullState(targetNetworkId)`.
2. Server checks `FullStateRequestAuthorizer`.
3. Server captures `GASFullStateData`.
4. Server sends `MsgFullState` to the requesting connection.
5. Client applies the state through `INetworkedASC.OnFullState`.

For per-connection visibility, implement `INetworkedASCConnectionScopedFullState`. This lets the server return a filtered state snapshot for a specific observer.

## Authorization and Rate Limiting

Full-state requests can expose sensitive gameplay state. Configure authorization on the bridge:

```csharp
bridge.ConfigureFullStateAuthorization(
    new OwnerOrObserverWithRateLimitPolicy(new InMemoryTokenBucketRateLimiter(4f, 1f)),
    getOwnerConnectionId,
    getObservers);
```

Recommended rules:

- Owners may request their own full state.
- Observers may request only state they are allowed to observe.
- Repeated requests should be rate limited.
- Unauthorized attempts should be sent to an `IGASSecurityAuditSink` when the project has one.

## Serializer Options

`GASNetworkSerializerOptions` controls bounded array sizes and serializer behavior. Use profiles for common scales:

```csharp
var options = GASNetworkSerializerOptions.CreateForProfile(GASNetworkCapacityProfile.Conservative);
var bridge = new NetworkedAbilityBridge(networkManager, options);
```

Available profiles include conservative and large-server-oriented capacities. Choose the smallest profile that fits the game mode, then load-test with at least twice the expected peak.

The bridge can install `GASNetworkSerializer` into any `INetworkManager` that implements `INetworkSerializerConfigurable`.

## Prediction and Drift Handling

The package supports the standard client-prediction flow:

1. Client predicts ability activation locally.
2. Client sends `AbilityActivateRequest` with a prediction key.
3. Server validates authority, cooldown, cost, tags, range, target, and game rules.
4. Server sends `AbilityActivateConfirm` or `AbilityActivateReject`.
5. Client keeps or rolls back predicted state.

`GASStateSyncMetadata` and checksum helpers help detect drift:

- Out-of-order sequence.
- Base version mismatch.
- Checksum mismatch.
- Target network id mismatch.
- Invalid version range.

When drift is detected, the client can request a full-state baseline.

## Backend Compatibility

This package talks to `CycloneGames.Networking`, not directly to a backend SDK.

| Backend | Recommended integration |
| --- | --- |
| Mirror | Use `CycloneGames.Networking.Adapter.Mirror`, then create `NetworkedAbilityBridge` from the exposed `INetworkManager`. |
| Mirage | Use `CycloneGames.Networking.Adapter.Mirage`, then keep GAS code on Cyclone interfaces. |
| Nakama | Use Nakama for session, matchmaking, match state, RPC, or presence through Cyclone backend services. Realtime GAS replication should still pass through `INetworkManager`. |
| Best HTTP | Use for backend HTTP/RPC/download flows. Keep realtime GAS replication behind Cyclone networking. |
| Custom server | Implement `INetworkManager` and required Cyclone services, then reuse the GAS bridge unchanged. |

## Editor Diagnostics

Create a preset:

```text
Create > CycloneGames > GameplayAbilities > Networking > Diagnostics Preset
```

Open diagnostics:

```text
Tools > CycloneGames > GameplayAbilities > Networking > Diagnostics
Tools > CycloneGames > GameplayAbilities > Networking > Run Diagnostics Check
```

The diagnostics check:

- Required bridge type support.
- Required Ability runtime support.
- Required Cyclone network runtime support.
- Missing scene-driven `INetworkManager`.
- Optional SDK package detection for Mirror and Nakama.

The preset is editor-only and does not add runtime dependency on optional SDK packages.

## Testing

Recommended checks after changing this package:

```text
dotnet build UnityStarter/CycloneGames.GameplayAbilities.Networking.Core.csproj -nologo
dotnet build UnityStarter/CycloneGames.GameplayAbilities.Networking.Unity.Runtime.csproj -nologo
dotnet build UnityStarter/CycloneGames.GameplayAbilities.Networking.Tests.Editor.csproj -nologo
```

In Unity Editor, run the package tests through:

```text
Window > General > Test Runner
```

Focus on serializer bounds, full-state authorization, checksum drift detection, adapter dispose behavior, and bridge register/unregister lifecycle.

## Best Practices

- Keep ability validation server-authoritative.
- Use prediction keys only for local predicted state, not as proof of authority.
- Keep observer lists explicit and interest-aware.
- Keep full-state snapshots filtered for the target connection.
- Register handlers once during startup and unregister during teardown.
- Dispose `GameplayAbilitiesNetworkedASCAdapter` when the owning actor is destroyed.
- Pre-size serializer profiles for expected peak state.
- Do not let GAS code call Mirror, Mirage, Nakama, or Best HTTP SDK types directly.
- Keep message id allocations documented at the project level.

## Related Packages

- `CycloneGames.Networking` provides the transport-neutral network layer used by this package.
- `CycloneGames.GameplayAbilities` provides the ability system runtime.
- `CycloneGames.GameplayFramework` can provide owner, observer, team, and area-interest information for replicated ability state.
