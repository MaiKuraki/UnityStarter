namespace CycloneGames.AssetManagement.Runtime
{
    public enum PatchRunStatus : byte
    {
        PendingDownload = 0,
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3
    }
}
