namespace CycloneGames.AssetManagement.Runtime
{
    public enum PatchTrustFailurePolicy : byte
    {
        FailFast = 0,
        ClearUnusedCacheThenFail = 1,
        ClearAllCacheThenFail = 2,
        RollbackManifestThenFail = 3,
        RepairLocationsThenFail = 4,
        RepairLocationsThenReverify = 5
    }
}
