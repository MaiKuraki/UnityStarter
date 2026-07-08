using System;
using System.Threading;

using Cysharp.Threading.Tasks;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class PatchContentTrustProcessor
    {
        private readonly IAssetPackage _package;
        private readonly Action<PatchWorkflowState> _setState;
        private readonly Action<PatchEvent, object> _publishEvent;

        public PatchContentTrustProcessor(
            IAssetPackage package,
            Action<PatchWorkflowState> setState,
            Action<PatchEvent, object> publishEvent)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _setState = setState ?? throw new ArgumentNullException(nameof(setState));
            _publishEvent = publishEvent ?? throw new ArgumentNullException(nameof(publishEvent));
        }

        public PatchTrustVerificationEventArgs Verify(PatchContentTrustOptions trustOptions, string packageVersion)
        {
            ContentTrustManifest manifest = trustOptions.Manifest;
            ulong fingerprint = manifest.Entries == null ? 0UL : manifest.ComputeFingerprint();
            int failureCount = trustOptions.Verifier.VerifyManifestFiles(
                trustOptions.RootDirectory,
                in manifest,
                trustOptions.FailureBuffer,
                trustOptions.SignatureVerifier);

            ContentTrustVerificationResult firstFailure = default;
            if (failureCount > 0 && trustOptions.FailureBuffer != null && trustOptions.FailureBuffer.Count > 0)
            {
                firstFailure = trustOptions.FailureBuffer[0];
            }

            return new PatchTrustVerificationEventArgs(packageVersion, fingerprint, failureCount, firstFailure);
        }

        public async UniTask<PatchTrustVerificationEventArgs> HandleFailureAsync(
            PatchRunOptions options,
            string packageName,
            string packageVersion,
            PatchTrustVerificationEventArgs trustArgs,
            CancellationToken cancellationToken)
        {
            PatchTrustVerificationException exception = CreateTrustException(packageName, packageVersion, trustArgs);
            PatchContentTrustOptions trustOptions = options.TrustOptions;

            switch (trustOptions.FailurePolicy)
            {
                case PatchTrustFailurePolicy.ClearUnusedCacheThenFail:
                    _setState(PatchWorkflowState.ClearCache);
                    await _package.ClearCacheFilesAsync(ClearCacheMode.Unused, cancellationToken: cancellationToken);
                    throw exception;
                case PatchTrustFailurePolicy.ClearAllCacheThenFail:
                    _setState(PatchWorkflowState.ClearCache);
                    await _package.ClearCacheFilesAsync(ClearCacheMode.All, cancellationToken: cancellationToken);
                    throw exception;
                case PatchTrustFailurePolicy.RollbackManifestThenFail:
                    await RollbackManifestAfterTrustFailureAsync(options, packageName, packageVersion, trustArgs, cancellationToken);
                    throw exception;
                case PatchTrustFailurePolicy.RepairLocationsThenFail:
                    await RepairLocationsAfterTrustFailureAsync(options, packageName, packageVersion, trustArgs, reverify: false, cancellationToken);
                    throw exception;
                case PatchTrustFailurePolicy.RepairLocationsThenReverify:
                    return await RepairLocationsAfterTrustFailureAsync(options, packageName, packageVersion, trustArgs, reverify: true, cancellationToken);
                default:
                    throw exception;
            }
        }

        private async UniTask<PatchTrustVerificationEventArgs> RepairLocationsAfterTrustFailureAsync(
            PatchRunOptions options,
            string packageName,
            string packageVersion,
            PatchTrustVerificationEventArgs trustArgs,
            bool reverify,
            CancellationToken cancellationToken)
        {
            if (options.TrustOptions.FailureBuffer == null)
            {
                throw CreateTrustException(
                    packageName,
                    packageVersion,
                    trustArgs,
                    "Content trust verification failed and repair requires a reusable failure buffer.");
            }

            _setState(PatchWorkflowState.RepairContent);
            using (var repairService = new AssetRepairService(_package))
            {
                var repairOptions = new AssetRepairOptions(
                    options.DownloadOptions,
                    options.TrustOptions,
                    clearUnusedCacheBeforeDownload: true,
                    recursiveDownloadLocations: true,
                    verifyAfterRepair: reverify);

                AssetRepairRunResult repairResult = await repairService.RepairAsync(
                    options.TrustOptions.Manifest,
                    options.TrustOptions.FailureBuffer,
                    repairOptions,
                    cancellationToken);
                _publishEvent(PatchEvent.ContentRepairCompleted, repairResult);

                if (!repairResult.Succeeded)
                {
                    throw CreateTrustException(
                        packageName,
                        packageVersion,
                        trustArgs,
                        string.IsNullOrEmpty(repairResult.Error)
                            ? "Content trust verification failed and content repair did not complete."
                            : $"Content trust verification failed and content repair did not complete: {repairResult.Error}");
                }

                if (!reverify)
                {
                    return trustArgs;
                }

                return new PatchTrustVerificationEventArgs(
                    packageVersion,
                    repairResult.ContentTrustManifestFingerprint,
                    repairResult.PostRepairTrustFailureCount,
                    repairResult.FirstPostRepairFailure);
            }
        }

        private async UniTask RollbackManifestAfterTrustFailureAsync(
            PatchRunOptions options,
            string packageName,
            string packageVersion,
            PatchTrustVerificationEventArgs trustArgs,
            CancellationToken cancellationToken)
        {
            string rollbackVersion = string.IsNullOrEmpty(options.TrustOptions.RollbackVersionOverride)
                ? options.TrustOptions.Manifest.RollbackVersion
                : options.TrustOptions.RollbackVersionOverride;

            if (string.IsNullOrEmpty(rollbackVersion))
            {
                throw CreateTrustException(packageName, packageVersion, trustArgs, "Content trust verification failed and no rollback version is available.");
            }

            _setState(PatchWorkflowState.RollbackManifest);
            bool rollbackSucceeded = await _package.UpdatePackageManifestAsync(
                rollbackVersion,
                options.DownloadOptions.RequestTimeoutSeconds,
                cancellationToken);
            _publishEvent(PatchEvent.RollbackCompleted, new PatchRollbackEventArgs(packageVersion, rollbackVersion, rollbackSucceeded));

            if (!rollbackSucceeded)
            {
                throw CreateTrustException(packageName, packageVersion, trustArgs, $"Content trust verification failed and rollback manifest update failed: {rollbackVersion}");
            }

            if (options.TrustOptions.ClearUnusedCacheAfterRollback)
            {
                _setState(PatchWorkflowState.ClearCache);
                await _package.ClearCacheFilesAsync(ClearCacheMode.Unused, cancellationToken: cancellationToken);
            }
        }

        private static PatchTrustVerificationException CreateTrustException(
            string packageName,
            string packageVersion,
            PatchTrustVerificationEventArgs trustArgs,
            string message = null)
        {
            return new PatchTrustVerificationException(
                packageName,
                packageVersion,
                trustArgs.FailureCount,
                trustArgs.FirstFailure,
                message ?? $"Content trust verification failed for package '{packageName}' version '{packageVersion}'.");
        }
    }
}
