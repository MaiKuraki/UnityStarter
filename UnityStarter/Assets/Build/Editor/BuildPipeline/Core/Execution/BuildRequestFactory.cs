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
            bool exportAndroidProject = false,
            IReadOnlyList<string> invocationIdsOverride = null)
        {
            if (buildData == null)
            {
                throw new ArgumentNullException(nameof(buildData));
            }

            IReadOnlyList<string> effectiveInvocationIds = invocationIdsOverride;
            if (invocationIdsOverride != null
                && !BuildRecipeSelection.TryExpandRequiredClosure(
                    buildData.RecipeInvocations,
                    invocationIdsOverride,
                    out effectiveInvocationIds,
                    out string selectionError))
            {
                throw new BuildFailedException(selectionError);
            }

            BuildAuthoringAssetGuard.EnsureSaved(buildData, effectiveInvocationIds);
            ValidateAndroidExport(target, exportAndroidProject);
            NamedBuildTarget namedTarget = GetNamedBuildTarget(target);
            bool outputIsFolder = IsFolderOutput(target, null, exportAndroidProject);
            string output = GetDefaultRelativeOutput(
                target,
                buildData.ProductName,
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
                deleteDebugFiles: !debugBuild,
                debugBuild,
                exportAndroidProject,
                allowExternalOutput: false,
                cheatOverride: null,
                applicationVersionOverride: null,
                outputBasePathOverride: null,
                versionInfoAssetPathOverride: null,
                effectiveInvocationIds,
                commandLineRecipeOverride: null,
                stepConfigurationPathOverrides: null,
                stepIncrementalityOverrides: null,
                stepDependencyOverrides: null,
                identityOverride: BuildIdentityOverride.Empty);
        }

        internal static BuildRequest CreateLocalReleasePreview(
            BuildData buildData,
            BuildTarget target,
            IReadOnlyList<string> invocationIdsOverride)
        {
            if (buildData == null)
            {
                throw new ArgumentNullException(nameof(buildData));
            }

            IReadOnlyList<string> effectiveInvocationIds = invocationIdsOverride;
            if (effectiveInvocationIds == null)
            {
                effectiveInvocationIds = ResolveLocalPreviewPlayerSelection(buildData);
            }
            else if (!BuildRecipeSelection.TryExpandRequiredClosure(
                         buildData.RecipeInvocations,
                         effectiveInvocationIds,
                         out effectiveInvocationIds,
                         out string selectionError))
            {
                throw new BuildFailedException(selectionError);
            }

            BuildAuthoringAssetGuard.EnsureSaved(buildData, effectiveInvocationIds);
            NamedBuildTarget namedTarget = GetNamedBuildTarget(target);
            bool outputIsFolder = IsFolderOutput(
                target,
                requestedOutput: null,
                exportAndroidProject: false);
            string requestedOutput = GetDefaultRelativeOutput(
                target,
                buildData.ProductName,
                debugBuild: false,
                exportAndroidProject: false);
            return Create(
                buildData,
                target,
                namedTarget,
                PlayerSettings.GetScriptingBackend(namedTarget),
                requestedOutput,
                outputRelativeToBuildRoot: true,
                outputIsFolder,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                applicationVersionOverride: null,
                outputBasePathOverride: Path.Combine(
                    buildData.OutputBasePath,
                    "LocalPreview"),
                versionInfoAssetPathOverride: null,
                effectiveInvocationIds,
                commandLineRecipeOverride: null,
                stepConfigurationPathOverrides: null,
                stepIncrementalityOverrides: null,
                stepDependencyOverrides: null,
                identityOverride: BuildIdentityOverride.Empty,
                purpose: BuildPurpose.LocalReleasePreview);
        }

        public static BuildRequest CreateForCommandLine(
            BuildData buildData,
            BuildCommandLineOptions options)
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
                deleteDebugFiles: !options.DebugBuild,
                options.DebugBuild,
                options.ExportAndroidProject,
                options.AllowExternalOutput,
                options.CheatEnabled,
                options.ApplicationVersion,
                options.OutputBasePath,
                options.VersionInfoAssetPath,
                invocationIdsOverride: options.SelectedInvocationIds.Count == 0
                    ? null
                    : options.SelectedInvocationIds,
                options.RecipeInvocations,
                options.StepConfigurationPathOverrides,
                options.StepIncrementalityOverrides,
                options.StepDependencyOverrides,
                identityOverride: options.IdentityOverride,
                replaceExactVersion: options.ReplaceExactVersion);
        }

        private static BuildRequest Create(
            BuildData buildData,
            BuildTarget target,
            NamedBuildTarget namedTarget,
            ScriptingImplementation scriptingBackend,
            string requestedOutput,
            bool outputRelativeToBuildRoot,
            bool outputIsFolder,
            bool deleteDebugFiles,
            bool debugBuild,
            bool exportAndroidProject,
            bool allowExternalOutput,
            bool? cheatOverride,
            string applicationVersionOverride,
            string outputBasePathOverride,
            string versionInfoAssetPathOverride,
            IReadOnlyList<string> invocationIdsOverride,
            IReadOnlyList<BuildCommandLineRecipeInvocation> commandLineRecipeOverride,
            IReadOnlyDictionary<string, string> stepConfigurationPathOverrides,
            IReadOnlyDictionary<string, BuildIncrementality> stepIncrementalityOverrides,
            IReadOnlyDictionary<string, IReadOnlyList<BuildInvocationDependency>> stepDependencyOverrides,
            BuildIdentityOverride identityOverride,
            bool replaceExactVersion = false)
        {
            return Create(
                buildData,
                target,
                namedTarget,
                scriptingBackend,
                requestedOutput,
                outputRelativeToBuildRoot,
                outputIsFolder,
                deleteDebugFiles,
                debugBuild,
                exportAndroidProject,
                allowExternalOutput,
                cheatOverride,
                applicationVersionOverride,
                outputBasePathOverride,
                versionInfoAssetPathOverride,
                invocationIdsOverride,
                commandLineRecipeOverride,
                stepConfigurationPathOverrides,
                stepIncrementalityOverrides,
                stepDependencyOverrides,
                identityOverride,
                debugBuild ? BuildPurpose.Development : BuildPurpose.Release,
                replaceExactVersion);
        }

        private static BuildRequest Create(
            BuildData buildData,
            BuildTarget target,
            NamedBuildTarget namedTarget,
            ScriptingImplementation scriptingBackend,
            string requestedOutput,
            bool outputRelativeToBuildRoot,
            bool outputIsFolder,
            bool deleteDebugFiles,
            bool debugBuild,
            bool exportAndroidProject,
            bool allowExternalOutput,
            bool? cheatOverride,
            string applicationVersionOverride,
            string outputBasePathOverride,
            string versionInfoAssetPathOverride,
            IReadOnlyList<string> invocationIdsOverride,
            IReadOnlyList<BuildCommandLineRecipeInvocation> commandLineRecipeOverride,
            IReadOnlyDictionary<string, string> stepConfigurationPathOverrides,
            IReadOnlyDictionary<string, BuildIncrementality> stepIncrementalityOverrides,
            IReadOnlyDictionary<string, IReadOnlyList<BuildInvocationDependency>> stepDependencyOverrides,
            BuildIdentityOverride identityOverride,
            BuildPurpose purpose,
            bool replaceExactVersion = false)
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
            IReadOnlyList<BuildStepInvocation> invocations = ResolveStepInvocations(
                buildData,
                invocationIdsOverride,
                commandLineRecipeOverride,
                stepConfigurationPathOverrides,
                stepIncrementalityOverrides,
                stepDependencyOverrides);

            if (purpose == BuildPurpose.LocalReleasePreview)
            {
                ValidateLocalReleasePreview(invocations);
            }

            ValidateAndroidExportRecipe(invocations, exportAndroidProject);
            string versionInfoAssetPath = string.IsNullOrWhiteSpace(versionInfoAssetPathOverride)
                ? buildData.VersionInfoAssetPath
                : versionInfoAssetPathOverride.Trim().Replace('\\', '/');
            return new BuildRequest(
                buildData.CompanyName,
                buildData.ProductName,
                buildData.ApplicationIdentifier,
                versionInfoAssetPath,
                buildData.GetBuildScenePaths(),
                buildData.CheatBuildMode,
                target,
                namedTarget,
                scriptingBackend,
                projectRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder,
                deleteDebugFiles,
                debugBuild,
                exportAndroidProject,
                allowExternalOutput,
                cheatOverride,
                Application.isBatchMode,
                applicationVersion,
                identityOverride ?? throw new ArgumentNullException(nameof(identityOverride)),
                invocations,
                buildData.SourceCleanlinessPolicy,
                purpose,
                replaceExactVersion);
        }

        private static IReadOnlyList<string> ResolveLocalPreviewPlayerSelection(
            BuildData buildData)
        {
            if (TryResolveLocalReleasePreviewSelection(
                    buildData,
                    out IReadOnlyList<string> selected,
                    out string error))
            {
                return selected;
            }

            throw new BuildFailedException(error);
        }

        internal static bool TryResolveLocalReleasePreviewSelection(
            BuildData buildData,
            out IReadOnlyList<string> selected,
            out string error)
        {
            selected = Array.Empty<string>();
            error = string.Empty;
            if (buildData == null)
            {
                error = "Local Release Preview requires a build profile.";
                return false;
            }

            string playerInvocationId = null;
            IReadOnlyList<BuildRecipeInvocation> authored = buildData.RecipeInvocations;
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                if (invocation == null
                    || !invocation.Enabled
                    || !string.Equals(
                        invocation?.StepTypeId,
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (playerInvocationId != null)
                {
                    error = "Local Release Preview requires one unambiguous Player invocation.";
                    return false;
                }

                playerInvocationId = invocation.InvocationId;
            }

            if (string.IsNullOrWhiteSpace(playerInvocationId))
            {
                error = "Local Release Preview requires one Player invocation.";
                return false;
            }

            if (!BuildRecipeSelection.TryExpandRequiredClosure(
                    authored,
                    new[] { playerInvocationId },
                    out selected,
                    out error))
            {
                return false;
            }

            // A local Release Player still needs its content baked in so the Player can load
            // offline bundles from StreamingAssets. Include every enabled asset-content invocation;
            // hot-update and custom invocations stay excluded from this isolated preview.
            var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                if (invocation != null
                    && invocation.Enabled
                    && string.Equals(
                        invocation.StepTypeId,
                        BuildStepTypeIds.AssetContent,
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedSet.Add(invocation.InvocationId);
                }
            }

            var ordered = new List<string>(selectedSet.Count);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                if (invocation != null && selectedSet.Contains(invocation.InvocationId))
                {
                    ordered.Add(invocation.InvocationId);
                }
            }

            selected = ordered;

            if (!ValidateLocalReleasePreviewSelection(authored, selected, out error))
            {
                return false;
            }

            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                if (!string.Equals(
                        invocation?.InvocationId,
                        playerInvocationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (invocation.Incrementality != BuildIncrementality.Clean)
                {
                    error = "Local Release Preview requires a Clean Player invocation.";
                    return false;
                }

                return true;
            }

            error = "Local Release Preview Player invocation could not be resolved.";
            return false;
        }

        private static bool ValidateLocalReleasePreviewSelection(
            IReadOnlyList<BuildRecipeInvocation> authored,
            IReadOnlyList<string> selectedIds,
            out string error)
        {
            error = string.Empty;
            int playerCount = 0;
            var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                if (invocation == null || !selected.Contains(invocation.InvocationId))
                {
                    continue;
                }

                if (string.Equals(
                        invocation.StepTypeId,
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    playerCount++;
                    continue;
                }

                if (string.Equals(
                        invocation.StepTypeId,
                        BuildStepTypeIds.AssetContent,
                        StringComparison.OrdinalIgnoreCase)
                    && invocation.Enabled)
                {
                    continue;
                }

                error =
                    "Local Release Preview can only include the Player and its enabled asset-content dependencies. " +
                    "Disabled content, hot-update, and custom invocations are not allowed.";
                return false;
            }

            if (playerCount != 1)
            {
                error = "Local Release Preview requires exactly one Player invocation.";
                return false;
            }

            return true;
        }

        private static void ValidateLocalReleasePreview(
            IReadOnlyList<BuildStepInvocation> invocations)
        {
            if (invocations == null || invocations.Count == 0)
            {
                throw new BuildFailedException(
                    "Local Release Preview requires at least a Player invocation.");
            }

            int playerCount = 0;
            for (int index = 0; index < invocations.Count; index++)
            {
                BuildStepInvocation invocation = invocations[index];
                if (invocation == null)
                {
                    throw new BuildFailedException(
                        "Local Release Preview contains an invalid invocation.");
                }

                if (string.Equals(
                        invocation.StepTypeId,
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    playerCount++;
                    if (invocation.Incrementality != BuildIncrementality.Clean)
                    {
                        throw new BuildFailedException(
                            "Local Release Preview requires a Clean Player invocation.");
                    }

                    continue;
                }

                if (string.Equals(
                        invocation.StepTypeId,
                        BuildStepTypeIds.AssetContent,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw new BuildFailedException(
                    "Local Release Preview can only include the Player and asset-content invocations. " +
                    "Hot-update and custom invocations are not allowed.");
            }

            if (playerCount != 1)
            {
                throw new BuildFailedException(
                    "Local Release Preview requires exactly one Player invocation.");
            }
        }

        internal static void ValidateLocalReleasePreviewRequest(BuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Purpose != BuildPurpose.LocalReleasePreview)
            {
                return;
            }

            if (request.BatchMode
                || request.DebugBuild
                || !request.DeleteDebugFiles
                || request.ExportAndroidProject
                || request.AllowExternalOutput
                || request.CanPublishReleaseBaseline
                || !string.Equals(
                    Path.GetFileName(request.BuildRoot),
                    "LocalPreview",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "Local Release Preview must remain an interactive, optimized, isolated, " +
                    "non-distributable request rooted below a LocalPreview directory.");
            }

            ValidateLocalReleasePreview(request.Steps);
        }

        private static IReadOnlyList<BuildStepInvocation> ResolveStepInvocations(
            BuildData buildData,
            IReadOnlyList<string> invocationIdsOverride,
            IReadOnlyList<BuildCommandLineRecipeInvocation> commandLineRecipeOverride,
            IReadOnlyDictionary<string, string> configurationPathOverrides,
            IReadOnlyDictionary<string, BuildIncrementality> incrementalityOverrides,
            IReadOnlyDictionary<string, IReadOnlyList<BuildInvocationDependency>> dependencyOverrides)
        {
            IReadOnlyList<BuildRecipeInvocation> authored = buildData.RecipeInvocations;
            var authoredByInvocation = new Dictionary<string, BuildRecipeInvocation>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation entry = authored[index];
                string invocationId = entry?.InvocationId?.Trim();
                if (string.IsNullOrEmpty(invocationId))
                {
                    throw new BuildFailedException(
                        $"Build recipe invocation at index {index} has an empty invocation id.");
                }

                if (string.IsNullOrWhiteSpace(entry.StepTypeId))
                {
                    throw new BuildFailedException(
                        $"Build recipe invocation '{invocationId}' has an empty step type id.");
                }

                if (!authoredByInvocation.TryAdd(invocationId, entry))
                {
                    throw new BuildFailedException(
                        $"Build recipe contains duplicate invocation id '{invocationId}'.");
                }
            }

            bool hasExplicitCommandLineRecipe = commandLineRecipeOverride != null
                && commandLineRecipeOverride.Count > 0;
            if (hasExplicitCommandLineRecipe
                && invocationIdsOverride != null
                && invocationIdsOverride.Count > 0)
            {
                throw new BuildFailedException(
                    "A focused profile selection cannot be combined with an explicit command-line recipe replacement.");
            }

            IReadOnlyList<string> selectedIds;
            if (invocationIdsOverride == null)
            {
                selectedIds = buildData.EnabledInvocationIds;
            }
            else if (!BuildRecipeSelection.TryExpandRequiredClosure(
                         authored,
                         invocationIdsOverride,
                         out selectedIds,
                         out string selectionError))
            {
                throw new BuildFailedException(selectionError);
            }

            int selectedCount = hasExplicitCommandLineRecipe
                ? commandLineRecipeOverride.Count
                : selectedIds.Count;
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var invocations = new List<BuildStepInvocation>(selectedCount);
            for (int index = 0; index < selectedCount; index++)
            {
                string invocationId;
                string stepTypeId;
                ScriptableObject configuration;
                BuildIncrementality incrementality;
                IReadOnlyList<BuildInvocationDependency> dependencies;

                if (hasExplicitCommandLineRecipe)
                {
                    BuildCommandLineRecipeInvocation commandLineInvocation =
                        commandLineRecipeOverride[index]
                        ?? throw new BuildFailedException(
                            $"Command-line recipe invocation at index {index} is null.");
                    invocationId = commandLineInvocation.InvocationId?.Trim();
                    stepTypeId = commandLineInvocation.StepTypeId?.Trim();
                    configuration = null;
                    incrementality = BuildIncrementality.Clean;
                    dependencies = Array.Empty<BuildInvocationDependency>();
                }
                else
                {
                    invocationId = selectedIds[index]?.Trim();
                    if (string.IsNullOrEmpty(invocationId))
                    {
                        throw new BuildFailedException(
                            $"Selected recipe invocation at index {index} has an empty id.");
                    }

                    if (!authoredByInvocation.TryGetValue(
                            invocationId,
                            out BuildRecipeInvocation authoredEntry))
                    {
                        throw new BuildFailedException(
                            $"Selected recipe invocation '{invocationId}' does not exist in the build profile.");
                    }

                    stepTypeId = authoredEntry.StepTypeId;
                    configuration = authoredEntry.Configuration;
                    incrementality = authoredEntry.Incrementality;
                    dependencies = authoredEntry.Dependencies;
                }

                if (string.IsNullOrWhiteSpace(invocationId)
                    || string.IsNullOrWhiteSpace(stepTypeId))
                {
                    throw new BuildFailedException(
                        $"Selected recipe invocation at index {index} requires non-empty invocation and step type ids.");
                }

                if (!selected.Add(invocationId))
                {
                    throw new BuildFailedException(
                        $"Selected recipe invocation '{invocationId}' is specified more than once.");
                }

                if (configurationPathOverrides != null
                    && configurationPathOverrides.TryGetValue(
                        invocationId,
                        out string configurationPath))
                {
                    configuration = LoadStepConfiguration(invocationId, configurationPath);
                }

                if (incrementalityOverrides != null
                    && incrementalityOverrides.TryGetValue(
                        invocationId,
                        out BuildIncrementality overridePolicy))
                {
                    incrementality = overridePolicy;
                }

                if (dependencyOverrides != null
                    && dependencyOverrides.TryGetValue(
                        invocationId,
                        out IReadOnlyList<BuildInvocationDependency> overrideDependencies))
                {
                    dependencies = overrideDependencies;
                }

                ValidateStepConfigurationAsset(invocationId, configuration);
                invocations.Add(new BuildStepInvocation(
                    invocationId,
                    stepTypeId,
                    configuration,
                    incrementality,
                    dependencies));
            }

            ValidateOverrideTargets(selected, configurationPathOverrides, "configuration");
            ValidateOverrideTargets(selected, incrementalityOverrides, "incrementality");
            ValidateOverrideTargets(selected, dependencyOverrides, "dependency");
            return invocations;
        }

        private static void ValidateOverrideTargets<T>(
            HashSet<string> selectedInvocationIds,
            IReadOnlyDictionary<string, T> overrides,
            string overrideKind)
        {
            if (overrides == null)
            {
                return;
            }

            foreach (KeyValuePair<string, T> entry in overrides)
            {
                if (!selectedInvocationIds.Contains(entry.Key))
                {
                    throw new BuildFailedException(
                        $"Step {overrideKind} override '{entry.Key}' does not target a selected recipe invocation.");
                }
            }
        }

        private static ScriptableObject LoadStepConfiguration(
            string invocationId,
            string configurationPath)
        {
            string normalizedPath = configurationPath?.Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedPath)
                || !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !normalizedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    $"Configuration override for invocation '{invocationId}' must be a project-relative " +
                    $".asset path below Assets: '{configurationPath}'.");
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    normalizedPath,
                    $"Configuration override for invocation '{invocationId}'");
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    $"Configuration override path for invocation '{invocationId}' is not portable: " +
                    $"'{configurationPath}'. {exception.Message}");
            }

            ScriptableObject configuration = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                normalizedPath);
            if (configuration == null)
            {
                throw new BuildFailedException(
                    $"Configuration override for invocation '{invocationId}' does not resolve to a " +
                    $"ScriptableObject asset at '{normalizedPath}'.");
            }

            return configuration;
        }

        private static void ValidateStepConfigurationAsset(
            string invocationId,
            ScriptableObject configuration)
        {
            if (configuration == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(configuration)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    $"Configuration for invocation '{invocationId}' must be a persistent .asset below Assets. " +
                    "Package assets, in-memory objects, and scene objects cannot provide an equivalent CI recipe.");
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    path,
                    $"Configuration for invocation '{invocationId}'");
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    $"Configuration asset for invocation '{invocationId}' has a non-portable path: " +
                    $"'{path}'. {exception.Message}");
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != configuration)
            {
                throw new BuildFailedException(
                    $"Configuration for invocation '{invocationId}' must be the main asset at '{path}'. " +
                    "Sub-assets cannot be addressed unambiguously by CI.");
            }
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
                    throw new ArgumentOutOfRangeException(
                        nameof(target),
                        target,
                        "Unsupported player build target.");
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
                    throw new ArgumentOutOfRangeException(
                        nameof(target),
                        target,
                        "Unsupported player build target.");
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

            string artifactName;
            switch (target)
            {
                case BuildTarget.Android:
                    artifactName = exportAndroidProject ? "AndroidProject" : productName + ".apk";
                    break;
                case BuildTarget.StandaloneWindows64:
                    artifactName = productName + ".exe";
                    break;
                case BuildTarget.StandaloneOSX:
                    artifactName = productName + ".app";
                    break;
                default:
                    artifactName = productName;
                    break;
            }

            string variant = debugBuild ? "Development" : "Release";
            return Path.Combine(GetPlatformFolderName(target), variant, artifactName);
        }

        private static void ValidateAndroidExport(BuildTarget target, bool exportAndroidProject)
        {
            if (exportAndroidProject && target != BuildTarget.Android)
            {
                throw new ArgumentException(
                    "Android project export is valid only for the Android build target.");
            }
        }

        internal static void ValidateAndroidExportRecipe(
            IReadOnlyList<BuildStepInvocation> invocations,
            bool exportAndroidProject)
        {
            if (!exportAndroidProject)
            {
                return;
            }

            if (invocations == null)
            {
                throw new ArgumentNullException(nameof(invocations));
            }

            for (int index = 0; index < invocations.Count; index++)
            {
                if (string.Equals(
                        invocations[index]?.StepTypeId?.Trim(),
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            throw new ArgumentException(
                $"Android Gradle export requires a '{BuildStepTypeIds.Player}' invocation. " +
                "Add one to the selected recipe.",
                nameof(invocations));
        }
    }
}
