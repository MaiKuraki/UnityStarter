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
        // Message IDs in the reserved RPC range (100-999)
        public const ushort MsgAbilityActivateRequest = 200;
        public const ushort MsgAbilityActivateConfirm = 201;
        public const ushort MsgAbilityActivateReject = 202;
        public const ushort MsgAbilityEnd = 203;
        public const ushort MsgAbilityCancel = 204;
        public const ushort MsgEffectApplied = 210;
        public const ushort MsgEffectRemoved = 211;
        public const ushort MsgEffectStackChanged = 212;
        public const ushort MsgEffectUpdated = 213;
        public const ushort MsgAttributeUpdate = 220;
        public const ushort MsgTagUpdate = 225;
        public const ushort MsgAbilityMulticast = 230;
        public const ushort MsgFullState = 240;
        public const ushort MsgFullStateRequest = 241;
        public const ushort MsgStateSyncMetadata = 242;

        private readonly INetworkManager _networkManager;
        private readonly Dictionary<int, INetworkedASC> _ascByConnectionId =
            new Dictionary<int, INetworkedASC>(32);
        private readonly Dictionary<uint, INetworkedASC> _ascByNetworkId =
            new Dictionary<uint, INetworkedASC>(64);

        public INetworkManager NetworkManager => _networkManager;

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
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
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
        public void ClientRequestActivateAbility(int abilityIndex, int predictionKey,
            NetworkVector3 targetPos, NetworkVector3 direction, uint targetNetworkId = 0)
        {
            ClientRequestActivateAbility(
                abilityIndex,
                predictionKey,
                0,
                0,
                targetPos,
                direction,
                targetNetworkId);
        }

        public void ClientRequestActivateAbility(
            int abilityIndex,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence,
            NetworkVector3 targetPos,
            NetworkVector3 direction,
            uint targetNetworkId = 0)
        {
            _networkManager.SendToServer(MsgAbilityActivateRequest, new AbilityActivateRequest
            {
                AbilityIndex = abilityIndex,
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
        public void ClientNotifyAbilityEnd(int abilityIndex, bool wasCancelled)
        {
            _networkManager.SendToServer(wasCancelled ? MsgAbilityCancel : MsgAbilityEnd,
                new AbilityEndMessage { AbilityIndex = abilityIndex });
        }

        // =====================================================
        // SERVER ->CLIENT: Ability Confirmation/Rejection
        // =====================================================

        /// <summary>
        /// Server confirms predicted ability activation.
        /// Client removes pending prediction and keeps running effects.
        /// </summary>
        public void ServerConfirmActivation(INetConnection client, int abilityIndex, int predictionKey)
        {
            ServerConfirmActivation(client, abilityIndex, predictionKey, 0, 0);
        }

        public void ServerConfirmActivation(
            INetConnection client,
            int abilityIndex,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence)
        {
            _networkManager.SendToClient(client, MsgAbilityActivateConfirm, new AbilityActivateConfirm
            {
                AbilityIndex = abilityIndex,
                PredictionKey = predictionKey,
                PredictionKeyOwner = predictionKeyOwner,
                PredictionInputSequence = predictionInputSequence
            });
        }

        /// <summary>
        /// Server rejects predicted ability activation.
        /// Client rolls back all effects tagged with this prediction key.
        /// </summary>
        public void ServerRejectActivation(INetConnection client, int abilityIndex, int predictionKey)
        {
            ServerRejectActivation(client, abilityIndex, predictionKey, 0, 0);
        }

        public void ServerRejectActivation(
            INetConnection client,
            int abilityIndex,
            int predictionKey,
            int predictionKeyOwner,
            int predictionInputSequence)
        {
            _networkManager.SendToClient(client, MsgAbilityActivateReject, new AbilityActivateReject
            {
                AbilityIndex = abilityIndex,
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
                    msg.AbilityIndex,
                    msg.PredictionKey,
                    msg.PredictionKeyOwner,
                    msg.PredictionInputSequence);
        }

        private void OnRecvActivateReject(INetConnection sender, AbilityActivateReject msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnServerRejectActivation(
                    msg.AbilityIndex,
                    msg.PredictionKey,
                    msg.PredictionKeyOwner,
                    msg.PredictionInputSequence);
        }

        private void OnRecvAbilityEnd(INetConnection sender, AbilityEndMessage msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnAbilityEnded(msg.AbilityIndex);
        }

        private void OnRecvAbilityCancel(INetConnection sender, AbilityCancelMessage msg)
        {
            if (_ascByConnectionId.TryGetValue(sender.ConnectionId, out var asc))
                asc.OnAbilityCancelled(msg.AbilityIndex);
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

            var data = asc.CaptureFullState();
            ServerSendFullState(sender, data);
        }
    }
}
