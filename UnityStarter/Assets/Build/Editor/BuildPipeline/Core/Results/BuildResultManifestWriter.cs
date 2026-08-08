using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using UnityEngine;
using BuildIdentityEntry = Build.Pipeline.Editor.BuildResultManifestFormat.BuildIdentityEntry;
using CiIdentityEntry = Build.Pipeline.Editor.BuildResultManifestFormat.CiIdentityEntry;
using DependencyEntry = Build.Pipeline.Editor.BuildResultManifestFormat.DependencyEntry;
using ManifestDocument = Build.Pipeline.Editor.BuildResultManifestFormat.Document;
using RecipeInvocationEntry = Build.Pipeline.Editor.BuildResultManifestFormat.RecipeInvocationEntry;

namespace Build.Pipeline.Editor
{
    internal sealed class BuildResultManifestSnapshot
    {
        private const string WorstCaseFinishedUtc =
            "9999-12-31T23:59:59.9999999Z";
        private readonly ManifestDocument manifest;

        internal BuildResultManifestSnapshot(
            ManifestDocument manifest,
            string manifestPath,
            string outputPath)
        {
            this.manifest = manifest
                ?? throw new ArgumentNullException(nameof(manifest));
            ManifestPath = manifestPath
                ?? throw new ArgumentNullException(nameof(manifestPath));
            OutputPath = outputPath ?? string.Empty;

            string previousFinishedUtc = manifest.finishedUtc;
            bool previousSucceeded = manifest.succeeded;
            string previousFailure = manifest.failure;
            try
            {
                manifest.finishedUtc = WorstCaseFinishedUtc;
                manifest.succeeded = false;
                manifest.failure =
                    BuildResultEvidencePolicy.WorstCaseDiagnosticText;
                WorstCaseByteCount =
                    BuildResultManifestWriter.SerializeStrictUtf8(manifest).Length;
            }
            finally
            {
                manifest.finishedUtc = previousFinishedUtc;
                manifest.succeeded = previousSucceeded;
                manifest.failure = previousFailure;
            }
        }

        internal string ManifestPath { get; }
        internal string OutputPath { get; }
        internal int WorstCaseByteCount { get; }
        internal bool CapacityValidated { get; private set; }

