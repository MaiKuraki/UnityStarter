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
                DerivedGameplayWorldHost host = hostObject.AddComponent<DerivedGameplayWorldHost>();
                SetField(host, "worldSettings", settings);

                yield return null;
                Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                Assert.IsTrue(host.AwakeWasCalled);
                Assert.IsNotNull(hostObject.GetComponent<GameplayWorldTickDriver>());

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

        private sealed class DerivedGameplayWorldHost : GameplayWorldHost
        {
            public bool AwakeWasCalled { get; private set; }

            private void Awake()
            {
                AwakeWasCalled = true;
            }
        }
    }
}
