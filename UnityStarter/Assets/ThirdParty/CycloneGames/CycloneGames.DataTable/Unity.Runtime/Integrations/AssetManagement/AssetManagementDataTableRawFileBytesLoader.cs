using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.DataTable.Unity.Integrations.AssetManagement
{
    /// <summary>
    /// Main-thread-owned bounded loader for provider raw-file payloads. One instance accepts only
    /// one in-flight load operation; returned memory is borrowed until <see cref="Dispose"/>.
    /// Editor file fallback is opt-in and restricted to canonical paths below Assets/.
    /// </summary>
    public sealed class AssetManagementDataTableRawFileBytesLoader :
        IDataTableBytesProvider,
        IDataTableBytesInventory,
        IDisposable
    {
        private readonly AssetBucketScope _assetScope;
        private readonly IDataTableLocationResolver _locationResolver;
        private readonly DataTableBytesCache _bytesCache;
        private readonly DataTableManifest _manifest;
        private readonly DataTableLoadLimits _limits;
        private readonly bool _enableEditorFileFallback;
        private readonly int _ownerThreadId;
        private readonly CancellationTokenSource _lifetimeCancellation;
        private readonly DataTableAssetHandleRetirement _handleRetirement =
            new DataTableAssetHandleRetirement();
        private IRawFileHandle _activeHandle;
        private bool _activeHandleDisposedByOwner;
        private bool _loadInProgress;
        private bool _disposed;
        private bool _disposeCompleted;
        private bool _lifetimeCancellationRequested;
        private bool _lifetimeCancellationDisposed;
        private bool _bytesCacheDisposed;

        public AssetManagementDataTableRawFileBytesLoader(
            IAssetPackage assetPackage,
            string bucketName,
            IDataTableLocationResolver locationResolver,
            string owner = null,
            bool enableEditorFileFallback = false,
            int initialCapacity = 8,
            DataTableManifest manifest = null,
            DataTableLoadLimits? limits = null)
            : this(
                assetPackage,
                new DataTableAssetLoadContext(bucketName, owner: owner),
                locationResolver,
                enableEditorFileFallback,
                initialCapacity,
                manifest,
                limits)
        {
        }

        public AssetManagementDataTableRawFileBytesLoader(
            IAssetPackage assetPackage,
            DataTableAssetLoadContext loadContext,
            IDataTableLocationResolver locationResolver,
            bool enableEditorFileFallback = false,
            int initialCapacity = 8,
            DataTableManifest manifest = null,
            DataTableLoadLimits? limits = null)
        {
            if (assetPackage == null)
            {
                throw new ArgumentNullException(nameof(assetPackage));
            }

            if (assetPackage is not IAssetRawFileLoader)
            {
                throw new NotSupportedException(
                    $"Asset package '{assetPackage.Name}' does not implement {nameof(IAssetRawFileLoader)}.");
            }

            if (string.IsNullOrWhiteSpace(loadContext.Bucket))
            {
                throw new ArgumentException("Bucket name is null or empty.", nameof(loadContext));
            }

            _locationResolver = locationResolver ?? throw new ArgumentNullException(nameof(locationResolver));
            _limits = limits ?? DataTableLoadLimits.Default;
            _ownerThreadId = DataTableAssetLoaderUtility.CaptureOwnerThread();
            _bytesCache = new DataTableBytesCache(_limits, initialCapacity);
            _lifetimeCancellation = new CancellationTokenSource();
            try
            {
                _assetScope = assetPackage.CreateBucketScope(
                    loadContext.Bucket,
                    loadContext.Tag,
                    loadContext.Owner) ?? throw new InvalidOperationException(
                        "Asset package returned a null bucket scope.");
            }
            catch
            {
                _lifetimeCancellation.Dispose();
                _bytesCache.Dispose();
                throw;
            }

            _manifest = manifest;
            _enableEditorFileFallback = enableEditorFileFallback;
        }

        public async UniTask LoadAsync(
            IReadOnlyList<string> tableNames,
            CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            string[] tableNameSnapshot = CreateTableNameSnapshot(tableNames);

            string[] addedTableNames = tableNameSnapshot.Length == 0
                ? Array.Empty<string>()
                : new string[tableNameSnapshot.Length];
            int addedTableCount = 0;
            BeginLoad();
            try
            {
                using CancellationTokenSource linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _lifetimeCancellation.Token);
                for (int i = 0; i < tableNameSnapshot.Length; i++)
                {
                    string addedTableName = await LoadCoreAsync(
                        tableNameSnapshot[i],
                        linkedCancellation.Token);
                    if (addedTableName != null)
                    {
                        addedTableNames[addedTableCount++] = addedTableName;
                    }
                }

                _manifest?.ValidateInventory(this);
            }
            catch
            {
                RollbackAddedTables(addedTableNames, addedTableCount);
                throw;
            }
            finally
            {
                EndLoad();
            }
        }

        public async UniTask LoadAsync(
            string tableName,
            CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            string normalizedName = _limits.NormalizeTableName(tableName);

            BeginLoad();
            try
            {
                using CancellationTokenSource linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _lifetimeCancellation.Token);
                await LoadCoreAsync(normalizedName, linkedCancellation.Token);
            }
            finally
            {
                EndLoad();
            }
        }

        private async UniTask<string> LoadCoreAsync(
            string normalizedName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_bytesCache.TryGetBytes(normalizedName, out _))
            {
                return null;
            }

            string location = ResolveLocation(normalizedName);
            IRawFileHandle handle = null;
            bool loadCompletedSuccessfully = false;
            try
            {
                handle = _assetScope.LoadRawFileAsync(
                    location,
                    cancellationToken: cancellationToken);
                if (handle == null)
                {
                    throw new InvalidOperationException("Raw file handle is null.");
                }

                _activeHandle = handle;
                await handle.Task.AttachExternalCancellation(cancellationToken);
                await DataTableAssetLoaderUtility.SwitchToOwnerThread(
                    _ownerThreadId,
                    nameof(AssetManagementDataTableRawFileBytesLoader));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureUsable();

                byte[] ownedBytes = handle.ReadBytes();
                cancellationToken.ThrowIfCancellationRequested();
                if (ownedBytes == null)
                {
                    throw new InvalidOperationException(
                        $"Raw file handle returned no data. Table={normalizedName}, Location={location}");
                }

                ValidatePayload(normalizedName, ownedBytes);
                cancellationToken.ThrowIfCancellationRequested();
                // IRawFileHandle.ReadBytes returns a caller-owned snapshot. Transfer that array
                // directly into the cache; cloning it here would double the cold-load peak for
                // every raw table without adding an ownership boundary.
                _bytesCache.AddOwned(normalizedName, ownedBytes);
                loadCompletedSuccessfully = true;
                return normalizedName;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                throw;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                await DataTableAssetLoaderUtility.SwitchToOwnerThread(
                    _ownerThreadId,
                    nameof(AssetManagementDataTableRawFileBytesLoader));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureUsable();
                byte[] fallbackBytes = TryLoadEditorFile(location);
                if (fallbackBytes != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidatePayload(normalizedName, fallbackBytes);
                    cancellationToken.ThrowIfCancellationRequested();
                    _bytesCache.AddOwned(normalizedName, fallbackBytes);
                    loadCompletedSuccessfully = true;
                    return normalizedName;
                }

                throw;
            }
            finally
            {
                await DataTableAssetLoaderUtility.SwitchToOwnerThread(
                    _ownerThreadId,
                    nameof(AssetManagementDataTableRawFileBytesLoader));
                bool handleDisposedByOwner = _activeHandleDisposedByOwner;
                if (ReferenceEquals(_activeHandle, handle))
                {
                    _activeHandle = null;
                }

                _activeHandleDisposedByOwner = false;
                if (!handleDisposedByOwner)
                {
                    try
                    {
                        _handleRetirement.DisposeOrRetain(
                            handle,
                            nameof(AssetManagementDataTableRawFileBytesLoader),
                            normalizedName,
                            suppressRecoverableFailure: !loadCompletedSuccessfully);
                    }
                    catch
                    {
                        if (loadCompletedSuccessfully && !_disposed)
                        {
                            _bytesCache.Remove(normalizedName);
                        }

                        throw;
                    }
                }
            }
        }

        public ReadOnlyMemory<byte> GetBytes(string tableName)
        {
            EnsureUsable();
            return _bytesCache.GetBytes(tableName);
        }

        public bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes)
        {
            EnsureUsable();
            return _bytesCache.TryGetBytes(tableName, out bytes);
        }

        public int Count
        {
            get
            {
                EnsureUsable();
                return _bytesCache.Count;
            }
        }

        public string GetTableName(int index)
        {
            EnsureUsable();
            return _bytesCache.GetTableName(index);
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (_disposeCompleted)
            {
                return;
            }

            _disposed = true;
            Exception cleanupFailure = null;
            if (!_lifetimeCancellationRequested)
            {
                _lifetimeCancellationRequested = true;
                try
                {
                    _lifetimeCancellation.Cancel();
                }
                catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
                {
                    cleanupFailure = exception;
                }
            }

            IRawFileHandle activeHandle = _activeHandle;
            if (activeHandle != null)
            {
                _activeHandleDisposedByOwner = true;
                _activeHandle = null;
                try
                {
                    _handleRetirement.DisposeOrRetain(
                        activeHandle,
                        nameof(AssetManagementDataTableRawFileBytesLoader),
                        "<active>",
                        suppressRecoverableFailure: false);
                }
                catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
                {
                    cleanupFailure ??= exception;
                }
            }
            else if (_handleRetirement.HasPending)
            {
                try
                {
                    _handleRetirement.Retry(nameof(AssetManagementDataTableRawFileBytesLoader));
                }
                catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
                {
                    cleanupFailure ??= exception;
                }
            }

            if (!_bytesCacheDisposed)
            {
                try
                {
                    _bytesCache.Dispose();
                    _bytesCacheDisposed = true;
                }
                catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
                {
                    cleanupFailure ??= exception;
                }
            }

            if (!_lifetimeCancellationDisposed)
            {
                try
                {
                    _lifetimeCancellation.Dispose();
                    _lifetimeCancellationDisposed = true;
                }
                catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
                {
                    cleanupFailure ??= exception;
                }
            }

            _disposeCompleted = cleanupFailure == null && !_handleRetirement.HasPending;
            if (cleanupFailure != null)
            {
                throw new InvalidOperationException(
                    "One or more DataTable loader resources failed to shut down cleanly.",
                    cleanupFailure);
            }
        }

        /// <summary>
        /// True when a provider handle release failed and the exact ownership edge is retained for retry.
        /// This owner-thread-only property remains available after loader disposal starts.
        /// </summary>
        public bool HasPendingHandleDisposal
        {
            get
            {
                EnsureOwnerThread();
                return _handleRetirement.HasPending;
            }
        }

        /// <summary>
        /// Retries the retained provider handle release on the owner thread. A recoverable failure
        /// throws and leaves the same handle retained for another explicit retry.
        /// </summary>
        public void RetryPendingHandleDisposal()
        {
            EnsureOwnerThread();
            _handleRetirement.Retry(nameof(AssetManagementDataTableRawFileBytesLoader));
        }

        private byte[] TryLoadEditorFile(string assetPath)
        {
            if (!_enableEditorFileFallback)
            {
                return null;
            }

#if UNITY_EDITOR
            string fullPath = DataTableAssetLoaderUtility.ResolveProjectRelativeAssetPath(assetPath);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return null;
            }

            long remainingBudget = _limits.MaxTotalBytes - _bytesCache.TotalBytes;
            long maximumBytes = Math.Min(_limits.MaxBytesPerTable, remainingBudget);
            return DataTableAssetLoaderUtility.ReadBoundedFile(
                fullPath,
                maximumBytes,
                assetPath);
