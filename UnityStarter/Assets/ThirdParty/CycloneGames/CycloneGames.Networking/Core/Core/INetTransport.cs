using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Standardized Quality of Service (QoS) channels for game networking.
    /// </summary>
    public enum NetworkChannel
    {
        /// <summary>
        /// Guaranteed delivery, ordered. Use for critical gameplay events (RPCs, Spawning).
        /// </summary>
        Reliable,

        /// <summary>
        /// No guarantee, unordered (or sequenced). Use for high-frequency data (Position/Rotation updates).
        /// </summary>
        Unreliable,

        /// <summary>
        /// Reliable but unordered. Good for file transfer or chat where global order doesn't matter.
        /// </summary>
        ReliableUnordered,

        /// <summary>
        /// Unreliable but sequenced (older packets are dropped). Good for VoIP.
        /// </summary>
        UnreliableSequenced
    }

    public enum NetworkLifecycleState : byte
    {
        Unknown,
        Unavailable,
        Stopped,
        StartingServer,
        StartingClient,
        ServerRunning,
        ClientRunning,
        HostRunning,
        Stopping,
        Faulted,
        Disposed
    }

    public readonly struct NetworkLifecycleSnapshot
    {
        public readonly NetworkLifecycleState State;
        public readonly NetworkBackendFeatures Features;
        public readonly TransportError LastError;
        public readonly string LastErrorMessage;
        public readonly bool IsAvailable;
        public readonly bool IsRunning;
        public readonly bool IsServer;
        public readonly bool IsClient;
        public readonly bool IsEncrypted;

        public NetworkLifecycleSnapshot(
            NetworkLifecycleState state,
            NetworkBackendFeatures features,
            TransportError lastError,
            string lastErrorMessage,
            bool isAvailable,
            bool isRunning,
            bool isServer,
            bool isClient,
            bool isEncrypted)
        {
            State = state;
            Features = features;
            LastError = lastError;
            LastErrorMessage = lastErrorMessage ?? string.Empty;
            IsAvailable = isAvailable;
            IsRunning = isRunning;
            IsServer = isServer;
            IsClient = isClient;
            IsEncrypted = isEncrypted;
        }

        public bool HasFeature(NetworkBackendFeatures feature)
        {
            return (Features & feature) == feature;
        }
    }

    public interface INetworkLifecycleProvider
    {
        NetworkLifecycleSnapshot GetLifecycleSnapshot();
    }

    public interface INetworkFeatureProvider
    {
        NetworkBackendFeatures Features { get; }
    }

    public static class NetworkLifecycle
    {
        public static NetworkLifecycleSnapshot GetSnapshot(INetTransport transport)
        {
            if (transport == null)
            {
                return new NetworkLifecycleSnapshot(
                    NetworkLifecycleState.Unavailable,
                    NetworkBackendFeatures.None,
                    TransportError.None,
                    string.Empty,
                    false,
                    false,
                    false,
                    false,
                    false);
            }

            if (transport is INetworkLifecycleProvider provider)
                return provider.GetLifecycleSnapshot();

            NetworkBackendFeatures features = NetworkBackendFeatures.RealtimeTransport;
            if (transport is INetworkFeatureProvider featureProvider)
                features = featureProvider.Features;

            return new NetworkLifecycleSnapshot(
                GetTransportState(transport),
                features,
                TransportError.None,
                string.Empty,
                transport.Available,
                transport.IsRunning,
                transport.IsServer,
                transport.IsClient,
                transport.IsEncrypted);
        }

        public static NetworkLifecycleState GetTransportState(INetTransport transport)
        {
            if (transport == null || !transport.Available)
                return NetworkLifecycleState.Unavailable;

            if (!transport.IsRunning)
                return NetworkLifecycleState.Stopped;

            if (transport.IsServer && transport.IsClient)
                return NetworkLifecycleState.HostRunning;

            if (transport.IsServer)
                return NetworkLifecycleState.ServerRunning;

            if (transport.IsClient)
                return NetworkLifecycleState.ClientRunning;

            return NetworkLifecycleState.Unknown;
        }
    }

    public readonly struct NetworkTickId : IEquatable<NetworkTickId>, IComparable<NetworkTickId>
    {
        public static readonly NetworkTickId Invalid = new NetworkTickId(-1);
        public static readonly NetworkTickId Zero = new NetworkTickId(0);

        public readonly long Value;

        public NetworkTickId(long value)
        {
            Value = value;
        }

        public bool IsValid => Value >= 0L;

        public int CompareTo(NetworkTickId other) => Value.CompareTo(other.Value);
        public bool Equals(NetworkTickId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NetworkTickId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static NetworkTickId operator +(NetworkTickId tick, long delta) => new NetworkTickId(tick.Value + delta);
        public static NetworkTickId operator -(NetworkTickId tick, long delta) => new NetworkTickId(tick.Value - delta);
        public static long operator -(NetworkTickId a, NetworkTickId b) => a.Value - b.Value;
        public static bool operator ==(NetworkTickId a, NetworkTickId b) => a.Value == b.Value;
        public static bool operator !=(NetworkTickId a, NetworkTickId b) => a.Value != b.Value;
        public static bool operator <(NetworkTickId a, NetworkTickId b) => a.Value < b.Value;
        public static bool operator >(NetworkTickId a, NetworkTickId b) => a.Value > b.Value;
        public static bool operator <=(NetworkTickId a, NetworkTickId b) => a.Value <= b.Value;
        public static bool operator >=(NetworkTickId a, NetworkTickId b) => a.Value >= b.Value;
    }

    public readonly struct NetworkTickRate
    {
        public readonly int TicksPerSecond;
        public readonly double SecondsPerTick;

        public NetworkTickRate(int ticksPerSecond)
        {
            if (ticksPerSecond < NetworkConstants.MinTickRate || ticksPerSecond > NetworkConstants.MaxTickRate)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));

            TicksPerSecond = ticksPerSecond;
            SecondsPerTick = 1d / ticksPerSecond;
        }

        public static NetworkTickRate Default => new NetworkTickRate(NetworkConstants.DefaultTickRate);

        public NetworkTickId SecondsToTick(double seconds)
        {
            if (seconds <= 0d)
                return NetworkTickId.Zero;

            return new NetworkTickId((long)Math.Floor(seconds * TicksPerSecond));
        }

        public double TickToSeconds(NetworkTickId tick)
        {
            return tick.Value * SecondsPerTick;
        }
    }

    public interface INetworkTimeSource
    {
        NetworkTickRate TickRate { get; }
        NetworkTickId LocalTick { get; }
        double LocalTimeSeconds { get; }
    }

    public sealed class ManualNetworkTimeSource : INetworkTimeSource
    {
        private double _localTimeSeconds;

        public ManualNetworkTimeSource(NetworkTickRate tickRate)
        {
            TickRate = tickRate;
        }

        public NetworkTickRate TickRate { get; }
        public NetworkTickId LocalTick => TickRate.SecondsToTick(_localTimeSeconds);
        public double LocalTimeSeconds => _localTimeSeconds;

        public void Reset(double localTimeSeconds = 0d)
        {
            _localTimeSeconds = Math.Max(0d, localTimeSeconds);
        }

        public void Advance(double deltaSeconds)
        {
            if (deltaSeconds <= 0d)
                return;

            _localTimeSeconds += deltaSeconds;
        }
    }

    [Flags]
    public enum NetworkSnapshotFlags : byte
    {
        None = 0,
        FullState = 1 << 0,
        Delta = 1 << 1,
        BaselineReset = 1 << 2,
        Compressed = 1 << 3
    }

    public readonly struct NetworkSnapshotHeader
    {
        public readonly NetworkTickId Tick;
        public readonly NetworkTickId BaselineTick;
        public readonly uint Sequence;
        public readonly ushort EntityCount;
        public readonly NetworkSnapshotFlags Flags;

        public NetworkSnapshotHeader(
            NetworkTickId tick,
            NetworkTickId baselineTick,
            uint sequence,
            ushort entityCount,
            NetworkSnapshotFlags flags)
        {
            Tick = tick;
            BaselineTick = baselineTick;
            Sequence = sequence;
            EntityCount = entityCount;
            Flags = flags;
        }

        public bool IsDelta => (Flags & NetworkSnapshotFlags.Delta) != 0;
        public bool IsFullState => (Flags & NetworkSnapshotFlags.FullState) != 0;

        public bool IsValid()
        {
            if (!Tick.IsValid)
                return false;

            if (IsDelta && !BaselineTick.IsValid)
                return false;

            if (IsDelta && BaselineTick >= Tick)
                return false;

            return true;
        }
    }

    public readonly struct NetworkSnapshotAck
    {
        public readonly NetworkTickId Tick;
        public readonly uint Sequence;

        public NetworkSnapshotAck(NetworkTickId tick, uint sequence)
        {
            Tick = tick;
            Sequence = sequence;
        }

        public bool IsValid => Tick.IsValid;
    }

    public readonly struct NetworkSnapshotRequest
    {
        public readonly INetConnection Connection;
        public readonly NetworkTickId TargetTick;
        public readonly NetworkTickId BaselineTick;
        public readonly int MaxPayloadBytes;
        public readonly bool ForceFullState;

        public NetworkSnapshotRequest(
            INetConnection connection,
            NetworkTickId targetTick,
            NetworkTickId baselineTick,
            int maxPayloadBytes,
            bool forceFullState)
        {
            Connection = connection;
            TargetTick = targetTick;
            BaselineTick = baselineTick;
            MaxPayloadBytes = maxPayloadBytes;
            ForceFullState = forceFullState;
        }
    }

    public interface INetworkSnapshotProvider
    {
        bool TryWriteSnapshot(in NetworkSnapshotRequest request, Serialization.INetWriter writer, out NetworkSnapshotHeader header);
    }

    public interface INetworkSnapshotApplier
    {
        bool TryApplySnapshot(in NetworkSnapshotHeader header, Serialization.INetReader reader);
    }

    public interface INetworkSnapshotAckStore
    {
        bool TryGetLastAck(INetConnection connection, out NetworkSnapshotAck ack);
        void SetLastAck(INetConnection connection, in NetworkSnapshotAck ack);
        void Remove(INetConnection connection);
    }

    public sealed class NetworkSnapshotAckStore : INetworkSnapshotAckStore
    {
        private readonly Dictionary<int, NetworkSnapshotAck> _acks;

        public NetworkSnapshotAckStore(int capacity = 16)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _acks = new Dictionary<int, NetworkSnapshotAck>(capacity);
        }

        public int Count => _acks.Count;

        public bool TryGetLastAck(INetConnection connection, out NetworkSnapshotAck ack)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            return _acks.TryGetValue(connection.ConnectionId, out ack);
        }

        public void SetLastAck(INetConnection connection, in NetworkSnapshotAck ack)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (!ack.IsValid)
            {
                _acks.Remove(connection.ConnectionId);
                return;
            }

            _acks[connection.ConnectionId] = ack;
        }

        public void Remove(INetConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            _acks.Remove(connection.ConnectionId);
        }

        public void Clear()
        {
            _acks.Clear();
        }
    }

    public readonly struct NetworkInterestTarget
    {
        public readonly uint EntityId;
        public readonly NetworkVector3 Position;
        public readonly uint LayerMask;
        public readonly ulong OwnerId;
        public readonly int TeamId;

        public NetworkInterestTarget(uint entityId, NetworkVector3 position, uint layerMask = uint.MaxValue, ulong ownerId = 0UL, int teamId = 0)
        {
            EntityId = entityId;
            Position = position;
            LayerMask = layerMask;
            OwnerId = ownerId;
            TeamId = teamId;
        }

        public bool IsValid => EntityId != 0u && Position.IsFinite();
    }

    public readonly struct NetworkInterestObserver
    {
        public readonly INetConnection Connection;
        public readonly NetworkVector3 Position;
        public readonly float Radius;
        public readonly uint LayerMask;
        public readonly ulong PlayerId;
        public readonly int TeamId;

        public NetworkInterestObserver(
            INetConnection connection,
            NetworkVector3 position,
            float radius,
            uint layerMask = uint.MaxValue,
            ulong playerId = 0UL,
            int teamId = 0)
        {
            Connection = connection;
            Position = position;
            Radius = radius;
            LayerMask = layerMask;
            PlayerId = playerId;
            TeamId = teamId;
        }

        public bool IsValid => Radius >= 0f && Position.IsFinite();
    }

    public interface INetworkInterestRule
    {
        bool IsInterested(in NetworkInterestObserver observer, in NetworkInterestTarget target);
    }

    public sealed class DistanceInterestRule : INetworkInterestRule
    {
        public bool IsInterested(in NetworkInterestObserver observer, in NetworkInterestTarget target)
        {
            if (!observer.IsValid || !target.IsValid)
                return false;

            if ((observer.LayerMask & target.LayerMask) == 0u)
                return false;

            float radius = observer.Radius;
            return NetworkVector3.SqrDistance(observer.Position, target.Position) <= radius * radius;
        }
    }

    public static class NetworkInterestUtility
    {
        public static int BuildVisibleSet(
            in NetworkInterestObserver observer,
            IReadOnlyList<NetworkInterestTarget> targets,
            INetworkInterestRule rule,
            IList<uint> visibleEntityIds)
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
            if (visibleEntityIds == null)
                throw new ArgumentNullException(nameof(visibleEntityIds));

            visibleEntityIds.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                NetworkInterestTarget target = targets[i];
                if (rule.IsInterested(observer, target))
                    visibleEntityIds.Add(target.EntityId);
            }

            return visibleEntityIds.Count;
        }
    }

    /// <summary>
    /// Low-level transport interface responsible for raw byte delivery and connection lifecycle.
    /// </summary>
    public interface INetTransport
    {
        bool IsServer { get; }
        bool IsClient { get; }
        bool IsRunning { get; }
        bool IsEncrypted { get; }

        /// <summary>
        /// True if this transport is available on the current platform.
        /// </summary>
        bool Available { get; }

        // --- Channels ---

        /// <summary>
        /// Maps a standardized channel type to the underlying transport's integer channel ID.
        /// </summary>
        int GetChannelId(NetworkChannel channel);

        /// <summary>
        /// Maximum payload size for the given channel. Use for payload validation.
        /// </summary>
        int GetMaxPacketSize(int channelId);

        // --- Diagnostics ---

        /// <summary>
        /// Get current transport statistics for monitoring and debugging.
        /// </summary>
        NetworkStatistics GetStatistics();

        // --- Lifecycle Events ---

        /// <summary>
        /// Invoked on Server when a client connects.
        /// </summary>
        event Action<INetConnection> OnClientConnected;

        /// <summary>
        /// Invoked on Server when a client disconnects.
        /// </summary>
        event Action<INetConnection> OnClientDisconnected;

        /// <summary>
        /// Invoked on Client when successfully connected to server.
        /// </summary>
        event Action OnConnectedToServer;

        /// <summary>
        /// Invoked on Client when disconnected from server.
        /// </summary>
        event Action OnDisconnectedFromServer;

        /// <summary>
        /// Invoked when a transport error occurs. Connection may be null for client-side errors.
        /// </summary>
        event Action<INetConnection, TransportError, string> OnError;

        /// <summary>
        /// Invoked when a raw payload is received from the transport.
        /// The payload segment is only valid for the duration of the callback.
        /// </summary>
        event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        // --- Control ---

        void StartServer();
        void StartClient(string address);
        void Stop();

        /// <summary>
        /// Forcefully disconnects a connection (Server kicking client, or Client disconnecting self).
        /// </summary>
        void Disconnect(INetConnection connection);

        // --- Raw I/O ---

        /// <summary>
        /// Send a raw payload to a connection using given channel.
        /// Must be zero-allocation in hot paths.
        /// </summary>
        void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId);

        /// <summary>
        /// Broadcast to many connections using given channel.
        /// Implementations should batch for efficiency.
        /// </summary>
        void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId);
    }
}
