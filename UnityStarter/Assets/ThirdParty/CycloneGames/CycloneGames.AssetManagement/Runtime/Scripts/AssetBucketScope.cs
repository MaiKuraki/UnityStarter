using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Lightweight wrapper that applies the same bucket/tag/owner metadata to a family of asset loads.
    /// Use child scopes to model hierarchical lifetime domains such as UI.Scene.MainCity or UI.Persistent.HUD.
    /// </summary>
    public sealed class AssetBucketScope
    {
        private readonly IAssetPackage _package;
        private readonly string _bucket;
        private readonly string _tag;
        private readonly string _owner;

        public IAssetPackage Package => _package;
        public string Bucket => _bucket;
        public string Tag => _tag;
        public string Owner => _owner;

        public AssetBucketScope(IAssetPackage package, string bucket, string tag = null, string owner = null)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _bucket = bucket ?? string.Empty;
            _tag = tag;
            _owner = owner;
        }

        public AssetBucketScope CreateChild(string childSegment, string tag = null, string owner = null)
        {
            return new AssetBucketScope(
                _package,
                AssetBucketPath.Combine(_bucket, childSegment),
                tag ?? _tag,
                owner ?? _owner);
        }

        public void Clear()
        {
            _package.ClearBucket(_bucket);
        }

        public void ClearHierarchy()
        {
            _package.ClearBucketsByPrefix(_bucket);
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string tag = null, string owner = null)
            where TAsset : UnityEngine.Object
        {
            return RequireCapability<IAssetSyncOperations>().LoadAssetSync<TAsset>(
                location,
                _bucket,
                tag ?? _tag,
                owner ?? _owner);
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string tag = null, string owner = null, CancellationToken cancellationToken = default)
            where TAsset : UnityEngine.Object
        {
            return _package.LoadAssetAsync<TAsset>(location, _bucket, tag ?? _tag, owner ?? _owner, cancellationToken);
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string tag = null, string owner = null, CancellationToken cancellationToken = default)
            where TAsset : UnityEngine.Object
        {
            return RequireCapability<IAssetBulkLoader>().LoadAllAssetsAsync<TAsset>(
                location,
                _bucket,
                tag ?? _tag,
                owner ?? _owner,
                cancellationToken);
        }

        public IRawFileHandle LoadRawFileSync(string location, string tag = null, string owner = null)
        {
            return RequireCapability<IAssetRawFileLoader>().LoadRawFileSync(
                location,
                _bucket,
                tag ?? _tag,
                owner ?? _owner);
        }

        public IRawFileHandle LoadRawFileAsync(string location, string tag = null, string owner = null, CancellationToken cancellationToken = default)
        {
            return RequireCapability<IAssetRawFileLoader>().LoadRawFileAsync(
                location,
                _bucket,
                tag ?? _tag,
                owner ?? _owner,
                cancellationToken);
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneParameters loadParameters, SceneActivationMode activationMode, int priority = 100)
        {
            return RequireCapability<IAssetSceneLoader>().LoadSceneAsync(
                sceneLocation,
                loadParameters,
                activationMode,
                priority,
                _bucket);
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            return RequireCapability<IAssetSceneLoader>().LoadSceneAsync(
                sceneLocation,
                loadMode,
                activateOnLoad,
                priority,
                _bucket);
        }

        private TCapability RequireCapability<TCapability>() where TCapability : class
        {
            if (_package is TCapability capability)
            {
                return capability;
            }

            throw new NotSupportedException(
                $"Asset package '{_package.Name}' does not implement {typeof(TCapability).Name}.");
        }
    }

    public static class AssetBucketScopeExtensions
    {
        public static AssetBucketScope CreateBucketScope(this IAssetPackage package, string bucket, string tag = null, string owner = null)
        {
            return new AssetBucketScope(package, bucket, tag, owner);
        }
    }
}
