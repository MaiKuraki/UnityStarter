using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace CycloneGames.IO.Tests.SystemIO
{
    public sealed class SystemFileStoreTests
    {
        [Test]
        public void ReadBytes_FileExceedsLimit_ThrowsWithoutReturningPartialContent()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.GetPath("bounded.bin");
                File.WriteAllBytes(path, new byte[17]);

                Assert.Throws<IOException>(() => SystemFileStore.Default.ReadBytes(path, 16));
            }
        }

        [Test]
        public void WriteBytesAtomically_ExistingFile_ReplacesEntireContent()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.GetPath("atomic.bin");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

                SystemFileStore.Default.WriteBytesAtomically(path, new byte[] { 9, 8, 7, 6 });

                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 9, 8, 7, 6 }));
                Assert.That(Directory.GetFiles(directory.Path, "*.tmp"), Is.Empty);
            }
        }

        [Test]
        public void CreateWrite_ExistingFile_TruncatesPreviousContent()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.GetPath("create-write.bin");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

                using (Stream destination = SystemFileStore.Default.CreateWrite(path))
                {
                    destination.WriteByte(9);
                }

                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 9 }));
            }
        }

        [Test]
        public void OpenAppend_ExistingFile_AppendsWithoutTruncating()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.GetPath("append.bin");
                File.WriteAllBytes(path, new byte[] { 1, 2 });

                using (Stream destination = SystemFileStore.Default.OpenAppend(path))
                {
                    destination.Write(new byte[] { 3, 4 }, 0, 2);
                }

                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            }
        }

        [Test]
        public async Task WriteStreamAtomicallyAsync_CancelledMidCopy_PreservesOriginalAndCleansTemporaryFile()
        {
            using (var directory = new TemporaryDirectory())
            using (var cancellation = new CancellationTokenSource())
            {
                string path = directory.GetPath("cancelled.bin");
                byte[] original = Encoding.UTF8.GetBytes("original");
                File.WriteAllBytes(path, original);
                var payload = new byte[SystemFileStoreOptions.MIN_BUFFER_SIZE * 3];

                using (var source = new GatedSecondReadStream(payload))
                {
                    Task<long> writeTask = SystemFileStore.Default.WriteStreamAtomicallyAsync(
                        path,
                        source,
                        cancellationToken: cancellation.Token);
                    await source.SecondReadStarted;
                    cancellation.Cancel();
                    source.ReleaseSecondRead();

                    OperationCanceledException cancellationException = null;
                    try
                    {
                        await writeTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception)
                    {
                        cancellationException = exception;
                    }

                    Assert.That(cancellationException, Is.Not.Null);
                }

                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(directory.Path, "*.tmp"), Is.Empty);
            }
        }

        [Test]
        public async Task WriteStreamAtomicallyAsync_FinalProgressThrows_PreservesOriginalAndCleansTemporaryFile()
        {
            using (var directory = new TemporaryDirectory())
            using (var source = new NonSeekableReadStream(new byte[] { 7, 8, 9 }))
            {
                string path = directory.GetPath("progress-failure.bin");
                byte[] original = { 1, 2, 3 };
                File.WriteAllBytes(path, original);

                InvalidOperationException progressException = null;
                try
                {
                    await SystemFileStore.Default.WriteStreamAtomicallyAsync(
                        path,
                        source,
                        new ThrowOnCompletedProgress()).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    progressException = exception;
                }

                Assert.That(progressException, Is.Not.Null);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(directory.Path, "*.tmp"), Is.Empty);
            }
        }

        [Test]
        public async Task WriteBytesAtomicallyAsync_ConcurrentWriters_NeverCommitsMixedContent()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.GetPath("concurrent.bin");
                var candidates = new List<byte[]>();
                var tasks = new List<Task>();
                for (int i = 0; i < 16; i++)
                {
                    byte[] candidate = Enumerable.Repeat((byte)i, 128 * 1024).ToArray();
                    candidates.Add(candidate);
                    tasks.Add(SystemFileStore.Default.WriteBytesAtomicallyAsync(path, candidate));
                }

                await Task.WhenAll(tasks);

                byte[] committed = File.ReadAllBytes(path);
                Assert.That(candidates.Any(candidate => candidate.SequenceEqual(committed)), Is.True);
                Assert.That(Directory.GetFiles(directory.Path, "*.tmp"), Is.Empty);
                Assert.That(PathCommitCoordinator.EntryCount, Is.Zero);
            }
        }

        [Test]
        public async Task CopyAtomicallyAsync_IdenticalDestination_SkipsReplacement()
        {
            using (var directory = new TemporaryDirectory())
            {
                string sourcePath = directory.GetPath("source.bin");
                string destinationPath = directory.GetPath("destination.bin");
                byte[] content = Encoding.UTF8.GetBytes("identical");
                File.WriteAllBytes(sourcePath, content);
                File.WriteAllBytes(destinationPath, content);
                DateTime timestamp = File.GetLastWriteTimeUtc(destinationPath);

                FileCopyResult result = await SystemFileStore.Default.CopyAtomicallyAsync(
                    sourcePath,
                    destinationPath);

                Assert.That(result, Is.EqualTo(FileCopyResult.SkippedIdentical));
                Assert.That(File.GetLastWriteTimeUtc(destinationPath), Is.EqualTo(timestamp));
            }
        }

        [Test]
        public void FileHasher_KnownSha256_ReturnsCanonicalLowercaseHex()
        {
            using (var directory = new TemporaryDirectory())
            {
                string path = directory.GetPath("hash.bin");
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

                string hash = FileHasher.ComputeHex(path, FileHashAlgorithm.Sha256);

                Assert.That(
                    hash,
                    Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            }
        }

        private sealed class GatedSecondReadStream : MemoryStream
        {
            private readonly TaskCompletionSource<bool> _secondReadStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _secondReadRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _readCount;

            internal GatedSecondReadStream(byte[] content)
                : base(content, false)
            {
            }

            internal Task SecondReadStarted => _secondReadStarted.Task;

            internal void ReleaseSecondRead()
            {
                _secondReadRelease.TrySetResult(true);
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                _readCount++;
                if (_readCount == 2)
                {
                    return ReadAfterReleaseAsync(buffer, offset, count);
                }

                return base.ReadAsync(buffer, offset, count, CancellationToken.None);
            }

            private async Task<int> ReadAfterReleaseAsync(
                byte[] buffer,
                int offset,
                int count)
            {
                _secondReadStarted.TrySetResult(true);
                await _secondReadRelease.Task.ConfigureAwait(false);
                return await base.ReadAsync(
                    buffer,
                    offset,
                    count,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private sealed class NonSeekableReadStream : Stream
        {
            private readonly MemoryStream _source;

            internal NonSeekableReadStream(byte[] content)
            {
                _source = new MemoryStream(content, false);
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _source.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                return _source.ReadAsync(buffer, offset, count, CancellationToken.None);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _source.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class ThrowOnCompletedProgress : IProgress<FileTransferProgress>
        {
            public void Report(FileTransferProgress value)
            {
                if (value.HasKnownTotal && value.ProcessedBytes == value.TotalBytes)
                {
                    throw new InvalidOperationException("Injected progress failure.");
                }
            }
        }
    }
}
