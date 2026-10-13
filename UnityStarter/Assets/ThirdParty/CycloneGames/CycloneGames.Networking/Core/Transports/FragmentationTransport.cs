using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Transports
{
    /// <summary>
    /// Transport decorator that splits payloads exceeding the effective MTU into
    /// sequenced fragments and reassembles them on the receiving side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ownership/threading: same owner-thread contract as <see cref="INetTransport"/>.
    /// All sends, receives and <see cref="PollEvents"/> must run on the owner thread;
    /// no internal locking is performed.
    /// </para>
    /// <para>
    /// Allocation: the unfragmented fast path is pass-through with zero copies and zero
    /// allocation. The fragmented path rents buffers from <see cref="ArrayPool{T}.Shared"/>
    /// and returns them after use; steady-state allocation is bounded by pool warmup.
    /// </para>
    /// <para>
    /// Safety: reassembly is bounded by <see cref="FragmentationOptions.MaxReassembledBytes"/>,
    /// <see cref="FragmentationOptions.MaxConcurrentGroups"/> and
    /// <see cref="FragmentationOptions.GroupTimeoutSeconds"/>. Malformed or hostile
    /// fragments are dropped without state growth.
    /// </para>
    /// </remarks>
    public sealed class FragmentationTransport : IPollableTransport, IDisposable
    {
        // Fragment header layout (little-endian, 16 bytes), at the start of each fragment payload:
        //   magic "CF" (2) | version (1) | reserved (1) | groupId u32 (4) | index u16 (2) | count u16 (2) | totalLength i32 (4)
        private const byte FragMagic0 = (byte)'C';
        private const byte FragMagic1 = (byte)'F';
        private const byte FragVersion = 1;
        public const int FragmentHeaderSize = 16;
        public const int MaxFragmentCount = 256;

        private readonly INetTransport _inner;
        private readonly FragmentationOptions _options;
        private readonly int _maxUnfragmentedPayload;
        private readonly Dictionary<ulong, ReassemblyGroup> _groups;
        private readonly List<ulong> _expiredKeys;
        private uint _nextGroupId;
        private int _reassemblyBytesInFlight;
        private double _lastPruneTime;
        private bool _disposed;

        public FragmentationTransport(INetTransport inner) : this(inner, FragmentationOptions.Default) { }

        public FragmentationTransport(INetTransport inner, in FragmentationOptions options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            options.Validate();
            _options = options;
            _maxUnfragmentedPayload = Math.Max(64, options.Mtu - FragmentHeaderSize);
            _groups = new Dictionary<ulong, ReassemblyGroup>(16);
            _expiredKeys = new List<ulong>(16);
            _nextGroupId = 1;

            _inner.OnClientConnected += HandleClientConnected;
            _inner.OnClientDisconnected += HandleClientDisconnected;
            _inner.OnConnectedToServer += HandleConnectedToServer;
            _inner.OnDisconnectedFromServer += HandleDisconnectedFromServer;
            _inner.OnError += HandleError;
            _inner.OnDataReceived += HandleDataReceived;
        }

        public FragmentationMetrics Metrics
        {
            get
            {
                return new FragmentationMetrics(_groups.Count, _reassemblyBytesInFlight);
            }
        }

        #region INetTransport delegation

        public bool IsServer => _inner.IsServer;
        public bool IsClient => _inner.IsClient;
        public bool IsRunning => _inner.IsRunning;
        public bool IsEncrypted => _inner.IsEncrypted;
        public bool Available => _inner.Available;

        public NetworkTransportCapabilities Capabilities
        {
            get
            {
                NetworkTransportCapabilities inner = _inner.Capabilities;
                return new NetworkTransportCapabilities(
                    inner.RuntimeName,
                    inner.Features | NetworkTransportFeatureFlags.Fragmentation,
                    inner.SupportedChannels,
                    inner.MaxConnections,
                    _options.MaxReassembledBytes,
                    _options.MaxReassembledBytes,
                    inner.MaxUnreliablePacketSize,
                    inner.MaxQueuedPackets,
                    inner.IsDeterministicLoopback);
            }
        }

        public int GetChannelId(NetworkChannel channel) => _inner.GetChannelId(channel);
        public int GetMaxPacketSize(int channelId) => _options.MaxReassembledBytes;
        public NetworkStatistics GetStatistics() => _inner.GetStatistics();

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        public void StartServer() => _inner.StartServer();
        public void StartClient(string address) => _inner.StartClient(address);

        public void Stop()
        {
            ReleaseAllGroups();
            _inner.Stop();
        }

        public void Disconnect(INetConnection connection)
        {
            if (connection != null)
                DropGroupsForConnection(connection.ConnectionId);
            _inner.Disconnect(connection);
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            // A null connection is legal for connection-less transports (e.g. LocalLoop); the
            // inner transport decides whether the route is valid.
            if (payload.Array == null || payload.Count < 0)
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection);

            if (payload.Count <= _maxUnfragmentedPayload)
                return _inner.Send(connection, payload, channelId);

            if (payload.Count > _options.MaxReassembledBytes)
                return NetworkSendResult.Fail(NetworkSendStatus.PayloadTooLarge, channelId, connection);

            return SendFragmented(connection, payload, channelId);
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            if (connections == null || connections.Count == 0)
                return NetworkSendResult.Broadcast(NetworkSendStatus.NotConnected, 0, 0, channelId);

            if (payload.Count <= _maxUnfragmentedPayload)
                return _inner.Broadcast(connections, payload, channelId);

            if (payload.Count > _options.MaxReassembledBytes)
                return NetworkSendResult.Broadcast(NetworkSendStatus.PayloadTooLarge, 0, 0, channelId);

            // Fragment once into pooled buffers, then unicast the same fragments to every
            // recipient so the encode cost is paid a single time per broadcast.
            int fragmentCount = ComputeFragmentCount(payload.Count);
            byte[][] fragmentBuffers = null;
            int rented = 0;
            int totalAccepted = 0;
            int recipientsAccepted = 0;
            NetworkSendStatus firstFailure = NetworkSendStatus.Accepted;
            bool failed = false;

            try
            {
                fragmentBuffers = RentFragmentSet(payload, fragmentCount, channelId, out int fragmentPayloadSize);
                rented = fragmentCount;

                for (int c = 0; c < connections.Count; c++)
                {
                    INetConnection connection = connections[c];
                    if (connection == null)
                    {
                        failed = true;
                        firstFailure = NetworkSendStatus.NotConnected;
                        continue;
                    }

                    bool connectionOk = true;
                    for (int f = 0; f < fragmentCount; f++)
                    {
                        var segment = new ArraySegment<byte>(fragmentBuffers[f], 0, FragmentHeaderSize + FragmentPayloadLength(payload.Count, f, fragmentPayloadSize));
                        NetworkSendResult result = _inner.Send(connection, segment, channelId);
                        if (!result.Succeeded)
                        {
                            connectionOk = false;
                            failed = true;
                            if (firstFailure == NetworkSendStatus.Accepted)
                                firstFailure = result.Status;
                            break;
                        }
                    }

                    if (connectionOk)
                    {
                        recipientsAccepted++;
                        totalAccepted += payload.Count;
                    }
                }
            }
            finally
            {
                if (fragmentBuffers != null)
                {
                    for (int i = 0; i < rented; i++)
                    {
                        if (fragmentBuffers[i] != null)
                            ArrayPool<byte>.Shared.Return(fragmentBuffers[i]);
                    }
                }
            }

            if (recipientsAccepted == 0)
                return NetworkSendResult.Broadcast(failed ? firstFailure : NetworkSendStatus.NotConnected, 0, 0, channelId);

            return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, totalAccepted, recipientsAccepted, channelId);
        }

        #endregion

        #region Fragment send path

        // Fragment geometry is canonical: count = ceil(total / maxChunk), then
        // chunkSize = ceil(total / count). Both peers derive identical offsets from
        // (totalLength, count) in the header alone, so no chunk-size field is needed.
        private int ComputeFragmentCount(int payloadLength)
        {
            int count = (payloadLength + _maxUnfragmentedPayload - 1) / _maxUnfragmentedPayload;
            return Math.Min(count, MaxFragmentCount);
        }

        private static int CanonicalChunkSize(int totalLength, int fragmentCount)
        {
            return (totalLength + fragmentCount - 1) / fragmentCount;
        }

        private static int FragmentPayloadLength(int totalLength, int index, int chunkSize)
        {
            int remaining = totalLength - index * chunkSize;
            return Math.Min(remaining, chunkSize);
        }

        private NetworkSendResult SendFragmented(INetConnection connection, ArraySegment<byte> payload, int channelId)
        {
            int fragmentCount = ComputeFragmentCount(payload.Count);
            int chunkSize = CanonicalChunkSize(payload.Count, fragmentCount);
            uint groupId = NextGroupId();
            byte[] buffer = null;
            int bytesAccepted = 0;

            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(FragmentHeaderSize + chunkSize);

                for (int i = 0; i < fragmentCount; i++)
                {
                    int chunkLength = FragmentPayloadLength(payload.Count, i, chunkSize);
                    WriteFragmentHeader(buffer, 0, groupId, (ushort)i, (ushort)fragmentCount, payload.Count);
                    Buffer.BlockCopy(payload.Array, payload.Offset + i * chunkSize, buffer, FragmentHeaderSize, chunkLength);

                    var segment = new ArraySegment<byte>(buffer, 0, FragmentHeaderSize + chunkLength);
                    NetworkSendResult result = _inner.Send(connection, segment, channelId);
                    if (!result.Succeeded)
                        return result.Status == NetworkSendStatus.Accepted
                            ? NetworkSendResult.Fail(NetworkSendStatus.Invalid, channelId, connection)
                            : NetworkSendResult.Fail(result.Status, channelId, connection);

                    bytesAccepted += chunkLength;
                }

                return NetworkSendResult.Accepted(bytesAccepted, channelId, connection);
            }
            finally
            {
                if (buffer != null)
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: _options.ClearBuffersOnReturn);
            }
        }

        private byte[][] RentFragmentSet(ArraySegment<byte> payload, int fragmentCount, int channelId, out int chunkSize)
        {
            chunkSize = CanonicalChunkSize(payload.Count, fragmentCount);
            uint groupId = NextGroupId();
            var buffers = new byte[fragmentCount][];
            try
            {
                for (int i = 0; i < fragmentCount; i++)
                {
                    int chunkLength = FragmentPayloadLength(payload.Count, i, chunkSize);
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(FragmentHeaderSize + chunkLength);
                    WriteFragmentHeader(buffer, 0, groupId, (ushort)i, (ushort)fragmentCount, payload.Count);
                    Buffer.BlockCopy(payload.Array, payload.Offset + i * chunkSize, buffer, FragmentHeaderSize, chunkLength);
                    buffers[i] = buffer;
                }
                return buffers;
            }
            catch
            {
                for (int i = 0; i < fragmentCount; i++)
                {
                    if (buffers[i] != null)
                        ArrayPool<byte>.Shared.Return(buffers[i]);
                }
                throw;
            }
        }

        private uint NextGroupId()
        {
            uint id = _nextGroupId++;
            if (_nextGroupId == 0u)
                _nextGroupId = 1u;
            return id;
        }

        private static void WriteFragmentHeader(byte[] buffer, int offset, uint groupId, ushort index, ushort count, int totalLength)
        {
            buffer[offset] = FragMagic0;
            buffer[offset + 1] = FragMagic1;
            buffer[offset + 2] = FragVersion;
            buffer[offset + 3] = 0;
            WriteU32(buffer, offset + 4, groupId);
            WriteU16(buffer, offset + 8, index);
            WriteU16(buffer, offset + 10, count);
            WriteI32(buffer, offset + 12, totalLength);
        }

        #endregion

        #region Fragment receive path

        private void HandleDataReceived(INetConnection connection, ArraySegment<byte> payload, int channelId)
        {
            if (!TryReadFragmentHeader(payload, out FragmentHeader header))
            {
                OnDataReceived?.Invoke(connection, payload, channelId);
                return;
            }

            if (!ValidateFragmentHeader(header))
                return; // Hostile or corrupt fragment: drop silently, no state growth.

            ulong key = MakeGroupKey(connection != null ? connection.ConnectionId : 0, header.GroupId);
            if (!_groups.TryGetValue(key, out ReassemblyGroup group))
            {
                if (_groups.Count >= _options.MaxConcurrentGroups)
                    PruneExpiredGroups(forceOne: true);
                if (_groups.Count >= _options.MaxConcurrentGroups)
                    return;
                if (_reassemblyBytesInFlight + header.TotalLength > _options.MaxReassemblyBytesInFlight)
                    return;

                group = ReassemblyGroup.Create(header.TotalLength, header.Count, NowSeconds());
                if (group == null)
                    return; // Allocation refused by pool or invalid size.

                _groups.Add(key, group);
                _reassemblyBytesInFlight += header.TotalLength;
            }
            else if (group.TotalLength != header.TotalLength || group.FragmentCount != header.Count)
            {
                // Group metadata mismatch indicates spoofing or a reused group id.
                RemoveGroup(key, group);
                return;
            }

            int fragmentLength = payload.Count - FragmentHeaderSize;
            if (fragmentLength <= 0 || fragmentLength > _maxUnfragmentedPayload)
                return;
            if (!group.AcceptFragment(header.Index, payload.Array, payload.Offset + FragmentHeaderSize, fragmentLength, NowSeconds()))
                return;

            if (!group.IsComplete)
                return;

            DeliverReassembled(connection, key, group, channelId);
        }

        private void DeliverReassembled(INetConnection connection, ulong key, ReassemblyGroup group, int channelId)
        {
            _groups.Remove(key);
            _reassemblyBytesInFlight -= group.TotalLength;

            var segment = new ArraySegment<byte>(group.Buffer, 0, group.TotalLength);
            try
            {
                OnDataReceived?.Invoke(connection, segment, channelId);
            }
            finally
            {
                group.Release(_options.ClearBuffersOnReturn);
            }
        }

        private static ulong MakeGroupKey(int connectionId, uint groupId)
        {
            return ((ulong)(uint)connectionId << 32) | groupId;
        }

        private static bool TryReadFragmentHeader(ArraySegment<byte> payload, out FragmentHeader header)
        {
            header = default;
            if (payload.Array == null || payload.Count < FragmentHeaderSize)
                return false;
            byte[] a = payload.Array;
            int o = payload.Offset;
            if (a[o] != FragMagic0 || a[o + 1] != FragMagic1)
                return false;
            if (a[o + 2] != FragVersion || a[o + 3] != 0)
                return false;

            header = new FragmentHeader(
                ReadU32(a, o + 4),
                ReadU16(a, o + 8),
                ReadU16(a, o + 10),
                ReadI32(a, o + 12));
            return true;
        }

        private bool ValidateFragmentHeader(in FragmentHeader header)
        {
            if (header.GroupId == 0u)
                return false;
            if (header.Count < 2 || header.Count > MaxFragmentCount)
                return false;
            if (header.Index >= header.Count)
                return false;
            if (header.TotalLength <= _maxUnfragmentedPayload || header.TotalLength > _options.MaxReassembledBytes)
                return false;
            if ((long)header.Count * _maxUnfragmentedPayload < header.TotalLength)
                return false;
            return true;
        }

        #endregion

        #region Reassembly group lifecycle

        private void DropGroupsForConnection(int connectionId)
        {
            if (_groups.Count == 0)
                return;

            _expiredKeys.Clear();
            foreach (var pair in _groups)
            {
                if ((int)(pair.Key >> 32) == connectionId)
                    _expiredKeys.Add(pair.Key);
            }

            for (int i = 0; i < _expiredKeys.Count; i++)
            {
                ulong key = _expiredKeys[i];
                if (_groups.TryGetValue(key, out ReassemblyGroup group))
                    RemoveGroup(key, group);
            }
            _expiredKeys.Clear();
        }

        private void PruneExpiredGroups(bool forceOne)
        {
            double now = NowSeconds();
            _expiredKeys.Clear();
            foreach (var pair in _groups)
            {
                if (now - pair.Value.LastUpdateTime >= _options.GroupTimeoutSeconds)
                    _expiredKeys.Add(pair.Key);
            }

            if (forceOne && _expiredKeys.Count == 0 && _groups.Count > 0)
            {
                // Evict the oldest group to guarantee forward progress under pressure.
                ulong oldestKey = 0;
                double oldestTime = double.MaxValue;
                foreach (var pair in _groups)
                {
                    if (pair.Value.LastUpdateTime < oldestTime)
                    {
                        oldestTime = pair.Value.LastUpdateTime;
                        oldestKey = pair.Key;
                    }
                }
                _expiredKeys.Add(oldestKey);
            }

            for (int i = 0; i < _expiredKeys.Count; i++)
            {
                ulong key = _expiredKeys[i];
                if (_groups.TryGetValue(key, out ReassemblyGroup group))
                    RemoveGroup(key, group);
            }
            _expiredKeys.Clear();
        }

        private void RemoveGroup(ulong key, ReassemblyGroup group)
        {
            _groups.Remove(key);
            _reassemblyBytesInFlight -= group.TotalLength;
            group.Release(_options.ClearBuffersOnReturn);
        }

        private void ReleaseAllGroups()
        {
            foreach (var pair in _groups)
                pair.Value.Release(_options.ClearBuffersOnReturn);
            _groups.Clear();
            _reassemblyBytesInFlight = 0;
        }

        private static double NowSeconds()
        {
            return DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
        }

        public void PollEvents()
        {
            double now = NowSeconds();
            if (now - _lastPruneTime >= Math.Min(1d, _options.GroupTimeoutSeconds * 0.5d))
            {
                _lastPruneTime = now;
                PruneExpiredGroups(forceOne: false);
            }

            if (_inner is IPollableTransport pollable)
                pollable.PollEvents();
        }

        #endregion

        #region Event forwarding

        private void HandleClientConnected(INetConnection connection) => OnClientConnected?.Invoke(connection);

        private void HandleClientDisconnected(INetConnection connection)
        {
            if (connection != null)
                DropGroupsForConnection(connection.ConnectionId);
            OnClientDisconnected?.Invoke(connection);
        }

        private void HandleConnectedToServer() => OnConnectedToServer?.Invoke();

        private void HandleDisconnectedFromServer()
        {
            ReleaseAllGroups();
            OnDisconnectedFromServer?.Invoke();
        }

        private void HandleError(INetConnection connection, TransportError error, string message)
        {
            OnError?.Invoke(connection, error, message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _inner.OnClientConnected -= HandleClientConnected;
            _inner.OnClientDisconnected -= HandleClientDisconnected;
            _inner.OnConnectedToServer -= HandleConnectedToServer;
            _inner.OnDisconnectedFromServer -= HandleDisconnectedFromServer;
            _inner.OnError -= HandleError;
            _inner.OnDataReceived -= HandleDataReceived;
            ReleaseAllGroups();
            if (_inner is IDisposable d)
                d.Dispose();
        }

        #endregion

        #region Binary helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadI32(byte[] b, int o) =>
            b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteI32(byte[] b, int o, int v) => WriteU32(b, o, unchecked((uint)v));

        #endregion

        private readonly struct FragmentHeader
        {
            public readonly uint GroupId;
            public readonly ushort Index;
            public readonly ushort Count;
            public readonly int TotalLength;

            public FragmentHeader(uint groupId, ushort index, ushort count, int totalLength)
            {
                GroupId = groupId;
                Index = index;
                Count = count;
                TotalLength = totalLength;
            }
        }

        /// <summary>
        /// Per-group reassembly state. Buffer ownership: rented from ArrayPool on create,
        /// returned exactly once via <see cref="Release"/>. The completed payload is only
        /// valid during the delivery callback.
        /// </summary>
        private sealed class ReassemblyGroup
        {
            private const int BitmapWords = MaxFragmentCount / 64;

            private readonly ulong[] _receivedBitmap;
            private int _receivedCount;
            private int _receivedBytes;

            public byte[] Buffer;
            public int TotalLength;
            public ushort FragmentCount;
            public double LastUpdateTime;

            private ReassemblyGroup(int totalLength, ushort fragmentCount, double now)
            {
                TotalLength = totalLength;
                FragmentCount = fragmentCount;
                LastUpdateTime = now;
                _receivedBitmap = new ulong[BitmapWords];
            }

            public static ReassemblyGroup Create(int totalLength, ushort fragmentCount, double now)
            {
                if (totalLength <= 0)
                    return null;

                var group = new ReassemblyGroup(totalLength, fragmentCount, now)
                {
                    Buffer = ArrayPool<byte>.Shared.Rent(totalLength)
                };
                return group;
            }

            public bool IsComplete => _receivedCount == FragmentCount;

            public bool AcceptFragment(ushort index, byte[] source, int sourceOffset, int length, double now)
            {
                int word = index >> 6;
                ulong bit = 1UL << (index & 63);
                if ((_receivedBitmap[word] & bit) != 0UL)
                    return true; // Duplicate fragment: idempotent, already copied.

                int chunkSize = (TotalLength + FragmentCount - 1) / FragmentCount;
                int expectedOffset = index * chunkSize;
                if (expectedOffset + length > TotalLength)
                    return false;
                if (index < FragmentCount - 1 && length != chunkSize)
                    return false;

                System.Buffer.BlockCopy(source, sourceOffset, Buffer, expectedOffset, length);
                _receivedBitmap[word] |= bit;
                _receivedCount++;
                _receivedBytes += length;
                LastUpdateTime = now;

                if (IsComplete && _receivedBytes != TotalLength)
                    return false; // Internal inconsistency; caller drops the group.

                return true;
            }

            public void Release(bool clearBuffer)
            {
                if (Buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(Buffer, clearArray: clearBuffer);
                    Buffer = null;
                }
            }
        }
    }

    public readonly struct FragmentationOptions
    {
        public static FragmentationOptions Default => new FragmentationOptions(
            mtu: NetworkConstants.DefaultMTU,
            maxReassembledBytes: 256 * 1024,
            maxConcurrentGroups: 256,
            maxReassemblyBytesInFlight: 4 * 1024 * 1024,
            groupTimeoutSeconds: 5d,
            clearBuffersOnReturn: false);

        /// <summary>Effective MTU including the fragment header. Match the underlying link MTU.</summary>
        public readonly int Mtu;
        /// <summary>Hard cap on a single reassembled message.</summary>
        public readonly int MaxReassembledBytes;
        /// <summary>Hard cap on concurrent reassembly groups across all connections.</summary>
        public readonly int MaxConcurrentGroups;
        /// <summary>Hard cap on total bytes held by in-flight reassembly groups.</summary>
        public readonly int MaxReassemblyBytesInFlight;
        /// <summary>Idle time after which an incomplete group is discarded.</summary>
        public readonly double GroupTimeoutSeconds;
        /// <summary>Zero buffers before returning them to the pool. Enable when payloads are sensitive.</summary>
        public readonly bool ClearBuffersOnReturn;

        public FragmentationOptions(
            int mtu,
            int maxReassembledBytes,
            int maxConcurrentGroups,
            int maxReassemblyBytesInFlight,
            double groupTimeoutSeconds,
            bool clearBuffersOnReturn)
        {
            Mtu = mtu;
            MaxReassembledBytes = maxReassembledBytes;
            MaxConcurrentGroups = maxConcurrentGroups;
            MaxReassemblyBytesInFlight = maxReassemblyBytesInFlight;
            GroupTimeoutSeconds = groupTimeoutSeconds;
            ClearBuffersOnReturn = clearBuffersOnReturn;
        }

        public void Validate()
        {
            if (Mtu < 128 || Mtu > NetworkConstants.MaxMTU)
                throw new ArgumentOutOfRangeException(nameof(Mtu));
            if (MaxReassembledBytes <= Mtu)
                throw new ArgumentOutOfRangeException(nameof(MaxReassembledBytes));
            if (MaxConcurrentGroups <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxConcurrentGroups));
            if (MaxReassemblyBytesInFlight < MaxReassembledBytes)
                throw new ArgumentOutOfRangeException(nameof(MaxReassemblyBytesInFlight));
            if (GroupTimeoutSeconds <= 0d || double.IsNaN(GroupTimeoutSeconds) || double.IsInfinity(GroupTimeoutSeconds))
                throw new ArgumentOutOfRangeException(nameof(GroupTimeoutSeconds));
        }
    }

    public readonly struct FragmentationMetrics
    {
        public readonly int ActiveGroups;
        public readonly int ReassemblyBytesInFlight;

        public FragmentationMetrics(int activeGroups, int reassemblyBytesInFlight)
        {
            ActiveGroups = activeGroups;
            ReassemblyBytesInFlight = reassemblyBytesInFlight;
        }
    }
}
