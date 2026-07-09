namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchRecoveryPolicy
    {
        public readonly bool ClearSucceededJournal;
        public readonly bool ClearCancelledJournal;
        public readonly bool RollbackFailedJournalWithVersion;
        public readonly bool ClearUnusedCacheAfterRollback;
        public readonly bool ClearCacheForInterruptedDownload;
        public readonly ClearCacheMode InterruptedDownloadClearMode;
        public readonly bool ClearJournalAfterSuccessfulRecovery;
        public readonly int ManifestUpdateTimeoutSeconds;
        public readonly object InterruptedDownloadClearParam;

        public AssetPatchRecoveryPolicy(
            bool clearSucceededJournal = false,
            bool clearCancelledJournal = false,
            bool rollbackFailedJournalWithVersion = false,
            bool clearUnusedCacheAfterRollback = false,
            bool clearCacheForInterruptedDownload = false,
            ClearCacheMode interruptedDownloadClearMode = ClearCacheMode.Unused,
            bool clearJournalAfterSuccessfulRecovery = false,
            int manifestUpdateTimeoutSeconds = 60,
            object interruptedDownloadClearParam = null)
        {
            ClearSucceededJournal = clearSucceededJournal;
            ClearCancelledJournal = clearCancelledJournal;
            RollbackFailedJournalWithVersion = rollbackFailedJournalWithVersion;
            ClearUnusedCacheAfterRollback = clearUnusedCacheAfterRollback;
            ClearCacheForInterruptedDownload = clearCacheForInterruptedDownload;
            InterruptedDownloadClearMode = interruptedDownloadClearMode;
            ClearJournalAfterSuccessfulRecovery = clearJournalAfterSuccessfulRecovery;
            ManifestUpdateTimeoutSeconds = manifestUpdateTimeoutSeconds <= 0 ? 60 : manifestUpdateTimeoutSeconds;
            InterruptedDownloadClearParam = interruptedDownloadClearParam;
        }

        public static AssetPatchRecoveryPolicy InspectOnly => default;

        public static AssetPatchRecoveryPolicy ClearTerminalJournals => new AssetPatchRecoveryPolicy(
            clearSucceededJournal: true,
            clearCancelledJournal: true);
    }
}
