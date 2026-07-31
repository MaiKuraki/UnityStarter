using System;
using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using NUnit.Framework;

namespace CycloneGames.Choreography.Tests
{
    public sealed class ChoreographyMemoryBoundsTests
    {
        [Test]
        public void DefaultOptions_UseExplicitCompatibleCapacities()
        {
            ChoreographySchedulerOptions options = ChoreographySchedulerOptions.Default;

            Assert.That(options.MaximumActiveCount, Is.EqualTo(ChoreographySchedulerOptions.DefaultMaximumActiveCount));
            Assert.That(options.MaximumQueuedCount, Is.EqualTo(ChoreographySchedulerOptions.DefaultMaximumQueuedCount));
            Assert.That(options.MaximumRetainedPoolCount, Is.EqualTo(ChoreographySchedulerOptions.DefaultMaximumRetainedPoolCount));
        }

        [Test]
        public void Scheduler_RejectsActiveAndQueuedGrowthAtConfiguredLimits()
        {
            var provider = new RecordingProvider();
            var activeAsset = new TestChoreographyAsset(
                "active",
                TestFactory.Section("main", 10d, new ChoreographyTrack[0], mode: ChoreographyPlaybackMode.Additive));
            var activeScheduler = new ChoreographyScheduler(
                new RecordingProviderSet(provider),
                new ChoreographySchedulerOptions(maximumActiveCount: 1, maximumQueuedCount: 1, maximumRetainedPoolCount: 1));

            Assert.That(activeScheduler.Play(activeAsset, new ChoreographyPlayRequest(mode: ChoreographyPlaybackMode.Additive)), Is.Positive);
            Assert.That(activeScheduler.Play(activeAsset, new ChoreographyPlayRequest(mode: ChoreographyPlaybackMode.Additive)), Is.Zero);
            Assert.That(activeScheduler.GetMemoryStats().RejectedActiveCount, Is.EqualTo(1));

            var queueAsset = new TestChoreographyAsset(
                "queue",
                TestFactory.Section("main", 10d, new ChoreographyTrack[0], mode: ChoreographyPlaybackMode.Queue));
            var queueScheduler = new ChoreographyScheduler(
                new RecordingProviderSet(provider),
                new ChoreographySchedulerOptions(maximumActiveCount: 2, maximumQueuedCount: 1, maximumRetainedPoolCount: 1));

            Assert.That(queueScheduler.Play(queueAsset, new ChoreographyPlayRequest(mode: ChoreographyPlaybackMode.Queue)), Is.Positive);
            Assert.That(queueScheduler.Play(queueAsset, new ChoreographyPlayRequest(mode: ChoreographyPlaybackMode.Queue)), Is.Positive);
            Assert.That(queueScheduler.Play(queueAsset, new ChoreographyPlayRequest(mode: ChoreographyPlaybackMode.Queue)), Is.Zero);
            Assert.That(queueScheduler.QueuedCount, Is.EqualTo(1));
            Assert.That(queueScheduler.GetMemoryStats().RejectedQueuedCount, Is.EqualTo(1));
        }

        [Test]
        public void PreloadRunner_FailsClosedBeforeBackendWorkWhenBatchExceedsLimit()
        {
            var provider = new FakeResourceProvider();
            var runner = new PreloadRunner(provider, maximumReferenceCount: 1, maximumConcurrentLoadCount: 1);
            var references = new List<ChoreographyResourceReference>
            {
                new ChoreographyResourceReference("a", ChoreographyResourceKind.Generic),
                new ChoreographyResourceReference("b", ChoreographyResourceKind.Generic)
            };
            PreloadResult result = default;
            runner.Completed += value => result = value;

            Assert.That(runner.TryBegin(references, PreloadOptions.Default), Is.False);
            Assert.That(runner.Status, Is.EqualTo(PreloadStatus.Failed));
            Assert.That(provider.LoadCount, Is.Zero);
            Assert.That(runner.GetMemoryStats().RejectedReferenceCount, Is.EqualTo(2));
            Assert.That(result.FailedCount, Is.EqualTo(2));
            Assert.That(result.FailedReferences, Is.Empty);
            Assert.That(result.HasTruncatedFailureDetails, Is.True);
        }

