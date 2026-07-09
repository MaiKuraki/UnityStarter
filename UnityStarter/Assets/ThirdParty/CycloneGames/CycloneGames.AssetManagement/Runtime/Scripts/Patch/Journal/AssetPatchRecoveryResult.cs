namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchRecoveryResult
    {
        public readonly AssetPatchRecoveryStatus Status;
        public readonly AssetPatchRecoveryAction Action;
        public readonly AssetPatchJournalRecord Record;
        public readonly AssetPatchRecoveryRecommendation Recommendation;
        public readonly bool JournalRead;
        public readonly bool JournalCleared;
        public readonly bool ManifestRolledBack;
        public readonly bool CacheCleared;
        public readonly string Error;
        public readonly AssetPatchProviderReconciliationResult ProviderReconciliation;

        public AssetPatchRecoveryResult(
            AssetPatchRecoveryStatus status,
            AssetPatchRecoveryAction action,
            AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            bool journalRead,
            bool journalCleared,
            bool manifestRolledBack,
            bool cacheCleared,
            string error)
            : this(
                status,
                action,
                record,
                recommendation,
                journalRead,
                journalCleared,
                manifestRolledBack,
                cacheCleared,
                error,
                default)
        {
        }

        public AssetPatchRecoveryResult(
            AssetPatchRecoveryStatus status,
            AssetPatchRecoveryAction action,
            AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            bool journalRead,
            bool journalCleared,
            bool manifestRolledBack,
            bool cacheCleared,
            string error,
            AssetPatchProviderReconciliationResult providerReconciliation)
        {
            Status = status;
            Action = action;
            Record = record;
            Recommendation = recommendation;
            JournalRead = journalRead;
            JournalCleared = journalCleared;
            ManifestRolledBack = manifestRolledBack;
            CacheCleared = cacheCleared;
            Error = error;
            ProviderReconciliation = providerReconciliation;
        }

        public bool Succeeded =>
            Status == AssetPatchRecoveryStatus.NoJournal ||
            Status == AssetPatchRecoveryStatus.NoActionRequired ||
            Status == AssetPatchRecoveryStatus.RollbackCompleted ||
            Status == AssetPatchRecoveryStatus.CacheCleanupCompleted;
    }
}
