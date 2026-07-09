namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct PatchRunResult
    {
        public readonly string PackageName;
        public readonly string PackageVersion;
        public readonly string RollbackVersion;
        public readonly PatchRunStatus Status;
        public readonly PatchFailureKind FailureKind;
        public readonly int TotalDownloadCount;
        public readonly long TotalDownloadBytes;
        public readonly bool ContentTrustEnabled;
        public readonly int TrustFailureCount;
        public readonly ulong ContentTrustManifestFingerprint;
        public readonly string Error;

        public PatchRunResult(
            string packageName,
            string packageVersion,
            string rollbackVersion,
            PatchRunStatus status,
            int totalDownloadCount,
            long totalDownloadBytes,
            bool contentTrustEnabled,
            int trustFailureCount,
            ulong contentTrustManifestFingerprint,
            string error)
            : this(
                packageName,
                packageVersion,
                rollbackVersion,
                status,
                PatchFailureKind.None,
                totalDownloadCount,
                totalDownloadBytes,
                contentTrustEnabled,
                trustFailureCount,
                contentTrustManifestFingerprint,
                error)
        {
        }

        public PatchRunResult(
            string packageName,
            string packageVersion,
            string rollbackVersion,
            PatchRunStatus status,
            PatchFailureKind failureKind,
            int totalDownloadCount,
            long totalDownloadBytes,
            bool contentTrustEnabled,
            int trustFailureCount,
            ulong contentTrustManifestFingerprint,
            string error)
        {
            PackageName = packageName;
            PackageVersion = packageVersion;
            RollbackVersion = rollbackVersion;
            Status = status;
            FailureKind = failureKind;
            TotalDownloadCount = totalDownloadCount;
            TotalDownloadBytes = totalDownloadBytes;
            ContentTrustEnabled = contentTrustEnabled;
            TrustFailureCount = trustFailureCount;
            ContentTrustManifestFingerprint = contentTrustManifestFingerprint;
            Error = error;
        }

        public bool Succeeded => Status == PatchRunStatus.Succeeded;
        public bool PendingDownload => Status == PatchRunStatus.PendingDownload;
        public bool Cancelled => Status == PatchRunStatus.Cancelled;
        public bool Failed => Status == PatchRunStatus.Failed;
        public bool ProviderDownloadFailed => FailureKind == PatchFailureKind.ProviderDownloadFailed;
        public bool ExplicitlyCancelled => FailureKind == PatchFailureKind.Cancelled;
        public bool PackageVersionRequestFailed => FailureKind == PatchFailureKind.PackageVersionRequestFailed;
        public bool ManifestUpdateFailed => FailureKind == PatchFailureKind.ManifestUpdateFailed;
        public bool DownloaderCreationFailed => FailureKind == PatchFailureKind.DownloaderCreationFailed;
    }
}
