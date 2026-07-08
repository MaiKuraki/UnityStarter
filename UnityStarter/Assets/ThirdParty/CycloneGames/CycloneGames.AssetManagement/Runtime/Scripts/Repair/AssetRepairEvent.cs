namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetRepairEvent : byte
    {
        StageChanged = 0,
        PlanCreated = 1,
        DownloadProgress = 2,
        RepairCompleted = 3,
        RepairFailed = 4
    }
}
