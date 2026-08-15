using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal sealed class HybridCLRReleaseBaselineExpectation
    {
        internal HybridCLRReleaseBaselineExpectation(
            string projectRoot,
            string buildRoot,
            string finalDirectory,
            string releaseKey,
            string applicationIdentifier,
            string applicationVersion,
            string hotUpdateInvocationId,
            string buildTarget,
            string namedBuildTarget,
            string scriptingBackend,
            string unityVersion,
            string hybridCLRPackageIdentity,
            string authoringConfigurationHash,
            string hybridCLRSettingsHash,
            string playerConfigurationHash,
            string compatibilityHash,
            IReadOnlyList<string> hotUpdateAssemblies)
        {
            ProjectRoot = Path.GetFullPath(projectRoot);
            BuildRoot = Path.GetFullPath(buildRoot);
            FinalDirectory = Path.GetFullPath(finalDirectory);
            ReleaseKey = RequireValue(releaseKey, nameof(releaseKey));
            ApplicationIdentifier = RequireValue(
                applicationIdentifier,
                nameof(applicationIdentifier));
            ApplicationVersion = RequireValue(applicationVersion, nameof(applicationVersion));
            HotUpdateInvocationId = RequireValue(
                hotUpdateInvocationId,
                nameof(hotUpdateInvocationId));
            BuildTarget = RequireValue(buildTarget, nameof(buildTarget));
            NamedBuildTarget = RequireValue(namedBuildTarget, nameof(namedBuildTarget));
            ScriptingBackend = RequireValue(scriptingBackend, nameof(scriptingBackend));
            UnityVersion = RequireValue(unityVersion, nameof(unityVersion));
            HybridCLRPackageIdentity = RequireValue(
                hybridCLRPackageIdentity,
                nameof(hybridCLRPackageIdentity));
            AuthoringConfigurationHash = HybridCLRReleaseBaselineStore.RequireSha256(
                authoringConfigurationHash,
                nameof(authoringConfigurationHash));
            HybridCLRSettingsHash = HybridCLRReleaseBaselineStore.RequireSha256(
                hybridCLRSettingsHash,
                nameof(hybridCLRSettingsHash));
            PlayerConfigurationHash = HybridCLRReleaseBaselineStore.RequireSha256(
                playerConfigurationHash,
                nameof(playerConfigurationHash));
            CompatibilityHash = HybridCLRReleaseBaselineStore.RequireSha256(
                compatibilityHash,
                nameof(compatibilityHash));
            HotUpdateAssemblies = (hotUpdateAssemblies
                    ?? throw new ArgumentNullException(nameof(hotUpdateAssemblies)))
                .ToArray();
            if (HotUpdateAssemblies.Count == 0)
            {
                throw new ArgumentException(
                    "At least one hot-update assembly is required for a release baseline.",
                    nameof(hotUpdateAssemblies));
            }
        }

        internal string ProjectRoot { get; }
        internal string BuildRoot { get; }
        internal string FinalDirectory { get; }
        internal string ReleaseKey { get; }
        internal string ApplicationIdentifier { get; }
        internal string ApplicationVersion { get; }
        internal string HotUpdateInvocationId { get; }
        internal string BuildTarget { get; }
        internal string NamedBuildTarget { get; }
        internal string ScriptingBackend { get; }
        internal string UnityVersion { get; }
        internal string HybridCLRPackageIdentity { get; }
        internal string AuthoringConfigurationHash { get; }
        internal string HybridCLRSettingsHash { get; }
        internal string PlayerConfigurationHash { get; }
        internal string CompatibilityHash { get; }
        internal IReadOnlyList<string> HotUpdateAssemblies { get; }

        private static string RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "HybridCLR release-baseline identity values are required.",
                    parameterName);
            }

            return value.Trim();
        }
    }

    internal sealed class HybridCLRReleaseBaseline
    {
        internal HybridCLRReleaseBaseline(
            string directory,
            string aotDirectory,
            string manifestHash)
        {
            Directory = directory;
            AOTDirectory = aotDirectory;
            ManifestHash = manifestHash;
        }

        internal string Directory { get; }
        internal string AOTDirectory { get; }
        internal string ManifestHash { get; }
    }

    internal static class HybridCLRReleaseBaselineEligibility
    {
        internal static bool TryGetExplicitReleasePlayerConsumer(
            BuildExecutionContext context,
            BuildStepInvocation hotUpdateInvocation,
            out string playerInvocationId,
            out string reason)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (hotUpdateInvocation == null)
            {
                throw new ArgumentNullException(nameof(hotUpdateInvocation));
            }

            playerInvocationId = string.Empty;
            if (!context.Request.CanPublishReleaseBaseline)
            {
                reason = "Only qualified Release builds publish a HybridCLR release baseline. " +
                         "Development and Local Release Preview builds never publish one.";
                return false;
            }

            for (int planIndex = 0; planIndex < context.Plan.Count; planIndex++)
            {
                CompiledBuildStep candidate = context.Plan[planIndex];
                if (!candidate.IsApplicable
                    || !string.Equals(
                        candidate.Invocation.StepTypeId,
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int dependencyIndex = 0;
                     dependencyIndex < candidate.Invocation.Dependencies.Count;
                     dependencyIndex++)
                {
                    BuildInvocationDependency dependency =
                        candidate.Invocation.Dependencies[dependencyIndex];
                    if (dependency != null
                        && string.Equals(
                            dependency.InvocationId,
                            hotUpdateInvocation.InvocationId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(playerInvocationId))
                        {
                            throw new InvalidOperationException(
                                $"More than one Player invocation directly consumes hot-update invocation " +
                                $"'{hotUpdateInvocation.InvocationId}'. A release baseline requires one unambiguous Player owner.");
                        }

                        playerInvocationId = candidate.Invocation.InvocationId;
                    }
                }
            }

            if (string.IsNullOrEmpty(playerInvocationId))
            {
                reason =
                    "No selected Player invocation directly depends on this hot-update invocation. " +
                    "Transitive consumption and hot-update-only recipes cannot publish a release baseline.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }

    internal static class HybridCLRReleaseBaselineStore
    {
        [Serializable]
        internal sealed class Manifest
        {
            public string documentType;
            public string releaseKey;
            public string applicationIdentifier;
            public string applicationVersion;
            public string hotUpdateInvocationId;
            public string playerInvocationId;
            public string buildTarget;
            public string namedBuildTarget;
            public string scriptingBackend;
            public string buildConfiguration;
            public string unityVersion;
            public string hybridCLRPackageIdentity;
            public string authoringConfigurationHash;
            public string hybridCLRSettingsHash;
            public string playerConfigurationHash;
            public string compatibilityHash;
            public string[] hotUpdateAssemblies;
            public AOTAssembly[] aotAssemblies;
            public string createdUtc;
            public long sourceBuildNumber;
            public string sourceProvider;
            public string sourceRevision;
            public string sourceBranch;
            public string manifestChecksum;
        }

        [Serializable]
        internal sealed class AOTAssembly
        {
            public string fileName;
            public long byteLength;
            public string sha256;
        }

        internal const string DocumentType = "hybridclr-release-baseline";
        internal const string ManifestFileName = "baseline.json";
        internal const string AOTDirectoryName = "AOT";
        internal const string BaselineRootRelativePath =
            ".buildpipeline/baselines/hybridclr";
        internal const int MaximumAOTAssemblyCount = 4096;
        internal const long MaximumManifestBytes = 4L * 1024L * 1024L;
        internal const long MaximumAOTAssemblyBytes = 1024L * 1024L * 1024L;
        internal const long MaximumTotalAOTBytes = 16L * 1024L * 1024L * 1024L;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static HybridCLRReleaseBaselineExpectation CreateExpectation(
            BuildExecutionContext context,
            BuildStepInvocation invocation,
            HybridCLRBuildConfig configuration)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (context.Version == null)
            {
                throw new InvalidOperationException(
                    "Build version identity must be resolved before validating a HybridCLR release baseline.");
            }

            BuildRequest request = context.Request;
            if (!request.CanPublishReleaseBaseline)
            {
                throw new InvalidOperationException(
                    "Incremental HybridCLR builds require a qualified Release request. " +
                    "Development and Local Release Preview builds cannot consume or publish a release baseline.");
            }

            string projectRoot = Path.GetFullPath(request.ProjectRoot);
            string buildRoot = BuildPathPolicy.EnsureSafeBuildRoot(
                projectRoot,
                request.BuildRoot);
            string[] assemblyNames = configuration.GetHotUpdateAssemblyNames()
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (assemblyNames.Length == 0)
            {
                throw new InvalidOperationException(
                    "HybridCLR release-baseline compatibility requires at least one configured hot-update assembly.");
            }

            string authoringHash = ComputeAuthoringConfigurationHash(
                projectRoot,
                configuration,
                assemblyNames);
            string hybridCLRSettingsHash = ComputeHybridCLRSettingsHash();
            string packageIdentity = GetHybridCLRPackageIdentity();
            string playerHash = ComputePlayerConfigurationHash(request);
            string compatibilityHash = ComputeTextSha256(
                string.Join("\n", new[]
                {
                    "hybridclr-release-compatibility",
                    request.Target.ToString(),
                    request.NamedTarget.ToString(),
                    request.ScriptingBackend.ToString(),
                    Application.unityVersion,
                    packageIdentity,
                    authoringHash,
                    hybridCLRSettingsHash,
                    playerHash
                }));

            string releaseKey = ComputeTextSha256(
                string.Join("\n", new[]
                {
                    "hybridclr-release-key",
                    request.ApplicationIdentifier,
                    context.Version.ApplicationVersion,
                    invocation.InvocationId
                }));
            string finalDirectory = Path.Combine(
                buildRoot,
                BaselineRootRelativePath.Replace('/', Path.DirectorySeparatorChar),
                request.Target.ToString(),
                request.ScriptingBackend.ToString(),
                releaseKey);
            finalDirectory = ValidateBaselineDirectory(
                projectRoot,
                buildRoot,
                finalDirectory,
                "HybridCLR release baseline");

            return new HybridCLRReleaseBaselineExpectation(
                projectRoot,
                buildRoot,
                finalDirectory,
                releaseKey,
                request.ApplicationIdentifier,
                context.Version.ApplicationVersion,
                invocation.InvocationId,
                request.Target.ToString(),
                request.NamedTarget.ToString(),
                request.ScriptingBackend.ToString(),
                Application.unityVersion,
                packageIdentity,
                authoringHash,
                hybridCLRSettingsHash,
                playerHash,
                compatibilityHash,
                assemblyNames);
        }

        internal static HybridCLRReleaseBaseline ValidateAndResolve(
            HybridCLRReleaseBaselineExpectation expectation)
        {
            if (expectation == null)
            {
                throw new ArgumentNullException(nameof(expectation));
            }

            return ValidateDirectory(
                expectation.FinalDirectory,
                expectation,
                requireCompatibilityMatch: true);
        }

        internal static HybridCLRReleaseBaseline ValidateForReplacement(
            HybridCLRReleaseBaselineExpectation expectation)
        {
            if (expectation == null)
            {
                throw new ArgumentNullException(nameof(expectation));
            }

            if (!Directory.Exists(expectation.FinalDirectory))
            {
                return null;
            }

            return ValidateDirectory(
                expectation.FinalDirectory,
                expectation,
                requireCompatibilityMatch: false);
        }

        internal static HybridCLRReleaseBaseline ValidateStagedDirectory(
            string directory,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "HybridCLR staged release-baseline directory is required.",
                    nameof(directory));
            }

            if (expectation == null)
            {
                throw new ArgumentNullException(nameof(expectation));
            }

            return ValidateDirectoryContents(
                Path.GetFullPath(directory),
                expectation,
                requireCompatibilityMatch: true);
        }

        internal static HybridCLRReleaseBaseline ValidateReplacementDirectory(
            string directory,
            HybridCLRReleaseBaselineExpectation expectation)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "HybridCLR replacement release-baseline directory is required.",
                    nameof(directory));
            }

            if (expectation == null)
            {
                throw new ArgumentNullException(nameof(expectation));
            }

            return ValidateDirectoryContents(
                Path.GetFullPath(directory),
                expectation,
                requireCompatibilityMatch: false);
        }

        internal static Manifest CreateManifest(
            HybridCLRReleaseBaselineExpectation expectation,
            string playerInvocationId,
            IReadOnlyList<AOTAssembly> aotAssemblies,
            BuildVersionContext sourceVersion)
        {
            if (expectation == null)
            {
                throw new ArgumentNullException(nameof(expectation));
            }

            if (string.IsNullOrWhiteSpace(playerInvocationId))
            {
                throw new ArgumentException(
                    "A release baseline requires the explicit Player consumer invocation id.",
                    nameof(playerInvocationId));
            }

            if (sourceVersion == null)
            {
                throw new ArgumentNullException(nameof(sourceVersion));
            }

            var manifest = new Manifest
            {
                documentType = DocumentType,
                releaseKey = expectation.ReleaseKey,
                applicationIdentifier = expectation.ApplicationIdentifier,
                applicationVersion = expectation.ApplicationVersion,
                hotUpdateInvocationId = expectation.HotUpdateInvocationId,
                playerInvocationId = playerInvocationId.Trim(),
                buildTarget = expectation.BuildTarget,
                namedBuildTarget = expectation.NamedBuildTarget,
                scriptingBackend = expectation.ScriptingBackend,
                buildConfiguration = "Release",
                unityVersion = expectation.UnityVersion,
                hybridCLRPackageIdentity = expectation.HybridCLRPackageIdentity,
                authoringConfigurationHash = expectation.AuthoringConfigurationHash,
                hybridCLRSettingsHash = expectation.HybridCLRSettingsHash,
                playerConfigurationHash = expectation.PlayerConfigurationHash,
                compatibilityHash = expectation.CompatibilityHash,
                hotUpdateAssemblies = expectation.HotUpdateAssemblies.ToArray(),
                aotAssemblies = (aotAssemblies
                        ?? throw new ArgumentNullException(nameof(aotAssemblies)))
                    .ToArray(),
                createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sourceBuildNumber = sourceVersion.BuildNumber,
                sourceProvider = sourceVersion.ProviderId,
                sourceRevision = sourceVersion.CommitHash,
                sourceBranch = sourceVersion.Branch,
                manifestChecksum = string.Empty
            };
            manifest.manifestChecksum = ComputeManifestChecksum(manifest);
            return manifest;
        }

        internal static IReadOnlyList<AOTAssembly> CaptureAOTAssemblies(
            string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new ArgumentException(
                    "HybridCLR stripped-AOT source directory is required.",
                    nameof(sourceDirectory));
            }

            string sourceRoot = Path.GetFullPath(sourceDirectory);
            if (!Directory.Exists(sourceRoot))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR stripped-AOT source directory was not found: '{sourceRoot}'.");
            }

            RejectReparsePoint(sourceRoot, "HybridCLR stripped-AOT source directory");
            string[] files = Directory.GetFiles(sourceRoot, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0 || files.Length > MaximumAOTAssemblyCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR stripped-AOT input must contain between 1 and {MaximumAOTAssemblyCount} DLL files.");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<AOTAssembly>(files.Length);
            long totalBytes = 0;
            foreach (string file in files)
            {
                string readable = BuildPathPolicy.EnsureSafeReadableFile(sourceRoot, file);
                RejectReparsePoint(readable, "HybridCLR stripped-AOT assembly");
                string fileName = Path.GetFileName(readable);
                ValidateAOTFileName(fileName);
                if (!names.Add(fileName))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR stripped-AOT input contains a portable file-name collision: '{fileName}'.");
                }

                var info = new FileInfo(readable);
                if (info.Length <= 0 || info.Length > MaximumAOTAssemblyBytes)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR AOT assembly '{fileName}' exceeds the allowed size range.");
                }

                totalBytes = checked(totalBytes + info.Length);
                if (totalBytes > MaximumTotalAOTBytes)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR stripped-AOT input exceeds the {MaximumTotalAOTBytes}-byte aggregate budget.");
                }

                entries.Add(new AOTAssembly
                {
                    fileName = fileName,
                    byteLength = info.Length,
                    sha256 = ComputeFileSha256(readable)
                });
            }

            return entries;
        }

        internal static string SerializeManifest(Manifest manifest, bool prettyPrint)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return JsonUtility.ToJson(manifest, prettyPrint);
        }

        internal static string ComputeManifestChecksum(Manifest manifest)
        {
            string original = manifest.manifestChecksum;
            try
            {
                manifest.manifestChecksum = string.Empty;
                return ComputeTextSha256(JsonUtility.ToJson(manifest, false));
            }
            finally
            {
                manifest.manifestChecksum = original;
            }
        }

        internal static string ComputeFileSha256(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       1024 * 1024,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        internal static string ComputeTextSha256(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(StrictUtf8.GetBytes(value ?? string.Empty)));
            }
        }

        internal static string RequireSha256(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                throw new ArgumentException(
                    "A lowercase SHA-256 value is required.",
                    parameterName);
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException(
                        "A lowercase SHA-256 value is required.",
                        parameterName);
                }
            }

            return value;
        }

        internal static string ValidateBaselineDirectory(
            string projectRoot,
            string buildRoot,
            string directory,
            string displayName)
        {
            string project = Path.GetFullPath(projectRoot);
            string root = BuildPathPolicy.EnsureSafeBuildRoot(project, buildRoot);
            string value = Path.GetFullPath(directory);
            string relative = Path.GetRelativePath(root, value);
            if (relative == "."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(relative, "..", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException(
                    $"{displayName} must remain below the configured build root: '{value}'.");
            }

            if (File.Exists(value))
            {
                throw new InvalidOperationException(
                    $"{displayName} resolves to a file: '{value}'.");
            }

            string existing = value;
            while (!Directory.Exists(existing))
            {
                existing = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(existing))
                {
                    break;
                }
            }

            while (!string.IsNullOrEmpty(existing)
                   && !PathsEqual(existing, root)
                   && IsDescendant(root, existing))
            {
                RejectReparsePoint(existing, displayName);
                existing = Path.GetDirectoryName(existing);
            }

            return BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                value,
                displayName,
                reservedChildPathCharacters: 80);
        }

        internal static void RejectReparsePoint(string path, string displayName)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{displayName} cannot be a symbolic link or reparse point: '{path}'.");
            }
        }

        internal static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsDescendant(string root, string candidate)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedCandidate = Path.GetFullPath(candidate);
            return normalizedCandidate.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase);
        }

        private static HybridCLRReleaseBaseline ValidateDirectory(
            string directory,
            HybridCLRReleaseBaselineExpectation expectation,
            bool requireCompatibilityMatch)
        {
            string baselineDirectory = ValidateBaselineDirectory(
                expectation.ProjectRoot,
                expectation.BuildRoot,
                directory,
                "HybridCLR release baseline");
            if (!Directory.Exists(baselineDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR release baseline is missing: '{baselineDirectory}'. " +
                    "Run a successful Clean Release recipe whose Player invocation directly depends on this hot-update invocation.");
            }


            return ValidateDirectoryContents(
                baselineDirectory,
                expectation,
                requireCompatibilityMatch);
        }

        private static HybridCLRReleaseBaseline ValidateDirectoryContents(
            string baselineDirectory,
            HybridCLRReleaseBaselineExpectation expectation,
            bool requireCompatibilityMatch)
        {
            if (!Directory.Exists(baselineDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR release-baseline directory is missing: '{baselineDirectory}'.");
            }

            RejectReparsePoint(baselineDirectory, "HybridCLR release baseline");
            string[] topLevelEntries = Directory.GetFileSystemEntries(
                baselineDirectory,
                "*",
                SearchOption.TopDirectoryOnly);
            if (topLevelEntries.Length != 2)
            {
                throw new InvalidDataException(
                    "HybridCLR release baseline must contain exactly baseline.json and the AOT directory.");
            }

            string manifestPath = Path.Combine(baselineDirectory, ManifestFileName);
            string aotDirectory = Path.Combine(baselineDirectory, AOTDirectoryName);
            if (!File.Exists(manifestPath) || !Directory.Exists(aotDirectory))
            {
                throw new InvalidDataException(
                    "HybridCLR release baseline is incomplete: baseline.json or the AOT directory is missing.");
            }

            RejectReparsePoint(manifestPath, "HybridCLR release-baseline manifest");
            RejectReparsePoint(aotDirectory, "HybridCLR release-baseline AOT directory");
            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline manifest exceeds the {MaximumManifestBytes}-byte budget.");
            }

            string json = File.ReadAllText(manifestPath, StrictUtf8);
            Manifest manifest;
            try
            {
                BuildJsonDocumentContract.Validate<Manifest>(
                    json,
                    DocumentType,
                    "HybridCLR release-baseline manifest");
                manifest = JsonUtility.FromJson<Manifest>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline manifest is not valid JSON.",
                    exception);
            }

            ValidateManifestIdentity(manifest, expectation, requireCompatibilityMatch);
            string manifestChecksum = RequireSha256(
                manifest.manifestChecksum,
                "manifestChecksum");
            string computedChecksum = ComputeManifestChecksum(manifest);
            if (!string.Equals(manifestChecksum, computedChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline manifest checksum does not match its content.");
            }

            ValidateAOTDirectory(aotDirectory, manifest.aotAssemblies);
            return new HybridCLRReleaseBaseline(
                baselineDirectory,
                aotDirectory,
                ComputeFileSha256(manifestPath));
        }

        private static void ValidateManifestIdentity(
            Manifest manifest,
            HybridCLRReleaseBaselineExpectation expectation,
            bool requireCompatibilityMatch)
        {
            if (manifest == null
                || !string.Equals(
                    manifest.documentType,
                    DocumentType,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline manifest does not match the current document contract.");
            }

            RequireEqual(manifest.releaseKey, expectation.ReleaseKey, "release key");
            RequireEqual(
                manifest.applicationIdentifier,
                expectation.ApplicationIdentifier,
                "application identifier");
            RequireEqual(
                manifest.applicationVersion,
                expectation.ApplicationVersion,
                "application version");
            RequireEqual(
                manifest.hotUpdateInvocationId,
                expectation.HotUpdateInvocationId,
                "hot-update invocation id");
            RequireEqual(manifest.buildTarget, expectation.BuildTarget, "build target");
            RequireEqual(
                manifest.scriptingBackend,
                expectation.ScriptingBackend,
                "scripting backend");
            RequireEqual(manifest.buildConfiguration, "Release", "build configuration");
            if (string.IsNullOrWhiteSpace(manifest.playerInvocationId))
            {
                throw new InvalidDataException(
                    "HybridCLR release baseline has no explicit Player consumer provenance.");
            }

            if (manifest.sourceBuildNumber <= 0)
            {
                throw new InvalidDataException(
                    "HybridCLR release baseline has an invalid source build number.");
            }

            if (manifest.hotUpdateAssemblies == null
                || manifest.hotUpdateAssemblies.Length == 0)
            {
                throw new InvalidDataException(
                    "HybridCLR release baseline has no hot-update assembly inventory.");
            }

            if (!requireCompatibilityMatch)
            {
                return;
            }

            RequireEqual(
                manifest.namedBuildTarget,
                expectation.NamedBuildTarget,
                "named build target");
            RequireEqual(manifest.unityVersion, expectation.UnityVersion, "Unity version");
            RequireEqual(
                manifest.hybridCLRPackageIdentity,
                expectation.HybridCLRPackageIdentity,
                "HybridCLR package identity");
            RequireEqual(
                manifest.authoringConfigurationHash,
                expectation.AuthoringConfigurationHash,
                "HybridCLR authoring configuration hash");
            RequireEqual(
                manifest.hybridCLRSettingsHash,
                expectation.HybridCLRSettingsHash,
                "HybridCLR settings hash");
            RequireEqual(
                manifest.playerConfigurationHash,
                expectation.PlayerConfigurationHash,
                "Player AOT configuration hash");
            RequireEqual(
                manifest.compatibilityHash,
                expectation.CompatibilityHash,
                "release compatibility hash");
            if (!manifest.hotUpdateAssemblies.SequenceEqual(
                    expectation.HotUpdateAssemblies,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline hot-update assembly inventory does not match the current configuration.");
            }
        }

        private static void ValidateAOTDirectory(
            string aotDirectory,
            IReadOnlyList<AOTAssembly> manifestEntries)
        {
            if (manifestEntries == null
                || manifestEntries.Count == 0
                || manifestEntries.Count > MaximumAOTAssemblyCount)
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline AOT inventory must contain between 1 and {MaximumAOTAssemblyCount} entries.");
            }

            string[] files = Directory.GetFiles(aotDirectory, "*", SearchOption.TopDirectoryOnly);
            string[] directories = Directory.GetDirectories(aotDirectory, "*", SearchOption.TopDirectoryOnly);
            if (directories.Length != 0 || files.Length != manifestEntries.Count)
            {
                throw new InvalidDataException(
                    "HybridCLR release-baseline AOT directory does not exactly match its manifest inventory.");
            }

            var actualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                RejectReparsePoint(file, "HybridCLR release-baseline AOT assembly");
                string fileName = Path.GetFileName(file);
                ValidateAOTFileName(fileName);
                if (!actualFiles.TryAdd(fileName, file))
                {
                    throw new InvalidDataException(
                        $"HybridCLR release baseline contains a portable file-name collision: '{fileName}'.");
                }
            }

            long totalBytes = 0;
            var manifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AOTAssembly entry in manifestEntries)
            {
                if (entry == null)
                {
                    throw new InvalidDataException(
                        "HybridCLR release-baseline AOT inventory contains a null entry.");
                }

                ValidateAOTFileName(entry.fileName);
                if (!manifestNames.Add(entry.fileName))
                {
                    throw new InvalidDataException(
                        $"HybridCLR release-baseline manifest contains a duplicate AOT assembly: '{entry.fileName}'.");
                }

                if (!actualFiles.TryGetValue(entry.fileName, out string file))
                {
                    throw new FileNotFoundException(
                        "HybridCLR release-baseline AOT assembly is missing.",
                        entry.fileName);
                }

                long actualLength = new FileInfo(file).Length;
                if (entry.byteLength <= 0
                    || entry.byteLength > MaximumAOTAssemblyBytes
                    || actualLength != entry.byteLength)
                {
                    throw new InvalidDataException(
                        $"HybridCLR release-baseline AOT assembly length does not match: '{entry.fileName}'.");
                }

                totalBytes = checked(totalBytes + actualLength);
                if (totalBytes > MaximumTotalAOTBytes)
                {
                    throw new InvalidDataException(
                        $"HybridCLR release-baseline AOT inventory exceeds {MaximumTotalAOTBytes} bytes.");
                }

                string expectedHash = RequireSha256(entry.sha256, "AOT assembly hash");
                string actualHash = ComputeFileSha256(file);
                if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"HybridCLR release-baseline AOT assembly hash does not match: '{entry.fileName}'.");
                }
            }
        }

        private static string ComputeAuthoringConfigurationHash(
            string projectRoot,
            HybridCLRBuildConfig configuration,
            IReadOnlyList<string> assemblyNames)
        {
            var builder = new StringBuilder(2048);
            Append(builder, "hybridclr-authoring");
            Append(builder, configuration.Variant.ToString());
            Append(builder, configuration.GetHotUpdateDllOutputDirectoryPath());
            Append(builder, configuration.GetAOTDllOutputDirectoryPath());
            foreach (string assemblyName in assemblyNames)
            {
                Append(builder, assemblyName);
            }

            string assetsRoot = Path.Combine(projectRoot, "Assets");
            foreach (string assetPath in configuration.GetHotUpdateAssemblyAssetPaths()
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    assetPath,
                    "HybridCLR hot-update assembly asset path");
                string absolutePath = BuildPathPolicy.EnsureSafeReadableFile(
                    assetsRoot,
                    Path.Combine(projectRoot, assetPath));
                Append(builder, assetPath);
                Append(builder, ComputeFileSha256(absolutePath));
            }

            return ComputeTextSha256(builder.ToString());
        }

        private static string ComputeHybridCLRSettingsHash()
        {
            Type settingsUtilType = ReflectionCache.GetType("HybridCLR.Editor.SettingsUtil");
            if (settingsUtilType == null)
            {
                throw new InvalidOperationException(
                    "HybridCLR SettingsUtil is unavailable while computing release-baseline compatibility.");
            }

            PropertyInfo property = ReflectionCache.GetProperty(
                settingsUtilType,
                "HybridCLRSettings",
                BindingFlags.Public | BindingFlags.Static);
            if (property == null)
            {
                throw new MissingMemberException(
                    settingsUtilType.FullName,
                    "HybridCLRSettings");
            }

            if (!(property.GetValue(null) is UnityEngine.Object settings))
            {
                throw new InvalidOperationException(
                    "HybridCLR SettingsUtil returned no serializable settings object.");
            }

            return ComputeTextSha256(EditorJsonUtility.ToJson(settings, false));
        }

        private static string GetHybridCLRPackageIdentity()
        {
            Type settingsUtilType = ReflectionCache.GetType("HybridCLR.Editor.SettingsUtil");
            if (settingsUtilType == null)
            {
                throw new InvalidOperationException(
                    "HybridCLR SettingsUtil is unavailable while computing package identity.");
            }

            Assembly assembly = settingsUtilType.Assembly;
            AssemblyName name = assembly.GetName();
            return string.Join("|", new[]
            {
                name.Name ?? string.Empty,
                name.Version?.ToString() ?? string.Empty,
                assembly.ManifestModule.ModuleVersionId.ToString("D")
            });
        }

        private static string ComputePlayerConfigurationHash(BuildRequest request)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(request.NamedTarget) ?? string.Empty;
            string normalizedDefines = string.Join(
                ";",
                defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal));
            var builder = new StringBuilder(512);
            Append(builder, "hybridclr-player-aot");
            Append(builder, request.Target.ToString());
            Append(builder, request.NamedTarget.ToString());
            Append(builder, request.ScriptingBackend.ToString());
            Append(builder, PlayerSettings.GetApiCompatibilityLevel(request.NamedTarget).ToString());
            Append(builder, PlayerSettings.GetManagedStrippingLevel(request.NamedTarget).ToString());
            Append(builder, PlayerSettings.GetIl2CppCompilerConfiguration(request.NamedTarget).ToString());
            Append(builder, PlayerSettings.stripEngineCode ? "1" : "0");
            Append(builder, PlayerSettings.allowUnsafeCode ? "1" : "0");
            Append(builder, normalizedDefines);
            return ComputeTextSha256(builder.ToString());
        }

        private static void RequireEqual(string actual, string expected, string displayName)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline {displayName} does not match the current build. " +
                    $"Baseline='{actual ?? "<null>"}', current='{expected ?? "<null>"}'.");
            }
        }

        private static void ValidateAOTFileName(string fileName)
        {
            BuildPathPolicy.ValidatePortableFileName(
                fileName,
                "HybridCLR AOT assembly file name");
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"HybridCLR release-baseline AOT artifact must be a DLL: '{fileName}'.");
            }
        }

        private static void Append(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append('\n');
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
