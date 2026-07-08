using System.Collections.Generic;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct PatchContentTrustOptions
    {
        public readonly bool Enabled;
        public readonly string RootDirectory;
        public readonly ContentTrustManifest Manifest;
        public readonly IContentTrustVerifier Verifier;
        public readonly IContentTrustSignatureVerifier SignatureVerifier;
        public readonly PatchTrustFailurePolicy FailurePolicy;
        public readonly string RollbackVersionOverride;
        public readonly bool ClearUnusedCacheAfterRollback;
        public readonly List<ContentTrustVerificationResult> FailureBuffer;

        public PatchContentTrustOptions(
            string rootDirectory,
            ContentTrustManifest manifest,
            IContentTrustVerifier verifier = null,
            IContentTrustSignatureVerifier signatureVerifier = null,
            PatchTrustFailurePolicy failurePolicy = PatchTrustFailurePolicy.FailFast,
            string rollbackVersionOverride = null,
            bool clearUnusedCacheAfterRollback = false,
            List<ContentTrustVerificationResult> failureBuffer = null,
            bool enabled = true)
        {
            Enabled = enabled;
            RootDirectory = rootDirectory;
            Manifest = manifest;
            Verifier = verifier;
            SignatureVerifier = signatureVerifier;
            FailurePolicy = failurePolicy;
            RollbackVersionOverride = rollbackVersionOverride;
            ClearUnusedCacheAfterRollback = clearUnusedCacheAfterRollback;
            FailureBuffer = failureBuffer;
        }

        public static PatchContentTrustOptions Disabled => default;

        public PatchContentTrustOptions Normalized()
        {
            if (!Enabled)
            {
                return this;
            }

            return new PatchContentTrustOptions(
                RootDirectory,
                Manifest,
                Verifier ?? ContentTrustVerifier.Shared,
                SignatureVerifier,
                FailurePolicy,
                RollbackVersionOverride,
                ClearUnusedCacheAfterRollback,
                FailureBuffer,
                enabled: true);
        }
    }
}
