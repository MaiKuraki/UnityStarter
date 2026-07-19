using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class WorldSettingsTests
    {
        private const string SampleWorldSettingsPath = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/Samples/Sample.PureUnity/Settings/UnitySampleWorldSettings.asset";
        private const string SampleGameModePrefabPath = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/Samples/Sample.PureUnity/Prefabs/UnitySampleGameMode.prefab";

        private readonly List<GameObject> objects = new List<GameObject>(6);
        private WorldSettings settings;

        [TearDown]
        public void TearDown()
        {
            if (settings != null) Object.DestroyImmediate(settings);
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void Validate_RequiresGameModeControllerPawnAndPlayerState()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            Assert.IsFalse(settings.Validate(logWarnings: false));

            AssignRequiredDirectReferences();

            Assert.IsTrue(settings.Validate(logWarnings: false));
            Assert.IsTrue(settings.HasConfiguredGameMode);
            Assert.IsTrue(settings.HasConfiguredPlayerController);
            Assert.IsTrue(settings.HasConfiguredPawn);
            Assert.IsTrue(settings.HasConfiguredPlayerState);
            Assert.IsFalse(settings.UsesExternalReferences);
        }

        [Test]
        public void SampleWorldSettings_GameModeDirectReference_ResolvesPersistedPrefabComponent()
        {
            WorldSettings sampleSettings = AssetDatabase.LoadAssetAtPath<WorldSettings>(SampleWorldSettingsPath);
            GameObject gameModePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SampleGameModePrefabPath);

            Assert.IsNotNull(sampleSettings);
            Assert.IsNotNull(gameModePrefab);

            GameMode expectedGameMode = gameModePrefab.GetComponent<GameMode>();
            Assert.IsNotNull(expectedGameMode);
            Assert.AreEqual(
                "CycloneGames.GameplayFramework.Runtime.Sample.PureUnity.UnitySampleGameMode",
                expectedGameMode.GetType().FullName);

            var serializedSettings = new SerializedObject(sampleSettings);
            serializedSettings.Update();
            SerializedProperty gameModeProperty = serializedSettings.FindProperty("gameModeClass");

            Assert.AreSame(expectedGameMode, gameModeProperty.objectReferenceValue);
            Assert.AreEqual(WorldSettingsReferenceSource.DirectReference, sampleSettings.GameModeSource);
            Assert.AreSame(expectedGameMode, sampleSettings.GameModeClass);
            Assert.IsTrue(sampleSettings.HasConfiguredGameMode);
            Assert.IsTrue(sampleSettings.Validate(logWarnings: false));

            using WorldDefinition definition = sampleSettings
                .ResolveDefinitionAsync()
                .GetAwaiter()
                .GetResult();
            Assert.AreSame(expectedGameMode, definition.GameModeClass);

            GameMode instance = null;
            try
            {
                instance = new DefaultUnityObjectSpawner().Create(definition.GameModeClass);
                Assert.AreEqual(expectedGameMode.GetType(), instance.GetType());
                Assert.AreNotSame(expectedGameMode, instance);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance.gameObject);
                }
            }
        }

        [Test]
        public void ResolveDefinition_UsesExplicitResolverWithoutMutatingAuthoringAsset()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            Pawn authoringPawn = settings.PawnClass;
            Pawn resolvedPawn = CreateComponent<Pawn>("ResolvedPawn");
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            var resolver = new TestResolver { Asset = resolvedPawn };

            using WorldDefinition definition = settings
                .ResolveDefinitionAsync(resolver)
                .GetAwaiter()
                .GetResult();

            Assert.AreSame(resolvedPawn, definition.PawnClass);
            Assert.AreSame(authoringPawn, settings.PawnClass);
            Assert.AreEqual("world/pawn", resolver.LastLocation);
            Assert.IsTrue(settings.UsesExternalReferences);
        }

        [Test]
        public void WorldDefinition_DisposesExternalLeaseExactlyOnce()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            var lease = new TestLease();
            var resolver = new TestResolver
            {
                Asset = CreateComponent<Pawn>("ResolvedPawn"),
                Lease = lease,
            };

            WorldDefinition definition = settings.ResolveDefinitionAsync(resolver).GetAwaiter().GetResult();
            Assert.IsFalse(lease.Disposed);

            definition.Dispose();
            definition.Dispose();

            Assert.IsTrue(lease.Disposed);
            Assert.AreEqual(1, lease.DisposeCount);
        }

        [Test]
        public void WorldDefinition_WorkerThreadDisposeIsRejectedAndCanRetryOnOwnerThread()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            var lease = new TestLease();
            var resolver = new TestResolver
            {
                Asset = CreateComponent<Pawn>("ResolvedPawn"),
                Lease = lease,
            };
            WorldDefinition definition = settings.ResolveDefinitionAsync(resolver).GetAwaiter().GetResult();
            Exception workerException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    definition.Dispose();
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");
            Assert.IsInstanceOf<InvalidOperationException>(workerException);
            Assert.IsFalse(definition.IsDisposed);
            Assert.AreEqual(0, lease.DisposeCount);

            definition.Dispose();

            Assert.IsTrue(definition.IsDisposed);
            Assert.AreEqual(1, lease.DisposeCount);
        }

        [Test]
        public void ResolveDefinition_ThrowsWhenExternalResolverIsMissing()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.AssetReference);
            SetString("pawnAssetLocation", "assets/pawn");

            Assert.Throws<InvalidOperationException>(() =>
                settings.ResolveDefinitionAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void ResolveDefinition_PropagatesCancellation()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                settings.ResolveDefinitionAsync(new TestResolver(), cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
        }

        [Test]
        public void ResolveDefinition_CancellationAfterResolveDisposesUntransferredLease()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            using var cancellation = new CancellationTokenSource();
            var lease = new TestLease();
            var resolver = new TestResolver
            {
                Asset = CreateComponent<Pawn>("ResolvedPawn"),
                Lease = lease,
                OnResolve = cancellation.Cancel,
            };

            Assert.Throws<OperationCanceledException>(() =>
                settings.ResolveDefinitionAsync(resolver, cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
            Assert.AreEqual(1, lease.DisposeCount);
        }

        [UnityTest]
        public IEnumerator ResolveDefinition_WorkerFaultRollsBackPriorLeaseOnMainThread()
        {
            return UniTask.ToCoroutine(async () =>
            {
                int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                AssignRequiredDirectReferences();
                SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
                SetString("pawnAssetLocation", "world/pawn");
                SetSource("playerStateSource", WorldSettingsReferenceSource.PathLocation);
                SetString("playerStateAssetLocation", "world/player-state");
                var lease = new TestLease();
                var resolver = new WorkerFaultResolver(
                    CreateComponent<Pawn>("ResolvedPawn"),
                    lease);

                InvalidOperationException failure = null;
                try
                {
                    await settings.ResolveDefinitionAsync(resolver);
                }
                catch (InvalidOperationException exception)
                {
                    failure = exception;
                }

                Assert.IsNotNull(failure);
                Assert.AreEqual(1, lease.DisposeCount);
                Assert.AreEqual(ownerThreadId, lease.DisposeThreadId);
            });
        }

        private void AssignRequiredDirectReferences()
        {
            SetObject("gameModeClass", CreateComponent<GameMode>("GameMode"));
            SetObject("playerControllerClass", CreateComponent<PlayerController>("PlayerController"));
            SetObject("pawnClass", CreateComponent<Pawn>("Pawn"));
            SetObject("playerStateClass", CreateComponent<PlayerState>("PlayerState"));
        }

        private T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private void SetObject(string fieldName, Object value)
        {
            var serializedObject = new SerializedObject(settings);
            serializedObject.FindProperty(fieldName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetSource(string fieldName, WorldSettingsReferenceSource source)
        {
            var serializedObject = new SerializedObject(settings);
            serializedObject.FindProperty(fieldName).enumValueIndex = (int)source;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetString(string fieldName, string value)
        {
            var serializedObject = new SerializedObject(settings);
            serializedObject.FindProperty(fieldName).stringValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class TestResolver : IWorldSettingsReferenceResolver
        {
            public Object Asset { get; set; }
            public IDisposable Lease { get; set; }
            public Action OnResolve { get; set; }
            public string LastLocation { get; private set; }

            public bool Supports(WorldSettingsReferenceSource source)
            {
                return source == WorldSettingsReferenceSource.PathLocation ||
                       source == WorldSettingsReferenceSource.AssetReference;
            }

            public UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
                string location,
                CancellationToken cancellationToken) where T : Object
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastLocation = location;
                T typedAsset = Asset as T;
                OnResolve?.Invoke();
                return UniTask.FromResult(typedAsset != null
                    ? new WorldSettingsAssetLoadResult<T>(true, typedAsset, null, Lease)
                    : new WorldSettingsAssetLoadResult<T>(false, null, "Missing test asset."));
            }
        }

        private sealed class TestLease : IDisposable
        {
            public bool Disposed { get; private set; }
            public int DisposeCount { get; private set; }
            public int DisposeThreadId { get; private set; }

            public void Dispose()
            {
                Disposed = true;
                DisposeCount++;
                DisposeThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        private sealed class WorkerFaultResolver : IWorldSettingsReferenceResolver
        {
            private readonly Object successfulAsset;
            private readonly IDisposable lease;

            public WorkerFaultResolver(Object successfulAsset, IDisposable lease)
            {
                this.successfulAsset = successfulAsset;
                this.lease = lease;
            }

            public bool Supports(WorldSettingsReferenceSource source)
            {
                return source == WorldSettingsReferenceSource.PathLocation;
            }

            public UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
                string location,
                CancellationToken cancellationToken) where T : Object
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (location == "world/pawn")
                {
                    return UniTask.FromResult(new WorldSettingsAssetLoadResult<T>(
                        true,
                        successfulAsset as T,
                        null,
                        lease));
                }

                var completion = new UniTaskCompletionSource<WorldSettingsAssetLoadResult<T>>();
                ThreadPool.QueueUserWorkItem(_ => completion.TrySetException(
                    new InvalidOperationException("Worker resolver failure requested by test.")));
                return completion.Task;
            }
        }
    }
}
