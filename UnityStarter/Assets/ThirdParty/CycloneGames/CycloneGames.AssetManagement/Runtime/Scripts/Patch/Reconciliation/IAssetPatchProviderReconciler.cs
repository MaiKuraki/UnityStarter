using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public interface IAssetPatchProviderReconciler
    {
        AssetPatchProviderReconciliationCapabilities Capabilities { get; }

        UniTask<AssetPatchProviderReconciliationResult> ReconcileAsync(
            AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            CancellationToken cancellationToken = default);
    }
}
