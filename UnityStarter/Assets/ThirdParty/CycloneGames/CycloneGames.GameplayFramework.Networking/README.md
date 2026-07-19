# CycloneGames.GameplayFramework.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.GameplayFramework.Networking` connects `CycloneGames.GameplayFramework` to `CycloneGames.Networking`. It supplies the authoritative session adapter, GameplayFramework message catalog, actor migration wire contract, server-authoritative damage messages, authority role helpers, and observer selection for owner, team, area, and always-relevant replication.

The base `CycloneGames.GameplayFramework.Runtime` assembly remains network-agnostic. Projects only reference this integration assembly when GameplayFramework objects participate in Cyclone Networking flows.

## Package Layout

```text
CycloneGames.GameplayFramework.Networking/
  Core/
    CycloneGames.GameplayFramework.Networking.Core.asmdef
    ActorMigrationNetworkingExtensions.cs
    GameplayFrameworkNetworkProtocol.cs
    GameplayNetworkObserverRegistry.cs
    GameplayNetworkReplication.cs
    NetworkGameSessionAdapter.cs
    Damage/
      DamageNetworkMessages.cs
      ServerDamageValidation.cs
  Tests/Editor/
    CycloneGames.GameplayFramework.Networking.Tests.Editor.asmdef
    ActorMigrationNetworkingExtensionsTests.cs
    GameplayNetworkReplicationTests.cs
    ServerDamageValidationTests.cs
```

## Assembly Boundary

| Assembly | Role | Unity dependency | Reference behavior |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Networking.Core` | Session bridging, protocol registration, actor migration DTO/codec, damage messages, authority helpers, observer registry, and observer resolution. | Yes | Explicit opt-in: `autoReferenced` is `false`; every consuming project/integration asmdef must reference this assembly directly. |
| `CycloneGames.GameplayFramework.Networking.Tests.Editor` | EditMode coverage for migration, protocol registration, authority/observer selection, and damage validation. | Yes, Editor only | Test assembly with `autoReferenced` set to `false`; it explicitly references the Runtime, Networking Core, and shared Networking assemblies. |

The core assembly directly references `CycloneGames.GameplayFramework.Runtime` and `CycloneGames.Networking.Core`. It has no Editor reference, no platform exclusion, and no dependency on a concrete transport SDK or DI container. Because it is not auto-referenced, predefined Unity assemblies and unrelated asmdefs do not receive it implicitly.

`ActorMigrationState` is owned by the `CycloneGames.GameplayFramework.Networking` namespace and compiled into `CycloneGames.GameplayFramework.Networking.Core`. A consumer that names this DTO must therefore reference the Networking Core assembly. Catalog descriptors do not store a runtime type name; message `11000` uses the explicit wire contract ID `ActorMigrationState:v1`.

## Core Concepts

| Type | Purpose |
| --- | --- |
| `NetworkGameSessionAdapter` | A plain C# `GameSession` implementation that stages authenticated connections by player id, binds successful `PlayerController` instances, and applies connection-aware admission, kick, and bounded in-memory address-ban rules. |
| `PlayerLoginRequest` | The bounded GameplayFramework admission input consumed by `GameMode.LoginAsync` and `NetworkGameSessionAdapter.ApproveLogin`, including the trusted composition-only `IsLocal` marker. |
| `ActorMigrationState` | Version-1 actor migration DTO registered as message `11000`. |
| `ActorMigrationNetworkingExtensions` | Captures/applies Unity actor state and reads/writes the fixed version-1 payload. |
| `GameplayFrameworkNetworkProtocol` | Owns the module manifest, message range, explicit contract identities, protocol version/fingerprint, and atomic catalog registration. |
| `DamageRequestMessage` / `DamageResultMessage` | Client intent and server-authoritative damage outcome messages. |
| `ServerAuthoritativeGameplayAuthorityResolver` | Resolves server authority, autonomous proxy, and simulated proxy roles. |
| `NetworkedGameplayActor` | Projects an `Actor` into network id, ownership, team, layer, relevance, and interest-position data. |
| `GameplayReplicationPolicy` | Carries visibility, channel, distance, tick interval, priority, layer, owner-inclusion, and authentication metadata. |
| `GameplayNetworkObserverRegistry` | Stores `NetworkInterestObserver` data by connection id. |
| `GameplayNetworkObserverResolver` | Filters caller-owned candidate connections by connection state, authentication, ownership, team, area, and relevance policy. |

## Composition And Session Workflow

The authority composition root owns the session object. `NetworkGameSessionAdapter` is not a `MonoBehaviour` and is not placed in a scene.

1. Construct `NetworkGameSessionAdapter(maxPlayers, maxSpectators)` in the authority composition root.
2. Call `SetMessageEndpoint` with the active `INetworkMessageEndpoint`. This attempts GameplayFramework catalog registration when the endpoint exposes an `INetworkMessageCatalog` through `INetworkRuntimeContextProvider`.
3. Pass the session into `GameInstance.StartWorldAsync(settings, authorityNetMode, gameSession: session, cancellationToken: cancellationToken)`.
4. `World.InitializeAsync` spawns and initializes the authoritative `GameMode` with that session. Host/local requests created by `GameMode` use `IsLocal = true` and do not require staged network connections. After `StartWorldAsync` returns, obtain the initialized instance from `world.GameMode`.
5. Authenticate and rate-limit the transport connection, assign a positive authoritative player id, then call `TryStageConnection(playerId, connection, out error)`.
6. Create `PlayerLoginRequest` with the same player id and connection address, then call `GameMode.LoginAsync`.
7. `TryRegisterPlayer` consumes the staged connection and binds the successful `PlayerController` automatically.
8. If login does not succeed, the composition root must call `RemoveStagedConnection(playerId, connection)`.
9. Use `GameMode.Logout`, `UnregisterPlayer`, `KickPlayer`, or `BanPlayer` on the World owner thread during teardown.

```csharp
using System;
using System.Threading;
using CycloneGames.GameplayFramework.Networking;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using Cysharp.Threading.Tasks;

