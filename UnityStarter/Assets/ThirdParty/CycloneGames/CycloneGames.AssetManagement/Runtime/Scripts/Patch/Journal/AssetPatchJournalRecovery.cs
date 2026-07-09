namespace CycloneGames.AssetManagement.Runtime
{
    public static class AssetPatchJournalRecovery
    {
        public static AssetPatchRecoveryRecommendation Analyze(in AssetPatchJournalRecord record)
        {
            switch (record.Status)
            {
                case AssetPatchJournalStatus.Succeeded:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.None,
                        hasActiveWork: false,
                        isTerminal: true,
                        requiresProviderReconciliation: false,
                        "Patch transaction completed successfully.");
                case AssetPatchJournalStatus.Cancelled:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.None,
                        hasActiveWork: false,
                        isTerminal: true,
                        requiresProviderReconciliation: false,
                        "Patch transaction was cancelled by the owner.");
                case AssetPatchJournalStatus.Failed:
                    return new AssetPatchRecoveryRecommendation(
                        string.IsNullOrEmpty(record.RollbackVersion) ? AssetPatchRecoveryAction.InspectTerminalFailure : AssetPatchRecoveryAction.RollbackManifest,
                        hasActiveWork: false,
                        isTerminal: true,
                        requiresProviderReconciliation: true,
                        "Patch transaction failed; inspect provider cache and active manifest before retrying.");
                case AssetPatchJournalStatus.PendingDownload:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.ResumeOrRestartDownload,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: true,
                        "Patch found downloadable content but did not start the downloader.");
                case AssetPatchJournalStatus.InProgress:
                    return AnalyzeInProgress(in record);
                default:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.None,
                        hasActiveWork: false,
                        isTerminal: false,
                        requiresProviderReconciliation: false,
                        "No usable patch journal state was found.");
            }
        }

        private static AssetPatchRecoveryRecommendation AnalyzeInProgress(in AssetPatchJournalRecord record)
        {
            switch (record.Stage)
            {
                case PatchWorkflowState.WaitingForDownload:
                case PatchWorkflowState.Download:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.ResumeOrRestartDownload,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: true,
                        "Patch downloader may have been interrupted.");
                case PatchWorkflowState.VerifyContentTrust:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.VerifyContentTrust,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: true,
                        "Downloaded content may exist but must be verified before activation.");
                case PatchWorkflowState.RepairContent:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.RepairContent,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: true,
                        "Content repair may have been interrupted.");
                case PatchWorkflowState.RollbackManifest:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.RollbackManifest,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: true,
                        "Manifest rollback may have been interrupted.");
                case PatchWorkflowState.ClearCache:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.ClearCacheAndRetry,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: true,
                        "Cache cleanup may have been interrupted.");
                case PatchWorkflowState.Initialize:
                case PatchWorkflowState.CheckVersion:
                case PatchWorkflowState.UpdateManifest:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.RestartPatch,
                        hasActiveWork: true,
                        isTerminal: false,
                        requiresProviderReconciliation: false,
                        "Patch stopped before the downloader owned provider-side content.");
                default:
                    return new AssetPatchRecoveryRecommendation(
                        AssetPatchRecoveryAction.RestartPatch,
                        hasActiveWork: record.HasActiveWork,
                        isTerminal: record.IsTerminal,
                        requiresProviderReconciliation: record.HasActiveWork,
                        "Patch journal contains an unfinished transaction.");
            }
        }
    }
}
