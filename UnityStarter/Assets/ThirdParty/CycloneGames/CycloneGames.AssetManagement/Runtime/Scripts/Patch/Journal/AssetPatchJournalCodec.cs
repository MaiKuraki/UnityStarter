using System;
using System.Globalization;
using System.Text;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    public static class AssetPatchJournalCodec
    {
        public const int SCHEMA_VERSION = 1;

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
            AppendJsonProperty(builder, "schemaVersion", SCHEMA_VERSION, appendComma: false);
            AppendJsonProperty(builder, "sequence", record.Sequence, appendComma: true);
            AppendJsonProperty(builder, "packageName", record.PackageName, appendComma: true);
            AppendJsonProperty(builder, "packageVersion", record.PackageVersion, appendComma: true);
            AppendJsonProperty(builder, "rollbackVersion", record.RollbackVersion, appendComma: true);
            AppendJsonProperty(builder, "stage", GetStageName(record.Stage), appendComma: true);
            AppendJsonProperty(builder, "status", GetStatusName(record.Status), appendComma: true);
            AppendJsonProperty(builder, "totalDownloadCount", record.TotalDownloadCount, appendComma: true);
            AppendJsonProperty(builder, "totalDownloadBytes", record.TotalDownloadBytes, appendComma: true);
            AppendJsonProperty(builder, "contentTrustEnabled", record.ContentTrustEnabled, appendComma: true);
            AppendJsonProperty(builder, "trustFailureCount", record.TrustFailureCount, appendComma: true);
            AppendJsonProperty(builder, "contentTrustManifestFingerprint", record.ContentTrustManifestFingerprint.ToString(CultureInfo.InvariantCulture), appendComma: true);
            AppendJsonProperty(builder, "startedUtcTicks", record.StartedUtcTicks, appendComma: true);
            AppendJsonProperty(builder, "updatedUtcTicks", record.UpdatedUtcTicks, appendComma: true);
            AppendJsonProperty(builder, "error", record.Error, appendComma: true);
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
            return Enum.TryParse(value, ignoreCase: false, out stage);
        }

        private static bool TryParseStatus(string value, out AssetPatchJournalStatus status)
        {
            return Enum.TryParse(value, ignoreCase: false, out status);
        }

        private static bool TryParseUInt64(string value, out ulong result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = 0UL;
                return true;
            }

            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }

        private static string GetStageName(PatchWorkflowState stage)
        {
            switch (stage)
            {
                case PatchWorkflowState.None:
                    return nameof(PatchWorkflowState.None);
                case PatchWorkflowState.Initialize:
                    return nameof(PatchWorkflowState.Initialize);
                case PatchWorkflowState.CheckVersion:
                    return nameof(PatchWorkflowState.CheckVersion);
                case PatchWorkflowState.UpdateManifest:
                    return nameof(PatchWorkflowState.UpdateManifest);
                case PatchWorkflowState.WaitingForDownload:
                    return nameof(PatchWorkflowState.WaitingForDownload);
                case PatchWorkflowState.Download:
                    return nameof(PatchWorkflowState.Download);
                case PatchWorkflowState.VerifyContentTrust:
                    return nameof(PatchWorkflowState.VerifyContentTrust);
                case PatchWorkflowState.RollbackManifest:
                    return nameof(PatchWorkflowState.RollbackManifest);
                case PatchWorkflowState.ClearCache:
                    return nameof(PatchWorkflowState.ClearCache);
                case PatchWorkflowState.Done:
                    return nameof(PatchWorkflowState.Done);
                case PatchWorkflowState.Failed:
                    return nameof(PatchWorkflowState.Failed);
                case PatchWorkflowState.Cancelled:
                    return nameof(PatchWorkflowState.Cancelled);
                case PatchWorkflowState.RepairContent:
                    return nameof(PatchWorkflowState.RepairContent);
                default:
                    return stage.ToString();
            }
        }

        private static string GetStatusName(AssetPatchJournalStatus status)
        {
            switch (status)
            {
                case AssetPatchJournalStatus.None:
                    return nameof(AssetPatchJournalStatus.None);
                case AssetPatchJournalStatus.InProgress:
                    return nameof(AssetPatchJournalStatus.InProgress);
                case AssetPatchJournalStatus.PendingDownload:
                    return nameof(AssetPatchJournalStatus.PendingDownload);
                case AssetPatchJournalStatus.Succeeded:
                    return nameof(AssetPatchJournalStatus.Succeeded);
                case AssetPatchJournalStatus.Failed:
                    return nameof(AssetPatchJournalStatus.Failed);
                case AssetPatchJournalStatus.Cancelled:
                    return nameof(AssetPatchJournalStatus.Cancelled);
                default:
                    return status.ToString();
            }
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, string value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            AppendJsonString(builder, value);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, int value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            builder.Append(value);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, long value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            builder.Append(value);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, bool value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            builder.Append(value ? "true" : "false");
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
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
