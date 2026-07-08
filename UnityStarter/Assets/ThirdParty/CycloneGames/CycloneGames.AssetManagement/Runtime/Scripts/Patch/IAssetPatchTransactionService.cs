using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public interface IAssetPatchTransactionService
    {
        UniTask<PatchRunResult> RunAsync(PatchRunOptions options, CancellationToken cancellationToken = default);
        UniTask<PatchRunResult> DownloadAsync(CancellationToken cancellationToken = default);
    }
}
