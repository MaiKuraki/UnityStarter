using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Editor
{
    public sealed class BuildCommandLineOptions
    {
        public BuildTarget BuildTarget { get; internal set; } = BuildTarget.NoTarget;
        public string BuildProfilePath { get; internal set; }
        public string OutputPath { get; internal set; }
        public string ApplicationVersion { get; internal set; }
        public string OutputBasePath { get; internal set; }
        public string VersionInfoAssetPath { get; internal set; }
        public string AssetContentProviderId { get; internal set; }
        public string AssetContentConfigurationPath { get; internal set; }
        public BuildIncrementality Incrementality { get; internal set; } = BuildIncrementality.Clean;
        public bool DebugBuild { get; internal set; }
        public bool ExportAndroidProject { get; internal set; }
        public bool AllowExternalOutput { get; internal set; }
        public ScriptingImplementation? ScriptingBackend { get; internal set; }
        public bool? UseHybridClr { get; internal set; }
        public bool? CheatEnabled { get; internal set; }
        public string[] StepIds { get; internal set; }
    }

    /// <summary>
    /// Stable command-line tokens owned by this build pipeline. Unity's native
    /// <c>-buildTarget</c> token is intentionally reused; every custom token is
    /// isolated under the <c>-pipeline</c> namespace to avoid collisions with
    /// Unity Editor command-line arguments.
    /// </summary>
    public static class BuildCommandLineOptionNames
    {
        public const string Prefix = "-pipeline";
        public const string BuildTarget = "-buildTarget";
        public const string Profile = Prefix + "Profile";
        public const string ScriptingBackend = Prefix + "ScriptingBackend";
        public const string Output = Prefix + "Output";
        public const string Version = Prefix + "Version";
        public const string OutputRoot = Prefix + "OutputRoot";
        public const string VersionInfo = Prefix + "VersionInfo";
        public const string Provider = Prefix + "Provider";
        public const string ProviderConfiguration = Prefix + "ProviderConfig";
        public const string Steps = Prefix + "Steps";
        public const string Clean = Prefix + "Clean";
        public const string Incremental = Prefix + "Incremental";
        public const string Development = Prefix + "Development";
        public const string ExportAndroidProject = Prefix + "ExportAndroidProject";
        public const string UseHybridCLR = Prefix + "UseHybridCLR";
        public const string SkipHybridCLR = Prefix + "SkipHybridCLR";
        public const string EnableCheat = Prefix + "EnableCheat";
        public const string DisableCheat = Prefix + "DisableCheat";
        public const string AllowExternalOutput = Prefix + "AllowExternalOutput";
    }

    public static class BuildCommandLine
    {
        private static readonly HashSet<string> ValueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildCommandLineOptionNames.BuildTarget,
            BuildCommandLineOptionNames.Profile,
            BuildCommandLineOptionNames.ScriptingBackend,
            BuildCommandLineOptionNames.Output,
            BuildCommandLineOptionNames.Version,
            BuildCommandLineOptionNames.OutputRoot,
            BuildCommandLineOptionNames.VersionInfo,
            BuildCommandLineOptionNames.Provider,
            BuildCommandLineOptionNames.ProviderConfiguration,
            BuildCommandLineOptionNames.Steps
        };

        private static readonly HashSet<string> FlagOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildCommandLineOptionNames.Clean,
            BuildCommandLineOptionNames.Incremental,
            BuildCommandLineOptionNames.Development,
            BuildCommandLineOptionNames.ExportAndroidProject,
            BuildCommandLineOptionNames.UseHybridCLR,
            BuildCommandLineOptionNames.SkipHybridCLR,
            BuildCommandLineOptionNames.EnableCheat,
            BuildCommandLineOptionNames.DisableCheat,
            BuildCommandLineOptionNames.AllowExternalOutput
        };

        public static BuildCommandLineOptions Parse(IReadOnlyList<string> arguments)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            var options = new BuildCommandLineOptions();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < arguments.Count; index++)
            {
                string argument = arguments[index];
                if (!ValueOptions.Contains(argument) && !FlagOptions.Contains(argument))
                {
                    if (LooksLikeBuildPipelineOption(argument))
                    {
                        throw new ArgumentException($"Unknown build pipeline option '{argument}'.");
                    }

                    continue;
                }

                if (!seen.Add(argument))
                {
                    throw new ArgumentException($"Build pipeline option '{argument}' was specified more than once.");
                }

                string value = null;
                if (ValueOptions.Contains(argument))
                {
                    if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Build pipeline option '{argument}' requires a value.");
                    }

                    value = arguments[++index];
                }

                ApplyOption(options, argument, value);
            }

            Validate(options, seen);
            return options;
        }

        private static void ApplyOption(BuildCommandLineOptions options, string argument, string value)
        {
            if (argument.Equals(BuildCommandLineOptionNames.BuildTarget, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSupportedBuildTarget(value, out BuildTarget target))
                {
                    throw new ArgumentException(
                        $"Unsupported build target '{value}'. Use Win64, OSXUniversal, Linux64, Android, iOS, or WebGL.");
                }

                options.BuildTarget = target;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Profile, StringComparison.OrdinalIgnoreCase))
            {
                options.BuildProfilePath = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.ScriptingBackend, StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse(value, true, out ScriptingImplementation backend)
                    || (backend != ScriptingImplementation.Mono2x && backend != ScriptingImplementation.IL2CPP))
                {
                    throw new ArgumentException(
                        $"Unsupported scripting backend '{value}'. Use Mono2x or IL2CPP.");
                }

                options.ScriptingBackend = backend;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Output, StringComparison.OrdinalIgnoreCase))
            {
                options.OutputPath = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Version, StringComparison.OrdinalIgnoreCase))
            {
                options.ApplicationVersion = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.OutputRoot, StringComparison.OrdinalIgnoreCase))
            {
                options.OutputBasePath = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.VersionInfo, StringComparison.OrdinalIgnoreCase))
            {
                options.VersionInfoAssetPath = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Provider, StringComparison.OrdinalIgnoreCase))
            {
                options.AssetContentProviderId = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.ProviderConfiguration, StringComparison.OrdinalIgnoreCase))
            {
                options.AssetContentConfigurationPath = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Steps, StringComparison.OrdinalIgnoreCase))
            {
                options.StepIds = ParseStepIds(value);
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Clean, StringComparison.OrdinalIgnoreCase))
            {
                options.Incrementality = BuildIncrementality.Clean;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Incremental, StringComparison.OrdinalIgnoreCase))
            {
                options.Incrementality = BuildIncrementality.Incremental;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Development, StringComparison.OrdinalIgnoreCase))
            {
                options.DebugBuild = true;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.ExportAndroidProject, StringComparison.OrdinalIgnoreCase))
            {
                options.ExportAndroidProject = true;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.AllowExternalOutput, StringComparison.OrdinalIgnoreCase))
            {
                options.AllowExternalOutput = true;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.UseHybridCLR, StringComparison.OrdinalIgnoreCase))
            {
                options.UseHybridClr = true;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.SkipHybridCLR, StringComparison.OrdinalIgnoreCase))
            {
                options.UseHybridClr = false;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.EnableCheat, StringComparison.OrdinalIgnoreCase))
            {
                options.CheatEnabled = true;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.DisableCheat, StringComparison.OrdinalIgnoreCase))
            {
                options.CheatEnabled = false;
            }
        }

        private static string[] ParseStepIds(string value)
        {
            string[] raw = value.Split(',');
            var result = new List<string>(raw.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string item in raw)
            {
                string stepId = item.Trim();
                if (stepId.Length == 0)
                {
                    throw new ArgumentException(
                        $"The {BuildCommandLineOptionNames.Steps} option contains an empty step identifier.");
                }

                if (!seen.Add(stepId))
                {
                    throw new ArgumentException(
                        $"The {BuildCommandLineOptionNames.Steps} option contains duplicate step '{stepId}'.");
                }

                result.Add(stepId);
            }

            return result.ToArray();
        }

        private static void Validate(BuildCommandLineOptions options, HashSet<string> seen)
        {
            if (options.BuildTarget == BuildTarget.NoTarget)
            {
                throw new ArgumentException(
                    $"A valid {BuildCommandLineOptionNames.BuildTarget} option is required.");
            }

            ValidateMutuallyExclusive(
                seen,
                BuildCommandLineOptionNames.UseHybridCLR,
                BuildCommandLineOptionNames.SkipHybridCLR);
            ValidateMutuallyExclusive(
                seen,
                BuildCommandLineOptionNames.EnableCheat,
                BuildCommandLineOptionNames.DisableCheat);
            ValidateMutuallyExclusive(
                seen,
                BuildCommandLineOptionNames.Clean,
                BuildCommandLineOptionNames.Incremental);
            ValidateAssetContentOverride(options, seen);

            if (options.ExportAndroidProject && options.BuildTarget != BuildTarget.Android)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.ExportAndroidProject} is valid only with " +
                    $"{BuildCommandLineOptionNames.BuildTarget} Android.");
            }

        }

        private static void ValidateAssetContentOverride(
            BuildCommandLineOptions options,
            HashSet<string> seen)
        {
            bool providerSpecified = seen.Contains(BuildCommandLineOptionNames.Provider);
            bool configurationSpecified = seen.Contains(BuildCommandLineOptionNames.ProviderConfiguration);
            if (!providerSpecified && configurationSpecified)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.ProviderConfiguration} requires " +
                    $"{BuildCommandLineOptionNames.Provider}.");
            }

            if (!providerSpecified)
            {
                return;
            }

            bool disable = string.Equals(
                options.AssetContentProviderId?.Trim(),
                "none",
                StringComparison.OrdinalIgnoreCase);
            if (disable && configurationSpecified)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.ProviderConfiguration} cannot be used when " +
                    $"{BuildCommandLineOptionNames.Provider} is 'none'.");
            }

            if (!disable && !configurationSpecified)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.Provider} requires " +
                    $"{BuildCommandLineOptionNames.ProviderConfiguration} Assets/<path>/<config>.asset. " +
                    $"Use {BuildCommandLineOptionNames.Provider} none to disable content building " +
                    "for this invocation.");
            }
        }

        private static void ValidateMutuallyExclusive(HashSet<string> seen, string first, string second)
        {
            if (seen.Contains(first) && seen.Contains(second))
            {
                throw new ArgumentException($"Options '{first}' and '{second}' are mutually exclusive.");
            }
        }

        /// <summary>
        /// Returns the Unity Editor 2022.3 native command-line token for a supported target.
        /// </summary>
        public static string GetUnityBuildTargetArgument(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                    return "Win64";
                case BuildTarget.StandaloneOSX:
                    return "OSXUniversal";
                case BuildTarget.StandaloneLinux64:
                    return "Linux64";
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(target),
                        target,
                        "The build target is not supported by this pipeline.");
            }
        }

        internal static bool IsSupportedBuildTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.Android:
                case BuildTarget.iOS:
                case BuildTarget.WebGL:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseSupportedBuildTarget(string value, out BuildTarget target)
        {
            target = BuildTarget.NoTarget;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "win64":
                case "standalonewindows64":
                    target = BuildTarget.StandaloneWindows64;
                    return true;
                case "osxuniversal":
                case "standaloneosx":
                    target = BuildTarget.StandaloneOSX;
                    return true;
                case "linux64":
                case "standalonelinux64":
                    target = BuildTarget.StandaloneLinux64;
                    return true;
                case "android":
                    target = BuildTarget.Android;
                    return true;
                case "ios":
                    target = BuildTarget.iOS;
                    return true;
                case "webgl":
                    target = BuildTarget.WebGL;
                    return true;
                default:
                    return false;
            }
        }

        private static bool LooksLikeBuildPipelineOption(string argument)
        {
            if (string.IsNullOrEmpty(argument) || argument[0] != '-')
            {
                return false;
            }

            return argument.StartsWith(
                BuildCommandLineOptionNames.Prefix,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
