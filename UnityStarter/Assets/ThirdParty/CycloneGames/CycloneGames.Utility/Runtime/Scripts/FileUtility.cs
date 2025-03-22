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
        private const int BufferSize = 8192; // 8KB 

        /// <summary>
        /// Compares two files for equality. For smaller files, uses hash comparison; for larger files, compares in chunks.
        /// </summary>
        /// <param name="filePath1">Path of the first file.</param>
        /// <param name="filePath2">Path of the second file.</param>
        /// <returns>True if files are identical, otherwise false.</returns>
        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2)
        {
            // Check if files exist 
            if (!File.Exists(filePath1) || !File.Exists(filePath2))
            {
                return false;
            }

            FileInfo fileInfo1 = new FileInfo(filePath1);
            FileInfo fileInfo2 = new FileInfo(filePath2);

            // Immediate return if sizes differ 
            if (fileInfo1.Length != fileInfo2.Length)
            {
                return false;
            }

            return fileInfo1.Length > LargeFileThreshold
                ? await AreFilesEqualByChunksAsync(filePath1, filePath2)
                : await AreFilesEqualByHashAsync(filePath1, filePath2);
        }

        /// <summary>
        /// Compares files using SHA256 hash.
        /// </summary>
        private static async Task<bool> AreFilesEqualByHashAsync(string filePath1, string filePath2)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash1 = await ComputeHashAsync(sha256, filePath1);
            byte[] hash2 = await ComputeHashAsync(sha256, filePath2);
            return CompareByteArrays(hash1, hash2);
        }

        /// <summary>
        /// Compares files by reading in chunks (optimized for large files).
        /// </summary>
        private static async Task<bool> AreFilesEqualByChunksAsync(string filePath1, string filePath2)
        {
            try
            {
                // Use FileOptions.SequentialScan to optimize OS file access 
                using FileStream fs1 = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                using FileStream fs2 = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                byte[] buffer1 = new byte[BufferSize];
                byte[] buffer2 = new byte[BufferSize];

                int bytesRead1 = 0, bytesRead2 = 0;
                while ((bytesRead1 = await fs1.ReadAsync(buffer1, 0, BufferSize)) > 0)
                {
                    bytesRead2 = await fs2.ReadAsync(buffer2, 0, BufferSize);
                    if (bytesRead1 != bytesRead2 || !buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                    {
                        return false;
                    }
                }
                return bytesRead1 == bytesRead2;
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
            // using FileOptions.SequentialScan to optimize OS file access 
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
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
                    CLogger.LogError($"{DEBUG_FLAG} Source file does not exist: {sourceFilePath}");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)); // make sure the destination directory exists 

                if (File.Exists(destinationFilePath) && await AreFilesEqualAsync(sourceFilePath, destinationFilePath))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} The files are identical. No copy needed.");
                    return;
                }

                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                    CLogger.LogInfo($"{DEBUG_FLAG} Destination file deleted as it differs from the source.");
                }

                // using FileOptions.SequentialScan to optimize OS file access
                using (FileStream sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                CLogger.LogInfo($"{DEBUG_FLAG} File copied successfully from '{sourceFilePath}' to '{destinationFilePath}'.");
            }
            catch (IOException ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} I/O error during async file copy: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Access error during async file copy: {ex.Message}");
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Unexpected error during async file copy: {ex.Message}");
            }
        }

        /// <summary> 
        /// Compares two byte arrays for equality. 
        /// </summary> 
        private static bool CompareByteArrays(byte[] array1, byte[] array2, int length = -1)
        {
            if (length == -1)
            {
                length = array1.Length;
            }

            if (array1.Length < length || array2.Length < length)
            {
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                if (array1[i] != array2[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}