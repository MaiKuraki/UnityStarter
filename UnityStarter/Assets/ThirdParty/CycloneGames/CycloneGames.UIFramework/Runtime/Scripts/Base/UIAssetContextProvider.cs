using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Provides the default UI asset-load metadata for a UI framework host.
    /// Attach this to the same GameObject as UIRoot or any of its parents.
    /// OpenUI(...) can still override the context per call.
    /// </summary>
    [AddComponentMenu("CycloneGames/UIFramework/UI Asset Context Provider")]
    public sealed class UIAssetContextProvider : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIAssetContextProvider]";

        public enum ContextSourceMode
        {
            DirectReference = 0,
            PathLocation = 1,
            AssetReference = 2
        }

        [SerializeField] private ContextSourceMode sourceMode = ContextSourceMode.DirectReference;
        [SerializeField] private UIAssetContextAsset contextAsset;
        [SerializeField] private AssetRef<UIAssetContextAsset> contextAssetRef;
        [SerializeField] private string contextAssetLocation;
        [SerializeField] private bool useEmbeddedSnapshot = true;
        [SerializeField] private bool preloadPackageBackedContext = false;

        [Header("Embedded Metadata Snapshot")]
        [SerializeField] private string snapshotConfigBucket;
        [SerializeField] private string snapshotConfigTag;
        [SerializeField] private string snapshotConfigOwner;
        [SerializeField] private string snapshotPrefabBucket;
        [SerializeField] private string snapshotPrefabTag;
        [SerializeField] private string snapshotPrefabOwner;

        private IAssetHandle<UIAssetContextAsset> _resolvedHandle;
        private UIAssetLoadContext _resolvedContext;
        private UniTaskCompletionSource<UIAssetLoadContext> _resolveTcs;
        private bool _hasResolvedContext;
        private bool _loggedMissingPackage;
        private bool _loggedLoadFailure;

        public ContextSourceMode SourceMode => sourceMode;
        public UIAssetContextAsset ContextAsset => UsesDirectReference ? contextAsset : _resolvedHandle?.Asset;
        public AssetRef<UIAssetContextAsset> ContextAssetRef => contextAssetRef;
        public string ContextAssetLocation => UsesAssetReference ? contextAssetRef.Location : contextAssetLocation;
        public string EffectiveLocation
        {
            get
            {
                switch (sourceMode)
                {
                    case ContextSourceMode.AssetReference: return contextAssetRef.Location ?? string.Empty;
                    case ContextSourceMode.PathLocation: return contextAssetLocation ?? string.Empty;
                    default: return string.Empty;
                }
            }
        }
        public bool UsesDirectReference => sourceMode == ContextSourceMode.DirectReference;
        public bool UsesPathLocation => sourceMode == ContextSourceMode.PathLocation;
        public bool UsesAssetReference => sourceMode == ContextSourceMode.AssetReference;
        public bool UseEmbeddedSnapshot => useEmbeddedSnapshot;
        public bool PreloadPackageBackedContext => preloadPackageBackedContext;
        public bool HasAssignedContextAsset => contextAsset != null;
        public bool HasAssignedAssetReference => contextAssetRef.IsValid;
        public bool HasAssignedPathLocation => !string.IsNullOrEmpty(contextAssetLocation);
        public bool HasEmbeddedSnapshot => GetEmbeddedSnapshotContext().HasAnyMetadata;
        public bool HasConfiguredSource
        {
            get
            {
                switch (sourceMode)
                {
                    case ContextSourceMode.DirectReference: return contextAsset != null;
                    case ContextSourceMode.AssetReference: return contextAssetRef.IsValid;
                    case ContextSourceMode.PathLocation: return !string.IsNullOrEmpty(contextAssetLocation);
                    default: return false;
                }
            }
        }
        public bool HasResolvedAssetReference => _resolvedHandle != null && _resolvedHandle.Asset != null;
        public bool HasEffectiveMetadata => GetLoadContext().HasAnyMetadata;
        public string ConfigBucket => GetLoadContext().ConfigBucket;
        public string ConfigTag => GetLoadContext().ConfigTag;
        public string ConfigOwner => GetLoadContext().ConfigOwner;
        public string PrefabBucket => GetLoadContext().PrefabBucket;
        public string PrefabTag => GetLoadContext().PrefabTag;
        public string PrefabOwner => GetLoadContext().PrefabOwner;

        public UIAssetLoadContext GetLoadContext()
        {
            if (UsesDirectReference)
            {
                return contextAsset != null
                    ? contextAsset.ToLoadContext()
                    : GetEmbeddedSnapshotContext();
            }

            if (_hasResolvedContext)
            {
                return _resolvedContext;
            }

            if (useEmbeddedSnapshot)
            {
                UIAssetLoadContext snapshot = GetEmbeddedSnapshotContext();
                if (snapshot.HasAnyMetadata)
                {
                    return snapshot;
                }
            }

            return default;
        }

        public async UniTask<UIAssetLoadContext> ResolveLoadContextAsync(IAssetPackage package, CancellationToken cancellationToken = default)
        {
            if (UsesDirectReference)
            {
                return GetLoadContext();
            }

            if (_hasResolvedContext)
            {
                return _resolvedContext;
            }

            UIAssetLoadContext snapshotContext = useEmbeddedSnapshot
                ? GetEmbeddedSnapshotContext()
                : default;

            if (snapshotContext.HasAnyMetadata)
            {
                BeginWarmup(package);
                return snapshotContext;
            }

            return await EnsureResolvedContextAsync(package, cancellationToken);
        }

        public void BeginWarmup(IAssetPackage package)
        {
            if (UsesDirectReference || !preloadPackageBackedContext || _hasResolvedContext || _resolveTcs != null)
            {
                return;
            }

            if (!HasConfiguredSource || package == null)
            {
                return;
            }

            EnsureResolvedContextAsync(package, default).Forget();
        }

        public void SyncEmbeddedSnapshotFromAsset()
        {
            UIAssetLoadContext context = contextAsset != null
                ? contextAsset.ToLoadContext()
                : _resolvedHandle?.Asset != null
                    ? _resolvedHandle.Asset.ToLoadContext()
                    : default;
            ApplyEmbeddedSnapshot(context);
        }

        public void ClearEmbeddedSnapshot()
        {
            ApplyEmbeddedSnapshot(default);
        }

        private async UniTask<UIAssetLoadContext> EnsureResolvedContextAsync(IAssetPackage package, CancellationToken cancellationToken)
        {
            if (_resolveTcs != null)
            {
                return await _resolveTcs.Task.AttachExternalCancellation(cancellationToken);
            }

            string location = EffectiveLocation;
            if (!HasConfiguredSource || string.IsNullOrEmpty(location))
            {
                return default;
            }

            if (package == null)
            {
                if (!_loggedMissingPackage)
                {
                    _loggedMissingPackage = true;
                    CLogger.LogWarning($"{DEBUG_FLAG} UIAssetContextProvider is configured to use a package-backed source mode, but no IAssetPackage is available. Falling back to empty UI asset-load context.");
                }
                return default;
            }

            IAssetHandle<UIAssetContextAsset> handle = null;
            _resolveTcs = new UniTaskCompletionSource<UIAssetLoadContext>();
            try
            {
                handle = UsesAssetReference
                    ? package.LoadAsync(contextAssetRef, cancellationToken: cancellationToken)
                    : package.LoadAssetAsync<UIAssetContextAsset>(location, cancellationToken: cancellationToken);
                await WaitForHandleAsync(handle, cancellationToken);

                if (!string.IsNullOrEmpty(handle.Error) || handle.Asset == null)
                {
                    if (!_loggedLoadFailure)
                    {
                        _loggedLoadFailure = true;
                        CLogger.LogWarning($"{DEBUG_FLAG} Failed to resolve UIAssetContextAsset from '{location}'. Falling back to empty UI asset-load context.");
                    }

                    handle?.Dispose();
                    _resolveTcs.TrySetResult(default);
                    return default;
                }

                _resolvedHandle = handle;
                _resolvedContext = handle.Asset.ToLoadContext();
                _hasResolvedContext = true;
                if (!useEmbeddedSnapshot || !GetEmbeddedSnapshotContext().HasAnyMetadata)
                {
                    ApplyEmbeddedSnapshot(_resolvedContext);
                }
                _resolveTcs.TrySetResult(_resolvedContext);
                return _resolvedContext;
            }
            catch (System.OperationCanceledException)
            {
                handle?.Dispose();
                _resolveTcs.TrySetCanceled();
                throw;
            }
            catch (System.Exception ex)
            {
                if (!_loggedLoadFailure)
                {
                    _loggedLoadFailure = true;
                    CLogger.LogWarning($"{DEBUG_FLAG} Exception while resolving UIAssetContextAsset from '{location}': {ex.Message}. Falling back to empty UI asset-load context.");
                }

                handle?.Dispose();
                _resolveTcs.TrySetResult(default);
                return default;
            }
            finally
            {
                _resolveTcs = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseResolvedHandle();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Clear direct object references in non-direct modes to avoid phantom asset retention
            // when this provider lives on a persistent framework prefab.
            if (sourceMode != ContextSourceMode.DirectReference && contextAsset != null)
            {
                CLogger.LogWarning(
                    $"{DEBUG_FLAG} '{name}': Source is not DirectReference; clearing ContextAsset to prevent phantom memory retention.");
                contextAsset = null;
            }

            if (sourceMode == ContextSourceMode.DirectReference && contextAsset != null)
            {
                ApplyEmbeddedSnapshot(contextAsset.ToLoadContext());
            }
            else if (!useEmbeddedSnapshot)
            {
                ClearEmbeddedSnapshot();
            }

            ReleaseResolvedHandle();
            _resolvedContext = default;
            _hasResolvedContext = false;
            _loggedMissingPackage = false;
            _loggedLoadFailure = false;
        }
#endif

        private void ReleaseResolvedHandle()
        {
            _resolvedHandle?.Dispose();
            _resolvedHandle = null;
            _resolvedContext = default;
            _hasResolvedContext = false;
            _resolveTcs = null;
        }

        private static async UniTask WaitForHandleAsync(IOperation operation, CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                return;
            }

            while (!operation.IsDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private UIAssetLoadContext GetEmbeddedSnapshotContext()
        {
            return new UIAssetLoadContext(
                snapshotConfigBucket,
                snapshotConfigTag,
                snapshotConfigOwner,
                snapshotPrefabBucket,
                snapshotPrefabTag,
                snapshotPrefabOwner);
        }

        private void ApplyEmbeddedSnapshot(in UIAssetLoadContext context)
        {
            snapshotConfigBucket = context.ConfigBucket;
            snapshotConfigTag = context.ConfigTag;
            snapshotConfigOwner = context.ConfigOwner;
            snapshotPrefabBucket = context.PrefabBucket;
            snapshotPrefabTag = context.PrefabTag;
            snapshotPrefabOwner = context.PrefabOwner;
        }
    }
}
