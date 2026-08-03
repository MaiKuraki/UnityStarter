using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class BuildRequestFactory
    {
        public static BuildRequest CreateInteractive(
            BuildData buildData,
            BuildTarget target,
            bool debugBuild,
            BuildIncrementality incrementality,
            bool exportAndroidProject = false)
        {
            ValidateAndroidExport(target, exportAndroidProject);
            NamedBuildTarget namedTarget = GetNamedBuildTarget(target);
            bool outputIsFolder = IsFolderOutput(target, null, exportAndroidProject);
            string output = GetDefaultRelativeOutput(
                target,
                buildData?.ProductName,
                debugBuild,
                exportAndroidProject);

            return Create(
                buildData,
                target,
                namedTarget,
                PlayerSettings.GetScriptingBackend(namedTarget),
                output,
                outputRelativeToBuildRoot: true,
                outputIsFolder,
                incrementality,
                deleteDebugFiles: !debugBuild,
                debugBuild,
                exportAndroidProject,
                allowExternalOutput: false,
                cheatOverride: null,
                applicationVersionOverride: null,
                outputBasePathOverride: null,
                versionInfoAssetPathOverride: null,
                assetContentProviderIdOverride: null,
                assetContentConfigurationPathOverride: null,
                useHybridClrOverride: null,
                stepIdsOverride: null);
        }

        public static BuildRequest CreateForCommandLine(BuildData buildData, BuildCommandLineOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ValidateAndroidExport(options.BuildTarget, options.ExportAndroidProject);
            NamedBuildTarget namedTarget = GetNamedBuildTarget(options.BuildTarget);
            bool outputIsFolder = IsFolderOutput(
                options.BuildTarget,
                options.OutputPath,
                options.ExportAndroidProject);
            bool outputRelativeToBuildRoot = string.IsNullOrWhiteSpace(options.OutputPath);
            string requestedOutput = outputRelativeToBuildRoot
                ? GetDefaultRelativeOutput(
                    options.BuildTarget,
                    buildData?.ProductName,
                    options.DebugBuild,
                    options.ExportAndroidProject)
                : options.OutputPath;

            return Create(
                buildData,
                options.BuildTarget,
                namedTarget,
                options.ScriptingBackend ?? PlayerSettings.GetScriptingBackend(namedTarget),
                requestedOutput,
                outputRelativeToBuildRoot,
                outputIsFolder,
                options.Incrementality,
                deleteDebugFiles: !options.DebugBuild,
                options.DebugBuild,
                options.ExportAndroidProject,
                options.AllowExternalOutput,
                options.CheatEnabled,
                options.ApplicationVersion,
                options.OutputBasePath,
                options.VersionInfoAssetPath,
                options.AssetContentProviderId,
                options.AssetContentConfigurationPath,
                options.UseHybridClr,
                options.StepIds);
        }

        private static BuildRequest Create(
            BuildData buildData,
            BuildTarget target,
            NamedBuildTarget namedTarget,
            ScriptingImplementation scriptingBackend,
            string requestedOutput,
            bool outputRelativeToBuildRoot,
            bool outputIsFolder,
            BuildIncrementality incrementality,
            bool deleteDebugFiles,
            bool debugBuild,
            bool exportAndroidProject,
            bool allowExternalOutput,
            bool? cheatOverride,
            string applicationVersionOverride,
            string outputBasePathOverride,
            string versionInfoAssetPathOverride,
            string assetContentProviderIdOverride,
            string assetContentConfigurationPathOverride,
            bool? useHybridClrOverride,
            IReadOnlyList<string> stepIdsOverride)
        {
            if (buildData == null)
            {
                throw new ArgumentNullException(nameof(buildData));
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildRoot = BuildPathPolicy.ResolveBuildRoot(
                projectRoot,
                string.IsNullOrWhiteSpace(outputBasePathOverride)
                    ? buildData.OutputBasePath
                    : outputBasePathOverride.Trim());
            string outputPath = BuildPathPolicy.ResolveOutputPath(
                projectRoot,
                buildRoot,
                requestedOutput,
                outputRelativeToBuildRoot,
                allowExternalOutput);
            string outputDirectory = BuildPathPolicy.ResolveOutputDirectory(
                projectRoot,
                buildRoot,
                outputPath,
                outputIsFolder,
                allowExternalOutput);

            string applicationVersion = string.IsNullOrWhiteSpace(applicationVersionOverride)
                ? buildData.ApplicationVersion
                : applicationVersionOverride.Trim();
            bool useHybridClr = useHybridClrOverride ?? buildData.UseHybridCLR;
            IReadOnlyList<string> stepIds = stepIdsOverride ?? buildData.PipelineSteps;
            ValidateAndroidExportRecipe(stepIds, exportAndroidProject);
            string versionInfoAssetPath = string.IsNullOrWhiteSpace(versionInfoAssetPathOverride)
                ? buildData.VersionInfoAssetPath
                : versionInfoAssetPathOverride.Trim().Replace('\\', '/');
            ResolveAssetContentBinding(
                buildData,
                assetContentProviderIdOverride,
                assetContentConfigurationPathOverride,
                out string assetContentProviderId,
                out ScriptableObject assetContentConfiguration);
            ValidateContentOnlyRecipeBinding(
                stepIds,
                assetContentProviderId,
                assetContentConfiguration);

            return new BuildRequest(
                buildData.CompanyName,
                buildData.ProductName,
                buildData.ApplicationIdentifier,
                versionInfoAssetPath,
                buildData.GetBuildScenePaths(),
                buildData.CheatBuildMode,
                buildData.HybridCLRBuildConfig,
                target,
                namedTarget,
                scriptingBackend,
                projectRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder,
                incrementality,
                deleteDebugFiles,
                debugBuild,
                exportAndroidProject,
                allowExternalOutput,
                cheatOverride,
                Application.isBatchMode,
                applicationVersion,
                assetContentProviderId,
                assetContentConfiguration,
                useHybridClr,
                buildData.EnablePlayerObfuscation,
                stepIds);
        }

        private static void ResolveAssetContentBinding(
            BuildData buildData,
            string providerIdOverride,
            string configurationPathOverride,
            out string providerId,
            out ScriptableObject configuration)
        {
            if (string.IsNullOrWhiteSpace(providerIdOverride))
            {
                providerId = buildData.AssetContentProviderId?.Trim() ?? string.Empty;
                configuration = buildData.AssetContentConfiguration;
                return;
            }

            string requestedId = providerIdOverride.Trim();
            if (string.Equals(requestedId, "none", StringComparison.OrdinalIgnoreCase))
            {
                providerId = string.Empty;
                configuration = null;
                return;
            }

            AssetContentProviderDescriptor descriptor = null;
            var catalogDiagnostics = new List<string>();
            foreach (AssetContentProviderDescriptor candidate in
                     BuildPipelineRegistry.GetAssetContentProviderDescriptors(catalogDiagnostics))
            {
                if (string.Equals(
                    candidate.ProviderId,
                    requestedId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    descriptor = candidate;
                    break;
                }
            }

            if (descriptor == null)
            {
                string diagnostics = catalogDiagnostics.Count == 0
                    ? string.Empty
                    : " Catalog diagnostics: " + string.Join(" | ", catalogDiagnostics);
                throw new BuildFailedException(
                    $"Asset content provider '{requestedId}' is not declared by an installed authoring integration." +
                    diagnostics);
            }

            string normalizedPath = configurationPathOverride?.Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedPath)
                || !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !normalizedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    $"Asset content configuration must be a project-relative .asset path below Assets: '{configurationPathOverride}'.");
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    normalizedPath,
                    "Asset content configuration");
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    $"Asset content configuration path is not portable: '{configurationPathOverride}'. {exception.Message}");
            }

            configuration = AssetDatabase.LoadAssetAtPath(
                normalizedPath,
                descriptor.ConfigurationType) as ScriptableObject;
            if (configuration == null)
            {
                throw new BuildFailedException(
                    $"{descriptor.DisplayName} requires a {descriptor.ConfigurationType.Name} asset at '{normalizedPath}'.");
            }

            providerId = descriptor.ProviderId;
        }

        public static NamedBuildTarget GetNamedBuildTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return NamedBuildTarget.Android;
                case BuildTarget.iOS:
                    return NamedBuildTarget.iOS;
                case BuildTarget.WebGL:
                    return NamedBuildTarget.WebGL;
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return NamedBuildTarget.Standalone;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported player build target.");
            }
        }

        public static string GetPlatformFolderName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.StandaloneOSX:
                    return "Mac";
                case BuildTarget.StandaloneLinux64:
                    return "Linux";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported player build target.");
            }
        }

        private static bool IsFolderOutput(
            BuildTarget target,
            string requestedOutput,
            bool exportAndroidProject)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    if (exportAndroidProject)
                    {
                        if (HasAndroidPackageExtension(requestedOutput))
                        {
                            throw new ArgumentException(
                                "Android project export requires a directory output, not an .apk or .aab path.");
                        }

                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(requestedOutput)
                        && !HasAndroidPackageExtension(requestedOutput))
                    {
                        throw new ArgumentException(
                            "Android package output must end with .apk or .aab. Use " +
                            $"{BuildCommandLineOptionNames.ExportAndroidProject} for a directory export.");
                    }

                    return false;
                case BuildTarget.StandaloneOSX:
                case BuildTarget.WebGL:
                case BuildTarget.iOS:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasAndroidPackageExtension(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && (path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".aab", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDefaultRelativeOutput(
            BuildTarget target,
            string productName,
            bool debugBuild,
            bool exportAndroidProject)
        {
            BuildPathPolicy.ValidatePortableFileName(productName, "Product name");

            string safeProductName = productName;
            string artifactName;
            switch (target)
            {
                case BuildTarget.Android:
                    artifactName = exportAndroidProject ? "AndroidProject" : safeProductName + ".apk";
                    break;
                case BuildTarget.StandaloneWindows64:
                    artifactName = safeProductName + ".exe";
                    break;
                case BuildTarget.StandaloneOSX:
                    artifactName = safeProductName + ".app";
                    break;
                default:
                    artifactName = safeProductName;
                    break;
            }

            string variant = debugBuild ? "Development" : "Release";
            return Path.Combine(GetPlatformFolderName(target), variant, artifactName);
        }

        private static void ValidateAndroidExport(BuildTarget target, bool exportAndroidProject)
        {
            if (exportAndroidProject && target != BuildTarget.Android)
            {
                throw new ArgumentException("Android project export is valid only for the Android build target.");
            }
        }

        internal static void ValidateAndroidExportRecipe(
            IReadOnlyList<string> stepIds,
            bool exportAndroidProject)
        {
            if (!exportAndroidProject)
            {
                return;
            }

            if (stepIds == null)
            {
                throw new ArgumentNullException(nameof(stepIds));
            }

            for (int index = 0; index < stepIds.Count; index++)
            {
                if (string.Equals(
                        stepIds[index]?.Trim(),
                        BuildStepIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            throw new ArgumentException(
                $"Android Gradle export requires the '{BuildStepIds.Player}' step. " +
                "Apply the Player + Dependencies preset or add the step to the selected recipe.",
                nameof(stepIds));
        }

        internal static void ValidateContentOnlyRecipeBinding(
            IReadOnlyList<string> stepIds,
            string providerId,
            ScriptableObject configuration)
        {
            if (stepIds == null)
            {
                throw new ArgumentNullException(nameof(stepIds));
            }

            bool includesAssetContent = false;
            bool includesPlayer = false;
            for (int index = 0; index < stepIds.Count; index++)
            {
                string stepId = stepIds[index]?.Trim();
                includesAssetContent |= string.Equals(
                    stepId,
                    BuildStepIds.AssetContent,
                    StringComparison.OrdinalIgnoreCase);
                includesPlayer |= string.Equals(
                    stepId,
                    BuildStepIds.Player,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!includesAssetContent || includesPlayer)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(providerId) && configuration != null)
            {
                return;
            }

            throw new ArgumentException(
                $"A recipe containing '{BuildStepIds.AssetContent}' without '{BuildStepIds.Player}' " +
                "requires both an Asset Content Provider and its Configuration. " +
                "Configure the content binding or choose a Player recipe.",
                nameof(stepIds));
        }
    }
}
