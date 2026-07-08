using System;
using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;
using R3;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public interface IAssetRepairService : IDisposable
    {
        string PackageName { get; }
        Observable<(AssetRepairEvent, object)> RepairEvents { get; }

        UniTask<AssetRepairRunResult> RepairAsync(
            ContentTrustManifest manifest,
            IReadOnlyList<ContentTrustVerificationResult> failures,
            AssetRepairOptions options = default,
            CancellationToken cancellationToken = default);

        UniTask<AssetRepairRunResult> RepairAsync(
            AssetRepairPlan plan,
            AssetRepairOptions options = default,
            CancellationToken cancellationToken = default);
    }
}
