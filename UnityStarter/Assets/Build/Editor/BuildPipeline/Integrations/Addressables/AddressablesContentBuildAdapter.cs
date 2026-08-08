using System;
using System.Collections.Generic;
using System.IO;

namespace Build.Pipeline.Editor
{
    [AssetContentAdapterRegistration(AddressablesBuildConfig.ProviderIdValue)]
    public sealed class AddressablesContentBuildAdapter :
        IAssetContentBuildAdapter,
        IAssetContentBuildOutputClaimProvider,
        IAssetContentPlayerBuildSessionFactory
    {
        internal const string PlayerSessionKey = "addressables-player-session";

        public string ProviderId => AddressablesBuildConfig.ProviderIdValue;
        public string ExclusivePlayerSessionKey => PlayerSessionKey;

        public IReadOnlyList<string> GetExclusiveOutputPaths(
            AssetContentBuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!(request.Configuration is AddressablesBuildConfig config))
            {
                throw new InvalidOperationException(
                    "AddressablesBuildConfig is required for output claims.");
            }

            if (!config.copyToOutputDirectory)
            {
                return Array.Empty<string>();
            }

            string configuredOutput =
                AddressablesBuilder.ResolveConfiguredPublicationDirectory(
                    request.InvocationId,
                    config.buildOutputDirectory);
            string root = BuildPathPolicy.ResolveBuildRoot(
                request.ProjectRoot,
                configuredOutput);
            return new[] { Path.Combine(root, request.BuildTarget.ToString()) };
        }

        public AssetContentBuildResult Validate(AssetContentBuildRequest request)
        {
            if (!(request.Configuration is AddressablesBuildConfig config))
            {
                return AssetContentBuildResult.Failure(ProviderId, "Addressables", request.PackageVersion, "Preflight", "AddressablesBuildConfig is required.");
            }

            if (ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings") == null)
            {
                return AssetContentBuildResult.Failure(
                    ProviderId,
                    "Addressables",
                    request.PackageVersion,
                    "Preflight",
                    "Addressables package is not installed or its supported editor API is unavailable.");
            }

            string integrationError = AddressablesVersionBuildProcessor.ValidateSupport(
                request.Incrementality);
            if (!string.IsNullOrEmpty(integrationError))
            {
                return AssetContentBuildResult.Failure(
                    ProviderId,
                    "Addressables",
                    request.PackageVersion,
                    "Preflight",
                    integrationError);
            }

            string contentBuildError = AddressablesBuilder.ValidateContentBuildConfiguration(
                request,
                config);
            if (!string.IsNullOrEmpty(contentBuildError))
            {
                return AssetContentBuildResult.Failure(
                    ProviderId,
                    "Addressables",
                    request.PackageVersion,
                    "Preflight",
                    contentBuildError);
            }

            string publicationError = AddressablesBuilder.ValidatePublicationConfiguration(
                request.InvocationId,
                config,
                request.ProjectRoot);
            if (!string.IsNullOrEmpty(publicationError))
            {
                return AssetContentBuildResult.Failure(
                    ProviderId,
                    "Addressables",
                    request.PackageVersion,
                    "Preflight",
                    $"Addressables publication configuration is unsafe: {publicationError}");
            }

            return AssetContentBuildResult.Success(ProviderId, "Addressables", request.PackageVersion);
        }

        public AssetContentBuildOperation Build(AssetContentBuildRequest request)
        {
            var config = (AddressablesBuildConfig)request.Configuration;
            IBuildDeferredPublication publication = null;
            try
            {
                publication = AddressablesBuilder.Build(
                    request.InvocationId,
                    request.BuildTarget,
                    request.PackageVersion,
                    config,
                    request.Incrementality);

                string outputDirectory = null;
                string reportPath = null;
                var artifacts = new List<string>();
                if (config.copyToOutputDirectory)
                {
                    string configuredOutput =
                        AddressablesBuilder.ResolveConfiguredPublicationDirectory(
                            request.InvocationId,
                            config.buildOutputDirectory);
                    string root = BuildPathPolicy.ResolveBuildRoot(request.ProjectRoot, configuredOutput);
                    outputDirectory = Path.Combine(root, request.BuildTarget.ToString());
                    string playerDataDirectory = Path.Combine(outputDirectory, "PlayerData");
                    reportPath = Path.Combine(outputDirectory, "AddressablesArtifacts.json");
                    artifacts.Add(playerDataDirectory);
                    string remoteDirectory = Path.Combine(outputDirectory, "RemoteContent");
                    if (config.buildRemoteCatalog)
                    {
                        artifacts.Add(remoteDirectory);
                    }

                    artifacts.Add(Path.Combine(outputDirectory, "BuildMetadata"));
                    artifacts.Add(reportPath);
                }

                return new AssetContentBuildOperation(
                    new[]
                    {
                        AssetContentBuildResult.Success(
                            ProviderId,
                            "Addressables",
                            request.PackageVersion,
                            outputDirectory,
                            reportPath: reportPath,
                            producedArtifacts: artifacts)
                    },
                    publication);
            }
            catch (Exception exception)
            {
                Exception failure = exception;
                if (publication != null)
                {
                    try
                    {
                        publication.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        failure = new AggregateException(
                            "Addressables preparation failed and staged publication rollback did not complete.",
                            exception,
                            disposeException);
                    }
                }

                return new AssetContentBuildOperation(
                    new[]
                    {
                        AssetContentBuildResult.Failure(
                            ProviderId,
                            "Addressables",
                            request.PackageVersion,
                            "AddressablesBuilder.Build",
                            failure.Message,
                            failure.ToString())
                    });
            }
        }

        public IReadOnlyList<string> ValidatePlayerBuild(AssetContentBuildRequest request)
        {
            var errors = new List<string>();
            if (request == null)
            {
                errors.Add("Addressables Player build request is required.");
                return errors;
            }

            if (!(request.Configuration is AddressablesBuildConfig))
            {
                errors.Add("AddressablesBuildConfig is required for the Player build session.");
                return errors;
            }

            if (request.Incrementality == BuildIncrementality.Incremental)
            {
                errors.Add(
                    "Addressables Content Update output cannot feed a Player build. " +
                    "Run the Incremental asset-content invocation without the player step, or use Clean to build a new Player baseline.");
            }

            string integrationError = AddressablesVersionBuildProcessor.ValidateSupport(
                request.Incrementality);
            if (!string.IsNullOrEmpty(integrationError))
            {
                errors.Add(integrationError);
            }

            return errors;
        }

        public IDisposable BeginPlayerBuild(AssetContentBuildRequest request)
        {
            IReadOnlyList<string> errors = ValidatePlayerBuild(request);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Addressables Player build preflight failed: " + string.Join("; ", errors));
            }

            return AddressablesVersionBuildProcessor.BeginSession(
                request.BuildTarget,
                request.PackageVersion);
        }
    }
}