public sealed class GameplayNetworkLoginBridge
{
    private readonly GameMode gameMode;
    private readonly NetworkGameSessionAdapter session;

    private GameplayNetworkLoginBridge(
        GameMode gameMode,
        NetworkGameSessionAdapter session)
    {
        this.gameMode = gameMode;
        this.session = session;
    }

    public static async UniTask<GameplayNetworkLoginBridge> StartAuthorityWorldAsync(
        GameInstance gameInstance,
        WorldSettings settings,
        WorldNetMode authorityNetMode,
        INetworkMessageEndpoint messageEndpoint,
        CancellationToken cancellationToken)
    {
        if (authorityNetMode == WorldNetMode.Client)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityNetMode), "An authority net mode is required.");
        }

        var session = new NetworkGameSessionAdapter(maxPlayers: 64, maxSpectators: 8);
        session.SetMessageEndpoint(messageEndpoint);

        World world = await gameInstance.StartWorldAsync(
            settings,
            authorityNetMode,
            gameSession: session,
            cancellationToken: cancellationToken);

        GameMode gameMode = world.GameMode
            ?? throw new InvalidOperationException("The authority World did not create a GameMode.");
        return new GameplayNetworkLoginBridge(gameMode, session);
    }

    public async UniTask<PlayerLoginResult> LoginAsync(
        INetConnection connection,
        int playerId,
        string playerName,
        string validatedOptions,
        CancellationToken cancellationToken)
    {
        if (connection == null || !connection.IsConnected || !connection.IsAuthenticated)
        {
            return PlayerLoginResult.Failure(PlayerLoginStatus.Rejected, "Connection is not ready.");
        }

        if (!session.TryStageConnection(playerId, connection, out string stageError))
        {
            return PlayerLoginResult.Failure(PlayerLoginStatus.Rejected, stageError);
        }

        try
        {
            var request = new PlayerLoginRequest(
                playerId,
                playerName,
                isSpectator: false,
                remoteAddress: connection.RemoteAddress,
                options: validatedOptions,
                isLocal: false);

            return await gameMode.LoginAsync(request, cancellationToken: cancellationToken);
        }
        finally
        {
            // Success consumes the staged entry. Failure and cancellation require cleanup.
            session.RemoveStagedConnection(playerId, connection);
        }
    }
}
```

`GameInstance.StartWorldAsync` owns World construction and the one-time `GameMode.Initialize` call. Do not initialize `world.GameMode` again after the World is returned. The start call and subsequent session operations must follow GameInstance/World owner-thread rules; a backend adapter must not invoke them directly from an arbitrary transport callback thread.

The local exception is two-part: `GameMode.CreateLocalPlayerLoginRequest` sets `IsLocal = true`, and the resulting controller is initialized with a `LocalPlayer`, making `PlayerController.IsLocalController` true. `NetworkGameSessionAdapter` skips staged admission for the trusted local request and permits the trusted local controller to register without a connection, allowing listen-server local players to initialize during `StartWorldAsync`.

`IsLocal` is a server/composition trust marker, not network input. A transport adapter must ignore any client-supplied local flag and construct every remote `PlayerLoginRequest` with `isLocal: false`. Setting it from an untrusted payload can bypass staged connection, authentication, and ban checks during `ApproveLogin`.

### PlayerLoginRequest Contract

| Field | Validation and ownership |
| --- | --- |
| `PlayerId` | The base request requires a non-negative value; `NetworkGameSessionAdapter` requires a positive value for staged network login. The authoritative composition root owns id assignment and collision handling. |
| `PlayerName` | Optional; maximum `64` UTF-16 code units. Sanitization, uniqueness, and moderation are project responsibilities. |
| `IsSpectator` | Selects the independently bounded spectator roster. |
| `RemoteAddress` | Optional; maximum `256` UTF-16 code units. Used by the adapter for in-memory address bans and address lookup. Must be null or empty when `IsLocal` is true. Do not place credentials in this field. |
| `Options` | Optional; maximum `1024` UTF-16 code units. Parsing and per-option validation are project responsibilities. |
| `IsLocal` | Defaults to `false`. Only authoritative in-process composition may set it to `true`; remote/network-derived requests must keep it `false`. |

`GameSession` defaults to `16` players and `4` spectators. Each capacity must be between `0` and `100,000`, and their sum cannot exceed `100,000`. `ApproveLogin` validates the request and capacity before the adapter applies its network-specific checks.

`TryStageConnection` bounds pending entries to `maxPlayers + maxSpectators`. It rejects non-positive ids, null connections, overlong or banned addresses, active duplicate player ids, a different connection already staged for the same id, the same connection already assigned to a different staged or active player id, and capacity overflow. The adapter enforces both directions of the one connection ↔ one PlayerId relationship across staged and active bindings. Repeating the same player-id/connection pair is idempotent. Removing a staged entry or unbinding a player releases the reverse connection index. Staging does not replace transport authentication or flood control.

`RejectUnknownAddresses`, `RejectDisconnectedConnections`, and `RejectUnauthenticatedConnections` all default to `true`. For a remote request, admission requires a staged entry for `PlayerLoginRequest.PlayerId`; a non-empty request address must match the staged address case-insensitively, and the staged connection must be connected and authenticated. The adapter snapshots the remote address at staging and revalidates connection identity, current address, connection state, authentication, and bans during approval and commit-time binding. Address mutation, disconnection, authentication loss, or a ban applied after staging therefore rejects admission. A trusted local request is also rejected when its PlayerId collides with a staged remote identity. `TryRegisterPlayer` rolls back roster registration if automatic binding fails or a remote controller lacks its staged entry. `TryBindConnection` supports explicit binding/rebinding outside the normal login flow; `BindConnection` is its throwing wrapper.

Staged entries, address bans, and player/connection indices are memory-only. `UnregisterPlayer` also unbinds the connection. `KickPlayer` requests disconnect, then uses `player.World.GameMode.Logout` when available so World, `GameState`, session roster, and spawned player objects are cleaned through the authoritative gameplay path; it falls back to `UnregisterPlayer` when no `GameMode` is available. `BanPlayer` requires a bound connection with a non-empty address, and new banned addresses are bounded by `MaxBannedAddresses` (`4096`) before the player is kicked. `BanAddress` additionally enforces the `PlayerLoginRequest` address-length limit.

Set the `INetworkMessageEndpoint` before staging or registering any connection. Repeating the same endpoint is idempotent, but replacing or clearing it while staged or active bindings exist throws; clear those bindings through their owning transport and gameplay teardown paths first.

## Actor Migration Contract

### Stable Prefab Definition Id

`CaptureMigrationState` requires an explicit `prefabDefinitionId`. This value must be deterministic across server/client builds, processes, save/restore boundaries, and content updates covered by the same protocol compatibility window.

- Own ids in a project-visible, version-controlled content registry, configuration asset, or generated table.
- Do not use `UnityEngine.Object.name`, runtime instance ids, transient scene hierarchy paths, or process-local handles as identity.
- Validate that the id resolves to the expected spawn definition before allocating or spawning the destination actor.
- Define project behavior for missing, retired, unauthorized, or incompatible definitions.

The version-1 DTO property is named `PrefabAssetPath`; it carries the explicit `prefabDefinitionId` supplied to `CaptureMigrationState`. The codec checks its byte length, but the project-owned registry is responsible for existence, authorization, and compatibility.

### Capture And Apply

```csharp
ActorMigrationState outbound = sourceActor.CaptureMigrationState(
    prefabDefinitionId: "actors.hero.v1",
    ownerConnectionId: ownerConnectionId,
    instigatorActorId: instigatorActorId);

