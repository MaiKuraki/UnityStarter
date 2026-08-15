using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class ActorLifetimeOwnershipTests
    {
        [Test]
        public void OwnedDestroy_ReleasesTheCreatedActorExactlyOnce()
        {
            var lifetime = new RecordingActorLifetime();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(actorLifetime: lifetime);
            TestActor prefab = testWorld.CreateAuthoringActor<TestActor>("OwnedActorPrefab");
            TestActor actor = testWorld.World.SpawnActor(prefab);

            Assert.That(testWorld.World.DestroyActor(actor), Is.True);

            Assert.That(lifetime.GetReleaseCount(actor), Is.EqualTo(1));
            Assert.That(lifetime.DuplicateReleaseCount, Is.Zero);
        }

        [Test]
        public void Shutdown_ReleasesEveryCreatedActorExactlyOnce()
        {
            var lifetime = new RecordingActorLifetime();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 1,
                actorLifetime: lifetime);
            Assert.That(lifetime.CreatedCount, Is.GreaterThan(0));

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.That(lifetime.ReleasedCount, Is.EqualTo(lifetime.CreatedCount));
            Assert.That(lifetime.DuplicateReleaseCount, Is.Zero);
            Assert.That(lifetime.EveryCreatedActorWasReleasedExactlyOnce(), Is.True);
        }

        [Test]
        public void FailedAdmission_ReleasesTheCreatedActorExactlyOnce()
        {
            const int maximumActorCount = 32;
            var lifetime = new RecordingActorLifetime();
            var limits = new WorldRuntimeLimits(
                maximumActorCount: maximumActorCount,
                initialActorCapacity: maximumActorCount);
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                actorLifetime: lifetime,
                runtimeLimits: limits);

            while (testWorld.World.ActorCount < maximumActorCount - 1)
            {
                TestActor registered = testWorld.CreateAuthoringActor<TestActor>(
                    $"RegisteredActor{testWorld.World.ActorCount}");
                testWorld.World.RegisterActor(registered);
            }

            TestActor capacityFiller = testWorld.CreateAuthoringActor<TestActor>("CapacityFiller");
            TestActor prefab = testWorld.CreateAuthoringActor<TestActor>("RejectedSpawnPrefab");
            int createdBefore = lifetime.CreatedCount;
            int releasedBefore = lifetime.ReleasedCount;
            lifetime.AfterNextCreate = () => testWorld.World.RegisterActor(capacityFiller);

            bool spawned = testWorld.World.TrySpawnActor(prefab, out TestActor actor);

            Assert.That(spawned, Is.False);
            Assert.That(actor, Is.Null);
            Assert.That(testWorld.World.ActorCount, Is.EqualTo(maximumActorCount));
            Assert.That(lifetime.CreatedCount, Is.EqualTo(createdBefore + 1));
            Assert.That(lifetime.ReleasedCount, Is.EqualTo(releasedBefore + 1));
            Assert.That(lifetime.GetReleaseCount(lifetime.LastCreated), Is.EqualTo(1));
            Assert.That(lifetime.DuplicateReleaseCount, Is.Zero);
        }

        [Test]
        public void NonOwnedExplicitDestroy_DoesNotUseTheInjectedLifetime()
        {
            var lifetime = new RecordingActorLifetime();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(actorLifetime: lifetime);
            TestActor sceneActor = testWorld.CreateAuthoringActor<TestActor>("SceneActor");
            testWorld.World.RegisterActor(sceneActor);
            int releaseCountBefore = lifetime.ReleasedCount;

            Assert.That(testWorld.World.DestroyActor(sceneActor), Is.True);

            Assert.That(lifetime.ReleasedCount, Is.EqualTo(releaseCountBefore));
            Assert.That(sceneActor == null, Is.True);
        }

        [Test]
        public void DestroyCallbackOwnedActor_NotifiesTheInjectedLifetimeExactlyOnce()
        {
            var lifetime = new RecordingActorLifetime();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(actorLifetime: lifetime);
            TestActor prefab = testWorld.CreateAuthoringActor<TestActor>("ExternallyDestroyedPrefab");
            TestActor actor = testWorld.World.SpawnActor(prefab);

            actor.NotifyDestroyForTest();

            Assert.That(lifetime.GetReleaseCount(actor), Is.EqualTo(1));
            Assert.That(lifetime.DuplicateReleaseCount, Is.Zero);
            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();
            Assert.That(lifetime.GetReleaseCount(actor), Is.EqualTo(1));
        }

        [Test]
        public void RegistrationFailure_RollsBackRegistryAndReleasesExactlyOnce()
        {
            var lifetime = new RecordingActorLifetime();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(actorLifetime: lifetime);
            ThrowingBeginPlayActor prefab =
                testWorld.CreateAuthoringActor<ThrowingBeginPlayActor>("ThrowingActorPrefab");
            int actorCountBefore = testWorld.World.ActorCount;
            int ownedActorCountBefore = testWorld.World.OwnedActorCount;

            Assert.Throws<InvalidOperationException>(() => testWorld.World.SpawnActor(prefab));

            Assert.That(testWorld.World.ActorCount, Is.EqualTo(actorCountBefore));
            Assert.That(testWorld.World.OwnedActorCount, Is.EqualTo(ownedActorCountBefore));
            Assert.That(lifetime.GetReleaseCount(lifetime.LastCreated), Is.EqualTo(1));
            Assert.That(lifetime.DuplicateReleaseCount, Is.Zero);
        }

        [Test]
        public void NonOwnedRegistrationFailure_RollsBackWorldAndTickRegistriesWithoutRelease()
        {
            var lifetime = new RecordingActorLifetime();
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(actorLifetime: lifetime);
            ThrowingBeginPlayActor actor =
                testWorld.CreateAuthoringActor<ThrowingBeginPlayActor>("ThrowingSceneActor");
            actor.EnableUpdateTickForTest();
            int actorCountBefore = testWorld.World.ActorCount;
            int updateTickCountBefore = testWorld.World.GetTickActorCount(ActorTickPhase.Update);
            int releaseCountBefore = lifetime.ReleasedCount;

            Assert.Throws<InvalidOperationException>(() => testWorld.World.RegisterActor(actor));

            Assert.That(testWorld.World.ActorCount, Is.EqualTo(actorCountBefore));
            Assert.That(
                testWorld.World.GetTickActorCount(ActorTickPhase.Update),
                Is.EqualTo(updateTickCountBefore));
            Assert.That(testWorld.World.IsActorRegistered(actor), Is.False);
            Assert.That(actor.World, Is.Null);
            Assert.That(actor == null, Is.False);
            Assert.That(lifetime.ReleasedCount, Is.EqualTo(releaseCountBefore));
        }

        [Test]
        public void ThrowingLifetime_ShutdownContinuesAndReleasesEveryOwnedActorOnce()
        {
            var lifetime = new RecordingActorLifetime { ThrowAfterRelease = true };
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                localPlayerCount: 0,
                actorLifetime: lifetime);
            World world = testWorld.World;
            int createdBeforeShutdown = lifetime.CreatedCount;

            Assert.DoesNotThrow(() =>
                testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult());

            Assert.That(world.LifecycleState, Is.EqualTo(WorldLifecycleState.Disposed));
            Assert.That(lifetime.ReleasedCount, Is.EqualTo(createdBeforeShutdown));
            Assert.That(lifetime.DuplicateReleaseCount, Is.Zero);
            Assert.That(lifetime.EveryCreatedActorWasReleasedExactlyOnce(), Is.True);
        }

        private sealed class RecordingActorLifetime : IActorLifetime
        {
            private readonly UnityActorLifetime inner = new UnityActorLifetime();
            private readonly List<Actor> created = new List<Actor>(16);
            private readonly List<Actor> released = new List<Actor>(16);

            public Action AfterNextCreate { get; set; }
            public int CreatedCount => created.Count;
            public int ReleasedCount => released.Count;
            public int DuplicateReleaseCount { get; private set; }
            public Actor LastCreated => created.Count == 0 ? null : created[created.Count - 1];
            public bool ThrowAfterRelease { get; set; }

            public T Create<T>(T prefab) where T : Actor
            {
                T instance = inner.Create(prefab);
                created.Add(instance);
                Action callback = AfterNextCreate;
                AfterNextCreate = null;
                callback?.Invoke();
                return instance;
            }

            public void Release(Actor actor)
            {
                if (GetReleaseCount(actor) != 0)
                {
                    DuplicateReleaseCount++;
                }

                released.Add(actor);
                inner.Release(actor);
                if (ThrowAfterRelease)
                {
                    throw new InvalidOperationException("Release failure requested by the test.");
                }
            }

            public int GetReleaseCount(Actor actor)
            {
                int count = 0;
                for (int i = 0; i < released.Count; i++)
                {
                    if (ReferenceEquals(released[i], actor))
                    {
                        count++;
                    }
                }

                return count;
            }

            public bool EveryCreatedActorWasReleasedExactlyOnce()
            {
                for (int i = 0; i < created.Count; i++)
                {
                    if (GetReleaseCount(created[i]) != 1)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class TestActor : Actor
        {
            public void NotifyDestroyForTest()
            {
                base.OnDestroy();
            }
        }

        private sealed class ThrowingBeginPlayActor : Actor
        {
            public void EnableUpdateTickForTest()
            {
                ConfigureActorTick(ActorTickPhase.Update, startWithTickEnabled: true);
            }

            protected override void BeginPlay()
            {
                throw new InvalidOperationException("BeginPlay failure requested by the test.");
            }
        }
    }
}
