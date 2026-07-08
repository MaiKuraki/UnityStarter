using System;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetRepairPlan
    {
        public readonly string PackageName;
        public readonly string PackageVersion;
        public readonly ContentTrustManifest Manifest;
        public readonly string[] RepairLocations;
        public readonly int TotalFailureCount;
        public readonly int RepairableFailureCount;
        public readonly int UnrepairableFailureCount;

        public AssetRepairPlan(
            string packageName,
            string packageVersion,
            ContentTrustManifest manifest,
            string[] repairLocations,
            int totalFailureCount,
            int repairableFailureCount,
            int unrepairableFailureCount)
        {
            PackageName = packageName;
            PackageVersion = packageVersion;
            Manifest = manifest;
            RepairLocations = repairLocations ?? Array.Empty<string>();
            TotalFailureCount = totalFailureCount < 0 ? 0 : totalFailureCount;
            RepairableFailureCount = repairableFailureCount < 0 ? 0 : repairableFailureCount;
            UnrepairableFailureCount = unrepairableFailureCount < 0 ? 0 : unrepairableFailureCount;
        }

        public int RepairLocationCount => RepairLocations?.Length ?? 0;
        public bool HasFailures => TotalFailureCount > 0;
        public bool HasRepairableLocations => RepairLocationCount > 0;
        public bool HasUnrepairableFailures => UnrepairableFailureCount > 0;
    }
}
