using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.GameplayFramework.Tests.PlayMode
{
    public sealed class GameplayWorldHostPlayModeTests
    {
        [UnityTest]
        public IEnumerator AutoStart_OwnsAndDisposesWorldWithGameObjectLifetime()
        {
            var authoringObjects = new List<GameObject>(4);
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            GameObject hostObject = null;
            try
            {
                SetField(settings, "gameModeClass", CreateActor<GameMode>("GameModePrefab", authoringObjects));
                SetField(settings, "playerControllerClass", CreateActor<PlayerController>("PlayerControllerPrefab", authoringObjects));
                SetField(settings, "pawnClass", CreateActor<Pawn>("PawnPrefab", authoringObjects));
                SetField(settings, "playerStateClass", CreateActor<PlayerState>("PlayerStatePrefab", authoringObjects));

                hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                SetField(host, "worldSettings", settings);

                yield return null;

                Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                Assert.IsNotNull(host.CurrentWorld);
                Assert.AreEqual(WorldLifecycleState.Playing, host.CurrentWorld.LifecycleState);
                GameInstance instance = host.GameInstance;

                Object.Destroy(hostObject);
                hostObject = null;
                yield return null;

                Assert.IsTrue(instance.IsDisposed);
                Assert.IsNull(instance.CurrentWorld);
            }
            finally
            {
                if (hostObject != null)
                {
                    Object.Destroy(hostObject);
                }

                for (int i = authoringObjects.Count - 1; i >= 0; i--)
                {
                    if (authoringObjects[i] != null)
                    {
                        Object.Destroy(authoringObjects[i]);
                    }
                }

                Object.Destroy(settings);
            }
        }

        [UnityTest]
        public IEnumerator AutoStart_DrivesAllActorTickPhasesAndStopsWithHostLifetime()
        {
            var authoringObjects = new List<GameObject>(8);
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            GameObject hostObject = null;
            try
            {
                SetField(settings, "gameModeClass", CreateActor<GameMode>("GameModePrefab", authoringObjects));
                SetField(settings, "playerControllerClass", CreateActor<PlayerController>("PlayerControllerPrefab", authoringObjects));
                SetField(settings, "pawnClass", CreateActor<Pawn>("PawnPrefab", authoringObjects));
                SetField(settings, "playerStateClass", CreateActor<PlayerState>("PlayerStatePrefab", authoringObjects));

                PlayModeTickActor updateActor = CreateActor<PlayModeTickActor>("UpdateActor", authoringObjects);
                PlayModeTickActor fixedActor = CreateActor<PlayModeTickActor>("FixedActor", authoringObjects);
                PlayModeTickActor lateActor = CreateActor<PlayModeTickActor>("LateActor", authoringObjects);
                updateActor.Configure(ActorTickPhase.Update);
                fixedActor.Configure(ActorTickPhase.FixedUpdate);
                lateActor.Configure(ActorTickPhase.LateUpdate);

                hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                SetField(host, "worldSettings", settings);

                yield return null;
                Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                Assert.IsNotNull(hostObject.GetComponent<GameplayWorldTickDriver>());
                Assert.IsNotNull(hostObject.GetComponent<GameplayWorldLateTickDriver>());

                int updateBefore = updateActor.TickCount;
                int lateBefore = lateActor.TickCount;
                yield return null;
                Assert.Greater(updateActor.TickCount, updateBefore);
                Assert.Greater(lateActor.TickCount, lateBefore);
                Assert.GreaterOrEqual(updateActor.LastDeltaSeconds, 0f);
                Assert.GreaterOrEqual(lateActor.LastDeltaSeconds, 0f);

                int fixedBefore = fixedActor.TickCount;
                yield return new WaitForFixedUpdate();
                Assert.Greater(fixedActor.TickCount, fixedBefore);
                Assert.AreEqual(Time.fixedDeltaTime, fixedActor.LastDeltaSeconds, 0.000001f);

                host.enabled = false;
                int updateWhileDisabled = updateActor.TickCount;
                int fixedWhileDisabled = fixedActor.TickCount;
                int lateWhileDisabled = lateActor.TickCount;
                yield return null;
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(updateWhileDisabled, updateActor.TickCount);
                Assert.AreEqual(fixedWhileDisabled, fixedActor.TickCount);
                Assert.AreEqual(lateWhileDisabled, lateActor.TickCount);
                Assert.AreEqual(WorldLifecycleState.Playing, host.CurrentWorld.LifecycleState);

                host.enabled = true;
                yield return null;
                Assert.Greater(updateActor.TickCount, updateWhileDisabled);
                Assert.Greater(lateActor.TickCount, lateWhileDisabled);

                Object.Destroy(hostObject);
                hostObject = null;
                yield return null;

                int updateAfterStop = updateActor.TickCount;
                int fixedAfterStop = fixedActor.TickCount;
                int lateAfterStop = lateActor.TickCount;
                yield return null;
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(updateAfterStop, updateActor.TickCount);
                Assert.AreEqual(fixedAfterStop, fixedActor.TickCount);
                Assert.AreEqual(lateAfterStop, lateActor.TickCount);
            }
            finally
            {
                if (hostObject != null)
                {
                    Object.Destroy(hostObject);
                }

                for (int i = authoringObjects.Count - 1; i >= 0; i--)
                {
                    if (authoringObjects[i] != null)
                    {
                        Object.Destroy(authoringObjects[i]);
                    }
                }

                Object.Destroy(settings);
            }
        }

        [UnityTest]
        public IEnumerator PlayerLoop_OrdersFrameworkTicksAroundOrdinaryMonoBehaviourCallbacks()
        {
            var authoringObjects = new List<GameObject>(8);
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            GameObject hostObject = null;
            try
            {
                SetField(settings, "gameModeClass", CreateActor<GameMode>("GameModePrefab", authoringObjects));
                SetField(settings, "playerControllerClass", CreateActor<PlayerController>("PlayerControllerPrefab", authoringObjects));
                SetField(settings, "pawnClass", CreateActor<Pawn>("PawnPrefab", authoringObjects));
                SetField(settings, "playerStateClass", CreateActor<PlayerState>("PlayerStatePrefab", authoringObjects));

                var ordinaryCallbackObject = new GameObject("OrdinaryPlayerLoopCallbacks");
                authoringObjects.Add(ordinaryCallbackObject);
                PlayerLoopOrderRecorder recorder =
                    ordinaryCallbackObject.AddComponent<PlayerLoopOrderRecorder>();

                PlayerLoopOrderTickActor updateActor =
                    CreateActor<PlayerLoopOrderTickActor>("OrderedUpdateActor", authoringObjects);
                PlayerLoopOrderTickActor lateUpdateActor =
                    CreateActor<PlayerLoopOrderTickActor>("OrderedLateUpdateActor", authoringObjects);
                updateActor.Configure(
                    ActorTickPhase.Update,
                    recorder,
                    PlayerLoopCallback.FrameworkUpdate);
                lateUpdateActor.Configure(
                    ActorTickPhase.LateUpdate,
                    recorder,
                    PlayerLoopCallback.FrameworkLateUpdate);

                hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                SetField(host, "worldSettings", settings);

                yield return null;
                Assert.AreEqual(GameplayWorldHostState.Running, host.State);

                recorder.BeginCapture();
                const int maximumCaptureFrames = 8;
                for (int i = 0; i < maximumCaptureFrames && !recorder.HasCompletedFrame; i++)
                {
                    yield return null;
                }

                Assert.IsTrue(
                    recorder.HasCompletedFrame,
                    "A complete PlayerLoop ordering sample was not observed within the capture window.");
                CollectionAssert.AreEqual(
                    new[]
                    {
                        PlayerLoopCallback.FrameworkUpdate,
                        PlayerLoopCallback.OrdinaryUpdate,
                        PlayerLoopCallback.OrdinaryLateUpdate,
                        PlayerLoopCallback.FrameworkLateUpdate,
                    },
                    recorder.CompletedOrder,
                    "Framework callbacks must bracket ordinary MonoBehaviour Update and LateUpdate callbacks.");
            }
            finally
            {
                if (hostObject != null)
                {
                    Object.Destroy(hostObject);
                }

                for (int i = authoringObjects.Count - 1; i >= 0; i--)
                {
                    if (authoringObjects[i] != null)
                    {
                        Object.Destroy(authoringObjects[i]);
                    }
                }

                Object.Destroy(settings);
            }
        }

        [UnityTest]
        public IEnumerator ExternalUnityDestroy_ReleasesWorldOwnedActorExactlyOnce()
        {
            var authoringObjects = new List<GameObject>(5);
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            var lifetime = new RecordingActorLifetime();
            GameInstance instance = null;
            try
            {
                SetField(settings, "gameModeClass", CreateActor<GameMode>("GameModePrefab", authoringObjects));
                SetField(settings, "playerControllerClass", CreateActor<PlayerController>("PlayerControllerPrefab", authoringObjects));
                SetField(settings, "pawnClass", CreateActor<Pawn>("PawnPrefab", authoringObjects));
                SetField(settings, "playerStateClass", CreateActor<PlayerState>("PlayerStatePrefab", authoringObjects));

                instance = new GameInstance(lifetime, localPlayerCount: 0);
                World world = instance.StartWorldAsync(settings).GetAwaiter().GetResult();
                PlayModeLifetimeActor prefab =
                    CreateActor<PlayModeLifetimeActor>("LifetimeActorPrefab", authoringObjects);
                PlayModeLifetimeActor actor = world.SpawnActor(prefab);
                int actorCountBeforeDestroy = world.ActorCount;

                Object.Destroy(actor.gameObject);
                yield return null;

                Assert.AreEqual(actorCountBeforeDestroy - 1, world.ActorCount);
                Assert.AreEqual(1, lifetime.GetReleaseCount(actor));

                instance.Dispose();
                Assert.AreEqual(1, lifetime.GetReleaseCount(actor));
                instance = null;
            }
            finally
            {
                instance?.Dispose();
                for (int i = authoringObjects.Count - 1; i >= 0; i--)
                {
                    if (authoringObjects[i] != null)
                    {
                        Object.Destroy(authoringObjects[i]);
                    }
                }

                Object.Destroy(settings);
            }
        }

        private static T CreateActor<T>(string name, List<GameObject> objects) where T : Actor
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = null;
            System.Type currentType = target.GetType();
            while (currentType != null && field == null)
            {
                field = currentType.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                currentType = currentType.BaseType;
            }

            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private sealed class PlayModeTickActor : Actor
        {
            public int TickCount { get; private set; }
            public float LastDeltaSeconds { get; private set; }

            public void Configure(ActorTickPhase phase)
            {
                ConfigureActorTick(phase, startWithTickEnabled: true);
            }

            protected override void Tick(float deltaSeconds)
            {
                TickCount++;
                LastDeltaSeconds = deltaSeconds;
            }
        }

        private sealed class PlayerLoopOrderTickActor : Actor
        {
            private PlayerLoopOrderRecorder recorder;
            private PlayerLoopCallback callback;

            public void Configure(
                ActorTickPhase phase,
                PlayerLoopOrderRecorder targetRecorder,
                PlayerLoopCallback targetCallback)
            {
                recorder = targetRecorder;
                callback = targetCallback;
                ConfigureActorTick(phase, startWithTickEnabled: true);
            }

            protected override void Tick(float deltaSeconds)
            {
                recorder?.Record(callback);
            }
        }

        private sealed class PlayModeLifetimeActor : Actor
        {
        }

        private sealed class RecordingActorLifetime : IActorLifetime
        {
            private readonly List<Actor> releasedActors = new List<Actor>(8);

            public T Create<T>(T prefab) where T : Actor
            {
                return Object.Instantiate(prefab);
            }

            public void Release(Actor actor)
            {
                releasedActors.Add(actor);
                if (actor != null)
                {
                    Object.Destroy(actor.gameObject);
                }
            }

            public int GetReleaseCount(Actor actor)
            {
                int count = 0;
                for (int i = 0; i < releasedActors.Count; i++)
                {
                    if (ReferenceEquals(releasedActors[i], actor))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private sealed class PlayerLoopOrderRecorder : MonoBehaviour
        {
            private readonly List<PlayerLoopCallback> currentOrder = new List<PlayerLoopCallback>(4);
            private readonly PlayerLoopCallback[] completedOrder = new PlayerLoopCallback[4];
            private bool isCapturing;
            private int activeFrame = -1;

            public bool HasCompletedFrame { get; private set; }
            public IReadOnlyList<PlayerLoopCallback> CompletedOrder => completedOrder;

            private void Update()
            {
                Record(PlayerLoopCallback.OrdinaryUpdate);
            }

            private void LateUpdate()
            {
                Record(PlayerLoopCallback.OrdinaryLateUpdate);
            }

            public void BeginCapture()
            {
                isCapturing = true;
                HasCompletedFrame = false;
                activeFrame = -1;
                currentOrder.Clear();
            }

            public void Record(PlayerLoopCallback callback)
            {
                if (!isCapturing)
                {
                    return;
                }

                int frame = Time.frameCount;
                if (activeFrame != frame)
                {
                    activeFrame = frame;
                    currentOrder.Clear();
                }

                if (currentOrder.Contains(callback))
                {
                    return;
                }

                currentOrder.Add(callback);
                if (currentOrder.Count != completedOrder.Length)
                {
                    return;
                }

                currentOrder.CopyTo(completedOrder);
                HasCompletedFrame = true;
                isCapturing = false;
            }
        }

        private enum PlayerLoopCallback : byte
        {
            FrameworkUpdate = 0,
            OrdinaryUpdate = 1,
            OrdinaryLateUpdate = 2,
            FrameworkLateUpdate = 3,
        }

    }
}
