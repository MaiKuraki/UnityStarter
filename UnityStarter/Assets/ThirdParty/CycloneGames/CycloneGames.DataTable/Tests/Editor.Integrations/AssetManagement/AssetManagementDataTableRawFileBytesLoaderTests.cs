using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DataTable.Tests.Editor.Integrations.AssetManagement
{
    public sealed class AssetManagementDataTableRawFileBytesLoaderTests
    {
        [Test]
        public void LoadAsync_TransfersCallerOwnedHandleSnapshotWithoutASecondFullCopy()
        {
            var package = new TestRawFilePackage();
            package.Add("Tables/items.bytes", new byte[] { 3, 5, 8, 13 });

            using (var loader = CreateLoader(package))
            {
                loader.LoadAsync("items").GetAwaiter().GetResult();

                ReadOnlyMemory<byte> cached = loader.GetBytes("items");
                Assert.That(MemoryMarshal.TryGetArray(cached, out ArraySegment<byte> segment), Is.True);
                Assert.That(segment.Array, Is.SameAs(package.LastSnapshot));
                Assert.That(package.ReadCount, Is.EqualTo(1));
                Assert.That(package.LastHandle.IsDisposed, Is.True);
            }
        }

        [Test]
        public void BatchLoad_WhenLaterHandleFails_RollsBackEveryPayloadAddedByTheBatch()
        {
            var package = new TestRawFilePackage();
            package.Add("Tables/first.bytes", new byte[] { 1 });
            package.AddFailure("Tables/second.bytes", "Synthetic provider failure.");

            using (var loader = CreateLoader(package))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    loader.LoadAsync(new[] { "first", "second" }).GetAwaiter().GetResult());

                Assert.That(loader.Count, Is.Zero);
                Assert.That(loader.TryGetBytes("first", out _), Is.False);
            }
        }

        [Test]
        public void BatchLoad_DuplicateRequestedName_LoadsAndOwnsOneSnapshot()
        {
            var package = new TestRawFilePackage();
            package.Add("Tables/items.bytes", new byte[] { 21 });

            using (var loader = CreateLoader(package))
            {
                loader.LoadAsync(new[] { "items", "items.bytes" }).GetAwaiter().GetResult();

                Assert.That(loader.Count, Is.EqualTo(1));
                Assert.That(package.ReadCount, Is.EqualTo(1));
                Assert.That(loader.GetBytes("items").Span[0], Is.EqualTo(21));
            }
        }

        [Test]
        public void LoadAsync_TaskSuccessTreatsProviderErrorTextAsDiagnosticOnly()
        {
            var package = new TestRawFilePackage();
            package.AddWithDiagnostic(
                "Tables/items.bytes",
                new byte[] { 34 },
                "Synthetic non-terminal provider diagnostic.");

            using (var loader = CreateLoader(package))
            {
                loader.LoadAsync("items").GetAwaiter().GetResult();

                Assert.That(loader.GetBytes("items").Span[0], Is.EqualTo(34));
            }
        }

        [Test]
        public void LoadAsync_WhenHandleDisposalFails_RetainsOwnershipUntilExplicitRetry()
        {
            var package = new TestRawFilePackage();
            package.Add("Tables/items.bytes", new byte[] { 55 });
            package.FailNextHandleDisposals(1);
            var loader = CreateLoader(package);
            package.HandleDisposing = () => loader.RetryPendingHandleDisposal();
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    loader.LoadAsync("items").GetAwaiter().GetResult());

                Assert.That(loader.HasPendingHandleDisposal, Is.True);
                Assert.That(loader.TryGetBytes("items", out _), Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    loader.LoadAsync("items").GetAwaiter().GetResult());

                loader.RetryPendingHandleDisposal();

                Assert.That(loader.HasPendingHandleDisposal, Is.False);
                Assert.That(package.LastHandle.IsDisposed, Is.True);
                loader.LoadAsync("items").GetAwaiter().GetResult();
                Assert.That(loader.GetBytes("items").Span[0], Is.EqualTo(55));
            }
            finally
            {
                loader.Dispose();
            }
        }

        private static Unity.Integrations.AssetManagement.AssetManagementDataTableRawFileBytesLoader CreateLoader(
            IAssetPackage package)
        {
            return new Unity.Integrations.AssetManagement.AssetManagementDataTableRawFileBytesLoader(
                package,
                bucketName: "Tests.DataTable",
                locationResolver: new DataTableLocationResolver("Tables"),
                initialCapacity: 2,
                limits: new DataTableLoadLimits(4, 32, 64, 16, 64));
        }

        private sealed class TestRawFilePackage : IAssetPackage, IAssetRawFileLoader
        {
            private readonly Dictionary<string, PayloadResult> _payloads =
                new Dictionary<string, PayloadResult>(StringComparer.Ordinal);
            private int _nextDisposeFailures;

            public Action HandleDisposing { get; set; }

            public string Name => "DataTable.Tests";

            public byte[] LastSnapshot { get; private set; }

            public TestRawFileHandle LastHandle { get; private set; }

            public int ReadCount { get; private set; }

            public void Add(string location, byte[] payload)
            {
                _payloads.Add(location, new PayloadResult(payload, string.Empty));
            }

            public void AddFailure(string location, string error)
            {
                _payloads.Add(location, new PayloadResult(null, error));
            }

            public void AddWithDiagnostic(string location, byte[] payload, string diagnostic)
            {
                _payloads.Add(location, new PayloadResult(payload, diagnostic));
            }

            public void FailNextHandleDisposals(int count)
            {
                _nextDisposeFailures = count;
            }

            public UniTask<bool> InitializeAsync(
                AssetPackageInitOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(true);
            }

            public UniTask DestroyAsync()
            {
                return UniTask.CompletedTask;
            }

            public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(
                string location,
                string bucket = null,
                string tag = null,
                string owner = null,
                CancellationToken cancellationToken = default)
                where TAsset : UnityEngine.Object
            {
                throw new NotSupportedException();
            }

            public IInstantiateHandle InstantiateAsync(
                IAssetHandle<GameObject> handle,
                Transform parent = null,
                bool worldPositionStays = false,
                bool setActive = true)
            {
                throw new NotSupportedException();
            }

            public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object
            {
                return false;
            }

            public void SetCacheIdleMemoryBudget(long maxIdleBytes)
            {
            }

            public int TrimIdleCache(AssetCacheRetentionPolicy policy)
            {
                return 0;
            }

            public void ClearBucket(string bucket)
            {
            }

            public void ClearBucketsByPrefix(string bucketPrefix)
            {
            }

            public IRawFileHandle LoadRawFileSync(
                string location,
                string bucket = null,
                string tag = null,
                string owner = null)
            {
                return CreateHandle(location);
            }

            public IRawFileHandle LoadRawFileAsync(
                string location,
                string bucket = null,
                string tag = null,
                string owner = null,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CreateHandle(location);
            }

            private IRawFileHandle CreateHandle(string location)
            {
                if (!_payloads.TryGetValue(location, out PayloadResult result))
                {
                    result = new PayloadResult(null, "Missing test payload.");
                }

                LastHandle = new TestRawFileHandle(
                    result,
                    OnSnapshotRead,
                    _nextDisposeFailures,
                    HandleDisposing);
                _nextDisposeFailures = 0;
                return LastHandle;
            }

            private void OnSnapshotRead(byte[] snapshot)
            {
                LastSnapshot = snapshot;
                ReadCount++;
            }
        }

        private sealed class TestRawFileHandle : IRawFileHandle
        {
            private readonly PayloadResult _result;
            private readonly Action<byte[]> _onSnapshotRead;
            private readonly Action _onDisposing;
            private int _disposeFailuresRemaining;

            public TestRawFileHandle(
                PayloadResult result,
                Action<byte[]> onSnapshotRead,
                int disposeFailuresRemaining,
                Action onDisposing)
            {
                _result = result;
                _onSnapshotRead = onSnapshotRead;
                _disposeFailuresRemaining = disposeFailuresRemaining;
                _onDisposing = onDisposing;
            }

            public string FilePath => string.Empty;

            public bool IsDone => true;

            public float Progress => 1f;

            public string Error => _result.Error;

            public UniTask Task => UniTask.CompletedTask;

            public bool IsDisposed { get; private set; }

            public string ReadText()
            {
                return string.Empty;
            }

            public byte[] ReadBytes()
            {
                if (_result.Payload == null)
                {
                    return null;
                }

                byte[] snapshot = (byte[])_result.Payload.Clone();
                _onSnapshotRead(snapshot);
                return snapshot;
            }

            public void WaitForAsyncComplete()
            {
            }

            public void Dispose()
            {
                _onDisposing?.Invoke();
                if (_disposeFailuresRemaining > 0)
                {
                    _disposeFailuresRemaining--;
                    throw new InvalidOperationException("Synthetic raw-handle disposal failure.");
                }

                IsDisposed = true;
            }
        }

        private readonly struct PayloadResult
        {
            public PayloadResult(byte[] payload, string error)
            {
                Payload = payload;
                Error = error;
            }

            public byte[] Payload { get; }

            public string Error { get; }
        }
    }
}
