using CycloneGames.GameplayFramework.Core;
using CycloneGames.Networking;
using CycloneGames.Networking.Buffers;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class ActorMigrationCodecTests
    {
        [SetUp]
        public void SetUp()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 8, clearBuffersOnReturn: false);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkBufferPool.Clear();
        }

        [Test]
        public void RoundTripPreservesEveryWireField()
        {
            var state = CreateState(
                new NetworkVector3(1f, 2f, 3f),
                new NetworkQuaternion(0f, 0f, 0f, 1f),
                new NetworkVector3(2f, 3f, 4f),
                new[] { "Player", "Authority" });

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteMigrationState(in state);
            buffer.FlipForRead();
            ActorMigrationState result = buffer.ReadMigrationState();

            Assert.AreEqual(state.Position, result.Position);
            Assert.AreEqual(state.Rotation, result.Rotation);
            Assert.AreEqual(state.Scale, result.Scale);
            Assert.AreEqual(state.PrefabDefinitionId, result.PrefabDefinitionId);
            Assert.AreEqual(state.RemainingLifeSpan, result.RemainingLifeSpan);
            Assert.AreEqual(state.CanBeDamaged, result.CanBeDamaged);
            Assert.AreEqual(state.Hidden, result.Hidden);
            Assert.AreEqual(state.HasBegunPlay, result.HasBegunPlay);
            Assert.AreEqual(state.OwnerConnectionId, result.OwnerConnectionId);
            Assert.AreEqual(state.InstigatorActorId, result.InstigatorActorId);
            Assert.AreEqual(state.ActorName, result.ActorName);
            Assert.AreEqual(state.TagCount, result.TagCount);
            for (int i = 0; i < state.TagCount; i++)
            {
                Assert.AreEqual(state.GetTag(i), result.GetTag(i));
            }
        }

        [Test]
        public void WriteMatchesFrozenLittleEndianFieldOrder()
        {
            var state = new ActorMigrationState(
                new NetworkVector3(1f, 2f, 3f),
                new NetworkQuaternion(0f, 0f, 0f, 1f),
                new NetworkVector3(8f, 9f, 10f),
                "P",
                11f,
                canBeDamaged: true,
                hidden: false,
                new[] { "T" },
                ownerConnectionId: 0x01020304,
                instigatorActorId: 0x11121314,
                actorName: "N",
                hasBegunPlay: true);
            byte[] expected =
            {
                0x00, 0x00, 0x80, 0x3F,
                0x00, 0x00, 0x00, 0x40,
                0x00, 0x00, 0x40, 0x40,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x80, 0x3F,
                0x00, 0x00, 0x00, 0x41,
                0x00, 0x00, 0x10, 0x41,
                0x00, 0x00, 0x20, 0x41,
                0x01, 0x00, 0x50,
                0x00, 0x00, 0x30, 0x41,
                0x01, 0x00, 0x01,
                0x01, 0x00,
                0x01, 0x00, 0x54,
                0x04, 0x03, 0x02, 0x01,
                0x14, 0x13, 0x12, 0x11,
                0x01, 0x00, 0x4E,
            };

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteMigrationState(in state);
            System.ArraySegment<byte> segment = buffer.ToArraySegment();
            var actual = new byte[segment.Count];
            System.Buffer.BlockCopy(segment.Array, segment.Offset, actual, 0, segment.Count);

            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void ConstructorRejectsNonFiniteTransformAndLifeSpan()
        {
            Assert.Throws<System.InvalidOperationException>(() => CreateState(
                new NetworkVector3(float.NaN, 0f, 0f),
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                null));
            Assert.Throws<System.InvalidOperationException>(() => CreateState(
                NetworkVector3.Zero,
                new NetworkQuaternion(0f, float.PositiveInfinity, 0f, 1f),
                NetworkVector3.One,
                null));
            Assert.Throws<System.InvalidOperationException>(() => new ActorMigrationState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                "actor/player",
                float.NaN,
                true,
                false,
                System.ReadOnlySpan<string>.Empty,
                1,
                2,
                "Player",
                false));
        }

        [Test]
        public void CodecEnforcesCoreActorTagBudget()
        {
            var tags = new string[ActorTagLimits.MaximumTagCount + 1];
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i] = "T" + i;
            }

            Assert.Throws<System.ArgumentOutOfRangeException>(() => CreateState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                tags));
        }

        [Test]
        public void StateOwnsAnImmutableTagSnapshot()
        {
            string[] source = { "Player" };
            ActorMigrationState state = CreateState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                source);

            source[0] = "Mutated";

            Assert.AreEqual("Player", state.GetTag(0));
            Assert.AreEqual(1, state.Tags.Length);
        }

        [Test]
        public void ConstructorRejectsMissingDefinitionDegenerateRotationAndDuplicateTags()
        {
            Assert.Throws<System.InvalidOperationException>(() => new ActorMigrationState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                " ",
                0f,
                true,
                false,
                System.ReadOnlySpan<string>.Empty,
                0,
                0,
                null,
                false));
            Assert.Throws<System.InvalidOperationException>(() => new ActorMigrationState(
                NetworkVector3.Zero,
                new NetworkQuaternion(0f, 0f, 0f, 0f),
                NetworkVector3.One,
                "actor/player",
                0f,
                true,
                false,
                System.ReadOnlySpan<string>.Empty,
                0,
                0,
                null,
                false));
            Assert.Throws<System.InvalidOperationException>(() => new ActorMigrationState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                "actor/player",
                0f,
                true,
                false,
                new[] { "Player", "Player" },
                0,
                0,
                null,
                false));
        }

        [Test]
        public void ConstructorRejectsInvalidUnicodeIdentity()
        {
            Assert.Throws<System.ArgumentException>(() => new ActorMigrationState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                "actor/\uD800",
                0f,
                true,
                false,
                System.ReadOnlySpan<string>.Empty,
                0,
                0,
                null,
                false));
        }

        [Test]
        public void ReadLimitCannotExceedCoreActorTagBudget()
        {
            var tags = new string[ActorTagLimits.MaximumTagCount];
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i] = "T" + i;
            }

            ActorMigrationState state = CreateState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                tags);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteMigrationState(in state);
            buffer.FlipForRead();
            ActorMigrationState result = buffer.ReadMigrationState(maxRuntimeTagCount: 1024);

            Assert.AreEqual(ActorTagLimits.MaximumTagCount, result.TagCount);
        }

        [Test]
        public void MaximumLegalStateMatchesCodecAndProtocolPayloadBudget()
        {
            string[] tags = new string[ActorTagLimits.MaximumTagCount];
            string tagPrefix = new string('\u4E00', ActorTagLimits.MaximumTagLength - 1);
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i] = tagPrefix + (char)('\u4E01' + i);
            }

            var state = new ActorMigrationState(
                NetworkVector3.Zero,
                NetworkQuaternion.Identity,
                NetworkVector3.One,
                new string('P', ActorMigrationNetworkingExtensions.MaxPrefabDefinitionIdUtf8Bytes),
                0f,
                true,
                false,
                tags,
                1,
                2,
                new string('N', ActorMigrationNetworkingExtensions.MaxActorNameUtf8Bytes),
                false);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteMigrationState(in state);
            System.ArraySegment<byte> segment = buffer.ToArraySegment();
            NetworkProtocolManifest manifest = GameplayFrameworkNetworkProtocol.CreateProtocolManifest();
            NetworkMessageDescriptor descriptor = manifest.Messages[0];

            Assert.AreEqual(26045, ActorMigrationNetworkingExtensions.MaximumEncodedSize);
            Assert.AreEqual(ActorMigrationNetworkingExtensions.MaximumEncodedSize, segment.Count);
            Assert.AreEqual(ActorMigrationNetworkingExtensions.MaximumEncodedSize, descriptor.MaxPayloadSize);
            Assert.LessOrEqual(
                descriptor.MaxPayloadSize,
                NetworkConstants.MaxMTU - CycloneGames.Networking.Security.NetworkWireProtocol.HeaderLength);
        }

        private static ActorMigrationState CreateState(
            NetworkVector3 position,
            NetworkQuaternion rotation,
            NetworkVector3 scale,
            string[] tags)
        {
            return new ActorMigrationState(
                position,
                rotation,
                scale,
                "actor/player",
                5f,
                true,
                false,
                tags,
                12,
                13,
                "PlayerActor",
                true);
        }
    }
}
