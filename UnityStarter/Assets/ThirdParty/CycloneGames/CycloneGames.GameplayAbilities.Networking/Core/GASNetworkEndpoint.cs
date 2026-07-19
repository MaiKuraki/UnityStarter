using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using CycloneGames.Networking.Security;

namespace CycloneGames.GameplayAbilities.Networking
{
    public enum GASNetworkEndpointRole : byte
    {
        Invalid = 0,
        Client = 1,
        Authority = 2
    }

    public enum GASNetworkEndpointFailureKind : byte
    {
        Invalid = 0,
        UnexpectedDirection = 1,
        UnexpectedChannel = 2,
        UnknownPeer = 3,
        UnauthenticatedPeer = 4,
        PeerCapacityExceeded = 5,
        HandshakeRequired = 6,
        HandshakeRejected = 7,
        HandshakeResponseFailed = 8,
        MalformedPayload = 9,
        ReentrantDispatch = 10,
        MessageHandlerException = 11,
        EndpointException = 12
    }

    /// <summary>
    /// Stable failure data for an inbound GAS message. Payload bytes are intentionally excluded
    /// because their lifetime ends when the network callback returns.
    /// </summary>
    public readonly struct GASNetworkEndpointFailure
    {
        internal GASNetworkEndpointFailure(
            GASNetworkEndpointFailureKind kind,
            INetConnection connection,
            ushort messageId,
            NetworkMessageDirection direction,
            GASNetworkWireCodecResult codecResult,
            GASNetworkHandshakeResult handshakeResult,
            NetworkSendStatus sendStatus,
            Exception exception)
        {
            Kind = kind;
            Connection = connection;
            MessageId = messageId;
            Direction = direction;
            CodecResult = codecResult;
            HandshakeResult = handshakeResult;
            SendStatus = sendStatus;
            Exception = exception;
        }

        public GASNetworkEndpointFailureKind Kind { get; }
        public INetConnection Connection { get; }
        public ushort MessageId { get; }
        public NetworkMessageDirection Direction { get; }
        public GASNetworkWireCodecResult CodecResult { get; }
        public GASNetworkHandshakeResult HandshakeResult { get; }
        public NetworkSendStatus SendStatus { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// Receives an explicit report for every rejected inbound message and every caught sink,
    /// lifecycle, or endpoint exception. The callback runs synchronously on the endpoint owner thread.
    /// </summary>
    public delegate void GASNetworkEndpointFailureHandler(in GASNetworkEndpointFailure failure);

    public interface IGASNetworkClientSink
    {
        /// <summary>
        /// Called once after the compatible authority handshake has been committed.
        /// </summary>
        void OnAuthorityReady(INetConnection authority);

        /// <summary>
        /// Called once after an observed authority session has been removed. Endpoint state is
        /// already cleared when this callback runs.
        /// </summary>
        void OnAuthorityDisconnected(INetConnection authority);
        void OnCommandResult(INetConnection authority, in GASCommandResult message);

        /// <summary>
        /// All spans are valid only for this callback. Copying data for later use is the sink's
        /// explicit responsibility.
        /// </summary>
        void OnStateBatchChunk(
            INetConnection authority,
            in GASStateBatchChunk message,
            ReadOnlySpan<GASAbilityStateRecord> abilities,
            ReadOnlySpan<GASAttributeStateRecord> attributes,
            ReadOnlySpan<GASEffectStateRecord> effects,
            ReadOnlySpan<GASEffectTagStateRecord> effectTags,
            ReadOnlySpan<GASEffectMagnitudeStateRecord> effectMagnitudes,
            ReadOnlySpan<GASLooseTagStateRecord> looseTags);

        void OnCueExecuted(INetConnection authority, in GASCueExecuted message);
    }

    public interface IGASNetworkAuthoritySink
    {
        /// <summary>
        /// Called once after the compatible client handshake has been committed.
        /// </summary>
        void OnClientReady(INetConnection client);

        /// <summary>
        /// Called once after an observed client session has been removed. Endpoint state is
        /// already cleared when this callback runs.
        /// </summary>
        void OnClientDisconnected(INetConnection client);

        /// <summary>
        /// Actor targets are valid only for this callback. The authority must still validate
        /// ownership, rate limits, range, visibility, and collision before applying the intent.
        /// </summary>
        void OnAbilityCommand(
            INetConnection client,
            in GASAbilityCommand message,
            ReadOnlySpan<GASNetworkEntityId> actorTargets);

        void OnStateAcknowledgement(INetConnection client, in GASStateAcknowledgement message);
        void OnResyncRequest(INetConnection client, in GASResyncRequest message);
    }

    /// <summary>
    /// Owner-thread-affine, backend-neutral GAS message facade. Construction and all subsequent
    /// calls, inbound dispatch, failure callbacks, lifecycle callbacks, and disposal must occur
    /// on the same thread. The facade performs no Unity API or transport SDK calls.
    /// </summary>
    public sealed class GASNetworkEndpoint : IDisposable
    {
        public const int DefaultMaximumAuthorityPeers = NetworkConstants.DefaultMaxConnections;
        public const int MaximumAuthorityPeerCapacity = ushort.MaxValue;

        private const int InitialAuthorityPeerCapacity = 64;
        private const string EndpointStoppedReason = "The network message endpoint is not accepting messages.";
        private const string ChannelUnavailableReason = "The reliable GAS message route is unavailable.";
        private const string PayloadBudgetReason = "The GAS payload exceeds the endpoint route budget.";
        private const string InvalidMessageReason = "The GAS message is invalid for the wire contract.";
        private const string ScratchTooSmallReason = "The caller-owned state payload scratch buffer is too small.";
        private const string HandshakeRequiredReason = "The peer has not completed the GAS compatibility handshake.";
        private const string PeerUnavailableReason = "The peer is unavailable or unauthenticated.";
        private const string PeerCapacityReason = "The configured GAS authority peer capacity has been reached.";

        private struct PeerHandshakeState
        {
            public bool LocalSent;
            public bool RemoteCompatible;

            public bool IsComplete => LocalSent && RemoteCompatible;
        }

        private readonly INetworkMessageEndpoint endpoint;
        private readonly INetTransport transport;
        private readonly IGASNetworkClientSink clientSink;
        private readonly IGASNetworkAuthoritySink authoritySink;
        private readonly GASNetworkEndpointFailureHandler failureHandler;
        private readonly GASNetworkFeatureFlags requiredFeatures;
        private readonly bool disconnectOnProtocolViolation;
        private readonly int ownerThreadId;
        private readonly int maximumAuthorityPeers;
        private readonly Dictionary<INetConnection, PeerHandshakeState> authorityPeers;
        private readonly List<INetConnection> observedAuthorityPeers;

        private readonly GASNetworkEntityId[] actorTargetScratch;
        private readonly GASAbilityStateRecord[] abilityScratch;
        private readonly GASAttributeStateRecord[] attributeScratch;
        private readonly GASEffectStateRecord[] effectScratch;
        private readonly GASEffectTagStateRecord[] effectTagScratch;
        private readonly GASEffectMagnitudeStateRecord[] effectMagnitudeScratch;
        private readonly GASLooseTagStateRecord[] looseTagScratch;

        private readonly NetworkMessageHandlerLease handshakeLease;
        private readonly NetworkMessageHandlerLease abilityCommandLease;
        private readonly NetworkMessageHandlerLease commandResultLease;
        private readonly NetworkMessageHandlerLease stateBatchChunkLease;
        private readonly NetworkMessageHandlerLease stateAcknowledgementLease;
        private readonly NetworkMessageHandlerLease resyncRequestLease;
        private readonly NetworkMessageHandlerLease cueExecutedLease;

        private PeerHandshakeState clientHandshake;
        private INetConnection clientAuthority;
        private bool clientAuthorityObserved;
        private bool dispatching;
        private bool reportingFailure;
        private bool disposed;

        public GASNetworkEndpoint(
            INetworkMessageEndpoint endpoint,
            ulong contentCatalogHash,
            ulong gameplayTagManifestHash,
            IGASNetworkClientSink sink,
            GASNetworkEndpointFailureHandler failureHandler,
            GASNetworkFeatureFlags requiredFeatures = GameplayAbilitiesNetworkProtocol.SupportedFeatures,
            bool disconnectOnProtocolViolation = true)
            : this(
                endpoint,
                contentCatalogHash,
                gameplayTagManifestHash,
                GASNetworkEndpointRole.Client,
                sink,
                null,
                failureHandler,
                requiredFeatures,
                1,
                disconnectOnProtocolViolation)
        {
        }

        public GASNetworkEndpoint(
            INetworkMessageEndpoint endpoint,
            ulong contentCatalogHash,
            ulong gameplayTagManifestHash,
            IGASNetworkAuthoritySink sink,
            GASNetworkEndpointFailureHandler failureHandler,
            GASNetworkFeatureFlags requiredFeatures = GameplayAbilitiesNetworkProtocol.SupportedFeatures,
            int maximumAuthorityPeers = DefaultMaximumAuthorityPeers,
            bool disconnectOnProtocolViolation = true)
            : this(
                endpoint,
                contentCatalogHash,
                gameplayTagManifestHash,
                GASNetworkEndpointRole.Authority,
                null,
                sink,
                failureHandler,
                requiredFeatures,
                maximumAuthorityPeers,
                disconnectOnProtocolViolation)
        {
        }

        private GASNetworkEndpoint(
            INetworkMessageEndpoint endpoint,
            ulong contentCatalogHash,
            ulong gameplayTagManifestHash,
            GASNetworkEndpointRole role,
            IGASNetworkClientSink clientSink,
            IGASNetworkAuthoritySink authoritySink,
            GASNetworkEndpointFailureHandler failureHandler,
            GASNetworkFeatureFlags requiredFeatures,
            int maximumAuthorityPeers,
            bool disconnectOnProtocolViolation)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.failureHandler = failureHandler ?? throw new ArgumentNullException(nameof(failureHandler));
            if (role == GASNetworkEndpointRole.Client && clientSink == null)
                throw new ArgumentNullException(nameof(clientSink));
            if (role == GASNetworkEndpointRole.Authority && authoritySink == null)
                throw new ArgumentNullException(nameof(authoritySink));
            if (maximumAuthorityPeers <= 0 || maximumAuthorityPeers > MaximumAuthorityPeerCapacity)
                throw new ArgumentOutOfRangeException(nameof(maximumAuthorityPeers));
            if ((requiredFeatures & ~GameplayAbilitiesNetworkProtocol.SupportedFeatures) != 0)
                throw new ArgumentOutOfRangeException(nameof(requiredFeatures));

            Role = role;
            LocalHandshake = GameplayAbilitiesNetworkProtocol.CreateHandshake(
                contentCatalogHash,
                gameplayTagManifestHash);
            this.clientSink = clientSink;
            this.authoritySink = authoritySink;
            this.requiredFeatures = requiredFeatures;
            this.maximumAuthorityPeers = maximumAuthorityPeers;
            this.disconnectOnProtocolViolation = disconnectOnProtocolViolation;
            ownerThreadId = Environment.CurrentManagedThreadId;
            transport = endpoint.Transport;

            if (role == GASNetworkEndpointRole.Authority)
            {
                int initialPeerCapacity = Math.Min(maximumAuthorityPeers, InitialAuthorityPeerCapacity);
                authorityPeers = new Dictionary<INetConnection, PeerHandshakeState>(initialPeerCapacity);
                observedAuthorityPeers = new List<INetConnection>(initialPeerCapacity);
            }

            actorTargetScratch = new GASNetworkEntityId[GameplayAbilitiesNetworkProtocol.MaxActorTargets];
            abilityScratch = new GASAbilityStateRecord[GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk];
            attributeScratch = new GASAttributeStateRecord[GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk];
            effectScratch = new GASEffectStateRecord[GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk];
            effectTagScratch = new GASEffectTagStateRecord[GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk];
            effectMagnitudeScratch = new GASEffectMagnitudeStateRecord[GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk];
            looseTagScratch = new GASLooseTagStateRecord[GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk];

            NetworkMessageHandlerLease localHandshakeLease = default;
            NetworkMessageHandlerLease localAbilityCommandLease = default;
            NetworkMessageHandlerLease localCommandResultLease = default;
            NetworkMessageHandlerLease localStateBatchChunkLease = default;
            NetworkMessageHandlerLease localStateAcknowledgementLease = default;
            NetworkMessageHandlerLease localResyncRequestLease = default;
            NetworkMessageHandlerLease localCueExecutedLease = default;
            bool lifecycleSubscribed = false;

            try
            {
                localHandshakeLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    HandleHandshake);
                localAbilityCommandLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                    HandleAbilityCommand);
                localCommandResultLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.CommandResultMessageId,
                    HandleCommandResult);
                localStateBatchChunkLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                    HandleStateBatchChunk);
                localStateAcknowledgementLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.StateAcknowledgementMessageId,
                    HandleStateAcknowledgement);
                localResyncRequestLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.ResyncRequestMessageId,
                    HandleResyncRequest);
                localCueExecutedLease = endpoint.RegisterHandler(
                    GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                    HandleCueExecuted);

                if (transport != null)
                {
                    if (role == GASNetworkEndpointRole.Authority)
                        transport.OnClientDisconnected += HandleClientDisconnected;
                    else
                        transport.OnDisconnectedFromServer += HandleDisconnectedFromAuthority;
                    lifecycleSubscribed = true;
                }
            }
            catch
            {
                if (lifecycleSubscribed)
                {
                    if (role == GASNetworkEndpointRole.Authority)
                        transport.OnClientDisconnected -= HandleClientDisconnected;
                    else
                        transport.OnDisconnectedFromServer -= HandleDisconnectedFromAuthority;
                }

                localCueExecutedLease.Dispose();
                localResyncRequestLease.Dispose();
                localStateAcknowledgementLease.Dispose();
                localStateBatchChunkLease.Dispose();
                localCommandResultLease.Dispose();
                localAbilityCommandLease.Dispose();
                localHandshakeLease.Dispose();
                throw;
            }

            handshakeLease = localHandshakeLease;
            abilityCommandLease = localAbilityCommandLease;
            commandResultLease = localCommandResultLease;
            stateBatchChunkLease = localStateBatchChunkLease;
            stateAcknowledgementLease = localStateAcknowledgementLease;
            resyncRequestLease = localResyncRequestLease;
            cueExecutedLease = localCueExecutedLease;
        }

        public GASNetworkEndpointRole Role { get; }
        public GASNetworkHandshake LocalHandshake { get; }
        public bool IsDisposed => disposed;

        public bool IsAuthorityHandshakeComplete
        {
            get
            {
                EnsureOwnerThread();
                ThrowIfDisposed();
                EnsureRole(GASNetworkEndpointRole.Client);
                return clientHandshake.IsComplete;
            }
        }

        public int TrackedAuthorityPeerCount
        {
            get
            {
                EnsureOwnerThread();
                ThrowIfDisposed();
                EnsureRole(GASNetworkEndpointRole.Authority);
                return authorityPeers.Count;
            }
        }

        public bool IsClientHandshakeComplete(INetConnection client)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);
            return IsUsableConnection(client) &&
                   authorityPeers.TryGetValue(client, out PeerHandshakeState state) &&
                   state.IsComplete;
        }

        public NetworkSendResult SendHandshakeToAuthority()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Client);

            bool wasComplete = clientHandshake.IsComplete;
            NetworkSendResult result = SendLocalHandshakeToAuthority();
            if (result.Succeeded)
            {
                clientHandshake.LocalSent = true;
                if (!wasComplete && clientHandshake.IsComplete)
                    NotifyAuthorityReadyFromOutbound();
            }
            return result;
        }

        public NetworkSendResult SendHandshakeToClient(INetConnection client)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);
            if (!IsUsableConnection(client))
                return PeerUnavailable(client);

            if (!TryGetOrAddAuthorityPeer(client, out PeerHandshakeState state))
                return NetworkSendResult.Fail(NetworkSendStatus.Backpressure, connection: client, reason: PeerCapacityReason);

            bool wasComplete = state.IsComplete;
            NetworkSendResult result = SendLocalHandshakeToClient(client);
            if (result.Succeeded)
            {
                state.LocalSent = true;
                authorityPeers[client] = state;
                if (!wasComplete && state.IsComplete)
                    NotifyClientReadyFromOutbound(client);
            }
            return result;
        }

        public NetworkSendResult SendAbilityCommand(
            in GASAbilityCommand message,
            ReadOnlySpan<GASNetworkEntityId> actorTargets)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Client);
            if (!clientHandshake.IsComplete)
                return HandshakeRequired();

            int required = GASNetworkWireCodec.GetAbilityCommandPayloadBytes(in message);
            if (required <= 0)
                return InvalidMessage();
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                    required,
                    null,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[GASNetworkWireCodec.MaxAbilityCommandPayloadBytes];
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteAbilityCommand(
                in message,
                actorTargets,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, null);

            return endpoint.SendToServer(
                GameplayAbilitiesNetworkProtocol.AbilityCommandMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public NetworkSendResult SendStateAcknowledgement(in GASStateAcknowledgement message)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Client);
            if (!clientHandshake.IsComplete)
                return HandshakeRequired();

            const int required = GASNetworkWireCodec.StateAcknowledgementPayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.StateAcknowledgementMessageId,
                    required,
                    null,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteStateAcknowledgement(
                in message,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, null);
            return endpoint.SendToServer(
                GameplayAbilitiesNetworkProtocol.StateAcknowledgementMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public NetworkSendResult SendResyncRequest(in GASResyncRequest message)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Client);
            if (!clientHandshake.IsComplete)
                return HandshakeRequired();

            const int required = GASNetworkWireCodec.ResyncRequestPayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.ResyncRequestMessageId,
                    required,
                    null,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteResyncRequest(
                in message,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, null);
            return endpoint.SendToServer(
                GameplayAbilitiesNetworkProtocol.ResyncRequestMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public NetworkSendResult SendCommandResult(INetConnection client, in GASCommandResult message)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);
            if (!IsReadyAuthorityPeer(client))
                return HandshakeRequired(client);

            const int required = GASNetworkWireCodec.CommandResultPayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.CommandResultMessageId,
                    required,
                    client,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteCommandResult(
                in message,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, client);
            return endpoint.SendToClient(
                client,
                GameplayAbilitiesNetworkProtocol.CommandResultMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public NetworkSendResult SendStateBatchChunk(
            INetConnection client,
            in GASStateBatchChunk message,
            ReadOnlySpan<GASAbilityStateRecord> abilities,
            ReadOnlySpan<GASAttributeStateRecord> attributes,
            ReadOnlySpan<GASEffectStateRecord> effects,
            ReadOnlySpan<GASEffectTagStateRecord> effectTags,
            ReadOnlySpan<GASEffectMagnitudeStateRecord> effectMagnitudes,
            ReadOnlySpan<GASLooseTagStateRecord> looseTags,
            Span<byte> payloadScratch)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);
            if (!IsReadyAuthorityPeer(client))
                return HandshakeRequired(client);

            int required = GASNetworkWireCodec.GetStateBatchPayloadBytes(in message);
            if (required <= 0)
                return InvalidMessage(client);
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                    required,
                    client,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }
            if (payloadScratch.Length < required)
            {
                return NetworkSendResult.Fail(
                    NetworkSendStatus.InvalidPayload,
                    connection: client,
                    reason: ScratchTooSmallReason);
            }

            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteStateBatchChunk(
                in message,
                abilities,
                attributes,
                effects,
                effectTags,
                effectMagnitudes,
                looseTags,
                payloadScratch,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, client);
            return endpoint.SendToClient(
                client,
                GameplayAbilitiesNetworkProtocol.StateBatchChunkMessageId,
                payloadScratch.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public NetworkSendResult SendCueExecuted(INetConnection client, in GASCueExecuted message)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);
            if (!IsReadyAuthorityPeer(client))
                return HandshakeRequired(client);

            const int required = GASNetworkWireCodec.CueExecutedPayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                    required,
                    client,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteCueExecuted(
                in message,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, client);
            return endpoint.SendToClient(
                client,
                GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public NetworkSendResult BroadcastCueExecuted(
            IReadOnlyList<INetConnection> clients,
            in GASCueExecuted message)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);
            if (clients == null)
                throw new ArgumentNullException(nameof(clients));

            for (int i = 0; i < clients.Count; i++)
            {
                INetConnection client = clients[i];
                if (!IsReadyAuthorityPeer(client))
                    return HandshakeRequired(client);
            }

            const int required = GASNetworkWireCodec.CueExecutedPayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                    required,
                    null,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteCueExecuted(
                in message,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, null);
            return endpoint.Broadcast(
                clients,
                GameplayAbilitiesNetworkProtocol.CueExecutedMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        public bool RemoveClient(INetConnection client)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Authority);

            bool removed = InvalidatePeerAndNotify(client, out Exception callbackException);
            if (callbackException != null)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.MessageHandlerException,
                    client,
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    NetworkMessageDirection.ClientToServer,
                    GASNetworkWireCodecResult.Invalid,
                    GASNetworkHandshakeResult.Invalid,
                    NetworkSendStatus.Invalid,
                    callbackException);
            }
            return removed;
        }

        public void ResetAuthority()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureRole(GASNetworkEndpointRole.Client);

            INetConnection authority = clientAuthority;
            InvalidatePeerAndNotify(authority, out Exception callbackException);
            if (callbackException != null)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.MessageHandlerException,
                    authority,
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    NetworkMessageDirection.ServerToClient,
                    GASNetworkWireCodecResult.Invalid,
                    GASNetworkHandshakeResult.Invalid,
                    NetworkSendStatus.Invalid,
                    callbackException);
            }
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (disposed)
                return;

            disposed = true;
            if (transport != null)
            {
                if (Role == GASNetworkEndpointRole.Authority)
                    transport.OnClientDisconnected -= HandleClientDisconnected;
                else
                    transport.OnDisconnectedFromServer -= HandleDisconnectedFromAuthority;
            }

            cueExecutedLease.Dispose();
            resyncRequestLease.Dispose();
            stateAcknowledgementLease.Dispose();
            stateBatchChunkLease.Dispose();
            commandResultLease.Dispose();
            abilityCommandLease.Dispose();
            handshakeLease.Dispose();

            Exception pendingException = null;
            if (Role == GASNetworkEndpointRole.Authority)
            {
                while (observedAuthorityPeers.Count > 0)
                {
                    int lastIndex = observedAuthorityPeers.Count - 1;
                    INetConnection client = observedAuthorityPeers[lastIndex];
                    observedAuthorityPeers.RemoveAt(lastIndex);
                    authorityPeers.Remove(client);

                    try
                    {
                        authoritySink.OnClientDisconnected(client);
                    }
                    catch (Exception callbackException)
                    {
                        try
                        {
                            ReportFailure(
                                GASNetworkEndpointFailureKind.MessageHandlerException,
                                client,
                                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                                NetworkMessageDirection.ClientToServer,
                                GASNetworkWireCodecResult.Invalid,
                                GASNetworkHandshakeResult.Invalid,
                                NetworkSendStatus.Invalid,
                                callbackException);
                        }
                        catch (Exception reportingException)
                        {
                            pendingException = CombineExceptions(pendingException, reportingException);
                        }
                    }
                }

                authorityPeers.Clear();
            }
            else
            {
                INetConnection authority = clientAuthority;
                bool notify = clientAuthorityObserved;
                ClearClientHandshake();
                if (notify)
                {
                    try
                    {
                        clientSink.OnAuthorityDisconnected(authority);
                    }
                    catch (Exception callbackException)
                    {
                        try
                        {
                            ReportFailure(
                                GASNetworkEndpointFailureKind.MessageHandlerException,
                                authority,
                                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                                NetworkMessageDirection.ServerToClient,
                                GASNetworkWireCodecResult.Invalid,
                                GASNetworkHandshakeResult.Invalid,
                                NetworkSendStatus.Invalid,
                                callbackException);
                        }
                        catch (Exception reportingException)
                        {
                            pendingException = CombineExceptions(pendingException, reportingException);
                        }
                    }
                }
            }

            if (pendingException != null)
                throw pendingException;
        }

        private void HandleHandshake(in NetworkMessagePayload payload)
        {
            if (!TryEnterDispatch(in payload))
                return;

            try
            {
                NetworkMessageDirection expectedDirection = Role == GASNetworkEndpointRole.Authority
                    ? NetworkMessageDirection.ClientToServer
                    : NetworkMessageDirection.ServerToClient;
                if (payload.Direction != expectedDirection)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnexpectedDirection);
                    return;
                }
                if (payload.Header.Channel != NetworkChannel.Reliable)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnexpectedChannel);
                    return;
                }
                if (!IsAcceptableHandshakePeer(payload.Connection))
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnauthenticatedPeer);
                    return;
                }

                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadHandshake(
                    payload.Bytes,
                    out GASNetworkHandshake remote);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(
                        in payload,
                        GASNetworkEndpointFailureKind.MalformedPayload,
                        codec,
                        GASNetworkHandshakeResult.Malformed);
                    return;
                }

                GASNetworkHandshakeResult negotiation = GameplayAbilitiesNetworkProtocol.Negotiate(
                    in remote,
                    LocalHandshake.ContentCatalogHash,
                    LocalHandshake.GameplayTagManifestHash,
                    requiredFeatures);
                if (negotiation != GASNetworkHandshakeResult.Compatible)
                {
                    RejectProtocolViolation(
                        in payload,
                        GASNetworkEndpointFailureKind.HandshakeRejected,
                        GASNetworkWireCodecResult.Success,
                        negotiation);
                    return;
                }

                if (Role == GASNetworkEndpointRole.Authority)
                    CompleteAuthorityHandshake(in payload);
                else
                    CompleteClientHandshake(in payload);
            }
            finally
            {
                dispatching = false;
            }
        }

        private void CompleteAuthorityHandshake(in NetworkMessagePayload payload)
        {
            INetConnection client = payload.Connection;
            if (!TryGetOrAddAuthorityPeer(client, out PeerHandshakeState state))
            {
                RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.PeerCapacityExceeded);
                return;
            }

            bool wasComplete = state.IsComplete;
            bool wasObserved = state.RemoteCompatible;
            state.RemoteCompatible = true;
            authorityPeers[client] = state;
            if (!wasObserved)
                observedAuthorityPeers.Add(client);
            if (state.LocalSent)
            {
                if (!wasComplete)
                    NotifyClientReady(in payload, client);
                return;
            }

            NetworkSendResult result;
            try
            {
                result = SendLocalHandshakeToClient(client);
            }
            catch (Exception exception)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.EndpointException,
                    in payload,
                    GASNetworkWireCodecResult.Invalid,
                    GASNetworkHandshakeResult.Compatible,
                    NetworkSendStatus.Invalid,
                    exception);
                return;
            }

            if (!result.Succeeded)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.HandshakeResponseFailed,
                    in payload,
                    GASNetworkWireCodecResult.Success,
                    GASNetworkHandshakeResult.Compatible,
                    result.Status,
                    null);
                return;
            }

            state.LocalSent = true;
            authorityPeers[client] = state;
            if (!wasComplete)
                NotifyClientReady(in payload, client);
        }

        private void CompleteClientHandshake(in NetworkMessagePayload payload)
        {
            if (clientAuthorityObserved && !IsSameConnection(clientAuthority, payload.Connection))
            {
                RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnknownPeer);
                return;
            }

            bool wasComplete = clientHandshake.IsComplete;
            clientAuthority = payload.Connection;
            clientAuthorityObserved = true;
            clientHandshake.RemoteCompatible = true;
            if (clientHandshake.LocalSent)
            {
                if (!wasComplete)
                    NotifyAuthorityReady(in payload, payload.Connection);
                return;
            }

            NetworkSendResult result;
            try
            {
                result = SendLocalHandshakeToAuthority();
            }
            catch (Exception exception)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.EndpointException,
                    in payload,
                    GASNetworkWireCodecResult.Invalid,
                    GASNetworkHandshakeResult.Compatible,
                    NetworkSendStatus.Invalid,
                    exception);
                return;
            }

            if (!result.Succeeded)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.HandshakeResponseFailed,
                    in payload,
                    GASNetworkWireCodecResult.Success,
                    GASNetworkHandshakeResult.Compatible,
                    result.Status,
                    null);
                return;
            }

            clientHandshake.LocalSent = true;
            if (!wasComplete)
                NotifyAuthorityReady(in payload, payload.Connection);
        }

        private void NotifyClientReady(
            in NetworkMessagePayload payload,
            INetConnection client)
        {
            try
            {
                authoritySink.OnClientReady(client);
            }
            catch (Exception exception)
            {
                RejectHandlerException(in payload, exception);
            }
        }

        private void NotifyAuthorityReady(
            in NetworkMessagePayload payload,
            INetConnection authority)
        {
            try
            {
                clientSink.OnAuthorityReady(authority);
            }
            catch (Exception exception)
            {
                RejectHandlerException(in payload, exception);
            }
        }

        private void NotifyClientReadyFromOutbound(INetConnection client)
        {
            try
            {
                authoritySink.OnClientReady(client);
            }
            catch (Exception exception)
            {
                RejectOutboundLifecycleException(
                    client,
                    NetworkMessageDirection.ServerToClient,
                    exception);
            }
        }

        private void NotifyAuthorityReadyFromOutbound()
        {
            INetConnection authority = clientAuthority;
            try
            {
                clientSink.OnAuthorityReady(authority);
            }
            catch (Exception exception)
            {
                RejectOutboundLifecycleException(
                    authority,
                    NetworkMessageDirection.ClientToServer,
                    exception);
            }
        }

        private void HandleAbilityCommand(in NetworkMessagePayload payload)
        {
            if (!TryEnterGameplayDispatch(
                    in payload,
                    GASNetworkEndpointRole.Authority,
                    NetworkMessageDirection.ClientToServer,
                    false))
            {
                return;
            }

            try
            {
                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadAbilityCommand(
                    payload.Bytes,
                    actorTargetScratch,
                    out GASAbilityCommand message,
                    out int actorTargetCount);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.MalformedPayload, codec);
                    return;
                }

                try
                {
                    authoritySink.OnAbilityCommand(
                        payload.Connection,
                        in message,
                        new ReadOnlySpan<GASNetworkEntityId>(actorTargetScratch, 0, actorTargetCount));
                }
                catch (Exception exception)
                {
                    RejectHandlerException(in payload, exception);
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private void HandleCommandResult(in NetworkMessagePayload payload)
        {
            if (!TryEnterGameplayDispatch(
                    in payload,
                    GASNetworkEndpointRole.Client,
                    NetworkMessageDirection.ServerToClient,
                    false))
            {
                return;
            }

            try
            {
                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadCommandResult(
                    payload.Bytes,
                    out GASCommandResult message);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.MalformedPayload, codec);
                    return;
                }

                try
                {
                    clientSink.OnCommandResult(payload.Connection, in message);
                }
                catch (Exception exception)
                {
                    RejectHandlerException(in payload, exception);
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private void HandleStateBatchChunk(in NetworkMessagePayload payload)
        {
            if (!TryEnterGameplayDispatch(
                    in payload,
                    GASNetworkEndpointRole.Client,
                    NetworkMessageDirection.ServerToClient,
                    true))
            {
                return;
            }

            try
            {
                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadStateBatchChunk(
                    payload.Bytes,
                    abilityScratch,
                    attributeScratch,
                    effectScratch,
                    effectTagScratch,
                    effectMagnitudeScratch,
                    looseTagScratch,
                    out GASStateBatchChunk message);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.MalformedPayload, codec);
                    return;
                }

                try
                {
                    clientSink.OnStateBatchChunk(
                        payload.Connection,
                        in message,
                        new ReadOnlySpan<GASAbilityStateRecord>(abilityScratch, 0, message.AbilityCount),
                        new ReadOnlySpan<GASAttributeStateRecord>(attributeScratch, 0, message.AttributeCount),
                        new ReadOnlySpan<GASEffectStateRecord>(effectScratch, 0, message.EffectCount),
                        new ReadOnlySpan<GASEffectTagStateRecord>(effectTagScratch, 0, message.EffectTagCount),
                        new ReadOnlySpan<GASEffectMagnitudeStateRecord>(effectMagnitudeScratch, 0, message.EffectMagnitudeCount),
                        new ReadOnlySpan<GASLooseTagStateRecord>(looseTagScratch, 0, message.LooseTagCount));
                }
                catch (Exception exception)
                {
                    RejectHandlerException(in payload, exception);
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private void HandleStateAcknowledgement(in NetworkMessagePayload payload)
        {
            if (!TryEnterGameplayDispatch(
                    in payload,
                    GASNetworkEndpointRole.Authority,
                    NetworkMessageDirection.ClientToServer,
                    false))
            {
                return;
            }

            try
            {
                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadStateAcknowledgement(
                    payload.Bytes,
                    out GASStateAcknowledgement message);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.MalformedPayload, codec);
                    return;
                }

                try
                {
                    authoritySink.OnStateAcknowledgement(payload.Connection, in message);
                }
                catch (Exception exception)
                {
                    RejectHandlerException(in payload, exception);
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private void HandleResyncRequest(in NetworkMessagePayload payload)
        {
            if (!TryEnterGameplayDispatch(
                    in payload,
                    GASNetworkEndpointRole.Authority,
                    NetworkMessageDirection.ClientToServer,
                    false))
            {
                return;
            }

            try
            {
                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadResyncRequest(
                    payload.Bytes,
                    out GASResyncRequest message);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.MalformedPayload, codec);
                    return;
                }

                try
                {
                    authoritySink.OnResyncRequest(payload.Connection, in message);
                }
                catch (Exception exception)
                {
                    RejectHandlerException(in payload, exception);
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private void HandleCueExecuted(in NetworkMessagePayload payload)
        {
            if (!TryEnterGameplayDispatch(
                    in payload,
                    GASNetworkEndpointRole.Client,
                    NetworkMessageDirection.ServerToClient,
                    true))
            {
                return;
            }

            try
            {
                GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryReadCueExecuted(
                    payload.Bytes,
                    out GASCueExecuted message);
                if (codec != GASNetworkWireCodecResult.Success)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.MalformedPayload, codec);
                    return;
                }

                try
                {
                    clientSink.OnCueExecuted(payload.Connection, in message);
                }
                catch (Exception exception)
                {
                    RejectHandlerException(in payload, exception);
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private bool TryEnterGameplayDispatch(
            in NetworkMessagePayload payload,
            GASNetworkEndpointRole requiredRole,
            NetworkMessageDirection directDirection,
            bool allowServerBroadcast)
        {
            if (!TryEnterDispatch(in payload))
                return false;

            bool accepted = false;
            try
            {
                if (Role != requiredRole ||
                    (payload.Direction != directDirection &&
                     (!allowServerBroadcast || payload.Direction != NetworkMessageDirection.ServerBroadcast)))
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnexpectedDirection);
                    return false;
                }
                if (payload.Header.Channel != NetworkChannel.Reliable)
                {
                    RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnexpectedChannel);
                    return false;
                }

                if (Role == GASNetworkEndpointRole.Authority)
                {
                    if (!IsUsableConnection(payload.Connection))
                    {
                        RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnauthenticatedPeer);
                        return false;
                    }
                    if (!authorityPeers.TryGetValue(payload.Connection, out PeerHandshakeState state))
                    {
                        RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnknownPeer);
                        return false;
                    }
                    if (!state.IsComplete)
                    {
                        RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.HandshakeRequired);
                        return false;
                    }
                }
                else
                {
                    if (!clientAuthorityObserved || !IsSameConnection(clientAuthority, payload.Connection))
                    {
                        RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnknownPeer);
                        return false;
                    }
                    if (!clientHandshake.IsComplete)
                    {
                        RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.HandshakeRequired);
                        return false;
                    }
                    if (payload.Connection != null && !IsUsableConnection(payload.Connection))
                    {
                        RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.UnauthenticatedPeer);
                        return false;
                    }
                }

                accepted = true;
                return true;
            }
            finally
            {
                if (!accepted)
                {
                    dispatching = false;
                }
            }
        }

        private bool TryEnterDispatch(in NetworkMessagePayload payload)
        {
            EnsureOwnerThread();
            if (disposed)
                return false;
            if (!dispatching)
            {
                dispatching = true;
                return true;
            }

            RejectProtocolViolation(in payload, GASNetworkEndpointFailureKind.ReentrantDispatch);
            return false;
        }

        private NetworkSendResult SendLocalHandshakeToAuthority()
        {
            const int required = GASNetworkWireCodec.HandshakePayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    required,
                    null,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkHandshake localHandshake = LocalHandshake;
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteHandshake(
                in localHandshake,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, null);
            return endpoint.SendToServer(
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        private NetworkSendResult SendLocalHandshakeToClient(INetConnection client)
        {
            const int required = GASNetworkWireCodec.HandshakePayloadBytes;
            if (!TryValidateRouteBudget(
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    required,
                    client,
                    out NetworkSendResult routeFailure))
            {
                return routeFailure;
            }

            Span<byte> payload = stackalloc byte[required];
            GASNetworkHandshake localHandshake = LocalHandshake;
            GASNetworkWireCodecResult codec = GASNetworkWireCodec.TryWriteHandshake(
                in localHandshake,
                payload,
                out int written);
            if (codec != GASNetworkWireCodecResult.Success)
                return CodecFailure(codec, client);
            return endpoint.SendToClient(
                client,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                payload.Slice(0, written),
                NetworkChannel.Reliable);
        }

        private bool TryValidateRouteBudget(
            ushort messageId,
            int payloadLength,
            INetConnection connection,
            out NetworkSendResult failure)
        {
            if (!endpoint.IsAcceptingMessages)
            {
                failure = NetworkSendResult.Fail(
                    NetworkSendStatus.NotRunning,
                    connection: connection,
                    reason: EndpointStoppedReason);
                return false;
            }

            int maximum = endpoint.GetMaxPayloadSize(messageId, NetworkChannel.Reliable);
            if (maximum <= 0)
            {
                failure = NetworkSendResult.Fail(
                    NetworkSendStatus.ChannelUnavailable,
                    connection: connection,
                    reason: ChannelUnavailableReason);
                return false;
            }
            if (payloadLength > maximum)
            {
                failure = NetworkSendResult.Fail(
                    NetworkSendStatus.PayloadTooLarge,
                    connection: connection,
                    reason: PayloadBudgetReason);
                return false;
            }

            failure = default;
            return true;
        }

        private bool TryGetOrAddAuthorityPeer(
            INetConnection client,
            out PeerHandshakeState state)
        {
            if (authorityPeers.TryGetValue(client, out state))
                return true;
            if (authorityPeers.Count >= maximumAuthorityPeers)
                return false;

            state = default;
            authorityPeers.Add(client, state);
            return true;
        }

        private bool IsReadyAuthorityPeer(INetConnection client)
        {
            return IsUsableConnection(client) &&
                   authorityPeers.TryGetValue(client, out PeerHandshakeState state) &&
                   state.IsComplete;
        }

        private bool IsAcceptableHandshakePeer(INetConnection connection)
        {
            if (Role == GASNetworkEndpointRole.Authority)
                return IsUsableConnection(connection);
            return connection == null || IsUsableConnection(connection);
        }

        private static bool IsUsableConnection(INetConnection connection)
        {
            return connection != null && connection.IsConnected && connection.IsAuthenticated;
        }

        private static bool IsSameConnection(INetConnection left, INetConnection right)
        {
            if (ReferenceEquals(left, right))
                return true;
            return left != null && right != null && left.Equals(right);
        }

        private void RejectProtocolViolation(
            in NetworkMessagePayload payload,
            GASNetworkEndpointFailureKind kind,
            GASNetworkWireCodecResult codecResult = GASNetworkWireCodecResult.Invalid,
            GASNetworkHandshakeResult handshakeResult = GASNetworkHandshakeResult.Invalid)
        {
            InvalidatePeerAndNotify(payload.Connection, out Exception callbackException);
            Exception exception = CombineExceptions(
                callbackException,
                TryDisconnect(payload.Connection));
            ReportFailure(
                kind,
                in payload,
                codecResult,
                handshakeResult,
                NetworkSendStatus.Invalid,
                exception);
        }

        private void RejectHandlerException(
            in NetworkMessagePayload payload,
            Exception handlerException)
        {
            InvalidatePeerAndNotify(payload.Connection, out Exception callbackException);
            Exception disconnectException = TryDisconnect(payload.Connection);
            Exception reportedException = CombineExceptions(
                handlerException,
                callbackException,
                disconnectException);

            ReportFailure(
                GASNetworkEndpointFailureKind.MessageHandlerException,
                in payload,
                GASNetworkWireCodecResult.Success,
                GASNetworkHandshakeResult.Invalid,
                NetworkSendStatus.Invalid,
                reportedException);
        }

        private void RejectOutboundLifecycleException(
            INetConnection connection,
            NetworkMessageDirection direction,
            Exception handlerException)
        {
            InvalidatePeerAndNotify(connection, out Exception callbackException);
            Exception reportedException = CombineExceptions(
                handlerException,
                callbackException,
                TryDisconnect(connection));
            ReportFailure(
                GASNetworkEndpointFailureKind.MessageHandlerException,
                connection,
                GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                direction,
                GASNetworkWireCodecResult.Success,
                GASNetworkHandshakeResult.Compatible,
                NetworkSendStatus.Invalid,
                reportedException);
        }

        private Exception TryDisconnect(INetConnection connection)
        {
            if (!disconnectOnProtocolViolation || connection == null)
                return null;

            try
            {
                endpoint.Disconnect(connection);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private void ReportFailure(
            GASNetworkEndpointFailureKind kind,
            in NetworkMessagePayload payload,
            GASNetworkWireCodecResult codecResult,
            GASNetworkHandshakeResult handshakeResult,
            NetworkSendStatus sendStatus,
            Exception exception)
        {
            ReportFailure(
                kind,
                payload.Connection,
                payload.Header.MessageId,
                payload.Direction,
                codecResult,
                handshakeResult,
                sendStatus,
                exception);
        }

        private void ReportFailure(
            GASNetworkEndpointFailureKind kind,
            INetConnection connection,
            ushort messageId,
            NetworkMessageDirection direction,
            GASNetworkWireCodecResult codecResult,
            GASNetworkHandshakeResult handshakeResult,
            NetworkSendStatus sendStatus,
            Exception exception)
        {
            var failure = new GASNetworkEndpointFailure(
                kind,
                connection,
                messageId,
                direction,
                codecResult,
                handshakeResult,
                sendStatus,
                exception);

            if (reportingFailure)
                throw new InvalidOperationException("The GAS network failure callback cannot be reentered.");

            reportingFailure = true;
            try
            {
                failureHandler(in failure);
            }
            finally
            {
                reportingFailure = false;
            }
        }

        private bool InvalidatePeerAndNotify(
            INetConnection connection,
            out Exception callbackException)
        {
            callbackException = null;
            if (Role == GASNetworkEndpointRole.Authority)
            {
                if (connection == null ||
                    !authorityPeers.TryGetValue(connection, out PeerHandshakeState state))
                {
                    return false;
                }

                authorityPeers.Remove(connection);
                if (!state.RemoteCompatible)
                    return true;

                observedAuthorityPeers.Remove(connection);
                try
                {
                    authoritySink.OnClientDisconnected(connection);
                }
                catch (Exception exception)
                {
                    callbackException = exception;
                }
                return true;
            }

            if (clientAuthorityObserved)
            {
                if (!IsSameConnection(clientAuthority, connection))
                    return false;

                INetConnection authority = clientAuthority;
                ClearClientHandshake();
                try
                {
                    clientSink.OnAuthorityDisconnected(authority);
                }
                catch (Exception exception)
                {
                    callbackException = exception;
                }
                return true;
            }

            bool hadState = clientHandshake.LocalSent ||
                            clientHandshake.RemoteCompatible ||
                            clientAuthority != null;
            ClearClientHandshake();
            return hadState;
        }

        private void HandleClientDisconnected(INetConnection client)
        {
            EnsureOwnerThread();
            if (disposed)
                return;

            InvalidatePeerAndNotify(client, out Exception callbackException);
            if (callbackException != null)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.MessageHandlerException,
                    client,
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    NetworkMessageDirection.ClientToServer,
                    GASNetworkWireCodecResult.Invalid,
                    GASNetworkHandshakeResult.Invalid,
                    NetworkSendStatus.Invalid,
                    callbackException);
            }
        }

        private void HandleDisconnectedFromAuthority()
        {
            EnsureOwnerThread();
            if (disposed)
                return;

            INetConnection authority = clientAuthority;
            InvalidatePeerAndNotify(authority, out Exception callbackException);
            if (callbackException != null)
            {
                ReportFailure(
                    GASNetworkEndpointFailureKind.MessageHandlerException,
                    authority,
                    GameplayAbilitiesNetworkProtocol.HandshakeMessageId,
                    NetworkMessageDirection.ServerToClient,
                    GASNetworkWireCodecResult.Invalid,
                    GASNetworkHandshakeResult.Invalid,
                    NetworkSendStatus.Invalid,
                    callbackException);
            }
        }

        private static Exception CombineExceptions(
            Exception first,
            Exception second,
            Exception third = null)
        {
            if (first == null)
                return second == null ? third : third == null ? second : new AggregateException(second, third);
            if (second == null)
                return third == null ? first : new AggregateException(first, third);
            return third == null
                ? new AggregateException(first, second)
                : new AggregateException(first, second, third);
        }

        private void ClearClientHandshake()
        {
            clientHandshake = default;
            clientAuthority = null;
            clientAuthorityObserved = false;
        }

        private static NetworkSendResult CodecFailure(
            GASNetworkWireCodecResult codec,
            INetConnection connection)
        {
            NetworkSendStatus status = codec == GASNetworkWireCodecResult.PayloadTooLarge
                ? NetworkSendStatus.PayloadTooLarge
                : NetworkSendStatus.InvalidPayload;
            return NetworkSendResult.Fail(status, connection: connection, reason: InvalidMessageReason);
        }

        private static NetworkSendResult InvalidMessage(INetConnection connection = null)
        {
            return NetworkSendResult.Fail(
                NetworkSendStatus.InvalidPayload,
                connection: connection,
                reason: InvalidMessageReason);
        }

        private static NetworkSendResult HandshakeRequired(INetConnection connection = null)
        {
            return NetworkSendResult.Fail(
                NetworkSendStatus.NotConnected,
                connection: connection,
                reason: HandshakeRequiredReason);
        }

        private static NetworkSendResult PeerUnavailable(INetConnection connection)
        {
            return NetworkSendResult.Fail(
                NetworkSendStatus.NotConnected,
                connection: connection,
                reason: PeerUnavailableReason);
        }

        private void EnsureRole(GASNetworkEndpointRole expected)
        {
            if (Role != expected)
            {
                throw new InvalidOperationException(
                    $"This GAS network endpoint has role '{Role}', but the operation requires '{expected}'.");
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != ownerThreadId)
                throw new InvalidOperationException("GASNetworkEndpoint is owner-thread-affine.");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GASNetworkEndpoint));
        }
    }
}
