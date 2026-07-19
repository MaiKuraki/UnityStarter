using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking.Buffers;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class ActorMigrationNetworkingExtensionsTests
    {
        [SetUp]
        public void SetUp()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 32, clearBuffersOnReturn: false);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 32, clearBuffersOnReturn: false);
        }

        [Test]
        public void RoundTrip_Preserves_State()
        {
            var state = new ActorMigrationState(
                new Vector3(1f, 2f, 3f),
                Quaternion.Euler(10f, 20f, 30f),
                new Vector3(1f, 1f, 1f),
                "Prefabs/Hero",
                12.5f,
                canBeDamaged: true,
                hidden: false,
                tags: new[] { "Player", "Hero" },
                ownerConnectionId: 7,
                instigatorActorId: 13,
                actorName: "Hero_01",
                hasBegunPlay: true);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteMigrationState(state);
            buffer.FlipForRead();

            ActorMigrationState roundTripped = buffer.ReadMigrationState();

            Assert.AreEqual(state.PrefabAssetPath, roundTripped.PrefabAssetPath);
            Assert.AreEqual(state.ActorName, roundTripped.ActorName);
            Assert.AreEqual(state.OwnerConnectionId, roundTripped.OwnerConnectionId);
            Assert.AreEqual(state.InstigatorActorId, roundTripped.InstigatorActorId);
            Assert.AreEqual(2, roundTripped.Tags.Length);
            Assert.AreEqual(state.RemainingLifeSpan, roundTripped.RemainingLifeSpan);
        }

        [Test]
        public void Write_Rejects_NonFinite_Position()
        {
            var state = new ActorMigrationState(
                new Vector3(float.NaN, 0f, 0f),
                Quaternion.identity,
                Vector3.one,
                "Prefabs/Hero",
                0f,
                canBeDamaged: true,
                hidden: false,
                tags: null,
                ownerConnectionId: 1,
                instigatorActorId: 0,
                actorName: "A",
                hasBegunPlay: false);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteMigrationState(state));
        }

        [Test]
        public void Write_Rejects_NonFinite_LifeSpan()
        {
            var state = new ActorMigrationState(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                "Prefabs/Hero",
                float.PositiveInfinity,
                canBeDamaged: true,
                hidden: false,
                tags: null,
                ownerConnectionId: 1,
                instigatorActorId: 0,
                actorName: "A",
                hasBegunPlay: false);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteMigrationState(state));
        }

        [Test]
        public void Write_Rejects_Excessive_Runtime_Tag_Count()
        {
            var tags = new string[200];
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i] = "T";
            }

            var state = new ActorMigrationState(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                "Prefabs/Hero",
                0f,
                canBeDamaged: true,
                hidden: false,
                tags: tags,
                ownerConnectionId: 1,
                instigatorActorId: 0,
                actorName: "A",
                hasBegunPlay: false);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteMigrationState(state));
        }

        [Test]
        public void Read_CustomLimitCannotExceedActorRuntimeCapacity()
        {
            var tags = new string[Actor.MaxActorTags];
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i] = "T";
            }

            var state = new ActorMigrationState(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                "Prefabs/Hero",
                0f,
                canBeDamaged: true,
                hidden: false,
                tags: tags,
                ownerConnectionId: 1,
                instigatorActorId: 0,
                actorName: "A",
                hasBegunPlay: false);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteMigrationState(state);
            buffer.FlipForRead();

            ActorMigrationState roundTripped = buffer.ReadMigrationState(maxRuntimeTagCount: 256);

            Assert.AreEqual(Actor.MaxActorTags, roundTripped.Tags.Length);
        }

        [Test]
        public void CaptureAndApply_UseExplicitDefinitionIdAndRuntimeBounds()
        {
            var sourceObject = new GameObject("SourceActor");
            var targetObject = new GameObject("TargetActor");
            try
            {
                Actor source = sourceObject.AddComponent<Actor>();
                source.SetActorLocation(new Vector3(1f, 2f, 3f));
                source.SetActorScale(new Vector3(2f, 2f, 2f));
                source.AddTag("Player");
                ActorMigrationState state = source.CaptureMigrationState(
                    "characters/player",
                    ownerConnectionId: 7,
                    instigatorActorId: 9);

                Actor target = targetObject.AddComponent<Actor>();
                target.ApplyMigrationState(state);

                Assert.AreEqual("characters/player", state.PrefabAssetPath);
                Assert.AreEqual(source.GetActorLocation(), target.GetActorLocation());
                Assert.AreEqual(source.GetActorScale(), target.GetActorScale());
                Assert.IsTrue(target.ActorHasTag("Player"));
                Assert.AreEqual(7, state.OwnerConnectionId);
                Assert.AreEqual(9, state.InstigatorActorId);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }
    }
}