#else
            return null;
#endif
        }

        private string ResolveLocation(string normalizedName)
        {
            string location;
            if (_manifest != null &&
                _manifest.TryGetEntry(normalizedName, out DataTableManifestEntry entry))
            {
                location = entry.HasLocation
                    ? entry.Location
                    : _locationResolver.Resolve(entry.TableName);
            }
            else
            {
                location = _locationResolver.Resolve(normalizedName);
            }

            location = _limits.NormalizeLocation(location);
            if (location.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Data-table location resolver returned an empty location. Table={normalizedName}");
            }

            return location;
        }

        private void ValidatePayload(string normalizedName, ReadOnlyMemory<byte> bytes)
        {
            _limits.ValidatePayloadLength(normalizedName, bytes.Length);
            _limits.ValidateTotalBytes(checked(_bytesCache.TotalBytes + bytes.Length));
            _manifest?.ValidatePayload(normalizedName, bytes);
        }

        private string[] CreateTableNameSnapshot(IReadOnlyList<string> tableNames)
        {
            if (tableNames == null)
            {
                throw new ArgumentNullException(nameof(tableNames));
            }

            int count = tableNames.Count;
            _limits.ValidateTableCount(count);
            if (count == 0)
            {
                return Array.Empty<string>();
            }

            var snapshot = new string[count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = _limits.NormalizeTableName(tableNames[i]);
            }

            return snapshot;
        }

        private void RollbackAddedTables(string[] addedTableNames, int addedTableCount)
        {
            if (_disposed)
            {
                return;
            }

            for (int i = addedTableCount - 1; i >= 0; i--)
            {
                _bytesCache.Remove(addedTableNames[i]);
            }
        }

        private void BeginLoad()
        {
            if (_handleRetirement.HasPending)
            {
                throw new InvalidOperationException(
                    "A provider handle release is pending. Call RetryPendingHandleDisposal before loading again.");
            }

            if (_loadInProgress)
            {
                throw new InvalidOperationException(
                    "Concurrent load operations are not supported by this loader instance.");
            }

            _loadInProgress = true;
        }

        private void EndLoad()
        {
            _loadInProgress = false;
        }

        private void EnsureUsable()
        {
            EnsureOwnerThread();
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AssetManagementDataTableRawFileBytesLoader));
            }
        }

        private void EnsureOwnerThread()
        {
            DataTableAssetLoaderUtility.EnsureOwnerThread(
                _ownerThreadId,
                nameof(AssetManagementDataTableRawFileBytesLoader));
        }
    }
}