writer.WriteMigrationState(outbound);

ActorMigrationState inbound = reader.ReadMigrationState();
Actor targetActor = SpawnAndRegisterFromDefinitionId(inbound.PrefabAssetPath); // Project-owned.
targetActor.ApplyMigrationState(inbound);
ResolveOwnershipAndInstigator(targetActor, inbound.OwnerConnectionId, inbound.InstigatorActorId); // Project-owned.
```

| Operation | Included behavior | Explicitly outside the operation |
| --- | --- | --- |
| `CaptureMigrationState` | Copies position, rotation, scale, remaining lifespan, damageability, hidden state, actor tags, actor name, `HasBegunPlay`, stable definition id, owner connection id, and instigator actor id. | Prefab lookup, authority validation, target spawning, connection lookup, and subclass-specific state. |
| `WriteMigrationState` | Validates the snapshot and writes the version-1 fields in fixed order. | Catalog envelope creation, total payload enforcement, transport delivery, compression, encryption, and retry policy. |
| `ReadMigrationState` | Bounds tag allocation and string lengths, rejects non-finite numeric data, reconstructs the DTO, and validates the snapshot. | Definition authorization, semantic world bounds, owner/instigator resolution, and spawn budgeting. |
| `ApplyMigrationState` | Applies transform, scale, damageability, tags, hidden state, non-negative lifespan, and a non-empty actor name to an already spawned and registered actor. | Spawning/registering the actor, assigning owner/instigator, invoking or replaying `BeginPlay`, or applying `HasBegunPlay` as lifecycle state. |

`CaptureMigrationState` allocates a tag array when the actor has tags. Treat capture as a migration event operation, not a per-frame replication path.

## Protocol And Version-1 Wire Layout

`GameplayFrameworkNetworkProtocol` owns message IDs `11000-11999` inside the shared `NetworkMessageRanges.Module` range (`1000-29999`). Its current and minimum supported protocol versions are both `1`. `CreateProtocolManifest` builds the complete manifest, and `RegisterMessageCatalog` / `TryRegisterMessageCatalog` submit it through `TryRegisterProtocolManifest`. Registration either commits the range and every descriptor together or rejects the manifest without a partial catalog update.

| Message | ID | Default channel | Catalog payload limit | Purpose |
| --- | ---: | --- | --- | --- |
| `MsgActorMigrationState` | `11000` | Reliable | `NetworkConstants.DefaultMaxPayloadSize * 4` | Version-1 actor migration state. |
| `MsgDamageRequest` | `11001` | Reliable | `49` bytes | Untrusted client damage intent for server validation. |
| `MsgDamageResult` | `11002` | Reliable | `30` bytes | Server-authoritative damage result. |

Register the module protocol from the composition root before traffic is accepted:

```csharp
using CycloneGames.GameplayFramework.Networking;
using CycloneGames.Networking;

