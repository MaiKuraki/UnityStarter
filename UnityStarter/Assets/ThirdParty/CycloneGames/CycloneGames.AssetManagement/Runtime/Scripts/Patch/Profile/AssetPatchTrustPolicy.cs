using System.Collections.Generic;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchTrustPolicy
    {
        public readonly bool Enabled;
        public readonly string RootDirectory;
        public readonly PatchTrustFailurePolicy FailurePolicy;
        public readonly string RollbackVersionOverride;
        public readonly bool ClearUnusedCacheAfterRollback;

        public AssetPatchTrustPolicy(
            bool enabled,
            string rootDirectory,
            PatchTrustFailurePolicy failurePolicy,
            string rollbackVersionOverride,
            bool clearUnusedCacheAfterRollback)
        {
            Enabled = enabled;
            RootDirectory = rootDirectory;
            FailurePolicy = failurePolicy;
            RollbackVersionOverride = rollbackVersionOverride;
            ClearUnusedCacheAfterRollback = clearUnusedCacheAfterRollback;
        }

        public static AssetPatchTrustPolicy Disabled => default;

        public PatchContentTrustOptions CreateContentTrustOptions(
            ContentTrustManifest manifest,
            IContentTrustVerifier verifier = null,
            IContentTrustSignatureVerifier signatureVerifier = null,
            List<ContentTrustVerificationResult> failureBuffer = null)
        {
            if (!Enabled)
            {
                return PatchContentTrustOptions.Disabled;
            }

            return new PatchContentTrustOptions(
                RootDirectory,
                manifest,
                verifier,
                signatureVerifier,
                FailurePolicy,
                RollbackVersionOverride,
                ClearUnusedCacheAfterRollback,
                failureBuffer,
                enabled: true).Normalized();
        }
    }
}
