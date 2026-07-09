namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchProviderReconciliationResult
    {
        public readonly AssetPatchProviderReconciliationStatus Status;
        public readonly AssetPatchProviderReconciliationCapabilities Capabilities;
        public readonly string Message;
        public readonly string Error;

        public AssetPatchProviderReconciliationResult(
            AssetPatchProviderReconciliationStatus status,
            AssetPatchProviderReconciliationCapabilities capabilities,
            string message,
            string error)
        {
            Status = status;
            Capabilities = capabilities;
            Message = message;
            Error = error;
        }

        public bool HasResult => Status != AssetPatchProviderReconciliationStatus.NotRun;

        public bool Succeeded => Status != AssetPatchProviderReconciliationStatus.Failed;

        public bool NeedsOwnerAction =>
            Status == AssetPatchProviderReconciliationStatus.ReadyToRestartPatch ||
            Status == AssetPatchProviderReconciliationStatus.ReadyToResumeDownload ||
            Status == AssetPatchProviderReconciliationStatus.RequiresOwnerAction ||
            Status == AssetPatchProviderReconciliationStatus.NotSupported;

        public static AssetPatchProviderReconciliationResult NoActionRequired(
            AssetPatchProviderReconciliationCapabilities capabilities,
            string message)
        {
            return new AssetPatchProviderReconciliationResult(
                AssetPatchProviderReconciliationStatus.NoActionRequired,
                capabilities,
                message,
                null);
        }

        public static AssetPatchProviderReconciliationResult ReadyToRestartPatch(
            AssetPatchProviderReconciliationCapabilities capabilities,
            string message)
        {
            return new AssetPatchProviderReconciliationResult(
                AssetPatchProviderReconciliationStatus.ReadyToRestartPatch,
                capabilities,
                message,
                null);
        }

        public static AssetPatchProviderReconciliationResult ReadyToResumeDownload(
            AssetPatchProviderReconciliationCapabilities capabilities,
            string message)
        {
            return new AssetPatchProviderReconciliationResult(
                AssetPatchProviderReconciliationStatus.ReadyToResumeDownload,
                capabilities,
                message,
                null);
        }

        public static AssetPatchProviderReconciliationResult RequiresOwnerAction(
            AssetPatchProviderReconciliationCapabilities capabilities,
            string message)
        {
            return new AssetPatchProviderReconciliationResult(
                AssetPatchProviderReconciliationStatus.RequiresOwnerAction,
                capabilities,
                message,
                null);
        }

        public static AssetPatchProviderReconciliationResult NotSupported(
            AssetPatchProviderReconciliationCapabilities capabilities,
            string message)
        {
            return new AssetPatchProviderReconciliationResult(
                AssetPatchProviderReconciliationStatus.NotSupported,
                capabilities,
                message,
                null);
        }

        public static AssetPatchProviderReconciliationResult Failed(
            AssetPatchProviderReconciliationCapabilities capabilities,
            string error)
        {
            return new AssetPatchProviderReconciliationResult(
                AssetPatchProviderReconciliationStatus.Failed,
                capabilities,
                null,
                error);
        }
    }
}
