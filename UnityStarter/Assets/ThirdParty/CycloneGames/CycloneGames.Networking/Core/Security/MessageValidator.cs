using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
        Valid,
        InvalidMessageId,
        PayloadTooSmall,
        PayloadTooLarge,
        MalformedHeader
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
        public const uint ChecksumSeed = 2166136261u; // FNV-1a 32-bit offset basis.
        public const uint ChecksumPrime = 16777619u; // FNV-1a 32-bit prime.
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
            && HeaderLength >= NetworkWireProtocol.HeaderLength
            && PayloadLength >= 0;

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
        Valid,
        BufferTooSmall,
        InvalidBufferRange,
        InvalidMagic,
        UnsupportedVersion,
        InvalidHeaderLength,
        InvalidPayloadLength,
        InvalidChecksum
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
            return NetworkWireProtocol.HeaderLength + payloadLength;
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
            if (headerLength < NetworkWireProtocol.HeaderLength || headerLength > frame.Length)
                return NetworkFrameResult.InvalidHeaderLength;

            var flags = (NetworkMessageFlags)ReadUShort(frame, NetworkWireProtocol.FlagsOffset);
            ushort messageId = ReadUShort(frame, NetworkWireProtocol.MessageIdOffset);
            var channel = (NetworkChannel)frame[NetworkWireProtocol.ChannelOffset];
            uint sequence = ReadUInt(frame, NetworkWireProtocol.SequenceOffset);
            int payloadLength = ReadInt(frame, NetworkWireProtocol.PayloadLengthOffset);
            uint checksum = ReadUInt(frame, NetworkWireProtocol.ChecksumOffset);

            if (payloadLength < 0 || payloadLength > frame.Length - headerLength)
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
        public const byte CurrentVersion = NetworkWireProtocol.CurrentVersion;

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
            byte version = CurrentVersion)
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
            enableReplayProtection: false);

        public readonly NetworkMessageDirectionMask AllowedDirections;
        public readonly int MaxPayloadSize;
        public readonly bool RequireAuthenticatedConnection;
        public readonly bool RequireEncryptedTransport;
        public readonly bool EnableReplayProtection;

        public MessageSecurityPolicy(
            NetworkMessageDirectionMask allowedDirections,
            int maxPayloadSize,
            bool requireAuthenticatedConnection,
            bool requireEncryptedTransport,
            bool enableReplayProtection)
        {
            if (maxPayloadSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadSize));

            AllowedDirections = allowedDirections;
            MaxPayloadSize = maxPayloadSize;
            RequireAuthenticatedConnection = requireAuthenticatedConnection;
            RequireEncryptedTransport = requireEncryptedTransport;
            EnableReplayProtection = enableReplayProtection;
        }

        public MessageSecurityPolicy WithMaxPayloadSize(int maxPayloadSize)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                maxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                EnableReplayProtection);
        }

        public MessageSecurityPolicy WithAuthenticatedConnectionRequired(bool required)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                required,
                RequireEncryptedTransport,
                EnableReplayProtection);
        }

        public MessageSecurityPolicy WithEncryptedTransportRequired(bool required)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                required,
                EnableReplayProtection);
        }

        public MessageSecurityPolicy WithReplayProtection(bool enabled)
        {
            return new MessageSecurityPolicy(
                AllowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                enabled);
        }

        public MessageSecurityPolicy WithAllowedDirections(NetworkMessageDirectionMask allowedDirections)
        {
            return new MessageSecurityPolicy(
                allowedDirections,
                MaxPayloadSize,
                RequireAuthenticatedConnection,
                RequireEncryptedTransport,
                EnableReplayProtection);
        }
    }

    public enum MessageSecurityResult : byte
    {
        Valid,
        MalformedEnvelope,
        UnsupportedVersion,
        DirectionRejected,
        PayloadTooLarge,
        AuthenticationRequired,
        EncryptionRequired,
        ReplayRejected
    }

    public interface INetworkMessageSecurityConfigurable
    {
        MessageSecurityPolicy DefaultMessageSecurityPolicy { get; }
        void SetDefaultMessageSecurityPolicy(MessageSecurityPolicy policy);
        void SetMessageSecurityPolicy(ushort messageId, MessageSecurityPolicy policy);
        void ClearMessageSecurityPolicy(ushort messageId);
    }

    public sealed class MessageSecurityPolicyRegistry
    {
        private readonly object _syncRoot = new object();
        private RegistrySnapshot _snapshot;
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

        public MessageSecurityResult Validate(
            in NetworkMessageEnvelope envelope,
            INetConnection connection,
            bool transportEncrypted,
            NetworkReplayGuard replayGuard)
        {
            if (!envelope.IsValid)
                return MessageSecurityResult.MalformedEnvelope;

            if (envelope.Version > NetworkMessageEnvelope.CurrentVersion)
                return MessageSecurityResult.UnsupportedVersion;

            MessageSecurityPolicy policy = GetPolicy(envelope.MessageId);
            if (!IsDirectionAllowed(policy.AllowedDirections, envelope.Direction))
                return MessageSecurityResult.DirectionRejected;

            if (envelope.PayloadLength > policy.MaxPayloadSize)
                return MessageSecurityResult.PayloadTooLarge;

            if (policy.RequireAuthenticatedConnection && (connection == null || !connection.IsAuthenticated))
                return MessageSecurityResult.AuthenticationRequired;

            if (policy.RequireEncryptedTransport && !transportEncrypted)
                return MessageSecurityResult.EncryptionRequired;

            if (policy.EnableReplayProtection)
            {
                if (replayGuard == null || connection == null || !replayGuard.TryAccept(connection.ConnectionId, envelope.MessageId, envelope.Sequence))
                    return MessageSecurityResult.ReplayRejected;
            }

            return MessageSecurityResult.Valid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDirectionAllowed(NetworkMessageDirectionMask mask, NetworkMessageDirection direction)
        {
            return direction switch
            {
                NetworkMessageDirection.ClientToServer => (mask & NetworkMessageDirectionMask.ClientToServer) != 0,
                NetworkMessageDirection.ServerToClient => (mask & NetworkMessageDirectionMask.ServerToClient) != 0,
                NetworkMessageDirection.ServerBroadcast => (mask & NetworkMessageDirectionMask.ServerBroadcast) != 0,
                NetworkMessageDirection.PeerToPeer => (mask & NetworkMessageDirectionMask.PeerToPeer) != 0,
                _ => false
            };
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
        private readonly ConcurrentDictionary<ReplayKey, uint> _lastSequences = new ConcurrentDictionary<ReplayKey, uint>();

        public bool TryAccept(int connectionId, ushort messageId, uint sequence)
        {
            if (connectionId <= 0 || sequence == 0u)
                return false;

            var key = new ReplayKey(connectionId, messageId);
            while (true)
            {
                if (!_lastSequences.TryGetValue(key, out uint current))
                    return _lastSequences.TryAdd(key, sequence);

                if (sequence <= current)
                    return false;

                if (_lastSequences.TryUpdate(key, sequence, current))
                    return true;
            }
        }

        public void RemoveConnection(int connectionId)
        {
            if (connectionId <= 0)
                return;

            foreach (ReplayKey key in _lastSequences.Keys)
            {
                if (key.ConnectionId == connectionId)
                    _lastSequences.TryRemove(key, out _);
            }
        }

        public void Clear()
        {
            _lastSequences.Clear();
        }

        private readonly struct ReplayKey : IEquatable<ReplayKey>
        {
            public readonly int ConnectionId;
            public readonly ushort MessageId;

            public ReplayKey(int connectionId, ushort messageId)
            {
                ConnectionId = connectionId;
                MessageId = messageId;
            }

            public bool Equals(ReplayKey other)
            {
                return ConnectionId == other.ConnectionId && MessageId == other.MessageId;
            }

            public override bool Equals(object obj)
            {
                return obj is ReplayKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ConnectionId * 397) ^ MessageId;
                }
            }
        }
    }
}
