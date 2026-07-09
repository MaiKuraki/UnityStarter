namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetPatchJournalStatus : byte
    {
        None = 0,
        InProgress = 1,
        PendingDownload = 2,
        Succeeded = 3,
        Failed = 4,
        Cancelled = 5
    }
}
