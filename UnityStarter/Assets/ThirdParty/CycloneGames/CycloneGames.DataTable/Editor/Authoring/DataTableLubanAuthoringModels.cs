using System;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal enum DataTableLubanAuthoringState
    {
        Unknown,
        Inspecting,
        Ready,
        Blocked,
        Busy,
        RecoveryRequired,
        Invalid,
    }

    internal enum DataTableLubanIssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    internal readonly struct DataTableLubanAuthoringIssue
    {
        internal DataTableLubanAuthoringIssue(
            string code,
            DataTableLubanIssueSeverity severity,
            string scope,
            string message,
            string path)
        {
            Code = code ?? string.Empty;
            Severity = severity;
            Scope = scope ?? string.Empty;
            Message = message ?? string.Empty;
            Path = path ?? string.Empty;
        }

        internal string Code { get; }
        internal DataTableLubanIssueSeverity Severity { get; }
        internal string Scope { get; }
        internal string Message { get; }
        internal string Path { get; }
    }

    internal readonly struct DataTableLubanProfileSnapshot
    {
        internal DataTableLubanProfileSnapshot(
            string name,
            bool selected,
            string codeOutputPath,
            string dataOutputPath,
            string codeTarget,
            string dataTarget,
            string lineEnding)
        {
            Name = name ?? string.Empty;
            Selected = selected;
            CodeOutputPath = codeOutputPath ?? string.Empty;
            DataOutputPath = dataOutputPath ?? string.Empty;
            CodeTarget = codeTarget ?? string.Empty;
            DataTarget = dataTarget ?? string.Empty;
            LineEnding = lineEnding ?? string.Empty;
        }

        internal string Name { get; }
        internal bool Selected { get; }
        internal string CodeOutputPath { get; }
        internal string DataOutputPath { get; }
        internal string CodeTarget { get; }
        internal string DataTarget { get; }
        internal string LineEnding { get; }
    }

    internal readonly struct DataTableLubanToolchainSnapshot
    {
        internal DataTableLubanToolchainSnapshot(
            string state,
            string codegenProjectPath,
            bool codegenProjectExists,
            string lubanConfigurationPath,
            bool lubanConfigurationExists,
            string lubanExecutablePath,
            bool lubanExecutableExists,
            bool useDotNetHost,
            string configuredVersion,
            string configuredSha256,
            string actualSha256,
            string lubanIdentityStatus,
            string configuredSourceFingerprint,
            string actualSourceFingerprint,
            string sourceFingerprintStatus,
            string schemaSha256)
        {
            State = state ?? string.Empty;
            CodegenProjectPath = codegenProjectPath ?? string.Empty;
            CodegenProjectExists = codegenProjectExists;
            LubanConfigurationPath = lubanConfigurationPath ?? string.Empty;
            LubanConfigurationExists = lubanConfigurationExists;
            LubanExecutablePath = lubanExecutablePath ?? string.Empty;
            LubanExecutableExists = lubanExecutableExists;
            UseDotNetHost = useDotNetHost;
            ConfiguredVersion = configuredVersion ?? string.Empty;
            ConfiguredSha256 = configuredSha256 ?? string.Empty;
            ActualSha256 = actualSha256 ?? string.Empty;
            LubanIdentityStatus = lubanIdentityStatus ?? string.Empty;
            ConfiguredSourceFingerprint = configuredSourceFingerprint ?? string.Empty;
            ActualSourceFingerprint = actualSourceFingerprint ?? string.Empty;
            SourceFingerprintStatus = sourceFingerprintStatus ?? string.Empty;
            SchemaSha256 = schemaSha256 ?? string.Empty;
        }

        internal string State { get; }
        internal string CodegenProjectPath { get; }
        internal bool CodegenProjectExists { get; }
        internal string LubanConfigurationPath { get; }
        internal bool LubanConfigurationExists { get; }
        internal string LubanExecutablePath { get; }
        internal bool LubanExecutableExists { get; }
        internal bool UseDotNetHost { get; }
        internal string ConfiguredVersion { get; }
        internal string ConfiguredSha256 { get; }
        internal string ActualSha256 { get; }
        internal string LubanIdentityStatus { get; }
        internal string ConfiguredSourceFingerprint { get; }
        internal string ActualSourceFingerprint { get; }
        internal string SourceFingerprintStatus { get; }
        internal string SchemaSha256 { get; }
    }

    internal readonly struct DataTableLubanOutputSnapshot
    {
        internal DataTableLubanOutputSnapshot(
            string state,
            string receiptPath,
            bool receiptExists,
            bool receiptValid,
            string generation)
        {
            State = state ?? string.Empty;
            ReceiptPath = receiptPath ?? string.Empty;
            ReceiptExists = receiptExists;
            ReceiptValid = receiptValid;
            Generation = generation ?? string.Empty;
        }

        internal string State { get; }
        internal string ReceiptPath { get; }
        internal bool ReceiptExists { get; }
        internal bool ReceiptValid { get; }
        internal string Generation { get; }
    }

    internal readonly struct DataTableLubanTransactionSnapshot
    {
        internal DataTableLubanTransactionSnapshot(
            string state,
            string lockPath,
            bool lockExists,
            string runId,
            int writerProcessId,
            bool writerProcessAlive,
            bool cancelRequested,
            bool activeLubanEvidence,
            string transactionPath,
            bool journalExists,
            string journalState,
            bool recoveryRequired)
        {
            State = state ?? string.Empty;
            LockPath = lockPath ?? string.Empty;
            LockExists = lockExists;
            RunId = runId ?? string.Empty;
            WriterProcessId = writerProcessId;
            WriterProcessAlive = writerProcessAlive;
            CancelRequested = cancelRequested;
            ActiveLubanEvidence = activeLubanEvidence;
            TransactionPath = transactionPath ?? string.Empty;
            JournalExists = journalExists;
            JournalState = journalState ?? string.Empty;
            RecoveryRequired = recoveryRequired;
        }

        internal string State { get; }
        internal string LockPath { get; }
        internal bool LockExists { get; }
        internal string RunId { get; }
        internal int WriterProcessId { get; }
        internal bool WriterProcessAlive { get; }
        internal bool CancelRequested { get; }
        internal bool ActiveLubanEvidence { get; }
        internal string TransactionPath { get; }
        internal bool JournalExists { get; }
        internal string JournalState { get; }
        internal bool RecoveryRequired { get; }
    }

    internal sealed class DataTableLubanAuthoringSnapshot
    {
        internal static readonly DataTableLubanAuthoringSnapshot Empty = new DataTableLubanAuthoringSnapshot(
            DataTableLubanAuthoringState.Unknown,
            false,
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            Array.Empty<DataTableLubanAuthoringIssue>(),
            Array.Empty<DataTableLubanProfileSnapshot>(),
            default,
            default,
            default,
            default,
            string.Empty,
            0,
            false,
            false);

        internal DataTableLubanAuthoringSnapshot(
            DataTableLubanAuthoringState state,
            bool canGenerate,
            bool canCheck,
            bool canRecover,
            string settingsAssetPath,
            string configurationPath,
            string configurationSha256,
            string sourceRoot,
            string selectedProfileName,
            int processTimeoutSeconds,
            DataTableLubanAuthoringIssue[] issues,
            DataTableLubanProfileSnapshot[] profiles,
            DataTableLubanProfileSnapshot selectedProfile,
            DataTableLubanToolchainSnapshot toolchain,
            DataTableLubanOutputSnapshot output,
            DataTableLubanTransactionSnapshot transaction,
            string inspectionError,
            int settingsAssetCount,
            bool settingsDirty,
            bool isInspectionPending)
        {
            State = state;
            CanGenerate = canGenerate;
            CanCheck = canCheck;
            CanRecover = canRecover;
            SettingsAssetPath = settingsAssetPath ?? string.Empty;
            ConfigurationPath = configurationPath ?? string.Empty;
            ConfigurationSha256 = configurationSha256 ?? string.Empty;
            SourceRoot = sourceRoot ?? string.Empty;
            SelectedProfileName = selectedProfileName ?? string.Empty;
            ProcessTimeoutSeconds = processTimeoutSeconds;
            Issues = issues ?? Array.Empty<DataTableLubanAuthoringIssue>();
            Profiles = profiles ?? Array.Empty<DataTableLubanProfileSnapshot>();
            SelectedProfile = selectedProfile;
            Toolchain = toolchain;
            Output = output;
            Transaction = transaction;
            InspectionError = inspectionError ?? string.Empty;
            SettingsAssetCount = settingsAssetCount;
            SettingsDirty = settingsDirty;
            IsInspectionPending = isInspectionPending;
        }

        internal DataTableLubanAuthoringState State { get; }
        internal bool CanGenerate { get; }
        internal bool CanCheck { get; }
        internal bool CanRecover { get; }
        internal string SettingsAssetPath { get; }
        internal string ConfigurationPath { get; }
        internal string ConfigurationSha256 { get; }
        internal string SourceRoot { get; }
        internal string SelectedProfileName { get; }
        internal int ProcessTimeoutSeconds { get; }
        internal DataTableLubanAuthoringIssue[] Issues { get; }
        internal DataTableLubanProfileSnapshot[] Profiles { get; }
        internal DataTableLubanProfileSnapshot SelectedProfile { get; }
        internal DataTableLubanToolchainSnapshot Toolchain { get; }
        internal DataTableLubanOutputSnapshot Output { get; }
        internal DataTableLubanTransactionSnapshot Transaction { get; }
        internal string InspectionError { get; }
        internal int SettingsAssetCount { get; }
        internal bool SettingsDirty { get; }
        internal bool IsInspectionPending { get; }

        internal string StatusLabel
        {
            get
            {
                switch (State)
                {
                    case DataTableLubanAuthoringState.Inspecting:
                        return "INSPECTING";
                    case DataTableLubanAuthoringState.Ready:
                        return "READY";
                    case DataTableLubanAuthoringState.Blocked:
                        return "SETUP REQUIRED";
                    case DataTableLubanAuthoringState.Busy:
                        return "RUNNING";
                    case DataTableLubanAuthoringState.RecoveryRequired:
                        return "RECOVERY REQUIRED";
                    case DataTableLubanAuthoringState.Invalid:
                        return "INVALID";
                    default:
                        return "UNKNOWN";
                }
            }
        }
    }
}