public static class GameplayFrameworkNetworkInstaller
{
    public static void Configure(INetworkMessageCatalog catalog)
    {
        GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
    }
}
```

`ActorMigrationState.SchemaVersion` is `1`. The payload does not contain an embedded schema-version byte; compatibility is established by the protocol manifest/catalog and its explicit contract identity. Primitive numeric fields are little-endian. Floats use their 32-bit IEEE 754 bit representation. Strings are a little-endian `ushort` UTF-8 byte count followed by exactly that many bytes. Null strings are encoded as empty strings. Fields are serialized individually with no struct padding.

Every descriptor declares an explicit printable-ASCII `ContractId`: `ActorMigrationState:v1`, `DamageRequestMessage:v1`, or `DamageResultMessage:v1`. Its nonzero `SchemaHash` is the FNV-1a 64-bit hash of that exact identifier; manifest validation rejects a mismatch. The protocol fingerprint includes the range and each descriptor's message ID, contract identity, schema hash, channel, and payload limit. Damage messages additionally expose `DamageWireSchemaFingerprint`, derived from canonical field-offset, width, endian, payload-size, and `ServerDamageRejectReason` value descriptors. `DamageRequestMessage` is exactly 49 bytes and `DamageResultMessage` is exactly 30 bytes; these exact limits are registered in the manifest. `ServerDamageRejectReason.Unknown` is 0, `Accepted` is 1, and all rejection codes are nonzero, so default-initialized validation and wire messages fail closed. CLR type names, reflection, field inspection, and `SchemaVersion` are not automatic protocol identity. Any field-order, encoding, enum assignment, or semantic compatibility change must update the canonical descriptor and fixed byte fixture; every communicating peer must deploy matching protocol descriptors and fingerprints. Incompatible peers must be rejected before gameplay traffic. Project messages belong in a separate project-owned manifest using an unclaimed `NetworkMessageRanges.User` subrange; this module exposes no dynamic descriptor-registration facade.

| Order | Field | Encoding | Version-1 validation |
| ---: | --- | --- | --- |
| 1 | `Position.x/y/z` | 3 × `float32` | Every component must be finite. |
| 2 | `Rotation.x/y/z/w` | 4 × `float32` | Every component must be finite; normalization is not enforced by the codec. |
| 3 | `Scale.x/y/z` | 3 × `float32` | Every component must be finite; semantic scale bounds are project-owned. |
| 4 | `PrefabAssetPath` | `ushort byteCount` + UTF-8 bytes | Carries `prefabDefinitionId`; maximum `1024` UTF-8 bytes. |
| 5 | `RemainingLifeSpan` | `float32` | Must be finite and non-negative. |
| 6 | `CanBeDamaged`, `Hidden`, `HasBegunPlay` | 3 × `byte`, in that order | Writer emits `0` or `1`; reader treats nonzero as `true`. |
| 7 | `Tags` | `ushort tagCount`; each tag is `ushort byteCount` + UTF-8 bytes | Codec runtime count maximum `64`; each encoded tag maximum `384` UTF-8 bytes. Applying to an `Actor` additionally requires a nonblank tag of at most `128` UTF-16 code units. |
| 8 | `OwnerConnectionId` | `int32` | Resolution and authorization are project-owned. |
| 9 | `InstigatorActorId` | `int32` | Resolution and authorization are project-owned. |
| 10 | `ActorName` | `ushort byteCount` + UTF-8 bytes | Maximum `256` UTF-8 bytes. Empty leaves the target name unchanged during apply. |

The public codec constants are `MaxPrefabDefinitionIdUtf8Bytes = 1024`, `MaxActorNameUtf8Bytes = 256`, `MaxTagUtf8Bytes = Actor.MaxActorTagLength * 3`, and `DefaultMaxRuntimeTagCount = Actor.MaxActorTags`.

The minimum payload is `61` bytes. For `P` prefab-id bytes, `N` actor-name bytes, and encoded tags `Ti`, the payload size is:

```text
61 + P + N + sum(2 + Ti)
```

The snapshot must satisfy both the per-field limits and the message descriptor's total payload limit. `WriteMigrationState` enforces the per-field rules; catalog/transport validation owns the descriptor-level total.

`ReadMigrationState(maxRuntimeTagCount)` applies the smaller of the positive caller limit and `Actor.MaxActorTags` (`64`). A zero or negative argument selects the default runtime limit. Length prefixes are checked before tag arrays or large scratch buffers are allocated. The codec also rejects non-finite transform data and invalid lifespan values. `ApplyMigrationState` calls `Actor.ReplaceTags`, which validates every inbound tag before mutating the target tag set. Projects must add semantic validation for world bounds, allowed definitions, allowed tags, ownership, and resource budgets before spawning or applying untrusted state.

## Authority And Observer Boundaries

### Authority

`ServerAuthoritativeGameplayAuthorityResolver` provides role and permission decisions; it does not send packets, apply state, or prevent a caller from bypassing the result.

| Context | Role and permission |
| --- | --- |
| Invalid actor (`NetworkId == 0` or non-finite interest position) | `None`; no authoritative state write and no owner input. |
| Server, including a host that is also a client | `ServerAuthority`; may write authoritative state for a valid actor. |
| Client whose local connection id equals `OwnerConnectionId` | `AutonomousProxy`; may send owner input. |
| Other valid client | `SimulatedProxy`; cannot send owner input through this policy. |

The authoritative replication loop must call `CanWriteAuthoritativeState` before producing state. The input bridge must call `CanSendOwnerInput` and the server must still validate every received command. Migration owner/instigator ids and damage request fields are untrusted identifiers, not authority proof.

### Observer Resolution

`GameplayNetworkObserverResolver.ResolveObservers` clears the caller-provided result list and adds eligible entries from the caller-provided candidate list. Reuse both lists to avoid avoidable allocations.

1. Null, disconnected, and—when `RequireAuthenticated` is true—unauthenticated candidates are rejected.
2. An invalid target or `Visibility.None` returns an empty result.
3. `AlwaysRelevant` actors and `Visibility.All` include every remaining candidate, including the owner.
4. For other visibility modes, the owner is included only when `IncludeOwner` is true or visibility is `OwnerOnly`.
5. `Team` requires a nonzero actor team and matching observer team data.
6. `Area` requires observer data, a positive policy `MaxDistance`, overlapping policy/actor/observer layer masks, and distance within the policy radius.
7. `TeamOrArea` accepts either the team or area condition.

The registry's `NetworkInterestObserver.Radius` is stored but is not consumed by the area resolver; area selection uses `GameplayReplicationPolicy.MaxDistance`. `Channel`, `MinTickInterval`, and `Priority` are scheduling metadata for the caller's replication pipeline and are not enforced by the observer resolver. The resolver selects observers only; it does not establish authority, serialize payloads, send packets, or own connection lifecycle.

## Threading, Performance, And Platform Notes

- `GameSession`, `NetworkGameSessionAdapter`, and `GameplayNetworkObserverRegistry` are owner-thread objects and are not thread-safe. Invoke them on the owning World/replication thread. Marshal transport callbacks before calling GameplayFramework or Unity APIs.
- `CaptureMigrationState` and `ApplyMigrationState` access `Actor`, `Transform`, `GameObject`, and other Unity-facing state and therefore belong on the Unity/World owner thread.
- `WriteMigrationState` and `ReadMigrationState` do not access Unity objects. They may run on a worker only when the DTO, reader/writer, buffer, and result ownership are exclusive and the surrounding networking implementation permits it.
- Migration capture allocates a tag array for non-empty tags. Read allocates decoded strings and a bounded tag array. Short UTF-8 scratch data uses `stackalloc`; larger scratch data rents from `ArrayPool<byte>`.
- Reuse preallocated candidate/result collections and pre-size the registry to avoid collection growth during observer resolution.
- The Core asmdef has no platform exclusions and directly uses Unity types. It is suitable for Unity Player and Unity headless/server compositions, not a Unity-free .NET assembly.
- The package does not use reflection-based discovery, runtime code generation, or direct native plugins. Validate IL2CPP, managed stripping, AOT, Burst, architecture, and target-platform behavior with target Player builds and runtime tests.
- Protocol registration must execute in every runtime composition. Do not rely on Editor-only discovery or a type being present in a namespace to preserve it.

## Extension Points

- Implement `IGameplayNetworkAuthorityResolver` for project authority rules.
- Implement `IGameplayNetworkObserverSource` when observer data lives outside `GameplayNetworkObserverRegistry`.
- Implement `IGameplayNetworkObserverResolver` for a different interest policy while preserving connection/authentication and allocation budgets.
- Derive from `NetworkGameSessionAdapter` for project admission rules, while calling the base bounded validation where applicable.
- Add project-owned messages through a separate project-owned manifest in an unclaimed `NetworkMessageRanges.User` subrange; do not consume another module's ID range.
- Keep Mirror, Mirage, Nakama, Photon, Steam, platform-service, and dedicated-server transport SDK code in separate backend adapters.

## Persistence

This package writes no files, assets, preferences, caches, or save data.

| State | Owner and lifetime | Cleanup and version control |
| --- | --- | --- |
| Staged connections, player/connection maps, and banned addresses | `NetworkGameSessionAdapter`; memory-only for the adapter lifetime. Staging is bounded by participant capacity and bans by `4096`. | Remove failed/cancelled staged logins, unbind/unregister players, and discard the adapter. Nothing is committed to Git. |
| Observer records | `GameplayNetworkObserverRegistry`; memory-only for the registry lifetime. | Call `Remove`/`Clear` or discard the registry. Nothing is committed to Git. |
| Migration DTO and encoded payload | Caller/network buffer; transient transfer state. | Release or return buffers according to the networking buffer owner. Nothing is committed to Git. |
| Stable prefab definition registry | Project-owned and outside this package. | Document its path, schema version, migration policy, Git ownership, and safe retirement process in the owning project/module. |

## Validation

Run these EditMode suites after changing this package or its contracts:

```text
Unity Test Runner > EditMode > CycloneGames.GameplayFramework.Networking.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.GameplayFramework.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.Networking.Tests.Editor
```

The Networking package tests cover migration round-trip and bounds, explicit definition-id capture/apply, authority roles, owner/team/area observer selection, protocol range/catalog registration, staged session admission, disconnected/unauthenticated rejection, connection reuse, post-stage bans, and server damage validation. The base GameplayFramework tests cover `GameSession` roster/capacity, local-controller initialization, and authoritative World lifecycle behavior. The Networking Core tests cover shared buffers, message catalogs, security validation, and replication infrastructure.

For batchmode-capable environments, run the package suite with the Unity version recorded in `UnityStarter/ProjectSettings/ProjectVersion.txt`:

```text
<Unity-editor-executable> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -assemblyNames CycloneGames.GameplayFramework.Networking.Tests.Editor -testResults <test-results-path> -quit
```

Before release, also perform the following integration checks:

1. Register the same protocol manifest on client and server; verify ids `11000`, `11001`, and `11002`, version `1`, and matching protocol fingerprints.
2. Verify message `11000` against a fixed version-1 byte fixture, including empty strings/tags and maximum approved project values.
3. Exercise listen-server local-player bootstrap without staging, force `IsLocal = false` for all network-derived requests, and test authenticated staging, one connection ↔ one PlayerId rejection, staged-capacity rejection, post-stage bans, failed-login cleanup, automatic binding, spectator/player capacity, duplicate registration, disconnect, authoritative logout, kick, the `4096`-address ban bound, and unban through the real backend adapter.
4. Test owner, non-owner, host, dedicated-server, unauthenticated, team, area, layer-mask, and always-relevant observer cases.
5. Run a clean target Player build and runtime smoke test for every supported backend/platform combination, including IL2CPP/AOT and managed stripping where applicable.
6. Use the Unity Profiler or platform tooling to measure migration allocations, observer selection cost, payload sizes, and transport-thread marshaling under production-scale loads.
