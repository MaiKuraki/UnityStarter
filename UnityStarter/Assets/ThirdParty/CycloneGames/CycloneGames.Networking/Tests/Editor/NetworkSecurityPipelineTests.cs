using System;
using System.Collections.Generic;
using CycloneGames.Networking.Authentication;
using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkSecurityPipelineTests
    {
        [Test]
        public void AuthenticationProviderChain_Uses_First_Supported_Provider()
        {
            var chain = new NetworkAuthenticationProviderChain();
            chain.Add(new UnsupportedAuthenticationProvider());
            chain.Add(new AcceptingAuthenticationProvider(42UL));

            NetworkAuthenticationResult result = chain.Authenticate(
                new TestConnection(1, authenticated: false),
                ReadOnlySpan<byte>.Empty,
                new NetworkAuthenticationContext("test", NetworkWireProtocol.CurrentVersion));

            Assert.IsTrue(result.IsAccepted);
            Assert.AreEqual(42UL, result.Principal.PlayerId);
        }

        [Test]
        public void SecurityPipeline_Rejects_Missing_Required_Signature()
        {
            var sink = new RecordingSignalSink();
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                AntiCheatSignalSink = sink,
                MessagePolicies = new MessageSecurityPolicyRegistry(
                    MessageSecurityPolicy.Default.WithSignatureRequired(true))
            });
            byte[] payload = { 1, 2, 3 };
            var envelope = new NetworkMessageEnvelope(
                1200,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length);

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                new TestConnection(1, authenticated: true),
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 0d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.AreEqual(MessageSecurityResult.SignatureRequired, result.Result);
            Assert.AreEqual(1, sink.Signals.Count);
            Assert.AreEqual(NetworkAntiCheatSignalIds.SignatureRejected, sink.Signals[0].SignalId);
        }

        [Test]
        public void SecurityPipeline_Accepts_Signed_Message_And_Rejects_Replay()
        {
            byte[] key = CreateTestKey();
            using var signer = new HmacSha256NetworkMessageSigner(key);
            var sink = new RecordingSignalSink();
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                AntiCheatSignalSink = sink,
                MessageSigner = signer,
                MessagePolicies = new MessageSecurityPolicyRegistry(
                    MessageSecurityPolicy.Default
                        .WithAuthenticatedConnectionRequired(true)
                        .WithReplayProtection(true)
                        .WithSignatureRequired(true))
            });
            byte[] payload = { 8, 6, 7, 5 };
            var envelope = new NetworkMessageEnvelope(
                1201,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length,
                sequence: 1u);
            Span<byte> signature = stackalloc byte[HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH];

            Assert.IsTrue(pipeline.TrySign(new TestConnection(7, authenticated: true), envelope, payload, signature, out int writtenBytes));
            Assert.AreEqual(HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH, writtenBytes);

            NetworkSecurityPipelineResult first = pipeline.ValidateInbound(
                new TestConnection(7, authenticated: true),
                envelope,
                payload,
                signature,
                transportEncrypted: false,
                currentTime: 1d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length + signature.Length);
            NetworkSecurityPipelineResult replay = pipeline.ValidateInbound(
                new TestConnection(7, authenticated: true),
                envelope,
                payload,
                signature,
                transportEncrypted: false,
                currentTime: 1.1d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length + signature.Length);

            Assert.IsTrue(first.Accepted);
            Assert.AreEqual(MessageSecurityResult.ReplayRejected, replay.Result);
            Assert.AreEqual(1, sink.Signals.Count);
            Assert.AreEqual(NetworkAntiCheatSignalIds.ReplayRejected, sink.Signals[0].SignalId);
        }

        [Test]
        public void SecurityPipeline_Does_Not_Advance_Replay_Window_For_Invalid_Signature()
        {
            byte[] key = CreateTestKey();
            using var signer = new HmacSha256NetworkMessageSigner(key);
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessageSigner = signer,
                MessagePolicies = new MessageSecurityPolicyRegistry(
                    MessageSecurityPolicy.Default
                        .WithReplayProtection(true)
                        .WithSignatureRequired(true))
            });
            byte[] payload = { 3, 1, 4, 1 };
            var envelope = new NetworkMessageEnvelope(
                1203,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length,
                sequence: 1u);
            Span<byte> validSignature = stackalloc byte[HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH];
            Span<byte> invalidSignature = stackalloc byte[HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH];
            Assert.IsTrue(pipeline.TrySign(new TestConnection(11, authenticated: true), envelope, payload, validSignature, out _));

            NetworkSecurityPipelineResult invalid = pipeline.ValidateInbound(
                new TestConnection(11, authenticated: true),
                envelope,
                payload,
                invalidSignature,
                transportEncrypted: false,
                currentTime: 2d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length + invalidSignature.Length);
            NetworkSecurityPipelineResult valid = pipeline.ValidateInbound(
                new TestConnection(11, authenticated: true),
                envelope,
                payload,
                validSignature,
                transportEncrypted: false,
                currentTime: 2.1d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length + validSignature.Length);

            Assert.AreEqual(MessageSecurityResult.SignatureRejected, invalid.Result);
            Assert.IsTrue(valid.Accepted);
        }

        [Test]
        public void HmacSigner_Uses_Wire_Envelope_Not_TransportLocal_Connection_Identity()
        {
            byte[] key = CreateTestKey();
            using var signer = new HmacSha256NetworkMessageSigner(key);
            byte[] payload = { 1, 2, 3 };
            var envelope = new NetworkMessageEnvelope(
                1205,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length,
                sequence: 9u,
                checksum: 42u);
            Span<byte> signature = stackalloc byte[HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH];
            var signingConnection = new TestConnection(21, authenticated: true) { PlayerId = 100UL };
            var verifyingConnection = new TestConnection(22, authenticated: true) { PlayerId = 200UL };

            Assert.IsTrue(signer.TrySign(signingConnection, envelope, payload, signature, out _));
            Assert.IsTrue(signer.TryVerify(signingConnection, envelope, payload, signature));
            Assert.IsTrue(signer.TryVerify(verifyingConnection, envelope, payload, signature));

            var receiverEnvelope = new NetworkMessageEnvelope(
                1205,
                NetworkMessageDirection.PeerToPeer,
                NetworkChannel.Reliable,
                payload.Length,
                sequence: 9u,
                checksum: 42u);
            Assert.IsTrue(signer.TryVerify(verifyingConnection, receiverEnvelope, payload, signature));

            var alteredEnvelope = new NetworkMessageEnvelope(
                1205,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length,
                sequence: 10u,
                checksum: 42u);
            Assert.IsFalse(signer.TryVerify(verifyingConnection, alteredEnvelope, payload, signature));
        }

        [Test]
        public void HmacSigner_Rejects_Payload_Length_Mismatch_Without_Throwing()
        {
            using var signer = new HmacSha256NetworkMessageSigner(CreateTestKey());
            var envelope = new NetworkMessageEnvelope(
                1206,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payloadLength: 2);
            Span<byte> signature = stackalloc byte[HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH];

            Assert.IsFalse(signer.TrySign(null, envelope, new byte[] { 1 }, signature, out int written));
            Assert.AreEqual(0, written);
        }

        [Test]
        public void HmacSigner_Wire_Format_Matches_Frozen_KnownAnswer_Vector()
        {
            byte[] key = CreateSequentialTestKey();
            using var signer = new HmacSha256NetworkMessageSigner(key);
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };
            var envelope = new NetworkMessageEnvelope(
                0x1234,
                NetworkMessageDirection.ServerToClient,
                NetworkChannel.Unreliable,
                payload.Length,
                sequence: 0x01020304u,
                checksum: 0xA1B2C3D4u,
                flags: NetworkMessageFlags.Reliable | NetworkMessageFlags.Ordered);
            byte[] expected =
            {
                0x27, 0xAF, 0xE7, 0x7D, 0x9D, 0xB4, 0x46, 0xE1,
                0x47, 0x09, 0x7D, 0x23, 0x2E, 0x55, 0x91, 0xB3,
                0x05, 0x24, 0x11, 0xD1, 0xC0, 0x46, 0xFE, 0x89,
                0x98, 0xB0, 0xD2, 0x6A, 0xF0, 0x9D, 0x26, 0x9F
            };
            Span<byte> signature = stackalloc byte[HmacSha256NetworkMessageSigner.SIGNATURE_LENGTH];

            Assert.IsTrue(signer.TrySign(null, envelope, payload, signature, out int written));
            Assert.AreEqual(expected.Length, written);
            CollectionAssert.AreEqual(expected, signature.ToArray());
        }

        [Test]
        public void HmacSigner_Rejects_Keys_Shorter_Than_256_Bits()
        {
            Assert.Throws<ArgumentException>(() =>
                new HmacSha256NetworkMessageSigner(new byte[HmacSha256NetworkMessageSigner.MINIMUM_KEY_LENGTH - 1]));
        }

        [Test]
        public void SecurityPipeline_Reports_Rate_Limit_Rejections()
        {
            var sink = new RecordingSignalSink();
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                AntiCheatSignalSink = sink,
                RateLimiter = new RateLimiter(maxMessagesPerSecond: 1, maxBytesPerSecond: 1024, burstLimit: 0)
            });
            byte[] payload = { 1 };
            var envelope = new NetworkMessageEnvelope(
                1202,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Unreliable,
                payload.Length);
            var connection = new TestConnection(9, authenticated: true);

            NetworkSecurityPipelineResult first = pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 3d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);
            NetworkSecurityPipelineResult second = pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 3d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.IsTrue(first.Accepted);
            Assert.AreEqual(MessageSecurityResult.RateLimited, second.Result);
            Assert.AreEqual(1, sink.Signals.Count);
            Assert.AreEqual(NetworkAntiCheatSignalIds.RateLimited, sink.Signals[0].SignalId);
        }

        [Test]
        public void SecurityPipeline_Double_Time_Retains_LongSession_OneSecond_Precision()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                RateLimiter = new RateLimiter(maxMessagesPerSecond: 1, maxBytesPerSecond: 1024, burstLimit: 0)
            });
            byte[] payload = { 1 };
            var envelope = new NetworkMessageEnvelope(
                1207,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length);
            var connection = new TestConnection(13, authenticated: true);
            const double LongSessionTime = 16_777_216d;

            NetworkSecurityPipelineResult first = pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: LongSessionTime,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);
            NetworkSecurityPipelineResult second = pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: LongSessionTime + 1d,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);

            Assert.IsTrue(first.Accepted);
            Assert.IsTrue(second.Accepted);
        }

        [Test]
        public void SecurityPipeline_Uses_Explicit_Rate_Limit_Byte_Charge()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                RateLimiter = new RateLimiter(maxMessagesPerSecond: 60, maxBytesPerSecond: 3, burstLimit: 0)
            });
            byte[] payload = { 1 };
            var envelope = new NetworkMessageEnvelope(
                1204,
                NetworkMessageDirection.ClientToServer,
                NetworkChannel.Reliable,
                payload.Length);

            NetworkSecurityPipelineResult result = pipeline.ValidateInbound(
                new TestConnection(12, authenticated: true),
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 4f,
                rateLimitBytes: 5);

            Assert.AreEqual(MessageSecurityResult.RateLimited, result.Result);
        }

        [Test]
        public void AuthoritativeValidator_Composite_Stops_On_Rejected_Validator()
        {
            var validator = new CompositeNetworkAuthoritativeValidator<TestCommand, TestState>();
            var accepting = new CountingAuthorityValidator(NetworkAuthorityValidationResult.Accept());
            var rejecting = new CountingAuthorityValidator(NetworkAuthorityValidationResult.Reject("Rejected by rule."));
            var afterReject = new CountingAuthorityValidator(NetworkAuthorityValidationResult.Accept());
            validator.Add(accepting);
            validator.Add(rejecting);
            validator.Add(afterReject);

            NetworkAuthorityValidationResult result = validator.Validate(
                new TestConnection(3, authenticated: true),
                new TestCommand(1),
                new TestState(2),
                new NetworkAuthorityValidationContext(10, 1.5d));

            Assert.AreEqual(NetworkAuthorityValidationStatus.Rejected, result.Status);
            Assert.AreEqual(1, accepting.CallCount);
            Assert.AreEqual(1, rejecting.CallCount);
            Assert.AreEqual(0, afterReject.CallCount);
        }

        [Test]
        public void AuthoritativeValidator_Composite_Stops_On_DefaultInvalid_Result()
        {
            var validator = new CompositeNetworkAuthoritativeValidator<TestCommand, TestState>();
            var invalid = new CountingAuthorityValidator(default);
            var afterInvalid = new CountingAuthorityValidator(NetworkAuthorityValidationResult.Accept());
            validator.Add(invalid);
            validator.Add(afterInvalid);

            NetworkAuthorityValidationResult result = validator.Validate(
                new TestConnection(3, authenticated: true),
                new TestCommand(1),
                new TestState(2),
                new NetworkAuthorityValidationContext(10, 1.5d));

            Assert.AreEqual(NetworkAuthorityValidationStatus.Invalid, result.Status);
            Assert.IsFalse(result.IsAccepted);
            Assert.AreEqual(1, invalid.CallCount);
            Assert.AreEqual(0, afterInvalid.CallCount);
        }

        private static byte[] CreateTestKey()
        {
            var key = new byte[HmacSha256NetworkMessageSigner.MINIMUM_KEY_LENGTH];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(i * 7 + 1);
            }

            return key;
        }

        private static byte[] CreateSequentialTestKey()
        {
            var key = new byte[HmacSha256NetworkMessageSigner.MINIMUM_KEY_LENGTH];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)i;
            }

            return key;
        }

        private readonly struct TestCommand
        {
            public readonly int Value;

            public TestCommand(int value)
            {
                Value = value;
            }
        }

        private readonly struct TestState
        {
            public readonly int Value;

            public TestState(int value)
            {
                Value = value;
            }
        }

        private sealed class UnsupportedAuthenticationProvider : INetworkAuthenticationProvider
        {
            public NetworkAuthenticationResult Authenticate(
                INetConnection connection,
                ReadOnlySpan<byte> credentials,
                in NetworkAuthenticationContext context)
            {
                return NetworkAuthenticationResult.Reject("Unsupported.", NetworkAuthenticationStatus.Unsupported);
            }
        }

        private sealed class AcceptingAuthenticationProvider : INetworkAuthenticationProvider
        {
            private readonly ulong _playerId;

            public AcceptingAuthenticationProvider(ulong playerId)
            {
                _playerId = playerId;
            }

            public NetworkAuthenticationResult Authenticate(
                INetConnection connection,
                ReadOnlySpan<byte> credentials,
                in NetworkAuthenticationContext context)
            {
                return NetworkAuthenticationResult.Accept(new NetworkPrincipal(_playerId, "test-user"));
            }
        }

        private sealed class CountingAuthorityValidator : INetworkAuthoritativeValidator<TestCommand, TestState>
        {
            private readonly NetworkAuthorityValidationResult _result;

            public CountingAuthorityValidator(NetworkAuthorityValidationResult result)
            {
                _result = result;
            }

            public int CallCount { get; private set; }

            public NetworkAuthorityValidationResult Validate(
                INetConnection connection,
                in TestCommand command,
                in TestState serverState,
                in NetworkAuthorityValidationContext context)
            {
                CallCount++;
                return _result;
            }
        }

        private sealed class RecordingSignalSink : INetworkAntiCheatSignalSink
        {
            public readonly List<NetworkAntiCheatSignal> Signals = new List<NetworkAntiCheatSignal>();

            public void Report(in NetworkAntiCheatSignal signal)
            {
                Signals.Add(signal);
            }
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
