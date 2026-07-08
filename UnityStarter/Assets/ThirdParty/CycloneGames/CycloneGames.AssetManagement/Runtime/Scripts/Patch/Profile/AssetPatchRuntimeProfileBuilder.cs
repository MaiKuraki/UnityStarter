using System;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AssetPatchRuntimeProfileBuilder
    {
        private string _packageName = "DefaultPackage";
        private AssetPatchPlatform _platform = AssetPatchPlatform.Any;
        private bool _autoDownloadOnFoundNewVersion = true;
        private bool _appendTimeTicks = true;
        private AssetPatchDownloadPolicy _downloadPolicy = AssetPatchDownloadPolicy.Default;
        private AssetPatchTrustPolicy _trustPolicy = AssetPatchTrustPolicy.Disabled;

        public AssetPatchRuntimeProfileBuilder WithPackageName(string packageName)
        {
            _packageName = packageName;
            return this;
        }

        public AssetPatchRuntimeProfileBuilder WithPlatform(AssetPatchPlatform platform)
        {
            _platform = platform;
            return this;
        }

        public AssetPatchRuntimeProfileBuilder WithAutoDownloadOnFoundNewVersion(bool enabled)
        {
            _autoDownloadOnFoundNewVersion = enabled;
            return this;
        }

        public AssetPatchRuntimeProfileBuilder WithAppendTimeTicks(bool enabled)
        {
            _appendTimeTicks = enabled;
            return this;
        }

        public AssetPatchRuntimeProfileBuilder WithDownloadPolicy(AssetPatchDownloadPolicy policy)
        {
            _downloadPolicy = policy;
            return this;
        }

        public AssetPatchRuntimeProfileBuilder WithTrustPolicy(AssetPatchTrustPolicy policy)
        {
            _trustPolicy = policy;
            return this;
        }

        public AssetPatchRuntimeProfile Build()
        {
            if (string.IsNullOrWhiteSpace(_packageName))
            {
                throw new InvalidOperationException("Patch profile package name cannot be null or empty.");
            }

            return new AssetPatchRuntimeProfile(
                _packageName,
                _platform,
                _autoDownloadOnFoundNewVersion,
                _appendTimeTicks,
                _downloadPolicy,
                _trustPolicy);
        }
    }
}
