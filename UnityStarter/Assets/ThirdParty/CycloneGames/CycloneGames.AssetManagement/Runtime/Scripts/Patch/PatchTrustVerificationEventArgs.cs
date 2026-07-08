using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct PatchTrustVerificationEventArgs
    {
        public readonly string PackageVersion;
        public readonly ulong ManifestFingerprint;
        public readonly int FailureCount;
        public readonly ContentTrustVerificationResult FirstFailure;

        public PatchTrustVerificationEventArgs(
            string packageVersion,
            ulong manifestFingerprint,
            int failureCount,
            ContentTrustVerificationResult firstFailure)
        {
            PackageVersion = packageVersion;
            ManifestFingerprint = manifestFingerprint;
            FailureCount = failureCount;
            FirstFailure = firstFailure;
        }
    }
}
