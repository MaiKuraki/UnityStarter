namespace CycloneGames.AssetManagement.Runtime
{
    public enum AssetPatchProviderReconciliationStatus : byte
    {
        NotRun = 0,
        NotSupported = 1,
        NoActionRequired = 2,
        ReadyToRestartPatch = 3,
        ReadyToResumeDownload = 4,
        RequiresOwnerAction = 5,
        Failed = 6
    }
}
