using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class WorldActorSourceAndUnregistrationTests
    {
        [Test]
        public void DirectGameInstance_WithoutActorSource_DoesNotDiscoverSceneActors()
        {
            ProbeActor sceneActor = null;
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                configure: world =>
                    sceneActor = world.CreateAuthoringActor<ProbeActor>("UndiscoveredSceneActor"),
                discoverActiveSceneActors: false);

            Assert.IsFalse(testWorld.World.IsActorRegistered(sceneActor));
            Assert.IsNull(sceneActor.World);
            Assert.AreEqual(0, sceneActor.BeginPlayCount);
        }

        [Test]
        public void ActorSource_SkipsFakeNullAndDuplicateEntries()
        {
            var source = new BufferActorSource();
            ProbeActor liveActor = null;
            ProbeActor destroyedActor = null;
            using GameplayTestWorld testWorld = GameplayTestWorld.Create(
                configure: world =>
                {
                    liveActor = world.CreateAuthoringActor<ProbeActor>("LiveSourceActor");
                    destroyedActor = world.CreateAuthoringActor<ProbeActor>("DestroyedSourceActor");
                    Object.DestroyImmediate(destroyedActor.gameObject);
                    source.Add(liveActor);
                    source.Add(destroyedActor);
                    source.Add(liveActor);
                },
                actorSource: source);

            testWorld.StartWorld();

            Assert.AreEqual(1, source.CollectionCount);
            Assert.IsTrue(testWorld.World.IsActorRegistered(liveActor));
            Assert.AreEqual(1, liveActor.BeginPlayCount);
            Assert.AreSame(testWorld.World, liveActor.World);
        }

        [Test]
        public void SceneActorSource_IsolatesParallelGameInstancesByScene()
        {
            Scene sceneA = default;
            Scene sceneB = default;
            GameplayTestWorld testWorldA = null;
            GameplayTestWorld testWorldB = null;
            try
            {
                sceneA = EditorSceneManager.NewPreviewScene();
                sceneB = EditorSceneManager.NewPreviewScene();

                ProbeActor actorA = CreateActorInScene<ProbeActor>("SceneAActor", sceneA);
                ProbeActor actorB = CreateActorInScene<ProbeActor>("SceneBActor", sceneB);
                testWorldA = GameplayTestWorld.Create(
                    actorSource: new SceneWorldActorSource(sceneA));
                testWorldB = GameplayTestWorld.Create(
                    actorSource: new SceneWorldActorSource(sceneB));

                World worldA = testWorldA.StartWorld();
                World worldB = testWorldB.StartWorld();

                Assert.IsTrue(worldA.IsActorRegistered(actorA));
                Assert.IsFalse(worldA.IsActorRegistered(actorB));
                Assert.IsTrue(worldB.IsActorRegistered(actorB));
                Assert.IsFalse(worldB.IsActorRegistered(actorA));
                Assert.AreSame(worldA, actorA.World);
                Assert.AreSame(worldB, actorB.World);
            }
            finally
            {
                testWorldB?.Dispose();
                testWorldA?.Dispose();
                ClosePreviewScene(sceneB);
                ClosePreviewScene(sceneA);
            }
        }

        [Test]
        public void SceneActorSource_ReusesExternallyOwnedActorAcrossReplacementWorlds()
        {
            Scene actorScene = default;
            GameplayTestWorld testWorld = null;
            try
            {
                actorScene = EditorSceneManager.NewPreviewScene();
                ProbeActor actor = CreateActorInScene<ProbeActor>("ReplacementWorldActor", actorScene);
                var source = new SceneWorldActorSource(actorScene);
                testWorld = GameplayTestWorld.Create(actorSource: source);

                World firstWorld = testWorld.StartWorld();
                Assert.AreSame(firstWorld, actor.World);
                testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();
                Assert.IsNull(actor.World);
                Assert.IsFalse(actor == null);

                World replacementWorld = testWorld.StartWorld();
                Assert.AreNotSame(firstWorld, replacementWorld);
                Assert.AreSame(replacementWorld, actor.World);
                Assert.AreEqual(2, actor.BeginPlayCount);
            }
            finally
            {
                testWorld?.Dispose();
                ClosePreviewScene(actorScene);
            }
        }

        [Test]
        public void SceneActorSource_TraversalBudgetFailsWithoutPartiallyBindingCandidates()
        {
            Scene actorScene = default;
            GameplayTestWorld testWorld = null;
            World interruptedWorld = null;
            try
            {
                actorScene = EditorSceneManager.NewPreviewScene();
                ProbeActor candidate = CreateActorInScene<ProbeActor>("BudgetedRootActor", actorScene);
                var firstChild = new GameObject("FirstChild");
                var secondChild = new GameObject("SecondChild");
                firstChild.transform.SetParent(candidate.transform, worldPositionStays: false);
                secondChild.transform.SetParent(candidate.transform, worldPositionStays: false);
                var sceneSource = new SceneWorldActorSource(
                    actorScene,
                    maximumVisitedGameObjectCount: 2);
                var source = new CallbackActorSource(collector =>
                {
                    interruptedWorld = testWorld.Instance.CurrentWorld;
                    sceneSource.CollectActors(collector);
                });
                testWorld = GameplayTestWorld.Create(
                    actorSource: source,
                    discoverActiveSceneActors: false);

                Assert.Throws<InvalidOperationException>(() => testWorld.StartWorld());

                Assert.IsNotNull(interruptedWorld);
                Assert.AreEqual(WorldLifecycleState.Disposed, interruptedWorld.LifecycleState);
                Assert.AreEqual(0, interruptedWorld.ActorCount);
                Assert.IsNull(candidate.World);
                Assert.IsNull(testWorld.Instance.CurrentWorld);
            }
            finally
            {
                testWorld?.Dispose();
                ClosePreviewScene(actorScene);
            }
        }

        [Test]
        public void SceneActorSource_RejectsReentryAndRemainsReusableAfterFailure()
        {
            Scene actorScene = default;
            try
            {
                actorScene = EditorSceneManager.NewPreviewScene();
                CreateActorInScene<ProbeActor>("ReentrantSourceActor", actorScene);
                var source = new SceneWorldActorSource(actorScene);
                var collector = new ReentrantActorCollector(source);

                Assert.Throws<InvalidOperationException>(() => source.CollectActors(collector));

                Assert.DoesNotThrow(() => source.CollectActors(collector));
                Assert.AreEqual(2, collector.Count);
            }
            finally
            {
                ClosePreviewScene(actorScene);
            }
        }

        [Test]
        public void ActorSource_DisposingWorldDuringCollectionCannotBindAfterTerminalCleanup()
        {
            GameplayTestWorld testWorld = null;
            ProbeActor candidate = null;
            World interruptedWorld = null;
            var source = new CallbackActorSource(collector =>
            {
                interruptedWorld = testWorld.Instance.CurrentWorld;
                Assert.IsTrue(collector.TryAdd(candidate));
                interruptedWorld.Dispose();
            });

            try
            {
                testWorld = GameplayTestWorld.Create(
                    configure: world =>
                        candidate = world.CreateAuthoringActor<ProbeActor>("InterruptedCandidate"),
                    actorSource: source,
                    discoverActiveSceneActors: false);

                Assert.Throws<InvalidOperationException>(() => testWorld.StartWorld());

                Assert.IsNotNull(interruptedWorld);
                Assert.AreEqual(WorldLifecycleState.Disposed, interruptedWorld.LifecycleState);
                Assert.AreEqual(0, interruptedWorld.ActorCount);
                Assert.IsNull(candidate.World);
                Assert.IsNull(testWorld.Instance.CurrentWorld);
            }
            finally
            {
                testWorld?.Dispose();
            }
        }

        [Test]
        public void ActorSource_ExceedingCapacityFailsBeforeAnyCandidateIsRegistered()
        {
            GameplayTestWorld testWorld = null;
            World interruptedWorld = null;
            var candidates = new List<ProbeActor>(5);
            int observedCount = -1;
            int observedRemainingCapacity = -1;
            bool capacityRejected = false;
            var source = new CallbackActorSource(collector =>
            {
                interruptedWorld = testWorld.Instance.CurrentWorld;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!collector.TryAdd(candidates[i]))
                    {
                        capacityRejected = true;
                        observedCount = collector.Count;
                        observedRemainingCapacity = collector.RemainingCapacity;
                        return;
                    }
                }
            });

            try
            {
                testWorld = GameplayTestWorld.Create(
                    configure: world =>
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            candidates.Add(world.CreateAuthoringActor<ProbeActor>($"Candidate{i}"));
                        }
                    },
                    runtimeLimits: new WorldRuntimeLimits(
                        maximumActorCount: 4,
                        initialActorCapacity: 0),
                    actorSource: source,
                    discoverActiveSceneActors: false);

                Assert.Throws<InvalidOperationException>(() => testWorld.StartWorld());

                Assert.IsTrue(capacityRejected);
                Assert.AreEqual(4, observedCount);
                Assert.AreEqual(0, observedRemainingCapacity);
                Assert.IsNotNull(interruptedWorld);
                Assert.AreEqual(WorldLifecycleState.Disposed, interruptedWorld.LifecycleState);
                Assert.AreEqual(0, interruptedWorld.ActorCount);
                Assert.GreaterOrEqual(
                    interruptedWorld.GetActorAdmissionSnapshot().RejectedAdmissionCount,
                    1);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Assert.IsNull(candidates[i].World);
                }
            }
            finally
            {
                testWorld?.Dispose();
            }
        }

        [Test]
        public void ActorCollector_RejectsWorkerThreadReadsAndUseAfterCallback()
        {
            IWorldActorCollector retainedCollector = null;
            Exception countException = null;
            Exception capacityException = null;
            var source = new CallbackActorSource(collector =>
            {
                retainedCollector = collector;
                var worker = new Thread(() =>
                {
                    try
                    {
                        _ = collector.Count;
                    }
                    catch (Exception exception)
                    {
                        countException = exception;
                    }

                    try
                    {
                        _ = collector.RemainingCapacity;
                    }
                    catch (Exception exception)
                    {
                        capacityException = exception;
                    }
                });
                worker.Start();
                Assert.IsTrue(worker.Join(5_000), "Collector worker probe did not complete.");
            });

            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                actorSource: source,
                discoverActiveSceneActors: false);

            Assert.IsInstanceOf<InvalidOperationException>(countException);
            Assert.IsInstanceOf<InvalidOperationException>(capacityException);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = retainedCollector.Count;
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = retainedCollector.RemainingCapacity;
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                retainedCollector.TryAdd(null);
            });
        }

        [Test]
        public void UnregisterActor_RemovesNonOwnedActorWithoutDestroyOrLifetimeRelease()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            ProbeActor actor = testWorld.CreateAuthoringActor<ProbeActor>("ExternalActor");
            actor.EnableUpdateTick();
            testWorld.World.RegisterActor(actor);
            int actorCount = testWorld.World.ActorCount;

            Assert.IsTrue(testWorld.World.TryUnregisterActor(actor));

            Assert.AreEqual(actorCount - 1, testWorld.World.ActorCount);
            Assert.IsFalse(testWorld.World.IsActorRegistered(actor));
            Assert.IsNull(actor.World);
            Assert.IsFalse(actor == null);
            Assert.AreEqual(ActorLifecycleState.Ended, actor.LifecycleState);
            Assert.AreEqual(1, actor.EndPlayCount);
            Assert.AreEqual(EndPlayReason.RemovedFromWorld, actor.LastEndPlayReason);
            Assert.AreEqual(0, testWorld.World.GetTickActorCount(ActorTickPhase.Update));
        }

        [Test]
        public void UnregisterActor_RejectsWorldOwnedActorWithoutMutatingRegistration()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            ProbeActor prefab = testWorld.CreateAuthoringActor<ProbeActor>("OwnedActorPrefab");
            ProbeActor actor = testWorld.World.SpawnActor(prefab);
            int actorCount = testWorld.World.ActorCount;
            int ownedCount = testWorld.World.OwnedActorCount;

            Assert.IsFalse(testWorld.World.TryUnregisterActor(actor));
            Assert.Throws<InvalidOperationException>(() => testWorld.World.UnregisterActor(actor));
            Assert.AreEqual(actorCount, testWorld.World.ActorCount);
            Assert.AreEqual(ownedCount, testWorld.World.OwnedActorCount);
            Assert.IsTrue(testWorld.World.IsActorRegistered(actor));
            Assert.AreSame(testWorld.World, actor.World);
        }

        [Test]
        public void UnregisterActor_ClearsPlayerStartControllerAndGameStateBookkeeping()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start(
                netMode: WorldNetMode.Client,
                localPlayerCount: 1);
            PlayerStart playerStart = testWorld.CreateAuthoringActor<PlayerStart>("ExternalPlayerStart");
            PlayerState playerState = testWorld.CreateAuthoringActor<PlayerState>("ExternalPlayerState");
            PlayerController controller =
                testWorld.CreateAuthoringActor<PlayerController>("ExternalPlayerController");
            GameState gameState = testWorld.CreateAuthoringActor<GameState>("ExternalGameState");
            testWorld.World.RegisterActor(playerStart);
            testWorld.World.RegisterActor(playerState);
            testWorld.World.RegisterActor(controller);
            testWorld.World.RegisterActor(gameState);
            controller.InitializePlayer(
                testWorld.World,
                playerState,
                testWorld.Instance.LocalPlayers[0]);
            testWorld.World.CommitReplicatedPlayerController(
                controller,
                testWorld.Instance.LocalPlayers[0]);
            testWorld.World.SetReplicatedGameState(gameState);

            testWorld.World.UnregisterActor(playerStart);
            testWorld.World.UnregisterActor(controller);
            testWorld.World.UnregisterActor(gameState);

            Assert.AreEqual(0, testWorld.World.PlayerStartCount);
            Assert.AreEqual(0, testWorld.World.PlayerControllerCount);
            Assert.IsNull(testWorld.Instance.LocalPlayers[0].PlayerController);
            Assert.IsNull(testWorld.World.GameState);
            Assert.IsFalse(playerStart == null);
            Assert.IsFalse(controller == null);
            Assert.IsFalse(gameState == null);
            Assert.IsTrue(testWorld.World.IsActorRegistered(playerState));
        }

        private static T CreateActorInScene<T>(string name, Scene scene) where T : Actor
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            T actor = gameObject.AddComponent<T>();
            UnityLifecycleTestUtility.InvokeAwake(actor);
            return actor;
        }

        private static void ClosePreviewScene(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private sealed class BufferActorSource : IWorldActorSource
        {
            private readonly List<Actor> actors = new List<Actor>(4);

            public int CollectionCount { get; private set; }

            public void Add(Actor actor)
            {
                actors.Add(actor);
            }

            public void CollectActors(IWorldActorCollector collector)
            {
                CollectionCount++;
                for (int i = 0; i < actors.Count; i++)
                {
                    if (!collector.TryAdd(actors[i]))
                    {
                        return;
                    }
                }
            }
        }

        private sealed class CallbackActorSource : IWorldActorSource
        {
            private readonly Action<IWorldActorCollector> callback;

            public CallbackActorSource(Action<IWorldActorCollector> callback)
            {
                this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
            }

            public void CollectActors(IWorldActorCollector collector)
            {
                callback(collector);
            }
        }

        private sealed class ReentrantActorCollector : IWorldActorCollector
        {
            private readonly SceneWorldActorSource source;
            private bool reenter = true;

            public ReentrantActorCollector(SceneWorldActorSource source)
            {
                this.source = source;
            }

            public int Count { get; private set; }
            public int RemainingCapacity => int.MaxValue;

            public bool TryAdd(Actor actor)
            {
                Count++;
                if (reenter)
                {
                    reenter = false;
                    source.CollectActors(this);
                }

                return true;
            }
        }

        private sealed class ProbeActor : Actor
        {
            public int BeginPlayCount { get; private set; }
            public int EndPlayCount { get; private set; }
            public EndPlayReason LastEndPlayReason { get; private set; }

            public void EnableUpdateTick()
            {
                ConfigureActorTick(ActorTickPhase.Update, startWithTickEnabled: true);
            }

            protected override void BeginPlay()
            {
                BeginPlayCount++;
            }

            protected override void EndPlay(EndPlayReason reason)
            {
                EndPlayCount++;
                LastEndPlayReason = reason;
            }
        }
    }
}
