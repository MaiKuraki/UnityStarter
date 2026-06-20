using System;
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
            var sink = new RecordingNetworkAntiCheatSignalSink();
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
                currentTime: 0f);

            Assert.AreEqual(MessageSecurityResult.SignatureRequired, result.Result);
            Assert.AreEqual(1, sink.Signals.Count);
            Assert.AreEqual(NetworkAntiCheatSignalIds.SignatureRejected, sink.Signals[0].SignalId);
        }

        [Test]
        public void SecurityPipeline_Accepts_Signed_Message_And_Rejects_Replay()
        {
            byte[] key = { 1, 7, 9, 11, 13, 17, 19, 23 };
            using var signer = new HmacSha256NetworkMessageSigner(key);
            var sink = new RecordingNetworkAntiCheatSignalSink();
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
                currentTime: 1f);
            NetworkSecurityPipelineResult replay = pipeline.ValidateInbound(
                new TestConnection(7, authenticated: true),
                envelope,
                payload,
                signature,
                transportEncrypted: false,
                currentTime: 1.1f);

            Assert.IsTrue(first.Accepted);
            Assert.AreEqual(MessageSecurityResult.ReplayRejected, replay.Result);
            Assert.AreEqual(1, sink.Signals.Count);
            Assert.AreEqual(NetworkAntiCheatSignalIds.ReplayRejected, sink.Signals[0].SignalId);
        }

        [Test]
        public void SecurityPipeline_Does_Not_Advance_Replay_Window_For_Invalid_Signature()
        {
            byte[] key = { 2, 4, 6, 8, 10, 12, 14, 16 };
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
                currentTime: 2f);
            NetworkSecurityPipelineResult valid = pipeline.ValidateInbound(
                new TestConnection(11, authenticated: true),
                envelope,
                payload,
                validSignature,
                transportEncrypted: false,
                currentTime: 2.1f);

            Assert.AreEqual(MessageSecurityResult.SignatureRejected, invalid.Result);
            Assert.IsTrue(valid.Accepted);
        }

        [Test]
        public void SecurityPipeline_Reports_Rate_Limit_Rejections()
        {
            var sink = new RecordingNetworkAntiCheatSignalSink();
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                AntiCheatSignalSink = sink,
                EnableRateLimiting = true,
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
                currentTime: 3f);
            NetworkSecurityPipelineResult second = pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime: 3f);

            Assert.IsTrue(first.Accepted);
            Assert.AreEqual(MessageSecurityResult.RateLimited, second.Result);
            Assert.AreEqual(1, sink.Signals.Count);
            Assert.AreEqual(NetworkAntiCheatSignalIds.RateLimited, sink.Signals[0].SignalId);
        }

        [Test]
        public void SecurityPipeline_Uses_Explicit_Rate_Limit_Byte_Charge()
        {
            var pipeline = new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                EnableRateLimiting = true,
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
