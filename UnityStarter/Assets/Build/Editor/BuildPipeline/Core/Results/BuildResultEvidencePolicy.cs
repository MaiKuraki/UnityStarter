using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Owns the deterministic size and text contract shared by result-manifest
    /// production and terminal evidence confirmation.
    /// </summary>
    internal static class BuildResultEvidencePolicy
    {
        internal const int MaximumDiagnosticCharacters = 16 * 1024;
        internal const int MaximumNonFatalFailureCount = 1024;
        internal const int MaximumStepResultCount =
            BuildPipelineBudgets.MaximumInvocationCount + 1;
        internal const int MaximumContentOperationResultCount = 1024;
        internal const int MaximumContentResultCount = 4096;
        internal const int MaximumProducedArtifactCount = 4096;
        internal const int MaximumContentWarningCount = 1024;
        internal const long MaximumContentResultUtf8Bytes = 1024L * 1024L;
        internal const long MaximumContentRunUtf8Bytes = 8L * 1024L * 1024L;
        internal const int MaximumContentRunValueCount = 128 * 1024;

        private const int MaximumAggregateDiagnosticExactJsonBytes =
            4 * 1024 * 1024;
        private const int MaximumAggregateDiagnosticSummaryJsonBytes =
            512 * 1024;

        private const int MaximumContentFieldUtf8Bytes = 256 * 1024;
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);
        private static readonly string WorstCaseDiagnostic =
            new string('\u0001', MaximumDiagnosticCharacters);

        internal static string WorstCaseDiagnosticText => WorstCaseDiagnostic;

        internal static DiagnosticBudget CreateDiagnosticBudget()
        {
            return new DiagnosticBudget();
        }

        internal static string NormalizeException(Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            try
            {
                return NormalizeDiagnosticText(exception.ToString());
            }
            catch (Exception renderingFailure) when (
                !(renderingFailure is OutOfMemoryException))
            {
                string typeName = exception.GetType().FullName ??
                                  exception.GetType().Name;
                return NormalizeDiagnosticText(
                    "[exception-render-failed type=" + typeName +
                    " renderer=" + renderingFailure.GetType().FullName + "]");
            }
        }

        internal static string NormalizeDiagnosticText(string value)
        {
            string text = value ?? string.Empty;
            string digest = null;
            if (!HasValidUtf16(text))
            {
                digest = ComputeUtf16Sha256(text);
                return $"[invalid-utf16 chars={text.Length.ToString(CultureInfo.InvariantCulture)} sha256={digest}]";
            }

            if (text.Length <= MaximumDiagnosticCharacters)
            {
                return text;
            }

            digest = ComputeUtf16Sha256(text);
            string marker =
                $"...[truncated chars={text.Length.ToString(CultureInfo.InvariantCulture)} sha256={digest}]";
            int prefixLength = MaximumDiagnosticCharacters - marker.Length;
            if (prefixLength < 0)
            {
                return marker.Substring(marker.Length - MaximumDiagnosticCharacters);
            }

            if (prefixLength > 0
                && prefixLength < text.Length
                && char.IsHighSurrogate(text[prefixLength - 1]))
            {
                prefixLength--;
            }

            return text.Substring(0, prefixLength) + marker;
        }

        internal static string[] NormalizeExceptions(
            IReadOnlyList<Exception> exceptions,
            DiagnosticBudget budget)
        {
            if (budget == null)
            {
                throw new ArgumentNullException(nameof(budget));
            }

            int count = exceptions?.Count ?? 0;
            RequireCount(
                count,
                MaximumNonFatalFailureCount,
                "Non-fatal build failures");
            if (count == 0)
            {
                return Array.Empty<string>();
            }

            var normalized = new string[count];
            for (int index = 0; index < count; index++)
            {
                normalized[index] = budget.NormalizeException(exceptions[index]);
            }

            return normalized;
        }

        internal static BuildResultManifestFormat.StepEntry[] CreateStepEntries(
            IReadOnlyList<BuildStepResult> steps,
            DiagnosticBudget budget)
        {
            if (budget == null)
            {
                throw new ArgumentNullException(nameof(budget));
            }

            int count = steps?.Count ?? 0;
            RequireCount(count, MaximumStepResultCount, "Build step results");
            if (count == 0)
            {
                return Array.Empty<BuildResultManifestFormat.StepEntry>();
            }

            var entries = new BuildResultManifestFormat.StepEntry[count];
            for (int index = 0; index < count; index++)
            {
                BuildStepResult step = steps[index]
                    ?? throw new InvalidOperationException(
                        $"Build step result at index {index} is null.");
                entries[index] = new BuildResultManifestFormat.StepEntry
                {
                    invocationId = step.InvocationId,
                    stepTypeId = step.StepTypeId,
                    status = step.Status.ToString(),
                    durationSeconds = step.Duration.TotalSeconds,
                    message = budget.NormalizeText(step.Message)
                };
            }

            return entries;
        }

        internal static BuildResultManifestFormat.ContentEntry[] CreateContentEntries(
            IReadOnlyList<AssetContentInvocationResult> contentResults)
        {
            int count = contentResults?.Count ?? 0;
            RequireCount(count, MaximumContentResultCount, "Content build results");
            if (count == 0)
            {
                return Array.Empty<BuildResultManifestFormat.ContentEntry>();
            }

            var entries = new BuildResultManifestFormat.ContentEntry[count];
            for (int index = 0; index < count; index++)
            {
                AssetContentInvocationResult invocation = contentResults[index]
                    ?? throw new InvalidOperationException(
                        $"Content build result at index {index} is null.");
                AssetContentBuildResult result = invocation.Result;
                entries[index] = new BuildResultManifestFormat.ContentEntry
                {
                    invocationId = invocation.InvocationId,
                    succeeded = result.Succeeded,
                    providerId = result.ProviderId,
                    packageName = result.PackageName,
                    packageVersion = result.PackageVersion,
                    failedTask = result.FailedTask,
                    errorInfo = result.ErrorInfo,
                    errorStack = result.ErrorStack,
                    outputPackageDirectory = result.OutputPackageDirectory,
                    bundledPackageDirectory = result.BundledPackageDirectory,
                    reportPath = result.ReportPath,
                    artifacts = SnapshotStrings(result.ProducedArtifacts),
                    warnings = SnapshotStrings(result.Warnings)
                };
            }

            return entries;
        }

        internal static long ValidateContentResult(AssetContentBuildResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            RequireCount(
                result.ProducedArtifacts.Count,
                MaximumProducedArtifactCount,
                "Produced artifact evidence");
            RequireCount(
                result.Warnings.Count,
                MaximumContentWarningCount,
                "Content warning evidence");

            long totalBytes = 0;
            AddContentFieldBytes(ref totalBytes, result.ProviderId, "ProviderId");
            AddContentFieldBytes(ref totalBytes, result.PackageName, "PackageName");
            AddContentFieldBytes(ref totalBytes, result.PackageVersion, "PackageVersion");
            AddContentFieldBytes(ref totalBytes, result.FailedTask, "FailedTask");
            AddContentFieldBytes(ref totalBytes, result.ErrorInfo, "ErrorInfo");
            AddContentFieldBytes(ref totalBytes, result.ErrorStack, "ErrorStack");
            AddContentFieldBytes(
                ref totalBytes,
                result.OutputPackageDirectory,
                "OutputPackageDirectory");
            AddContentFieldBytes(
                ref totalBytes,
                result.BundledPackageDirectory,
                "BundledPackageDirectory");
            AddContentFieldBytes(ref totalBytes, result.ReportPath, "ReportPath");
            AddContentCollectionBytes(
                ref totalBytes,
                result.ProducedArtifacts,
                "ProducedArtifacts");
            AddContentCollectionBytes(
                ref totalBytes,
                result.Warnings,
                "Warnings");
            if (totalBytes > MaximumContentResultUtf8Bytes)
            {
                throw new InvalidOperationException(
                    $"Content result evidence exceeds the {MaximumContentResultUtf8Bytes}-byte UTF-8 budget.");
            }

            return totalBytes;
        }

        internal static void RequireRunContentBytes(
            long currentBytes,
            long addedBytes)
        {
            if (currentBytes < 0
                || addedBytes < 0
                || currentBytes > MaximumContentRunUtf8Bytes
                || addedBytes > MaximumContentRunUtf8Bytes - currentBytes)
            {
                throw new InvalidOperationException(
                    $"Build content result evidence exceeds the {MaximumContentRunUtf8Bytes}-byte aggregate UTF-8 budget.");
            }
        }

        internal static void RequireRunContentValueCount(
            int currentCount,
            int addedCount)
        {
            if (currentCount < 0
                || addedCount < 0
                || currentCount > MaximumContentRunValueCount
                || addedCount > MaximumContentRunValueCount - currentCount)
            {
                throw new InvalidOperationException(
                    $"Build content result evidence exceeds the {MaximumContentRunValueCount}-value aggregate count budget.");
            }
        }

        private static void AddContentCollectionBytes(
            ref long totalBytes,
            IReadOnlyList<string> values,
            string label)
        {
            for (int index = 0; index < values.Count; index++)
            {
                AddContentFieldBytes(
                    ref totalBytes,
                    values[index],
                    $"{label}[{index}]");
            }
        }

        private static void AddContentFieldBytes(
            ref long totalBytes,
            string value,
            string label)
        {
            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(value ?? string.Empty);
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    $"Content result field '{label}' contains invalid UTF-16 text.",
                    exception);
            }

            if (byteCount > MaximumContentFieldUtf8Bytes)
            {
                throw new InvalidOperationException(
                    $"Content result field '{label}' exceeds the {MaximumContentFieldUtf8Bytes}-byte UTF-8 budget.");
            }

            try
            {
                totalBytes = checked(totalBytes + byteCount);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    "Content result evidence byte count overflowed its safety budget.",
                    exception);
            }
        }

        private static string[] SnapshotStrings(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var snapshot = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                snapshot[index] = values[index] ?? string.Empty;
            }

            return snapshot;
        }

        private static void RequireCount(int count, int maximum, string label)
        {
            if (count < 0 || count > maximum)
            {
                throw new InvalidOperationException(
                    $"{label} exceeds the {maximum}-entry evidence budget.");
            }
        }

        private static bool HasValidUtf16(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length
                        || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }

                    index++;
                    continue;
                }

                if (char.IsLowSurrogate(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeUtf16Sha256(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                var buffer = new byte[8192];
                int characterIndex = 0;
                while (characterIndex < value.Length)
                {
                    int characterCount = Math.Min(
                        buffer.Length / 2,
                        value.Length - characterIndex);
                    int byteCount = characterCount * 2;
                    for (int index = 0; index < characterCount; index++)
                    {
                        char character = value[characterIndex + index];
                        buffer[index * 2] = (byte)character;
                        buffer[index * 2 + 1] = (byte)(character >> 8);
                    }

                    hash.TransformBlock(buffer, 0, byteCount, buffer, 0);
                    characterIndex += characterCount;
                }

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                byte[] digest = hash.Hash;
                var builder = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    builder.Append(
                        digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        [Serializable]
        private sealed class JsonTextProbe
        {
            public string value = string.Empty;
        }

        internal sealed class DiagnosticBudget
        {
            private int remainingExactJsonBytes =
                MaximumAggregateDiagnosticExactJsonBytes;
            private int remainingSummaryJsonBytes =
                MaximumAggregateDiagnosticSummaryJsonBytes;

            internal string NormalizeException(Exception exception)
            {
                if (exception == null)
                {
                    return NormalizeText(string.Empty);
                }

                string rendered;
                try
                {
                    rendered = exception.ToString();
                }
                catch (Exception renderingFailure) when (
                    !(renderingFailure is OutOfMemoryException))
                {
                    rendered =
                        "[exception-render-failed type=" +
                        (exception.GetType().FullName ?? exception.GetType().Name) +
                        " renderer=" + renderingFailure.GetType().FullName + "]";
                }

                return NormalizeText(rendered);
            }

            internal string NormalizeText(string value)
            {
                string source = value ?? string.Empty;
                string normalized = NormalizeDiagnosticText(source);
                int exactBytes = GetJsonBytes(normalized);
                if (exactBytes <= remainingExactJsonBytes)
                {
                    remainingExactJsonBytes -= exactBytes;
                    return normalized;
                }

                string marker =
                    $"[summarized-by-run-budget chars={source.Length.ToString(CultureInfo.InvariantCulture)} sha256={ComputeUtf16Sha256(source)}]";
                int markerBytes = GetJsonBytes(marker);
                if (markerBytes > remainingSummaryJsonBytes)
                {
                    throw new InvalidOperationException(
                        $"Build diagnostic summaries exceed the {MaximumAggregateDiagnosticSummaryJsonBytes}-byte JSON evidence budget.");
                }

                remainingSummaryJsonBytes -= markerBytes;
                return marker;
            }

            private static int GetJsonBytes(string value)
            {
                string json = JsonUtility.ToJson(
                    new JsonTextProbe { value = value },
                    false);
                return StrictUtf8.GetByteCount(json);
            }
        }
    }
}
