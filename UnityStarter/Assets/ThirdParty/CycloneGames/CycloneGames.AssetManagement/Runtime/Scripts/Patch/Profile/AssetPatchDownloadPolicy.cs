namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchDownloadPolicy
    {
        public readonly int MaxConcurrentDownloads;
        public readonly int FailedRetryCount;
        public readonly int RequestTimeoutSeconds;

        public AssetPatchDownloadPolicy(
            int maxConcurrentDownloads,
            int failedRetryCount,
            int requestTimeoutSeconds)
        {
            MaxConcurrentDownloads = maxConcurrentDownloads <= 0 ? PatchDownloadOptions.Default.MaxConcurrentDownloads : maxConcurrentDownloads;
            FailedRetryCount = failedRetryCount < 0 ? PatchDownloadOptions.Default.FailedRetryCount : failedRetryCount;
            RequestTimeoutSeconds = requestTimeoutSeconds <= 0 ? PatchDownloadOptions.Default.RequestTimeoutSeconds : requestTimeoutSeconds;
        }

        public static AssetPatchDownloadPolicy Default => new AssetPatchDownloadPolicy(
            PatchDownloadOptions.Default.MaxConcurrentDownloads,
            PatchDownloadOptions.Default.FailedRetryCount,
            PatchDownloadOptions.Default.RequestTimeoutSeconds);

        public PatchDownloadOptions ToPatchDownloadOptions()
        {
            return new PatchDownloadOptions
            {
                MaxConcurrentDownloads = MaxConcurrentDownloads,
                FailedRetryCount = FailedRetryCount,
                RequestTimeoutSeconds = RequestTimeoutSeconds
            }.Normalized();
        }
    }
}
