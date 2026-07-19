using CycloneGames.Networking.Session;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ProfileCapabilityManifestTests
    {
        [Test]
        public void RuntimeProfile_ProjectProfile_OverridesDefaultsWithoutChangingConstants()
        {
            NetworkRuntimeProfile profile = NetworkRuntimeProfiles.CreateDefaultBuilder()
                .SetInt("mygame.max_zone_players", 10000)
                .SetFloat("mygame.snapshot_jitter_buffer_seconds", 0.15f)
                .SetString("mygame.deployment", "regional-shard")
                .Build();

            Assert.AreEqual(NetworkConstants.DefaultMaxConnections, NetworkRuntimeProfiles.Default.MaxConnections);
            Assert.AreEqual(10000, profile.IntSettings["mygame.max_zone_players"]);
            Assert.IsTrue(profile.TryGetFloat("mygame.snapshot_jitter_buffer_seconds", out float jitter));
            Assert.AreEqual(0.15f, jitter);
            Assert.IsTrue(profile.TryGetString("mygame.deployment", out string deployment));
            Assert.AreEqual("regional-shard", deployment);
        }

        [Test]
        public void RuntimeProfileRegistry_ResolvesProjectProfilesById()
        {
            NetworkRuntimeProfile projectProfile = new NetworkRuntimeProfileBuilder
            {
                ProfileId = "project.mmo",
                DisplayName = "Project MMO",
                MaxConnections = 10000,
                TickRate = 30,
                SendRate = 10,
                Mtu = NetworkConstants.DefaultMTU,
                MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                BufferSize = NetworkConstants.DefaultBufferSize,
                PoolSize = NetworkConstants.DefaultPoolSize,
                SnapshotBufferSize = 512,
                SessionSearchMaxResults = 500
            }.SetString("mode", "mmo").Build();

            var registry = new NetworkRuntimeProfileRegistry();
            registry.Register(projectProfile);

            Assert.IsTrue(registry.TryGet("project.mmo", out NetworkRuntimeProfile resolved));
            Assert.AreEqual(10000, resolved.MaxConnections);
            Assert.AreEqual(512, resolved.SnapshotBufferSize);
            Assert.AreEqual(NetworkConstants.DefaultMaxConnections, registry.GetOrDefault("missing").MaxConnections);
        }

        [Test]
        public void NodeCapabilities_ProjectCapability_MatchesWithoutEnumChange()
        {
            var shardLease = new NetworkCapabilityId("mygame.zone.lease");
            NetworkNodeCapabilities capabilities = new NetworkNodeCapabilitiesBuilder
            {
                NodeId = "zone-001",
                RuntimeId = NetworkRuntimeId.FromAsciiCode("Shard"),
                RuntimeName = "ProjectShard",
                Region = "asia",
                Platform = "linux",
                MaxConnections = 10000,
                MaxPayloadBytes = 1200,
                CpuScore = 900,
                MemoryScore = 512
            }
                .Add(NetworkCapabilityIds.DedicatedServer)
                .Add(NetworkCapabilityIds.Sharding)
                .Add(shardLease, level: 2, score: 50d)
                .Build();

            var query = new NetworkCapabilityQuery
            {
                MinimumConnections = 1000,
                Region = "asia",
                Platform = "linux"
            }
                .Require(NetworkCapabilityIds.DedicatedServer)
                .Require(shardLease, minimumLevel: 2)
                .Prefer(NetworkCapabilityIds.Sharding);

            Assert.IsTrue(NetworkNodeCapabilityMatcher.TryScore(capabilities, query, out double score));
            Assert.Greater(score, 0d);
            Assert.IsTrue(capabilities.Supports(shardLease, 2));
        }

        [Test]
        public void ProtocolManifest_RegistersProjectUserRangeAndMessages()
        {
            NetworkProtocolManifest manifest = new NetworkProtocolManifestBuilder(
                "Project.Gameplay",
                minMessageId: 30000,
                maxMessageId: 30010)
            {
                ProtocolId = "project.gameplay",
                CurrentVersion = 3,
                MinimumSupportedVersion = 2
            }
                .AddMessage("TestProjectileMessage:v1", 30000, 0xF3D3AFA95EFF61ECUL, NetworkChannel.UnreliableSequenced, 96)
                .AddMessage("TestInventoryMessage:v1", 30001, 0x9CA010B33A030EEDUL, NetworkChannel.Reliable, 256)
                .Build();

            var catalog = new NetworkMessageCatalog();

            Assert.IsTrue(catalog.TryRegisterProtocolManifest(manifest));
            Assert.IsTrue(catalog.TryRegisterProtocolManifest(manifest));
            Assert.IsTrue(catalog.TryGet(30000, out NetworkMessageDescriptor descriptor));
            Assert.AreEqual("Project.Gameplay", descriptor.Owner);
            Assert.AreEqual(2, catalog.MessageCount);
            Assert.AreEqual(1, catalog.ManifestCount);
            Assert.IsTrue(manifest.IsCompatibleWith(2));
            Assert.IsFalse(manifest.IsCompatibleWith(1));
        }

        [Test]
        public void ProtocolManifest_RejectsMessageOutsideManifestRange()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new NetworkProtocolManifestBuilder(
                    "Project.Bad",
                    minMessageId: 30000,
                    maxMessageId: 30001)
                    .AddMessage("TestProjectileMessage:v1", 30002, 0xF3D3AFA95EFF61ECUL)
                    .Build());
        }

        [Test]
        public void ProtocolManifest_DetectsCatalogConflicts()
        {
            NetworkProtocolManifest first = new NetworkProtocolManifestBuilder(
                "Project.First",
                minMessageId: 30000,
                maxMessageId: 30001)
                .AddMessage("TestProjectileMessage:v1", 30000, 0xF3D3AFA95EFF61ECUL)
                .Build();
            NetworkProtocolManifest second = new NetworkProtocolManifestBuilder(
                "Project.Second",
                minMessageId: 30000,
                maxMessageId: 30001)
                .AddMessage("TestInventoryMessage:v1", 30000, 0x9CA010B33A030EEDUL)
                .Build();
            var catalog = new NetworkMessageCatalog();

            Assert.IsTrue(catalog.TryRegisterProtocolManifest(first));
            Assert.IsFalse(catalog.TryRegisterProtocolManifest(second));
        }

        private struct TestProjectileMessage { }

        private struct TestInventoryMessage { }
    }
}
