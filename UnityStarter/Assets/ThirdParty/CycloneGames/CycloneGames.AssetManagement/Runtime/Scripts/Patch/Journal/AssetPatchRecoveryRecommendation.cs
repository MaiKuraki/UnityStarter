namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchRecoveryRecommendation
    {
        public readonly AssetPatchRecoveryAction Action;
        public readonly bool HasActiveWork;
        public readonly bool IsTerminal;
        public readonly bool RequiresProviderReconciliation;
        public readonly string Reason;

        public AssetPatchRecoveryRecommendation(
            AssetPatchRecoveryAction action,
            bool hasActiveWork,
            bool isTerminal,
            bool requiresProviderReconciliation,
            string reason)
        {
            Action = action;
            HasActiveWork = hasActiveWork;
            IsTerminal = isTerminal;
            RequiresProviderReconciliation = requiresProviderReconciliation;
            Reason = reason;
        }
    }
}
