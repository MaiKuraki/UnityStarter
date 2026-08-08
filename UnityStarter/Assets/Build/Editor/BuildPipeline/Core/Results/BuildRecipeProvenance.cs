using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal sealed class BuildRecipeProvenanceEntry
    {
        public BuildRecipeProvenanceEntry(
            int order,
            string invocationId,
            string stepTypeId,
            BuildIncrementality incrementality,
            IReadOnlyList<BuildInvocationDependency> dependencies,
            bool hasConfiguration,
            string configurationAssetPath,
            string configurationAssetGuid,
            string configurationLocalFileId,
            string configurationType,
            string configurationAssetSha256,
            string configurationDependencyHash,
            int configurationDependencyCount,
            string validationError)
        {
            Order = order;
            InvocationId = invocationId ?? string.Empty;
            StepTypeId = stepTypeId ?? string.Empty;
            Incrementality = incrementality;
            Dependencies = SnapshotDependencies(dependencies);
            HasConfiguration = hasConfiguration;
            ConfigurationAssetPath = configurationAssetPath ?? string.Empty;
            ConfigurationAssetGuid = configurationAssetGuid ?? string.Empty;
            ConfigurationLocalFileId = configurationLocalFileId ?? string.Empty;
            ConfigurationType = configurationType ?? string.Empty;
            ConfigurationAssetSha256 = configurationAssetSha256 ?? string.Empty;
            ConfigurationDependencyHash = configurationDependencyHash ?? string.Empty;
            ConfigurationDependencyCount = configurationDependencyCount;
            ValidationError = validationError ?? string.Empty;
        }

        public int Order { get; }
        public string InvocationId { get; }
        public string StepTypeId { get; }
        public BuildIncrementality Incrementality { get; }
        public IReadOnlyList<BuildInvocationDependency> Dependencies { get; }
        public bool HasConfiguration { get; }
        public string ConfigurationAssetPath { get; }
        public string ConfigurationAssetGuid { get; }
        public string ConfigurationLocalFileId { get; }
        public string ConfigurationType { get; }
        public string ConfigurationAssetSha256 { get; }
        public string ConfigurationDependencyHash { get; }
        public int ConfigurationDependencyCount { get; }
        public string ValidationError { get; }

        private static IReadOnlyList<BuildInvocationDependency> SnapshotDependencies(
            IReadOnlyList<BuildInvocationDependency> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return Array.Empty<BuildInvocationDependency>();
            }

            var snapshot = new BuildInvocationDependency[dependencies.Count];
            for (int index = 0; index < dependencies.Count; index++)
            {
                snapshot[index] = dependencies[index]?.Snapshot()
                    ?? throw new ArgumentException(
                        $"Build provenance dependency at index {index} is null.",
                        nameof(dependencies));
            }

            return new ReadOnlyCollection<BuildInvocationDependency>(snapshot);
        }
    }

    internal sealed class BuildRecipeProvenanceCapture
    {
        private const long MaximumConfigurationAssetBytes = 64L * 1024L * 1024L;
        private const long MaximumDependencyAssetBytes = 64L * 1024L * 1024L;
        private const long MaximumDependencyAssetBytesTotal = 256L * 1024L * 1024L;
        private const int MaximumDependencyCount = 4096;
        private const int MaximumDependencyObjectCount = 16384;
        private readonly IReadOnlyList<string> validationErrors;

        private BuildRecipeProvenanceCapture(
            IReadOnlyList<BuildRecipeProvenanceEntry> entries,
            IReadOnlyList<string> validationErrors)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            this.validationErrors = validationErrors
                ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        public IReadOnlyList<BuildRecipeProvenanceEntry> Entries { get; }

        public static BuildRecipeProvenanceCapture Capture(BuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var entries = new BuildRecipeProvenanceEntry[request.Steps.Count];
            var errors = new List<string>();
            var configurationCache = new Dictionary<string, ConfigurationProvenance>(
                StringComparer.Ordinal);
            for (int index = 0; index < request.Steps.Count; index++)
            {
                BuildStepInvocation invocation = request.Steps[index];
                try
                {
                    entries[index] = CaptureEntry(
                        request,
                        invocation,
                        index,
                        configurationCache);
                }
                catch (Exception exception) when (IsProvenanceFailure(exception))
                {
                    string error =
                        $"Build invocation '{invocation.InvocationId}' ({invocation.StepTypeId}) " +
                        "configuration provenance is invalid: " +
                        exception.Message;
                    errors.Add(error);
                    entries[index] = CreateInvalidEntry(invocation, index, error);
                }
            }

            return new BuildRecipeProvenanceCapture(
                Array.AsReadOnly(entries),
                errors.AsReadOnly());
        }

        public void ThrowIfInvalid()
        {
            if (validationErrors.Count > 0)
            {
                throw new BuildFailedException(
                    "Build recipe provenance validation failed:\n" +
                    string.Join("\n", validationErrors));
            }
        }

        public void ValidateUnchanged(BuildRequest request, string checkpoint)
        {
            ValidateCheckpoint(checkpoint);
            BuildRecipeProvenanceCapture current = Capture(request);
            if (current.validationErrors.Count > 0)
            {
                ThrowChanged(
                    checkpoint,
                    string.Join(" ", current.validationErrors));
            }

            string difference = FindDifference(Entries, current.Entries);
            if (!string.IsNullOrEmpty(difference))
            {
                ThrowChanged(checkpoint, difference);
            }
        }

        public void ValidateUnchanged(
            BuildRequest request,
            BuildStepInvocation invocation,
            string checkpoint)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            ValidateCheckpoint(checkpoint);
            int order = FindInvocationOrder(request, invocation);
            BuildRecipeProvenanceEntry current;
            try
            {
                current = CaptureEntry(
                    request,
                    invocation,
                    order,
                    new Dictionary<string, ConfigurationProvenance>(
                        StringComparer.Ordinal));
            }
            catch (Exception exception) when (IsProvenanceFailure(exception))
            {
                ThrowChanged(
                    checkpoint,
                    $"Build invocation '{invocation.InvocationId}' ({invocation.StepTypeId}) " +
                    "configuration provenance is no longer valid: " +
                    exception.Message);
                return;
            }

            string difference = FindEntryDifference(Entries[order], current);
            if (!string.IsNullOrEmpty(difference))
            {
                ThrowChanged(checkpoint, difference);
            }
        }

        private static int FindInvocationOrder(
            BuildRequest request,
            BuildStepInvocation invocation)
        {
            for (int index = 0; index < request.Steps.Count; index++)
            {
                if (ReferenceEquals(request.Steps[index], invocation))
                {
                    return index;
                }
            }

            throw new ArgumentException(
                $"Build invocation '{invocation.InvocationId}' does not belong to the request being validated.",
                nameof(invocation));
        }

        private static string FindDifference(
            IReadOnlyList<BuildRecipeProvenanceEntry> expected,
            IReadOnlyList<BuildRecipeProvenanceEntry> current)
        {
            if (expected.Count != current.Count)
            {
                return
                    $"The selected recipe entry count changed from {expected.Count} to {current.Count}.";
            }

            for (int index = 0; index < expected.Count; index++)
            {
                string difference = FindEntryDifference(expected[index], current[index]);
                if (!string.IsNullOrEmpty(difference))
                {
                    return difference;
                }
            }

            return string.Empty;
        }

        private static string FindEntryDifference(
            BuildRecipeProvenanceEntry expected,
            BuildRecipeProvenanceEntry current)
        {
            string invocation =
                $"Build invocation '{expected.InvocationId}' ({expected.StepTypeId})";
            if (expected.Order != current.Order)
            {
                return $"{invocation} changed recipe order.";
            }

            if (!string.Equals(
                    expected.InvocationId,
                    current.InvocationId,
                    StringComparison.Ordinal))
            {
                return
                    $"Recipe entry {expected.Order} changed invocation identity from " +
                    $"'{expected.InvocationId}' to '{current.InvocationId}'.";
            }

            if (!string.Equals(
                    expected.StepTypeId,
                    current.StepTypeId,
                    StringComparison.Ordinal))
            {
                return $"{invocation} changed step type identity.";
            }

            if (expected.Incrementality != current.Incrementality)
            {
                return $"{invocation} changed incrementality.";
            }

            string dependencyDifference = FindDependencyDifference(
                expected.Dependencies,
                current.Dependencies);
            if (!string.IsNullOrEmpty(dependencyDifference))
            {
                return $"{invocation} {dependencyDifference}";
            }

            if (expected.HasConfiguration != current.HasConfiguration)
            {
                return $"{invocation} changed its configuration assignment.";
            }

            if (!string.Equals(
                    expected.ConfigurationAssetPath,
                    current.ConfigurationAssetPath,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expected.ConfigurationAssetGuid,
                    current.ConfigurationAssetGuid,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expected.ConfigurationLocalFileId,
                    current.ConfigurationLocalFileId,
                    StringComparison.Ordinal))
            {
                return $"{invocation} changed configuration asset identity.";
            }

            if (!string.Equals(
                    expected.ConfigurationType,
                    current.ConfigurationType,
                    StringComparison.Ordinal))
            {
                return $"{invocation} changed configuration type.";
            }

            if (!string.Equals(
                    expected.ConfigurationAssetSha256,
                    current.ConfigurationAssetSha256,
                    StringComparison.Ordinal))
            {
                return $"{invocation} configuration asset content changed.";
            }

            if (!string.Equals(
                    expected.ConfigurationDependencyHash,
                    current.ConfigurationDependencyHash,
                    StringComparison.Ordinal)
                || expected.ConfigurationDependencyCount
                != current.ConfigurationDependencyCount)
            {
                return $"{invocation} configuration dependency graph changed.";
            }

            if (!string.Equals(
                    expected.ValidationError,
                    current.ValidationError,
                    StringComparison.Ordinal))
            {
                return $"{invocation} configuration validation state changed.";
            }

            return string.Empty;
        }

        private static string FindDependencyDifference(
            IReadOnlyList<BuildInvocationDependency> expected,
            IReadOnlyList<BuildInvocationDependency> current)
        {
            if (expected.Count != current.Count)
            {
                return "changed its dependency count.";
            }

            for (int index = 0; index < expected.Count; index++)
            {
                BuildInvocationDependency expectedDependency = expected[index];
                BuildInvocationDependency currentDependency = current[index];
                if (!string.Equals(
                        expectedDependency.InvocationId,
                        currentDependency.InvocationId,
                        StringComparison.Ordinal)
                    || expectedDependency.Mode != currentDependency.Mode)
                {
                    return $"changed dependency {index}.";
                }
            }

            return string.Empty;
        }

        private static void ValidateCheckpoint(string checkpoint)
        {
            if (string.IsNullOrWhiteSpace(checkpoint))
            {
                throw new ArgumentException(
                    "A recipe provenance validation checkpoint is required.",
                    nameof(checkpoint));
            }
        }

        private static void ThrowChanged(string checkpoint, string detail)
        {
            throw new BuildFailedException(
                $"Build recipe provenance changed after preflight at '{checkpoint}'. " +
                detail +
                " Terminal outputs will not be published.");
        }

        private static bool IsProvenanceFailure(Exception exception)
        {
            return exception is ArgumentException
                   || exception is InvalidOperationException
                   || exception is IOException
                   || exception is UnauthorizedAccessException
                   || exception is CryptographicException;
        }

        private static BuildRecipeProvenanceEntry CaptureEntry(
            BuildRequest request,
            BuildStepInvocation invocation,
            int order,
            IDictionary<string, ConfigurationProvenance> configurationCache)
        {
            ScriptableObject configuration = invocation.Configuration;
            if (configuration == null)
            {
                return new BuildRecipeProvenanceEntry(
                    order,
                    invocation.InvocationId,
                    invocation.StepTypeId,
                    invocation.Incrementality,
                    invocation.Dependencies,
                    hasConfiguration: false,
                    configurationAssetPath: string.Empty,
                    configurationAssetGuid: string.Empty,
                    configurationLocalFileId: string.Empty,
                    configurationType: string.Empty,
                    configurationAssetSha256: string.Empty,
                    configurationDependencyHash: string.Empty,
                    configurationDependencyCount: 0,
                    validationError: string.Empty);
            }

            string typeName = GetStableTypeName(configuration.GetType());
            if (!EditorUtility.IsPersistent(configuration))
            {
                throw new InvalidOperationException(
                    $"The assigned {typeName} object is not a persistent Unity asset.");
            }

            if (EditorUtility.IsDirty(configuration))
            {
                throw new InvalidOperationException(
                    "The configuration asset has unsaved changes. Save it explicitly before building; " +
                    "the build pipeline never saves authoring assets implicitly.");
            }

            string assetPath = AssetDatabase.GetAssetPath(configuration)
                ?.Trim()
                .Replace('\\', '/');
            ValidateAssetPath(assetPath, invocation.InvocationId);

            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(
                assetPath);
            if (mainAsset != configuration)
            {
                throw new InvalidOperationException(
                    $"Build invocation '{invocation.InvocationId}' configuration must be the main asset at " +
                    $"'{assetPath}'; sub-assets are not valid recipe configuration identities.");
            }

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    configuration,
                    out string guid,
                    out long localFileId)
                || string.IsNullOrWhiteSpace(guid))
            {
                throw new InvalidOperationException(
                    $"Unity could not resolve a GUID and local file id for '{assetPath}'.");
            }

            string guidPath = AssetDatabase.GUIDToAssetPath(guid)
                ?.Replace('\\', '/');
            if (!string.Equals(assetPath, guidPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"GUID '{guid}' resolves to '{guidPath}', not '{assetPath}'.");
            }

            string cacheKey = guid + ":" + localFileId.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (!configurationCache.TryGetValue(
                    cacheKey,
                    out ConfigurationProvenance provenance))
            {
                string absoluteAssetPath = BuildPathPolicy.EnsureSafeReadableFile(
                    request.ProjectRoot,
                    Path.Combine(request.ProjectRoot, assetPath));
                var fileInfo = new FileInfo(absoluteAssetPath);
                if (fileInfo.Length > MaximumConfigurationAssetBytes)
                {
                    throw new IOException(
                        $"Configuration asset exceeds the {MaximumConfigurationAssetBytes}-byte provenance hash budget: " +
                        $"'{assetPath}'.");
                }

                string[] dependencies = NormalizeDependencyPaths(
                    AssetDatabase.GetDependencies(
                        assetPath,
                        recursive: true));
                if (dependencies.Length > MaximumDependencyCount)
                {
                    throw new InvalidOperationException(
                        $"Configuration asset dependency graph exceeds the {MaximumDependencyCount}-asset " +
                        $"provenance budget: '{assetPath}'.");
                }

                EnsureDependenciesAreSaved(assetPath, dependencies);
                string assetSha256 = ComputeSha256(absoluteAssetPath);
                string dependencyHash = ComputeDependencyGraphHash(
                    request.ProjectRoot,
                    dependencies);
                if (string.IsNullOrWhiteSpace(dependencyHash))
                {
                    throw new InvalidOperationException(
                        $"Unity returned an empty dependency hash for '{assetPath}'.");
                }

                provenance = new ConfigurationProvenance(
                    assetSha256,
                    dependencyHash,
                    dependencies.Length);
                configurationCache.Add(cacheKey, provenance);
            }

            return new BuildRecipeProvenanceEntry(
                order,
                invocation.InvocationId,
                invocation.StepTypeId,
                invocation.Incrementality,
                invocation.Dependencies,
                hasConfiguration: true,
                configurationAssetPath: assetPath,
                configurationAssetGuid: guid,
                configurationLocalFileId: localFileId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                configurationType: typeName,
                configurationAssetSha256: provenance.AssetSha256,
                configurationDependencyHash: provenance.DependencyHash,
                configurationDependencyCount: provenance.DependencyCount,
                validationError: string.Empty);
        }

        private static string[] NormalizeDependencyPaths(
            IReadOnlyList<string> dependencyPaths)
        {
            if (dependencyPaths == null || dependencyPaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            var normalized = new SortedSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < dependencyPaths.Count; index++)
            {
                string dependencyPath = dependencyPaths[index]
                    ?.Trim()
                    .Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(dependencyPath))
                {
                    normalized.Add(dependencyPath);
                }
            }

            var result = new string[normalized.Count];
            normalized.CopyTo(result);
            return result;
        }

        private static string ComputeDependencyGraphHash(
            string projectRoot,
            IReadOnlyList<string> dependencyPaths)
        {
            var builder = new StringBuilder(dependencyPaths.Count * 96);
            long hashedAssetBytes = 0;
            for (int index = 0; index < dependencyPaths.Count; index++)
            {
                string dependencyPath = dependencyPaths[index];
                string guid = AssetDatabase.AssetPathToGUID(dependencyPath) ?? string.Empty;
                string dependencyHash = AssetDatabase.GetAssetDependencyHash(
                        dependencyPath)
                    .ToString();
                string assetSha256 = string.Empty;
                long assetBytes = 0;
                if (dependencyPath.StartsWith("Assets/", StringComparison.Ordinal)
                    && dependencyPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    string absolutePath = BuildPathPolicy.EnsureSafeReadableFile(
                        projectRoot,
                        Path.Combine(projectRoot, dependencyPath));
                    assetBytes = new FileInfo(absolutePath).Length;
                    if (assetBytes > MaximumDependencyAssetBytes)
                    {
                        throw new IOException(
                            $"Dependency asset exceeds the {MaximumDependencyAssetBytes}-byte provenance hash budget: " +
                            $"'{dependencyPath}'.");
                    }

                    if (hashedAssetBytes > MaximumDependencyAssetBytesTotal - assetBytes)
                    {
                        throw new IOException(
                            "Configuration dependency assets exceed the " +
                            $"{MaximumDependencyAssetBytesTotal}-byte aggregate provenance hash budget.");
                    }

                    hashedAssetBytes += assetBytes;
                    assetSha256 = ComputeSha256(absolutePath);
                }

                AppendHashField(builder, dependencyPath);
                AppendHashField(builder, guid);
                AppendHashField(builder, dependencyHash);
                AppendHashField(
                    builder,
                    assetBytes.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                AppendHashField(builder, assetSha256);
            }

            return ComputeTextSha256(builder.ToString());
        }

        private static void AppendHashField(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append('\n');
        }

        private static BuildRecipeProvenanceEntry CreateInvalidEntry(
            BuildStepInvocation invocation,
            int order,
            string validationError)
        {
            ScriptableObject configuration = invocation.Configuration;
            string assetPath = configuration == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(configuration)?.Replace('\\', '/')
                  ?? string.Empty;
            return new BuildRecipeProvenanceEntry(
                order,
                invocation.InvocationId,
                invocation.StepTypeId,
                invocation.Incrementality,
                invocation.Dependencies,
                configuration != null,
                assetPath,
                string.Empty,
                string.Empty,
                configuration == null
                    ? string.Empty
                    : GetStableTypeName(configuration.GetType()),
                string.Empty,
                string.Empty,
                0,
                validationError);
        }

        private static void ValidateAssetPath(string assetPath, string invocationId)
        {
            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Build invocation '{invocationId}' configuration must be a project-relative .asset file below Assets; " +
                    $"resolved path was '{assetPath}'.");
            }

            BuildPathPolicy.ValidatePortableProjectRelativePath(
                assetPath,
                $"Build invocation '{invocationId}' configuration asset");
        }

        private static void EnsureDependenciesAreSaved(
            string configurationAssetPath,
            IReadOnlyList<string> dependencyPaths)
        {
            int objectCount = 0;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < dependencyPaths.Count; index++)
            {
                string dependencyPath = dependencyPaths[index]
                    ?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(dependencyPath)
                    || !visited.Add(dependencyPath))
                {
                    continue;
                }

                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                    dependencyPath);
                objectCount += assets?.Length ?? 0;
                if (objectCount > MaximumDependencyObjectCount)
                {
                    throw new InvalidOperationException(
                        $"Configuration asset dependency graph exceeds the " +
                        $"{MaximumDependencyObjectCount}-object provenance budget: " +
                        $"'{configurationAssetPath}'.");
                }

                if (assets == null)
                {
                    continue;
                }

                for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    UnityEngine.Object asset = assets[assetIndex];
                    if (asset != null && EditorUtility.IsDirty(asset))
                    {
                        throw new InvalidOperationException(
                            $"Dependency '{dependencyPath}' has unsaved changes. Save it explicitly " +
                            "before building so the recorded dependency hash matches the build inputs.");
                    }
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       8192,
                       FileOptions.SequentialScan))
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string ComputeTextSha256(string content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string GetStableTypeName(Type type)
        {
            string typeName = type.FullName ?? type.Name;
            AssemblyName assemblyName = type.Assembly.GetName();
            return typeName + ", " + assemblyName.Name;
        }

        private sealed class ConfigurationProvenance
        {
            internal ConfigurationProvenance(
                string assetSha256,
                string dependencyHash,
                int dependencyCount)
            {
                AssetSha256 = assetSha256;
                DependencyHash = dependencyHash;
                DependencyCount = dependencyCount;
            }

            internal string AssetSha256 { get; }
            internal string DependencyHash { get; }
            internal int DependencyCount { get; }
        }
    }
}