        [Test]
        public void PreloadRunner_AssetCollectionChecksCapacityBeforeAppendingBeyondLimit()
        {
            var provider = new FakeResourceProvider();
            var runner = new PreloadRunner(provider, maximumReferenceCount: 1, maximumConcurrentLoadCount: 1);
            var asset = new TestChoreographyAsset(
                "bounded",
                TestFactory.Section(
                    "main",
                    1d,
                    new[]
                    {
                        TestFactory.Track(
                            ChoreographyTrackKind.Animation,
                            TestFactory.Clip("a", 0d, 0.5d),
                            TestFactory.Clip("b", 0.5d, 0.5d))
                    }));

            Assert.That(runner.TryBegin(asset, PreloadOptions.Default), Is.False);
            Assert.That(provider.LoadCount, Is.Zero);
            Assert.That(runner.TotalCount, Is.Zero);
            Assert.That(runner.GetMemoryStats().RejectedReferenceCount, Is.EqualTo(2));
        }

        [Test]
        public void PreloadRunner_AssetCollectionStopsAtConfiguredNodeScanBudget()
        {
            var provider = new FakeResourceProvider();
            var runner = new PreloadRunner(
                provider,
                maximumReferenceCount: 2,
                maximumConcurrentLoadCount: 1,
                maximumAssetNodeScanCount: 3);
            ChoreographyClip duplicate = TestFactory.Clip("shared", 0d, 0.25d);
            var asset = new TestChoreographyAsset(
                "scan-budget",
                TestFactory.Section(
                    "main",
                    1d,
                    new[]
                    {
                        TestFactory.Track(ChoreographyTrackKind.Animation, duplicate, duplicate)
                    }));

            Assert.That(runner.TryBegin(asset, PreloadOptions.Default), Is.False);
            Assert.That(provider.LoadCount, Is.Zero);
            Assert.That(runner.Status, Is.EqualTo(PreloadStatus.Failed));
        }

        [Test]
        public void PreloadRunner_LegacyAssetCollectionPreservesHiddenDependencies()
        {
            var provider = new FakeResourceProvider();
            var hiddenReference = new ChoreographyResourceReference(
                "hidden/precomputed",
                ChoreographyResourceKind.Generic);
            var asset = new LegacyHiddenDependencyAsset("legacy-hidden", hiddenReference);
            var runner = new PreloadRunner(provider, maximumReferenceCount: 1, maximumConcurrentLoadCount: 1);

            Assert.That(runner.TryBegin(asset, PreloadOptions.Default), Is.True);
            Assert.That(asset.CollectCallCount, Is.EqualTo(1));
            Assert.That(provider.LoadCount, Is.EqualTo(1));
            Assert.That(runner.TotalCount, Is.EqualTo(1));

            runner.ReleaseAll();
        }

        [Test]
        public void PreloadRunner_BoundedCollectorRejectsBeforeBackendWithoutLegacyFallback()
        {
            var provider = new FakeResourceProvider();
            var asset = new RecordingBoundedAsset(
                "bounded-hidden",
                new ChoreographyResourceReference("hidden/a", ChoreographyResourceKind.Generic),
                new ChoreographyResourceReference("hidden/b", ChoreographyResourceKind.Generic));
            var runner = new PreloadRunner(provider, maximumReferenceCount: 1, maximumConcurrentLoadCount: 1);

            Assert.That(runner.TryBegin(asset, PreloadOptions.Default), Is.False);
            Assert.That(asset.BoundedCollectCallCount, Is.EqualTo(1));
            Assert.That(asset.LegacyCollectCallCount, Is.Zero);
            Assert.That(asset.MaximumObservedResultCount, Is.EqualTo(1));
            Assert.That(provider.LoadCount, Is.Zero);
            Assert.That(runner.TotalCount, Is.Zero);
        }

        [Test]
        public void BuiltAsset_ProvidesBoundedCollectionCapability()
        {
            var asset = new BuiltChoreographyAsset("built", new ChoreographySection[0]);

            Assert.That(asset, Is.InstanceOf<IBoundedChoreographyResourceCollector>());
        }

