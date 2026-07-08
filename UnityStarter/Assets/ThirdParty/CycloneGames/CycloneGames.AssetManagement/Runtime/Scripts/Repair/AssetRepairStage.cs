namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetRepairStage : byte
    {
        None = 0,
        Plan = 1,
        ClearCache = 2,
        Download = 3,
        VerifyContentTrust = 4,
        Done = 5,
        Failed = 6,
        Cancelled = 7
    }
}
