using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Logger;

namespace CycloneGames.Utility.Runtime
{
    public static class FileUtility
    {
        private const string DEBUG_FLAG = "[FileUtility]";

#if UNITY_IOS || UNITY_ANDROID
        private const int BufferSize = 81920; // 80KB for mobile 
#else
        private const int BufferSize = 32768; // 32KB for PC 
#endif

        private const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB 
        private static readonly ThreadLocal<SHA256> ThreadLocalSha256 = new ThreadLocal<SHA256>(SHA256.Create);

        private static byte[] GetBuffer() => ArrayPool<byte>.Shared.Rent(BufferSize);
        private static void ReturnBuffer(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!File.Exists(filePath1) || !File.Exists(filePath2))
                    return false;

                FileInfo fileInfo1, fileInfo2;
                try
                {
                    fileInfo1 = new FileInfo(filePath1);
                    fileInfo2 = new FileInfo(filePath2);
                }
                catch (Exception ex)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Error getting file info: {ex.Message}");
                    return false;
                }

                if (fileInfo1.Length != fileInfo2.Length)
                    return false;

                cancellationToken.ThrowIfCancellationRequested();

                return fileInfo1.Length > LargeFileThreshold
                    ? await AreFilesEqualByChunksAsync(filePath1, filePath2, cancellationToken).ConfigureAwait(false)
                    : await AreFilesEqualByHashAsync(filePath1, filePath2, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sw.Stop();
                CLogger.LogDebug($"{DEBUG_FLAG} File comparison took {sw.ElapsedMilliseconds}ms");
            }
        }

        private static async Task<bool> AreFilesEqualByHashAsync(string filePath1, string filePath2, CancellationToken cancellationToken)
        {
            try
            {
                var sha256 = ThreadLocalSha256.Value;
                byte[] hash1 = await ComputeHashAsync(sha256, filePath1, cancellationToken).ConfigureAwait(false);
                byte[] hash2 = await ComputeHashAsync(sha256, filePath2, cancellationToken).ConfigureAwait(false);
                return CompareByteArrays(hash1, hash2);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Error comparing files by hash: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> AreFilesEqualByChunksAsync(string filePath1, string filePath2, CancellationToken cancellationToken)
        {
            var buffer1 = GetBuffer();
            var buffer2 = GetBuffer();
            try
            {
                using (var fs1 = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                using (var fs2 = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    int bytesRead1, bytesRead2;
                    while ((bytesRead1 = await fs1.ReadAsync(buffer1, 0, BufferSize, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        bytesRead2 = await fs2.ReadAsync(buffer2, 0, BufferSize, cancellationToken).ConfigureAwait(false);
                        if (bytesRead1 != bytesRead2 || !CompareByteArrays(buffer1, buffer2, bytesRead1))
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Error comparing files by chunks: {ex.Message}");
                return false;
            }
            finally
            {
                ReturnBuffer(buffer1);
                ReturnBuffer(buffer2);
            }
        }

        private static async Task<byte[]> ComputeHashAsync(HashAlgorithm hashAlgorithm, string filePath, CancellationToken cancellationToken)
        {
            var buffer = GetBuffer();
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    int bytesRead;
                    hashAlgorithm.Initialize();
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, BufferSize, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }
                    hashAlgorithm.TransformFinalBlock(buffer, 0, 0);
                    return hashAlgorithm.Hash;
                }
            }
            finally
            {
                ReturnBuffer(buffer);
            }
        }

        public static async Task CopyFileWithComparisonAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!File.Exists(sourceFilePath))
                {
                    CLogger.LogError($"{DEBUG_FLAG} Source file does not exist: {sourceFilePath}");
                    return;
                }

                string directoryPath = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                if (File.Exists(destinationFilePath))
                {
                    if (await AreFilesEqualAsync(sourceFilePath, destinationFilePath, cancellationToken).ConfigureAwait(false))
                    {
                        CLogger.LogInfo($"{DEBUG_FLAG} Files identical, skipping copy.");
                        return;
                    }
                    File.Delete(destinationFilePath);
                }

                var buffer = GetBuffer();
                try
                {
                    using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                    using (var destinationStream = new FileStream(destinationFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                    {
                        int bytesRead;
                        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, BufferSize, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    CLogger.LogInfo($"{DEBUG_FLAG} File copied successfully.");
                }
                finally
                {
                    ReturnBuffer(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} File copy operation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Error during file operation: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                CLogger.LogDebug($"{DEBUG_FLAG} File copy took {sw.ElapsedMilliseconds}ms");
            }
        }

        private static unsafe bool CompareByteArrays(byte[] array1, byte[] array2, int length = -1)
        {
            if (length == -1)
            {
                if (array1.Length != array2.Length)
                    return false;
                length = array1.Length;
            }

            if (array1.Length < length || array2.Length < length)
                return false;

            fixed (byte* p1 = array1, p2 = array2)
            {
                byte* x1 = p1, x2 = p2;
                for (int i = 0; i < length / 8; i++, x1 += 8, x2 += 8)
                {
                    if (*(long*)x1 != *(long*)x2) return false;
                }

                if ((length & 4) != 0)
                {
                    if (*(int*)x1 != *(int*)x2) return false;
                    x1 += 4;
                    x2 += 4;
                }

                if ((length & 2) != 0)
                {
                    if (*(short*)x1 != *(short*)x2) return false;
                    x1 += 2;
                    x2 += 2;
                }

                if ((length & 1) != 0)
                {
                    if (*x1 != *x2) return false;
                }
            }

            return true;
        }
    }
}