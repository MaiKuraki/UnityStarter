using System;
using System.Collections.Generic;
using System.IO;

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
