using System;
using System.Collections.Generic;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Bridge between CycloneGames.GameplayAbilities and CycloneGames.Networking.
    /// 
    /// Handles:
    /// 1. Ability activation RPC (client request ->server auth ->client confirm/reject)
    /// 2. Effect replication (apply/remove/stack change)
    /// 3. Attribute dirty sync (delta or full)
    /// 4. Tag replication
    /// 5. Initial state data for late-join / reconnect
    /// 6. Prediction key lifecycle
    /// 
    /// Designed to work with any transport through INetworkManager.
    /// </summary>
    public sealed class NetworkedAbilityBridge : IDisposable
    {
        public const string MessageOwner = "CycloneGames.GameplayAbilities.Networking";

        public const ushort MESSAGE_ID_BASE = 10000;
        public const ushort MESSAGE_ID_MAX = 10999;
        public const ushort MsgAbilityActivateRequest = MESSAGE_ID_BASE + 0;
        public const ushort MsgAbilityActivateConfirm = MESSAGE_ID_BASE + 1;
        public const ushort MsgAbilityActivateReject = MESSAGE_ID_BASE + 2;
        public const ushort MsgAbilityEnd = MESSAGE_ID_BASE + 3;
        public const ushort MsgAbilityCancel = MESSAGE_ID_BASE + 4;
        public const ushort MsgEffectApplied = MESSAGE_ID_BASE + 10;
        public const ushort MsgEffectRemoved = MESSAGE_ID_BASE + 11;
        public const ushort MsgEffectStackChanged = MESSAGE_ID_BASE + 12;
        public const ushort MsgEffectUpdated = MESSAGE_ID_BASE + 13;
        public const ushort MsgAttributeUpdate = MESSAGE_ID_BASE + 20;
        public const ushort MsgTagUpdate = MESSAGE_ID_BASE + 25;
        public const ushort MsgAbilityMulticast = MESSAGE_ID_BASE + 30;
        public const ushort MsgFullState = MESSAGE_ID_BASE + 40;
        public const ushort MsgFullStateRequest = MESSAGE_ID_BASE + 41;
        public const ushort MsgStateSyncMetadata = MESSAGE_ID_BASE + 42;
        public const ushort MsgManifestHandshake = MESSAGE_ID_BASE + 50;

        /// <summary>
        /// Current wire protocol version for GameplayAbilities networking messages.
        /// This value is written into the protocol manifest and therefore participates in the
        /// protocol fingerprint and connection-time handshake compatibility checks.
        /// It is not part of gameplay state checksums, attribute math, prediction key math, or
        /// any combat simulation calculation.
        /// </summary>
        public const byte PROTOCOL_VERSION = 1;

        /// <summary>
        /// Oldest GameplayAbilities networking wire protocol version accepted by this package.
        /// Keep this equal to <see cref="PROTOCOL_VERSION"/> until a publicly shipped protocol
        /// version must remain compatible with a newer implementation.
        /// </summary>
        public const byte MIN_SUPPORTED_PROTOCOL_VERSION = 1;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsSupportedProtocolVersion(byte protocolVersion)
        {
            return Module.IsSupportedProtocolVersion(protocolVersion);
        }

        private readonly INetworkManager _networkManager;
        private readonly GASNetworkSerializerOptions _serializerOptions;
        private readonly Dictionary<int, INetworkedASC> _ascByConnectionId =
            new Dictionary<int, INetworkedASC>(32);
        private readonly Dictionary<uint, INetworkedASC> _ascByNetworkId =
            new Dictionary<uint, INetworkedASC>(64);

        public INetworkManager NetworkManager => _networkManager;
        public GASNetworkSerializerOptions SerializerOptions => _serializerOptions.Clone();

        /// <summary>
        /// Optional server-side authorization callback for full-state requests.
        /// Return true to allow the sender to receive the target ASC data.
        /// Default policy (when null): owner-only access.
        /// </summary>
        public Func<INetConnection, uint, bool> FullStateRequestAuthorizer { get; set; }

        // --- Events for game layer to subscribe ---
        public event Action<int, AbilityActivateRequest> OnAbilityActivateRequested;
        public event Action<uint, EffectReplicationData> OnEffectApplied;
        public event Action<uint, int> OnEffectRemoved; // (networkId, effectInstanceId)
        public event Action<uint, EffectUpdateData> OnEffectUpdated;
        public event Action<uint, AttributeUpdateData> OnAttributeUpdated;
        public event Action<uint, GASFullStateData> OnFullStateReceived;
        public event Action<uint, GASStateSyncMetadata> OnStateSyncMetadataReceived;

        public NetworkedAbilityBridge(INetworkManager networkManager)
            : this(networkManager, null, true)
        {
        }

        public NetworkedAbilityBridge(
            INetworkManager networkManager,
            GASNetworkSerializerOptions serializerOptions,
            bool installSerializer = true)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _serializerOptions = (serializerOptions ?? GASNetworkSerializerOptions.Default).Clone();
            _serializerOptions.Validate();

            if (installSerializer)
                TryInstallSerializer(networkManager, _serializerOptions);

            TryRegisterMessageCatalog(networkManager);
        }

        public static bool TryInstallSerializer(
            INetworkManager networkManager,
            GASNetworkSerializerOptions serializerOptions = null)
        {
            if (networkManager == null)
                throw new ArgumentNullException(nameof(networkManager));

            if (networkManager is not INetworkSerializerConfigurable configurable || networkManager.Serializer == null)
                return false;

            configurable.SetSerializer(GASNetworkSerializer.Wrap(networkManager.Serializer, serializerOptions));
            return true;
        }

        public static bool TryRegisterMessageCatalog(INetworkManager networkManager)
        {
            return Module.TryRegister(networkManager);
        }

        public static void RegisterMessageCatalog(INetworkMessageCatalog catalog)
        {
            Module.Register(catalog);
        }

        public static NetworkProtocolManifest CreateProtocolManifest()
        {
            var builder = new NetworkProtocolManifestBuilder(
                MessageOwner,
                MESSAGE_ID_BASE,
                MESSAGE_ID_MAX,
                NetworkMessageKind.Module)
            {
                ProtocolId = MessageOwner,
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "GameplayAbilities")
                .AddMessage<GASManifestHandshakeMessage>(MsgManifestHandshake, NetworkChannel.Reliable, 32)
                .AddMessage<AbilityActivateRequest>(MsgAbilityActivateRequest, NetworkChannel.Reliable)
                .AddMessage<AbilityActivateConfirm>(MsgAbilityActivateConfirm, NetworkChannel.Reliable)
                .AddMessage<AbilityActivateReject>(MsgAbilityActivateReject, NetworkChannel.Reliable)
                .AddMessage<AbilityEndMessage>(MsgAbilityEnd, NetworkChannel.Reliable)
                .AddMessage<AbilityCancelMessage>(MsgAbilityCancel, NetworkChannel.Reliable)
                .AddMessage<EffectReplicationData>(MsgEffectApplied, NetworkChannel.Reliable)
                .AddMessage<EffectRemoveData>(MsgEffectRemoved, NetworkChannel.Reliable)
                .AddMessage<EffectStackChangeData>(MsgEffectStackChanged, NetworkChannel.Reliable)
                .AddMessage<EffectUpdateData>(MsgEffectUpdated, NetworkChannel.Reliable)
                .AddMessage<AttributeUpdateData>(MsgAttributeUpdate, NetworkChannel.UnreliableSequenced)
                .AddMessage<TagUpdateData>(MsgTagUpdate, NetworkChannel.Reliable)
                .AddMessage<AbilityMulticastData>(MsgAbilityMulticast, NetworkChannel.Reliable)
                .AddMessage<GASFullStateData>(MsgFullState, NetworkChannel.Reliable)
                .AddMessage<FullStateRequest>(MsgFullStateRequest, NetworkChannel.Reliable)
                .AddMessage<GASStateSyncMetadata>(MsgStateSyncMetadata, NetworkChannel.Reliable);

            return builder.Build();
        }

        /// <summary>
        /// Register all message handlers. Call once during initialization.
        /// </summary>
        public void RegisterHandlers()
        {
            _networkManager.RegisterHandler<AbilityActivateRequest>(MsgAbilityActivateRequest, OnRecvActivateRequest);
            _networkManager.RegisterHandler<AbilityActivateConfirm>(MsgAbilityActivateConfirm, OnRecvActivateConfirm);
            _networkManager.RegisterHandler<AbilityActivateReject>(MsgAbilityActivateReject, OnRecvActivateReject);
            _networkManager.RegisterHandler<AbilityEndMessage>(MsgAbilityEnd, OnRecvAbilityEnd);
            _networkManager.RegisterHandler<AbilityCancelMessage>(MsgAbilityCancel, OnRecvAbilityCancel);
            _networkManager.RegisterHandler<EffectReplicationData>(MsgEffectApplied, OnRecvEffectApplied);
            _networkManager.RegisterHandler<EffectRemoveData>(MsgEffectRemoved, OnRecvEffectRemoved);
            _networkManager.RegisterHandler<EffectStackChangeData>(MsgEffectStackChanged, OnRecvEffectStackChanged);
            _networkManager.RegisterHandler<EffectUpdateData>(MsgEffectUpdated, OnRecvEffectUpdated);
            _networkManager.RegisterHandler<AttributeUpdateData>(MsgAttributeUpdate, OnRecvAttributeUpdate);
            _networkManager.RegisterHandler<TagUpdateData>(MsgTagUpdate, OnRecvTagUpdate);
            _networkManager.RegisterHandler<AbilityMulticastData>(MsgAbilityMulticast, OnRecvAbilityMulticast);
            _networkManager.RegisterHandler<GASFullStateData>(MsgFullState, OnRecvFullState);
            _networkManager.RegisterHandler<FullStateRequest>(MsgFullStateRequest, OnRecvFullStateRequest);
            _networkManager.RegisterHandler<GASStateSyncMetadata>(MsgStateSyncMetadata, OnRecvStateSyncMetadata);
        }

        /// <summary>
        /// Unregister all message handlers. Call during teardown or when disposing.
        /// </summary>
        public void UnregisterHandlers()
        {
            _networkManager.UnregisterHandler(MsgAbilityActivateRequest);
            _networkManager.UnregisterHandler(MsgAbilityActivateConfirm);
            _networkManager.UnregisterHandler(MsgAbilityActivateReject);
            _networkManager.UnregisterHandler(MsgAbilityEnd);
            _networkManager.UnregisterHandler(MsgAbilityCancel);
            _networkManager.UnregisterHandler(MsgEffectApplied);
            _networkManager.UnregisterHandler(MsgEffectRemoved);
            _networkManager.UnregisterHandler(MsgEffectStackChanged);
            _networkManager.UnregisterHandler(MsgEffectUpdated);
            _networkManager.UnregisterHandler(MsgAttributeUpdate);
            _networkManager.UnregisterHandler(MsgTagUpdate);
            _networkManager.UnregisterHandler(MsgAbilityMulticast);
            _networkManager.UnregisterHandler(MsgFullState);
            _networkManager.UnregisterHandler(MsgFullStateRequest);
            _networkManager.UnregisterHandler(MsgStateSyncMetadata);
        }

        public void Dispose()
        {
            UnregisterHandlers();
        }

        /// <summary>
        /// Register an ASC as a networked entity.
        /// </summary>
        public void RegisterASC(uint networkId, int ownerConnectionId, INetworkedASC asc)
        {
            _ascByNetworkId[networkId] = asc;
            _ascByConnectionId[ownerConnectionId] = asc;
        }

        public void UnregisterASC(uint networkId, int ownerConnectionId)
        {
            _ascByNetworkId.Remove(networkId);
            _ascByConnectionId.Remove(ownerConnectionId);
        }

        // =====================================================
        // CLIENT ->SERVER: Ability Activation
        // =====================================================

        /// <summary>
        /// Client calls this to request ability activation from the server.
        /// (LocalPredicted or ServerOnly execution policy)
        /// </summary>
        public void ClientRequestActivateAbility(int abilityDefinitionId, int predictionKey,
            NetworkVector3 targetPos, NetworkVector3 direction, uint targetNetworkId = 0)
        {
            ClientRequestActivateAbility(
                abilityDefinitionId,
                0,
                predictionKey,
                0,
                0,
                targetPos,
                direction,
                targetNetworkId);
        }

        public void ClientRequestActivateAbility(
            int abilityDefinitionId,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence,
            NetworkVector3 targetPos,
            NetworkVector3 direction,
            uint targetNetworkId = 0)
        {
            ClientRequestActivateAbility(
                abilityDefinitionId,
                0,
                predictionKey,
                predictionKeyOwner,
                predictionInputSequence,
                targetPos,
                direction,
                targetNetworkId);
        }

        public void ClientRequestActivateAbility(
            int abilityDefinitionId,
            int abilitySpecHandle,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence,
            NetworkVector3 targetPos,
            NetworkVector3 direction,
            uint targetNetworkId = 0)
        {
            _networkManager.SendToServer(MsgAbilityActivateRequest, new AbilityActivateRequest
            {
                AbilityDefinitionId = abilityDefinitionId,
                AbilitySpecHandle = abilitySpecHandle,
                PredictionKey = predictionKey,
                PredictionKeyOwner = predictionKeyOwner,
                PredictionInputSequence = predictionInputSequence,
                TargetPosition = targetPos,
                Direction = direction,
                TargetNetworkId = targetNetworkId
            });
        }

        /// <summary>
        /// Client notifies server that a locally-ended ability has completed.
        /// </summary>
        public void ClientNotifyAbilityEnd(int abilityDefinitionId, bool wasCancelled)
        {
            ClientNotifyAbilityEnd(abilityDefinitionId, 0, wasCancelled);
        }

        public void ClientNotifyAbilityEnd(int abilityDefinitionId, int abilitySpecHandle, bool wasCancelled)
        {
            if (wasCancelled)
            {
                _networkManager.SendToServer(MsgAbilityCancel, new AbilityCancelMessage
                {
                    AbilityDefinitionId = abilityDefinitionId,
                    AbilitySpecHandle = abilitySpecHandle
                });
                return;
            }

            _networkManager.SendToServer(MsgAbilityEnd, new AbilityEndMessage
            {
                AbilityDefinitionId = abilityDefinitionId,
                AbilitySpecHandle = abilitySpecHandle
            });
        }

        // =====================================================
        // SERVER ->CLIENT: Ability Confirmation/Rejection
        // =====================================================

        /// <summary>
        /// Server confirms predicted ability activation.
        /// Client removes pending prediction and keeps running effects.
        /// </summary>
        public void ServerConfirmActivation(INetConnection client, int abilityDefinitionId, int predictionKey)
        {
            ServerConfirmActivation(client, abilityDefinitionId, 0, predictionKey, 0, 0);
        }

        public void ServerConfirmActivation(
            INetConnection client,
            int abilityDefinitionId,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence)
        {
            ServerConfirmActivation(
                client,
                abilityDefinitionId,
                0,
                predictionKey,
                predictionKeyOwner,
                predictionInputSequence);
        }

        public void ServerConfirmActivation(
            INetConnection client,
            int abilityDefinitionId,
            int abilitySpecHandle,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence)
        {
            _networkManager.SendToClient(client, MsgAbilityActivateConfirm, new AbilityActivateConfirm
            {
                AbilityDefinitionId = abilityDefinitionId,
                AbilitySpecHandle = abilitySpecHandle,
                PredictionKey = predictionKey,
                PredictionKeyOwner = predictionKeyOwner,
                PredictionInputSequence = predictionInputSequence
            });
        }

        /// <summary>
        /// Server rejects predicted ability activation.
        /// Client rolls back all effects tagged with this prediction key.
        /// </summary>
        public void ServerRejectActivation(INetConnection client, int abilityDefinitionId, int predictionKey)
        {
            ServerRejectActivation(client, abilityDefinitionId, 0, predictionKey, 0, 0);
        }

        public void ServerRejectActivation(
            INetConnection client,
            int abilityDefinitionId,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence)
        {
            ServerRejectActivation(
                client,
                abilityDefinitionId,
                0,
                predictionKey,
                predictionKeyOwner,
                predictionInputSequence);
        }

        public void ServerRejectActivation(
            INetConnection client,
            int abilityDefinitionId,
            int abilitySpecHandle,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence)
        {
            _networkManager.SendToClient(client, MsgAbilityActivateReject, new AbilityActivateReject
            {
                AbilityDefinitionId = abilityDefinitionId,
                AbilitySpecHandle = abilitySpecHandle,
                PredictionKey = predictionKey,
                PredictionKeyOwner = predictionKeyOwner,
                PredictionInputSequence = predictionInputSequence
            });
        }

        // =====================================================
        // SERVER ->CLIENTS: Effect Replication
        // =====================================================

        /// <summary>
        /// Server broadcasts that an effect was applied to a target.
        /// </summary>
        public void ServerReplicateEffectApplied(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, EffectReplicationData data)
        {
            _networkManager.Broadcast(observers, MsgEffectApplied, data);
        }

        /// <summary>
        /// Server broadcasts that an effect was removed from a target.
        /// </summary>
        public void ServerReplicateEffectRemoved(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, int effectInstanceId)
        {
            _networkManager.Broadcast(observers, MsgEffectRemoved, new EffectRemoveData
            {
                TargetNetworkId = targetNetworkId,
                EffectInstanceId = effectInstanceId
            });
        }

        /// <summary>
        /// Server broadcasts effect stack count change.
        /// </summary>
        public void ServerReplicateStackChange(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, int effectInstanceId, int newStackCount)
        {
            _networkManager.Broadcast(observers, MsgEffectStackChanged, new EffectStackChangeData
            {
                TargetNetworkId = targetNetworkId,
                EffectInstanceId = effectInstanceId,
                NewStackCount = newStackCount
            });
        }

        /// <summary>
        /// Server broadcasts an in-place update for an existing effect instance.
        /// Use this when fields other than stack count changed.
        /// </summary>
        public void ServerReplicateEffectUpdated(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, EffectUpdateData data)
        {
            _networkManager.Broadcast(observers, MsgEffectUpdated, data);
        }

        // =====================================================
        // SERVER ->CLIENTS: Attribute Sync
        // =====================================================

        /// <summary>
        /// Server sends attribute updates. Can be delta (only changed) or full.
        /// </summary>
        public void ServerSyncAttributes(INetConnection client, uint targetNetworkId,
            AttributeUpdateData data)
        {
            _networkManager.SendToClient(client, MsgAttributeUpdate, data);
        }

        public void ServerBroadcastAttributes(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, AttributeUpdateData data)
        {
            _networkManager.Broadcast(observers, MsgAttributeUpdate, data);
        }

        // =====================================================
        // SERVER ->CLIENTS: Tag Sync
        // =====================================================

        public void ServerSyncTags(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, TagUpdateData data)
        {
            _networkManager.Broadcast(observers, MsgTagUpdate, data);
        }

        // =====================================================
        // SERVER ->ALL OBSERVERS: Ability Multicast (VFX/SFX)
        // =====================================================

        /// <summary>
        /// Multicast ability execution visual/audio to all observers.
        /// Can use unreliable channel for cosmetic-only events.
        /// </summary>
        public void ServerMulticastAbility(IReadOnlyList<INetConnection> observers,
            AbilityMulticastData data)
        {
            _networkManager.Broadcast(observers, MsgAbilityMulticast, data);
        }

        // =====================================================
        // FULL STATE SYNC (Join / Reconnect)
        // =====================================================

        /// <summary>
        /// Client requests full ASC state (on join or reconnect).
        /// </summary>
        public void ClientRequestFullState(uint targetNetworkId)
        {
            _networkManager.SendToServer(MsgFullStateRequest, new FullStateRequest
            {
                TargetNetworkId = targetNetworkId
            });
        }

        /// <summary>
        /// Server sends full ASC state to a client.
        /// </summary>
        public void ServerSendFullState(INetConnection client, GASFullStateData data)
        {
            _networkManager.SendToClient(client, MsgFullState, data);
        }

        /// <summary>
        /// Server sends the authoritative state version/checksum after all per-frame deltas.
        /// Clients use this as a cheap drift detector and request full state when validation fails.
        /// </summary>
        public void ServerBroadcastStateSyncMetadata(IReadOnlyList<INetConnection> observers, GASStateSyncMetadata metadata)
        {
            _networkManager.Broadcast(observers, MsgStateSyncMetadata, metadata);
        }

        // =====================================================
        // Message Handlers
        // =====================================================

        private void OnRecvActivateRequest(INetConnection sender, AbilityActivateRequest msg)
        {
            OnAbilityActivateRequested?.Invoke(sender.ConnectionId, msg);
        }

        private void OnRecvActivateConfirm(INetConnection sender, AbilityActivateConfirm msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnServerConfirmActivation(
                    msg.AbilityDefinitionId,
                    msg.AbilitySpecHandle,
                    msg.PredictionKey,
                    msg.PredictionKeyOwner,
                    msg.PredictionInputSequence);
        }

        private void OnRecvActivateReject(INetConnection sender, AbilityActivateReject msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnServerRejectActivation(
                    msg.AbilityDefinitionId,
                    msg.AbilitySpecHandle,
                    msg.PredictionKey,
                    msg.PredictionKeyOwner,
                    msg.PredictionInputSequence);
        }

        private void OnRecvAbilityEnd(INetConnection sender, AbilityEndMessage msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnAbilityEnded(msg.AbilityDefinitionId, msg.AbilitySpecHandle);
        }

        private void OnRecvAbilityCancel(INetConnection sender, AbilityCancelMessage msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnAbilityCancelled(msg.AbilityDefinitionId, msg.AbilitySpecHandle);
        }

        private void OnRecvEffectApplied(INetConnection sender, EffectReplicationData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnReplicatedEffectApplied(msg);
            OnEffectApplied?.Invoke(msg.TargetNetworkId, msg);
        }

        private void OnRecvEffectRemoved(INetConnection sender, EffectRemoveData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnReplicatedEffectRemoved(msg.EffectInstanceId);
            OnEffectRemoved?.Invoke(msg.TargetNetworkId, msg.EffectInstanceId);
        }

        private void OnRecvEffectStackChanged(INetConnection sender, EffectStackChangeData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnReplicatedStackChanged(msg.EffectInstanceId, msg.NewStackCount);
        }

        private void OnRecvEffectUpdated(INetConnection sender, EffectUpdateData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnReplicatedEffectUpdated(msg);
            OnEffectUpdated?.Invoke(msg.TargetNetworkId, msg);
        }

        private void OnRecvAttributeUpdate(INetConnection sender, AttributeUpdateData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnReplicatedAttributeUpdate(msg);
            OnAttributeUpdated?.Invoke(msg.TargetNetworkId, msg);
        }

        private void OnRecvTagUpdate(INetConnection sender, TagUpdateData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnReplicatedTagUpdate(msg);
        }

        private void OnRecvAbilityMulticast(INetConnection sender, AbilityMulticastData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.SourceNetworkId, out var asc))
                asc.OnAbilityMulticast(msg);
        }

        private void OnRecvFullState(INetConnection sender, GASFullStateData msg)
        {
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                asc.OnFullState(msg);
            OnFullStateReceived?.Invoke(msg.TargetNetworkId, msg);
        }

        private void OnRecvStateSyncMetadata(INetConnection sender, GASStateSyncMetadata msg)
        {
            bool isConsistent = true;
            if (_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
            {
                isConsistent = asc.OnStateSyncMetadata(msg);
            }

            OnStateSyncMetadataReceived?.Invoke(msg.TargetNetworkId, msg);

            if (!isConsistent)
            {
                ClientRequestFullState(msg.TargetNetworkId);
            }
        }

        private void OnRecvFullStateRequest(INetConnection sender, FullStateRequest msg)
        {
            if (!_ascByNetworkId.TryGetValue(msg.TargetNetworkId, out var asc))
                return;

            bool isAuthorized = FullStateRequestAuthorizer != null
                ? FullStateRequestAuthorizer(sender, msg.TargetNetworkId)
                : sender.ConnectionId == asc.OwnerConnectionId;

            if (!isAuthorized)
                return;

            var data = asc is INetworkedASCConnectionScopedFullState scopedFullState
                ? scopedFullState.CaptureFullStateForConnection(sender)
                : asc.CaptureFullState();
            ServerSendFullState(sender, data);
        }
    }
}
