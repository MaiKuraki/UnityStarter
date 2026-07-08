namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetRepairRunStatus : byte
    {
        Succeeded = 0,
        NoRepairNeeded = 1,
        NoRepairableLocations = 2,
        Failed = 3,
        Cancelled = 4
    }
}
