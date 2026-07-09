using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetRepairRunResult
    {
        public readonly string PackageName;
        public readonly string PackageVersion;
        public readonly AssetRepairRunStatus Status;
        public readonly int TotalFailureCount;
        public readonly int RepairableFailureCount;
        public readonly int UnrepairableFailureCount;
        public readonly int RepairLocationCount;
        public readonly int TotalDownloadCount;
        public readonly long TotalDownloadBytes;
        public readonly bool ContentTrustEnabled;
        public readonly int PostRepairTrustFailureCount;
        public readonly ulong ContentTrustManifestFingerprint;
        public readonly ContentTrustVerificationResult FirstPostRepairFailure;
        public readonly string Error;

        public AssetRepairRunResult(
            string packageName,
            string packageVersion,
            AssetRepairRunStatus status,
            int totalFailureCount,
            int repairableFailureCount,
            int unrepairableFailureCount,
            int repairLocationCount,
            int totalDownloadCount,
            long totalDownloadBytes,
            bool contentTrustEnabled,
            int postRepairTrustFailureCount,
            ulong contentTrustManifestFingerprint,
            ContentTrustVerificationResult firstPostRepairFailure,
            string error)
        {
            PackageName = packageName;
            PackageVersion = packageVersion;
            Status = status;
            TotalFailureCount = totalFailureCount;
            RepairableFailureCount = repairableFailureCount;
            UnrepairableFailureCount = unrepairableFailureCount;
            RepairLocationCount = repairLocationCount;
            TotalDownloadCount = totalDownloadCount;
            TotalDownloadBytes = totalDownloadBytes;
            ContentTrustEnabled = contentTrustEnabled;
            PostRepairTrustFailureCount = postRepairTrustFailureCount;
            ContentTrustManifestFingerprint = contentTrustManifestFingerprint;
            FirstPostRepairFailure = firstPostRepairFailure;
            Error = error;
        }

        public bool Succeeded => Status == AssetRepairRunStatus.Succeeded;
        public bool NoRepairNeeded => Status == AssetRepairRunStatus.NoRepairNeeded;
        public bool NoRepairableLocations => Status == AssetRepairRunStatus.NoRepairableLocations;
        public bool Cancelled => Status == AssetRepairRunStatus.Cancelled;
        public bool Failed => Status == AssetRepairRunStatus.Failed;
    }
}
