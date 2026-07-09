namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetPatchRecoveryStatus : byte
    {
        NoJournal = 0,
        JournalUnreadable = 1,
        NoActionRequired = 2,
        RequiresOwnerAction = 3,
        RollbackCompleted = 4,
        CacheCleanupCompleted = 5,
        Failed = 6,
        Cancelled = 7
    }
}
