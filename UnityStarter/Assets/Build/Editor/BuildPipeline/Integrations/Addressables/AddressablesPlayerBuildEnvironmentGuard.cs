using System;
using System.Collections.Generic;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Owns Addressables package hooks that must be disabled when the selected
    /// Player recipe does not consume an Addressables content invocation.
    /// </summary>
    [PlayerBuildEnvironmentGuardRegistration(GuardIdentifier)]
    public sealed class AddressablesPlayerBuildEnvironmentGuard :
        IPlayerBuildEnvironmentGuard
    {
        internal const string GuardIdentifier = "addressables-player-hook";

        private readonly Func<bool> isPackageInstalled;
        private readonly Func<string> validateSuppressionSupport;
        private readonly Func<string, IDisposable> beginSuppressed;

        public AddressablesPlayerBuildEnvironmentGuard()
            : this(
                AddressablesPlayerBuildIsolation.IsPackageInstalled,
                AddressablesPlayerBuildIsolation.ValidateSuppressionSupport,
                AddressablesPlayerBuildIsolation.BeginSuppressed)
        {
        }

        internal AddressablesPlayerBuildEnvironmentGuard(
            Func<bool> isPackageInstalled,
            Func<string> validateSuppressionSupport,
            Func<string, IDisposable> beginSuppressed)
        {
            this.isPackageInstalled = isPackageInstalled
                ?? throw new ArgumentNullException(nameof(isPackageInstalled));
            this.validateSuppressionSupport = validateSuppressionSupport
                ?? throw new ArgumentNullException(nameof(validateSuppressionSupport));
            this.beginSuppressed = beginSuppressed
                ?? throw new ArgumentNullException(nameof(beginSuppressed));
        }

        public string GuardId => GuardIdentifier;

        public IReadOnlyList<string> ValidateEnvironment(
            PlayerBuildEnvironmentRequest request)
        {
            if (request == null)
            {
                return new[] { "Addressables Player environment request is required." };
            }

            if (!ShouldSuppress(request) || !isPackageInstalled())
            {
                return Array.Empty<string>();
            }

            string error = validateSuppressionSupport();
            return string.IsNullOrWhiteSpace(error)
                ? Array.Empty<string>()
                : new[] { "Addressables Player hook suppression is unavailable: " + error };
        }

        public IDisposable BeginEnvironment(PlayerBuildEnvironmentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return ShouldSuppress(request) && isPackageInstalled()
                ? beginSuppressed(request.BuildRequest.ProjectRoot)
                : NoOpScope.Instance;
        }

        internal static bool ShouldSuppress(PlayerBuildEnvironmentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return !request.HasAssetContentProvider(
                AddressablesBuildConfig.ProviderIdValue);
        }

        private sealed class NoOpScope : IDisposable
        {
            internal static readonly NoOpScope Instance = new NoOpScope();

            public void Dispose()
            {
            }
        }
    }
}
