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
        public bool HasAssignedContextAsset => contextAsset != null;
        public bool HasAssignedAssetReference => contextAssetRef.IsValid;
        public bool HasAssignedPathLocation => !string.IsNullOrEmpty(contextAssetLocation);
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
                    : default;
            }

            return _hasResolvedContext
                ? _resolvedContext
                : default;
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

            if (_resolveTcs != null)
            {
                return await _resolveTcs.Task.AttachExternalCancellation(cancellationToken);
            }

            if (!contextAssetRef.IsValid)
            {
                return default;
            }

            string location = EffectiveLocation;
            if (string.IsNullOrEmpty(location))
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
    }
}
