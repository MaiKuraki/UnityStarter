namespace CycloneGames.AssetManagement.Runtime
{
    public enum PatchWorkflowState : byte
    {
        None = 0,
        Initialize = 1,
        CheckVersion = 2,
        UpdateManifest = 3,
        WaitingForDownload = 4,
        Download = 5,
        VerifyContentTrust = 6,
        RollbackManifest = 7,
        ClearCache = 8,
        Done = 9,
        Failed = 10,
        Cancelled = 11,
        RepairContent = 12
    }
}