        internal void ValidateCapacity(int maximumBytes)
        {
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBytes),
                    maximumBytes,
                    "Build result manifest capacity must be positive.");
            }

            if (WorstCaseByteCount > maximumBytes)
            {
                throw new IOException(
                    $"Worst-case terminal build result manifest requires {WorstCaseByteCount} bytes and exceeds the {maximumBytes}-byte safety budget.");
            }

            CapacityValidated = true;
        }

        internal byte[] CreateTerminalBytes(BuildRunResult result)
        {
            if (!CapacityValidated)
            {
                throw new InvalidOperationException(
                    "Build result manifest capacity was not validated before terminal serialization.");
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (!string.Equals(result.RunId, manifest.runId, StringComparison.Ordinal)
                || !PathsEqual(result.ResultManifestPath, ManifestPath)
                || !string.Equals(result.OutputPath, OutputPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The terminal build result does not match its frozen manifest payload.");
            }

            manifest.finishedUtc = DateTime.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);
            manifest.succeeded = result.Succeeded;
            manifest.failure =
                BuildResultEvidencePolicy.NormalizeException(result.Failure);
            byte[] bytes = BuildResultManifestWriter.SerializeStrictUtf8(manifest);
            if (bytes.Length > WorstCaseByteCount)
            {
                throw new InvalidOperationException(
                    "Terminal build result serialization exceeded its validated worst-case envelope.");
            }

            return bytes;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left ?? string.Empty),
                Path.GetFullPath(right ?? string.Empty),
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
    }

    internal static class BuildResultManifestWriter
    {
        private const int BufferSize = 8192;
        internal const int MaximumManifestBytes = 64 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static string GetManifestPath(BuildRequest request, string runId)
        {
            string path = Path.Combine(
                request.ProjectRoot,
                ".buildpipeline",
                "results",
                runId + ".json");
            return BuildPathPolicy.EnsureWin32MaxPathBudget(
                path,
                "Build result manifest",
                ".tmp".Length);
        }

        /// <summary>
        /// One-shot result writing for tests and non-publication callers. The
        /// pipeline runner uses FreezeForPublication and the snapshot overload
        /// so terminal I/O never re-reads mutable execution state.
        /// </summary>
        public static void Write(BuildExecutionContext context, BuildRunResult result)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (!context.RecipeProvenanceCaptured)
            {
                BuildRecipeProvenanceCapture provenance =
                    BuildRecipeProvenanceCapture.Capture(context.Request);
                context.SetRecipeProvenance(provenance.Entries);
                provenance.ThrowIfInvalid();
            }

            context.SealForPublication();
            BuildResultManifestSnapshot snapshot =
                FreezeForPublication(context, result);
            ValidatePublicationCapacity(snapshot);
            Write(snapshot, result);
        }

        internal static BuildResultManifestSnapshot FreezeForPublication(
            BuildExecutionContext context,
            BuildRunResult provisionalResult)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (provisionalResult == null)
            {
                throw new ArgumentNullException(nameof(provisionalResult));
            }

            if (!context.IsSealedForPublication)
            {
                throw new InvalidOperationException(
                    "Build execution context must be sealed before freezing result evidence.");
            }

            BuildVersionContext version = context.Version;
            BuildResultEvidencePolicy.DiagnosticBudget diagnosticBudget =
                BuildResultEvidencePolicy.CreateDiagnosticBudget();
            var manifest = new ManifestDocument
            {
                formatVersion = BuildResultManifestFormat.CurrentVersion,
                operation = "build",
                runId = provisionalResult.RunId,
                succeeded = false,
                partial = false,
                startedUtc = context.StartedUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                finishedUtc = string.Empty,
                unityVersion = Application.unityVersion,
                target = context.Request.Target.ToString(),
                namedBuildTarget = context.Request.NamedTarget.TargetName,
                scriptingBackend = context.Request.ScriptingBackend.ToString(),
                debugBuild = context.Request.DebugBuild,
                deleteDebugFiles = context.Request.DeleteDebugFiles,
                exportAndroidProject = context.Request.ExportAndroidProject,
                allowExternalOutput = context.Request.AllowExternalOutput,
                outputIsFolder = context.Request.OutputIsFolder,
                applicationVersion = context.Request.ApplicationVersion,
                packageVersion = version?.PackageVersion ?? string.Empty,
                detectedIdentity = new BuildIdentityEntry
                {
                    hasBuildNumber = version?.DetectedBuildNumber.HasValue ?? false,
                    buildNumber = version?.DetectedBuildNumber ?? 0,
                    sourceProvider = version?.DetectedProviderId ?? string.Empty,
                    sourceRevision = version?.DetectedCommitHash ?? string.Empty,
                    sourceBranch = version?.DetectedBranch ?? string.Empty,
                    sourceCommitCount = version?.DetectedCommitCount ?? string.Empty,
                    sourceCommitDate = version?.DetectedCommitDate ?? string.Empty
                },
                effectiveIdentity = new BuildIdentityEntry
                {
                    hasBuildNumber = version != null,
                    buildNumber = version?.EffectiveBuildNumber ?? 0,
                    sourceProvider = version?.EffectiveSourceProvider ?? string.Empty,
                    sourceRevision = version?.EffectiveSourceRevision ?? string.Empty,
                    sourceBranch = version?.EffectiveSourceBranch ?? string.Empty,
                    sourceCommitCount = version?.CommitCount ?? string.Empty,
                    sourceCommitDate = version?.CommitDate ?? string.Empty
                },
                identityOrigin = version?.IdentityOrigin.ToString() ?? string.Empty,
                ciIdentity = new CiIdentityEntry
                {
                    provider = version?.CiProvider ?? string.Empty,
                    runId = version?.CiRunId ?? string.Empty
                },
                buildRoot = context.Request.BuildRoot,
                outputPath = provisionalResult.OutputPath,
                outputDirectory = context.Request.OutputDirectory,
                versionInfoAssetPath = context.Request.VersionInfoAssetPath,
                buildScenePaths = context.Request.BuildScenePaths.ToArray(),
                cheatBuildMode = context.Request.CheatBuildMode.ToString(),
                cheatEnabled = context.Request.CheatEnabled,
                playerPipelineCompatibilityRevision =
                    PlayerOutputTransaction.PlayerPipelineCompatibilityRevision,
                playerExtensionFingerprint =
                    PlayerBuildExtensionFingerprint.ResolveForEvidence(context),
                failure = string.Empty,
                nonFatalFailures = BuildResultEvidencePolicy.NormalizeExceptions(
                    provisionalResult.NonFatalFailures,
                    diagnosticBudget),
                recipeInvocations = CreateRecipeEntries(
                    context.RecipeProvenance,
                    diagnosticBudget),
                steps = BuildResultEvidencePolicy.CreateStepEntries(
                    provisionalResult.Steps,
                    diagnosticBudget),
                content = BuildResultEvidencePolicy.CreateContentEntries(
                    context.ContentResults)
            };

            return new BuildResultManifestSnapshot(
                manifest,
                provisionalResult.ResultManifestPath,
                provisionalResult.OutputPath);
        }

        internal static void ValidatePublicationCapacity(
            BuildResultManifestSnapshot snapshot)
        {
            ValidatePublicationCapacity(snapshot, MaximumManifestBytes);
        }

        internal static void ValidatePublicationCapacity(
            BuildResultManifestSnapshot snapshot,
            int maximumBytes)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.ValidateCapacity(maximumBytes);
        }

        internal static void Write(
            BuildResultManifestSnapshot snapshot,
            BuildRunResult result)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            string path = BuildPathPolicy.EnsureWin32MaxPathBudget(
                snapshot.ManifestPath,
                "Build result manifest",
                ".tmp".Length);
            string directory = Path.GetDirectoryName(path);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                directory,
                "Build result manifest directory");
            Directory.CreateDirectory(directory);

            byte[] bytes = snapshot.CreateTerminalBytes(result);
            if (bytes.Length > MaximumManifestBytes)
            {
                throw new IOException(
                    $"Build result manifest exceeds the {MaximumManifestBytes}-byte safety budget: '{path}'.");
            }

            string temporaryPath = path + ".tmp";
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                temporaryPath,
                "Build result manifest temporary file");
            bool ownsTemporaryFile = false;
            Exception writeFailure = null;
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           BufferSize,
                           FileOptions.WriteThrough))
                {
                    ownsTemporaryFile = true;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                File.Move(temporaryPath, path);
                ownsTemporaryFile = false;
            }
            catch (Exception exception)
            {
                writeFailure = exception;
            }

            Exception cleanupFailure = null;
            try
            {
                if (ownsTemporaryFile && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (writeFailure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "Build result manifest write and temporary-file cleanup both failed.",
                    writeFailure,
                    cleanupFailure);
            }

            if (writeFailure != null)
            {
                ExceptionDispatchInfo.Capture(writeFailure).Throw();
            }

            if (cleanupFailure != null)
            {
                throw new IOException(
                    $"Build result manifest was written, but temporary file '{temporaryPath}' could not be removed.",
                    cleanupFailure);
            }
        }

        internal static byte[] SerializeStrictUtf8(ManifestDocument manifest)
        {
            string json = JsonUtility.ToJson(
                manifest ?? throw new ArgumentNullException(nameof(manifest)),
                true);
            return StrictUtf8.GetBytes(json);
        }

        private static RecipeInvocationEntry[] CreateRecipeEntries(
            IReadOnlyList<BuildRecipeProvenanceEntry> provenance,
            BuildResultEvidencePolicy.DiagnosticBudget diagnosticBudget)
        {
            if (diagnosticBudget == null)
            {
                throw new ArgumentNullException(nameof(diagnosticBudget));
            }

            if (provenance == null || provenance.Count == 0)
            {
                return Array.Empty<RecipeInvocationEntry>();
            }

            return provenance.Select(entry => new RecipeInvocationEntry
            {
                order = entry.Order,
                invocationId = entry.InvocationId,
                stepTypeId = entry.StepTypeId,
                incrementality = entry.Incrementality.ToString(),
                dependencies = ResolveDependencies(entry.Dependencies),
                hasConfiguration = entry.HasConfiguration,
                configurationAssetPath = entry.ConfigurationAssetPath,
                configurationAssetGuid = entry.ConfigurationAssetGuid,
                configurationLocalFileId = entry.ConfigurationLocalFileId,
                configurationType = entry.ConfigurationType,
                configurationAssetSha256 = entry.ConfigurationAssetSha256,
                configurationDependencyHash = entry.ConfigurationDependencyHash,
                configurationDependencyCount = entry.ConfigurationDependencyCount,
                validationError = diagnosticBudget.NormalizeText(
                    entry.ValidationError)
            }).ToArray();
        }

        private static DependencyEntry[] ResolveDependencies(
            IReadOnlyList<BuildInvocationDependency> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return Array.Empty<DependencyEntry>();
            }

            return dependencies.Select(dependency => new DependencyEntry
            {
                invocationId = dependency.InvocationId,
                mode = dependency.Mode.ToString()
            }).ToArray();
        }
    }
}
