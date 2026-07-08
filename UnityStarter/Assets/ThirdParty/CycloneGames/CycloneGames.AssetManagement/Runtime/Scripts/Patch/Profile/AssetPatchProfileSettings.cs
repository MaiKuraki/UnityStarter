using System;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    [Serializable]
    public sealed class AssetPatchProfileSettings
    {
        [SerializeField] private bool AutoDownloadOnFoundNewVersion = true;
        [SerializeField] private bool AppendTimeTicks = true;
        [SerializeField] private int MaxConcurrentDownloads = 10;
        [SerializeField] private int FailedRetryCount = 3;
        [SerializeField] private int RequestTimeoutSeconds = 60;
        [SerializeField] private bool ContentTrustEnabled;
        [SerializeField] private AssetPatchRootDirectorySource ContentTrustRootSource = AssetPatchRootDirectorySource.PersistentDataPath;
        [SerializeField] private string ContentTrustRootPath = "CycloneGames/AssetManagement/Content";
        [SerializeField] private PatchTrustFailurePolicy TrustFailurePolicy = PatchTrustFailurePolicy.FailFast;
        [SerializeField] private string RollbackVersionOverride = string.Empty;
        [SerializeField] private bool ClearUnusedCacheAfterRollback;

        public bool AutoDownload => AutoDownloadOnFoundNewVersion;
        public bool AppendTicks => AppendTimeTicks;
        public bool TrustEnabled => ContentTrustEnabled;

        public AssetPatchDownloadPolicy CreateDownloadPolicy()
        {
            return new AssetPatchDownloadPolicy(
                MaxConcurrentDownloads,
                FailedRetryCount,
                RequestTimeoutSeconds);
        }

        public AssetPatchTrustPolicy CreateTrustPolicy()
        {
            if (!ContentTrustEnabled)
            {
                return AssetPatchTrustPolicy.Disabled;
            }

            string rootDirectory = AssetPatchProfilePathResolver.ResolveRootDirectory(
                ContentTrustRootSource,
                ContentTrustRootPath);

            return new AssetPatchTrustPolicy(
                enabled: true,
                rootDirectory,
                TrustFailurePolicy,
                RollbackVersionOverride,
                ClearUnusedCacheAfterRollback);
        }
    }
}
