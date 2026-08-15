using System;
using CycloneGames.Networking;
using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class GameplayFrameworkNetworkProtocolTests
    {
        [Test]
        public void ProtocolRegistersBoundedVersionTwoManifest()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.AreEqual(3, catalog.MessageCount);
            Assert.AreEqual(1, catalog.ManifestCount);
            Assert.AreEqual(2, GameplayFrameworkNetworkProtocol.DefaultManifest.CurrentVersion);
            Assert.AreEqual(2, GameplayFrameworkNetworkProtocol.DefaultManifest.MinimumSupportedVersion);
            Assert.AreEqual(
                "ActorMigrationState:v1",
                GameplayFrameworkNetworkProtocol.DefaultManifest.Messages[0].ContractId);
            Assert.AreEqual(
                "DamageRequestMessage:v1",
                GameplayFrameworkNetworkProtocol.DefaultManifest.Messages[1].ContractId);
            Assert.AreEqual(
                "DamageResultMessage:v2",
                GameplayFrameworkNetworkProtocol.DefaultManifest.Messages[2].ContractId);
            Assert.AreEqual(
                ActorMigrationNetworkingExtensions.MaximumEncodedSize,
                GameplayFrameworkNetworkProtocol.DefaultManifest.Messages[0].MaxPayloadSize);
            Assert.AreEqual(
                GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes,
                GameplayFrameworkNetworkProtocol.DefaultManifest.Messages[1].MaxPayloadSize);
            Assert.AreEqual(
                GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes,
                GameplayFrameworkNetworkProtocol.DefaultManifest.Messages[2].MaxPayloadSize);
            Assert.AreEqual(
                "895DD9A4C8618476",
                GameplayFrameworkNetworkProtocol.DefaultManifest.Metadata["damageWireSchemaFingerprint"]);
            Assert.AreEqual(0x4853FB7FFAE15D14UL, GameplayFrameworkNetworkProtocol.ProtocolFingerprint);
        }

        [Test]
        public void SecurityPoliciesUseExactDirectionsBudgetsAndAuthentication()
        {
            var configurable = new TestPolicyConfigurable();

            GameplayFrameworkNetworkSecurityPolicies.Configure(
                configurable,
                NetworkMessageDirectionMask.ServerToClient,
                requireEncryptedTransport: true,
                requireSignature: true);

            MessageSecurityPolicy migration = configurable.Registry.GetPolicy(
                GameplayFrameworkNetworkProtocol.MsgActorMigrationState);
            MessageSecurityPolicy request = configurable.Registry.GetPolicy(
                GameplayFrameworkNetworkProtocol.MsgDamageRequest);
            MessageSecurityPolicy result = configurable.Registry.GetPolicy(
                GameplayFrameworkNetworkProtocol.MsgDamageResult);

            AssertPolicy(
                migration,
                NetworkMessageDirectionMask.ServerToClient,
                ActorMigrationNetworkingExtensions.MaximumEncodedSize);
            AssertPolicy(
                request,
                NetworkMessageDirectionMask.ClientToServer,
                GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes);
            AssertPolicy(
                result,
                NetworkMessageDirectionMask.ServerToClient | NetworkMessageDirectionMask.ServerBroadcast,
                GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes);
        }

        [Test]
        public void SecurityPoliciesRequireExplicitValidMigrationDirection()
        {
            var configurable = new TestPolicyConfigurable();

            Assert.Throws<ArgumentNullException>(() =>
                GameplayFrameworkNetworkSecurityPolicies.Configure(
                    null,
                    NetworkMessageDirectionMask.ServerToClient));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GameplayFrameworkNetworkSecurityPolicies.Configure(
                    configurable,
                    NetworkMessageDirectionMask.None));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GameplayFrameworkNetworkSecurityPolicies.Configure(
                    configurable,
                    (NetworkMessageDirectionMask)0x80));
        }

        [Test]
        public void DamageRequestPolicyRejectsWrongDirectionUnauthenticatedOversizeReplayAndRate()
        {
            byte[] payload = new byte[GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes];
            var authenticated = new TestConnection(1, authenticated: true);
            var unauthenticated = new TestConnection(2, authenticated: false);

            NetworkSecurityPipeline wrongDirectionPipeline = CreatePipeline();
            Assert.AreEqual(
                MessageSecurityResult.DirectionRejected,
                Validate(
                    wrongDirectionPipeline,
                    authenticated,
                    NetworkMessageDirection.ServerToClient,
                    payload,
                    sequence: 1u,
                    currentTime: 1d).Result);

            NetworkSecurityPipeline unauthenticatedPipeline = CreatePipeline();
            Assert.AreEqual(
                MessageSecurityResult.AuthenticationRequired,
                Validate(
                    unauthenticatedPipeline,
                    unauthenticated,
                    NetworkMessageDirection.ClientToServer,
                    payload,
                    sequence: 1u,
                    currentTime: 1d).Result);

            NetworkSecurityPipeline oversizedPipeline = CreatePipeline();
            Assert.AreEqual(
                MessageSecurityResult.PayloadTooLarge,
                Validate(
                    oversizedPipeline,
                    authenticated,
                    NetworkMessageDirection.ClientToServer,
                    new byte[payload.Length + 1],
                    sequence: 1u,
                    currentTime: 1d).Result);

            NetworkSecurityPipeline replayPipeline = CreatePipeline();
            Assert.IsTrue(Validate(
                replayPipeline,
                authenticated,
                NetworkMessageDirection.ClientToServer,
                payload,
                sequence: 7u,
                currentTime: 1d).Accepted);
            Assert.AreEqual(
                MessageSecurityResult.ReplayRejected,
                Validate(
                    replayPipeline,
                    authenticated,
                    NetworkMessageDirection.ClientToServer,
                    payload,
                    sequence: 7u,
                    currentTime: 1.1d).Result);

            NetworkSecurityPipeline ratePipeline = CreatePipeline(
                new RateLimiter(
                    maxMessagesPerSecond: 1,
                    maxBytesPerSecond: 1024,
                    burstLimit: 0));
            Assert.IsTrue(Validate(
                ratePipeline,
                authenticated,
                NetworkMessageDirection.ClientToServer,
                payload,
                sequence: 1u,
                currentTime: 2d).Accepted);
            Assert.AreEqual(
                MessageSecurityResult.RateLimited,
                Validate(
                    ratePipeline,
                    authenticated,
                    NetworkMessageDirection.ClientToServer,
                    payload,
                    sequence: 2u,
                    currentTime: 2d).Result);
        }

        private static void AssertPolicy(
            in MessageSecurityPolicy policy,
            NetworkMessageDirectionMask directions,
            int maxPayloadSize)
        {
            Assert.AreEqual(directions, policy.AllowedDirections);
            Assert.AreEqual(maxPayloadSize, policy.MaxPayloadSize);
            Assert.IsTrue(policy.RequireAuthenticatedConnection);
            Assert.IsTrue(policy.RequireEncryptedTransport);
            Assert.IsTrue(policy.EnableReplayProtection);
            Assert.IsTrue(policy.RequireSignature);
        }

        private static NetworkSecurityPipeline CreatePipeline(RateLimiter rateLimiter = null)
        {
            var configurable = new TestPolicyConfigurable();
            GameplayFrameworkNetworkSecurityPolicies.Configure(
                configurable,
                NetworkMessageDirectionMask.ServerToClient);
            return new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = configurable.Registry,
                RateLimiter = rateLimiter
            });
        }

        private static NetworkSecurityPipelineResult Validate(
            NetworkSecurityPipeline pipeline,
            INetConnection connection,
            NetworkMessageDirection direction,
            byte[] payload,
            uint sequence,
            double currentTime)
        {
            var envelope = new NetworkMessageEnvelope(
                GameplayFrameworkNetworkProtocol.MsgDamageRequest,
                direction,
                NetworkChannel.Reliable,
                payload.Length,
                sequence);
            return pipeline.ValidateInbound(
                connection,
                envelope,
                payload,
                ReadOnlySpan<byte>.Empty,
                transportEncrypted: false,
                currentTime,
                rateLimitBytes: NetworkWireProtocol.HeaderLength + payload.Length);
        }

        private sealed class TestPolicyConfigurable : INetworkSecurityPolicyConfigurable
        {
            public MessageSecurityPolicyRegistry Registry { get; } = new MessageSecurityPolicyRegistry();
            public MessageSecurityPolicy DefaultMessageSecurityPolicy => Registry.DefaultPolicy;
            public void SetDefaultMessageSecurityPolicy(MessageSecurityPolicy policy) => Registry.SetDefaultPolicy(policy);
            public void SetMessageSecurityPolicy(ushort messageId, MessageSecurityPolicy policy) =>
                Registry.SetPolicy(messageId, policy);
            public void ClearMessageSecurityPolicy(ushort messageId) => Registry.ClearPolicy(messageId);
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId, bool authenticated)
            {
                ConnectionId = connectionId;
                IsAuthenticated = authenticated;
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
            public ulong PlayerId { get; set; } = 1UL;
            public bool Equals(INetConnection other) =>
                other != null && other.ConnectionId == ConnectionId;
        }
    }
}
