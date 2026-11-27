using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using Cysharp.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class MultiAssetResultTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Location = "UI/Prompts/keyboard_sheet";
        private const string ProviderFailure = "Synthetic provider failure.";

        private sealed class CompletedListOperation<TAsset> : AsyncOperationBase<IList<TAsset>>
            where TAsset : UnityEngine.Object
        {
            private readonly IList<TAsset> _result;

            public CompletedListOperation(IList<TAsset> result)
            {
                _result = result;
            }

            protected override void Execute()
            {
                Complete(_result, true, (string)null);
            }
        }

        private sealed class FailedListOperation<TAsset> : AsyncOperationBase<IList<TAsset>>
            where TAsset : UnityEngine.Object
        {
            protected override void Execute()
            {
                Complete(null, false, ProviderFailure);
            }
        }

        private Texture2D[] _textures;

        [SetUp]
        public void SetUp()
        {
            _textures = new Texture2D[4];
            for (int i = 0; i < _textures.Length; i++)
            {
                _textures[i] = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _textures.Length; i++)
            {
                if (_textures[i] != null) UnityEngine.Object.DestroyImmediate(_textures[i]);
            }
        }

        [Test]
        public void EveryEntryAliasesOneObject_FaultsHandleAndHidesList()
        {
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            AsyncOperationHandle<IList<Texture2D>> raw = CreateHandle(out handle, List(0, 0, 0));

            Assert.That(handle.IsDone, Is.True);
            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await handle.Task);
            Assert.That(handle.Error, Does.Contain("3 entries"));
            Assert.That(handle.Error, Does.Contain("1 distinct object(s)"));
            Assert.That(handle.Error, Does.Contain(Location));
            Assert.That(handle.Assets.Count, Is.Zero);
            Assert.That(raw.IsValid(), Is.True, "The lease still owns the provider handle.");

            handle.Dispose();

            Assert.That(raw.IsValid(), Is.False);
            Assert.That(handle.RefCount, Is.Zero);
            Assert.That(((AssetRuntime.IAssetBackendLifetime)handle).IsDisposed, Is.True);
        }

        [Test]
        public void RepeatedEntryAmongDistinctEntries_FaultsHandle()
        {
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            CreateHandle(out handle, List(0, 1, 0, 2, 1));

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(handle.Error, Does.Contain("5 entries"));
            Assert.That(handle.Error, Does.Contain("3 distinct object(s)"));
            Assert.That(handle.Assets.Count, Is.Zero);

            handle.Dispose();
        }

        [Test]
        public void RepeatedLeadingEntries_FollowedByDistinctEntries_FaultsHandle()
        {
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            CreateHandle(out handle, List(0, 0, 1, 2, 3));

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(handle.Error, Does.Contain("5 entries"));
            Assert.That(handle.Error, Does.Contain("4 distinct object(s)"));
            Assert.That(handle.Assets.Count, Is.Zero);

            handle.Dispose();
        }

        [Test]
        public void RepeatedNullEntries_FaultsHandle()
        {
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            CreateHandle(out handle, new Texture2D[] { null, null, null });

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(handle.Error, Does.Contain("null or destroyed entry at index 0"));
            Assert.That(handle.Assets.Count, Is.Zero);

            handle.Dispose();
        }

        [Test]
        public void DestroyedEntry_FaultsHandle()
        {
            var destroyed = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            UnityEngine.Object.DestroyImmediate(destroyed);
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            CreateHandle(out handle, new[] { _textures[0], destroyed });

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(handle.Error, Does.Contain("null or destroyed entry at index 1"));
            Assert.That(handle.Assets.Count, Is.Zero);

            handle.Dispose();
        }

        [Test]
        public void FaultedHandle_ExposesNoAssetsAfterRelease()
        {
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            CreateHandle(out handle, List(0, 0));

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            handle.Dispose();
            Assert.That(handle.Assets.Count, Is.Zero);
            Assert.That(((AssetRuntime.IAssetBackendLifetime)handle).IsDisposed, Is.True);
            Assert.That(handle.RefCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void DistinctEntries_SucceedAndExposeEveryEntry(int count)
        {
            Texture2D[] expected = new Texture2D[count];
            for (int i = 0; i < count; i++) expected[i] = _textures[i];
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            CreateHandle(out handle, expected);

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(handle.Error, Is.Empty);
            Assert.That(handle.Assets.Count, Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.That(ReferenceEquals(handle.Assets[i], expected[i]), Is.True);
            }

            handle.Dispose();
        }

        [Test]
        public void ProviderFailure_ReportsTheProviderMessage()
        {
            AsyncOperationHandle<IList<Texture2D>> raw = Addressables.ResourceManager.StartOperation(
                new FailedListOperation<Texture2D>(),
                default);
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle = CreateHandle(
                raw,
                AssetRuntime.Cache.AssetCacheService.BuildCacheKey(
                    Location,
                    typeof(Texture2D),
                    AssetRuntime.AssetCacheEntryKind.MultipleAssets),
                null);

            Assert.That(handle.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(handle.Error, Is.EqualTo(ProviderFailure));
            Assert.That(handle.Assets.Count, Is.Zero);

            handle.Dispose();
        }

        [Test]
        public void FaultedMultiAssetHandle_NeverEntersIdleRetention()
        {
            AssetRuntime.AddressablesAssetPackage package = new AssetRuntime.AddressablesAssetPackage(
                "MultiAssetResultRetentionTests");
            AssetRuntime.Cache.AssetCacheService cache =
                (AssetRuntime.Cache.AssetCacheService)GetField(package, "_cacheService");
            AssetRuntime.Cache.AssetCacheKey key = AssetRuntime.Cache.AssetCacheService.BuildCacheKey(
                Location,
                typeof(Texture2D),
                AssetRuntime.AssetCacheEntryKind.MultipleAssets);
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            AsyncOperationHandle<IList<Texture2D>> raw = CreateHandle(out handle, List(0, 0, 0), cache.OnHandleReleased);
            cache.RegisterNew(key, null, null, null, handle);

            handle.Dispose();

            AssetRuntime.AssetRuntimeCacheSnapshot snapshot = cache.CreateRuntimeSnapshot(package.Name, "Addressables");
            Assert.That(raw.IsValid(), Is.False);
            Assert.That(snapshot.ActiveCount, Is.Zero);
            Assert.That(snapshot.IdleCount, Is.Zero);
            Assert.That(snapshot.FailedOperationRejectionCount, Is.EqualTo(1));
        }

        [Test]
        public void SharedLeases_ObserveOneFaultAndReleaseTheBackendOnce()
        {
            AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle;
            AsyncOperationHandle<IList<Texture2D>> raw = CreateHandle(out handle, List(0, 0));
            AssetRuntime.IMultiAssetHandle<Texture2D> first = AssetRuntime.AssetHandleLeases.Create<Texture2D>(handle);

            // The package cache retains the backend once for every additional caller lease.
            handle.Retain();
            AssetRuntime.IMultiAssetHandle<Texture2D> second = AssetRuntime.AssetHandleLeases.Create<Texture2D>(handle);

            Assert.That(first.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(second.Task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(first.Assets.Count, Is.Zero);
            Assert.That(second.Assets.Count, Is.Zero);

            first.Dispose();
            Assert.That(raw.IsValid(), Is.True, "A remaining lease must keep the backend alive.");

            second.Dispose();
            Assert.That(raw.IsValid(), Is.False);
        }

        private Texture2D[] List(params int[] textureIndices)
        {
            Texture2D[] items = new Texture2D[textureIndices.Length];
            for (int i = 0; i < textureIndices.Length; i++)
            {
                items[i] = _textures[textureIndices[i]];
            }

            return items;
        }

        private static AsyncOperationHandle<IList<Texture2D>> CreateHandle(
            out AssetRuntime.AddressableMultiAssetHandle<Texture2D> handle,
            IList<Texture2D> result,
            Action<AssetRuntime.Cache.AssetCacheKey, AssetRuntime.IReferenceCounted> onReleaseToCache = null)
        {
            AsyncOperationHandle<IList<Texture2D>> raw = Addressables.ResourceManager.StartOperation(
                new CompletedListOperation<Texture2D>(result),
                default);
            handle = CreateHandle(raw, AssetRuntime.Cache.AssetCacheService.BuildCacheKey(
                Location,
                typeof(Texture2D),
                AssetRuntime.AssetCacheEntryKind.MultipleAssets), onReleaseToCache);
            return raw;
        }

        private static AssetRuntime.AddressableMultiAssetHandle<Texture2D> CreateHandle(
            AsyncOperationHandle<IList<Texture2D>> raw,
            AssetRuntime.Cache.AssetCacheKey cacheKey,
            Action<AssetRuntime.Cache.AssetCacheKey, AssetRuntime.IReferenceCounted> onReleaseToCache)
        {
            return AssetRuntime.AddressableMultiAssetHandle<Texture2D>.Create(
                AssetRuntime.AssetRuntimeGuard.NextHandleId(),
                cacheKey,
                raw,
                AssetRuntime.MultiAssetResult.CreateScratch(),
                onReleaseToCache,
                new AssetRuntime.AssetOperationTailTracker());
        }

        private static object GetField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            return field.GetValue(target);
        }
    }
}
