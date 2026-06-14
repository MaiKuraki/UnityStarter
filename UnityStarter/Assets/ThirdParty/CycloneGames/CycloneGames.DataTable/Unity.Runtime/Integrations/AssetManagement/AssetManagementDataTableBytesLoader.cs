using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.DataTable.Unity.Integrations.AssetManagement
{
    public sealed class AssetManagementDataTableBytesLoader : IDataTableBytesProvider, IDisposable
    {
        private readonly AssetBucketScope _assetScope;
        private readonly IDataTableLocationResolver _locationResolver;
        private readonly DataTableBytesCache _bytesCache;
        private readonly List<IAssetHandle<TextAsset>> _handles;
        private readonly DataTableManifest _manifest;
        private readonly bool _enableEditorFileFallback;
        private bool _disposed;

        public AssetManagementDataTableBytesLoader(
            IAssetPackage assetPackage,
            string bucketName,
            IDataTableLocationResolver locationResolver,
            string owner = null,
            bool enableEditorFileFallback = true,
            int initialCapacity = 8,
            DataTableManifest manifest = null)
            : this(
                assetPackage,
                new DataTableAssetLoadContext(bucketName, owner: owner),
                locationResolver,
                enableEditorFileFallback,
                initialCapacity,
                manifest)
        {
        }

        public AssetManagementDataTableBytesLoader(
            IAssetPackage assetPackage,
            DataTableAssetLoadContext loadContext,
            IDataTableLocationResolver locationResolver,
            bool enableEditorFileFallback = true,
            int initialCapacity = 8,
            DataTableManifest manifest = null)
        {
            if (assetPackage == null)
            {
                throw new ArgumentNullException(nameof(assetPackage));
            }

            if (string.IsNullOrWhiteSpace(loadContext.Bucket))
            {
                throw new ArgumentException("Bucket name is null or empty.", nameof(loadContext));
            }

            _assetScope = assetPackage.CreateBucketScope(
                loadContext.Bucket,
                loadContext.Tag,
                loadContext.Owner);
            _locationResolver = locationResolver ?? throw new ArgumentNullException(nameof(locationResolver));
            _bytesCache = new DataTableBytesCache(initialCapacity);
            _handles = new List<IAssetHandle<TextAsset>>(Math.Max(0, initialCapacity));
            _manifest = manifest;
            _enableEditorFileFallback = enableEditorFileFallback;
        }

        public async UniTask LoadAsync(
            IReadOnlyList<string> tableNames,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (tableNames == null)
            {
                throw new ArgumentNullException(nameof(tableNames));
            }

            for (int i = 0; i < tableNames.Count; i++)
            {
                await LoadAsync(tableNames[i], cancellationToken);
            }

            _manifest?.ValidateRequiredTables(this);
        }

        public async UniTask LoadAsync(
            string tableName,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            string normalizedName = DataTableNameUtility.NormalizeTableName(tableName);
            if (_bytesCache.TryGetBytes(normalizedName, out _))
            {
                return;
            }

            string location = ResolveLocation(normalizedName);
            IAssetHandle<TextAsset> handle = null;
            try
            {
                handle = _assetScope.LoadAssetAsync<TextAsset>(
                    location,
                    cancellationToken: cancellationToken);
                if (handle == null)
                {
                    throw new InvalidOperationException("TextAsset handle is null.");
                }

                _handles.Add(handle);
                await handle.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                byte[] fallbackBytes = TryLoadEditorFile(location);
                if (fallbackBytes != null)
                {
                    ValidateBytes(normalizedName, fallbackBytes);
                    _bytesCache.Add(normalizedName, fallbackBytes);
                    return;
                }

                throw;
            }

            if (!string.IsNullOrEmpty(handle.Error) || handle.Asset == null)
            {
                byte[] fallbackBytes = TryLoadEditorFile(location);
                if (fallbackBytes != null)
                {
                    ValidateBytes(normalizedName, fallbackBytes);
                    _bytesCache.Add(normalizedName, fallbackBytes);
                    return;
                }

                throw new InvalidOperationException(
                    $"Failed to load data table asset. Table={normalizedName}, Location={location}, Error={handle.Error}");
            }

            byte[] bytes = handle.Asset.bytes;
            if (bytes == null || bytes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Loaded data table asset is empty. Table={normalizedName}, Location={location}");
            }

            ValidateBytes(normalizedName, bytes);
            _bytesCache.Add(normalizedName, bytes);
        }

        public byte[] GetBytes(string tableName)
        {
            ThrowIfDisposed();
            return _bytesCache.GetBytes(tableName);
        }

        public bool TryGetBytes(string tableName, out byte[] bytes)
        {
            ThrowIfDisposed();
            return _bytesCache.TryGetBytes(tableName, out bytes);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int i = 0; i < _handles.Count; i++)
            {
                _handles[i]?.Dispose();
            }

            _handles.Clear();
            _bytesCache.Clear();
            _assetScope.ClearHierarchy();
        }

        private byte[] TryLoadEditorFile(string assetPath)
        {
            if (!_enableEditorFileFallback)
            {
                return null;
            }

#if UNITY_EDITOR
            string fullPath = ResolveProjectRelativeAssetPath(assetPath);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            return bytes.Length == 0 ? null : bytes;
#else
            return null;
#endif
        }

        private string ResolveLocation(string normalizedName)
        {
            if (_manifest != null &&
                _manifest.TryGetEntry(normalizedName, out DataTableManifestEntry entry) &&
                entry.HasLocation)
            {
                return entry.Location;
            }

            return _locationResolver.Resolve(normalizedName);
        }

        private void ValidateBytes(string normalizedName, byte[] bytes)
        {
            _manifest?.ValidateBytes(normalizedName, bytes);
        }

#if UNITY_EDITOR
        private static string ResolveProjectRelativeAssetPath(string assetPath)
        {
            string normalizedPath = DataTableNameUtility.NormalizePath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath) ||
                !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                return string.Empty;
            }

            return Path.GetFullPath(Path.Combine(projectRoot.FullName, normalizedPath));
        }
#endif

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AssetManagementDataTableBytesLoader));
            }
        }
    }
}
