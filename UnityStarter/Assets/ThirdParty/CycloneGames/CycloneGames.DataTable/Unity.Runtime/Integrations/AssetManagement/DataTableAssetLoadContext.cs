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
        private static readonly DataTableDiagnosticChannel Diagnostics =
            DataTableAssetManagementDiagnostics.Channel;

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
            Diagnostics.TryWriteException(
                DataTableDiagnosticLevel.Error,
                exception,
                $"{ownerName} suppressed a handle cleanup failure to preserve the primary load exception. Table={tableName}.");
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

            if (!candidatePath.StartsWith(assetsPrefix, comparison) ||
                ContainsReparsePoint(assetsRoot, candidatePath))
            {
                return string.Empty;
            }

            return candidatePath;
        }

        private static bool ContainsReparsePoint(string assetsRoot, string candidatePath)
        {
            if (IsExistingReparsePoint(assetsRoot))
            {
                return true;
            }

            for (int i = assetsRoot.Length + 1; i <= candidatePath.Length; i++)
            {
                bool atEnd = i == candidatePath.Length;
                if (!atEnd &&
                    candidatePath[i] != Path.DirectorySeparatorChar &&
                    candidatePath[i] != Path.AltDirectorySeparatorChar)
                {
                    continue;
                }

                string partialPath = candidatePath.Substring(0, i);
                if (IsExistingReparsePoint(partialPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExistingReparsePoint(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                // The optional fallback is fail-closed when containment cannot be inspected.
                return true;
            }
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

    /// <summary>
    /// Owns at most one provider handle whose release has not completed. The handle reference is
    /// cleared only after Dispose succeeds, so recoverable cleanup failures remain explicitly
    /// retryable instead of becoming an unobservable provider leak.
    /// </summary>
    internal sealed class DataTableAssetHandleRetirement
    {
        private IDisposable _pendingHandle;
        private string _pendingTableName;
        private bool _disposeInProgress;

        public bool HasPending => _pendingHandle != null;

        public void DisposeOrRetain(
            IDisposable handle,
            string ownerName,
            string tableName,
            bool suppressRecoverableFailure)
        {
            if (handle == null)
            {
                return;
            }

            if (_pendingHandle != null && !ReferenceEquals(_pendingHandle, handle))
            {
                throw new InvalidOperationException(
                    $"{ownerName} cannot retire another handle while a failed release is pending. " +
                    $"PendingTable={_pendingTableName ?? "<unknown>"}, Table={tableName ?? "<unknown>"}");
            }

            if (_disposeInProgress)
            {
                // Cleanup is owner-thread-affine, so this can only be a callback re-entering the
                // same retirement. The outermost attempt alone may clear retained ownership.
                return;
            }

            _pendingHandle = handle;
            _pendingTableName = tableName;
            _disposeInProgress = true;
            try
            {
                handle.Dispose();
                _pendingHandle = null;
                _pendingTableName = null;
            }
            catch (Exception exception) when (
                suppressRecoverableFailure &&
                DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                DataTableAssetLoaderUtility.LogSuppressedCleanupFailure(
                    ownerName,
                    tableName,
                    exception);
            }
            catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                throw new InvalidOperationException(
                    $"{ownerName} could not release its provider handle. " +
                    $"Table={tableName ?? "<unknown>"}. Call RetryPendingHandleDisposal on the owner thread.",
                    exception);
            }
            finally
            {
                _disposeInProgress = false;
            }
        }

        public void Retry(string ownerName)
        {
            IDisposable pendingHandle = _pendingHandle;
            if (pendingHandle == null)
            {
                return;
            }

            DisposeOrRetain(
                pendingHandle,
                ownerName,
                _pendingTableName,
                suppressRecoverableFailure: false);
        }
    }
}
