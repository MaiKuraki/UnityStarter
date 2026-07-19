using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace CycloneGames.DataTable.Unity.Integrations.AssetManagement
{
    public readonly struct DataTableAssetLoadContext
    {
        public readonly string Bucket;
        public readonly string Tag;
        public readonly string Owner;

        public DataTableAssetLoadContext(
            string bucket,
            string tag = null,
            string owner = null)
        {
            Bucket = bucket;
            Tag = tag;
            Owner = owner;
        }

        public bool HasAnyMetadata =>
            !string.IsNullOrEmpty(Bucket) ||
            !string.IsNullOrEmpty(Tag) ||
            !string.IsNullOrEmpty(Owner);

        public DataTableAssetLoadContext Merge(in DataTableAssetLoadContext fallback)
        {
            return new DataTableAssetLoadContext(
                Bucket ?? fallback.Bucket,
                Tag ?? fallback.Tag,
                Owner ?? fallback.Owner);
        }

        public DataTableAssetLoadContext WithOwner(string owner)
        {
            return new DataTableAssetLoadContext(Bucket, Tag, owner);
        }

        public static DataTableAssetLoadContext FromScope(AssetBucketScope scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            return new DataTableAssetLoadContext(
                scope.Bucket,
                scope.Tag,
                scope.Owner);
        }
    }

    internal static class DataTableAssetLoaderUtility
    {
        public static int CaptureOwnerThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "DataTable AssetManagement loaders must be created on the Unity main thread.");
            }

            return Thread.CurrentThread.ManagedThreadId;
        }

        public static void EnsureOwnerThread(int ownerThreadId, string ownerName)
        {
            if (PlayerLoopHelper.IsMainThread &&
                Thread.CurrentThread.ManagedThreadId == ownerThreadId)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{ownerName} is thread-affine and must be used and disposed on its creating thread.");
        }

        public static async UniTask SwitchToOwnerThread(
            int ownerThreadId,
            string ownerName)
        {
            if (!PlayerLoopHelper.IsMainThread ||
                Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                // Do not attach the already-cancelled load token: cleanup must reach the Unity
                // owner thread before a handle can be observed or released safely.
                await UniTask.SwitchToMainThread();
            }

            EnsureOwnerThread(ownerThreadId, ownerName);
        }

        public static void LogSuppressedCleanupFailure(
            string ownerName,
            string tableName,
            Exception exception)
        {
            try
            {
                DataTableLogger.LogError(
                    $"{ownerName} suppressed a handle cleanup failure to preserve the primary " +
                    $"load exception. Table={tableName}, CleanupException={exception.GetType().FullName}: {exception.Message}");
            }
            catch (Exception)
            {
                // Cleanup diagnostics are best-effort and must not replace the primary failure.
            }
        }

        public static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException &&
                   exception is not StackOverflowException &&
                   exception is not AccessViolationException &&
                   exception is not ThreadAbortException;
        }

#if UNITY_EDITOR
        public static byte[] ReadBoundedFile(
            string fullPath,
            long maximumBytes,
            string displayPath)
        {
            if (maximumBytes <= 0)
            {
                throw new InvalidDataException(
                    $"Editor fallback has no remaining byte budget. Location={displayPath}, Remaining={maximumBytes}");
            }

            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: false);
            long fileLength = stream.Length;
            if (fileLength <= 0)
            {
                return null;
            }

            if (fileLength > maximumBytes || fileLength > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Editor fallback payload exceeds its byte budget. " +
                    $"Location={displayPath}, Bytes={fileLength}, Limit={maximumBytes}");
            }

            var bytes = new byte[(int)fileLength];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Editor fallback payload changed or ended during read. Location={displayPath}");
                }

                offset += read;
            }

            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    $"Editor fallback payload grew during read. Location={displayPath}");
            }

            return bytes;
        }

        public static string ResolveProjectRelativeAssetPath(string assetPath)
        {
            string normalizedPath = DataTableNameUtility.NormalizePath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath) ||
                Path.IsPathRooted(normalizedPath) ||
                !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                return string.Empty;
            }

            string assetsRoot = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidatePath = Path.GetFullPath(Path.Combine(projectRoot.FullName, normalizedPath));
            StringComparison comparison = GetPathComparison();
            string assetsPrefix = assetsRoot + Path.DirectorySeparatorChar;

            return candidatePath.StartsWith(assetsPrefix, comparison)
                ? candidatePath
                : string.Empty;
        }

        private static StringComparison GetPathComparison()
        {
#if UNITY_EDITOR_WIN
            return StringComparison.OrdinalIgnoreCase;
#else
            return StringComparison.Ordinal;
#endif
        }
#endif
    }
}
