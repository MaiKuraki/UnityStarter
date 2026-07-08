using System;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class PatchTrustVerificationException : Exception
    {
        public PatchTrustVerificationException(
            string packageName,
            string packageVersion,
            int failureCount,
            ContentTrustVerificationResult firstFailure,
            string message)
            : base(message)
        {
            PackageName = packageName;
            PackageVersion = packageVersion;
            FailureCount = failureCount;
            FirstFailure = firstFailure;
        }

        public string PackageName { get; }
        public string PackageVersion { get; }
        public int FailureCount { get; }
        public ContentTrustVerificationResult FirstFailure { get; }
    }
}
