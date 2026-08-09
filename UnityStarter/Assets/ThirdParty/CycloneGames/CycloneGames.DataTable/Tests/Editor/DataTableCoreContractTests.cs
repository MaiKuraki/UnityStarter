using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using NUnit.Framework;

namespace CycloneGames.DataTable.Tests.Editor
{
    public sealed class DataTableCoreContractTests
    {
        private IDataTableDiagnostics _previousDiagnostics;

        [SetUp]
        public void SetUp()
        {
            _previousDiagnostics = DataTableDiagnostics.Current;
            Assert.IsTrue(DataTableDiagnostics.TryReplace(
                _previousDiagnostics,
                NullDataTableDiagnostics.Instance));
        }

        [TearDown]
        public void TearDown()
        {
            IDataTableDiagnostics current = DataTableDiagnostics.Current;
            Assert.IsTrue(DataTableDiagnostics.TryReplace(
                current,
                _previousDiagnostics ?? NullDataTableDiagnostics.Instance));
            _previousDiagnostics = null;
        }

        [Test]
        public void Constructor_CopiesArrayAndExposesNonArrayReadOnlyView()
        {
            var original = new TestRow { Id = 1, Value = "original" };
            var replacement = new TestRow { Id = 2, Value = "replacement" };
            var source = new[] { original };

            var table = new DataTable<TestRow>(source);
            source[0] = replacement;

            Assert.AreSame(original, table.Get(1));
            Assert.AreEqual(1, table.Count);
            Assert.IsFalse(table.All is TestRow[]);
            Assert.Throws<NotSupportedException>(
                () => ((IList<TestRow>)table.All)[0] = replacement);
        }

        [Test]
        public void FromOwnedArray_IndexesTheTransferredArrayWithoutExposingItThroughAll()
        {
            var row = new TestRow { Id = 7 };
            var source = new[] { row };

            DataTable<TestRow> table = DataTable<TestRow>.FromOwnedArray(source);

            Assert.AreSame(row, table.Get(7));
            Assert.IsFalse(table.All is TestRow[]);
        }

        [Test]
        public void AsSpan_ProvidesAllocationFreeSourceOrderScan()
        {
            var first = new TestRow { Id = 7 };
            var second = new TestRow { Id = 3 };
            var table = new DataTable<TestRow>(new[] { first, second });

            ReadOnlySpan<TestRow> rows = table.AsSpan();
            Assert.AreEqual(2, rows.Length);
            Assert.AreSame(first, rows[0]);
            Assert.AreSame(second, rows[1]);

            // Warm generic span helpers and tiered JIT call sites before sampling. Unity Mono
            // normally needs fewer iterations, while CoreCLR may promote this loop later.
            for (int i = 0; i < 10_000; i++)
            {
                _ = table.AsSpan()[0];
            }

            TestRow sink = null;
            long allocatedBytes = -1;
            for (int sample = 0; sample < 2; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100_000; i++)
                {
                    sink = table.AsSpan()[i & 1];
                }

                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Assert.AreEqual(0, allocatedBytes);
            GC.KeepAlive(sink);
        }

        [Test]
        public void Constructor_RejectsNullRowsAndDuplicateKeys()
        {
            Assert.Throws<ArgumentException>(() => new DataTable<TestRow>(new TestRow[] { null }));
            Assert.Throws<ArgumentException>(() => new DataTable<TestRow>(new[]
            {
                new TestRow { Id = 1 },
                new TestRow { Id = 1 },
            }));
        }

        [Test]
        public void GenericTable_AcceptsGeneratedRowsWithoutFrameworkInterface()
        {
            var first = new ExternalGeneratedRow { Code = "ITEM_A" };
            var table = new DataTable<string, ExternalGeneratedRow>(
                new[] { first },
                row => row.Code,
                StringComparer.Ordinal);

            Assert.AreSame(first, table.Get("ITEM_A"));
            Assert.IsTrue(table.TryGet("ITEM_A", out ExternalGeneratedRow found));
            Assert.AreSame(first, found);
            Assert.IsNull(table.GetOrDefault("MISSING"));
            Assert.Throws<KeyNotFoundException>(() => table.Get("MISSING"));
        }

        [Test]
        public void ValueTypeRows_AreReadByKeyWithoutChangingSourceOrder()
        {
            var rows = new[]
            {
                new WideValueRow(7, 70),
                new WideValueRow(3, 30),
            };

            var table = new DataTable<WideValueRow>(rows);

            Assert.AreEqual(30, table.Get(3).Value);
            Assert.IsTrue(table.TryGet(7, out WideValueRow found));
            Assert.AreEqual(70, found.Value);
            Assert.IsFalse(table.TryGet(99, out WideValueRow missing));
            Assert.AreEqual(default(WideValueRow), missing);
            Assert.AreEqual(7, table.All[0].Id);
            Assert.AreEqual(3, table.All[1].Id);
        }

        [Test]
        public void Constructor_EnforcesConfiguredRowLimit()
        {
            var limits = new DataTableLoadLimits(2, 16, 32, maxRowsPerTable: 1, maxTableNameLength: 16);
            Assert.Throws<InvalidOperationException>(() => new DataTable<TestRow>(
                new[] { new TestRow { Id = 1 }, new TestRow { Id = 2 } },
                limits: limits));
        }

        [Test]
        public void FromEnumerable_StopsAtRowBudgetBeforeUnboundedMaterialization()
        {
            var limits = new DataTableLoadLimits(4, 128, 256, maxRowsPerTable: 3, maxTableNameLength: 32);
            var rows = new UnboundedRows();

            Assert.Throws<InvalidOperationException>(
                () => DataTable<TestRow>.FromEnumerable(rows, limits));
            Assert.AreEqual(
                4,
                rows.MoveNextCount,
                "Enumeration must stop immediately after observing the first row beyond the configured limit.");
        }

        [Test]
        public void Lookup_10000Rows_DoesNotAllocateAfterWarmup()
        {
            const int rowCount = 10_000;
            var rows = new TestRow[rowCount];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new TestRow { Id = i };
            }

