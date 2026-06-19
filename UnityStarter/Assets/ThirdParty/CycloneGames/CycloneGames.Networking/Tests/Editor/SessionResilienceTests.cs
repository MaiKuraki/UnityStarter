using System.Collections.Generic;
using CycloneGames.Networking.Session;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class SessionResilienceTests
    {
        [Test]
        public void SessionDirectory_Search_FiltersAndRanksCompatibleRooms()
        {
            var directory = new NetworkSessionDirectory();
            directory.Upsert(CreateSession("wrong-build", "roguelike", "arena", "na", "old", 2, 8, 10));
            directory.Upsert(CreateSession("full", "roguelike", "arena", "na", "1.0", 8, 8, 5));
            directory.Upsert(CreateSession("private", "roguelike", "arena", "na", "1.0", 2, 8, 1, isPrivate: true));
            directory.Upsert(CreateSession("steam-slower", "roguelike", "arena", "na", "1.0", 2, 8, 80));
            directory.Upsert(CreateSession("lan-fast", "roguelike", "arena", "na", "1.0", 2, 8, 12));

            var criteria = new NetworkSessionSearchCriteria
            {
                GameMode = "roguelike",
                Map = "arena",
                Region = "na",
                BuildId = "1.0",
                RequireHostMigration = true,
                RequireReconnection = true,
                RequiredConnectivity = NetworkSessionConnectivity.Direct,
                MaxResults = 8
            };
            var results = new List<NetworkSessionDescriptor>(8);

            int count = directory.Search(criteria, results);

            Assert.AreEqual(2, count);
            Assert.AreEqual("lan-fast", results[0].SessionId);
            Assert.AreEqual("steam-slower", results[1].SessionId);
        }

        [Test]
        public void MatchmakingCoordinator_BuildPlan_JoinsBestSession()
        {
            var directory = new NetworkSessionDirectory();
            directory.Upsert(CreateSession("room-a", "roguelike", "arena", "na", "1.0", 2, 8, 90));
            directory.Upsert(CreateSession("room-b", "roguelike", "arena", "na", "1.0", 2, 8, 20));
            var coordinator = new NetworkMatchmakingCoordinator();

            NetworkMatchmakingPlan plan = coordinator.BuildPlan(
                directory,
                new NetworkSessionSearchCriteria
                {
                    GameMode = "roguelike",
                    BuildId = "1.0"
                },
                NetworkMatchmakingOptions.Default);

            Assert.AreEqual(NetworkMatchmakingPlanAction.JoinSession, plan.Action);
            Assert.AreEqual("room-b", plan.SelectedSession.SessionId);
            Assert.AreEqual(NetworkMatchmakingPlanReason.CompatibleSessionFound, plan.Reason);
        }

        [Test]
        public void MatchmakingCoordinator_BuildPlan_CreatesWhenNoCompatibleSession()
        {
            var coordinator = new NetworkMatchmakingCoordinator();
            var directory = new NetworkSessionDirectory();

            NetworkMatchmakingPlan plan = coordinator.BuildPlan(
                directory,
                new NetworkSessionSearchCriteria { GameMode = "roguelike" },
                new NetworkMatchmakingOptions(allowCreateSession: true, allowQueue: false));

            Assert.AreEqual(NetworkMatchmakingPlanAction.CreateSession, plan.Action);
            Assert.AreEqual(NetworkMatchmakingPlanReason.NoCompatibleSessionCreateAllowed, plan.Reason);
        }

        [Test]
        public void HostMigrationCoordinator_HostDisconnected_SelectsHighestRankCandidate()
        {
            var coordinator = new HostMigrationCoordinator("session");
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 1,
                playerId: 100UL,
                isConnected: false,
                canHost: true,
                authorityRank: 100,
                lastConfirmedTick: new NetworkTickId(90)));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 2,
                playerId: 200UL,
                isConnected: true,
                canHost: true,
                authorityRank: 5,
                pingMs: 10,
                lastConfirmedTick: new NetworkTickId(100)));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 3,
                playerId: 300UL,
                isConnected: true,
                canHost: true,
                authorityRank: 10,
                pingMs: 120,
                lastConfirmedTick: new NetworkTickId(100)));
            coordinator.SetCurrentHost(1, 100UL);

            bool started = coordinator.TryBeginMigration(
                HostMigrationReason.HostDisconnected,
                new NetworkTickId(101),
                out NetworkAuthorityTransferPlan plan);

            Assert.IsTrue(started);
            Assert.AreEqual(3, plan.ToHostConnectionId);
            Assert.AreEqual(1, plan.Generation);
            Assert.AreEqual(NetworkAuthorityTransferScope.All, plan.TransferScopes);

            Assert.IsTrue(coordinator.CompleteMigration(3, new NetworkTickId(102)));
            Assert.AreEqual(3, coordinator.CurrentHostConnectionId);
            Assert.AreEqual(1, coordinator.Generation);
            Assert.AreEqual(HostMigrationState.Stable, coordinator.State);
        }

        [Test]
        public void HostMigrationCoordinator_RankTie_PrefersDedicatedCandidate()
        {
            var coordinator = new HostMigrationCoordinator("session");
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 1,
                playerId: 100UL,
                isConnected: false,
                canHost: true,
                authorityRank: 100));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 2,
                playerId: 200UL,
                isConnected: true,
                canHost: true,
                authorityRank: 10,
                kind: NetworkHostCandidateKind.PlayerListenServer,
                hardwareScore: 100));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 10,
                playerId: 0UL,
                isConnected: true,
                canHost: true,
                authorityRank: 10,
                kind: NetworkHostCandidateKind.DedicatedServer,
                hardwareScore: 10));
            coordinator.SetCurrentHost(1, 100UL);

            Assert.IsTrue(coordinator.TryBeginMigration(
                HostMigrationReason.HostDisconnected,
                new NetworkTickId(20),
                out NetworkAuthorityTransferPlan plan));

            Assert.AreEqual(10, plan.ToHostConnectionId);
            Assert.AreEqual(NetworkHostCandidateKind.DedicatedServer, plan.ToHostKind);
        }

        [Test]
        public void HostMigrationCoordinator_NoEligibleCandidate_FailsInsteadOfHanging()
        {
            var coordinator = new HostMigrationCoordinator("session");
            string failedReason = null;
            coordinator.OnHostMigrationFailed += reason => failedReason = reason;
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 1,
                playerId: 100UL,
                isConnected: false,
                canHost: true));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 2,
                playerId: 200UL,
                isConnected: true,
                canHost: false));
            coordinator.SetCurrentHost(1, 100UL);

            bool started = coordinator.TryBeginMigration(
                HostMigrationReason.HostDisconnected,
                new NetworkTickId(20),
                out NetworkAuthorityTransferPlan plan);

            Assert.IsFalse(started);
            Assert.IsFalse(plan.IsValid);
            Assert.AreEqual(HostMigrationState.Failed, coordinator.State);
            Assert.IsNotEmpty(failedReason);
        }

        [Test]
        public void HostMigrationCoordinator_Update_DetectsHostTimeout()
        {
            var coordinator = new HostMigrationCoordinator(
                "session",
                options: new HostMigrationOptions(
                    maxHostSilenceSeconds: 2d,
                    transferScopes: NetworkAuthorityTransferScope.SessionOwner | NetworkAuthorityTransferScope.MatchState,
                    allowCurrentHostAsCandidate: false));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 1,
                playerId: 100UL,
                isConnected: true,
                canHost: true,
                lastSeenTime: 10d,
                lastConfirmedTick: new NetworkTickId(50)));
            coordinator.UpsertParticipant(new NetworkHostParticipant(
                connectionId: 2,
                playerId: 200UL,
                isConnected: true,
                canHost: true,
                lastSeenTime: 12d,
                lastConfirmedTick: new NetworkTickId(51)));
            coordinator.SetCurrentHost(1, 100UL);

            bool started = coordinator.Update(12.5d, new NetworkTickId(52), out NetworkAuthorityTransferPlan plan);

            Assert.IsTrue(started);
            Assert.AreEqual(2, plan.ToHostConnectionId);
            Assert.AreEqual(NetworkAuthorityTransferScope.SessionOwner | NetworkAuthorityTransferScope.MatchState, plan.TransferScopes);
        }

        private static NetworkSessionDescriptor CreateSession(
            string id,
            string gameMode,
            string map,
            string region,
            string buildId,
            int currentPlayers,
            int maxPlayers,
            int pingMs,
            bool isPrivate = false)
        {
            return new NetworkSessionDescriptor(
                id,
                id,
                gameMode,
                currentPlayers,
                maxPlayers,
                map,
                region,
                buildId,
                hostAddress: "127.0.0.1",
                port: 7777,
                pingMs: pingMs,
                isPrivate: isPrivate,
                supportsHostMigration: true,
                supportsReconnection: true,
                connectivity: NetworkSessionConnectivity.Direct | NetworkSessionConnectivity.Lan | NetworkSessionConnectivity.PlatformLobby,
                source: NetworkSessionDiscoverySource.Lan);
        }
    }
}
