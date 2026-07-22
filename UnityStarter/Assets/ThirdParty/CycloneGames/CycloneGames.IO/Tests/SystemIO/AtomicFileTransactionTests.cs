using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CycloneGames.IO.Tests.SystemIO
{
    public sealed class AtomicFileTransactionTests
    {
        [Test]
        public void Commit_ReplacementUnsupported_FailsClosedWithoutDeletingDestination()
        {
            var operations = new FakeAtomicFileOperations
            {
                DestinationExists = true,
                ReplaceException = new PlatformNotSupportedException("Injected failure.")
            };
            var transaction = new AtomicFileTransaction(SystemFileStoreOptions.Default, operations);

            Assert.Throws<PlatformNotSupportedException>(() =>
                transaction.Commit("temporary", "destination"));
            Assert.That(operations.MoveCount, Is.Zero);
            Assert.That(operations.DeletedPaths, Is.Empty);
            Assert.That(operations.DestinationExists, Is.True);
            Assert.That(PathCommitCoordinator.EntryCount, Is.Zero);
        }

        [Test]
        public void Commit_FirstWriterRace_UsesAtomicReplaceWithoutDeletingDestination()
        {
            var operations = new FakeAtomicFileOperations
            {
                DestinationExists = false,
                SimulateMoveRace = true
            };
            var transaction = new AtomicFileTransaction(SystemFileStoreOptions.Default, operations);

            transaction.Commit("temporary", "destination");

            Assert.That(operations.MoveCount, Is.EqualTo(1));
            Assert.That(operations.ReplaceCount, Is.EqualTo(1));
            Assert.That(operations.DeletedPaths, Is.Empty);
            Assert.That(PathCommitCoordinator.EntryCount, Is.Zero);
        }

        [Test]
        public void Commit_CancelledBeforeCommitPoint_DoesNotMutateStorage()
        {
            var operations = new FakeAtomicFileOperations
            {
                DestinationExists = true
            };
            var transaction = new AtomicFileTransaction(SystemFileStoreOptions.Default, operations);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.Throws<OperationCanceledException>(() =>
                    transaction.Commit("temporary", "destination", cancellation.Token));
            }

            Assert.That(operations.MoveCount, Is.Zero);
            Assert.That(operations.ReplaceCount, Is.Zero);
            Assert.That(operations.DeletedPaths, Is.Empty);
            Assert.That(operations.DestinationExists, Is.True);
            Assert.That(PathCommitCoordinator.EntryCount, Is.Zero);
        }

        [Test]
        public void Commit_CancelledWhileWaitingForCommitGate_DoesNotMutateStorage()
        {
            var operations = new FakeAtomicFileOperations
            {
                DestinationExists = true
            };
            var transaction = new AtomicFileTransaction(SystemFileStoreOptions.Default, operations);
            using (var cancellation = new CancellationTokenSource())
            using (var workerStarted = new ManualResetEventSlim())
            using (var workerCompleted = new ManualResetEventSlim())
            {
                Exception workerException = null;
                Task worker = null;
                IDisposable blockingLease = PathCommitCoordinator.Acquire("destination");
                try
                {
                    worker = Task.Run(() =>
                    {
                        workerStarted.Set();
                        try
                        {
                            transaction.Commit(
                                "temporary",
                                "destination",
                                cancellation.Token);
                        }
                        catch (Exception exception)
                        {
                            workerException = exception;
                        }
                        finally
                        {
                            workerCompleted.Set();
                        }
                    });

                    Assert.That(
                        workerStarted.Wait(TimeSpan.FromSeconds(2)),
                        Is.True,
                        "The commit worker did not start within the bounded test timeout.");
                    cancellation.Cancel();
                    Assert.That(
                        workerCompleted.Wait(TimeSpan.FromMilliseconds(100)),
                        Is.False,
                        "The worker must remain behind the occupied commit gate.");
                }
                finally
                {
                    blockingLease.Dispose();
                }

                Assert.That(
                    workerCompleted.Wait(TimeSpan.FromSeconds(2)),
                    Is.True,
                    "The cancelled worker did not leave the commit gate within the bounded timeout.");
                Assert.That(workerException, Is.TypeOf<OperationCanceledException>());
                Assert.That(worker.Wait(TimeSpan.FromSeconds(2)), Is.True);
            }

            Assert.That(operations.MoveCount, Is.Zero);
            Assert.That(operations.ReplaceCount, Is.Zero);
            Assert.That(operations.DeletedPaths, Is.Empty);
            Assert.That(operations.DestinationExists, Is.True);
            Assert.That(PathCommitCoordinator.EntryCount, Is.Zero);
        }

        private sealed class FakeAtomicFileOperations : IAtomicFileOperations
        {
            internal bool DestinationExists { get; set; }

            internal bool SimulateMoveRace { get; set; }

            internal Exception ReplaceException { get; set; }

            internal int MoveCount { get; private set; }

            internal int ReplaceCount { get; private set; }

            internal List<string> DeletedPaths { get; } = new List<string>();

            public bool Exists(string path)
            {
                return string.Equals(path, "destination", StringComparison.Ordinal)
                    && DestinationExists;
            }

            public void Move(string sourcePath, string destinationPath)
            {
                MoveCount++;
                if (SimulateMoveRace)
                {
                    DestinationExists = true;
                    throw new IOException("Injected destination race.");
                }

                DestinationExists = true;
            }

            public void Replace(string sourcePath, string destinationPath)
            {
                ReplaceCount++;
                if (ReplaceException != null)
                {
                    throw ReplaceException;
                }

                DestinationExists = true;
            }

            public void Delete(string path)
            {
                DeletedPaths.Add(path);
                if (string.Equals(path, "destination", StringComparison.Ordinal))
                {
                    DestinationExists = false;
                }
            }
        }
    }
}
