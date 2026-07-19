using System;
using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class MessageValidatorTests
    {
        [Test]
        public void ValidateBuffer_Rejects_Overflowing_Range()
        {
            var validator = new MessageValidator(maxPayloadSize: 16, minPayloadSize: 1);
            byte[] buffer = new byte[8];

            Assert.IsFalse(validator.ValidateBuffer(buffer, int.MaxValue, 2));
            Assert.IsFalse(validator.ValidateBuffer(buffer, 7, 2));
        }

        [Test]
        public void Validate_Returns_Expected_Size_Results()
        {
            var validator = new MessageValidator(maxPayloadSize: 8, minPayloadSize: 2);

            Assert.AreEqual(ValidationResult.PayloadTooSmall, validator.Validate(1, 1));
            Assert.AreEqual(ValidationResult.PayloadTooLarge, validator.Validate(1, 9));
            Assert.AreEqual(ValidationResult.Valid, validator.Validate(1, 8));
        }

        [Test]
        public void NetworkSecurityPipeline_Allows_Default_Envelope()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                    NetworkMessageDirectionMask.Any,
                    16,
                    requireAuthenticatedConnection: false,
                    requireEncryptedTransport: false,
                    enableReplayProtection: false))
            });

            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);
            byte[] payload = new byte[8];

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                null,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 0d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.AreEqual(MessageSecurityResult.Valid, result.Result);
        }

        [Test]
        public void NetworkSecurityPipeline_Rejects_Disallowed_Direction()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                    NetworkMessageDirectionMask.ServerToClient,
                    16,
                    requireAuthenticatedConnection: false,
                    requireEncryptedTransport: false,
                    enableReplayProtection: false))
            });

            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);
            byte[] payload = new byte[8];

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                null,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 0d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.AreEqual(MessageSecurityResult.DirectionRejected, result.Result);
        }

        [Test]
        public void NetworkSecurityPipeline_Rejects_Unauthenticated_Connection()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                    NetworkMessageDirectionMask.Any,
                    16,
                    requireAuthenticatedConnection: true,
                    requireEncryptedTransport: false,
                    enableReplayProtection: false))
            });
            var connection = new TestConnection(1, authenticated: false);
            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);
            byte[] payload = new byte[8];

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 0d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.AreEqual(MessageSecurityResult.AuthenticationRequired, result.Result);
        }

        [Test]
        public void NetworkSecurityPipeline_Rejects_Unencrypted_Transport()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                    NetworkMessageDirectionMask.Any,
                    16,
                    requireAuthenticatedConnection: false,
                    requireEncryptedTransport: true,
                    enableReplayProtection: false))
            });
            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);
            byte[] payload = new byte[8];

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                null,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 0d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.AreEqual(MessageSecurityResult.EncryptionRequired, result.Result);
        }

        [Test]
        public void NetworkSecurityPipeline_Uses_PerMessage_Override()
        {
            var registry = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                64,
                requireAuthenticatedConnection: false,
                requireEncryptedTransport: false,
                enableReplayProtection: false));
            registry.SetPolicy(2000, new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                4,
                requireAuthenticatedConnection: false,
                requireEncryptedTransport: false,
                enableReplayProtection: false));
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = registry
            });

            var envelope = new NetworkMessageEnvelope(2000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);
            byte[] payload = new byte[8];

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                null,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 0d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.AreEqual(MessageSecurityResult.PayloadTooLarge, result.Result);
        }

        [Test]
        public void NetworkReplayGuard_Rejects_Replayed_Sequence()
        {
            var replayGuard = new NetworkReplayGuard();

            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 1, 0d));
            Assert.IsFalse(replayGuard.TryAccept(1, 1000, 1, 0d));
            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 2, 0d));
        }

        [Test]
        public void NetworkReplayGuard_Allows_Bounded_Reordering_And_Sequence_Wrap()
        {
            var replayGuard = new NetworkReplayGuard();

            Assert.IsTrue(replayGuard.TryAccept(1, 1000, uint.MaxValue - 1, 0d));
            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 1, 0d));
            Assert.IsTrue(replayGuard.TryAccept(1, 1000, uint.MaxValue, 0d));
            Assert.IsFalse(replayGuard.TryAccept(1, 1000, uint.MaxValue, 0d));
        }

        [Test]
        public void NetworkReplayGuard_Rejects_New_Stream_After_Capacity_Is_Exhausted()
        {
            var replayGuard = new NetworkReplayGuard(maxConnections: 1, maxStreamsPerConnection: 1);

            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 1, 1d));
            Assert.IsFalse(replayGuard.TryAccept(1, 1001, 1, 1d));
            Assert.IsFalse(replayGuard.TryAccept(2, 1000, 1, 1d));
        }

        [Test]
        public void NetworkReplayGuard_Reuses_Expired_Stream_Capacity()
        {
            var replayGuard = new NetworkReplayGuard(
                maxConnections: 1,
                maxStreamsPerConnection: 1,
                idleTimeoutSeconds: 2d);

            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 1, 0d));
            Assert.IsTrue(replayGuard.TryAccept(1, 1001, 1, 2d));
            Assert.IsFalse(replayGuard.TryAccept(1, 1000, 2, 2d));
        }

        [Test]
        public void NetworkReplayGuard_Rejects_Clock_Regression()
        {
            var replayGuard = new NetworkReplayGuard();

            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 1, 10d));
            Assert.IsFalse(replayGuard.TryAccept(1, 1000, 2, 9d));
            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 2, 10d));
        }

        [Test]
        public void NetworkFrameCodec_RoundTrips_Header_And_Payload()
        {
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength + 3];
            frame[NetworkWireProtocol.HeaderLength] = 10;
            frame[NetworkWireProtocol.HeaderLength + 1] = 20;
            frame[NetworkWireProtocol.HeaderLength + 2] = 30;
            var payload = new ReadOnlySpan<byte>(frame, NetworkWireProtocol.HeaderLength, 3);
            uint checksum = NetworkFrameCodec.ComputeChecksum(1200, NetworkChannel.Unreliable, NetworkMessageFlags.None, 7, payload);
            var header = new NetworkEnvelopeHeader(1200, NetworkChannel.Unreliable, 3, 7, checksum);

            NetworkFrameCodec.WriteHeader(frame, 0, header);

            Assert.AreEqual(NetworkFrameResult.Valid, NetworkFrameCodec.TryReadPayload(new ArraySegment<byte>(frame), out NetworkEnvelopeHeader readHeader, out ArraySegment<byte> readPayload));
            Assert.AreEqual(1200, readHeader.MessageId);
            Assert.AreEqual(NetworkChannel.Unreliable, readHeader.Channel);
            Assert.AreEqual(3, readHeader.PayloadLength);
            Assert.AreEqual(NetworkFrameResult.Valid, NetworkFrameCodec.ValidateChecksum(readHeader, new ReadOnlySpan<byte>(readPayload.Array, readPayload.Offset, readPayload.Count)));
        }

        [Test]
        public void NetworkFrameCodec_WritesReadableMagicAndExplicitHeaderLayout()
        {
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength];
            var header = new NetworkEnvelopeHeader(1200, NetworkChannel.Reliable, 0, 7, 0);

            NetworkFrameCodec.WriteHeader(frame, 0, header);

            Assert.AreEqual((byte)'C', frame[NetworkWireProtocol.MagicOffset]);
            Assert.AreEqual((byte)'N', frame[NetworkWireProtocol.MagicOffset + 1]);
            Assert.AreEqual(NetworkWireProtocol.CurrentVersion, frame[NetworkWireProtocol.VersionOffset]);
            Assert.AreEqual(NetworkWireProtocol.HeaderLength, frame[NetworkWireProtocol.HeaderLengthOffset]);
            Assert.AreEqual(22, NetworkWireProtocol.HeaderLength);
        }

        [Test]
        public void NetworkFrameCodec_Rejects_Tampered_Payload()
        {
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength + 1];
            frame[NetworkWireProtocol.HeaderLength] = 42;
            var payload = new ReadOnlySpan<byte>(frame, NetworkWireProtocol.HeaderLength, 1);
            uint checksum = NetworkFrameCodec.ComputeChecksum(1200, NetworkChannel.Reliable, NetworkMessageFlags.Reliable, 1, payload);
            var header = new NetworkEnvelopeHeader(1200, NetworkChannel.Reliable, 1, 1, checksum, NetworkMessageFlags.Reliable);
            NetworkFrameCodec.WriteHeader(frame, 0, header);
            frame[NetworkWireProtocol.HeaderLength] = 43;

            Assert.AreEqual(NetworkFrameResult.Valid, NetworkFrameCodec.TryReadPayload(new ArraySegment<byte>(frame), out NetworkEnvelopeHeader readHeader, out ArraySegment<byte> readPayload));
            Assert.AreEqual(NetworkFrameResult.InvalidChecksum, NetworkFrameCodec.ValidateChecksum(readHeader, new ReadOnlySpan<byte>(readPayload.Array, readPayload.Offset, readPayload.Count)));
        }

        [Test]
        public void NetworkFrameCodec_Rejects_Unsupported_Version()
        {
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength];
            var header = new NetworkEnvelopeHeader(1200, NetworkChannel.Reliable, 0, 1, 0);
            NetworkFrameCodec.WriteHeader(frame, 0, header);
            frame[NetworkWireProtocol.VersionOffset] = unchecked((byte)(NetworkWireProtocol.CurrentVersion + 1));

            Assert.AreEqual(NetworkFrameResult.UnsupportedVersion, NetworkFrameCodec.TryReadHeader(new ArraySegment<byte>(frame), out _));
        }

        [Test]
        public void NetworkFrameCodec_Rejects_Trailing_Bytes()
        {
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength + 1];
            NetworkFrameCodec.WriteHeader(
                frame,
                0,
                new NetworkEnvelopeHeader(1200, NetworkChannel.Reliable, 0, 1, 0));

            Assert.AreEqual(
                NetworkFrameResult.InvalidPayloadLength,
                NetworkFrameCodec.TryReadPayload(new ArraySegment<byte>(frame), out _, out _));
        }

        [Test]
        public void NetworkFrameCodec_Rejects_Unknown_Flags_Channel_And_Reserved_Byte()
        {
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength];
            NetworkFrameCodec.WriteHeader(
                frame,
                0,
                new NetworkEnvelopeHeader(1200, NetworkChannel.Reliable, 0, 1, 0));

            frame[NetworkWireProtocol.FlagsOffset + 1] = 0x80;
            Assert.AreEqual(NetworkFrameResult.InvalidFlags, NetworkFrameCodec.TryReadHeader(frame, out _));

            frame[NetworkWireProtocol.FlagsOffset + 1] = 0;
            frame[NetworkWireProtocol.ChannelOffset] = byte.MaxValue;
            Assert.AreEqual(NetworkFrameResult.InvalidChannel, NetworkFrameCodec.TryReadHeader(frame, out _));

            frame[NetworkWireProtocol.ChannelOffset] = (byte)NetworkChannel.Reliable;
            frame[NetworkWireProtocol.ReservedOffset] = 1;
            Assert.AreEqual(NetworkFrameResult.InvalidReservedByte, NetworkFrameCodec.TryReadHeader(frame, out _));
        }

        [Test]
        public void NetworkFrameCodec_GetFrameLength_Rejects_Integer_Overflow()
        {
            Assert.Throws<OverflowException>(() => NetworkFrameCodec.GetFrameLength(int.MaxValue));
        }

        private struct TestConnection : INetConnection
        {
            public TestConnection(int connectionId, bool authenticated)
            {
                ConnectionId = connectionId;
                IsAuthenticated = authenticated;
                PlayerId = 0UL;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => "test";
            public bool IsConnected => true;
            public bool IsAuthenticated { get; }
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Good;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }
            public bool Equals(INetConnection other) => other != null && ConnectionId == other.ConnectionId;
        }
    }
}
