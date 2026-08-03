using System;
using System.Collections.Generic;
using System.IO;

namespace Build.Pipeline.Editor
{
    [AssetContentAdapterRegistration(AssetContentProviderIds.Addressables)]
    public sealed class AddressablesContentBuildAdapter :
        IAssetContentBuildAdapter,
        IAssetContentPlayerBuildSessionFactory
    {
        public string ProviderId => AssetContentProviderIds.Addressables;
        public int Priority => 0;

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
                request.Incrementality == BuildIncrementality.Clean);
            if (!string.IsNullOrEmpty(integrationError))
            {
                return AssetContentBuildResult.Failure(
                    ProviderId,
                    "Addressables",
                    request.PackageVersion,
                    "Preflight",
                    integrationError);
            }

            string publicationError = AddressablesBuilder.ValidatePublicationConfiguration(
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

        public IReadOnlyList<AssetContentBuildResult> Build(AssetContentBuildRequest request)
        {
            var config = (AddressablesBuildConfig)request.Configuration;
            try
            {
                AddressablesBuilder.Build(
                    request.BuildTarget,
                    request.PackageVersion,
                    config,
                    request.Incrementality == BuildIncrementality.Clean);

                string outputDirectory = null;
                string reportPath = null;
                var artifacts = new List<string>();
                if (config.copyToOutputDirectory)
                {
                    string configuredOutput = string.IsNullOrWhiteSpace(config.buildOutputDirectory)
                        ? AddressablesBuildConfig.DefaultBuildOutputDirectory
                        : config.buildOutputDirectory;
                    string root = BuildPathPolicy.ResolveBuildRoot(request.ProjectRoot, configuredOutput);
                    outputDirectory = Path.Combine(root, request.BuildTarget.ToString());
                    if (!Directory.Exists(outputDirectory))
                    {
                        throw new DirectoryNotFoundException($"Addressables published output was not found: '{outputDirectory}'.");
                    }

                    string playerDataDirectory = Path.Combine(outputDirectory, "PlayerData");
                    reportPath = Path.Combine(outputDirectory, "AddressablesArtifacts.json");
                    if (!Directory.Exists(playerDataDirectory) || !File.Exists(reportPath))
                    {
                        throw new FileNotFoundException(
                            "Addressables publication is incomplete or its manifest is missing.",
                            reportPath);
                    }

                    artifacts.Add(playerDataDirectory);
                    string remoteDirectory = Path.Combine(outputDirectory, "RemoteContent");
                    if (Directory.Exists(remoteDirectory))
                    {
                        artifacts.Add(remoteDirectory);
                    }

                    artifacts.Add(reportPath);
                }

                return new[]
                {
                    AssetContentBuildResult.Success(
                        ProviderId,
                        "Addressables",
                        request.PackageVersion,
                        outputDirectory,
                        reportPath: reportPath,
                        producedArtifacts: artifacts)
                };
            }
            catch (Exception exception)
            {
                return new[]
                {
                    AssetContentBuildResult.Failure(
                        ProviderId,
                        "Addressables",
                        request.PackageVersion,
                        "AddressablesBuilder.Build",
                        exception.Message,
                        exception.ToString())
                };
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

            string integrationError = AddressablesVersionBuildProcessor.ValidateSupport(
                request.Incrementality == BuildIncrementality.Clean);
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
