using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    internal static class PlayerBuildExtensionFingerprint
    {
        internal const int MaximumExtensionCount = 64;
        internal const string InvalidEvidencePrefix = "invalid:";
        private const int MaximumEvidenceTextCharacters = 4096;
        internal const long MaximumExtensionAssetBytes = 64L * 1024L * 1024L;
        internal const long MaximumTotalExtensionAssetBytes = 256L * 1024L * 1024L;

        internal static string ComputeForRequest(BuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            IReadOnlyList<BuildStepInvocation> playerInvocations =
                request.GetInvocationsByStepType(BuildStepTypeIds.Player);
            if (playerInvocations.Count == 0)
            {
                return string.Empty;
            }

            if (playerInvocations.Count != 1)
            {
                throw new InvalidOperationException(
                    "Player extension provenance requires exactly one Player invocation.");
            }

            return Compute(
                playerInvocations[0].GetConfiguration<PlayerBuildConfiguration>());
        }

        internal static string ComputeForEvidence(BuildRequest request)
        {
            try
            {
                return ComputeForRequest(request);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return InvalidEvidencePrefix + ComputeInvalidEvidenceFingerprint(
                    request,
                    exception);
            }
        }

        internal static string ResolveForEvidence(BuildExecutionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return context.TryGetPlayerExtensionFingerprint(out string fingerprint)
                ? fingerprint
                : ComputeForEvidence(context.Request);
        }

        internal static string Compute(PlayerBuildConfiguration configuration)
        {
            var builder = new StringBuilder(1024);
            Append(builder, "player-build-extensions");
            if (configuration == null)
            {
                Append(builder, "0");
                return ComputeSha256(builder.ToString());
            }

            IReadOnlyList<PlayerBuildExtensionConfiguration> extensions =
                configuration.Extensions;
            if (extensions.Count > MaximumExtensionCount)
            {
                throw new InvalidOperationException(
                    $"A Player build may select at most {MaximumExtensionCount} extensions.");
            }

            Append(builder, extensions.Count.ToString(CultureInfo.InvariantCulture));
            var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalAssetBytes = 0;
            for (int index = 0; index < extensions.Count; index++)
            {
                PlayerBuildExtensionConfiguration extension = extensions[index];
                if (extension == null)
                {
                    throw new InvalidOperationException(
                        $"Player extension entry {index} is empty.");
                }

                string providerId = extension.ProviderId?.Trim();
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    throw new InvalidOperationException(
                        $"Player extension entry {index} returned an empty provider id.");
                }

                BuildIdentityPolicy.ValidateBuildIdentifier(
                    providerId,
                    $"Player extension provider id at index {index}");
                if (!providerIds.Add(providerId))
                {
                    throw new InvalidOperationException(
                        $"Player extension provider '{providerId}' is configured more than once.");
                }

                Append(builder, providerId);
                IPlayerBuildExtensionAdapter adapter =
                    PlayerBuildExtensionRegistry.ResolveAdapter(extension);
                if (adapter == null)
                {
                    throw new InvalidOperationException(
                        $"No Player extension adapter is registered for provider '{providerId}'.");
                }

                Append(builder, adapter.CompatibilityId);
                AppendAssetIdentity(
                    builder,
                    extension,
                    $"Player extension '{providerId}' configuration",
                    ref totalAssetBytes);
            }

            return ComputeSha256(builder.ToString());
        }

        private static void AppendAssetIdentity(
            StringBuilder builder,
            UnityEngine.Object asset,
            string label,
            ref long totalAssetBytes)
        {
            string path = AssetDatabase.GetAssetPath(asset)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || !ReferenceEquals(AssetDatabase.LoadMainAssetAtPath(path), asset))
            {
                throw new InvalidOperationException(
                    $"{label} must be a persistent main .asset below Assets.");
            }

            BuildPathPolicy.ValidatePortableProjectRelativePath(path, label + " path");
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    asset,
                    out string guid,
                    out long localFileId)
                || string.IsNullOrWhiteSpace(guid))
            {
                throw new InvalidOperationException(
                    $"{label} has no stable Unity asset identity.");
            }

            Append(builder, path);
            Append(builder, guid);
            Append(builder, localFileId.ToString(CultureInfo.InvariantCulture));
            string projectRoot = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.dataPath, ".."));
            string absolutePath = BuildPathPolicy.EnsureSafeReadableFile(
                projectRoot,
                Path.Combine(projectRoot, path));
            long assetBytes = new FileInfo(absolutePath).Length;
            totalAssetBytes = AddAssetBytesToBudget(
                totalAssetBytes,
                assetBytes,
                label);

            Append(builder, assetBytes.ToString(CultureInfo.InvariantCulture));
            Append(builder, ComputeFileSha256(absolutePath));
            Append(builder, AssetDatabase.GetAssetDependencyHash(path).ToString());
        }

        internal static long AddAssetBytesToBudget(
            long currentTotalBytes,
            long assetBytes,
            string label)
        {
            if (currentTotalBytes < 0
                || currentTotalBytes > MaximumTotalExtensionAssetBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentTotalBytes),
                    currentTotalBytes,
                    "Player extension fingerprint aggregate bytes are outside the supported range.");
            }

            if (assetBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(assetBytes),
                    assetBytes,
                    "Player extension configuration asset bytes may not be negative.");
            }

            string assetLabel = string.IsNullOrWhiteSpace(label)
                ? "Player extension configuration"
                : label;
            if (assetBytes > MaximumExtensionAssetBytes)
            {
                throw new IOException(
                    $"{assetLabel} exceeds the {MaximumExtensionAssetBytes}-byte fingerprint budget.");
            }

            if (currentTotalBytes > MaximumTotalExtensionAssetBytes - assetBytes)
            {
                throw new IOException(
                    $"Player extension configuration assets exceed the {MaximumTotalExtensionAssetBytes}-byte aggregate fingerprint budget.");
            }

            return currentTotalBytes + assetBytes;
        }

        private static string ComputeInvalidEvidenceFingerprint(
            BuildRequest request,
            Exception failure)
        {
            var builder = new StringBuilder(1024);
            Append(builder, "invalid-player-build-extensions");
            Append(builder, failure?.GetType().FullName ?? string.Empty);
            Append(builder, LimitEvidenceText(failure?.Message));
            if (request == null)
            {
                Append(builder, "request-null");
                return ComputeSha256(builder.ToString());
            }

            try
            {
                IReadOnlyList<BuildStepInvocation> invocations =
                    request.GetInvocationsByStepType(BuildStepTypeIds.Player);
                Append(
                    builder,
                    invocations.Count.ToString(CultureInfo.InvariantCulture));
                for (int invocationIndex = 0;
                     invocationIndex < invocations.Count;
                     invocationIndex++)
                {
                    BuildStepInvocation invocation = invocations[invocationIndex];
                    Append(builder, invocation?.InvocationId ?? string.Empty);
                    Append(builder, invocation?.StepTypeId ?? string.Empty);
                    PlayerBuildConfiguration configuration =
                        invocation?.Configuration as PlayerBuildConfiguration;
                    Append(
                        builder,
                        configuration?.GetType().FullName ?? string.Empty);
                    if (configuration == null)
                    {
                        Append(builder, "0");
                        continue;
                    }

                    IReadOnlyList<PlayerBuildExtensionConfiguration> extensions =
                        configuration.Extensions;
                    Append(
                        builder,
                        extensions.Count.ToString(CultureInfo.InvariantCulture));
                    int capturedCount = Math.Min(
                        extensions.Count,
                        MaximumExtensionCount);
                    for (int extensionIndex = 0;
                         extensionIndex < capturedCount;
                         extensionIndex++)
                    {
                        PlayerBuildExtensionConfiguration extension =
                            extensions[extensionIndex];
                        Append(
                            builder,
                            extension?.GetType().FullName ?? string.Empty);
                        AppendInvalidExtensionIdentity(builder, extension);
                    }
                }
            }
            catch (Exception identityException) when (
                !(identityException is OutOfMemoryException))
            {
                Append(builder, "identity-capture-failed");
                Append(builder, identityException.GetType().FullName);
                Append(builder, LimitEvidenceText(identityException.Message));
            }

            return ComputeSha256(builder.ToString());
        }

        private static void AppendInvalidExtensionIdentity(
            StringBuilder builder,
            PlayerBuildExtensionConfiguration extension)
        {
            if (extension == null)
            {
                Append(builder, string.Empty);
                Append(builder, string.Empty);
                Append(builder, string.Empty);
                return;
            }

            try
            {
                Append(builder, LimitEvidenceText(extension.ProviderId));
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                Append(builder, "provider-id-failed:" + exception.GetType().FullName);
            }

            try
            {
                string path = AssetDatabase.GetAssetPath(extension)?.Replace('\\', '/');
                Append(builder, path);
                Append(builder, string.IsNullOrWhiteSpace(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path));
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                Append(builder, "asset-identity-failed:" + exception.GetType().FullName);
                Append(builder, string.Empty);
            }
        }

        private static string LimitEvidenceText(string value)
        {
            if (string.IsNullOrEmpty(value)
                || value.Length <= MaximumEvidenceTextCharacters)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, MaximumEvidenceTextCharacters);
        }

        private static void Append(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append('\n');
        }

        private static string ComputeSha256(string text)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string ComputeFileSha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       8192,
                       FileOptions.SequentialScan))
            {
                byte[] bytes = hash.ComputeHash(stream);
                var builder = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
