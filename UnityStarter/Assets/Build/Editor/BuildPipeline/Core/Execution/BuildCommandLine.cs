using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Editor
{
    public sealed class BuildCommandLineRecipeInvocation
    {
        internal BuildCommandLineRecipeInvocation(string invocationId, string stepTypeId)
        {
            InvocationId = invocationId;
            StepTypeId = stepTypeId;
        }

        public string InvocationId { get; }
        public string StepTypeId { get; }
    }

    public sealed class BuildCommandLineOptions
    {
        private readonly List<BuildCommandLineRecipeInvocation> recipeInvocations =
            new List<BuildCommandLineRecipeInvocation>();
        private readonly List<string> selectedInvocationIds = new List<string>();
        private readonly Dictionary<string, string> stepConfigurationPaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BuildIncrementality> stepIncrementalities =
            new Dictionary<string, BuildIncrementality>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<BuildInvocationDependency>> stepDependencies =
            new Dictionary<string, List<BuildInvocationDependency>>(StringComparer.OrdinalIgnoreCase);

        public BuildTarget BuildTarget { get; internal set; } = BuildTarget.NoTarget;
        public string BuildProfilePath { get; internal set; }
        public string OutputPath { get; internal set; }
        public string ApplicationVersion { get; internal set; }
        public string OutputBasePath { get; internal set; }
        public string VersionInfoAssetPath { get; internal set; }
        public bool DebugBuild { get; internal set; }
        public bool ExportAndroidProject { get; internal set; }
        public bool AllowExternalOutput { get; internal set; }
        public ScriptingImplementation? ScriptingBackend { get; internal set; }
        public bool? CheatEnabled { get; internal set; }
        public BuildIdentityOverride IdentityOverride { get; internal set; } = BuildIdentityOverride.Empty;
        public IReadOnlyList<BuildCommandLineRecipeInvocation> RecipeInvocations =>
            new ReadOnlyCollection<BuildCommandLineRecipeInvocation>(recipeInvocations);
        public IReadOnlyList<string> SelectedInvocationIds =>
            new ReadOnlyCollection<string>(selectedInvocationIds);
        public IReadOnlyDictionary<string, string> StepConfigurationPathOverrides =>
            new ReadOnlyDictionary<string, string>(stepConfigurationPaths);
        public IReadOnlyDictionary<string, BuildIncrementality> StepIncrementalityOverrides =>
            new ReadOnlyDictionary<string, BuildIncrementality>(stepIncrementalities);
        public IReadOnlyDictionary<string, IReadOnlyList<BuildInvocationDependency>> StepDependencyOverrides
        {
            get
            {
                var snapshot = new Dictionary<string, IReadOnlyList<BuildInvocationDependency>>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, List<BuildInvocationDependency>> entry in stepDependencies)
                {
                    snapshot.Add(
                        entry.Key,
                        new ReadOnlyCollection<BuildInvocationDependency>(entry.Value.ToArray()));
                }

                return new ReadOnlyDictionary<string, IReadOnlyList<BuildInvocationDependency>>(snapshot);
            }
        }
        public bool RecoverOnly { get; internal set; }

        internal List<BuildCommandLineRecipeInvocation> MutableRecipeInvocations => recipeInvocations;
        internal List<string> MutableSelectedInvocationIds => selectedInvocationIds;
        internal Dictionary<string, string> MutableStepConfigurationPaths =>
            stepConfigurationPaths;
        internal Dictionary<string, BuildIncrementality> MutableStepIncrementalities =>
            stepIncrementalities;
        internal Dictionary<string, List<BuildInvocationDependency>> MutableStepDependencies =>
            stepDependencies;
        internal long? IdentityBuildNumber { get; set; }
        internal string IdentitySourceProvider { get; set; }
        internal string IdentitySourceRevision { get; set; }
        internal string IdentitySourceBranch { get; set; }
        internal string IdentityCiProvider { get; set; }
        internal string IdentityCiRunId { get; set; }
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
        public const string BuildNumber = Prefix + "BuildNumber";
        public const string SourceProvider = Prefix + "SourceProvider";
        public const string SourceRevision = Prefix + "SourceRevision";
        public const string SourceBranch = Prefix + "SourceBranch";
        public const string CiProvider = Prefix + "CiProvider";
        public const string CiRunId = Prefix + "CiRunId";
        public const string Recipe = Prefix + "Recipe";
        public const string Selection = Prefix + "Select";
        public const string StepConfiguration = Prefix + "StepConfig";
        public const string StepIncrementality = Prefix + "StepIncrementality";
        public const string StepDependency = Prefix + "StepDependency";
        public const string Development = Prefix + "Development";
        public const string ExportAndroidProject = Prefix + "ExportAndroidProject";
        public const string EnableCheat = Prefix + "EnableCheat";
        public const string DisableCheat = Prefix + "DisableCheat";
        public const string AllowExternalOutput = Prefix + "AllowExternalOutput";
        public const string RecoverOnly = Prefix + "RecoverOnly";
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
            BuildCommandLineOptionNames.BuildNumber,
            BuildCommandLineOptionNames.SourceProvider,
            BuildCommandLineOptionNames.SourceRevision,
            BuildCommandLineOptionNames.SourceBranch,
            BuildCommandLineOptionNames.CiProvider,
            BuildCommandLineOptionNames.CiRunId,
            BuildCommandLineOptionNames.Recipe,
            BuildCommandLineOptionNames.Selection,
            BuildCommandLineOptionNames.StepConfiguration,
            BuildCommandLineOptionNames.StepIncrementality,
            BuildCommandLineOptionNames.StepDependency
        };

        private static readonly HashSet<string> RepeatableValueOptions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                BuildCommandLineOptionNames.Recipe,
                BuildCommandLineOptionNames.Selection,
                BuildCommandLineOptionNames.StepConfiguration,
                BuildCommandLineOptionNames.StepIncrementality,
                BuildCommandLineOptionNames.StepDependency
            };

        private static readonly HashSet<string> FlagOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildCommandLineOptionNames.Development,
            BuildCommandLineOptionNames.ExportAndroidProject,
            BuildCommandLineOptionNames.EnableCheat,
            BuildCommandLineOptionNames.DisableCheat,
            BuildCommandLineOptionNames.AllowExternalOutput,
            BuildCommandLineOptionNames.RecoverOnly
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

                if (!RepeatableValueOptions.Contains(argument) && !seen.Add(argument))
                {
                    throw new ArgumentException($"Build pipeline option '{argument}' was specified more than once.");
                }

                seen.Add(argument);

                string value = null;
                if (ValueOptions.Contains(argument))
                {
                    if (index + 1 >= arguments.Count
                        || string.IsNullOrWhiteSpace(arguments[index + 1])
                        || arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
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
                    || !Enum.IsDefined(typeof(ScriptingImplementation), backend)
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
            else if (argument.Equals(BuildCommandLineOptionNames.BuildNumber, StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long buildNumber))
                {
                    throw new ArgumentException(
                        $"Invalid build number '{value}'. Use a positive integer between 1 and {int.MaxValue}.");
                }

                options.IdentityBuildNumber = buildNumber;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.SourceProvider, StringComparison.OrdinalIgnoreCase))
            {
                options.IdentitySourceProvider = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.SourceRevision, StringComparison.OrdinalIgnoreCase))
            {
                options.IdentitySourceRevision = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.SourceBranch, StringComparison.OrdinalIgnoreCase))
            {
                options.IdentitySourceBranch = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.CiProvider, StringComparison.OrdinalIgnoreCase))
            {
                options.IdentityCiProvider = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.CiRunId, StringComparison.OrdinalIgnoreCase))
            {
                options.IdentityCiRunId = value;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Recipe, StringComparison.OrdinalIgnoreCase))
            {
                AddRecipeInvocation(options, value);
            }
            else if (argument.Equals(BuildCommandLineOptionNames.Selection, StringComparison.OrdinalIgnoreCase))
            {
                AddSelectedInvocation(options, value);
            }
            else if (argument.Equals(BuildCommandLineOptionNames.StepConfiguration, StringComparison.OrdinalIgnoreCase))
            {
                AddStepConfiguration(options, value);
            }
            else if (argument.Equals(BuildCommandLineOptionNames.StepIncrementality, StringComparison.OrdinalIgnoreCase))
            {
                AddStepIncrementality(options, value);
            }
            else if (argument.Equals(BuildCommandLineOptionNames.StepDependency, StringComparison.OrdinalIgnoreCase))
            {
                AddStepDependency(options, value);
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
            else if (argument.Equals(BuildCommandLineOptionNames.EnableCheat, StringComparison.OrdinalIgnoreCase))
            {
                options.CheatEnabled = true;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.DisableCheat, StringComparison.OrdinalIgnoreCase))
            {
                options.CheatEnabled = false;
            }
            else if (argument.Equals(BuildCommandLineOptionNames.RecoverOnly, StringComparison.OrdinalIgnoreCase))
            {
                options.RecoverOnly = true;
            }
        }

        private static void AddRecipeInvocation(BuildCommandLineOptions options, string value)
        {
            ParseAssignment(value, BuildCommandLineOptionNames.Recipe, out string invocationId, out string stepTypeId);
            ValidateRecipeIdentifier(invocationId, "Recipe invocation id");
            ValidateRecipeIdentifier(stepTypeId, "Recipe step type id");

            if (options.MutableRecipeInvocations.Count >= BuildPipelineBudgets.MaximumInvocationCount)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.Recipe} may be specified at most {BuildPipelineBudgets.MaximumInvocationCount} times.");
            }

            for (int index = 0; index < options.MutableRecipeInvocations.Count; index++)
            {
                if (string.Equals(
                        options.MutableRecipeInvocations[index].InvocationId,
                        invocationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Recipe invocation '{invocationId}' was specified more than once.");
                }
            }

            options.MutableRecipeInvocations.Add(
                new BuildCommandLineRecipeInvocation(invocationId, stepTypeId));
        }

        private static void AddSelectedInvocation(
            BuildCommandLineOptions options,
            string value)
        {
            string invocationId = value?.Trim();
            ValidateRecipeIdentifier(invocationId, "Selected invocation id");
            if (options.MutableSelectedInvocationIds.Count
                >= BuildPipelineBudgets.MaximumInvocationCount)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.Selection} may be specified at most " +
                    $"{BuildPipelineBudgets.MaximumInvocationCount} times.");
            }

            for (int index = 0;
                 index < options.MutableSelectedInvocationIds.Count;
                 index++)
            {
                if (string.Equals(
                        options.MutableSelectedInvocationIds[index],
                        invocationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Selected invocation '{invocationId}' was specified more than once.");
                }
            }

            options.MutableSelectedInvocationIds.Add(invocationId);
        }

        private static void AddStepConfiguration(BuildCommandLineOptions options, string value)
        {
            ParseAssignment(
                value,
                BuildCommandLineOptionNames.StepConfiguration,
                out string invocationId,
                out string path);
            ValidateRecipeIdentifier(invocationId, "Step configuration invocation id");
            if (options.MutableStepConfigurationPaths.Count >= BuildPipelineBudgets.MaximumInvocationCount)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.StepConfiguration} may be specified at most {BuildPipelineBudgets.MaximumInvocationCount} times.");
            }

            if (!options.MutableStepConfigurationPaths.TryAdd(invocationId, path))
            {
                throw new ArgumentException(
                    $"A configuration override for invocation '{invocationId}' was specified more than once.");
            }
        }

        private static void AddStepIncrementality(BuildCommandLineOptions options, string value)
        {
            ParseAssignment(
                value,
                BuildCommandLineOptionNames.StepIncrementality,
                out string invocationId,
                out string modeValue);
            ValidateRecipeIdentifier(invocationId, "Step incrementality invocation id");
            if (!TryParseIncrementality(modeValue, out BuildIncrementality incrementality))
            {
                throw new ArgumentException(
                    $"Unsupported step incrementality '{modeValue}'. Use Clean or Incremental.");
            }

            if (!options.MutableStepIncrementalities.TryAdd(invocationId, incrementality))
            {
                throw new ArgumentException(
                    $"An incrementality override for invocation '{invocationId}' was specified more than once.");
            }
        }

        private static void AddStepDependency(BuildCommandLineOptions options, string value)
        {
            ParseAssignment(
                value,
                BuildCommandLineOptionNames.StepDependency,
                out string invocationId,
                out string dependencyExpression);
            ValidateRecipeIdentifier(invocationId, "Step dependency owner invocation id");

            int modeDelimiter = dependencyExpression.IndexOf(':');
            if (modeDelimiter <= 0 || modeDelimiter == dependencyExpression.Length - 1)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.StepDependency} requires " +
                    "'<invocation-id>=<Required|IfSelected>:<dependency-invocation-id>'.");
            }

            string modeValue = dependencyExpression.Substring(0, modeDelimiter).Trim();
            string dependencyId = dependencyExpression.Substring(modeDelimiter + 1).Trim();
            ValidateRecipeIdentifier(dependencyId, "Dependency invocation id");
            if (!TryParseDependencyMode(modeValue, out BuildDependencyMode mode))
            {
                throw new ArgumentException(
                    $"Unsupported dependency mode '{modeValue}'. Use Required or IfSelected.");
            }

            int dependencyCount = options.MutableStepDependencies.Sum(entry => entry.Value.Count);
            if (dependencyCount >= BuildPipelineBudgets.MaximumDependencyEdgeCount)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.StepDependency} may be specified at most {BuildPipelineBudgets.MaximumDependencyEdgeCount} times.");
            }

            if (!options.MutableStepDependencies.TryGetValue(
                    invocationId,
                    out List<BuildInvocationDependency> dependencies))
            {
                dependencies = new List<BuildInvocationDependency>();
                options.MutableStepDependencies.Add(invocationId, dependencies);
            }

            for (int index = 0; index < dependencies.Count; index++)
            {
                if (string.Equals(
                        dependencies[index].InvocationId,
                        dependencyId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Dependency '{dependencyId}' for invocation '{invocationId}' was specified more than once.");
                }
            }

            dependencies.Add(new BuildInvocationDependency(dependencyId, mode));
        }

        private static void ParseAssignment(
            string value,
            string optionName,
            out string key,
            out string assignedValue)
        {
            int delimiter = value?.IndexOf('=') ?? -1;
            if (delimiter <= 0 || delimiter == value.Length - 1)
            {
                throw new ArgumentException(
                    $"{optionName} requires a non-empty '<key>=<value>' assignment.");
            }

            key = value.Substring(0, delimiter).Trim();
            assignedValue = value.Substring(delimiter + 1).Trim();
            if (key.Length == 0 || assignedValue.Length == 0)
            {
                throw new ArgumentException(
                    $"{optionName} requires a non-empty '<key>=<value>' assignment.");
            }
        }

        private static bool TryParseIncrementality(
            string value,
            out BuildIncrementality incrementality)
        {
            if (string.Equals(value, nameof(BuildIncrementality.Clean), StringComparison.OrdinalIgnoreCase))
            {
                incrementality = BuildIncrementality.Clean;
                return true;
            }

            if (string.Equals(value, nameof(BuildIncrementality.Incremental), StringComparison.OrdinalIgnoreCase))
            {
                incrementality = BuildIncrementality.Incremental;
                return true;
            }

            incrementality = default;
            return false;
        }

        private static bool TryParseDependencyMode(
            string value,
            out BuildDependencyMode mode)
        {
            if (string.Equals(value, nameof(BuildDependencyMode.Required), StringComparison.OrdinalIgnoreCase))
            {
                mode = BuildDependencyMode.Required;
                return true;
            }

            if (string.Equals(value, nameof(BuildDependencyMode.IfSelected), StringComparison.OrdinalIgnoreCase))
            {
                mode = BuildDependencyMode.IfSelected;
                return true;
            }

            mode = default;
            return false;
        }

        private static void ValidateRecipeIdentifier(string value, string label)
        {
            BuildIdentityPolicy.ValidateBuildIdentifier(value, label);
        }

        private static void Validate(BuildCommandLineOptions options, HashSet<string> seen)
        {
            if (options.RecoverOnly)
            {
                string incompatible = seen.FirstOrDefault(option =>
                    !option.Equals(BuildCommandLineOptionNames.RecoverOnly, StringComparison.OrdinalIgnoreCase)
                    && !option.Equals(BuildCommandLineOptionNames.BuildTarget, StringComparison.OrdinalIgnoreCase));
                if (incompatible != null)
                {
                    throw new ArgumentException(
                        $"{BuildCommandLineOptionNames.RecoverOnly} cannot be combined with build option '{incompatible}'.");
                }

                return;
            }

            if (options.BuildTarget == BuildTarget.NoTarget)
            {
                throw new ArgumentException(
                    $"A valid {BuildCommandLineOptionNames.BuildTarget} option is required.");
            }

            ValidateMutuallyExclusive(
                seen,
                BuildCommandLineOptionNames.EnableCheat,
                BuildCommandLineOptionNames.DisableCheat);

            if (options.MutableSelectedInvocationIds.Count > 0)
            {
                if (!seen.Contains(BuildCommandLineOptionNames.Profile))
                {
                    throw new ArgumentException(
                        $"{BuildCommandLineOptionNames.Selection} requires an explicit " +
                        $"{BuildCommandLineOptionNames.Profile} so selection always addresses a version-controlled graph.");
                }

                if (options.MutableRecipeInvocations.Count > 0)
                {
                    throw new ArgumentException(
                        $"{BuildCommandLineOptionNames.Selection} cannot be combined with " +
                        $"{BuildCommandLineOptionNames.Recipe}. Select invocations from the profile or replace the recipe, not both.");
                }
            }

            if (options.ExportAndroidProject && options.BuildTarget != BuildTarget.Android)
            {
                throw new ArgumentException(
                    $"{BuildCommandLineOptionNames.ExportAndroidProject} is valid only with " +
                    $"{BuildCommandLineOptionNames.BuildTarget} Android.");
            }

            options.IdentityOverride = new BuildIdentityOverride(
                options.IdentityBuildNumber,
                options.IdentitySourceProvider,
                options.IdentitySourceRevision,
                options.IdentitySourceBranch,
                options.IdentityCiProvider,
                options.IdentityCiRunId);
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
