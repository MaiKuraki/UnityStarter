using System;
using System.Globalization;
using System.Text;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    public static class AssetPatchJournalCodec
    {
        public const int SCHEMA_VERSION = 1;

        // Wire tokens are string literals by design; enum renames must not silently change persisted journal files.
        private static readonly StageToken[] StageTokens =
        {
            new StageToken(PatchWorkflowState.None, "None"),
            new StageToken(PatchWorkflowState.Initialize, "Initialize"),
            new StageToken(PatchWorkflowState.CheckVersion, "CheckVersion"),
            new StageToken(PatchWorkflowState.UpdateManifest, "UpdateManifest"),
            new StageToken(PatchWorkflowState.WaitingForDownload, "WaitingForDownload"),
            new StageToken(PatchWorkflowState.Download, "Download"),
            new StageToken(PatchWorkflowState.VerifyContentTrust, "VerifyContentTrust"),
            new StageToken(PatchWorkflowState.RollbackManifest, "RollbackManifest"),
            new StageToken(PatchWorkflowState.ClearCache, "ClearCache"),
            new StageToken(PatchWorkflowState.Done, "Done"),
            new StageToken(PatchWorkflowState.Failed, "Failed"),
            new StageToken(PatchWorkflowState.Cancelled, "Cancelled"),
            new StageToken(PatchWorkflowState.RepairContent, "RepairContent")
        };

        private static readonly StatusToken[] StatusTokens =
        {
            new StatusToken(AssetPatchJournalStatus.None, "None"),
            new StatusToken(AssetPatchJournalStatus.InProgress, "InProgress"),
            new StatusToken(AssetPatchJournalStatus.PendingDownload, "PendingDownload"),
            new StatusToken(AssetPatchJournalStatus.Succeeded, "Succeeded"),
            new StatusToken(AssetPatchJournalStatus.Failed, "Failed"),
            new StatusToken(AssetPatchJournalStatus.Cancelled, "Cancelled")
        };

        public static string ToJson(in AssetPatchJournalRecord record)
        {
            var builder = new StringBuilder(512);
            AppendJson(builder, in record);
            return builder.ToString();
        }

        public static void AppendJson(StringBuilder builder, in AssetPatchJournalRecord record)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Append('{');
            JsonBuilderUtility.AppendProperty(builder, "schemaVersion", SCHEMA_VERSION, appendComma: false);
            JsonBuilderUtility.AppendProperty(builder, "sequence", record.Sequence, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "packageName", record.PackageName, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "packageVersion", record.PackageVersion, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "rollbackVersion", record.RollbackVersion, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "stage", GetStageName(record.Stage), appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "status", GetStatusName(record.Status), appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "totalDownloadCount", record.TotalDownloadCount, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "totalDownloadBytes", record.TotalDownloadBytes, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "contentTrustEnabled", record.ContentTrustEnabled, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "trustFailureCount", record.TrustFailureCount, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "contentTrustManifestFingerprint", record.ContentTrustManifestFingerprint.ToString(CultureInfo.InvariantCulture), appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "startedUtcTicks", record.StartedUtcTicks, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "updatedUtcTicks", record.UpdatedUtcTicks, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "error", record.Error, appendComma: true);
            builder.Append('}');
        }

        public static AssetPatchJournalRecord FromJson(string json)
        {
            if (!TryFromJson(json, out AssetPatchJournalRecord record, out string error))
            {
                throw new FormatException(error);
            }

            return record;
        }

        public static bool TryFromJson(string json, out AssetPatchJournalRecord record, out string error)
        {
            record = default;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Patch journal JSON cannot be null or empty.";
                return false;
            }

            try
            {
                AssetPatchJournalDocument document = JsonUtility.FromJson<AssetPatchJournalDocument>(json);
                if (document == null)
                {
                    error = "Patch journal JSON could not be parsed.";
                    return false;
                }

                if (document.schemaVersion != SCHEMA_VERSION)
                {
                    error = $"Unsupported patch journal schema version: {document.schemaVersion}.";
                    return false;
                }

                if (!TryParseStage(document.stage, out PatchWorkflowState stage))
                {
                    error = $"Unsupported patch journal stage: {document.stage}.";
                    return false;
                }

                if (!TryParseStatus(document.status, out AssetPatchJournalStatus status))
                {
                    error = $"Unsupported patch journal status: {document.status}.";
                    return false;
                }

                if (!TryParseUInt64(document.contentTrustManifestFingerprint, out ulong fingerprint))
                {
                    error = $"Unsupported patch journal content trust fingerprint: {document.contentTrustManifestFingerprint}.";
                    return false;
                }

                record = new AssetPatchJournalRecord(
                    document.sequence,
                    document.packageName,
                    document.packageVersion,
                    document.rollbackVersion,
                    stage,
                    status,
                    document.totalDownloadCount,
                    document.totalDownloadBytes,
                    document.contentTrustEnabled,
                    document.trustFailureCount,
                    fingerprint,
                    document.startedUtcTicks,
                    document.updatedUtcTicks,
                    document.error);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryParseStage(string value, out PatchWorkflowState stage)
        {
            for (int i = 0; i < StageTokens.Length; i++)
            {
                if (string.Equals(StageTokens[i].Name, value, StringComparison.Ordinal))
                {
                    stage = StageTokens[i].Value;
                    return true;
                }
            }

            stage = default;
            return false;
        }

        private static bool TryParseStatus(string value, out AssetPatchJournalStatus status)
        {
            for (int i = 0; i < StatusTokens.Length; i++)
            {
                if (string.Equals(StatusTokens[i].Name, value, StringComparison.Ordinal))
                {
                    status = StatusTokens[i].Value;
                    return true;
                }
            }

            status = default;
            return false;
        }

        private static bool TryParseUInt64(string value, out ulong result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = 0UL;
                return false;
            }

            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }

        private static string GetStageName(PatchWorkflowState stage)
        {
            for (int i = 0; i < StageTokens.Length; i++)
            {
                if (StageTokens[i].Value == stage)
                {
                    return StageTokens[i].Name;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(stage), "Unsupported patch workflow state.");
        }

        private static string GetStatusName(AssetPatchJournalStatus status)
        {
            for (int i = 0; i < StatusTokens.Length; i++)
            {
                if (StatusTokens[i].Value == status)
                {
                    return StatusTokens[i].Name;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(status), "Unsupported patch journal status.");
        }

        private readonly struct StageToken
        {
            public readonly PatchWorkflowState Value;
            public readonly string Name;

            public StageToken(PatchWorkflowState value, string name)
            {
                Value = value;
                Name = name;
            }
        }

        private readonly struct StatusToken
        {
            public readonly AssetPatchJournalStatus Value;
            public readonly string Name;

            public StatusToken(AssetPatchJournalStatus value, string name)
            {
                Value = value;
                Name = name;
            }
        }

        [Serializable]
        private sealed class AssetPatchJournalDocument
        {
            public int schemaVersion;
            public long sequence;
            public string packageName;
            public string packageVersion;
            public string rollbackVersion;
            public string stage;
            public string status;
            public int totalDownloadCount;
            public long totalDownloadBytes;
            public bool contentTrustEnabled;
            public int trustFailureCount;
            public string contentTrustManifestFingerprint;
            public long startedUtcTicks;
            public long updatedUtcTicks;
            public string error;
        }
    }
}
