using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Editor;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class GameplayWorldHostTests
    {
        [Test]
        public void Host_StartAndStop_OwnsOneGameInstanceAndWorld()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);

            try
            {
                World world = host.StartWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                Assert.AreSame(world, host.CurrentWorld);
                Assert.IsNotNull(host.GameInstance);
                Assert.Greater(world.OwnedActorCount, 0);

                world.Dispose();
                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.CurrentWorld);

                World replacementWorld = host.StartWorldAsync().GetAwaiter().GetResult();
                Assert.AreNotSame(world, replacementWorld);
                Assert.AreEqual(GameplayWorldHostState.Running, host.State);

                host.StopWorldAsync().GetAwaiter().GetResult();

                Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                Assert.IsNull(host.CurrentWorld);
                Assert.IsNull(host.GameInstance);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void Host_ExplicitComposition_AppliesImmutableWorldRuntimeLimits()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            AssignWorldSettings(host, testWorld.Settings);
            var limits = new WorldRuntimeLimits(
                maximumActorCount: 32,
                initialActorCapacity: 8,
                initialUpdateTickCapacity: 4,
                initialFixedUpdateTickCapacity: 2,
                initialLateUpdateTickCapacity: 2);
            host.Configure(new GameplayWorldComposition(
                new UnityActorLifetime(),
                runtimeLimits: limits));

            try
            {
                World world = host.StartWorldAsync().GetAwaiter().GetResult();

                Assert.IsTrue(host.HasExplicitComposition);
                Assert.AreSame(limits, host.Composition.RuntimeLimits);
                Assert.AreSame(limits, host.GameInstance.RuntimeLimits);
                Assert.AreSame(limits, world.RuntimeLimits);
                ActorAdmissionSnapshot snapshot = world.GetActorAdmissionSnapshot();
                Assert.AreEqual(32, snapshot.MaximumActorCount);
                Assert.GreaterOrEqual(snapshot.AllocatedActorCapacity, snapshot.ActorCount);
                Assert.Throws<System.InvalidOperationException>(() =>
                    host.Configure(GameplayWorldComposition.CreateDefault()));
            }
            finally
            {
                if (host.IsRunning)
                {
                    host.StopWorldAsync().GetAwaiter().GetResult();
                }

                Object.DestroyImmediate(hostObject);
            }
        }

        [UnityTest]
        public IEnumerator Host_PreCanceledStart_RollsBackToStoppedAndCanRetry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                try
                {
                    Exception cancellationFailure = await CaptureStartFailure(
                        host.StartWorldAsync(cancellation.Token));

                    Assert.IsInstanceOf<OperationCanceledException>(cancellationFailure);
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.IsNull(host.CurrentWorld);

                    World retryWorld = await host.StartWorldAsync();
                    Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                    Assert.AreSame(retryWorld, host.CurrentWorld);
                    await host.StopWorldAsync();
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_StopDuringPendingStart_CancelsAndCompletesBothTransactions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Pending,
                };
                ConfigureExternalGameMode(testWorld.Settings);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    referenceResolver: resolver));

                try
                {
                    UniTask<World> startTask = host.StartWorldAsync();
                    Assert.IsTrue(resolver.ResolveEntered);
                    Assert.AreEqual(GameplayWorldHostState.Starting, host.State);

                    UniTask stopTask = host.StopWorldAsync();
                    await stopTask;
                    Exception startFailure = await CaptureStartFailure(startTask);

                    Assert.IsInstanceOf<OperationCanceledException>(startFailure);
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.IsNull(host.CurrentWorld);
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_FaultedStart_CanRetryWithoutLeakingPreviousGameInstance()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Fault,
                };
                ConfigureExternalGameMode(testWorld.Settings);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    referenceResolver: resolver));

                try
                {
                    Exception firstFailure = await CaptureStartFailure(host.StartWorldAsync());
                    Assert.IsInstanceOf<InvalidOperationException>(firstFailure);
                    Assert.AreEqual(GameplayWorldHostState.Faulted, host.State);
                    Assert.IsNull(host.GameInstance);
                    Assert.IsNotEmpty(host.LastError);

                    resolver.Behavior = ResolverBehavior.Succeed;
                    World world = await host.StartWorldAsync();

                    Assert.AreEqual(2, resolver.ResolveCount);
                    Assert.AreEqual(GameplayWorldHostState.Running, host.State);
                    Assert.AreSame(world, host.CurrentWorld);
                    Assert.IsNull(host.LastError);
                    await host.StopWorldAsync();
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [UnityTest]
        public IEnumerator Host_ReentrantStartWhilePending_FailsWithoutReplacingActiveTransaction()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using GameplayTestWorld testWorld = GameplayTestWorld.Create();
                var resolver = new ControlledResolver(testWorld.Settings.GameModeClass)
                {
                    Behavior = ResolverBehavior.Pending,
                };
                ConfigureExternalGameMode(testWorld.Settings);
                var hostObject = new GameObject("GameplayWorldHost");
                GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
                AssignWorldSettings(host, testWorld.Settings);
                host.Configure(new GameplayWorldComposition(
                    new UnityActorLifetime(),
                    referenceResolver: resolver));

                try
                {
                    UniTask<World> firstStart = host.StartWorldAsync();
                    GameInstance firstInstance = host.GameInstance;
                    Exception reentrantFailure = await CaptureStartFailure(host.StartWorldAsync());

                    Assert.IsInstanceOf<InvalidOperationException>(reentrantFailure);
                    Assert.AreSame(firstInstance, host.GameInstance);
                    Assert.AreEqual(GameplayWorldHostState.Starting, host.State);
                    Assert.AreEqual(1, resolver.ResolveCount);

                    await host.StopWorldAsync();
                    Exception firstFailure = await CaptureStartFailure(firstStart);
                    Assert.IsInstanceOf<OperationCanceledException>(firstFailure);
                    Assert.AreEqual(GameplayWorldHostState.Stopped, host.State);
                }
                finally
                {
                    Object.DestroyImmediate(hostObject);
                }
            });
        }

        [Test]
        public void WorldRuntimeLimits_CapInitialCapacityHintsToAdmissionLimit()
        {
            var limits = new WorldRuntimeLimits(maximumActorCount: 4);

            Assert.AreEqual(4, limits.InitialActorCapacity);
            Assert.AreEqual(4, limits.InitialUpdateTickCapacity);
            Assert.AreEqual(4, limits.InitialFixedUpdateTickCapacity);
            Assert.AreEqual(4, limits.InitialLateUpdateTickCapacity);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new WorldRuntimeLimits(initialActorCapacity: -1));
        }

        [Test]
        public void WorldActorRegistration_IsAllocationFreeIndexedReadModel()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            World world = testWorld.World;
            int ownedCount = 0;

            for (int i = 0; i < world.ActorCount; i++)
            {
                Assert.IsTrue(world.TryGetActorRegistration(i, out WorldActorRegistration registration));
                Assert.IsNotNull(registration.Actor);
                if (registration.IsWorldOwned)
                {
                    ownedCount++;
                }
            }

            Assert.AreEqual(world.OwnedActorCount, ownedCount);
            Assert.IsFalse(world.TryGetActorRegistration(-1, out _));
            Assert.IsFalse(world.TryGetActorRegistration(world.ActorCount, out _));
        }

        [Test]
        public void EditorTools_ExposeHostInspectorAndConfigurationErrors()
        {
            var hostObject = new GameObject("GameplayWorldHost");
            GameplayWorldHost host = hostObject.AddComponent<GameplayWorldHost>();
            WorldSettings settings = ScriptableObject.CreateInstance<WorldSettings>();
            UnityEditor.Editor hostEditor = null;
            try
            {
                hostEditor = UnityEditor.Editor.CreateEditor(host);
                Assert.IsInstanceOf<GameplayWorldHostEditor>(hostEditor);

                var issues = new List<GameplayFrameworkValidationIssue>();
                GameplayFrameworkProjectValidator.ValidateWorldSettings(settings, issues);

                Assert.AreEqual(4, issues.Count);
                Assert.IsTrue(issues.TrueForAll(
                    issue => issue.Severity == GameplayFrameworkValidationSeverity.Error));
            }
            finally
            {
                Object.DestroyImmediate(hostEditor);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(hostObject);
            }
        }

        private static void AssignWorldSettings(GameplayWorldHost host, WorldSettings settings)
        {
            var serializedHost = new SerializedObject(host);
            serializedHost.FindProperty("worldSettings").objectReferenceValue = settings;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureExternalGameMode(WorldSettings settings)
        {
            var serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("gameModeSource").enumValueIndex =
                (int)WorldSettingsReferenceSource.PathLocation;
            serializedSettings.FindProperty("gameModeAssetLocation").stringValue =
                "tests/game-mode";
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static async UniTask<Exception> CaptureStartFailure(UniTask<World> startTask)
        {
            try
            {
                await startTask;
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private enum ResolverBehavior : byte
        {
            Pending = 0,
            Fault = 1,
            Succeed = 2,
        }

        private sealed class ControlledResolver : IWorldSettingsReferenceResolver
        {
            private readonly GameMode gameMode;

            public ControlledResolver(GameMode gameMode)
            {
                this.gameMode = gameMode;
            }

            public ResolverBehavior Behavior { get; set; }
            public bool ResolveEntered { get; private set; }
            public int ResolveCount { get; private set; }

            public bool Supports(WorldSettingsReferenceSource source)
            {
                return source == WorldSettingsReferenceSource.PathLocation;
            }

            public async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
                string location,
                CancellationToken cancellationToken) where T : UnityEngine.Object
            {
                ResolveEntered = true;
                ResolveCount++;
                if (Behavior == ResolverBehavior.Pending)
                {
                    await UniTask.WaitUntilCanceled(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (Behavior == ResolverBehavior.Fault)
                {
                    throw new InvalidOperationException("Resolver failure requested by the test.");
                }

                T asset = gameMode as T;
                return asset != null
                    ? new WorldSettingsAssetLoadResult<T>(true, asset, null)
                    : new WorldSettingsAssetLoadResult<T>(false, null, "Unexpected asset type.");
            }
        }

    }
}
