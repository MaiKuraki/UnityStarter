using System;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Samples;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    /// <summary>
    /// Behavioural coverage for the delegated sample provider.
    /// </summary>
    [TestFixture]
    public sealed class DelegatedUIWindowAssetProviderTests
    {
        private UIWindowConfiguration _configuration;
        private GameObject _prefab;
        private UIWindowConfiguration _configurationResult;
        private GameObject _prefabResult;
        private bool _throwOnLoad;
        private string _lastConfigurationLocation;
        private string _lastPrefabLocation;
        private UIAssetLoadContext _lastConfigurationContext;
        private UIAssetLoadContext _lastPrefabContext;

        [SetUp]
        public void SetUp()
        {
            _configuration = ScriptableObject.CreateInstance<UIWindowConfiguration>();
            _prefab = new GameObject("DelegatedProviderTestPrefab");
            _configurationResult = _configuration;
            _prefabResult = _prefab;
            _throwOnLoad = false;
            _lastConfigurationLocation = null;
            _lastPrefabLocation = null;
            _lastConfigurationContext = default;
            _lastPrefabContext = default;
        }

        [TearDown]
        public void TearDown()
        {
            if (_configuration != null)
            {
                UnityEngine.Object.DestroyImmediate(_configuration);
            }

            if (_prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(_prefab);
            }
        }

        private DelegatedUIWindowAssetProvider CreateProvider()
        {
            return new DelegatedUIWindowAssetProvider(
                (location, context, token) =>
                {
                    _lastConfigurationLocation = location;
                    _lastConfigurationContext = context;
                    if (_throwOnLoad)
                    {
                        throw new InvalidOperationException("Host load delegate failed.");
                    }

                    return UniTask.FromResult(_configurationResult);
                },
                (location, context, token) =>
                {
                    _lastPrefabLocation = location;
                    _lastPrefabContext = context;
                    return UniTask.FromResult(_prefabResult);
                });
        }

        [Test]
        public async Task ConfigurationLease_ReturnsHostAsset()
        {
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            IUIAssetLease<UIWindowConfiguration> lease = await provider.AcquireConfigurationAsync(
                "Inventory", default, CancellationToken.None);

            Assert.AreSame(_configuration, lease.Asset);
            lease.Dispose();
        }

        [Test]
        public async Task PrefabLease_ReturnsHostAssetAndForwardsLocation()
        {
            var reference = new UIAssetReference("UI/Windows/Inventory");
            var context = new UIAssetLoadContext("ui", "frontend", "game-client");
            DelegatedUIWindowAssetProvider provider = CreateProvider();

            IUIAssetLease<GameObject> lease = await provider.AcquirePrefabAsync(
                reference, context, CancellationToken.None);

            Assert.AreSame(_prefab, lease.Asset);
            Assert.AreEqual("UI/Windows/Inventory", _lastPrefabLocation);
            Assert.AreEqual("ui", _lastPrefabContext.ConfigBucket);
            Assert.AreEqual("frontend", _lastPrefabContext.ConfigTag);
            Assert.AreEqual("game-client", _lastPrefabContext.ConfigOwner);
            lease.Dispose();
        }

        [Test]
        public async Task ConfigurationLoad_ForwardsWindowIdAndContext()
        {
            var context = new UIAssetLoadContext("cfgBucket", "cfgTag", "cfgOwner");
            DelegatedUIWindowAssetProvider provider = CreateProvider();

            IUIAssetLease<UIWindowConfiguration> lease = await provider.AcquireConfigurationAsync(
                "MainMenu", context, CancellationToken.None);

            Assert.AreEqual("MainMenu", _lastConfigurationLocation);
            Assert.AreEqual("cfgBucket", _lastConfigurationContext.ConfigBucket);
            Assert.AreEqual("cfgTag", _lastConfigurationContext.ConfigTag);
            Assert.AreEqual("cfgOwner", _lastConfigurationContext.ConfigOwner);
            lease.Dispose();
        }

        [Test]
        public void EmptyWindowId_ThrowsSynchronously()
        {
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            Assert.Throws<ArgumentException>(() =>
                provider.AcquireConfigurationAsync("  ", default, CancellationToken.None));
        }

        [Test]
        public void OverlongLocation_ThrowsSynchronously()
        {
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            string tooLong = new string('x', 1025);
            Assert.Throws<ArgumentException>(() =>
                provider.AcquireConfigurationAsync(tooLong, default, CancellationToken.None));
        }

        [Test]
        public void InvalidPrefabReference_ThrowsSynchronously()
        {
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            Assert.Throws<ArgumentException>(() =>
                provider.AcquirePrefabAsync(default, default, CancellationToken.None));
        }

        [Test]
        public void NullAssetFromHost_ReportsFailedOpen()
        {
            _configurationResult = null;
            DelegatedUIWindowAssetProvider provider = CreateProvider();

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await provider.AcquireConfigurationAsync(
                    "Inventory", default, CancellationToken.None);
            });
        }

        [Test]
        public void HostDelegateException_Propagates()
        {
            _throwOnLoad = true;
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            Assert.Throws<InvalidOperationException>(() =>
                provider.AcquireConfigurationAsync("Inventory", default, CancellationToken.None));
        }

        [Test]
        public void PreCancelledToken_ThrowsSynchronously()
        {
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    provider.AcquireConfigurationAsync("Inventory", default, cts.Token));
            }
        }

        [Test]
        public async Task Lease_DisposeIsIdempotentAndDoesNotDestroyAsset()
        {
            DelegatedUIWindowAssetProvider provider = CreateProvider();
            IUIAssetLease<UIWindowConfiguration> lease = await provider.AcquireConfigurationAsync(
                "Inventory", default, CancellationToken.None);

            Assert.DoesNotThrow(() => lease.Dispose());
            Assert.DoesNotThrow(() => lease.Dispose());
            Assert.IsNotNull(_configuration);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                UIWindowConfiguration unused = lease.Asset;
            });
        }

        [Test]
        public void Constructor_RejectsNullDelegates()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DelegatedUIWindowAssetProvider(
                    null,
                    (location, context, token) => UniTask.FromResult(_prefab)));
            Assert.Throws<ArgumentNullException>(() =>
                new DelegatedUIWindowAssetProvider(
                    (location, context, token) => UniTask.FromResult(_configuration),
                    null));
        }

        [Test]
        public void SteadyStateAllocation_IsBounded()
        {

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            object control = new object();
            GC.KeepAlive(control);
            if (GC.GetAllocatedBytesForCurrentThread() - controlBefore == 0)
            {
                Assert.Ignore(
                    "GC.GetAllocatedBytesForCurrentThread() is unavailable in this runtime; " +
                    "measure the delegated provider in a Player build or with the Unity Profiler.");
            }

            DelegatedUIWindowAssetProvider provider = CreateProvider();
            for (int i = 0; i < 100; i++)
            {
                provider.AcquireConfigurationAsync("Inventory", default, CancellationToken.None)
                    .GetAwaiter().GetResult().Dispose();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetAllocatedBytesForCurrentThread();
            const int Iterations = 1000;
            for (int i = 0; i < Iterations; i++)
            {
                provider.AcquireConfigurationAsync("Inventory", default, CancellationToken.None)
                    .GetAwaiter().GetResult().Dispose();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.LessOrEqual(allocated / Iterations, 4096,
                "Steady-state acquisition must stay bounded; investigate new per-open allocations.");
        }
    }
}