        [Test]
        public void PreloadRunner_BoundedBuiltAssetWarmPathDoesNotAllocateManagedMemory()
        {
            var provider = new FakeResourceProvider();
            var asset = new BuiltChoreographyAsset(
                "warm",
                new[]
                {
                    TestFactory.Section(
                        "main",
                        1d,
                        new[]
                        {
                            TestFactory.Track(
                                ChoreographyTrackKind.Animation,
                                TestFactory.Clip("warm", 0d, 1d))
                        })
                });
            var runner = new PreloadRunner(provider, maximumReferenceCount: 1, maximumConcurrentLoadCount: 1);

            Assert.That(runner.TryBegin(asset, PreloadOptions.Default), Is.True);
            runner.ReleaseAll();

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool allAccepted = true;
            for (int i = 0; i < 16; i++)
            {
                allAccepted &= runner.TryBegin(asset, PreloadOptions.Default);
                runner.ReleaseAll();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allAccepted, Is.True);
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void PreloadRunner_ExposesExplicitCoreDiagnosticsConstructor()
        {
            Assert.That(
                typeof(PreloadRunner).GetConstructor(new[]
                {
                    typeof(IResourceProvider),
                    typeof(IChoreographyDiagnostics)
                }),
                Is.Not.Null);
        }

        private sealed class LegacyHiddenDependencyAsset : IChoreographyAsset
        {
            private static readonly ChoreographySection[] EmptySections = new ChoreographySection[0];
            private readonly ChoreographyResourceReference _hiddenReference;

            public LegacyHiddenDependencyAsset(string id, ChoreographyResourceReference hiddenReference)
            {
                Id = id;
                _hiddenReference = hiddenReference;
            }

            public string Id { get; }
            public double TotalDuration => 0d;
            public IReadOnlyList<ChoreographySection> Sections => EmptySections;
            public int CollectCallCount { get; private set; }

            public int CollectResourceReferences(List<ChoreographyResourceReference> results)
            {
                CollectCallCount++;
                if (results == null || results.Contains(_hiddenReference))
                {
                    return 0;
                }

                results.Add(_hiddenReference);
                return 1;
            }
        }

        private sealed class RecordingBoundedAsset : IChoreographyAsset, IBoundedChoreographyResourceCollector
        {
            private static readonly ChoreographySection[] EmptySections = new ChoreographySection[0];
            private readonly ChoreographyResourceReference[] _references;

            public RecordingBoundedAsset(string id, params ChoreographyResourceReference[] references)
            {
                Id = id;
                _references = references;
            }

            public string Id { get; }
            public double TotalDuration => 0d;
            public IReadOnlyList<ChoreographySection> Sections => EmptySections;
            public int BoundedCollectCallCount { get; private set; }
            public int LegacyCollectCallCount { get; private set; }
            public int MaximumObservedResultCount { get; private set; }

            public int CollectResourceReferences(List<ChoreographyResourceReference> results)
            {
                LegacyCollectCallCount++;
                int addedCount = 0;
                for (int i = 0; i < _references.Length; i++)
                {
                    ChoreographyResourceReference reference = _references[i];
                    if (!results.Contains(reference))
                    {
                        results.Add(reference);
                        addedCount++;
                    }
                }

                return addedCount;
            }

            public bool TryCollectResourceReferences(
                List<ChoreographyResourceReference> results,
                int maximumResultCount,
                int maximumNodeScanCount,
                out int addedCount,
                out int scannedNodeCount)
            {
                BoundedCollectCallCount++;
                addedCount = 0;
                scannedNodeCount = 0;
                if (results == null || maximumResultCount < 0 || maximumNodeScanCount < 0
                    || results.Count > maximumResultCount)
                {
                    return false;
                }

                for (int i = 0; i < _references.Length; i++)
                {
                    if (scannedNodeCount >= maximumNodeScanCount)
                    {
                        return false;
                    }
                    scannedNodeCount++;

                    ChoreographyResourceReference reference = _references[i];
                    if (results.Contains(reference))
                    {
                        continue;
                    }

                    if (results.Count >= maximumResultCount)
                    {
                        return false;
                    }

                    results.Add(reference);
                    addedCount++;
                    if (results.Count > MaximumObservedResultCount)
                    {
                        MaximumObservedResultCount = results.Count;
                    }
                }

                return true;
            }
        }
    }
}
