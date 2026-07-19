using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Collections.Generic;

using NUnit.Framework;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class YooAssetProviderPolicyTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void DownloadControls_EnforceQualifiedConcurrencyLimit()
        {
            MethodInfo validateMethod = typeof(AssetRuntime.YooAssetPackage).GetMethod(
                "ValidateDownloadControls",
                PrivateStatic);

            Assert.That(validateMethod, Is.Not.Null);
            Assert.DoesNotThrow(() => validateMethod.Invoke(null, new object[] { 1, 0 }));
            Assert.DoesNotThrow(() => validateMethod.Invoke(null, new object[] { 32, 0 }));

            AssertArgumentOutOfRange(validateMethod, 0, 0, "downloadingMaxNumber");
            AssertArgumentOutOfRange(validateMethod, 33, 0, "downloadingMaxNumber");
        }

        [Test]
        public void RawFileHandle_ReturnsOwnedSnapshotsAndClearsStateOnDispose()
        {
#pragma warning disable SYSLIB0050
            var handle = (AssetRuntime.YooRawFileHandle)FormatterServices.GetUninitializedObject(
                typeof(AssetRuntime.YooRawFileHandle));
#pragma warning restore SYSLIB0050

            FieldInfo bytesField = typeof(AssetRuntime.YooRawFileHandle).GetField(
                "_bytesSnapshot",
                PrivateInstance);
            FieldInfo textField = typeof(AssetRuntime.YooRawFileHandle).GetField(
                "_textSnapshot",
                PrivateInstance);
            FieldInfo overheadField = typeof(AssetRuntime.YooRawFileHandle).GetField(
                "SNAPSHOT_OBJECT_OVERHEAD_BYTES",
                PrivateStatic);
            MethodInfo estimateRuntimeBytesMethod = typeof(AssetRuntime.YooRawFileHandle).GetMethod(
                "CycloneGames.AssetManagement.Runtime.IAssetMemoryFootprint.EstimateRuntimeBytes",
                PrivateInstance);

            Assert.That(bytesField, Is.Not.Null);
            Assert.That(textField, Is.Not.Null);
            Assert.That(overheadField, Is.Not.Null);
            Assert.That(estimateRuntimeBytesMethod, Is.Not.Null);

            var source = new byte[] { 1, 2, 3, 4 };
            const string TextSnapshot = "snapshot";
            bytesField.SetValue(handle, source);
            textField.SetValue(handle, TextSnapshot);

            byte[] first = handle.ReadBytes();
            byte[] second = handle.ReadBytes();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(source));
            Assert.That(second, Is.Not.SameAs(source));
            Assert.That(second, Is.Not.SameAs(first));
            CollectionAssert.AreEqual(source, first);
            CollectionAssert.AreEqual(source, second);
            Assert.That(handle.ReadText(), Is.EqualTo(TextSnapshot));
            Assert.That(handle.FilePath, Is.Empty);
            long overheadBytes = (long)overheadField.GetRawConstantValue();
            Assert.That(
                estimateRuntimeBytesMethod.Invoke(handle, null),
                Is.EqualTo(overheadBytes + source.LongLength + (long)TextSnapshot.Length * sizeof(char)));

            first[0] = 99;
            Assert.That(second[0], Is.EqualTo(1));
            Assert.That(source[0], Is.EqualTo(1));

            handle.DisposeInternal();

            Assert.That(handle.ReadBytes(), Is.Null);
            Assert.That(handle.ReadText(), Is.Empty);
            Assert.That(bytesField.GetValue(handle), Is.Null);
            Assert.That(textField.GetValue(handle), Is.EqualTo(string.Empty));
            Assert.That(estimateRuntimeBytesMethod.Invoke(handle, null), Is.EqualTo(overheadBytes));
        }

        [Test]
        public void SceneHandle_ReportsManualActivationBarrierFromProviderProgress()
        {
#pragma warning disable SYSLIB0050
            var handle = (AssetRuntime.YooSceneHandle)FormatterServices.GetUninitializedObject(
                typeof(AssetRuntime.YooSceneHandle));
#pragma warning restore SYSLIB0050

            FieldInfo activationModeField = typeof(AssetRuntime.YooSceneHandle).GetField(
                "<ActivationMode>k__BackingField",
                PrivateInstance);
            FieldInfo activationStateField = typeof(AssetRuntime.YooSceneHandle).GetField(
                "_activationState",
                PrivateInstance);
            FieldInfo progressField = typeof(AssetRuntime.YooSceneHandle).GetField(
                "_progress",
                PrivateInstance);
            FieldInfo resumedField = typeof(AssetRuntime.YooSceneHandle).GetField(
                "_manualLoadResumed",
                PrivateInstance);

            Assert.That(activationModeField, Is.Not.Null);
            Assert.That(activationStateField, Is.Not.Null);
            Assert.That(progressField, Is.Not.Null);
            Assert.That(resumedField, Is.Not.Null);

            activationModeField.SetValue(handle, AssetRuntime.SceneActivationMode.Manual);
            activationStateField.SetValue(handle, AssetRuntime.SceneActivationState.Loading);
            progressField.SetValue(handle, 0.899f);
            Assert.That(handle.ActivationState, Is.EqualTo(AssetRuntime.SceneActivationState.Loading));
            Assert.That(handle.RequiresShutdownActivation, Is.True);

            progressField.SetValue(handle, 0.9f);
            Assert.That(
                handle.ActivationState,
                Is.EqualTo(AssetRuntime.SceneActivationState.WaitingForActivation));
            Assert.That(handle.RequiresShutdownActivation, Is.True);

            activationStateField.SetValue(handle, AssetRuntime.SceneActivationState.Loading);
            resumedField.SetValue(handle, true);
            Assert.That(handle.ActivationState, Is.EqualTo(AssetRuntime.SceneActivationState.Activated));
            Assert.That(handle.RequiresShutdownActivation, Is.False);
        }

        [Test]
        public void ScenePackage_TerminalUnloadIsIdempotentAndIgnoresCallerCancellation()
        {
            object ownerToken = new object();
            AssetRuntime.YooAssetPackage package = CreateUninitialized<AssetRuntime.YooAssetPackage>();
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            SetField(package, "_sceneOwnerToken", ownerToken);
            SetAutoProperty(handle, "OwnerToken", ownerToken);
            SetField(handle, "_disposed", 1);

            var cancelled = new CancellationToken(canceled: true);
            Assert.That(
                package.UnloadSceneAsync(handle, cancelled).Status,
                Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(
                package.UnloadSceneAsync(handle, cancelled).Status,
                Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
        }

        [Test]
        public void ScenePackage_TerminalUnloadRejectsDifferentOwnerGeneration()
        {
            AssetRuntime.YooAssetPackage package = CreateUninitialized<AssetRuntime.YooAssetPackage>();
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            SetField(package, "_sceneOwnerToken", new object());
            SetAutoProperty(handle, "OwnerToken", new object());
            SetField(handle, "_disposed", 1);

            Assert.Throws<ArgumentException>(() => package.UnloadSceneAsync(handle));
        }

        [Test]
        public void SceneHandle_TerminalActivationFailsFast()
        {
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            SetField(handle, "_disposed", 1);

            Assert.Throws<ObjectDisposedException>(() => handle.ActivateAsync());
        }

        [Test]
        public void SceneHandle_PreCancelledUnloadDoesNotCommitLifecycleState()
        {
            const long HandleId = 9_100_001L;
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            SetField(handle, "_id", HandleId);
            var observationHandle = new PersistentSceneObservationHandle();
            AssetRuntime.SceneTracker.Enabled = true;
            AssetRuntime.SceneTracker.Register(
                HandleId,
                "test",
                "YooAsset",
                "Scenes/Cancelled",
                null,
                UnityEngine.SceneManagement.LoadSceneMode.Additive,
                observationHandle);

            try
            {
                Assert.Throws<OperationCanceledException>(
                    () => handle.UnloadAsync(new CancellationToken(canceled: true)));
                Assert.That(handle.UnloadStarted, Is.False);

                var scenes = new List<AssetRuntime.SceneTracker.SceneInfo>();
                AssetRuntime.SceneTracker.CopyTrackedScenesTo(
                    scenes,
                    AssetRuntime.SceneTracker.Capacity);
                AssetRuntime.SceneTracker.SceneInfo info = scenes.Find(item => item.Id == HandleId);
                Assert.That(info.Id, Is.EqualTo(HandleId));
                Assert.That(info.UnloadRequested, Is.False);
                GC.KeepAlive(observationHandle);
            }
            finally
            {
                AssetRuntime.SceneTracker.Unregister(HandleId);
            }
        }

        [Test]
        public void SceneHandle_RejectsNewActivationAfterUnloadCommit()
        {
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            SetAutoProperty(handle, "ActivationMode", AssetRuntime.SceneActivationMode.Manual);
            SetField(handle, "_activationState", AssetRuntime.SceneActivationState.Loading);
            SetField(handle, "_unloadStarted", true);

            Assert.Throws<InvalidOperationException>(() => handle.ActivateAsync());
        }

        [Test]
        public void SceneHandle_CommittedUnloadJoinsAfterPackageShutdown()
        {
            object ownerToken = new object();
            AssetRuntime.YooAssetPackage package = CreateUninitialized<AssetRuntime.YooAssetPackage>();
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            var completion = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            var registry = new Dictionary<long, AssetRuntime.YooSceneHandle>
            {
                [9_100_002L] = handle
            };
            SetField(package, "_sceneOwnerToken", ownerToken);
            SetField(package, "_sceneHandles", registry);
            SetField(package, "_shutdownRequested", 1);
            SetAutoProperty(handle, "OwnerToken", ownerToken);
            SetField(handle, "_id", 9_100_002L);
            SetField(handle, "_unloadStarted", true);
            SetField(handle, "_unloadTask", completion.Task);

            Cysharp.Threading.Tasks.UniTask joined = package.UnloadSceneAsync(
                handle,
                new CancellationToken(canceled: true));

            Assert.That(joined.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Pending));
            completion.TrySetResult();
            Assert.That(joined.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(registry, Is.Empty);
        }

        [Test]
        public void SceneHandle_UnloadJoinsCommittedActivationBeforeProviderRetirement()
        {
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            var activation = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            SetField(handle, "_id", 9_100_003L);
            SetField(handle, "_activationStarted", true);
            SetField(handle, "_activationTask", activation.Task);

            Cysharp.Threading.Tasks.UniTask unload = handle.UnloadAsync(CancellationToken.None);

            Assert.That(unload.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Pending));
            activation.TrySetResult();
            Assert.That(unload.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(handle.IsTerminallyReleased, Is.True);
        }

        [Test]
        public void SceneHandle_FailedCommittedActivationStillConvergesAfterProviderRelease()
        {
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            var activation = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            activation.TrySetException(new InvalidOperationException("Activation interrupted by scene unload."));
            SetField(handle, "_id", 9_100_004L);
            SetField(handle, "_activationStarted", true);
            SetField(handle, "_activationTask", activation.Task);
            SetField(handle, "_providerSceneUnloaded", true);

            Cysharp.Threading.Tasks.UniTask unload = handle.UnloadAsync(CancellationToken.None);

            Assert.That(unload.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(handle.IsTerminallyReleased, Is.True);
        }

        [Test]
        public void SceneUnloadCallback_RetiresWrapperAfterProviderReleasedBeforeSceneCapture()
        {
            const long HandleId = 9_100_005L;
            AssetRuntime.YooAssetPackage package = CreateUninitialized<AssetRuntime.YooAssetPackage>();
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            var registry = new Dictionary<long, AssetRuntime.YooSceneHandle>
            {
                [HandleId] = handle
            };
            SetField(package, "_sceneHandles", registry);
            SetField(package, "_sceneUnloadScratchIds", new List<long>());
            SetField(handle, "_id", HandleId);

            MethodInfo callback = typeof(AssetRuntime.YooAssetPackage).GetMethod(
                "OnSceneUnloaded",
                PrivateInstance);
            Assert.That(callback, Is.Not.Null);
            callback.Invoke(package, new object[] { default(UnityEngine.SceneManagement.Scene) });

            Assert.That(registry, Is.Empty);
            Assert.That(handle.IsTerminallyReleased, Is.True);
        }

        [Test]
        public void FailedErrorProviderScene_IsReleasedWithoutNativeUnloadOperation()
        {
            Assembly yooAssembly = typeof(YooAsset.YooAssets).Assembly;
            Type resourceManagerType = yooAssembly.GetType("YooAsset.ResourceManager", throwOnError: true);
            object resourceManager = Activator.CreateInstance(
                resourceManagerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { "SceneFailureFixture" },
                culture: null);

            ConstructorInfo assetInfoConstructor = typeof(YooAsset.AssetInfo).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string), typeof(string) },
                modifiers: null);
            Assert.That(assetInfoConstructor, Is.Not.Null);
            var assetInfo = (YooAsset.AssetInfo)assetInfoConstructor.Invoke(
                new object[] { "SceneFailureFixture", "Synthetic scene failure." });

            Type errorProviderType = yooAssembly.GetType("YooAsset.ErrorProvider", throwOnError: true);
            object errorProvider = Activator.CreateInstance(
                errorProviderType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new[] { resourceManager, assetInfo },
                culture: null);
            errorProviderType.GetMethod("SetCompletedWithError", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(errorProvider, new object[] { "Synthetic scene failure." });

            MethodInfo createHandleMethod = errorProviderType.GetMethod(
                "CreateHandle",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(createHandleMethod, Is.Not.Null);
            var raw = (YooAsset.SceneHandle)createHandleMethod
                .MakeGenericMethod(typeof(YooAsset.SceneHandle))
                .Invoke(errorProvider, null);
            Assert.That(raw.IsDone, Is.True);
            Assert.That(raw.Status, Is.EqualTo(YooAsset.EOperationStatus.Failed));

            AssetRuntime.YooSceneHandle handle = AssetRuntime.YooSceneHandle.Create(
                9_100_006L,
                new object(),
                "Scenes/Failed",
                raw,
                activateOnLoad: true);

            Cysharp.Threading.Tasks.UniTask unload = handle.UnloadAsync(CancellationToken.None);

            Assert.That(unload.Status, Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(handle.IsTerminallyReleased, Is.True);
            Assert.That(raw.IsValid, Is.False);
        }

        [Test]
        public void PackageShutdown_RejectsAnyUnresolvedManualBarrierInAnotherPackage()
        {
            var module = new AssetRuntime.YooAssetModule();
            AssetRuntime.YooAssetPackage targetPackage = CreateUninitialized<AssetRuntime.YooAssetPackage>();
            AssetRuntime.YooAssetPackage blockingPackage = CreateUninitialized<AssetRuntime.YooAssetPackage>();
            AssetRuntime.YooSceneHandle targetScene = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            AssetRuntime.YooSceneHandle blockingScene = CreateUninitialized<AssetRuntime.YooSceneHandle>();

            SetField(targetScene, "_id", 9_100_010L);
            SetField(blockingScene, "_id", 9_100_020L);
            SetAutoProperty(blockingScene, "ActivationMode", AssetRuntime.SceneActivationMode.Manual);
            SetField(targetPackage, "_sceneHandles", new Dictionary<long, AssetRuntime.YooSceneHandle>
            {
                [9_100_010L] = targetScene
            });
            SetField(blockingPackage, "_sceneHandles", new Dictionary<long, AssetRuntime.YooSceneHandle>
            {
                [9_100_020L] = blockingScene
            });

            FieldInfo packagesField = typeof(AssetRuntime.YooAssetModule).GetField("_packages", PrivateInstance);
            Assert.That(packagesField, Is.Not.Null);
            var packages = (Dictionary<string, AssetRuntime.YooAssetPackage>)packagesField.GetValue(module);
            packages.Add("Target", targetPackage);
            packages.Add("Blocking", blockingPackage);

            Assert.Throws<InvalidOperationException>(
                () => module.ValidatePackageSceneDrainOrder(targetPackage));

            SetField(blockingScene, "_manualLoadResumed", true);
            Assert.DoesNotThrow(() => module.ValidatePackageSceneDrainOrder(targetPackage));
        }

        [Test]
        public void CallerDispose_IsIdempotentAndDoesNotUnloadScene()
        {
            AssetRuntime.YooSceneHandle handle = CreateUninitialized<AssetRuntime.YooSceneHandle>();
            SetField(handle, "_refCount", 1);

            handle.Dispose();
            handle.Dispose();

            Assert.That(handle.RefCount, Is.Zero);
            Assert.That(handle.IsTerminallyReleased, Is.False);
        }

        [TestCase((AssetRuntime.SceneActivationMode)byte.MaxValue)]
        public void SceneParameters_RejectUndefinedActivationMode(
            AssetRuntime.SceneActivationMode activationMode)
        {
            MethodInfo validateMethod = typeof(AssetRuntime.YooAssetPackage).GetMethod(
                "ValidateSceneActivationMode",
                PrivateStatic);

            Assert.That(validateMethod, Is.Not.Null);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => validateMethod.Invoke(null, new object[] { activationMode }));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase((UnityEngine.SceneManagement.LoadSceneMode)int.MaxValue)]
        public void SceneParameters_RejectUndefinedLoadMode(
            UnityEngine.SceneManagement.LoadSceneMode loadMode)
        {
            MethodInfo validateMethod = typeof(AssetRuntime.YooAssetPackage).GetMethod(
                "ValidateSceneLoadMode",
                PrivateStatic);

            Assert.That(validateMethod, Is.Not.Null);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => validateMethod.Invoke(null, new object[] { loadMode }));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(UnityEngine.SceneManagement.LocalPhysicsMode.None)]
        [TestCase(UnityEngine.SceneManagement.LocalPhysicsMode.Physics2D)]
        [TestCase(UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D)]
        [TestCase(UnityEngine.SceneManagement.LocalPhysicsMode.Physics2D |
                  UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D)]
        public void SceneParameters_AcceptSupportedLocalPhysicsModeFlags(
            UnityEngine.SceneManagement.LocalPhysicsMode physicsMode)
        {
            MethodInfo validateMethod = typeof(AssetRuntime.YooAssetPackage).GetMethod(
                "ValidateLocalPhysicsMode",
                PrivateStatic);

            Assert.That(validateMethod, Is.Not.Null);
            Assert.DoesNotThrow(() => validateMethod.Invoke(null, new object[] { physicsMode }));
        }

        [TestCase((UnityEngine.SceneManagement.LocalPhysicsMode)4)]
        [TestCase((UnityEngine.SceneManagement.LocalPhysicsMode)(-1))]
        [TestCase((UnityEngine.SceneManagement.LocalPhysicsMode)int.MaxValue)]
        public void SceneParameters_RejectUndefinedLocalPhysicsMode(
            UnityEngine.SceneManagement.LocalPhysicsMode physicsMode)
        {
            MethodInfo validateMethod = typeof(AssetRuntime.YooAssetPackage).GetMethod(
                "ValidateLocalPhysicsMode",
                PrivateStatic);

            Assert.That(validateMethod, Is.Not.Null);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => validateMethod.Invoke(null, new object[] { physicsMode }));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        private static T CreateUninitialized<T>() where T : class
        {
#pragma warning disable SYSLIB0050
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static void SetAutoProperty(object target, string propertyName, object value)
        {
            SetField(target, $"<{propertyName}>k__BackingField", value);
        }

        private static void AssertArgumentOutOfRange(
            MethodInfo validateMethod,
            int downloadingMaxNumber,
            int failedTryAgain,
            string expectedParameterName)
        {
            TargetInvocationException invocationException = Assert.Throws<TargetInvocationException>(
                () => validateMethod.Invoke(
                    null,
                    new object[] { downloadingMaxNumber, failedTryAgain }));

            Assert.That(invocationException.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
            var rangeException = (ArgumentOutOfRangeException)invocationException.InnerException;
            Assert.That(rangeException.ParamName, Is.EqualTo(expectedParameterName));
        }

        private sealed class PersistentSceneObservationHandle : AssetRuntime.ISceneHandle
        {
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => string.Empty;
            public Cysharp.Threading.Tasks.UniTask Task => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public string ScenePath => string.Empty;
            public UnityEngine.SceneManagement.Scene Scene => default;
            public AssetRuntime.SceneActivationMode ActivationMode =>
                AssetRuntime.SceneActivationMode.ActivateOnLoad;
            public AssetRuntime.SceneActivationState ActivationState =>
                AssetRuntime.SceneActivationState.Activated;
            public bool SupportsManualActivation => false;

            public Cysharp.Threading.Tasks.UniTask ActivateAsync(
                CancellationToken cancellationToken = default) =>
                Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public void WaitForAsyncComplete()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
