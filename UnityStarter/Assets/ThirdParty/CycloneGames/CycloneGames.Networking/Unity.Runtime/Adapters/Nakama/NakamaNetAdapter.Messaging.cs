#if CYCLONE_NETWORKING_HAS_NAKAMA
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Security;
using Nakama;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Nakama
{
    public sealed partial class NakamaNetAdapter
    {
        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (channelId != GetChannelId(NetworkChannel.Reliable))
                return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, channelId, connection, UnsupportedChannelError);
            if (!ValidatePayload(payload))
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection);

            if (!IsInMatch)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, MissingMatchError);
            if (connection is not NakamaNetConnection nakamaConnection
                || !IsCurrentConnection(nakamaConnection))
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            IEnumerable<IUserPresence> targets;
            if (nakamaConnection.RouteRole == NakamaConnectionRouteRole.Authority)
            {
                if (_match == null
                    || !_match.Authoritative
                    || !ReferenceEquals(nakamaConnection, _authorityConnection))
                {
                    return NetworkSendResult.Fail(
                        NetworkSendStatus.Unsupported,
                        channelId,
                        connection,
                        NonAuthoritativeMatchError);
                }

                targets = null;
            }
            else
            {
                if (nakamaConnection.Presence == null)
                    return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
                targets = nakamaConnection.TargetPresences;
            }

            byte[] buffer = GetSendBuffer();
            int frameLength = BuildFrame(NetworkConstants.SystemMsgIdMin, NetworkChannel.Reliable, payload, buffer);
            NetworkSendStatus status = QueueFrameToMatch(
                new ArraySegment<byte>(buffer, 0, frameLength),
                targets);
            return status == NetworkSendStatus.Queued
                ? NetworkSendResult.Queued(frameLength, channelId, connection)
                : NetworkSendResult.Fail(status, channelId, connection, GetSendFailureReason(status));
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                reason: UnsupportedClientRouteError);
        }

        public NetworkMessageHandlerLease RegisterHandler(ushort messageId, NetworkMessageHandler handler)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            return _messageHandlers.Register(messageId, handler);
        }

        public NetworkSendResult SendToServer(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (!TryPrepareCanonicalPayload(messageId, channel, payload.Length, null, out _, out NetworkSendResult failure))
                return failure;

            if (_match == null || !_match.Authoritative)
            {
                return NetworkSendResult.Fail(
                    NetworkSendStatus.Unsupported,
                    GetChannelId(channel),
                    reason: NonAuthoritativeMatchError);
            }

            return SendCanonicalMessage(messageId, payload, channel, null);
        }

        public NetworkSendResult SendToClient(
            INetConnection connection,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            int channelId = channel == NetworkChannel.Reliable ? GetChannelId(channel) : (int)channel;
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                connection,
                UnsupportedClientRouteError);
        }

        public NetworkSendResult BroadcastToClients(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            int channelId = channel == NetworkChannel.Reliable ? GetChannelId(channel) : (int)channel;
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                reason: UnsupportedClientRouteError);
        }

        public NetworkSendResult Broadcast(
            IReadOnlyList<INetConnection> connections,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            int channelId = channel == NetworkChannel.Reliable ? GetChannelId(channel) : (int)channel;
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                reason: UnsupportedClientRouteError);
        }

        private bool TryPrepareCanonicalPayload(
            ushort messageId,
            NetworkChannel channel,
            int payloadLength,
            INetConnection connection,
            out int channelId,
            out NetworkSendResult failure)
        {
            if (channel != NetworkChannel.Reliable)
            {
                channelId = (int)channel;
                failure = NetworkSendResult.Fail(NetworkSendStatus.ChannelUnavailable, channelId, connection, UnsupportedChannelError);
                return false;
            }

            channelId = GetChannelId(channel);
            if (!IsAcceptingMessages)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, MissingMatchError);
                return false;
            }

            int maximumPayloadSize = GetMaxPayloadSize(messageId, channel);
            if (maximumPayloadSize <= 0)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.ChannelUnavailable, channelId, connection);
                return false;
            }

            if (payloadLength > maximumPayloadSize)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.PayloadTooLarge, channelId, connection);
                return false;
            }

            failure = default;
            return true;
        }

        private NetworkSendResult SendCanonicalMessage(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel,
            IEnumerable<IUserPresence> targetPresences)
        {
            if (channel != NetworkChannel.Reliable)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, (int)channel, reason: UnsupportedChannelError);
            }

            int channelId = GetChannelId(channel);
            if (!IsInMatch)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, reason: MissingMatchError);
            }

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                NetworkSendStatus status = QueueFrameToMatch(
                    new ArraySegment<byte>(buffer, 0, frameLength),
                    targetPresences);
                return status == NetworkSendStatus.Queued
                    ? NetworkSendResult.Queued(frameLength, channelId)
                    : NetworkSendResult.Fail(status, channelId, reason: GetSendFailureReason(status));
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                RaiseError(null, TransportError.InvalidSend, e.Message);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, in ArraySegment<byte> payload, byte[] buffer)
        {
            if (payload.Array == null)
                throw new ArgumentException("Payload must reference a valid array.", nameof(payload));

            if (payload.Offset < 0
                || payload.Count < 0
                || payload.Offset > payload.Array.Length
                || payload.Count > payload.Array.Length - payload.Offset)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }

            return BuildFrame(
                msgId,
                channel,
                new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count),
                buffer);
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, ReadOnlySpan<byte> payload, byte[] buffer)
        {
            int maximumPayloadSize = GetMaxPayloadSize(msgId, channel);
            if (maximumPayloadSize <= 0 || payload.Length > maximumPayloadSize)
                throw new InvalidOperationException("Payload exceeds the available Nakama message route.");

            return BuildFrameUnchecked(msgId, channel, payload, buffer);
        }

        private int BuildFrameUnchecked(ushort msgId, NetworkChannel channel, ReadOnlySpan<byte> payload, byte[] buffer)
        {
            int frameLength = NetworkFrameCodec.GetFrameLength(payload.Length);
            if (frameLength > buffer.Length)
                throw new InvalidOperationException("Frame size exceeds send buffer capacity.");

            payload.CopyTo(new Span<byte>(buffer, NetworkWireProtocol.HeaderLength, payload.Length));
            WriteFrameHeader(msgId, channel, buffer, payload.Length);
            return frameLength;
        }

        private int WriteFrameHeader(ushort msgId, NetworkChannel channel, byte[] buffer, int payloadLength)
        {
            uint sequence = unchecked((uint)Interlocked.Increment(ref _sendSequence));
            if (sequence == 0u)
                sequence = unchecked((uint)Interlocked.Increment(ref _sendSequence));

            var payload = new ReadOnlySpan<byte>(buffer, NetworkWireProtocol.HeaderLength, payloadLength);
            NetworkMessageFlags flags = GetMessageFlags(channel);
            uint checksum = NetworkFrameCodec.ComputeChecksum(msgId, channel, flags, sequence, payload);
            var header = new NetworkEnvelopeHeader(msgId, channel, payloadLength, sequence, checksum, flags);
            NetworkFrameCodec.WriteHeader(buffer, 0, header);
            return NetworkFrameCodec.GetFrameLength(payloadLength);
        }

        private NetworkSendStatus QueueFrameToMatch(
            ArraySegment<byte> frame,
            IEnumerable<IUserPresence> presences)
        {
            string matchId = _match != null ? _match.Id : _matchId;
            if (string.IsNullOrEmpty(matchId))
                return NetworkSendStatus.NotConnected;

            return QueueMatchState(matchId, _matchStateOpCode, frame, presences);
        }

        private NetworkSendStatus QueueMatchState(
            string matchId,
            long opCode,
            ArraySegment<byte> payload,
            IEnumerable<IUserPresence> presences)
        {
            ISocket socket = _socket;
            if (socket == null
                || !socket.IsConnected
                || !_socketEventsBound
                || !ReferenceEquals(socket, _socketEventSource))
            {
                return NetworkSendStatus.NotConnected;
            }

            if (Interlocked.Increment(ref _pendingSends) > _maxPendingSends)
            {
                Interlocked.Decrement(ref _pendingSends);
                return NetworkSendStatus.Backpressure;
            }

            CancellationToken cancellationToken = _shutdown != null
                ? _shutdown.Token
                : CancellationToken.None;
            SendMatchStateAsync(
                socket,
                matchId,
                opCode,
                payload,
                presences,
                cancellationToken,
                _operationGeneration).Forget();
            return NetworkSendStatus.Queued;
        }

        private async UniTask SendMatchStateAsync(
            ISocket socket,
            string matchId,
            long opCode,
            ArraySegment<byte> payload,
            IEnumerable<IUserPresence> presences,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            byte[] rented = null;
            try
            {
                EnsureMainThread();
                rented = ArrayPool<byte>.Shared.Rent(payload.Count);
                Buffer.BlockCopy(payload.Array, payload.Offset, rented, 0, payload.Count);
                var segment = new ArraySegment<byte>(rented, 0, payload.Count);
                await socket.SendMatchStateAsync(matchId, opCode, segment, presences);
                EnsureMainThread();
                Interlocked.Add(ref _bytesSent, payload.Count);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                RaiseError(null, TransportError.InvalidSend, e.Message);
            }
            finally
            {
                if (rented != null)
                    ArrayPool<byte>.Shared.Return(rented, clearArray: true);
                Interlocked.Decrement(ref _pendingSends);
            }
        }

        private static string GetSendFailureReason(NetworkSendStatus status)
        {
            switch (status)
            {
                case NetworkSendStatus.Backpressure:
                    return SendCapacityError;
                case NetworkSendStatus.Unsupported:
                    return UnsupportedChannelError;
                case NetworkSendStatus.NotConnected:
                    return MissingSocketError;
                default:
                    return string.Empty;
            }
        }

        private bool TryValidateIncoming(INetConnection connection, ArraySegment<byte> frame, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload)
        {
            header = default;
            payload = default;

            NetworkFrameResult frameResult = NetworkFrameCodec.TryReadPayload(frame, out header, out payload);
            if (frameResult != NetworkFrameResult.Valid)
                return false;
            if (header.Channel != NetworkChannel.Reliable)
                return false;

            int routePayloadLimit = GetMaxPayloadSize(header.MessageId, header.Channel);
            if (routePayloadLimit <= 0 || header.PayloadLength > routePayloadLimit)
                return false;

            if (_messageValidator.Validate(header.MessageId, header.PayloadLength) != ValidationResult.Valid)
                return false;

            var payloadSpan = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
            if (NetworkFrameCodec.ValidateChecksum(header, payloadSpan) != NetworkFrameResult.Valid)
                return false;

            return true;
        }

        private bool ValidatePayload(in ArraySegment<byte> payload)
        {
            if (payload.Array == null || payload.Offset < 0 || payload.Count < 0 || payload.Offset > payload.Array.Length || payload.Count > payload.Array.Length - payload.Offset)
            {
                RaiseError(null, TransportError.InvalidSend, "Invalid payload segment.");
                return false;
            }

            if (payload.Count > _maxPayloadSize)
            {
                RaiseError(null, TransportError.InvalidSend, "Payload exceeds Nakama adapter packet size limit.");
                return false;
            }

            return true;
        }

        private static NetworkMessageFlags GetMessageFlags(NetworkChannel channel)
        {
            switch (channel)
            {
                case NetworkChannel.Reliable:
                    return NetworkMessageFlags.Reliable | NetworkMessageFlags.Ordered;
                case NetworkChannel.ReliableUnordered:
                    return NetworkMessageFlags.Reliable;
                case NetworkChannel.UnreliableSequenced:
                    return NetworkMessageFlags.Ordered;
                default:
                    return NetworkMessageFlags.None;
            }
        }

        private byte[] GetSendBuffer() => _sendBuffer;
    }
}
#endif
