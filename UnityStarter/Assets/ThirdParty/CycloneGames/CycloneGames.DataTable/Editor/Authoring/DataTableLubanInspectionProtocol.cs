using System;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal static class DataTableLubanInspectionProtocol
    {
        internal const string Schema = "CycloneGames.DataTable.PipelineInspection";
        internal const int SchemaVersion = 1;
        internal const int MaximumJsonCharacters = 8 * 1024 * 1024;
        private const int MaximumProfiles = 256;
        private const int MaximumIssues = 1024;
        private const int MaximumPathCharacters = 4096;
        private const int MaximumMessageCharacters = 16 * 1024;

        internal static bool TryParse(
            string json,
            out DataTableLubanInspectionDocument document,
            out string error)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The pipeline inspection returned an empty JSON document.";
                return false;
            }

            if (json.Length > MaximumJsonCharacters)
            {
                error = "The pipeline inspection JSON exceeded the 8 MiB character limit.";
                return false;
            }

            try
            {
                document = JsonUtility.FromJson<DataTableLubanInspectionDocument>(json);
            }
            catch (ArgumentException exception)
            {
                error = "The pipeline inspection returned malformed JSON: " + exception.Message;
                return false;
            }

            if (document == null)
            {
                error = "The pipeline inspection JSON could not be decoded.";
                return false;
            }

            if (!string.Equals(document.schema, Schema, StringComparison.Ordinal) ||
                document.schemaVersion != SchemaVersion)
            {
                error =
                    "Unsupported pipeline inspection schema. Expected " + Schema +
                    " version " + SchemaVersion + ".";
                return false;
            }

            if (!IsStatus(document.status))
            {
                error = "The pipeline inspection returned an unknown status: " + document.status;
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.configurationPath) ||
                string.IsNullOrWhiteSpace(document.configurationSha256) ||
                string.IsNullOrWhiteSpace(document.sourceRoot) ||
                string.IsNullOrWhiteSpace(document.selectedProfileName) ||
                document.processTimeoutSeconds < 1 ||
                document.processTimeoutSeconds > 86_400)
            {
                error = "The pipeline inspection returned incomplete top-level configuration identity.";
                return false;
            }

            document.issues ??= Array.Empty<DataTableLubanInspectionIssueDto>();
            document.profiles ??= Array.Empty<DataTableLubanInspectionProfileDto>();
            document.selectedProfile ??= new DataTableLubanInspectionProfileDto();
            document.toolchain ??= new DataTableLubanInspectionToolchainDto();
            document.output ??= new DataTableLubanInspectionOutputDto();
            document.transaction ??= new DataTableLubanInspectionTransactionDto();

            if (document.issues.Length > MaximumIssues || document.profiles.Length > MaximumProfiles)
            {
                error = "The pipeline inspection exceeded the supported issue or profile count.";
                return false;
            }

            if (!ValidateDocumentStrings(document, out error))
            {
                return false;
            }

            return true;
        }

        internal static DataTableLubanAuthoringSnapshot Project(
            DataTableLubanInspectionDocument document,
            string settingsAssetPath,
            int settingsAssetCount,
            bool settingsDirty)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            int localIssueCount = (settingsAssetCount == 1 ? 0 : 1) + (settingsDirty ? 1 : 0);
            var issues = new DataTableLubanAuthoringIssue[
                document.issues.Length + localIssueCount];
            for (int index = 0; index < document.issues.Length; index++)
            {
                DataTableLubanInspectionIssueDto source = document.issues[index]
                    ?? new DataTableLubanInspectionIssueDto();
                issues[index] = new DataTableLubanAuthoringIssue(
                    source.code,
                    ParseSeverity(source.severity),
                    source.scope,
                    source.message,
                    source.path);
            }

            int localIssueIndex = document.issues.Length;
            if (settingsAssetCount != 1)
            {
                issues[localIssueIndex++] = new DataTableLubanAuthoringIssue(
                    "SETTINGS_NOT_UNIQUE",
                    DataTableLubanIssueSeverity.Error,
                    "configuration",
                    "Exactly one DataTableLubanSettings asset is required before any pipeline operation.",
                    settingsAssetPath);
            }

            if (settingsDirty)
            {
                issues[localIssueIndex] = new DataTableLubanAuthoringIssue(
                    "SETTINGS_UNSAVED",
                    DataTableLubanIssueSeverity.Error,
                    "configuration",
                    "Save the DataTableLubanSettings asset before running the pipeline so Editor and CI use durable configuration.",
                    settingsAssetPath);
            }

            var profiles = new DataTableLubanProfileSnapshot[document.profiles.Length];
            for (int index = 0; index < profiles.Length; index++)
            {
                profiles[index] = ProjectProfile(document.profiles[index]);
            }

            bool localBlock = settingsAssetCount != 1 || settingsDirty;
            DataTableLubanAuthoringState state = ParseState(document.status);
            if (localBlock && state == DataTableLubanAuthoringState.Ready)
            {
                state = DataTableLubanAuthoringState.Blocked;
            }

            return new DataTableLubanAuthoringSnapshot(
                state,
                document.canGenerate && !localBlock,
                document.canCheck && !localBlock,
                document.canRecover && !localBlock,
                settingsAssetPath,
                document.configurationPath,
                document.configurationSha256,
                document.sourceRoot,
                document.selectedProfileName,
                document.processTimeoutSeconds,
                issues,
                profiles,
                ProjectProfile(document.selectedProfile),
                ProjectToolchain(document.toolchain),
                new DataTableLubanOutputSnapshot(
                    document.output.state,
                    document.output.receiptPath,
                    document.output.receiptExists,
                    document.output.receiptValid,
                    document.output.generation),
                ProjectTransaction(document.transaction),
                string.Empty,
                settingsAssetCount,
                settingsDirty,
                false);
        }

        private static bool ValidateDocumentStrings(
            DataTableLubanInspectionDocument document,
            out string error)
        {
            if (!Fits(document.configurationPath, MaximumPathCharacters) ||
                !Fits(document.sourceRoot, MaximumPathCharacters) ||
                !Fits(document.selectedProfileName, 128) ||
                !Fits(document.configurationSha256, 128))
            {
                error = "The pipeline inspection returned an oversized top-level field.";
                return false;
            }

            for (int index = 0; index < document.issues.Length; index++)
            {
                DataTableLubanInspectionIssueDto issue = document.issues[index];
                if (issue == null ||
                    string.IsNullOrWhiteSpace(issue.code) ||
                    !IsIssueSeverity(issue.severity) ||
                    !IsIssueScope(issue.scope) ||
                    string.IsNullOrWhiteSpace(issue.message) ||
                    !Fits(issue.code, 128) ||
                    !Fits(issue.severity, 16) ||
                    !Fits(issue.scope, 32) ||
                    !Fits(issue.message, MaximumMessageCharacters) ||
                    !Fits(issue.path, MaximumPathCharacters))
                {
                    error = "The pipeline inspection returned an invalid issue entry.";
                    return false;
                }
            }

            for (int index = 0; index < document.profiles.Length; index++)
            {
                if (!ValidateProfile(document.profiles[index], allowEmpty: false))
                {
                    error = "The pipeline inspection returned an invalid profile entry.";
                    return false;
                }
            }

            if (!ValidateProfile(document.selectedProfile, allowEmpty: true) ||
                !ValidateToolchain(document.toolchain) ||
                !ValidateOutput(document.output) ||
                !ValidateTransaction(document.transaction))
            {
                error = "The pipeline inspection returned an invalid nested object.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool ValidateProfile(
            DataTableLubanInspectionProfileDto profile,
            bool allowEmpty)
        {
            if (profile == null)
            {
                return false;
            }

            bool emptySelection = string.IsNullOrEmpty(profile.name) &&
                                  string.IsNullOrEmpty(profile.codeOutputPath) &&
                                  string.IsNullOrEmpty(profile.dataOutputPath) &&
                                  string.IsNullOrEmpty(profile.codeTarget) &&
                                  string.IsNullOrEmpty(profile.dataTarget) &&
                                  string.IsNullOrEmpty(profile.lineEnding);
            return (allowEmpty && emptySelection ||
                    !string.IsNullOrWhiteSpace(profile.name) &&
                    !string.IsNullOrWhiteSpace(profile.codeOutputPath) &&
                    !string.IsNullOrWhiteSpace(profile.dataOutputPath) &&
                    !string.IsNullOrWhiteSpace(profile.codeTarget) &&
                    !string.IsNullOrWhiteSpace(profile.dataTarget) &&
                    IsLineEnding(profile.lineEnding)) &&
                   Fits(profile.name, 128) &&
                   Fits(profile.codeOutputPath, MaximumPathCharacters) &&
                   Fits(profile.dataOutputPath, MaximumPathCharacters) &&
                   Fits(profile.codeTarget, 128) &&
                   Fits(profile.dataTarget, 128) &&
                   Fits(profile.lineEnding, 16);
        }

        private static bool ValidateToolchain(DataTableLubanInspectionToolchainDto value)
        {
            return value != null &&
                   IsToolchainState(value.state) &&
                   IsLubanIdentityStatus(value.lubanIdentityStatus) &&
                   IsSourceFingerprintStatus(value.sourceFingerprintStatus) &&
                   Fits(value.state, 32) &&
                   Fits(value.codegenProjectPath, MaximumPathCharacters) &&
                   Fits(value.lubanConfigurationPath, MaximumPathCharacters) &&
                   Fits(value.lubanExecutablePath, MaximumPathCharacters) &&
                   Fits(value.configuredVersion, 256) &&
                   Fits(value.configuredSha256, 128) &&
                   Fits(value.actualSha256, 128) &&
                   Fits(value.lubanIdentityStatus, 32) &&
                   Fits(value.configuredSourceFingerprint, 128) &&
                   Fits(value.actualSourceFingerprint, 128) &&
                   Fits(value.sourceFingerprintStatus, 32) &&
                   Fits(value.schemaSha256, 128);
        }

        private static bool ValidateOutput(DataTableLubanInspectionOutputDto value)
        {
            return value != null &&
                   IsOutputState(value.state) &&
                   Fits(value.state, 32) &&
                   Fits(value.receiptPath, MaximumPathCharacters) &&
                   Fits(value.generation, 256);
        }

        private static bool ValidateTransaction(DataTableLubanInspectionTransactionDto value)
        {
            return value != null &&
                   IsTransactionState(value.state) &&
                   IsJournalState(value.journalState) &&
                   Fits(value.state, 32) &&
                   Fits(value.lockPath, MaximumPathCharacters) &&
                   Fits(value.runId, 128) &&
                   Fits(value.transactionPath, MaximumPathCharacters) &&
                   Fits(value.journalState, 32);
        }

        private static bool Fits(string value, int maximumCharacters)
        {
            return value == null || value.Length <= maximumCharacters;
        }

        private static bool IsStatus(string value)
        {
            return string.Equals(value, "ready", StringComparison.Ordinal) ||
                   string.Equals(value, "blocked", StringComparison.Ordinal) ||
                   string.Equals(value, "busy", StringComparison.Ordinal) ||
                   string.Equals(value, "recoveryRequired", StringComparison.Ordinal);
        }

        private static bool IsIssueSeverity(string value)
        {
            return string.Equals(value, "info", StringComparison.Ordinal) ||
                   string.Equals(value, "warning", StringComparison.Ordinal) ||
                   string.Equals(value, "error", StringComparison.Ordinal);
        }

        private static bool IsIssueScope(string value)
        {
            return string.Equals(value, "configuration", StringComparison.Ordinal) ||
                   string.Equals(value, "toolchain", StringComparison.Ordinal) ||
                   string.Equals(value, "source", StringComparison.Ordinal) ||
                   string.Equals(value, "profile", StringComparison.Ordinal) ||
                   string.Equals(value, "output", StringComparison.Ordinal) ||
                   string.Equals(value, "transaction", StringComparison.Ordinal);
        }

        private static bool IsLineEnding(string value)
        {
            return string.Equals(value, "lf", StringComparison.Ordinal) ||
                   string.Equals(value, "crlf", StringComparison.Ordinal);
        }

        private static bool IsToolchainState(string value)
        {
            return string.Equals(value, "ready", StringComparison.Ordinal) ||
                   string.Equals(value, "blocked", StringComparison.Ordinal);
        }

        private static bool IsLubanIdentityStatus(string value)
        {
            return string.Equals(value, "approved", StringComparison.Ordinal) ||
                   string.Equals(value, "placeholder", StringComparison.Ordinal) ||
                   string.Equals(value, "missing", StringComparison.Ordinal) ||
                   string.Equals(value, "mismatch", StringComparison.Ordinal) ||
                   string.Equals(value, "invalid", StringComparison.Ordinal);
        }

        private static bool IsSourceFingerprintStatus(string value)
        {
            return string.Equals(value, "current", StringComparison.Ordinal) ||
                   string.Equals(value, "placeholder", StringComparison.Ordinal) ||
                   string.Equals(value, "mismatch", StringComparison.Ordinal) ||
                   string.Equals(value, "unavailable", StringComparison.Ordinal);
        }

        private static bool IsOutputState(string value)
        {
            return string.Equals(value, "missing", StringComparison.Ordinal) ||
                   string.Equals(value, "current", StringComparison.Ordinal) ||
                   string.Equals(value, "invalid", StringComparison.Ordinal) ||
                   string.Equals(value, "drifted", StringComparison.Ordinal) ||
                   string.Equals(value, "unavailable", StringComparison.Ordinal);
        }

        private static bool IsTransactionState(string value)
        {
            return string.Equals(value, "idle", StringComparison.Ordinal) ||
                   string.Equals(value, "active", StringComparison.Ordinal) ||
                   string.Equals(value, "recoveryRequired", StringComparison.Ordinal) ||
                   string.Equals(value, "invalid", StringComparison.Ordinal);
        }

        private static bool IsJournalState(string value)
        {
            return string.Equals(value, "none", StringComparison.Ordinal) ||
                   string.Equals(value, "prepared", StringComparison.Ordinal) ||
                   string.Equals(value, "publishing", StringComparison.Ordinal) ||
                   string.Equals(value, "committed", StringComparison.Ordinal) ||
                   string.Equals(value, "recoveryRequired", StringComparison.Ordinal) ||
                   string.Equals(value, "invalid", StringComparison.Ordinal);
        }

        private static DataTableLubanAuthoringState ParseState(string value)
        {
            if (string.Equals(value, "ready", StringComparison.Ordinal))
            {
                return DataTableLubanAuthoringState.Ready;
            }

            if (string.Equals(value, "busy", StringComparison.Ordinal))
            {
                return DataTableLubanAuthoringState.Busy;
            }

            if (string.Equals(value, "recoveryRequired", StringComparison.Ordinal))
            {
                return DataTableLubanAuthoringState.RecoveryRequired;
            }

            return DataTableLubanAuthoringState.Blocked;
        }

        private static DataTableLubanIssueSeverity ParseSeverity(string value)
        {
            if (string.Equals(value, "error", StringComparison.Ordinal))
            {
                return DataTableLubanIssueSeverity.Error;
            }

            if (string.Equals(value, "warning", StringComparison.Ordinal))
            {
                return DataTableLubanIssueSeverity.Warning;
            }

            return DataTableLubanIssueSeverity.Info;
        }

        private static DataTableLubanProfileSnapshot ProjectProfile(
            DataTableLubanInspectionProfileDto value)
        {
            value ??= new DataTableLubanInspectionProfileDto();
            return new DataTableLubanProfileSnapshot(
                value.name,
                value.selected,
                value.codeOutputPath,
                value.dataOutputPath,
                value.codeTarget,
                value.dataTarget,
                value.lineEnding);
        }

        private static DataTableLubanToolchainSnapshot ProjectToolchain(
            DataTableLubanInspectionToolchainDto value)
        {
            value ??= new DataTableLubanInspectionToolchainDto();
            return new DataTableLubanToolchainSnapshot(
                value.state,
                value.codegenProjectPath,
                value.codegenProjectExists,
                value.lubanConfigurationPath,
                value.lubanConfigurationExists,
                value.lubanExecutablePath,
                value.lubanExecutableExists,
                value.useDotNetHost,
                value.configuredVersion,
                value.configuredSha256,
                value.actualSha256,
                value.lubanIdentityStatus,
                value.configuredSourceFingerprint,
                value.actualSourceFingerprint,
                value.sourceFingerprintStatus,
                value.schemaSha256);
        }

        private static DataTableLubanTransactionSnapshot ProjectTransaction(
            DataTableLubanInspectionTransactionDto value)
        {
            value ??= new DataTableLubanInspectionTransactionDto();
            return new DataTableLubanTransactionSnapshot(
                value.state,
                value.lockPath,
                value.lockExists,
                value.runId,
                value.writerProcessId,
                value.writerProcessAlive,
                value.cancelRequested,
                value.activeLubanEvidence,
                value.transactionPath,
                value.journalExists,
                value.journalState,
                value.recoveryRequired);
        }
    }

    [Serializable]
    internal sealed class DataTableLubanInspectionDocument
    {
        public string schema = string.Empty;
        public int schemaVersion;
        public string status = string.Empty;
        public bool canGenerate;
        public bool canCheck;
        public bool canRecover;
        public string configurationPath = string.Empty;
        public string configurationSha256 = string.Empty;
        public string sourceRoot = string.Empty;
        public string selectedProfileName = string.Empty;
        public int processTimeoutSeconds;
        public DataTableLubanInspectionIssueDto[] issues = Array.Empty<DataTableLubanInspectionIssueDto>();
        public DataTableLubanInspectionProfileDto[] profiles = Array.Empty<DataTableLubanInspectionProfileDto>();
        public DataTableLubanInspectionProfileDto selectedProfile = new DataTableLubanInspectionProfileDto();
        public DataTableLubanInspectionToolchainDto toolchain = new DataTableLubanInspectionToolchainDto();
        public DataTableLubanInspectionOutputDto output = new DataTableLubanInspectionOutputDto();
        public DataTableLubanInspectionTransactionDto transaction = new DataTableLubanInspectionTransactionDto();
    }

    [Serializable]
    internal sealed class DataTableLubanInspectionIssueDto
    {
        public string code = string.Empty;
        public string severity = string.Empty;
        public string scope = string.Empty;
        public string message = string.Empty;
        public string path = string.Empty;
    }

    [Serializable]
    internal sealed class DataTableLubanInspectionProfileDto
    {
        public string name = string.Empty;
        public bool selected;
        public string codeOutputPath = string.Empty;
        public string dataOutputPath = string.Empty;
        public string codeTarget = string.Empty;
        public string dataTarget = string.Empty;
        public string lineEnding = string.Empty;
    }

    [Serializable]
    internal sealed class DataTableLubanInspectionToolchainDto
    {
        public string state = string.Empty;
        public string codegenProjectPath = string.Empty;
        public bool codegenProjectExists;
        public string lubanConfigurationPath = string.Empty;
        public bool lubanConfigurationExists;
        public string lubanExecutablePath = string.Empty;
        public bool lubanExecutableExists;
        public bool useDotNetHost;
        public string configuredVersion = string.Empty;
        public string configuredSha256 = string.Empty;
        public string actualSha256 = string.Empty;
        public string lubanIdentityStatus = string.Empty;
        public string configuredSourceFingerprint = string.Empty;
        public string actualSourceFingerprint = string.Empty;
        public string sourceFingerprintStatus = string.Empty;
        public string schemaSha256 = string.Empty;
    }

    [Serializable]
    internal sealed class DataTableLubanInspectionOutputDto
    {
        public string state = string.Empty;
        public string receiptPath = string.Empty;
        public bool receiptExists;
        public bool receiptValid;
        public string generation = string.Empty;
    }

    [Serializable]
    internal sealed class DataTableLubanInspectionTransactionDto
    {
        public string state = string.Empty;
        public string lockPath = string.Empty;
        public bool lockExists;
        public string runId = string.Empty;
        public int writerProcessId;
        public bool writerProcessAlive;
        public bool cancelRequested;
        public bool activeLubanEvidence;
        public string transactionPath = string.Empty;
        public bool journalExists;
        public string journalState = string.Empty;
        public bool recoveryRequired;
    }
}
