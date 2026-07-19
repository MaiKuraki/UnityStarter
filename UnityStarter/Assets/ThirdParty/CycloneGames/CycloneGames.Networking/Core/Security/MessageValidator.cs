using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.Hash.Core;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Validates incoming message payloads before deserialization.
    /// Prevents buffer overflow attacks, oversized messages, and malformed packets.
    /// 
    /// NOTE: This performs structural validation only (size, message ID range).
    /// It does NOT provide cryptographic integrity verification (HMAC/signatures).
    /// For authentication and tamper protection, use transport-level encryption (TLS/DTLS)
    /// or add application-level HMAC verification on top of this validator.
    /// </summary>
    public sealed class MessageValidator
    {
        public int MaxPayloadSize { get; set; }
        public int MinPayloadSize { get; set; }
        public ushort MaxMessageId { get; set; }

        public MessageValidator(
            int maxPayloadSize = NetworkConstants.MaxMTU,
            int minPayloadSize = 2,
            ushort maxMessageId = NetworkConstants.MaxMessageId)
        {
            MaxPayloadSize = maxPayloadSize;
            MinPayloadSize = minPayloadSize;
            MaxMessageId = maxMessageId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValidationResult Validate(ushort messageId, int payloadSize)
        {
            if (messageId > MaxMessageId)
                return ValidationResult.InvalidMessageId;

            if (payloadSize < MinPayloadSize)
                return ValidationResult.PayloadTooSmall;

            if (payloadSize > MaxPayloadSize)
                return ValidationResult.PayloadTooLarge;

            return ValidationResult.Valid;
        }

        /// <summary>
        /// Validate raw buffer integrity. Checks for minimum header presence.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ValidateBuffer(byte[] buffer, int offset, int length)
        {
            if (buffer == null) return false;
            if (offset < 0 || length <= 0) return false;
            if (offset > buffer.Length) return false;
            if (length > buffer.Length - offset) return false;
            return length >= MinPayloadSize && length <= MaxPayloadSize;
        }
    }

    public enum ValidationResult : byte
    {
        Invalid = 0,
        Valid = 1,
        InvalidMessageId = 2,
        PayloadTooSmall = 3,
        PayloadTooLarge = 4,
        MalformedHeader = 5
    }

    public enum NetworkMessageDirection : byte
    {
        Unknown = 0,
        ClientToServer = 1,
        ServerToClient = 2,
        ServerBroadcast = 3,
        PeerToPeer = 4
    }

    [Flags]
    public enum NetworkMessageDirectionMask : byte
    {
        None = 0,
        ClientToServer = 1 << 0,
        ServerToClient = 1 << 1,
        ServerBroadcast = 1 << 2,
        PeerToPeer = 1 << 3,
        Any = ClientToServer | ServerToClient | ServerBroadcast | PeerToPeer
    }

    [Flags]
    public enum NetworkMessageFlags : ushort
    {
        None = 0,
        Reliable = 1 << 0,
        Ordered = 1 << 1,
        Fragmented = 1 << 2,
        Compressed = 1 << 3,
        Encrypted = 1 << 4
    }

    /// <summary>
    /// Defines the stable Cyclone wire-frame header used by transport adapters.
    /// Header bytes are little-endian and immediately followed by the serialized payload.
    /// </summary>
    public static class NetworkWireProtocol
    {
        public const int UInt16Size = 2;
        public const int UInt32Size = 4;
        public const int Int32Size = 4;

        // The magic bytes are written as "CN" on the wire; the ushort value is little-endian for cheap reads.
        public const byte MagicByte0 = (byte)'C';
        public const byte MagicByte1 = (byte)'N';
        public const ushort Magic = (ushort)(MagicByte0 | (MagicByte1 << 8));
        public const byte CurrentVersion = 1;
        public const byte MinSupportedVersion = 1;
        public const int MagicOffset = 0;
        public const int VersionOffset = 2;
        public const int HeaderLengthOffset = 3;
        public const int FlagsOffset = 4;
        public const int MessageIdOffset = 6;
        public const int ChannelOffset = 8;
        public const int ReservedOffset = 9;
        public const int SequenceOffset = 10;
        public const int PayloadLengthOffset = 14;
        public const int ChecksumOffset = 18;
        public const int HeaderLength = ChecksumOffset + UInt32Size;
        public const NetworkMessageFlags KnownFlags = NetworkMessageFlags.Reliable
                                                       | NetworkMessageFlags.Ordered
                                                       | NetworkMessageFlags.Fragmented
                                                       | NetworkMessageFlags.Compressed
                                                       | NetworkMessageFlags.Encrypted;
        public const uint ChecksumSeed = Fnv1a32.OffsetBasis; // FNV-1a 32-bit offset basis.
        public const uint ChecksumPrime = Fnv1a32.Prime; // FNV-1a 32-bit prime.
    }

    /// <summary>
    /// Parsed Cyclone wire-frame header.
    /// </summary>
    public readonly struct NetworkEnvelopeHeader
    {
        public readonly byte Version;
        public readonly byte HeaderLength;
        public readonly NetworkMessageFlags Flags;
        public readonly ushort MessageId;
        public readonly NetworkChannel Channel;
        public readonly uint Sequence;
        public readonly int PayloadLength;
        public readonly uint Checksum;

        public NetworkEnvelopeHeader(
            ushort messageId,
            NetworkChannel channel,
            int payloadLength,
            uint sequence,
            uint checksum,
            NetworkMessageFlags flags = NetworkMessageFlags.None,
            byte version = NetworkWireProtocol.CurrentVersion,
            byte headerLength = NetworkWireProtocol.HeaderLength)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            if (headerLength < NetworkWireProtocol.HeaderLength)
                throw new ArgumentOutOfRangeException(nameof(headerLength));

            Version = version;
            HeaderLength = headerLength;
            Flags = flags;
            MessageId = messageId;
            Channel = channel;
            Sequence = sequence;
            PayloadLength = payloadLength;
            Checksum = checksum;
        }

        public bool IsSupported =>
            Version >= NetworkWireProtocol.MinSupportedVersion
            && Version <= NetworkWireProtocol.CurrentVersion
            && HeaderLength == NetworkWireProtocol.HeaderLength
            && PayloadLength >= 0
            && (Flags & ~NetworkWireProtocol.KnownFlags) == 0
            && Channel >= NetworkChannel.Reliable
            && Channel <= NetworkChannel.UnreliableSequenced;

        public NetworkMessageEnvelope ToEnvelope(NetworkMessageDirection direction)
        {
            return new NetworkMessageEnvelope(
                MessageId,
                direction,
                Channel,
                PayloadLength,
                Sequence,
                Checksum,
                Flags,
                Version);
        }
    }

    public enum NetworkFrameResult : byte
    {
        Invalid = 0,
        Valid = 1,
        BufferTooSmall = 2,
        InvalidBufferRange = 3,
        InvalidMagic = 4,
        UnsupportedVersion = 5,
        InvalidHeaderLength = 6,
        InvalidFlags = 7,
        InvalidChannel = 8,
        InvalidReservedByte = 9,
        InvalidPayloadLength = 10,
        InvalidChecksum = 11
    }

    /// <summary>
    /// Encodes and validates Cyclone wire frames without allocating.
    /// A frame is the fixed Cyclone header plus the serialized message payload.
    /// </summary>
    public static class NetworkFrameCodec
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFrameLength(int payloadLength)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));

            return checked(NetworkWireProtocol.HeaderLength + payloadLength);
        }

        public static NetworkFrameResult TryReadHeader(ArraySegment<byte> frame, out NetworkEnvelopeHeader header)
        {
            if (frame.Array == null || frame.Offset < 0 || frame.Count < 0 || frame.Offset > frame.Array.Length || frame.Count > frame.Array.Length - frame.Offset)
            {
                header = default;
                return NetworkFrameResult.InvalidBufferRange;
            }

            return TryReadHeader(new ReadOnlySpan<byte>(frame.Array, frame.Offset, frame.Count), out header);
        }

        public static NetworkFrameResult TryReadHeader(ReadOnlySpan<byte> frame, out NetworkEnvelopeHeader header)
        {
            header = default;
            if (frame.Length < NetworkWireProtocol.HeaderLength)
                return NetworkFrameResult.BufferTooSmall;

            ushort magic = ReadUShort(frame, NetworkWireProtocol.MagicOffset);
            if (magic != NetworkWireProtocol.Magic)
                return NetworkFrameResult.InvalidMagic;

            byte version = frame[NetworkWireProtocol.VersionOffset];
            if (version < NetworkWireProtocol.MinSupportedVersion || version > NetworkWireProtocol.CurrentVersion)
                return NetworkFrameResult.UnsupportedVersion;

            byte headerLength = frame[NetworkWireProtocol.HeaderLengthOffset];
            if (headerLength != NetworkWireProtocol.HeaderLength)
                return NetworkFrameResult.InvalidHeaderLength;

            var flags = (NetworkMessageFlags)ReadUShort(frame, NetworkWireProtocol.FlagsOffset);
            if ((flags & ~NetworkWireProtocol.KnownFlags) != 0)
                return NetworkFrameResult.InvalidFlags;

            ushort messageId = ReadUShort(frame, NetworkWireProtocol.MessageIdOffset);
            var channel = (NetworkChannel)frame[NetworkWireProtocol.ChannelOffset];
            if (channel < NetworkChannel.Reliable || channel > NetworkChannel.UnreliableSequenced)
                return NetworkFrameResult.InvalidChannel;

            if (frame[NetworkWireProtocol.ReservedOffset] != 0)
                return NetworkFrameResult.InvalidReservedByte;

            uint sequence = ReadUInt(frame, NetworkWireProtocol.SequenceOffset);
            int payloadLength = ReadInt(frame, NetworkWireProtocol.PayloadLengthOffset);
            uint checksum = ReadUInt(frame, NetworkWireProtocol.ChecksumOffset);

            if (payloadLength < 0 || payloadLength != frame.Length - headerLength)
                return NetworkFrameResult.InvalidPayloadLength;

            header = new NetworkEnvelopeHeader(
                messageId,
                channel,
                payloadLength,
                sequence,
                checksum,
                flags,
                version,
                headerLength);
            return NetworkFrameResult.Valid;
        }

        public static NetworkFrameResult TryReadPayload(ArraySegment<byte> frame, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload)
        {
            NetworkFrameResult result = TryReadHeader(frame, out header);
            if (result != NetworkFrameResult.Valid)
            {
                payload = default;
                return result;
            }

            payload = new ArraySegment<byte>(
                frame.Array,
                frame.Offset + header.HeaderLength,
                header.PayloadLength);
            return NetworkFrameResult.Valid;
        }

        public static NetworkFrameResult ValidateChecksum(in NetworkEnvelopeHeader header, ReadOnlySpan<byte> payload)
        {
            return ComputeChecksum(header.MessageId, header.Channel, header.Flags, header.Sequence, payload) == header.Checksum
                ? NetworkFrameResult.Valid
                : NetworkFrameResult.InvalidChecksum;
        }

        public static void WriteHeader(byte[] buffer, int offset, in NetworkEnvelopeHeader header)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length || NetworkWireProtocol.HeaderLength > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (!header.IsSupported)
                throw new ArgumentException("Network frame header contains unsupported values.", nameof(header));

            WriteUShort(buffer, offset + NetworkWireProtocol.MagicOffset, NetworkWireProtocol.Magic);
            buffer[offset + NetworkWireProtocol.VersionOffset] = header.Version;
            buffer[offset + NetworkWireProtocol.HeaderLengthOffset] = header.HeaderLength;
            WriteUShort(buffer, offset + NetworkWireProtocol.FlagsOffset, (ushort)header.Flags);
            WriteUShort(buffer, offset + NetworkWireProtocol.MessageIdOffset, header.MessageId);
            buffer[offset + NetworkWireProtocol.ChannelOffset] = (byte)header.Channel;
            buffer[offset + NetworkWireProtocol.ReservedOffset] = 0;
            WriteUInt(buffer, offset + NetworkWireProtocol.SequenceOffset, header.Sequence);
            WriteInt(buffer, offset + NetworkWireProtocol.PayloadLengthOffset, header.PayloadLength);
            WriteUInt(buffer, offset + NetworkWireProtocol.ChecksumOffset, header.Checksum);
        }

        /// <summary>
        /// Computes a fast non-cryptographic FNV-1a checksum over routing metadata and payload bytes.
        /// </summary>
        public static uint ComputeChecksum(ushort messageId, NetworkChannel channel, NetworkMessageFlags flags, uint sequence, ReadOnlySpan<byte> payload)
        {
            // FNV-1a catches accidental corruption and mismatched frame parsing; it is not a tamper-proof MAC.
            uint hash = NetworkWireProtocol.ChecksumSeed;
            hash = Add(hash, (byte)messageId);
            hash = Add(hash, (byte)(messageId >> 8));
            hash = Add(hash, (byte)channel);
            hash = Add(hash, (byte)flags);
            hash = Add(hash, (byte)((ushort)flags >> 8));
            hash = Add(hash, (byte)sequence);
            hash = Add(hash, (byte)(sequence >> 8));
            hash = Add(hash, (byte)(sequence >> 16));
            hash = Add(hash, (byte)(sequence >> 24));
            for (int i = 0; i < payload.Length; i++)
                hash = Add(hash, payload[i]);
            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Add(uint hash, byte value)
        {
            return (hash ^ value) * NetworkWireProtocol.ChecksumPrime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ReadUShort(ReadOnlySpan<byte> data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadInt(ReadOnlySpan<byte> data, int offset)
        {
            return data[offset]
                   | (data[offset + 1] << 8)
                   | (data[offset + 2] << 16)
                   | (data[offset + 3] << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadUInt(ReadOnlySpan<byte> data, int offset)
        {
            return (uint)ReadInt(data, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteUShort(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteUInt(byte[] buffer, int offset, uint value)
        {
            WriteInt(buffer, offset, unchecked((int)value));
        }
    }

    public readonly struct NetworkMessageEnvelope
    {
        public readonly byte Version;
        public readonly ushort MessageId;
        public readonly NetworkMessageDirection Direction;
        public readonly NetworkChannel Channel;
        public readonly NetworkMessageFlags Flags;
        public readonly int PayloadLength;
        public readonly uint Sequence;
        public readonly uint Checksum;

        public NetworkMessageEnvelope(
            ushort messageId,
            NetworkMessageDirection direction,
            NetworkChannel channel,
            int payloadLength,
            uint sequence = 0u,
            uint checksum = 0u,
            NetworkMessageFlags flags = NetworkMessageFlags.None,
            byte version = NetworkWireProtocol.CurrentVersion)
        {
            Version = version;
            MessageId = messageId;
            Direction = direction;
            Channel = channel;
            Flags = flags;
            PayloadLength = payloadLength;
            Sequence = sequence;
            Checksum = checksum;
        }

        public bool IsValid => Version != 0 && Direction != NetworkMessageDirection.Unknown && PayloadLength >= 0;
    }

    public readonly struct MessageSecurityPolicy
    {
        public static MessageSecurityPolicy Default => new MessageSecurityPolicy(
            NetworkMessageDirectionMask.Any,
            NetworkConstants.DefaultMaxPayloadSize,
            requireAuthenticatedConnection: false,
            requireEncryptedTransport: false,
            enableReplayProtection: false,
            requireSignature: false);

        public readonly NetworkMessageDirectionMask AllowedDirections;
        public readonly int MaxPayloadSize;
        public readonly bool RequireAuthenticatedConnection;
        public readonly bool RequireEncryptedTransport;
        public readonly bool EnableReplayProtection;
        public readonly bool RequireSignature;

        public MessageSecurityPolicy(
            NetworkMessageDirectionMask allowedDirections,
            int maxPayloadSize,
            bool requireAuthenticatedConnection,
            bool requireEncryptedTransport,
            bool enableReplayProtection,
            bool requireSignature = false)
        {
            if (maxPayloadSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadSize));

            AllowedDirections = allowedDirections;
            MaxPayloadSize = maxPayloadSize;
            RequireAuthenticatedConnection = requireAuthenticatedConnection;
            RequireEncryptedTransport = requireEncryptedTransport;
            EnableReplayProtection = enableReplayProtection;
            RequireSignature = requireSignature;
        }

        public MessageSecurityPolicy WithMaxPayloadSize(int maxPayloadSize)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                maxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                EnableReplayProtection,
                RequireSignature);
        }

        public MessageSecurityPolicy WithAuthenticatedConnectionRequired(bool required)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                required,
                RequireEncryptedTransport,
                EnableReplayProtection,
                RequireSignature);
        }

        public MessageSecurityPolicy WithEncryptedTransportRequired(bool required)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                required,
                EnableReplayProtection,
                RequireSignature);
        }

        public MessageSecurityPolicy WithReplayProtection(bool enabled)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                enabled,
                RequireSignature);
        }

        public MessageSecurityPolicy WithSignatureRequired(bool required)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                EnableReplayProtection,
                required);
        }

        public MessageSecurityPolicy WithAllowedDirections(NetworkMessageDirectionMask allowedDirections)
        {
            return new MessageSecurityPolicy(
                allowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                EnableReplayProtection,
                RequireSignature);
        }
    }

    public enum MessageSecurityResult : byte
    {
        Invalid = 0,
        Valid = 1,
        MalformedEnvelope = 2,
        UnsupportedVersion = 3,
        DirectionRejected = 4,
        PayloadTooLarge = 5,
        AuthenticationRequired = 6,
        EncryptionRequired = 7,
        ReplayRejected = 8,
        SignatureRequired = 9,
        SignatureRejected = 10,
        RateLimited = 11
    }

    public interface INetworkSecurityPolicyConfigurable
    {
        MessageSecurityPolicy DefaultMessageSecurityPolicy { get; }
        void SetDefaultMessageSecurityPolicy(MessageSecurityPolicy policy);
        void SetMessageSecurityPolicy(ushort messageId, MessageSecurityPolicy policy);
        void ClearMessageSecurityPolicy(ushort messageId);
    }

    public sealed class MessageSecurityPolicyRegistry
    {
        private readonly object _syncRoot = new object();
        private volatile RegistrySnapshot _snapshot;
        private volatile bool _frozen;

        public MessageSecurityPolicyRegistry()
            : this(MessageSecurityPolicy.Default)
        {
        }

        public MessageSecurityPolicyRegistry(MessageSecurityPolicy defaultPolicy)
        {
            _snapshot = new RegistrySnapshot(defaultPolicy, new Dictionary<ushort, MessageSecurityPolicy>());
        }

        public MessageSecurityPolicy DefaultPolicy => _snapshot.DefaultPolicy;
        public bool IsFrozen => _frozen;

        public MessageSecurityPolicy GetPolicy(ushort messageId)
        {
            RegistrySnapshot snapshot = _snapshot;
            return snapshot.Policies.TryGetValue(messageId, out MessageSecurityPolicy policy) ? policy : snapshot.DefaultPolicy;
        }

        public void SetDefaultPolicy(MessageSecurityPolicy policy)
        {
            lock (_syncRoot)
            {
                ThrowIfFrozen();
                RegistrySnapshot snapshot = _snapshot;
                _snapshot = new RegistrySnapshot(policy, snapshot.Policies);
            }
        }

        public void SetPolicy(ushort messageId, MessageSecurityPolicy policy)
        {
            lock (_syncRoot)
            {
                ThrowIfFrozen();
                RegistrySnapshot snapshot = _snapshot;
                var copy = new Dictionary<ushort, MessageSecurityPolicy>(snapshot.Policies)
                {
                    [messageId] = policy
                };
                _snapshot = new RegistrySnapshot(snapshot.DefaultPolicy, copy);
            }
        }

        public void ClearPolicy(ushort messageId)
        {
            lock (_syncRoot)
            {
                ThrowIfFrozen();
                RegistrySnapshot snapshot = _snapshot;
                if (!snapshot.Policies.ContainsKey(messageId))
                    return;

                var copy = new Dictionary<ushort, MessageSecurityPolicy>(snapshot.Policies);
                copy.Remove(messageId);
                _snapshot = new RegistrySnapshot(snapshot.DefaultPolicy, copy);
            }
        }

        public void Freeze()
        {
            lock (_syncRoot)
                _frozen = true;
        }

        private void ThrowIfFrozen()
        {
            if (_frozen)
                throw new InvalidOperationException("Message security policies are frozen.");
        }

        private sealed class RegistrySnapshot
        {
            public readonly MessageSecurityPolicy DefaultPolicy;
            public readonly Dictionary<ushort, MessageSecurityPolicy> Policies;

            public RegistrySnapshot(MessageSecurityPolicy defaultPolicy, Dictionary<ushort, MessageSecurityPolicy> policies)
            {
                DefaultPolicy = defaultPolicy;
                Policies = policies;
            }
        }
    }

    public sealed class NetworkReplayGuard
    {
        private const int DefaultMaxConnections = 4096;
        private const int DefaultMaxStreamsPerConnection = 256;
        private const double DefaultIdleTimeoutSeconds = 120d;

        private readonly ConcurrentDictionary<int, ConnectionReplayState> _connections = new ConcurrentDictionary<int, ConnectionReplayState>();
        private readonly object _creationLock = new object();
        private readonly int _maxConnections;
        private readonly int _maxStreamsPerConnection;
        private readonly double _idleTimeoutSeconds;

        public NetworkReplayGuard(
            int maxConnections = DefaultMaxConnections,
            int maxStreamsPerConnection = DefaultMaxStreamsPerConnection,
            double idleTimeoutSeconds = DefaultIdleTimeoutSeconds)
        {
            if (maxConnections <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConnections));
            if (maxStreamsPerConnection <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStreamsPerConnection));
            if (idleTimeoutSeconds <= 0d || double.IsNaN(idleTimeoutSeconds) || double.IsInfinity(idleTimeoutSeconds))
                throw new ArgumentOutOfRangeException(nameof(idleTimeoutSeconds));

            _maxConnections = maxConnections;
            _maxStreamsPerConnection = maxStreamsPerConnection;
            _idleTimeoutSeconds = idleTimeoutSeconds;
        }

        public bool TryAccept(int connectionId, ushort messageId, uint sequence, double currentTime)
        {
            if (connectionId <= 0 || sequence == 0u || !IsFiniteNonNegative(currentTime))
                return false;

            if (!_connections.TryGetValue(connectionId, out ConnectionReplayState state))
            {
                lock (_creationLock)
                {
                    if (!_connections.TryGetValue(connectionId, out state))
                    {
                        if (_connections.Count >= _maxConnections)
                            PruneExpired(currentTime, Math.Max(1, _maxConnections / 16));
                        if (_connections.Count >= _maxConnections)
                            return false;

                        state = new ConnectionReplayState(_maxStreamsPerConnection, currentTime);
                        if (!_connections.TryAdd(connectionId, state)
                            && !_connections.TryGetValue(connectionId, out state))
                        {
                            return false;
                        }
                    }
                }
            }

            return state.TryAccept(messageId, sequence, currentTime, _idleTimeoutSeconds);
        }

        public int PruneExpired(double currentTime, int maxRemovals = int.MaxValue)
        {
            if (!IsFiniteNonNegative(currentTime))
                throw new ArgumentOutOfRangeException(nameof(currentTime));
            if (maxRemovals < 0)
                throw new ArgumentOutOfRangeException(nameof(maxRemovals));

            int removed = 0;
            foreach (var pair in _connections)
            {
                if (removed >= maxRemovals)
                    break;

                if (pair.Value.TryRetireIfExpired(currentTime, _idleTimeoutSeconds)
                    && ((ICollection<KeyValuePair<int, ConnectionReplayState>>)_connections).Remove(pair))
                {
                    removed++;
                }
            }

            return removed;
        }

        public void RemoveConnection(int connectionId)
        {
            if (connectionId <= 0
                || !_connections.TryGetValue(connectionId, out ConnectionReplayState state))
            {
                return;
            }

            state.Retire();
            ((ICollection<KeyValuePair<int, ConnectionReplayState>>)_connections).Remove(
                new KeyValuePair<int, ConnectionReplayState>(connectionId, state));
        }

        public void Clear()
        {
            lock (_creationLock)
            {
                foreach (var pair in _connections)
                {
                    pair.Value.Retire();
                    ((ICollection<KeyValuePair<int, ConnectionReplayState>>)_connections).Remove(pair);
                }
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class ConnectionReplayState
        {
            private const int WindowBitCount = 64;

            private readonly object _syncRoot = new object();
            private readonly int _maxStreams;
            private readonly Dictionary<ushort, SequenceWindow> _windows;
            private double _lastSeen;
            private bool _retired;

            public ConnectionReplayState(int maxStreams, double currentTime)
            {
                _maxStreams = maxStreams;
                _windows = new Dictionary<ushort, SequenceWindow>(Math.Min(maxStreams, 32));
                _lastSeen = currentTime;
            }

            public bool TryAccept(ushort messageId, uint sequence, double currentTime, double idleTimeoutSeconds)
            {
                lock (_syncRoot)
                {
                    if (_retired || currentTime < _lastSeen)
                        return false;

                    _lastSeen = currentTime;
                    if (!_windows.TryGetValue(messageId, out SequenceWindow window))
                    {
                        if (_windows.Count >= _maxStreams)
                            PruneOneExpiredStream(currentTime, idleTimeoutSeconds);
                        if (_windows.Count >= _maxStreams)
                            return false;

                        _windows.Add(messageId, new SequenceWindow(sequence, currentTime));
                        return true;
                    }

                    int forward = unchecked((int)(sequence - window.Highest));
                    if (forward > 0)
                    {
                        window.SeenMask = forward >= WindowBitCount
                            ? 1UL
                            : (window.SeenMask << forward) | 1UL;
                        window.Highest = sequence;
                        window.LastSeen = currentTime;
                        _windows[messageId] = window;
                        return true;
                    }

                    int age = unchecked((int)(window.Highest - sequence));
                    if (age < 0 || age >= WindowBitCount)
                        return false;

                    ulong bit = 1UL << age;
                    if ((window.SeenMask & bit) != 0UL)
                        return false;

                    window.SeenMask |= bit;
                    window.LastSeen = currentTime;
                    _windows[messageId] = window;
                    return true;
                }
            }

            public bool TryRetireIfExpired(double currentTime, double idleTimeoutSeconds)
            {
                lock (_syncRoot)
                {
                    if (_retired || currentTime - _lastSeen < idleTimeoutSeconds)
                        return false;

                    _retired = true;
                    return true;
                }
            }

            public void Retire()
            {
                lock (_syncRoot)
                    _retired = true;
            }

            private void PruneOneExpiredStream(double currentTime, double idleTimeoutSeconds)
            {
                ushort expiredMessageId = 0;
                bool found = false;
                foreach (var pair in _windows)
                {
                    if (currentTime - pair.Value.LastSeen < idleTimeoutSeconds)
                        continue;

                    expiredMessageId = pair.Key;
                    found = true;
                    break;
                }

                if (found)
                    _windows.Remove(expiredMessageId);
            }

            private struct SequenceWindow
            {
                public uint Highest;
                public ulong SeenMask;
                public double LastSeen;

                public SequenceWindow(uint highest, double lastSeen)
                {
                    Highest = highest;
                    SeenMask = 1UL;
                    LastSeen = lastSeen;
                }
            }
        }
    }
}
