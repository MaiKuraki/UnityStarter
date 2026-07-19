using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Collections.Generic;

using NUnit.Framework;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AddressablesScenePolicyTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void TerminalUnload_IsIdempotentAndIgnoresCallerCancellation()
        {
            object ownerToken = new object();
            AssetRuntime.AddressablesAssetPackage package =
                CreateUninitialized<AssetRuntime.AddressablesAssetPackage>();
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
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
        public void TerminalUnload_RejectsDifferentOwnerGeneration()
        {
            AssetRuntime.AddressablesAssetPackage package =
                CreateUninitialized<AssetRuntime.AddressablesAssetPackage>();
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            SetField(package, "_sceneOwnerToken", new object());
            SetAutoProperty(handle, "OwnerToken", new object());
            SetField(handle, "_disposed", 1);

            Assert.Throws<ArgumentException>(() => package.UnloadSceneAsync(handle));
        }

        [Test]
        public void TerminalActivation_FailsFast()
        {
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            SetField(handle, "_disposed", 1);

            Assert.Throws<ObjectDisposedException>(() => handle.ActivateAsync());
        }

        [Test]
        public void ShutdownBarrier_RequiresUnresolvedManualActivationBeforeUnload()
        {
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            SetAutoProperty(handle, "ActivationMode", AssetRuntime.SceneActivationMode.Manual);
            SetField(handle, "_activationState", AssetRuntime.SceneActivationState.Loading);

            Assert.That(handle.RequiresShutdownActivation, Is.True);

            SetField(handle, "_activationState", AssetRuntime.SceneActivationState.Activated);
            Assert.That(handle.RequiresShutdownActivation, Is.False);

            SetField(handle, "_disposed", 1);
            Assert.That(handle.RequiresShutdownActivation, Is.False);
        }

        [Test]
        public void PreCancelledUnload_DoesNotCommitLifecycleState()
        {
            const long HandleId = 9_200_001L;
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            SetField(handle, "Id", HandleId);
            AssetRuntime.SceneTracker.Enabled = true;
            AssetRuntime.SceneTracker.Register(
                HandleId,
                "test",
                "Addressables",
                "Scenes/Cancelled",
                null,
                UnityEngine.SceneManagement.LoadSceneMode.Additive,
                handle);

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
            }
            finally
            {
                AssetRuntime.SceneTracker.Unregister(HandleId);
            }
        }

        [Test]
        public void NewActivationAfterUnloadCommit_IsRejected()
        {
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            SetAutoProperty(handle, "ActivationMode", AssetRuntime.SceneActivationMode.Manual);
            SetField(handle, "_activationState", AssetRuntime.SceneActivationState.Loading);
            SetField(handle, "_unloadStarted", true);

            Assert.Throws<InvalidOperationException>(() => handle.ActivateAsync());
        }

        [Test]
        public void CommittedUnload_JoinsAfterPackageShutdown()
        {
            object ownerToken = new object();
            AssetRuntime.AddressablesAssetPackage package =
                CreateUninitialized<AssetRuntime.AddressablesAssetPackage>();
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            var completion = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            var registry = new Dictionary<long, AssetRuntime.AddressableSceneHandle>
            {
                [9_200_002L] = handle
            };
            SetField(package, "_sceneOwnerToken", ownerToken);
            SetField(package, "_sceneHandles", registry);
            SetField(package, "_shutdownRequested", 1);
            SetAutoProperty(handle, "OwnerToken", ownerToken);
            SetField(handle, "Id", 9_200_002L);
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
        public void ProviderReleasedDuringCommittedActivation_ConvergesToTerminalUnload()
        {
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            var activation = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            activation.TrySetException(new InvalidOperationException("Activation interrupted by scene unload."));
            SetField(handle, "Id", 9_200_003L);
            SetAutoProperty(handle, "ActivationMode", AssetRuntime.SceneActivationMode.Manual);
            SetField(handle, "_activationState", AssetRuntime.SceneActivationState.Loading);
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
            const long HandleId = 9_200_004L;
            AssetRuntime.AddressablesAssetPackage package =
                CreateUninitialized<AssetRuntime.AddressablesAssetPackage>();
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            var registry = new Dictionary<long, AssetRuntime.AddressableSceneHandle>
            {
                [HandleId] = handle
            };
            SetField(package, "_sceneHandles", registry);
            SetField(package, "_sceneUnloadScratchIds", new List<long>());
            SetField(handle, "Id", HandleId);

            MethodInfo callback = typeof(AssetRuntime.AddressablesAssetPackage).GetMethod(
                "OnSceneUnloaded",
                PrivateInstance);
            Assert.That(callback, Is.Not.Null);
            callback.Invoke(package, new object[] { default(UnityEngine.SceneManagement.Scene) });

            Assert.That(registry, Is.Empty);
            Assert.That(handle.IsTerminallyReleased, Is.True);
        }

        [Test]
        public void ActivationCompletion_RejectsRetiredProviderOwnership()
        {
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
            SetField(handle, "_providerSceneUnloaded", true);
            MethodInfo validateMethod = typeof(AssetRuntime.AddressableSceneHandle).GetMethod(
                "EnsureActivationCompletionStillOwned",
                PrivateInstance);
            Assert.That(validateMethod, Is.Not.Null);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => validateMethod.Invoke(handle, null));

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CallerDispose_IsIdempotentAndDoesNotUnloadScene()
        {
            AssetRuntime.AddressableSceneHandle handle =
                CreateUninitialized<AssetRuntime.AddressableSceneHandle>();
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
            MethodInfo validateMethod = typeof(AssetRuntime.AddressablesAssetPackage).GetMethod(
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
            MethodInfo validateMethod = typeof(AssetRuntime.AddressablesAssetPackage).GetMethod(
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
            MethodInfo validateMethod = typeof(AssetRuntime.AddressablesAssetPackage).GetMethod(
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
            MethodInfo validateMethod = typeof(AssetRuntime.AddressablesAssetPackage).GetMethod(
                "ValidateLocalPhysicsMode",
                PrivateStatic);

            Assert.That(validateMethod, Is.Not.Null);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => validateMethod.Invoke(null, new object[] { physicsMode }));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NativeContract_ProvidesLoadSceneParametersOverload()
        {
            MethodInfo method = typeof(UnityEngine.AddressableAssets.Addressables).GetMethod(
                nameof(UnityEngine.AddressableAssets.Addressables.LoadSceneAsync),
                new[]
                {
                    typeof(object),
                    typeof(UnityEngine.SceneManagement.LoadSceneParameters),
                    typeof(bool),
                    typeof(int)
                });

            Assert.That(method, Is.Not.Null);
        }

        private static T CreateUninitialized<T>() where T : class
        {
#pragma warning disable SYSLIB0050
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
        }

        private static void SetField(object target, string fieldName, object value)
        {
            Type type = target.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, PrivateInstance);
                type = type.BaseType;
            }
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static void SetAutoProperty(object target, string propertyName, object value)
        {
            SetField(target, $"<{propertyName}>k__BackingField", value);
        }
    }
}
