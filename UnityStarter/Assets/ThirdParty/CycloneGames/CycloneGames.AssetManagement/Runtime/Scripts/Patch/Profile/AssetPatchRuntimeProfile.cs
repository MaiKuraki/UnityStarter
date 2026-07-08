using System.Collections.Generic;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetPatchRuntimeProfile
    {
        public readonly string PackageName;
        public readonly AssetPatchPlatform Platform;
        public readonly bool AutoDownloadOnFoundNewVersion;
        public readonly bool AppendTimeTicks;
        public readonly AssetPatchDownloadPolicy DownloadPolicy;
        public readonly AssetPatchTrustPolicy TrustPolicy;

        public AssetPatchRuntimeProfile(
            string packageName,
            AssetPatchPlatform platform,
            bool autoDownloadOnFoundNewVersion,
            bool appendTimeTicks,
            AssetPatchDownloadPolicy downloadPolicy,
            AssetPatchTrustPolicy trustPolicy)
        {
            PackageName = packageName;
            Platform = platform;
            AutoDownloadOnFoundNewVersion = autoDownloadOnFoundNewVersion;
            AppendTimeTicks = appendTimeTicks;
            DownloadPolicy = downloadPolicy;
            TrustPolicy = trustPolicy;
        }

        public PatchRunOptions CreateRunOptions(
            ContentTrustManifest manifest = default,
            IContentTrustVerifier verifier = null,
            IContentTrustSignatureVerifier signatureVerifier = null,
            List<ContentTrustVerificationResult> failureBuffer = null)
        {
            PatchContentTrustOptions trustOptions = TrustPolicy.CreateContentTrustOptions(
                manifest,
                verifier,
                signatureVerifier,
                failureBuffer);

            return new PatchRunOptions(
                AutoDownloadOnFoundNewVersion,
                DownloadPolicy.ToPatchDownloadOptions(),
                trustOptions,
                AppendTimeTicks).Normalized();
        }
    }
}
