namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchProviderReconciliationCapabilities
    {
        public readonly string ProviderName;
        public readonly bool SupportsVersionedManifestUpdate;
        public readonly bool SupportsExplicitCacheCleanup;
        public readonly bool SupportsUnusedCacheCleanup;
        public readonly bool SupportsTagScopedCacheCleanup;
        public readonly bool SupportsProviderManagedDownloadCache;
        public readonly bool SupportsIsolatedVersionPreDownload;
        public readonly bool RequiresMainThreadAccess;

        public AssetPatchProviderReconciliationCapabilities(
            string providerName,
            bool supportsVersionedManifestUpdate,
            bool supportsExplicitCacheCleanup,
            bool supportsUnusedCacheCleanup,
            bool supportsTagScopedCacheCleanup,
            bool supportsProviderManagedDownloadCache,
            bool supportsIsolatedVersionPreDownload,
            bool requiresMainThreadAccess)
        {
            ProviderName = providerName;
            SupportsVersionedManifestUpdate = supportsVersionedManifestUpdate;
            SupportsExplicitCacheCleanup = supportsExplicitCacheCleanup;
            SupportsUnusedCacheCleanup = supportsUnusedCacheCleanup;
            SupportsTagScopedCacheCleanup = supportsTagScopedCacheCleanup;
            SupportsProviderManagedDownloadCache = supportsProviderManagedDownloadCache;
            SupportsIsolatedVersionPreDownload = supportsIsolatedVersionPreDownload;
            RequiresMainThreadAccess = requiresMainThreadAccess;
        }

        public bool HasProvider => !string.IsNullOrEmpty(ProviderName);
    }
}
