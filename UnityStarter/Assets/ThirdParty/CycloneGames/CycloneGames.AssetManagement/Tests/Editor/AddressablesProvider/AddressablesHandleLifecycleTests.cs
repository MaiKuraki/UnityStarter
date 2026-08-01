using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using Cysharp.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AddressablesHandleLifecycleTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class ThrowingDestroyOperation : AsyncOperationBase<GameObject>
        {
            public int DestroyCallCount { get; private set; }

            protected override void Execute()
            {
                Complete(null, true, (string)null);
            }

            protected override void Destroy()
            {
                DestroyCallCount++;
                throw new InvalidOperationException("Synthetic provider destroy failure after reference-count mutation.");
            }
        }

        [Test]
        public void ShutdownRejectedDuringMaintenance_KeepsSceneUnloadObservation()
        {
            AssetRuntime.AddressablesAssetPackage package =
                CreateUninitialized<AssetRuntime.AddressablesAssetPackage>();
            SetField(package, "_sceneUnloadSubscribed", true);
            SetField(package, "_maintenanceMutationInProgress", true);

            Assert.Throws<InvalidOperationException>(() => package.DestroyAsync());

            Assert.That(GetField<bool>(package, "_sceneUnloadSubscribed"), Is.True);
        }

        [UnityTest]
        public IEnumerator SuccessfulShutdown_UnsubscribesSceneUnloadObservation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var package = new AssetRuntime.AddressablesAssetPackage(
                    "AddressablesSuccessfulShutdownSubscriptionTests");
                try
                {
                    Assert.That(
                        await package.InitializeAsync(new AssetRuntime.AssetPackageInitOptions()),
                        Is.True);
                    Assert.That(GetField<bool>(package, "_sceneUnloadSubscribed"), Is.True);

                    await package.DestroyAsync();

                    Assert.That(GetField<bool>(package, "_sceneUnloadSubscribed"), Is.False);
                }
                finally
                {
                    ForceUnsubscribe(package);
                }
            });
        }

        [Test]
        public async Task IndeterminateRelease_IsNotReissuedAndPackageCleanupStaysFailed()
        {
            var package = new AssetRuntime.AddressablesAssetPackage(
                "AddressablesIndeterminateReleaseTests");
            await package.InitializeAsync(new AssetRuntime.AssetPackageInitOptions());

            var resourceManager = new ResourceManager();
            var operation = new ThrowingDestroyOperation();
            AsyncOperationHandle<GameObject> raw = resourceManager.StartOperation(
                operation,
                default);
            AssetRuntime.AddressableInstantiateHandle handle = CreateInstantiateHandle(raw);
            Dictionary<long, AssetRuntime.AddressableInstantiateHandle> registry =
                GetField<Dictionary<long, AssetRuntime.AddressableInstantiateHandle>>(
                    package,
                    "_instantiateHandles");
            registry.Add(9_300_001L, handle);

            try
            {
                AggregateException firstFailure = Assert.ThrowsAsync<AggregateException>(
                    async () => await package.DestroyAsync());
                Assert.That(firstFailure, Is.Not.Null);
                Exception firstReleaseFailure = firstFailure.Flatten().InnerExceptions[0];
                Assert.That(GetField<bool>(package, "_sceneUnloadSubscribed"), Is.True);
                Assert.That(operation.DestroyCallCount, Is.EqualTo(1));
                Assert.That(raw.IsValid(), Is.True);
                Assert.That(GetProviderReferenceCount(raw), Is.Zero);

                AggregateException retryFailure = Assert.ThrowsAsync<AggregateException>(
                    async () => await package.DestroyAsync());
                Assert.That(retryFailure, Is.Not.Null);
                Assert.That(
                    ReferenceEquals(
                        firstReleaseFailure,
                        retryFailure.Flatten().InnerExceptions[0]),
                    Is.True,
                    "Cleanup retry must replay the retained diagnostic without calling Addressables.Release again.");
                Assert.That(operation.DestroyCallCount, Is.EqualTo(1));
                Assert.That(registry.ContainsKey(9_300_001L), Is.True);
                Assert.That(GetField<bool>(package, "_sceneUnloadSubscribed"), Is.True);
                Assert.That(
                    handle.Error,
                    Does.Contain("release outcome is indeterminate"));
            }
            finally
            {
                ForceUnsubscribe(package);
            }
        }

        [Test]
        public void ProviderBackedGetters_RequireUnityMainThread()
        {
            AssetRuntime.AddressableAssetHandle<Texture2D> asset =
                CreateUninitialized<AssetRuntime.AddressableAssetHandle<Texture2D>>();
            AssetRuntime.AddressableAllAssetsHandle<Texture2D> allAssets =
                CreateUninitialized<AssetRuntime.AddressableAllAssetsHandle<Texture2D>>();
            AssetRuntime.AddressableInstantiateHandle instance =
                CreateUninitialized<AssetRuntime.AddressableInstantiateHandle>();

            AssertRequiresMainThread(() => { _ = asset.Progress; });
            AssertRequiresMainThread(() => { _ = asset.Error; });
            AssertRequiresMainThread(() => { _ = asset.Asset; });
            AssertRequiresMainThread(() => { _ = asset.AssetObject; });
            AssertRequiresMainThread(() => { _ = allAssets.Progress; });
            AssertRequiresMainThread(() => { _ = allAssets.Error; });
            AssertRequiresMainThread(() => { _ = allAssets.Assets; });
            AssertRequiresMainThread(() => { _ = instance.Progress; });
            AssertRequiresMainThread(() => { _ = instance.Error; });
            AssertRequiresMainThread(() => { _ = instance.Instance; });
        }

        private static AssetRuntime.AddressableInstantiateHandle CreateInstantiateHandle(
            AsyncOperationHandle<GameObject> raw)
        {
            MethodInfo create = typeof(AssetRuntime.AddressableInstantiateHandle).GetMethod(
                "Create",
                AnyStatic);
            Assert.That(create, Is.Not.Null);
            ParameterInfo[] parameters = create.GetParameters();
            object operationTails = Activator.CreateInstance(
                parameters[parameters.Length - 1].ParameterType,
                new object[] { false });

            return (AssetRuntime.AddressableInstantiateHandle)create.Invoke(
                null,
                new object[]
                {
                    9_300_001L,
                    raw,
                    true,
                    null,
                    operationTails
                });
        }

        private static void AssertRequiresMainThread(Action getter)
        {
            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await Task.Run(getter));
            Assert.That(exception.Message, Does.Contain("Unity main thread"));
        }

        private static int GetProviderReferenceCount(AsyncOperationHandle<GameObject> handle)
        {
            PropertyInfo referenceCount = typeof(AsyncOperationHandle<GameObject>).GetProperty(
                "ReferenceCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(referenceCount, Is.Not.Null);
            return (int)referenceCount.GetValue(handle);
        }

        private static void ForceUnsubscribe(AssetRuntime.AddressablesAssetPackage package)
        {
            MethodInfo unsubscribe = typeof(AssetRuntime.AddressablesAssetPackage).GetMethod(
                "UnsubscribeFromSceneUnloads",
                PrivateInstance);
            Assert.That(unsubscribe, Is.Not.Null);
            unsubscribe.Invoke(package, null);
        }

        private static T CreateUninitialized<T>() where T : class
        {
#pragma warning disable SYSLIB0050
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
        }

        private static T GetField<T>(object target, string fieldName)
        {
            return (T)GetFieldInfo(target, fieldName).GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            GetFieldInfo(target, fieldName).SetValue(target, value);
        }

        private static FieldInfo GetFieldInfo(object target, string fieldName)
        {
            Type type = target.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, PrivateInstance);
                type = type.BaseType;
            }

            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            return field;
        }
    }
}
