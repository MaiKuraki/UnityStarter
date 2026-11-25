using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;
using NUnit.Framework;

namespace CycloneGames.Persistence.Resilience.Tests
{
    public sealed class ResilienceTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "cgp-resilience-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void Options_RejectInvalidValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateOptions(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateOptions(1, 0, 1));
            Assert.Throws<ArgumentException>(() => new ResilienceOptions(
                string.Empty, 3, 4, TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(30), 3, 256, null));
        }

        [Test]
        public void EventSink_KeepsFixedCapacity()
        {
            var sink = new InMemoryEventSink(4);
            for (int index = 0; index < 100; index++)
            {
                var value = new PersistenceEvent(
                    PersistenceEventKind.SaveSucceeded,
                    index,
                    PersistenceErrorCode.None,
                    index,
                    0);
                sink.Record(in value);
            }

            Assert.That(sink.Capacity, Is.EqualTo(4));
            Assert.That(sink.WrittenCount, Is.EqualTo(100));
            Assert.That(sink.CountByKind(PersistenceEventKind.SaveSucceeded), Is.EqualTo(4));
        }

        [Test]
        public void Gate_RejectsBeyondCapacityAndReleasesPermit()
        {
            var gate = new BoundedWriteGate(1, TimeSpan.FromMilliseconds(20), null);
            IDisposable first = gate.AcquireAsync().GetAwaiter().GetResult();

            Assert.ThrowsAsync<TimeoutException>(async () => await gate.AcquireAsync());
            Assert.That(gate.RejectedCount, Is.EqualTo(1));
            first.Dispose();

            IDisposable second = gate.AcquireAsync().GetAwaiter().GetResult();
            Assert.That(gate.AcquiredCount, Is.EqualTo(2));
            second.Dispose();
            gate.Dispose();
        }

        [Test]
        public void Lease_SecondOwnerFailsAndExpiredRecordIsReclaimed()
        {
            string lockPath = Path.Combine(_root, "owner.lock");
            var first = new FileProcessLease(
                lockPath, TimeSpan.FromMinutes(5), 2, null);
            var second = new FileProcessLease(
                lockPath, TimeSpan.FromMinutes(5), 2, null);

            Assert.That(first.TryAcquire(), Is.True);
            Assert.That(second.TryAcquire(), Is.False);
            first.Release();
            Assert.That(second.TryAcquire(), Is.True);
            second.Release();

            WriteExpiredLock(lockPath);
            var third = new FileProcessLease(
                lockPath, TimeSpan.FromSeconds(1), 2, null);
            Assert.That(third.TryAcquire(), Is.True);
            third.Release();
        }

        [Test]
        public async Task ResilientStorage_RoundTripRotatesBackupsAndEmitsEvents()
        {
            var inner = new InMemoryStorage();
            var sink = new InMemoryEventSink(64);
            var options = new ResilienceOptions(
                Path.Combine(_root, "wal"), 2, 2, TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(30), 2, 64, sink);
            var storage = new ResilientStorage(inner, options);

            for (int index = 0; index < 6; index++)
            {
                byte[] content = new byte[8 + index];
                for (int i = 0; i < content.Length; i++)
                {
                    content[i] = (byte)(index + 1);
                }

                await storage.WriteAtomicallyAsync(content);
            }

            PersistenceStorageReadResult read = await storage.ReadAsync(1024);
            Assert.That(read.IsMissing, Is.False);
            Assert.That(read.Content[0], Is.EqualTo(6));

            ResilienceDiagnostics diagnostics = storage.GetDiagnostics();
            Assert.That(diagnostics.BackupCount, Is.EqualTo(2));
            Assert.That(diagnostics.PendingCount, Is.Zero);
            Assert.That(diagnostics.GateAcquiredCount, Is.EqualTo(6));
            Assert.That(sink.WrittenCount, Is.GreaterThan(0));
            Assert.That(sink.CountByKind(PersistenceEventKind.SaveSucceeded), Is.GreaterThan(0));
            storage.Dispose();
        }

        [Test]
        public async Task Recover_KeepsCompletedWrite()
        {
            var inner = new InMemoryStorage();
            var options = CreateOptions(2, 2, 2);
            var storage = new ResilientStorage(inner, options);
            byte[] content = { 1, 2, 3, 4, 5 };
            await storage.WriteAtomicallyAsync(content);

            string pendingDirectory = Path.Combine(_root, "wal");
            string marker = Path.Combine(pendingDirectory, "injected.pending");
            WriteMarker(marker, content, string.Empty);

            RecoveryReport report = await storage.RecoverAsync();

            Assert.That(report.PendingCount, Is.EqualTo(1));
            Assert.That(report.CommittedCount, Is.EqualTo(1));
            Assert.That(report.RestoredCount, Is.Zero);
            Assert.That(File.Exists(marker), Is.False);
            storage.Dispose();
        }

        [Test]
        public async Task Recover_RestoresInterruptedWriteFromBackup()
        {
            var inner = new InMemoryStorage();
            var options = CreateOptions(2, 2, 2);
            var storage = new ResilientStorage(inner, options);
            byte[] backup = { 9, 8, 7 };
            string backupName = "injected.bak";
            File.WriteAllBytes(Path.Combine(_root, "wal", backupName), backup);

            byte[] intended = { 1, 2, 3, 4 };
            string marker = Path.Combine(_root, "wal", "injected.pending");
            WriteMarker(marker, intended, backupName);
            inner.SetRaw(new byte[] { 1, 2 });

            RecoveryReport report = await storage.RecoverAsync();

            Assert.That(report.RestoredCount, Is.EqualTo(1));
            Assert.That(inner.PeekRaw(), Is.EqualTo(backup));
            Assert.That(File.Exists(marker), Is.False);
            storage.Dispose();
        }

        [Test]
        public async Task Recover_ReportsCorruptMarkerWithoutDeletingIt()
        {
            var inner = new InMemoryStorage();
            var options = CreateOptions(2, 2, 2);
            var storage = new ResilientStorage(inner, options);
            string marker = Path.Combine(_root, "wal", "corrupt.pending");
            File.WriteAllBytes(marker, new byte[] { 1, 2, 3 });

            RecoveryReport report = await storage.RecoverAsync();

            Assert.That(report.CorruptCount, Is.EqualTo(1));
            Assert.That(File.Exists(marker), Is.True);
            storage.Dispose();
        }

        private ResilienceOptions CreateOptions(int backups, int capacity, int attempts)
        {
            return new ResilienceOptions(
                Path.Combine(_root, "wal"),
                backups,
                capacity,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(30),
                attempts,
                64,
                null);
        }

        private static void WriteExpiredLock(string path)
        {
            byte[] record = new byte[44];
            record[0] = 32;
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                new Span<byte>(record, 36, 8), 1L);
            File.WriteAllBytes(path, record);
        }

        private static void WriteMarker(string path, byte[] intended, string backupName)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(backupName);
            byte[] record = new byte[20 + nameBytes.Length];
            record[0] = (byte)'C';
            record[1] = (byte)'G';
            record[2] = (byte)'P';
            record[3] = (byte)'W';
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                new Span<byte>(record, 4, 4), intended.Length);
            ulong digest = CycloneGames.Hash.Core.XxHash64.Compute(intended);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
                new Span<byte>(record, 8, 8), digest);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                new Span<byte>(record, 16, 4), nameBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, record, 20, nameBytes.Length);
            File.WriteAllBytes(path, record);
        }

        private sealed class InMemoryStorage : IPersistenceStorage
        {
            private byte[] _content;

            public string Location => "memory://resilience";

            public Task<PersistenceStorageReadResult> ReadAsync(
                int maxByteCount,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    _content == null
                        ? PersistenceStorageReadResult.Missing()
                        : PersistenceStorageReadResult.Found((byte[])_content.Clone()));
            }

            public Task WriteAtomicallyAsync(
                byte[] content,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = (byte[])content.Clone();
                return Task.CompletedTask;
            }

            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = null;
                return Task.CompletedTask;
            }

            internal void SetRaw(byte[] value)
            {
                _content = (byte[])value.Clone();
            }

            internal byte[] PeekRaw()
            {
                return _content == null ? null : (byte[])_content.Clone();
            }
        }
    }
}
