namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetPatchRecoveryAction : byte
    {
        None = 0,
        RestartPatch = 1,
        ResumeOrRestartDownload = 2,
        VerifyContentTrust = 3,
        RepairContent = 4,
        RollbackManifest = 5,
        ClearCacheAndRetry = 6,
        InspectTerminalFailure = 7
    }
}
