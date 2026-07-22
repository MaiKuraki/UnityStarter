using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence.SystemIO;
using NUnit.Framework;

namespace CycloneGames.Persistence.Tests.SystemIO
{
    [TestFixture]
    public sealed class SystemFilePersistenceStorageTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.Persistence.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, true);
            }
        }

        [Test]
        public void CreateSandboxed_RejectsTraversalAndRootedPaths()
        {
            Assert.Throws<ArgumentException>(() =>
                SystemFilePersistenceStorage.CreateSandboxed(
                    _temporaryRoot,
                    "../outside.save"));
            Assert.Throws<ArgumentException>(() =>
                SystemFilePersistenceStorage.CreateSandboxed(
                    _temporaryRoot,
                    Path.GetFullPath(Path.Combine(_temporaryRoot, "outside.save"))));
        }

        [Test]
        public async Task Storage_BoundedReadAtomicWriteDeleteAndMissingRoundTrip()
        {
            SystemFilePersistenceStorage storage =
                SystemFilePersistenceStorage.CreateSandboxed(
                    _temporaryRoot,
                    "Saves/slot-001.save");
            byte[] expected = { 1, 2, 3, 4 };

            await storage.WriteAtomicallyAsync(expected);
            PersistenceStorageReadResult result = await storage.ReadAsync(expected.Length);

            Assert.That(result.IsMissing, Is.False);
            Assert.That(result.Content, Is.EqualTo(expected));
            Assert.ThrowsAsync<IOException>(async () =>
                await storage.ReadAsync(expected.Length - 1));

            await storage.DeleteAsync();
            await storage.DeleteAsync();
            PersistenceStorageReadResult missing = await storage.ReadAsync(expected.Length);
            Assert.That(missing.IsMissing, Is.True);
            Assert.That(missing.Content, Is.Null);
        }

        [Test]
        public async Task AlreadyCancelledOperations_DoNotMutateOrReadTheBoundEntry()
        {
            SystemFilePersistenceStorage storage =
                SystemFilePersistenceStorage.CreateSandboxed(
                    _temporaryRoot,
                    "Saves/slot-002.save");
            byte[] original = { 10, 20, 30 };
            await storage.WriteAtomicallyAsync(original);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await storage.ReadAsync(32, cancellation.Token));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await storage.WriteAtomicallyAsync(new byte[] { 99 }, cancellation.Token));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await storage.DeleteAsync(cancellation.Token));

            PersistenceStorageReadResult result = await storage.ReadAsync(32);
            Assert.That(result.Content, Is.EqualTo(original));
        }

        [Test]
        public void Constructor_RejectsRelativePath()
        {
            Assert.Throws<ArgumentException>(() =>
                new SystemFilePersistenceStorage("Saves/slot-001.save"));
        }
    }
}