            var table = DataTable<TestRow>.FromOwnedArray(rows);
            Assert.AreEqual(999, table.Get(999).Id);
            int checksum = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100_000; i++)
            {
                checksum += table.Get(i % rowCount).Id;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(checksum);
            Assert.AreEqual(0, allocatedBytes);
        }

        [Test]
        public void CatalogBuilder_IsStrictAndOneShot()
        {
            var first = new PairLeft(1);
            var builder = new DataTableCatalogBuilder(1);
            builder.Add(first);

            Assert.Throws<ArgumentException>(() => builder.Add(new PairLeft(2)));
            Assert.Throws<ArgumentException>(() => builder.Add(typeof(PairLeft), new PairRight(1)));
            Assert.Throws<ArgumentException>(
                () => new DataTableCatalogBuilder().Add(typeof(int), 1));

            DataTableCatalog catalog = builder.Build();
            Assert.AreSame(first, catalog.Get<PairLeft>());
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => _ = builder.Count);
        }

        [Test]
        public void CatalogBuilder_EnforcesConfiguredTableLimitBeforeAddingEntry()
        {
            var limits = new DataTableLoadLimits(
                maxTableCount: 1,
                maxBytesPerTable: 16,
                maxTotalBytes: 16,
                maxRowsPerTable: 4,
                maxTableNameLength: 32);
            var builder = new DataTableCatalogBuilder(limits, capacity: 1);
            builder.Add(new PairLeft(1));

            Assert.Throws<InvalidOperationException>(() => builder.Add(new PairRight(1)));
            Assert.AreEqual(1, builder.Count);
        }

        [Test]
        public void Store_PublishesWholeSnapshotsToConcurrentReader()
        {
            DataTableCatalog first = CreatePairCatalog(1);
            using (var store = new DataTableStore())
            using (var firstCandidate = new DataTableCandidate(first, CreateRevision(1)))
            {
                Assert.IsTrue(store.TryPublish(firstCandidate, expectedGeneration: 0).IsCommitted);
                using (DataTableReader reader = store.RegisterReader())
                {
                    Exception readerFailure = null;
                    var readerThread = new Thread(() =>
                    {
                        try
                        {
                            for (int i = 0; i < 100_000; i++)
                            {
                                reader.Refresh();
                                DataTableSnapshot snapshot = reader.Snapshot;
                                int left = snapshot.Get<PairLeft>().Version;
                                int right = snapshot.Get<PairRight>().Version;
                                if (left != right)
                                {
                                    throw new InvalidOperationException(
                                        $"Observed mixed table generations: {left}/{right}.");
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            readerFailure = exception;
                        }
                    });

                    readerThread.Start();
                    long expectedGeneration = store.Generation;
                    for (int i = 0; i < 2_000; i++)
                    {
                        int version = (i & 1) == 0 ? 2 : 1;
                        using (var candidate = new DataTableCandidate(
                            CreatePairCatalog(version),
                            CreateRevision(i + 2)))
                        {
                            Assert.IsTrue(store.TryPublish(candidate, expectedGeneration).IsCommitted);
                            expectedGeneration++;
                        }
                    }

                    Assert.IsTrue(
                        readerThread.Join(5_000),
                        "Concurrent data-table reader did not finish within the test budget.");
                    Assert.IsNull(readerFailure);
                    Assert.IsTrue(store.IsInitialized);
                    Assert.Greater(store.Generation, 0);
                }
            }
        }

        [Test]
        public void Store_RetiresOwnerOnlyAfterEveryReaderLeavesTheGeneration()
        {
            var firstOwner = new CountingDisposable();
            var secondOwner = new CountingDisposable();
            var store = new DataTableStore();
            using (var firstCandidate = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                firstOwner))
            {
                Assert.IsTrue(store.TryPublish(firstCandidate, expectedGeneration: 0).IsCommitted);
            }

            DataTableReader firstReader = store.RegisterReader();
            DataTableReader secondReader = store.RegisterReader();
            Assert.AreEqual(2, store.ActiveReaderCount);
            using (var secondCandidate = new DataTableCandidate(
                CreatePairCatalog(2),
                CreateRevision(2),
                secondOwner))
            {
                Assert.IsTrue(store.TryPublish(secondCandidate, expectedGeneration: 1).IsCommitted);
            }

            Assert.AreEqual(0, firstOwner.DisposeCount);
            Assert.IsTrue(firstReader.Refresh());
            Assert.AreEqual(0, firstOwner.DisposeCount);

            secondReader.Dispose();
            secondReader.Dispose();
            Assert.AreEqual(1, store.ActiveReaderCount);
            Assert.AreEqual(1, firstOwner.DisposeCount);

            store.Dispose();
            Assert.AreEqual(0, secondOwner.DisposeCount);
            Assert.AreEqual(2, firstReader.Get<PairLeft>().Version);
            Assert.Throws<ObjectDisposedException>(() => firstReader.Refresh());

            firstReader.Dispose();
            Assert.AreEqual(0, store.ActiveReaderCount);
            Assert.AreEqual(1, secondOwner.DisposeCount);
        }

        [TestCase(DiagnosticFailurePoint.IsEnabled)]
        [TestCase(DiagnosticFailurePoint.Write)]
        public void Store_Publish_RemainsSuccessfulWhenInstalledDiagnosticsThrowsAfterCommit(
            DiagnosticFailurePoint failurePoint)
        {
            DataTableCatalog catalog = CreatePairCatalog(42);
            var diagnostics = new ThrowingDataTableDiagnostics(failurePoint);
            Assert.IsTrue(DataTableDiagnostics.TryReplace(
                NullDataTableDiagnostics.Instance,
                diagnostics));
            using (var store = new DataTableStore())
            using (var candidate = new DataTableCandidate(catalog, CreateRevision(42)))
            {
                Assert.DoesNotThrow(() => Assert.IsTrue(store.TryPublish(candidate, 0).IsCommitted));
                using (DataTableReader reader = store.RegisterReader())
                {
                    Assert.IsTrue(reader.IsInitialized);
                    Assert.AreSame(catalog, reader.Snapshot.Catalog);
                    Assert.AreEqual(42, reader.Get<PairLeft>().Version);
                }
            }
        }

        [Test]
        public void Store_ExplicitDiagnosticChannel_DoesNotDependOnAmbientSink()
        {
            var ambient = new ThrowingDataTableDiagnostics(DiagnosticFailurePoint.OutOfMemory);
            Assert.IsTrue(DataTableDiagnostics.TryReplace(
                NullDataTableDiagnostics.Instance,
                ambient));
            DataTableDiagnosticChannel isolated = DataTableDiagnosticChannel.Create(
                "CycloneGames.DataTable.Isolated",
                NullDataTableDiagnostics.Instance);

            using (var store = new DataTableStore(0, isolated))
            using (var candidate = new DataTableCandidate(CreatePairCatalog(3), CreateRevision(3)))
            {
                Assert.DoesNotThrow(() => Assert.IsTrue(store.TryPublish(candidate, 0).IsCommitted));
            }
        }

        [Test]
        public void Store_RejectsStalePublicationWithoutTakingCandidateOwnership()
        {
            var publishedOwner = new CountingDisposable();
            var staleOwner = new CountingDisposable();
            using (var store = new DataTableStore())
            using (var published = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                publishedOwner))
            using (var stale = new DataTableCandidate(
                CreatePairCatalog(2),
                CreateRevision(2),
                staleOwner))
            {
                Assert.IsTrue(store.TryPublish(published, 0).IsCommitted);
                DataTablePublishResult staleResult = store.TryPublish(stale, 0);
                Assert.AreEqual(DataTablePublishStatus.Superseded, staleResult.Status);
                Assert.AreEqual(1, staleResult.ObservedGeneration);
                Assert.IsTrue(stale.IsCallerOwned);
                Assert.IsFalse(stale.IsCommitted);
                Assert.AreEqual(0, staleOwner.DisposeCount);

                stale.Dispose();
                Assert.AreEqual(1, staleOwner.DisposeCount);
            }

            Assert.AreEqual(1, publishedOwner.DisposeCount);
        }

        [Test]
        public void Store_ConcurrentPublishAllowsOnlyOneExpectedGenerationWinner()
        {
            var firstOwner = new CountingDisposable();
            var secondOwner = new CountingDisposable();
            var store = new DataTableStore();
            var first = new DataTableCandidate(CreatePairCatalog(1), CreateRevision(1), firstOwner);
            var second = new DataTableCandidate(CreatePairCatalog(2), CreateRevision(2), secondOwner);
            var start = new ManualResetEventSlim(initialState: false);
            DataTablePublishResult firstResult = default;
            DataTablePublishResult secondResult = default;
            Exception firstFailure = null;
            Exception secondFailure = null;

            var firstPublisher = new Thread(() =>
            {
                try
                {
                    start.Wait();
                    firstResult = store.TryPublish(first, expectedGeneration: 0);
                }
                catch (Exception exception)
                {
                    firstFailure = exception;
                }
            });
            var secondPublisher = new Thread(() =>
            {
                try
                {
                    start.Wait();
                    secondResult = store.TryPublish(second, expectedGeneration: 0);
                }
                catch (Exception exception)
                {
                    secondFailure = exception;
                }
            });

            firstPublisher.Start();
            secondPublisher.Start();
            start.Set();
            Assert.IsTrue(firstPublisher.Join(5_000));
            Assert.IsTrue(secondPublisher.Join(5_000));

            Assert.IsNull(firstFailure);
            Assert.IsNull(secondFailure);
            Assert.AreNotEqual(firstResult.IsCommitted, secondResult.IsCommitted);
            Assert.AreEqual(
                DataTablePublishStatus.Superseded,
                firstResult.IsCommitted ? secondResult.Status : firstResult.Status);
            Assert.AreEqual(1, store.Generation);
            Assert.AreNotEqual(first.IsCallerOwned, second.IsCallerOwned);

            first.Dispose();
            second.Dispose();
            store.Dispose();
            start.Dispose();
            Assert.AreEqual(1, firstOwner.DisposeCount);
            Assert.AreEqual(1, secondOwner.DisposeCount);
        }

        [Test]
        public void Store_ResetRetiresThePreviousOwnerAfterReaderRefresh()
        {
            var owner = new CountingDisposable();
            using (var store = new DataTableStore())
            using (var candidate = new DataTableCandidate(
                CreatePairCatalog(7),
                CreateRevision(7),
                owner))
            {
                Assert.IsTrue(store.TryPublish(candidate, 0).IsCommitted);
                using (DataTableReader reader = store.RegisterReader())
                {
                    Assert.IsTrue(store.TryReset(1).IsCommitted);
                    Assert.AreEqual(0, owner.DisposeCount);
                    Assert.IsTrue(reader.IsInitialized);
                    Assert.AreEqual(7, reader.Get<PairLeft>().Version);

                    Assert.IsTrue(reader.Refresh());
                    Assert.IsFalse(reader.IsInitialized);
                    Assert.AreEqual(2, reader.Generation);
                    Assert.IsFalse(reader.Revision.IsPublishable);
                    Assert.AreEqual(0, reader.Revision.Sequence);
                    Assert.AreEqual(1, owner.DisposeCount);
                }
            }
        }

        [Test]
        public void Store_SteadyStateReaderReadsAndNoOpRefreshDoNotAllocateAfterWarmup()
        {
            using (var store = new DataTableStore())
            using (var candidate = new DataTableCandidate(CreatePairCatalog(9), CreateRevision(9)))
            {
                Assert.IsTrue(store.TryPublish(candidate, 0).IsCommitted);
                using (DataTableReader reader = store.RegisterReader())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        reader.TryGet(out PairLeft _);
                        _ = reader.Snapshot;
                        reader.Refresh();
                    }

                    PairLeft sink = null;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 100_000; i++)
                    {
                        reader.TryGet(out sink);
                        _ = reader.Snapshot;
                        reader.Refresh();
                    }

                    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    GC.KeepAlive(sink);
                    Assert.AreEqual(0, allocatedBytes);
                }
            }
        }

        [Test]
        public void Store_DisposalFailureAfterRetirementDoesNotUndoPublication()
        {
            var throwingOwner = new ThrowOnceDisposable();
            using (var store = new DataTableStore())
            using (var first = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                throwingOwner))
            using (var second = new DataTableCandidate(CreatePairCatalog(2), CreateRevision(2)))
            {
                Assert.IsTrue(store.TryPublish(first, 0).IsCommitted);
                Assert.DoesNotThrow(() => Assert.IsTrue(store.TryPublish(second, 1).IsCommitted));
                Assert.AreEqual(1, throwingOwner.DisposeCount);
                Assert.AreEqual(1, store.FailedRetirementCount);
                using (DataTableReader reader = store.RegisterReader())
                {
                    Assert.AreEqual(2, reader.Get<PairLeft>().Version);
                }

                Assert.AreEqual(0, store.RetryFailedRetirements());
                Assert.AreEqual(2, throwingOwner.DisposeCount);
                Assert.AreEqual(0, store.FailedRetirementCount);
            }
        }

        [Test]
        public void Store_FailedRetirementDoesNotRetainTheRetiredTableGraph()
        {
            var owner = new ThrowOnceDisposable();
            using (var store = new DataTableStore())
            {
                WeakReference retiredTable = PublishThenFailRetirement(store, owner);

                Assert.AreEqual(1, store.FailedRetirementCount);
                CollectGarbage();
                Assert.IsFalse(
                    retiredTable.IsAlive,
                    "The retry boundary must retain the failed resource owner, not its retired catalog.");
                Assert.AreEqual(0, store.RetryFailedRetirements());
                Assert.AreEqual(2, owner.DisposeCount);
                GC.KeepAlive(store);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Store_FatalRetryRetainsTheCurrentAndRemainingOwners(int fatalRetryIndex)
        {
            var firstOwner = new RetryScriptDisposable(throwOutOfMemoryOnSecondAttempt: false);
            var secondOwner = new RetryScriptDisposable(throwOutOfMemoryOnSecondAttempt: false);
            var thirdOwner = new RetryScriptDisposable(throwOutOfMemoryOnSecondAttempt: false);
            RetryScriptDisposable[] retryOrder = { thirdOwner, secondOwner, firstOwner };
            retryOrder[fatalRetryIndex].ThrowOutOfMemoryOnSecondAttempt = true;

            using (var store = new DataTableStore())
            using (var first = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                firstOwner))
            using (var second = new DataTableCandidate(
                CreatePairCatalog(2),
                CreateRevision(2),
                secondOwner))
            using (var third = new DataTableCandidate(
                CreatePairCatalog(3),
                CreateRevision(3),
                thirdOwner))
            using (var current = new DataTableCandidate(CreatePairCatalog(4), CreateRevision(4)))
            {
                Assert.IsTrue(store.TryPublish(first, expectedGeneration: 0).IsCommitted);
                Assert.IsTrue(store.TryPublish(second, expectedGeneration: 1).IsCommitted);
                Assert.IsTrue(store.TryPublish(third, expectedGeneration: 2).IsCommitted);
                Assert.IsTrue(store.TryPublish(current, expectedGeneration: 3).IsCommitted);
                Assert.AreEqual(3, store.FailedRetirementCount);

                Assert.Throws<OutOfMemoryException>(() => store.RetryFailedRetirements());
                Assert.AreEqual(3 - fatalRetryIndex, store.FailedRetirementCount);
                for (int i = 0; i < retryOrder.Length; i++)
                {
                    Assert.AreEqual(i <= fatalRetryIndex ? 2 : 1, retryOrder[i].DisposeCount);
                }

                Assert.AreEqual(0, store.RetryFailedRetirements());
                for (int i = 0; i < retryOrder.Length; i++)
                {
                    Assert.AreEqual(i == fatalRetryIndex ? 3 : 2, retryOrder[i].DisposeCount);
                }
            }
        }

        [Test]
        public void Store_FatalRetryDuringDisposeDefersTheDetachedCurrentOwner()
        {
            var failedOwner = new RetryScriptDisposable(throwOutOfMemoryOnSecondAttempt: true);
            var currentOwner = new CountingDisposable();
            var store = new DataTableStore();
            using (var failed = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                failedOwner))
            using (var current = new DataTableCandidate(
                CreatePairCatalog(2),
                CreateRevision(2),
                currentOwner))
            {
                Assert.IsTrue(store.TryPublish(failed, expectedGeneration: 0).IsCommitted);
                Assert.IsTrue(store.TryPublish(current, expectedGeneration: 1).IsCommitted);
                Assert.AreEqual(1, store.FailedRetirementCount);

                Assert.Throws<OutOfMemoryException>(() => store.Dispose());
                Assert.IsTrue(store.IsDisposed);
                Assert.AreEqual(2, store.FailedRetirementCount);
                Assert.AreEqual(0, currentOwner.DisposeCount);

                Assert.DoesNotThrow(() => store.Dispose());
                Assert.AreEqual(0, store.FailedRetirementCount);
                Assert.AreEqual(3, failedOwner.DisposeCount);
                Assert.AreEqual(1, currentOwner.DisposeCount);
            }
        }

        [Test]
        public void Store_ResetResultCapturesRevisionWatermarkAtItsCommitPoint()
        {
            var store = new DataTableStore();
            var reentrantCandidate = new DataTableCandidate(
                CreatePairCatalog(2),
                CreateRevision(2));
            DataTablePublishResult reentrantResult = default;
            var owner = new ReentrantDisposable(() =>
            {
                reentrantResult = store.TryPublish(reentrantCandidate, expectedGeneration: 2);
            });

            using (store)
            using (reentrantCandidate)
            using (var first = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                owner))
            {
                Assert.IsTrue(store.TryPublish(first, expectedGeneration: 0).IsCommitted);

                DataTablePublishResult resetResult = store.TryReset(expectedGeneration: 1);

                Assert.IsTrue(resetResult.IsCommitted);
                Assert.AreEqual(2, resetResult.ObservedGeneration);
                Assert.AreEqual(
                    1,
                    resetResult.ObservedRevisionSequence,
                    "A reset result must not mix in a later reentrant publication's watermark.");
                Assert.IsTrue(reentrantResult.IsCommitted);
                Assert.AreEqual(3, store.Metadata.Generation);
                Assert.AreEqual(2, store.Metadata.RevisionSequenceHighWatermark);
            }
        }

        [Test]
        public void Candidate_RetainsOwnerForRetryWhenCallerDisposalFails()
        {
            var owner = new ThrowOnceDisposable();
            var candidate = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                owner);

            Assert.Throws<InvalidOperationException>(() => candidate.Dispose());
            Assert.IsTrue(candidate.HasDisposeFailure);
            Assert.IsTrue(candidate.OwnsResources);
            Assert.IsFalse(candidate.IsDisposed);
            Assert.Throws<InvalidOperationException>(() => _ = candidate.Catalog);

            Assert.DoesNotThrow(() => candidate.Dispose());
            Assert.IsFalse(candidate.HasDisposeFailure);
            Assert.IsFalse(candidate.OwnsResources);
            Assert.IsTrue(candidate.IsDisposed);
            Assert.AreEqual(2, owner.DisposeCount);
        }

        [Test]
        public void Store_RejectsReplayAndRollbackAcrossReset()
        {
            using (var store = new DataTableStore(revisionSequenceFloor: 5))
            using (var baselineReplay = new DataTableCandidate(CreatePairCatalog(5), CreateRevision(5)))
            using (var accepted = new DataTableCandidate(CreatePairCatalog(6), CreateRevision(6)))
            using (var replay = new DataTableCandidate(CreatePairCatalog(6), CreateRevision(6)))
            using (var rollback = new DataTableCandidate(CreatePairCatalog(4), CreateRevision(4)))
            using (var postResetRollback = new DataTableCandidate(CreatePairCatalog(5), CreateRevision(5)))
            {
                DataTablePublishResult floorResult = store.TryPublish(baselineReplay, 0);
                Assert.AreEqual(DataTablePublishStatus.NonMonotonicRevision, floorResult.Status);
                Assert.IsTrue(baselineReplay.IsCallerOwned);

                Assert.IsTrue(store.TryPublish(accepted, 0).IsCommitted);
                DataTableStoreMetadata acceptedMetadata = store.Metadata;
                Assert.AreEqual(1, acceptedMetadata.Generation);
                Assert.AreEqual(6, acceptedMetadata.Revision.Sequence);
                Assert.AreEqual(6, acceptedMetadata.RevisionSequenceHighWatermark);

                Assert.AreEqual(
                    DataTablePublishStatus.NonMonotonicRevision,
                    store.TryPublish(replay, 1).Status);
                Assert.AreEqual(
                    DataTablePublishStatus.NonMonotonicRevision,
                    store.TryPublish(rollback, 1).Status);

                Assert.IsTrue(store.TryReset(1).IsCommitted);
                DataTableStoreMetadata resetMetadata = store.Metadata;
                Assert.IsFalse(resetMetadata.IsInitialized);
                Assert.AreEqual(2, resetMetadata.Generation);
                Assert.AreEqual(6, resetMetadata.RevisionSequenceHighWatermark);
                Assert.AreEqual(
                    DataTablePublishStatus.NonMonotonicRevision,
                    store.TryPublish(postResetRollback, 2).Status);
            }
        }

        [Test]
        public void Store_InvokesRetirementOwnerOutsideTransitionLock()
        {
            var store = new DataTableStore();
            var reentrantOwner = new ReentrantDisposable(() =>
            {
                DataTableStoreMetadata metadata = store.Metadata;
                using (DataTableReader reader = store.RegisterReader())
                {
                    Assert.AreEqual(metadata.Generation, reader.Generation);
                }
            });

            using (store)
            using (var first = new DataTableCandidate(
                CreatePairCatalog(1),
                CreateRevision(1),
                reentrantOwner))
            using (var second = new DataTableCandidate(CreatePairCatalog(2), CreateRevision(2)))
            {
                Assert.IsTrue(store.TryPublish(first, 0).IsCommitted);
                Assert.DoesNotThrow(() => Assert.IsTrue(store.TryPublish(second, 1).IsCommitted));
                Assert.AreEqual(1, reentrantOwner.DisposeCount);
            }
        }

        [Test]
        public void Diagnostics_ConditionalReplacementRequiresTheExpectedOwner()
        {
            var owner = new ThrowingDataTableDiagnostics(DiagnosticFailurePoint.Write);
            var other = new ThrowingDataTableDiagnostics(DiagnosticFailurePoint.Write);
            var replacement = new ThrowingDataTableDiagnostics(DiagnosticFailurePoint.Write);
            Assert.IsTrue(DataTableDiagnostics.TryReplace(
                NullDataTableDiagnostics.Instance,
                owner));

            Assert.IsFalse(DataTableDiagnostics.TryReplace(other, replacement));
            Assert.AreSame(owner, DataTableDiagnostics.Current);
            Assert.IsTrue(DataTableDiagnostics.TryReplace(owner, replacement));
            Assert.AreSame(replacement, DataTableDiagnostics.Current);
            Assert.Throws<ArgumentNullException>(() => DataTableDiagnostics.TryReplace(null, owner));
            Assert.Throws<ArgumentNullException>(() => DataTableDiagnostics.TryReplace(replacement, null));

            Assert.IsFalse(DataTableDiagnostics.TryReset(other));
            Assert.AreSame(replacement, DataTableDiagnostics.Current);
            Assert.IsTrue(DataTableDiagnostics.TryReset(replacement));
            Assert.AreSame(NullDataTableDiagnostics.Instance, DataTableDiagnostics.Current);
            Assert.Throws<ArgumentNullException>(() => DataTableDiagnostics.TryReset(null));
        }

        [Test]
        public void BytesCache_CopiesInputAndTracksReplacementBudgetsTransactionally()
        {
            var limits = new DataTableLoadLimits(2, 4, 6, 10, 16);
            var source = new byte[] { 1, 2, 3, 4 };
            using (var cache = new DataTableBytesCache(limits, capacity: 2))
            {
                cache.Add("a", source);
                source[0] = 99;

                Assert.AreEqual(1, cache.GetBytes("a").Span[0]);
                Assert.AreEqual(4, cache.TotalBytes);
                Assert.Throws<InvalidOperationException>(() => cache.Add("b", new byte[] { 1, 2, 3 }));
                Assert.AreEqual(1, cache.Count);
                Assert.AreEqual(4, cache.TotalBytes);

                cache.Set("a", new byte[] { 8, 9 });
                cache.Add("b", new byte[] { 1, 2, 3, 4 });
                Assert.AreEqual(6, cache.TotalBytes);
                Assert.Throws<InvalidOperationException>(() => cache.Add("c", new byte[] { 1 }));
                Assert.IsTrue(cache.Remove("b"));
                Assert.AreEqual(2, cache.TotalBytes);
                Assert.AreEqual(1, cache.Count);
                Assert.IsFalse(cache.Remove("missing"));
            }
        }

        [Test]
        public void PayloadNames_RejectCaseOnlyDuplicatesAcrossPortableFileSystems()
        {
            var limits = new DataTableLoadLimits(2, 4, 8, 10, 32);
            using (var cache = new DataTableBytesCache(limits, capacity: 2))
            {
                cache.AddOwned("Items", new byte[] { 1 });

                Assert.Throws<ArgumentException>(
                    () => cache.AddOwned("items", new byte[] { 2 }));
                Assert.AreEqual(1, cache.GetBytes("ITEMS").Span[0]);
            }

            Assert.Throws<ArgumentException>(() => new DataTableManifest(
                schemaVersion: 1,
                entries: new[]
                {
                    new DataTableManifestEntry("Items", expectedByteLength: 1),
                    new DataTableManifestEntry("items", expectedByteLength: 1),
                },
                limits));

            var manifest = new DataTableManifest(
                schemaVersion: 1,
                entries: new[] { new DataTableManifestEntry("Items", expectedByteLength: 1) },
                limits);
            Assert.IsTrue(manifest.TryGetEntry("items", out DataTableManifestEntry canonicalEntry));
            Assert.AreEqual("Items", canonicalEntry.TableName);
        }

        [Test]
        public void BytesCache_SealAndDisposeHaveExplicitOwnershipSemantics()
        {
            var owned = new byte[] { 4, 5, 6 };
            var cache = new DataTableBytesCache(
                new DataTableLoadLimits(2, 4, 8),
                capacity: 1,
                clearBytesOnRelease: true);
            cache.AddOwned("owned", owned);
            cache.Seal();

            Assert.IsTrue(cache.IsSealed);
            Assert.AreEqual(3, cache.GetBytes("owned").Length);
            Assert.Throws<InvalidOperationException>(() => cache.Set("owned", new byte[] { 7 }));
            Assert.Throws<InvalidOperationException>(() => cache.Clear());

            cache.Dispose();
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, owned);
            Assert.IsTrue(cache.IsClosed);
            Assert.IsTrue(cache.IsReleaseComplete);
            Assert.Throws<ObjectDisposedException>(() => cache.GetBytes("owned"));
        }

        [TestCase("../secret")]
        [TestCase("tables/../secret")]
        [TestCase("tables//items")]
        [TestCase("/absolute/items")]
        [TestCase("C:\\absolute\\items")]
        [TestCase("tables/items?")]
        [TestCase("tables/items/")]
        [TestCase("tables/CON.bytes")]
        [TestCase("tables/com1")]
        [TestCase("foo..bytes")]
        [TestCase("foo .bytes")]
        [TestCase("dir/.bytes")]
        [TestCase(".bytes")]
        [TestCase("")]
        [TestCase("tables/zero\u200Bwidth")]
        public void NameUtility_RejectsTraversalRootedAndNonPortableNames(string value)
        {
            Assert.Throws<ArgumentException>(() => DataTableNameUtility.NormalizeTableName(value));
        }

        [Test]
        public void NameUtility_RejectsUnpairedSurrogateCodeUnits()
        {
            string value = "tables/unpaired" + new string(new[] { '\uD800' });

            Assert.Throws<ArgumentException>(() => DataTableNameUtility.NormalizeTableName(value));
        }

        [Test]
        public void NameUtility_NormalizesPortableRelativeNamesAndLocations()
        {
            Assert.AreEqual(
                "Config/Items",
                DataTableNameUtility.NormalizeTableName("  Config\\Items.bytes  "));
            var resolver = new DataTableLocationResolver("Assets/DataTables/");
            Assert.AreEqual("Assets/DataTables/Config/Items.bytes", resolver.Resolve("Config/Items"));
            Assert.AreEqual(
                "Caf\u00E9",
                DataTableNameUtility.NormalizeTableName("Cafe\u0301.bytes"));
        }

        [TestCase(".bytes.")]
        [TestCase("..")]
        [TestCase(".data..bytes")]
        [TestCase(".byte s")]
        public void NameUtility_RejectsAmbiguousOrNonPortableDataExtensions(string extension)
        {
            Assert.Throws<ArgumentException>(() => DataTableNameUtility.NormalizeDataExtension(extension));
        }

        [Test]
        public void LoadLimits_RejectRawNamesAndLocationsBeforeNormalizationBudgetsAreExceeded()
        {
            var limits = new DataTableLoadLimits(
                maxTableCount: 2,
                maxBytesPerTable: 4,
                maxTotalBytes: 8,
                maxRowsPerTable: 4,
                maxTableNameLength: 8,
                maxLocationLength: 16);

            Assert.Throws<ArgumentException>(() =>
                limits.NormalizeTableName(new string('a', 15)));
            Assert.Throws<ArgumentException>(() =>
                limits.NormalizeLocation(new string('a', 17)));
            Assert.Throws<ArgumentException>(() =>
                DataTableNameUtility.NormalizeDataExtension(
                    new string('x', DataTableNameUtility.DEFAULT_MAX_DATA_EXTENSION_LENGTH + 1)));
            Assert.Throws<ArgumentException>(() =>
                new DataTableManifestEntry(
                    "items",
                    location: new string('a', 17),
                    limits: limits));
        }

        [Test]
        public void Manifest_DefensivelyCopiesEntriesAndEnforcesSchemaLimitsAndHash()
        {
            byte[] bytes = { 1, 2, 3 };
            string sha256 = DataTableHashUtility.ComputeSha256Hex(bytes);
            var source = new[]
            {
                new DataTableManifestEntry("items", expectedByteLength: bytes.Length, sha256Hex: sha256)
            };
            var limits = new DataTableLoadLimits(2, 4, 8, 10, 16);
            var manifest = new DataTableManifest(2, source, limits, requireKnownTables: true);
            source[0] = new DataTableManifestEntry("changed", expectedByteLength: 1);

            Assert.AreEqual("items", manifest.Entries[0].TableName);
            Assert.IsFalse(manifest.Entries is DataTableManifestEntry[]);
            manifest.ValidatePayload("items", bytes);
            Assert.Throws<InvalidOperationException>(() => manifest.ValidatePayload("items", new byte[] { 1, 2 }));
            Assert.Throws<InvalidOperationException>(() => manifest.ValidatePayload("unknown", new byte[] { 1 }));
            Assert.DoesNotThrow(() => manifest.EnsureSchemaVersionSupported(1, 2));
            Assert.Throws<NotSupportedException>(() => manifest.EnsureSchemaVersionSupported(1, 1));
        }

        [Test]
        public void HashUtility_MatchesOnlyExplicitExpectedHash()
        {
            byte[] bytes = { 1, 2, 3 };
            string sha256 = DataTableHashUtility.ComputeSha256Hex(bytes);

            Assert.IsTrue(DataTableHashUtility.Sha256Matches(bytes, sha256));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, null));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, string.Empty));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, "   "));
            Assert.IsFalse(DataTableHashUtility.Sha256Matches(bytes, new string('0', 64)));
        }

        [Test]
        public void Manifest_ValidatesRequiredPayloadPresenceAndKnownInventory()
        {
            var manifest = new DataTableManifest(
                schemaVersion: 1,
                entries: new[] { new DataTableManifestEntry("required") },
                limits: DataTableLoadLimits.Default,
                requireKnownTables: true);
            using (var cache = new DataTableBytesCache())
            {
                Assert.Throws<InvalidOperationException>(() => manifest.ValidateInventory(cache));
                cache.Add("required", new byte[] { 1 });
                Assert.DoesNotThrow(() => manifest.ValidateInventory(cache));
                cache.Add("unknown", new byte[] { 2 });
                Assert.Throws<InvalidOperationException>(() => manifest.ValidateInventory(cache));
            }
        }

        [Test]
        public void Manifest_KnownOnlyModeRejectsProvidersWithoutInventory()
        {
            byte[] payload = { 1 };
            var manifest = new DataTableManifest(
                schemaVersion: 1,
                entries: new[] { new DataTableManifestEntry("required", expectedByteLength: 1) },
                limits: DataTableLoadLimits.Default,
                requireKnownTables: true);
            var provider = new SinglePayloadProvider("required", payload);

            manifest.ValidatePayload("required", payload);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => manifest.ValidateInventory(provider));
            StringAssert.Contains(nameof(IDataTableBytesInventory), exception.Message);
        }

        [Test]
        public void Manifest_InventoryValidationEnforcesAggregatePayloadBudget()
        {
            var manifestLimits = new DataTableLoadLimits(2, 4, 4);
            var cacheLimits = new DataTableLoadLimits(2, 4, 8);
            var manifest = new DataTableManifest(
                schemaVersion: 1,
                entries: Array.Empty<DataTableManifestEntry>(),
                limits: manifestLimits,
                requireKnownTables: false);
            using (var cache = new DataTableBytesCache(cacheLimits, capacity: 2))
            {
                cache.AddOwned("a", new byte[] { 1, 2, 3 });
                cache.AddOwned("b", new byte[] { 4, 5, 6 });

                Assert.Throws<InvalidOperationException>(() => manifest.ValidateInventory(cache));
            }
        }

        [Test]
        public void Manifest_ReentrantSourceMutationCannotProducePartialSnapshot()
        {
            var entries = new ShrinkingManifestEntries(
                new DataTableManifestEntry("first"),
                new DataTableManifestEntry("second"));

            Assert.Throws<IndexOutOfRangeException>(() =>
                new DataTableManifest(
                    schemaVersion: 1,
                    entries: entries,
                    limits: DataTableLoadLimits.Default));
        }

        private static DataTableCatalog CreatePairCatalog(int version)
        {
            return new DataTableCatalogBuilder(2)
                .Add(new PairLeft(version))
                .Add(new PairRight(version))
                .Build();
        }

        private static DataTableRevision CreateRevision(long sequence)
        {
            return new DataTableRevision(sequence, "revision-" + sequence);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference PublishThenFailRetirement(
            DataTableStore store,
            ThrowOnceDisposable owner)
        {
            var retiredTable = new RetentionProbeTable();
            var retiredTableReference = new WeakReference(retiredTable);
            DataTableCatalog catalog = new DataTableCatalogBuilder(1)
                .Add(retiredTable)
                .Build();

            using (var first = new DataTableCandidate(catalog, CreateRevision(1), owner))
            using (var second = new DataTableCandidate(CreatePairCatalog(2), CreateRevision(2)))
            {
                Assert.IsTrue(store.TryPublish(first, expectedGeneration: 0).IsCommitted);
                Assert.IsTrue(store.TryPublish(second, expectedGeneration: 1).IsCommitted);
            }

            return retiredTableReference;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private sealed class TestRow : IDataRow
        {
            public int Id { get; set; }

            public string Value { get; set; }
        }

        private sealed class ExternalGeneratedRow
        {
            public string Code { get; set; }
        }

        private readonly struct WideValueRow : IDataRow, IEquatable<WideValueRow>
        {
            public WideValueRow(int id, long value)
            {
                Id = id;
                Value = value;
                Padding0 = value + 1;
                Padding1 = value + 2;
                Padding2 = value + 3;
                Padding3 = value + 4;
                Padding4 = value + 5;
                Padding5 = value + 6;
            }

            public int Id { get; }

            public long Value { get; }

            public long Padding0 { get; }

            public long Padding1 { get; }

            public long Padding2 { get; }

            public long Padding3 { get; }

            public long Padding4 { get; }

            public long Padding5 { get; }

            public bool Equals(WideValueRow other)
            {
                return Id == other.Id && Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                return obj is WideValueRow other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Id * 397) ^ Value.GetHashCode();
                }
            }
        }

        private sealed class PairLeft
        {
            public PairLeft(int version)
            {
                Version = version;
            }

            public int Version { get; }
        }

        private sealed class PairRight
        {
            public PairRight(int version)
            {
                Version = version;
            }

            public int Version { get; }
        }

        private sealed class RetentionProbeTable
        {
        }

        private sealed class CountingDisposable : IDisposable
        {
            private int _disposeCount;

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        private sealed class ThrowOnceDisposable : IDisposable
        {
            private int _disposeCount;

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref _disposeCount) == 1)
                {
                    throw new InvalidOperationException("Expected first resource-owner disposal failure.");
                }
            }
        }

        private sealed class RetryScriptDisposable : IDisposable
        {
            private int _disposeCount;

            public RetryScriptDisposable(bool throwOutOfMemoryOnSecondAttempt)
            {
                ThrowOutOfMemoryOnSecondAttempt = throwOutOfMemoryOnSecondAttempt;
            }

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public bool ThrowOutOfMemoryOnSecondAttempt { get; set; }

            public void Dispose()
            {
                int attempt = Interlocked.Increment(ref _disposeCount);
                if (attempt == 1)
                {
                    throw new InvalidOperationException("Expected initial resource-owner disposal failure.");
                }

                if (attempt == 2 && ThrowOutOfMemoryOnSecondAttempt)
                {
                    throw new OutOfMemoryException("Expected fatal resource-owner retry failure.");
                }
            }
        }

        private sealed class ReentrantDisposable : IDisposable
        {
            private readonly Action _disposeAction;
            private int _disposeCount;

            public ReentrantDisposable(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
                _disposeAction();
            }
        }

        public enum DiagnosticFailurePoint
        {
            IsEnabled,
            Write,
            WriteException,
            OutOfMemory
        }

        private sealed class ThrowingDataTableDiagnostics : IDataTableDiagnostics
        {
            private readonly DiagnosticFailurePoint _failurePoint;

            public ThrowingDataTableDiagnostics(DiagnosticFailurePoint failurePoint)
            {
                _failurePoint = failurePoint;
            }

            public bool IsEnabled(DataTableDiagnosticLevel level, string category)
            {
                if (_failurePoint == DiagnosticFailurePoint.OutOfMemory)
                {
                    throw new OutOfMemoryException("Expected diagnostic sink failure.");
                }

                if (_failurePoint == DiagnosticFailurePoint.IsEnabled)
                {
                    Throw();
                }

                return true;
            }

            public void Write(
                DataTableDiagnosticLevel level,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (_failurePoint == DiagnosticFailurePoint.Write)
                {
                    Throw();
                }
            }

            public void WriteException(
                DataTableDiagnosticLevel level,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (_failurePoint == DiagnosticFailurePoint.WriteException)
                {
                    Throw();
                }
            }

            private static void Throw() =>
                throw new InvalidOperationException("Expected diagnostic sink failure.");
        }

        private sealed class SinglePayloadProvider : IDataTableBytesProvider
        {
            private readonly string _tableName;
            private readonly ReadOnlyMemory<byte> _payload;

            public SinglePayloadProvider(string tableName, ReadOnlyMemory<byte> payload)
            {
                _tableName = tableName;
                _payload = payload;
            }

            public ReadOnlyMemory<byte> GetBytes(string tableName)
            {
                if (TryGetBytes(tableName, out ReadOnlyMemory<byte> bytes))
                {
                    return bytes;
                }

                throw new KeyNotFoundException(tableName);
            }

            public bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes)
            {
                if (string.Equals(tableName, _tableName, StringComparison.OrdinalIgnoreCase))
                {
                    bytes = _payload;
                    return true;
                }

                bytes = default;
                return false;
            }
        }

        private sealed class ShrinkingManifestEntries : IReadOnlyList<DataTableManifestEntry>
        {
            private readonly DataTableManifestEntry[] _entries;
            private int _count;

            public ShrinkingManifestEntries(params DataTableManifestEntry[] entries)
            {
                _entries = entries;
                _count = entries.Length;
            }

            public int Count => _count;

            public DataTableManifestEntry this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)_count)
                    {
                        throw new IndexOutOfRangeException();
                    }

                    DataTableManifestEntry entry = _entries[index];
                    if (index == 0)
                    {
                        _count = 1;
                    }

                    return entry;
                }
            }

            public IEnumerator<DataTableManifestEntry> GetEnumerator()
            {
                for (int i = 0; i < _count; i++)
                {
                    yield return this[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class UnboundedRows : IEnumerable<TestRow>
        {
            public int MoveNextCount { get; private set; }

            public IEnumerator<TestRow> GetEnumerator()
            {
                int id = 0;
                while (true)
                {
                    MoveNextCount++;
                    yield return new TestRow { Id = id++, Value = "row" };
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
