using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.DataTable.Unity.Integrations.AssetManagement
{
    /// <summary>
    /// Main-thread-owned bounded loader for TextAsset payloads. One instance accepts only one
    /// in-flight load operation; returned memory is borrowed until <see cref="Dispose"/>.
    /// Editor file fallback is opt-in and restricted to canonical paths below Assets/.
    /// </summary>
    public sealed class AssetManagementDataTableBytesLoader : IDataTableBytesProvider, IDisposable
    {
        private readonly AssetBucketScope _assetScope;
        private readonly IDataTableLocationResolver _locationResolver;
        private readonly DataTableBytesCache _bytesCache;
        private readonly DataTableManifest _manifest;
        private readonly DataTableLoadLimits _limits;
        private readonly bool _enableEditorFileFallback;
        private readonly int _ownerThreadId;
        private readonly CancellationTokenSource _lifetimeCancellation;
        private IAssetHandle<TextAsset> _activeHandle;
        private bool _activeHandleDisposedByOwner;
        private bool _loadInProgress;
        private bool _disposed;

        public AssetManagementDataTableBytesLoader(
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

        public AssetManagementDataTableBytesLoader(
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

                _manifest?.ValidateRequiredTables(this);
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

            BeginLoad();
            try
            {
                using CancellationTokenSource linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _lifetimeCancellation.Token);
                await LoadCoreAsync(tableName, linkedCancellation.Token);
            }
            finally
            {
                EndLoad();
            }
        }

        private async UniTask<string> LoadCoreAsync(
            string tableName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            if (_bytesCache.TryGetBytes(normalizedName, out _))
            {
                return null;
            }

            string location = ResolveLocation(normalizedName);
            IAssetHandle<TextAsset> handle = null;
            bool loadCompletedSuccessfully = false;
            try
            {
                handle = _assetScope.LoadAssetAsync<TextAsset>(
                    location,
                    cancellationToken: cancellationToken);
                if (handle == null)
                {
                    throw new InvalidOperationException("TextAsset handle is null.");
                }

                _activeHandle = handle;
                await handle.Task.AttachExternalCancellation(cancellationToken);
                await DataTableAssetLoaderUtility.SwitchToOwnerThread(
                    _ownerThreadId,
                    nameof(AssetManagementDataTableBytesLoader));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureUsable();

                if (!string.IsNullOrEmpty(handle.Error) || handle.Asset == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load data table asset. Table={normalizedName}, " +
                        $"Location={location}, Error={handle.Error}");
                }

                byte[] bytes = handle.Asset.bytes;
                cancellationToken.ThrowIfCancellationRequested();
                ValidateBytes(normalizedName, bytes);
                cancellationToken.ThrowIfCancellationRequested();
                _bytesCache.AddOwned(normalizedName, bytes);
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
                    nameof(AssetManagementDataTableBytesLoader));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureUsable();
                byte[] fallbackBytes = TryLoadEditorFile(location);
                if (fallbackBytes != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateBytes(normalizedName, fallbackBytes);
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
                    nameof(AssetManagementDataTableBytesLoader));
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
                        DisposeHandle(handle, loadCompletedSuccessfully, normalizedName);
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureOwnerThread();
            _disposed = true;
            Exception cleanupFailure = null;
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                cleanupFailure = exception;
            }

            try
            {
                if (_activeHandle != null)
                {
                    _activeHandleDisposedByOwner = true;
                    _activeHandle.Dispose();
                }
            }
            catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                cleanupFailure ??= exception;
            }

            _activeHandle = null;
            try
            {
                _bytesCache.Dispose();
            }
            catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                cleanupFailure ??= exception;
            }

            _lifetimeCancellation.Dispose();
            if (cleanupFailure != null)
            {
                throw new InvalidOperationException(
                    "One or more DataTable loader resources failed to shut down cleanly.",
                    cleanupFailure);
            }
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
            if (_manifest != null &&
                _manifest.TryGetEntry(normalizedName, out DataTableManifestEntry entry))
            {
                return entry.HasLocation
                    ? entry.Location
                    : _locationResolver.Resolve(entry.TableName);
            }

            return _locationResolver.Resolve(normalizedName);
        }

        private void ValidateBytes(string normalizedName, ReadOnlyMemory<byte> bytes)
        {
            _limits.ValidatePayloadLength(normalizedName, bytes.Length);
            _limits.ValidateTotalBytes(checked(_bytesCache.TotalBytes + bytes.Length));
            _manifest?.ValidateBytes(normalizedName, bytes);
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
                string normalizedName = DataTableNameUtility.NormalizeTableName(tableNames[i]);
                _limits.ValidateTableName(normalizedName);
                snapshot[i] = normalizedName;
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

        private static void DisposeHandle(
            IAssetHandle<TextAsset> handle,
            bool loadCompletedSuccessfully,
            string tableName)
        {
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Dispose();
            }
            catch (Exception exception) when (
                !loadCompletedSuccessfully &&
                DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                DataTableAssetLoaderUtility.LogSuppressedCleanupFailure(
                    nameof(AssetManagementDataTableBytesLoader),
                    tableName,
                    exception);
            }
            catch (Exception exception) when (DataTableAssetLoaderUtility.IsRecoverableException(exception))
            {
                throw new InvalidOperationException(
                    $"Data table loaded, but its TextAsset handle could not be released. Table={tableName}",
                    exception);
            }
        }

        private void BeginLoad()
        {
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
                throw new ObjectDisposedException(nameof(AssetManagementDataTableBytesLoader));
            }
        }

        private void EnsureOwnerThread()
        {
            DataTableAssetLoaderUtility.EnsureOwnerThread(
                _ownerThreadId,
                nameof(AssetManagementDataTableBytesLoader));
        }
    }
}
