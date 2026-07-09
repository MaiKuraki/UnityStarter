namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchJournalRecord
    {
        public readonly int SchemaVersion;
        public readonly long Sequence;
        public readonly string PackageName;
        public readonly string PackageVersion;
        public readonly string RollbackVersion;
        public readonly PatchWorkflowState Stage;
        public readonly AssetPatchJournalStatus Status;
        public readonly int TotalDownloadCount;
        public readonly long TotalDownloadBytes;
        public readonly bool ContentTrustEnabled;
        public readonly int TrustFailureCount;
        public readonly ulong ContentTrustManifestFingerprint;
        public readonly long StartedUtcTicks;
        public readonly long UpdatedUtcTicks;
        public readonly string Error;

        public AssetPatchJournalRecord(
            long sequence,
            string packageName,
            string packageVersion,
            string rollbackVersion,
            PatchWorkflowState stage,
            AssetPatchJournalStatus status,
            int totalDownloadCount,
            long totalDownloadBytes,
            bool contentTrustEnabled,
            int trustFailureCount,
            ulong contentTrustManifestFingerprint,
            long startedUtcTicks,
            long updatedUtcTicks,
            string error)
        {
            SchemaVersion = AssetPatchJournalCodec.SCHEMA_VERSION;
            Sequence = sequence;
            PackageName = packageName;
            PackageVersion = packageVersion;
            RollbackVersion = rollbackVersion;
            Stage = stage;
            Status = status;
            TotalDownloadCount = totalDownloadCount;
            TotalDownloadBytes = totalDownloadBytes;
            ContentTrustEnabled = contentTrustEnabled;
            TrustFailureCount = trustFailureCount;
            ContentTrustManifestFingerprint = contentTrustManifestFingerprint;
            StartedUtcTicks = startedUtcTicks;
            UpdatedUtcTicks = updatedUtcTicks;
            Error = error;
        }

        public bool IsTerminal =>
            Status == AssetPatchJournalStatus.Succeeded ||
            Status == AssetPatchJournalStatus.Failed ||
            Status == AssetPatchJournalStatus.Cancelled;

        public bool HasActiveWork =>
            Status == AssetPatchJournalStatus.InProgress ||
            Status == AssetPatchJournalStatus.PendingDownload;
    }
}
