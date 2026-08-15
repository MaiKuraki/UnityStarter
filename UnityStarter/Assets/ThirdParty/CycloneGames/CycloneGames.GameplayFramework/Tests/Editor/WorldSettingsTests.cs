using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class WorldSettingsTests
    {
        private const string SampleWorldSettingsPath = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/Samples/Sample.PureUnity/Settings/UnitySampleWorldSettings.asset";
        private const string SampleGameModePrefabPath = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/Samples/Sample.PureUnity/Prefabs/UnitySampleGameMode.prefab";
        private const string SampleCameraManagerPrefabPath = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/Samples/Sample.PureUnity/Prefabs/UnitySampleCameraManager.prefab";
        private const string SampleScenePath = "Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/Samples/Sample.PureUnity/Scene/UnitySampleScene.unity";

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
                instance = new UnityActorLifetime().Create(definition.GameModeClass);
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
        public void WorldSettings_SerializationContainsNoUnusedAssetGuidFields()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            var serializedSettings = new SerializedObject(settings);
            string[] removedFields =
            {
                "gameModeAssetGuid",
                "playerControllerAssetGuid",
                "pawnAssetGuid",
                "playerStateAssetGuid",
                "cameraManagerAssetGuid",
                "spectatorPawnAssetGuid",
            };

            for (int i = 0; i < removedFields.Length; i++)
            {
                Assert.IsNull(
                    serializedSettings.FindProperty(removedFields[i]),
                    removedFields[i]);
            }
        }

        [Test]
        public void PureUnitySample_HasNoCinemachineOrUniversalRenderPipelineDependencies()
        {
            GameObject cameraManagerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SampleCameraManagerPrefabPath);
            Assert.IsNotNull(cameraManagerPrefab);
            AssertNoOptionalRenderingComponents(cameraManagerPrefab);

            Scene scene = EditorSceneManager.OpenScene(
                SampleScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    AssertNoOptionalRenderingComponents(roots[i]);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static void AssertNoOptionalRenderingComponents(GameObject root)
        {
            Assert.Zero(
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root),
                $"'{root.name}' contains a missing MonoBehaviour reference.");

            Component[] components = root.GetComponentsInChildren<Component>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                Assert.IsNotNull(component, $"'{root.name}' contains a missing component.");
                Type componentType = component.GetType();

                // URP auto-attaches these companion components to every Light and Camera when
                // the pipeline is active. They are not authored sample dependencies and must not
                // fail the PureUnity check.
                string componentName = componentType.Name;
                if (componentName == "UniversalAdditionalLightData" ||
                    componentName == "UniversalAdditionalCameraData")
                {
                    continue;
                }

                string componentNamespace = componentType.Namespace ?? string.Empty;
                Assert.IsFalse(
                    componentNamespace.StartsWith("Unity.Cinemachine", StringComparison.Ordinal),
                    componentType.FullName);
                Assert.IsFalse(
                    componentNamespace.StartsWith("UnityEngine.Rendering.Universal", StringComparison.Ordinal),
                    componentType.FullName);
            }
        }

        [Test]
        public void PureUnitySample_LeavesCameraOwnershipToSpawnedCameraManager()
        {
            Scene scene = EditorSceneManager.OpenScene(
                SampleScenePath,
                OpenSceneMode.Additive);
            try
            {
                int sceneCameraCount = 0;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    sceneCameraCount += roots[i]
                        .GetComponentsInChildren<Camera>(includeInactive: true)
                        .Length;
                }

                Assert.AreEqual(0, sceneCameraCount,
                    "The World-spawned CameraManager prefab is the sample's only camera owner.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        [Test]
        public void PureUnitySample_HasIndependentTerminalCleanupOwnerReferencedByHost()
        {
            Scene scene = EditorSceneManager.OpenScene(
                SampleScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameplayWorldHost host = null;
                GameplayWorldTerminalCleanupOwner cleanupOwner = null;
                int hostCount = 0;
                int cleanupOwnerCount = 0;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameplayWorldHost[] hosts = roots[i]
                        .GetComponentsInChildren<GameplayWorldHost>(includeInactive: true);
                    GameplayWorldTerminalCleanupOwner[] cleanupOwners = roots[i]
                        .GetComponentsInChildren<GameplayWorldTerminalCleanupOwner>(includeInactive: true);

                    hostCount += hosts.Length;
                    cleanupOwnerCount += cleanupOwners.Length;
                    if (hosts.Length > 0)
                    {
                        host = hosts[0];
                    }

                    if (cleanupOwners.Length > 0)
                    {
                        cleanupOwner = cleanupOwners[0];
                    }
                }

                Assert.AreEqual(1, hostCount, "The sample must contain exactly one gameplay world host.");
                Assert.AreEqual(
                    1,
                    cleanupOwnerCount,
                    "The sample must contain exactly one application-lifetime cleanup owner.");
                Assert.IsNotNull(host);
                Assert.IsNotNull(cleanupOwner);
                Assert.IsNull(
                    cleanupOwner.transform.parent,
                    "The cleanup owner must be an independent scene root so Host destruction cannot destroy it.");
                Assert.AreNotSame(host.gameObject, cleanupOwner.gameObject);

                var serializedHost = new SerializedObject(host);
                serializedHost.Update();
                SerializedProperty cleanupOwnerProperty =
                    serializedHost.FindProperty("terminalCleanupOwner");
                Assert.IsNotNull(cleanupOwnerProperty);
                Assert.AreSame(cleanupOwner, cleanupOwnerProperty.objectReferenceValue);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
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
        public void WorldDefinition_FailedLeaseRemainsQuarantinedUntilRetrySucceeds()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            var lease = new TestLease { FailuresRemaining = 1 };
            var resolver = new TestResolver
            {
                Asset = CreateComponent<Pawn>("ResolvedPawn"),
                Lease = lease,
            };
            WorldDefinition definition = settings
                .ResolveDefinitionAsync(resolver)
                .GetAwaiter()
                .GetResult();

            Assert.DoesNotThrow(definition.Dispose);
            Assert.IsFalse(definition.IsDisposed);
            Assert.AreEqual(1, definition.PendingLeaseCount);
            Assert.IsFalse(lease.Disposed);

            Assert.DoesNotThrow(definition.Dispose);
            Assert.IsTrue(definition.IsDisposed);
            Assert.Zero(definition.PendingLeaseCount);
            Assert.IsTrue(lease.Disposed);
            Assert.AreEqual(2, lease.DisposeCount);
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

        [Test]
        public void ResolveDefinition_InvalidResultDisposesReturnedLeaseBeforePropagatingFailure()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            var lease = new TestLease();
            var resolver = new TestResolver { Lease = lease };

            Assert.Throws<InvalidOperationException>(() =>
                settings.ResolveDefinitionAsync(resolver)
                    .GetAwaiter()
                    .GetResult());

            Assert.IsTrue(lease.Disposed);
            Assert.AreEqual(1, lease.DisposeCount);
        }

        [Test]
        public void ResolveDefinition_CancellationRollbackFailureTransfersRetryableQuarantine()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            using var cancellation = new CancellationTokenSource();
            var lease = new TestLease { FailuresRemaining = 1 };
            var resolver = new TestResolver
            {
                Asset = CreateComponent<Pawn>("ResolvedPawn"),
                Lease = lease,
                OnResolve = cancellation.Cancel,
            };

            WorldSettingsLeaseCleanupException failure =
                Assert.Throws<WorldSettingsLeaseCleanupException>(() =>
                    settings.ResolveDefinitionAsync(resolver, cancellation.Token)
                        .GetAwaiter()
                        .GetResult());

            Assert.IsInstanceOf<OperationCanceledException>(failure.ResolutionFailure);
            Assert.AreEqual(1, failure.PendingLeaseCount);
            WorldSettingsLeaseQuarantine quarantine = failure.TakeLeaseQuarantine();
            Assert.IsFalse(quarantine.IsDisposed);
            Assert.Throws<InvalidOperationException>(() => failure.TakeLeaseQuarantine());

            quarantine.Dispose();

            Assert.IsTrue(quarantine.IsDisposed);
            Assert.Zero(failure.PendingLeaseCount);
            Assert.AreEqual(2, lease.DisposeCount);
        }

        [Test]
        public void ResolveDefinition_RollbackOutOfMemoryTriesAllLeasesAndTransfersQuarantine()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences();
            SetSource("pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString("pawnAssetLocation", "world/pawn");
            using var cancellation = new CancellationTokenSource();
            var cleanupOutOfMemory = new OutOfMemoryException(
                "Lease cleanup out-of-memory requested by the test.");
            var lease = new TestLease
            {
                FailuresRemaining = 1,
                Failure = cleanupOutOfMemory,
            };
            var resolver = new TestResolver
            {
                Asset = CreateComponent<Pawn>("ResolvedPawn"),
                Lease = lease,
                OnResolve = cancellation.Cancel,
            };

            WorldSettingsLeaseCleanupOutOfMemoryException failure =
                Assert.Throws<WorldSettingsLeaseCleanupOutOfMemoryException>(() =>
                    settings.ResolveDefinitionAsync(resolver, cancellation.Token)
                        .GetAwaiter()
                        .GetResult());

            Assert.AreSame(cleanupOutOfMemory, failure.CleanupFailure);
            Assert.IsInstanceOf<OperationCanceledException>(failure.ResolutionFailure);
            Assert.AreEqual(1, failure.PendingLeaseCount);
            WorldSettingsLeaseQuarantine quarantine = failure.TakeLeaseQuarantine();

            quarantine.Dispose();

            Assert.IsTrue(quarantine.IsDisposed);
            Assert.Zero(failure.PendingLeaseCount);
            Assert.AreEqual(2, lease.DisposeCount);
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
                IWorldSettingsLeaseRegistrar leaseRegistrar,
                CancellationToken cancellationToken) where T : Object
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastLocation = location;
                T typedAsset = Asset as T;
                leaseRegistrar.Register(Lease);
                OnResolve?.Invoke();
                return UniTask.FromResult(typedAsset != null
                    ? new WorldSettingsAssetLoadResult<T>(true, typedAsset, null)
                    : new WorldSettingsAssetLoadResult<T>(false, null, "Missing test asset."));
            }
        }

        private sealed class TestLease : IDisposable
        {
            public bool Disposed { get; private set; }
            public int DisposeCount { get; private set; }
            public int DisposeThreadId { get; private set; }
            public int FailuresRemaining { get; set; }
            public Exception Failure { get; set; }

            public void Dispose()
            {
                DisposeCount++;
                DisposeThreadId = Thread.CurrentThread.ManagedThreadId;
                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    throw Failure ?? new InvalidOperationException(
                        "Lease cleanup failure requested by the test.");
                }

                Disposed = true;
            }
        }

        private sealed class WorkerFaultResolver : IWorldSettingsReferenceResolver
        {
            private readonly IDisposable lease;

            public WorkerFaultResolver(Object successfulAsset, IDisposable lease)
            {
                _ = successfulAsset;
                this.lease = lease;
            }

            public bool Supports(WorldSettingsReferenceSource source)
            {
                return source == WorldSettingsReferenceSource.PathLocation;
            }

            public async UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
                string location,
                IWorldSettingsLeaseRegistrar leaseRegistrar,
                CancellationToken cancellationToken) where T : Object
            {
                cancellationToken.ThrowIfCancellationRequested();
                leaseRegistrar.Register(lease);
                await UniTask.SwitchToThreadPool();
                throw new InvalidOperationException(
                    "Worker resolver failure requested by test.");
            }
        }
    }
}
