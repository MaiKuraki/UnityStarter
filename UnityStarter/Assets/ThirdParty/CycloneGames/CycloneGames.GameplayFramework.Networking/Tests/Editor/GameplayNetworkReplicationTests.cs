using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class GameplayNetworkReplicationTests
    {
        [Test]
        public void AuthorityResolver_AssignsServerOwnerAndSimulatedRoles()
        {
            var resolver = new ServerAuthoritativeGameplayAuthorityResolver();
            var actor = CreateActor(ownerConnectionId: 2);

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.ServerAuthority,
                resolver.GetRole(new GameplayNetworkAuthorityContext(true, false, 0), actor));

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.AutonomousProxy,
                resolver.GetRole(new GameplayNetworkAuthorityContext(false, true, 2), actor));

            Assert.AreEqual(
                GameplayNetworkAuthorityRole.SimulatedProxy,
                resolver.GetRole(new GameplayNetworkAuthorityContext(false, true, 3), actor));

            Assert.IsTrue(resolver.CanWriteAuthoritativeState(new GameplayNetworkAuthorityContext(true, false, 0), actor));
            Assert.IsTrue(resolver.CanSendOwnerInput(new GameplayNetworkAuthorityContext(false, true, 2), actor));
            Assert.IsFalse(resolver.CanSendOwnerInput(new GameplayNetworkAuthorityContext(false, true, 3), actor));
        }

        [Test]
        public void ObserverResolver_OwnerOnly_ReturnsOwner()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var owner = new TestConnection(2);
            var other = new TestConnection(3);
            var candidates = new INetConnection[] { owner, other };
            var results = new List<INetConnection>(4);
            var context = new GameplayReplicationContext(CreateActor(ownerConnectionId: 2), GameplayReplicationPolicy.OwnerReliable);

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(1, count);
            Assert.AreSame(owner, results[0]);
        }

        [Test]
        public void ObserverResolver_AreaPolicy_FiltersByDistanceAndLayer()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var near = new TestConnection(2);
            var far = new TestConnection(3);
            var wrongLayer = new TestConnection(4);
            var candidates = new INetConnection[] { near, far, wrongLayer };
            var results = new List<INetConnection>(4);

            registry.SetObserver(2, new NetworkVector3(3f, 0f, 4f), 100f, 0b0001u);
            registry.SetObserver(3, new NetworkVector3(20f, 0f, 0f), 100f, 0b0001u);
            registry.SetObserver(4, new NetworkVector3(1f, 0f, 0f), 100f, 0b0100u);

            var actor = new NetworkedGameplayActor(
                null,
                10,
                ownerConnectionId: 99,
                ownerPlayerId: 0UL,
                teamId: 0,
                interestLayerMask: 0b0001u,
                alwaysRelevant: false,
                interestPosition: NetworkVector3.Zero);
            var context = new GameplayReplicationContext(actor, GameplayReplicationPolicy.AreaUnreliable(10f, layerMask: 0b0001u));

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(1, count);
            Assert.AreSame(near, results[0]);
        }

        [Test]
        public void ObserverResolver_TeamPolicy_ReturnsSameTeamAndOwner()
        {
            var resolver = new GameplayNetworkObserverResolver();
            var registry = new GameplayNetworkObserverRegistry();
            var owner = new TestConnection(2);
            var sameTeam = new TestConnection(3);
            var otherTeam = new TestConnection(4);
            var candidates = new INetConnection[] { owner, sameTeam, otherTeam };
            var results = new List<INetConnection>(4);

            registry.SetObserver(2, NetworkVector3.Zero, 100f, teamId: 1);
            registry.SetObserver(3, NetworkVector3.Zero, 100f, teamId: 1);
            registry.SetObserver(4, NetworkVector3.Zero, 100f, teamId: 2);

            var actor = CreateActor(ownerConnectionId: 2, teamId: 1);
            var context = new GameplayReplicationContext(actor, GameplayReplicationPolicy.TeamReliable);

            int count = resolver.ResolveObservers(context, candidates, registry, results);

            Assert.AreEqual(2, count);
            Assert.AreSame(owner, results[0]);
            Assert.AreSame(sameTeam, results[1]);
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_UsesGameplayFrameworkRange()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.IsTrue(catalog.TryGet(
                GameplayFrameworkNetworkProtocol.MsgActorMigrationState,
                out NetworkMessageDescriptor descriptor));
            Assert.IsTrue(GameplayFrameworkNetworkProtocol.MessageRange.Contains(descriptor.MessageId));
            Assert.IsTrue(NetworkMessageRanges.Module.Contains(descriptor.MessageId));
            Assert.IsTrue(catalog.TryGetRegisteredRange(descriptor.MessageId, out NetworkMessageIdRange range));
            Assert.AreEqual(GameplayFrameworkNetworkProtocol.MessageOwner, range.Name);
            Assert.AreEqual(GameplayFrameworkNetworkProtocol.MessageOwner, descriptor.Owner);
            Assert.AreEqual("ActorMigrationState:v1", descriptor.ContractId);
            Assert.AreEqual(NetworkChannel.Reliable, descriptor.DefaultChannel);
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_IsIdempotentForSameDescriptor()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            // ActorMigrationState + DamageRequest + DamageResult are registered; re-registering is idempotent.
            Assert.AreEqual(3, catalog.MessageCount);
            Assert.AreEqual(1, catalog.ManifestCount);
        }

        [Test]
        public void ProtocolManifest_UsesFrozenV1SchemasAndFingerprint()
        {
            NetworkProtocolManifest manifest = GameplayFrameworkNetworkProtocol.CreateProtocolManifest();
            string[] canonicalSchemaLiterals =
            {
                "ActorMigrationState:v1",
                "DamageRequestMessage:v1",
                "DamageResultMessage:v1"
            };
            ulong[] expectedSchemaHashes =
            {
                0x06A6A8934573CD8EUL,
                0x43A411569257B773UL,
                0x937BD1B6AA2D5D2BUL
            };

            Assert.AreEqual(0x1301C67656F5C3F9UL, manifest.Fingerprint);
            Assert.AreEqual(manifest.Fingerprint, GameplayFrameworkNetworkProtocol.ProtocolFingerprint);
            Assert.AreEqual(
                GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes,
                manifest.Messages[1].MaxPayloadSize);
            Assert.AreEqual(
                GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes,
                manifest.Messages[2].MaxPayloadSize);
            Assert.AreEqual(expectedSchemaHashes.Length, canonicalSchemaLiterals.Length);
            Assert.AreEqual(expectedSchemaHashes.Length, manifest.Messages.Count);
            for (int i = 0; i < expectedSchemaHashes.Length; i++)
            {
                Assert.AreEqual(
                    expectedSchemaHashes[i],
                    ComputeFnv1a64(canonicalSchemaLiterals[i]),
                    canonicalSchemaLiterals[i]);
                Assert.AreEqual(expectedSchemaHashes[i], manifest.Messages[i].SchemaHash);
                Assert.AreEqual(canonicalSchemaLiterals[i], manifest.Messages[i].ContractId);
            }
        }

        [Test]
        public void DamageWireSchemaFingerprint_IsFrozenFromLayoutAndResultCodeDescriptors()
        {
            string[] canonicalWireSchemas =
            {
                "DamageRequestMessage:v1|Sequence:u32le@0|InstigatorActorId:i32le@4|" +
                "TargetActorId:i32le@8|WeaponOrAbilityId:i32le@12|DamageEventType:u8@16|" +
                "RequestedDamage:f32le@17|ShotOrigin:f32le[3]@21|HitLocation:f32le[3]@33|" +
                "ClientTimeSeconds:f32le@45|size:49",
                "DamageResultMessage:v1|RequestSequence:u32le@0|InstigatorActorId:i32le@4|" +
                "TargetActorId:i32le@8|AppliedDamage:f32le@12|ResultCode:u8@16|" +
                "DamageEventType:u8@17|HitLocation:f32le[3]@18|size:30",
                "ServerDamageRejectReason:u8|Unknown=0|Accepted=1|InvalidPayload=2|" +
                "OwnershipMismatch=3|TargetNotDamageable=4|OutOfRange=5|OnCooldown=6|" +
                "TargetNotFound=7|Custom=8"
            };

            ulong fingerprint = ComputeWireSchemaFingerprint(canonicalWireSchemas);

            Assert.AreEqual(0x303A17781A25FAD4UL, fingerprint);
            Assert.AreEqual(fingerprint, GameplayFrameworkNetworkProtocol.DamageWireSchemaFingerprint);
            Assert.AreEqual(
                "303A17781A25FAD4",
                GameplayFrameworkNetworkProtocol.DefaultManifest.Metadata["damageWireSchemaFingerprint"]);
        }

        private static ulong ComputeFnv1a64(string canonicalLiteral)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            for (int i = 0; i < canonicalLiteral.Length; i++)
            {
                char character = canonicalLiteral[i];
                if (character > 0x7F)
                {
                    throw new AssertionException("Canonical schema literals must contain ASCII characters only.");
                }

                hash ^= (byte)character;
                hash = unchecked(hash * prime);
            }

            return hash;
        }

        private static ulong ComputeWireSchemaFingerprint(string[] canonicalSchemas)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            for (int schemaIndex = 0; schemaIndex < canonicalSchemas.Length; schemaIndex++)
            {
                string schema = canonicalSchemas[schemaIndex];
                for (int i = 0; i < schema.Length; i++)
                {
                    char character = schema[i];
                    if (character > 0x7F)
                    {
                        throw new AssertionException("Canonical wire schema descriptors must contain ASCII characters only.");
                    }

                    hash ^= (byte)character;
                    hash = unchecked(hash * prime);
                }

                if (schemaIndex + 1 < canonicalSchemas.Length)
                {
                    hash ^= 0xFF;
                    hash = unchecked(hash * prime);
                }
            }

            return hash;
        }

        [Test]
        public void Protocol_TryRegisterMessageCatalog_ReturnsFalseForMissingRuntimeContext()
        {
            Assert.IsFalse(GameplayFrameworkNetworkProtocol.TryRegisterMessageCatalog(null));
        }

        [Test]
        public void SessionAdmission_RequiresAndValidatesStagedConnectionByDefault()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var request = new PlayerLoginRequest(
                playerId: 42,
                playerName: "RemotePlayer",
                remoteAddress: "127.0.0.1");

            Assert.IsFalse(session.ApproveLogin(in request, out _));

            var connection = new TestConnection(7, "127.0.0.1");
            Assert.IsTrue(session.TryStageConnection(42, connection, out string stageError), stageError);
            Assert.IsTrue(session.ApproveLogin(in request, out string approvalError), approvalError);
            Assert.AreEqual(1, session.StagedConnectionCount);
            Assert.IsTrue(session.RemoveStagedConnection(42, connection));
            Assert.AreEqual(0, session.StagedConnectionCount);
        }

        [Test]
        public void SessionAdmission_RejectsDisconnectedOrUnauthenticatedStagedConnection()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var connection = new TestConnection(8, "10.0.0.8")
            {
                IsConnectedValue = false,
                IsAuthenticatedValue = false,
            };
            var request = new PlayerLoginRequest(8, "RemotePlayer", remoteAddress: "10.0.0.8");

            Assert.IsTrue(session.TryStageConnection(8, connection, out _));
            Assert.IsFalse(session.ApproveLogin(in request, out _));

            connection.IsConnectedValue = true;
            Assert.IsFalse(session.ApproveLogin(in request, out _));

            connection.IsAuthenticatedValue = true;
            Assert.IsTrue(session.ApproveLogin(in request, out string approvalError), approvalError);
        }

        [Test]
        public void SessionAdmission_RejectsConnectionReuseAndBanAppliedAfterStaging()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var connection = new TestConnection(9, "10.0.0.9");
            var sameConnectionIdWrapper = new TestConnection(9, "10.0.0.9");

            Assert.IsTrue(session.TryStageConnection(9, connection, out _));
            Assert.IsFalse(session.TryStageConnection(10, sameConnectionIdWrapper, out _));
            Assert.IsTrue(session.BanAddress("10.0.0.9"));

            var requestWithoutAddress = new PlayerLoginRequest(9, "RemotePlayer");
            Assert.IsFalse(session.ApproveLogin(in requestWithoutAddress, out _));
        }

        [Test]
        public void SessionAdmission_LocalLoginCannotConsumeStagedRemoteIdentity()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var connection = new TestConnection(10, "10.0.0.10");
            var localRequest = new PlayerLoginRequest(
                playerId: 10,
                playerName: "LocalPlayer",
                isLocal: true);

            Assert.IsTrue(session.TryStageConnection(10, connection, out string stageError), stageError);
            Assert.IsFalse(session.ApproveLogin(in localRequest, out _));
            Assert.AreEqual(1, session.StagedConnectionCount);
        }

        [Test]
        public void SessionAdmission_MessageEndpointCannotChangeWithStagedConnections()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var firstEndpoint = new TestMessageEndpoint();
            session.SetMessageEndpoint(firstEndpoint);
            Assert.IsTrue(session.TryStageConnection(
                11,
                new TestConnection(11, "10.0.0.11"),
                out string stageError), stageError);

            Assert.DoesNotThrow(() => session.SetMessageEndpoint(firstEndpoint));
            Assert.Throws<System.InvalidOperationException>(() =>
                session.SetMessageEndpoint(new TestMessageEndpoint()));
            Assert.AreSame(firstEndpoint, session.MessageEndpoint);
        }

        private static NetworkedGameplayActor CreateActor(int ownerConnectionId, int teamId = 0)
        {
            return new NetworkedGameplayActor(
                null,
                1,
                ownerConnectionId,
                0UL,
                teamId,
                uint.MaxValue,
                false,
                NetworkVector3.Zero);
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId, string remoteAddress = "")
            {
                ConnectionId = connectionId;
                RemoteAddress = remoteAddress;
            }

            public int ConnectionId { get; }
            public string RemoteAddress { get; }
            public bool IsConnectedValue { get; set; } = true;
            public bool IsAuthenticatedValue { get; set; } = true;
            public bool IsConnected => IsConnectedValue;
            public bool IsAuthenticated => IsAuthenticatedValue;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Good;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }

            public bool Equals(INetConnection other)
            {
                return other != null && other.ConnectionId == ConnectionId;
            }
        }

        private sealed class TestMessageEndpoint : INetworkMessageEndpoint
        {
            private readonly NetworkMessageHandlerRegistry handlers = new NetworkMessageHandlerRegistry();

            public INetTransport Transport => null;
            public bool IsAcceptingMessages => true;

            public int GetMaxPayloadSize(ushort messageId, NetworkChannel channel) =>
                NetworkConstants.DefaultMaxPayloadSize;

            public NetworkMessageHandlerLease RegisterHandler(
                ushort messageId,
                NetworkMessageHandler handler) => handlers.Register(messageId, handler);

            public NetworkSendResult SendToServer(
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) => default;

            public NetworkSendResult SendToClient(
                INetConnection connection,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) => default;

            public NetworkSendResult BroadcastToClients(
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) => default;

            public NetworkSendResult Broadcast(
                IReadOnlyList<INetConnection> connections,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) => default;

            public void Disconnect(INetConnection connection) { }
        }
    }
}
