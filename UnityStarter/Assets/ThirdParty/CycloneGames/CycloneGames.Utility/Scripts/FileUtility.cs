using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CycloneGames.Logger;

namespace CycloneGames.Utility.Runtime
{
    public static class FileUtility
    {
        private const string DEBUG_FLAG = "[FileUtility]";
        private const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB

        /// <summary>
        /// Compares two files for equality. For smaller files, uses hash comparison; for larger files, compares in chunks.
        /// </summary>
        /// <param name="filePath1">The path of the first file.</param>
        /// <param name="filePath2">The path of the second file.</param>
        /// <returns>Whether the files are equal.</returns>
        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2)
        {
            FileInfo fileInfo1 = new FileInfo(filePath1);
            FileInfo fileInfo2 = new FileInfo(filePath2);

            // If file sizes are different, return false immediately
            if (fileInfo1.Length != fileInfo2.Length)
            {
                return false;
            }

            // Choose comparison strategy based on file size
            return fileInfo1.Length > LargeFileThreshold 
                ? await AreFilesEqualByChunksAsync(filePath1, filePath2) 
                : await AreFilesEqualByHashAsync(filePath1, filePath2);
        }

        /// <summary>
        /// Compares two files using hash.
        /// </summary>
        private static async Task<bool> AreFilesEqualByHashAsync(string filePath1, string filePath2)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash1 = await ComputeHashAsync(sha256, filePath1);
            byte[] hash2 = await ComputeHashAsync(sha256, filePath2);
            return hash1.AsSpan().SequenceEqual(hash2);
        }

        /// <summary>
        /// Compares two files by reading them in chunks.
        /// </summary>
        private static async Task<bool> AreFilesEqualByChunksAsync(string filePath1, string filePath2)
        {
            const int bufferSize = 8192; // 8KB
            try
            {
                using FileStream fs1 = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read);
                using FileStream fs2 = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer1 = new byte[bufferSize];
                byte[] buffer2 = new byte[bufferSize];

                int bytesRead1, bytesRead2 = 0;
                while ((bytesRead1 = await fs1.ReadAsync(buffer1, 0, bufferSize)) > 0 &&
                       (bytesRead2 = await fs2.ReadAsync(buffer2, 0, bufferSize)) > 0)
                {
                    if (bytesRead1 != bytesRead2 || !buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                    {
                        return false;
                    }
                }

                return bytesRead1 == bytesRead2; // Ensure both files are equal at the end
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Computes the hash of a file.
        /// </summary>
        private static async Task<byte[]> ComputeHashAsync(HashAlgorithm hashAlgorithm, string filePath)
        {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await Task.Run(() => hashAlgorithm.ComputeHash(stream));
        }

        /// <summary>
        /// Copies a file only if the destination file does not exist or the contents are different.
        /// </summary>
        public static async Task CopyFileWithComparisonAsync(string sourceFilePath, string destinationFilePath)
        {
            try
            {
                if (!File.Exists(sourceFilePath))
                {
                    MLogger.LogError($"{DEBUG_FLAG} Source file does not exist: {sourceFilePath}");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)); // Ensure the destination directory exists

                if (File.Exists(destinationFilePath) && await AreFilesEqualAsync(sourceFilePath, destinationFilePath))
                {
                    MLogger.LogInfo($"{DEBUG_FLAG} The files are identical. No copy needed.");
                    return;
                }

                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                    MLogger.LogInfo($"{DEBUG_FLAG} Destination file deleted as it differs from the source.");
                }

                using (FileStream sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                MLogger.LogInfo($"{DEBUG_FLAG} File copied successfully from '{sourceFilePath}' to '{destinationFilePath}'.");
            }
            catch (IOException ex)
            {
                MLogger.LogError($"{DEBUG_FLAG} I/O error during async file copy: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                MLogger.LogError($"{DEBUG_FLAG} Access error during async file copy: {ex.Message}");
            }
            catch (Exception ex)
            {
                MLogger.LogError($"{DEBUG_FLAG} Unexpected error during async file copy: {ex.Message}");
            }
        }
    }
}