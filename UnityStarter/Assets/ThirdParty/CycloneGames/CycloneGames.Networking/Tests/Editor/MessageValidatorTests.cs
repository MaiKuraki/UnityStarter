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
        public void MessageSecurityPolicyRegistry_Allows_Default_Envelope()
        {
            var registry = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                16,
                requireAuthenticatedConnection: false,
                requireEncryptedTransport: false,
                enableReplayProtection: false));

            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);

            Assert.AreEqual(MessageSecurityResult.Valid, registry.Validate(envelope, null, transportEncrypted: false, replayGuard: null));
        }

        [Test]
        public void MessageSecurityPolicyRegistry_Rejects_Disallowed_Direction()
        {
            var registry = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                NetworkMessageDirectionMask.ServerToClient,
                16,
                requireAuthenticatedConnection: false,
                requireEncryptedTransport: false,
                enableReplayProtection: false));

            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);

            Assert.AreEqual(MessageSecurityResult.DirectionRejected, registry.Validate(envelope, null, transportEncrypted: false, replayGuard: null));
        }

        [Test]
        public void MessageSecurityPolicyRegistry_Rejects_Unauthenticated_Connection()
        {
            var registry = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                16,
                requireAuthenticatedConnection: true,
                requireEncryptedTransport: false,
                enableReplayProtection: false));
            var connection = new TestConnection(1, authenticated: false);
            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);

            Assert.AreEqual(MessageSecurityResult.AuthenticationRequired, registry.Validate(envelope, connection, transportEncrypted: false, replayGuard: null));
        }

        [Test]
        public void MessageSecurityPolicyRegistry_Rejects_Unencrypted_Transport()
        {
            var registry = new MessageSecurityPolicyRegistry(new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                16,
                requireAuthenticatedConnection: false,
                requireEncryptedTransport: true,
                enableReplayProtection: false));
            var envelope = new NetworkMessageEnvelope(1000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);

            Assert.AreEqual(MessageSecurityResult.EncryptionRequired, registry.Validate(envelope, null, transportEncrypted: false, replayGuard: null));
        }

        [Test]
        public void MessageSecurityPolicyRegistry_Uses_PerMessage_Override()
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

            var envelope = new NetworkMessageEnvelope(2000, NetworkMessageDirection.ClientToServer, NetworkChannel.Reliable, 8);

            Assert.AreEqual(MessageSecurityResult.PayloadTooLarge, registry.Validate(envelope, null, transportEncrypted: false, replayGuard: null));
        }

        [Test]
        public void NetworkReplayGuard_Rejects_Replayed_Sequence()
        {
            var replayGuard = new NetworkReplayGuard();

            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 1));
            Assert.IsFalse(replayGuard.TryAccept(1, 1000, 1));
            Assert.IsTrue(replayGuard.TryAccept(1, 1000, 2));
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
