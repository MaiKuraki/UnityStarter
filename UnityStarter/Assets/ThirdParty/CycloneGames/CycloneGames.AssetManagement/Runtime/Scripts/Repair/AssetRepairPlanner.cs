using System;
using System.Collections.Generic;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AssetRepairPlanner
    {
        public static readonly AssetRepairPlanner Shared = new AssetRepairPlanner();

        public AssetRepairPlan CreatePlan(
            string packageName,
            ContentTrustManifest manifest,
            IReadOnlyList<ContentTrustVerificationResult> failures,
            List<string> locationWorkspace = null)
        {
            List<string> locations = locationWorkspace ?? new List<string>(failures?.Count ?? 0);
            locations.Clear();

            int totalFailureCount = 0;
            int repairableFailureCount = 0;
            int unrepairableFailureCount = 0;

            int count = failures?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                ContentTrustVerificationResult failure = failures[i];
                if (failure.Succeeded)
                {
                    continue;
                }

                totalFailureCount++;
                if (IsLocationRepairable(failure.Failure) && !string.IsNullOrEmpty(failure.Location))
                {
                    repairableFailureCount++;
                    AddUniqueLocation(locations, failure.Location);
                }
                else
                {
                    unrepairableFailureCount++;
                }
            }

            return new AssetRepairPlan(
                packageName,
                manifest.Version,
                manifest,
                locations.Count == 0 ? Array.Empty<string>() : locations.ToArray(),
                totalFailureCount,
                repairableFailureCount,
                unrepairableFailureCount);
        }

        public static bool IsLocationRepairable(ContentTrustFailure failure)
        {
            switch (failure)
            {
                case ContentTrustFailure.MissingFile:
                case ContentTrustFailure.SizeMismatch:
                case ContentTrustFailure.HashComputationFailed:
                case ContentTrustFailure.HashMismatch:
                case ContentTrustFailure.IoError:
                    return true;
                default:
                    return false;
            }
        }

        private static void AddUniqueLocation(List<string> locations, string location)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                if (string.Equals(locations[i], location, StringComparison.Ordinal))
                {
                    return;
                }
            }

            locations.Add(location);
        }
    }
}
