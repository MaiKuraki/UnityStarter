using System.Collections.Generic;
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
            Assert.IsFalse(NetworkMessageRanges.Rpc.Contains(descriptor.MessageId));
            Assert.IsTrue(catalog.TryGetRegisteredModuleRange(descriptor.MessageId, out NetworkMessageIdRange range));
            Assert.AreEqual(GameplayFrameworkNetworkProtocol.MessageOwner, range.Name);
            Assert.AreEqual(GameplayFrameworkNetworkProtocol.MessageOwner, descriptor.Owner);
            Assert.AreEqual(NetworkMessageKind.Module, descriptor.Kind);
            Assert.AreEqual(NetworkChannel.Reliable, descriptor.DefaultChannel);
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_IsIdempotentForSameDescriptor()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            // ActorMigrationState + DamageRequest + DamageResult are registered; re-registering is idempotent.
            Assert.AreEqual(3, catalog.Count);
        }

        [Test]
        public void Protocol_TryRegisterMessageCatalog_ReturnsFalseForMissingRuntimeContext()
        {
            Assert.IsFalse(GameplayFrameworkNetworkProtocol.TryRegisterMessageCatalog(null));
        }

        [Test]
        public void Protocol_RegisterMessage_RejectsConflictingDescriptor()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.Throws<System.InvalidOperationException>(() =>
                GameplayFrameworkNetworkProtocol.RegisterMessage<NetworkedGameplayActor>(
                    catalog,
                    GameplayFrameworkNetworkProtocol.MsgActorMigrationState));
        }

        [Test]
        public void Protocol_RegisterMessage_RejectsMessageIdOutsideGameplayFrameworkRange()
        {
            var catalog = new NetworkMessageCatalog();

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                GameplayFrameworkNetworkProtocol.RegisterMessage<NetworkedGameplayActor>(
                    catalog,
                    NetworkConstants.UserMsgIdMin));
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
            public TestConnection(int connectionId)
            {
                ConnectionId = connectionId;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => string.Empty;
            public bool IsConnected => true;
            public bool IsAuthenticated => true;
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
    }
}
