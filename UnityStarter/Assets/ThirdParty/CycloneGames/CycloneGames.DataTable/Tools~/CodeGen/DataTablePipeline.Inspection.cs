using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private const string InspectionSchema = "CycloneGames.DataTable.PipelineInspection";
            private const int InspectionSchemaVersion = 1;
            private const int InspectionMaximumJsonBytes = 8 * 1024 * 1024;
            private const int InspectionMaximumIssues = 256;
            private const int InspectionMaximumIssueCharacters = 4096;

            private sealed class PipelineInspectionSnapshot
            {
                public string Schema { get; set; } = InspectionSchema;
                public int SchemaVersion { get; set; } = InspectionSchemaVersion;
                public string Status { get; set; } = "blocked";
                public bool CanGenerate { get; set; }
                public bool CanCheck { get; set; }
                public bool CanRecover { get; set; }
                public string ConfigurationPath { get; set; } = string.Empty;
                public string ConfigurationSha256 { get; set; } = string.Empty;
                public string SourceRoot { get; set; } = string.Empty;
                public string SelectedProfileName { get; set; } = string.Empty;
                public int ProcessTimeoutSeconds { get; set; }
                public PipelineInspectionIssue[] Issues { get; set; } = Array.Empty<PipelineInspectionIssue>();
                public PipelineInspectionProfile[] Profiles { get; set; } = Array.Empty<PipelineInspectionProfile>();
                public PipelineInspectionProfile SelectedProfile { get; set; } = new PipelineInspectionProfile();
                public PipelineInspectionToolchain Toolchain { get; set; } = new PipelineInspectionToolchain();
                public PipelineInspectionOutput Output { get; set; } = new PipelineInspectionOutput();
                public PipelineInspectionTransaction Transaction { get; set; } = new PipelineInspectionTransaction();
            }

            private sealed class PipelineInspectionIssue
            {
                public string Code { get; set; } = string.Empty;
                public string Severity { get; set; } = string.Empty;
                public string Scope { get; set; } = string.Empty;
                public string Message { get; set; } = string.Empty;
                public string Path { get; set; } = string.Empty;
            }

            private sealed class PipelineInspectionProfile
            {
                public string Name { get; set; } = string.Empty;
                public bool Selected { get; set; }
                public string CodeOutputPath { get; set; } = string.Empty;
                public string DataOutputPath { get; set; } = string.Empty;
                public string CodeTarget { get; set; } = string.Empty;
                public string DataTarget { get; set; } = string.Empty;
                public string LineEnding { get; set; } = string.Empty;
            }

            private sealed class PipelineInspectionToolchain
            {
                public string State { get; set; } = "blocked";
                public string CodegenProjectPath { get; set; } = string.Empty;
                public bool CodegenProjectExists { get; set; }
                public string LubanConfigurationPath { get; set; } = string.Empty;
                public bool LubanConfigurationExists { get; set; }
                public string LubanExecutablePath { get; set; } = string.Empty;
                public bool LubanExecutableExists { get; set; }
                public bool UseDotNetHost { get; set; }
                public string ConfiguredVersion { get; set; } = string.Empty;
                public string ConfiguredSha256 { get; set; } = string.Empty;
                public string ActualSha256 { get; set; } = string.Empty;
                public string LubanIdentityStatus { get; set; } = "invalid";
                public string ConfiguredSourceFingerprint { get; set; } = string.Empty;
                public string ActualSourceFingerprint { get; set; } = string.Empty;
                public string SourceFingerprintStatus { get; set; } = "unavailable";
                public string SchemaSha256 { get; set; } = string.Empty;
            }

            private sealed class PipelineInspectionOutput
            {
                public string State { get; set; } = "unavailable";
                public string ReceiptPath { get; set; } = string.Empty;
                public bool ReceiptExists { get; set; }
                public bool ReceiptValid { get; set; }
                public string Generation { get; set; } = string.Empty;
            }

            private sealed class PipelineInspectionTransaction
            {
                public string State { get; set; } = "idle";
                public string LockPath { get; set; } = string.Empty;
                public bool LockExists { get; set; }
                public string RunId { get; set; } = string.Empty;
                public int WriterProcessId { get; set; }
                public bool WriterProcessAlive { get; set; }
                public bool CancelRequested { get; set; }
                public bool ActiveLubanEvidence { get; set; }
                public string TransactionPath { get; set; } = string.Empty;
                public bool JournalExists { get; set; }
                public string JournalState { get; set; } = "none";
                public bool RecoveryRequired { get; set; }
            }

            private sealed class InspectionIssueCollector
            {
                private readonly List<PipelineInspectionIssue> _issues =
                    new List<PipelineInspectionIssue>(32);
                private bool _truncated;

                public bool HasErrors => _issues.Any(static issue => issue.Severity == "error");

                public PipelineInspectionIssue[] ToArray()
                {
                    return _issues.ToArray();
                }

                public void Add(
                    string code,
                    string severity,
                    string scope,
                    string message,
                    string path = "")
                {
                    if (_truncated)
                    {
                        return;
                    }

                    if (_issues.Count >= InspectionMaximumIssues - 1)
                    {
                        _truncated = true;
                        _issues.Add(new PipelineInspectionIssue
                        {
                            Code = "INSPECTION_ISSUES_TRUNCATED",
                            Severity = "warning",
                            Scope = "configuration",
                            Message = "Inspection stopped collecting issues at its configured bound.",
                            Path = string.Empty,
                        });
                        return;
                    }

                    _issues.Add(new PipelineInspectionIssue
                    {
                        Code = BoundInspectionText(code),
                        Severity = BoundInspectionText(severity),
                        Scope = BoundInspectionText(scope),
                        Message = BoundInspectionText(message),
                        Path = BoundInspectionText(path),
                    });
                }
            }

            private static int Inspect(
                PipelineConfiguration configuration,
                string selectedProfileName,
                string format)
            {
                if (!string.Equals(format, "json", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Unsupported inspection format: " + format);
                }

                PipelineInspectionSnapshot snapshot = BuildInspectionSnapshot(
                    configuration,
                    selectedProfileName);
                string json = SerializeInspectionSnapshot(snapshot);
                Console.Out.Write(json);
                return 0;
            }

            private static string SerializeInspectionSnapshot(PipelineInspectionSnapshot snapshot)
            {
                string json = JsonSerializer.Serialize(snapshot, PipelineJsonOptions) + "\n";
                if (Encoding.UTF8.GetByteCount(json) > InspectionMaximumJsonBytes)
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection JSON exceeds its 8 MiB output bound.");
                }

                return json;
            }

            private static PipelineInspectionSnapshot BuildInspectionSnapshot(
                PipelineConfiguration configuration,
                string selectedProfileName)
            {
                var issues = new InspectionIssueCollector();
                PipelineInspectionProfile[] profiles = configuration.Profiles.Values
                    .OrderBy(static profile => profile.Name, StringComparer.Ordinal)
                    .Select(profile => CreateInspectionProfile(
                        profile,
                        string.Equals(
                            profile.Name,
                            selectedProfileName,
                            StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                PipelineProfile? selectedProfile = configuration.Profiles.TryGetValue(
                    selectedProfileName,
                    out PipelineProfile? selected)
                    ? selected
                    : null;
                PipelineInspectionProfile selectedProfileSnapshot = selectedProfile == null
                    ? new PipelineInspectionProfile()
                    : CreateInspectionProfile(selectedProfile, selected: true);
                if (selectedProfile == null)
                {
                    issues.Add(
                        "PROFILE_NOT_FOUND",
                        "error",
                        "profile",
                        "The selected pipeline profile does not exist: " + selectedProfileName,
                        configuration.ConfigurationPath);
                }

                PipelineInspectionTransaction transaction = InspectTransaction(
                    configuration,
                    issues,
                    out bool canRecover);
                bool transactionIdle = transaction.State == "idle";
                PipelineInspectionToolchain toolchain = InspectToolchain(
                    configuration,
                    issues,
                    includeDeepValidation: transactionIdle,
                    out PipelineIdentity? identity);
                PipelineInspectionOutput output = transactionIdle
                    ? InspectOutput(selectedProfile, identity, issues)
                    : CreateDeferredInspectionOutput(selectedProfile, issues);

                bool outputCanGenerate = output.State == "missing" || output.State == "current";
                bool canGenerate = selectedProfile != null && identity != null && transactionIdle &&
                                   outputCanGenerate && !issues.HasErrors;
                bool canCheck = selectedProfile != null && identity != null && transactionIdle &&
                                output.State == "current" && !issues.HasErrors;
                string status = transaction.State == "active"
                    ? "busy"
                    : transaction.RecoveryRequired
                        ? "recoveryRequired"
                        : issues.HasErrors
                            ? "blocked"
                            : "ready";
                var snapshot = new PipelineInspectionSnapshot
                {
                    Status = status,
                    CanGenerate = canGenerate,
                    CanCheck = canCheck,
                    CanRecover = canRecover,
                    ConfigurationPath = configuration.ConfigurationPath,
                    ConfigurationSha256 = configuration.ConfigurationSha256,
                    SourceRoot = configuration.SourceRoot,
                    SelectedProfileName = selectedProfileName,
                    ProcessTimeoutSeconds = configuration.ProcessTimeoutSeconds,
                    Profiles = profiles,
                    SelectedProfile = selectedProfileSnapshot,
                    Toolchain = toolchain,
                    Output = output,
                    Transaction = transaction,
                };
                snapshot.Issues = issues.ToArray();
                return snapshot;
            }

            private static PipelineInspectionProfile CreateInspectionProfile(
                PipelineProfile profile,
                bool selected)
            {
                return new PipelineInspectionProfile
                {
                    Name = profile.Name,
                    Selected = selected,
                    CodeOutputPath = profile.CodeOutputRoot,
                    DataOutputPath = profile.DataOutputRoot,
                    CodeTarget = profile.CodeTarget,
                    DataTarget = profile.DataTarget,
                    LineEnding = profile.LineEnding,
                };
            }

            private static PipelineInspectionToolchain InspectToolchain(
                PipelineConfiguration configuration,
                InspectionIssueCollector issues,
                bool includeDeepValidation,
                out PipelineIdentity? identity)
            {
                identity = null;
                var result = new PipelineInspectionToolchain
                {
                    CodegenProjectPath = configuration.CodegenProjectPath,
                    CodegenProjectExists = File.Exists(configuration.CodegenProjectPath),
                    LubanConfigurationPath = configuration.LubanConfigurationPath,
                    LubanConfigurationExists = File.Exists(configuration.LubanConfigurationPath),
                    ConfiguredVersion = configuration.LubanVersion,
                    ConfiguredSourceFingerprint = configuration.SourceFingerprint,
                };
                if (!result.CodegenProjectExists)
                {
                    issues.Add(
                        "CODEGEN_PROJECT_MISSING",
                        "error",
                        "toolchain",
                        "The configured CodeGen project is missing.",
                        configuration.CodegenProjectPath);
                }

                if (!result.LubanConfigurationExists)
                {
                    issues.Add(
                        "LUBAN_CONFIGURATION_MISSING",
                        "error",
                        "source",
                        "The Luban configuration file is missing.",
                        configuration.LubanConfigurationPath);
                }

                if (configuration.CustomTemplateRoot.Length != 0 &&
                    !Directory.Exists(configuration.CustomTemplateRoot))
                {
                    issues.Add(
                        "CUSTOM_TEMPLATE_ROOT_MISSING",
                        "error",
                        "source",
                        "The configured custom-template directory is missing.",
                        configuration.CustomTemplateRoot);
                }

                InspectBridgeFiles(configuration, issues);
                bool workbooksReady = InspectRequiredWorkbooks(configuration, issues);

                string executablePath = configuration.LubanPath;
                string expectedHash = configuration.LubanSha256;
                bool useDotNetHost = true;
                if (OperatingSystem.IsWindows() &&
                    configuration.WindowsLubanPath.Length != 0 &&
                    File.Exists(configuration.WindowsLubanPath))
                {
                    executablePath = configuration.WindowsLubanPath;
                    expectedHash = configuration.WindowsLubanSha256;
                    useDotNetHost = false;
                }

                result.LubanExecutablePath = executablePath;
                result.UseDotNetHost = useDotNetHost;
                result.ConfiguredSha256 = expectedHash;
                bool executablePathValid = TryValidateInspectionPath(
                    executablePath,
                    configuration.RepositoryRoot,
                    "Luban executable",
                    issues,
                    "LUBAN_EXECUTABLE_PATH_INVALID",
                    "toolchain");
                result.LubanExecutableExists = executablePathValid && File.Exists(executablePath);
                bool identityPlaceholder = IsPlaceholder(configuration.LubanVersion) || !IsSha256(expectedHash);
                if (identityPlaceholder)
                {
                    issues.Add(
                        "LUBAN_IDENTITY_PLACEHOLDER",
                        "error",
                        "toolchain",
                        "The Luban version or SHA-256 identity has not been explicitly approved.",
                        configuration.ConfigurationPath);
                }

                if (!result.LubanExecutableExists)
                {
                    result.LubanIdentityStatus = "missing";
                    issues.Add(
                        "LUBAN_EXECUTABLE_MISSING",
                        "error",
                        "toolchain",
                        "The selected Luban executable is missing.",
                        executablePath);
                }
                else if (identityPlaceholder)
                {
                    result.LubanIdentityStatus = "placeholder";
                }

                bool configuredFingerprintApproved = IsSha256(configuration.SourceFingerprint) &&
                                                     !IsPlaceholder(configuration.SourceFingerprint);
                if (!configuredFingerprintApproved)
                {
                    result.SourceFingerprintStatus = "placeholder";
                    issues.Add(
                        "SOURCE_FINGERPRINT_PLACEHOLDER",
                        "error",
                        "source",
                        "The source fingerprint has not been explicitly approved.",
                        configuration.ConfigurationPath);
                }

                if (!includeDeepValidation)
                {
                    issues.Add(
                        "TOOLCHAIN_DEEP_VALIDATION_DEFERRED",
                        "info",
                        "transaction",
                        "Toolchain hashes and the bounded source fingerprint were not computed while transaction state is non-idle.",
                        configuration.LockDirectory);
                    return result;
                }

                if (result.LubanExecutableExists)
                {
                    try
                    {
                        result.ActualSha256 = ComputeFileSha256(executablePath);
                        if (identityPlaceholder)
                        {
                            result.LubanIdentityStatus = "placeholder";
                        }
                        else if (!string.Equals(
                                     result.ActualSha256,
                                     expectedHash,
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            result.LubanIdentityStatus = "mismatch";
                            issues.Add(
                                "LUBAN_HASH_MISMATCH",
                                "error",
                                "toolchain",
                                "The selected Luban executable does not match its configured SHA-256.",
                                executablePath);
                        }
                        else
                        {
                            result.LubanIdentityStatus = "approved";
                        }
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        result.LubanIdentityStatus = "invalid";
                        issues.Add(
                            "LUBAN_HASH_UNAVAILABLE",
                            "error",
                            "toolchain",
                            exception.Message,
                            executablePath);
                    }
                }

                try
                {
                    result.ActualSourceFingerprint = ComputeSourceFingerprint(
                        configuration,
                        writeSummary: false);
                    if (configuredFingerprintApproved)
                    {
                        if (string.Equals(
                                result.ActualSourceFingerprint,
                                configuration.SourceFingerprint,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            result.SourceFingerprintStatus = "current";
                        }
                        else
                        {
                            result.SourceFingerprintStatus = "mismatch";
                            issues.Add(
                                "SOURCE_FINGERPRINT_MISMATCH",
                                "error",
                                "source",
                                "The current bounded source fingerprint differs from the approved value.",
                                configuration.ConfigurationPath);
                        }
                    }
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    if (configuredFingerprintApproved)
                    {
                        result.SourceFingerprintStatus = "unavailable";
                    }

                    issues.Add(
                        "SOURCE_FINGERPRINT_UNAVAILABLE",
                        "error",
                        "source",
                        exception.Message,
                        configuration.SourceRoot);
                }

                try
                {
                    result.SchemaSha256 = ComputeSchemaHash(configuration);
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    issues.Add(
                        "SCHEMA_HASH_UNAVAILABLE",
                        "error",
                        "source",
                        exception.Message,
                        configuration.SourceRoot);
                }

                string toolHash = string.Empty;
                try
                {
                    toolHash = ComputeToolSourceHash(configuration);
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    issues.Add(
                        "TOOL_HASH_UNAVAILABLE",
                        "error",
                        "toolchain",
                        exception.Message,
                        configuration.CodegenProjectPath);
                }

                bool identityReady = result.CodegenProjectExists && result.LubanConfigurationExists &&
                                     workbooksReady && result.LubanIdentityStatus == "approved" &&
                                     result.SourceFingerprintStatus == "current" &&
                                     result.SchemaSha256.Length != 0 && toolHash.Length != 0 &&
                                     (configuration.CustomTemplateRoot.Length == 0 ||
                                      Directory.Exists(configuration.CustomTemplateRoot));
                if (identityReady)
                {
                    identity = new PipelineIdentity(
                        executablePath,
                        useDotNetHost,
                        result.ActualSha256,
                        result.ActualSourceFingerprint,
                        result.SchemaSha256,
                        toolHash);
                    result.State = "ready";
                }

                return result;
            }

            private static bool InspectRequiredWorkbooks(
                PipelineConfiguration configuration,
                InspectionIssueCollector issues)
            {
                bool ready = true;
                foreach (string workbook in new[] { "__tables__.xlsx", "__beans__.xlsx", "__enums__.xlsx" })
                {
                    string path = Path.Combine(configuration.SourceRoot, "Datas", workbook);
                    if (!File.Exists(path))
                    {
                        ready = false;
                        issues.Add(
                            "SCHEMA_WORKBOOK_MISSING",
                            "error",
                            "source",
                            "A required Luban schema workbook is missing: " + workbook,
                            path);
                        continue;
                    }

                    try
                    {
                        AssertNotReparsePoint(path, "schema workbook");
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        ready = false;
                        issues.Add(
                            "SCHEMA_WORKBOOK_INVALID",
                            "error",
                            "source",
                            exception.Message,
                            path);
                    }
                }

                return ready;
            }

            private static void InspectBridgeFiles(
                PipelineConfiguration configuration,
                InspectionIssueCollector issues)
            {
                if (configuration.BridgeFiles.Length == 0 ||
                    !Directory.Exists(configuration.CustomTemplateRoot))
                {
                    return;
                }

                foreach (string relativePath in configuration.BridgeFiles)
                {
                    string path = ResolveRelativePath(
                        configuration.CustomTemplateRoot,
                        relativePath,
                        "bridge source");
                    if (!File.Exists(path))
                    {
                        issues.Add(
                            "BRIDGE_FILE_MISSING",
                            "error",
                            "source",
                            "A configured bridge file is missing.",
                            path);
                        continue;
                    }

                    try
                    {
                        AssertPhysicalContainedPath(
                            path,
                            configuration.CustomTemplateRoot,
                            "bridge source",
                            mustExist: true);
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        issues.Add(
                            "BRIDGE_FILE_INVALID",
                            "error",
                            "source",
                            exception.Message,
                            path);
                    }
                }
            }

            private static bool TryValidateInspectionPath(
                string path,
                string approvedRoot,
                string description,
                InspectionIssueCollector issues,
                string issueCode,
                string issueScope)
            {
                try
                {
                    AssertPhysicalContainedPath(
                        path,
                        approvedRoot,
                        description,
                        mustExist: false);
                    return true;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    issues.Add(issueCode, "error", issueScope, exception.Message, path);
                    return false;
                }
            }

            private static PipelineInspectionOutput InspectOutput(
                PipelineProfile? profile,
                PipelineIdentity? identity,
                InspectionIssueCollector issues)
            {
                var result = new PipelineInspectionOutput();
                if (profile == null)
                {
                    return result;
                }

                result.ReceiptPath = GetReceiptPath(profile);
                result.ReceiptExists = File.Exists(result.ReceiptPath);
                if (!result.ReceiptExists)
                {
                    try
                    {
                        EnsureUnreceiptedRootsEmpty(profile);
                        result.State = "missing";
                        issues.Add(
                            "OUTPUT_NOT_GENERATED",
                            "info",
                            "output",
                            "The selected profile has no live generation receipt yet.",
                            result.ReceiptPath);
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        result.State = "invalid";
                        issues.Add(
                            "OUTPUT_UNRECEIPTED_CONTENT",
                            "error",
                            "output",
                            exception.Message,
                            result.ReceiptPath);
                    }

                    return result;
                }

                GenerationReceipt receipt;
                try
                {
                    receipt = ReadAndValidateLiveReceipt(profile);
                    result.ReceiptValid = true;
                    result.Generation = receipt.Generation;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    result.State = "invalid";
                    issues.Add(
                        "OUTPUT_RECEIPT_INVALID",
                        "error",
                        "output",
                        exception.Message,
                        result.ReceiptPath);
                    return result;
                }

                try
                {
                    ValidateLiveOutputs(profile, receipt, identity: null, requireCurrentIdentity: false);
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    result.State = "drifted";
                    issues.Add(
                        "OUTPUT_DRIFT",
                        "error",
                        "output",
                        exception.Message,
                        result.ReceiptPath);
                    return result;
                }

                if (identity == null)
                {
                    result.State = "unavailable";
                    return result;
                }

                try
                {
                    ValidateLiveOutputs(profile, receipt, identity, requireCurrentIdentity: true);
                    result.State = "current";
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    result.State = "drifted";
                    issues.Add(
                        "OUTPUT_IDENTITY_DRIFT",
                        "error",
                        "output",
                        exception.Message,
                        result.ReceiptPath);
                }

                return result;
            }

            private static PipelineInspectionOutput CreateDeferredInspectionOutput(
                PipelineProfile? profile,
                InspectionIssueCollector issues)
            {
                var result = new PipelineInspectionOutput();
                if (profile == null)
                {
                    return result;
                }

                result.ReceiptPath = GetReceiptPath(profile);
                result.ReceiptExists = File.Exists(result.ReceiptPath);
                issues.Add(
                    "OUTPUT_VALIDATION_DEFERRED",
                    "info",
                    "transaction",
                    "Live output validation was not performed while transaction state is non-idle.",
                    result.ReceiptPath);
                return result;
            }

            private static PipelineInspectionTransaction InspectTransaction(
                PipelineConfiguration configuration,
                InspectionIssueCollector issues,
                out bool canRecover)
            {
                canRecover = false;
                var result = new PipelineInspectionTransaction
                {
                    LockPath = configuration.LockDirectory,
                };
                bool lockDirectoryExists = Directory.Exists(configuration.LockDirectory);
                bool lockFileExists = File.Exists(configuration.LockDirectory);
                result.LockExists = lockDirectoryExists || lockFileExists;
                if (!result.LockExists)
                {
                    if (TryFindUnexpectedTransaction(
                            configuration,
                            expectedRunId: string.Empty,
                            out string unexpected,
                            out string inspectionError))
                    {
                        result.State = "invalid";
                        result.RecoveryRequired = true;
                        issues.Add(
                            "ORPHANED_TRANSACTION",
                            "error",
                            "transaction",
                            "Transaction state exists without its authoritative writer lock.",
                            unexpected);
                    }
                    else if (inspectionError.Length != 0)
                    {
                        result.State = "invalid";
                        result.RecoveryRequired = true;
                        issues.Add(
                            "TRANSACTION_STATE_INVALID",
                            "error",
                            "transaction",
                            inspectionError,
                            configuration.TransactionsRoot);
                    }

                    return result;
                }

                if (!lockDirectoryExists)
                {
                    result.State = "invalid";
                    result.RecoveryRequired = true;
                    issues.Add(
                        "WRITER_LOCK_INVALID",
                        "error",
                        "transaction",
                        "The writer-lock path is not a physical directory.",
                        configuration.LockDirectory);
                    return result;
                }

                string ownerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                WriterLockOwner owner;
                try
                {
                    AssertPhysicalContainedPath(
                        configuration.LockDirectory,
                        configuration.SourceRoot,
                        "inspection writer lock",
                        mustExist: true);
                    owner = ReadWriterLockOwner(ownerPath);
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    result.State = "invalid";
                    result.RecoveryRequired = true;
                    issues.Add(
                        "WRITER_LOCK_INVALID",
                        "error",
                        "transaction",
                        exception.Message,
                        configuration.LockDirectory);
                    return result;
                }

                result.RunId = owner.RunId;
                result.WriterProcessId = owner.ProcessIdentity.ProcessId;
                result.TransactionPath = Path.Combine(configuration.TransactionsRoot, owner.RunId);
                if (!TryGetRecordedProcessAlive(
                        owner.ProcessIdentity,
                        out bool writerAlive,
                        out string processInspectionError))
                {
                    result.State = "invalid";
                    result.RecoveryRequired = true;
                    issues.Add(
                        "WRITER_PROCESS_STATE_UNKNOWN",
                        "error",
                        "transaction",
                        processInspectionError,
                        ownerPath);
                    return result;
                }

                result.WriterProcessAlive = writerAlive;
                if (writerAlive)
                {
                    result.CancelRequested = File.Exists(
                        Path.Combine(configuration.LockDirectory, CancelRequestFileName));
                    result.ActiveLubanEvidence = HasActiveLubanEvidence(configuration.LockDirectory);
                    result.State = "active";
                    return result;
                }

                try
                {
                    WriterLockOwner validatedOwner = ValidateRecoveryOwnership(
                        configuration,
                        owner.RunId,
                        ownerPath);
                    if (!string.Equals(
                            validatedOwner.Content,
                            owner.Content,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Writer-lock ownership changed during inspection.");
                    }

                    owner = validatedOwner;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    result.State = "invalid";
                    result.RecoveryRequired = true;
                    issues.Add(
                        "WRITER_LOCK_INVALID",
                        "error",
                        "transaction",
                        exception.Message,
                        configuration.LockDirectory);
                    return result;
                }

                result.CancelRequested = File.Exists(
                    Path.Combine(configuration.LockDirectory, CancelRequestFileName));
                result.ActiveLubanEvidence = HasActiveLubanEvidence(configuration.LockDirectory);
                result.RecoveryRequired = true;
                bool transactionExists = Directory.Exists(result.TransactionPath);
                bool transactionInvalid = false;
                if (TryFindUnexpectedTransaction(
                        configuration,
                        owner.RunId,
                        out string unexpectedTransaction,
                        out string transactionInspectionError))
                {
                    transactionInvalid = true;
                    issues.Add(
                        "UNEXPECTED_TRANSACTION",
                        "error",
                        "transaction",
                        "A transaction not owned by the current writer lock remains.",
                        unexpectedTransaction);
                }
                else if (transactionInspectionError.Length != 0)
                {
                    transactionInvalid = true;
                    issues.Add(
                        "TRANSACTION_STATE_INVALID",
                        "error",
                        "transaction",
                        transactionInspectionError,
                        configuration.TransactionsRoot);
                }

                if (!transactionExists)
                {
                    result.State = "invalid";
                    issues.Add(
                        "RECOVERY_TRANSACTION_MISSING",
                        "error",
                        "transaction",
                        "The retained writer lock has no matching transaction directory.",
                        result.TransactionPath);
                    return result;
                }

                bool journalValid = InspectJournal(
                    configuration,
                    owner.RunId,
                    result,
                    issues,
                    out TransactionJournal? journal);
                result.State = transactionInvalid || !journalValid
                    ? "invalid"
                    : "recoveryRequired";

                bool recoveryProcessesStopped;
                try
                {
                    AssertRecoveryProcessesStopped(configuration, owner);
                    recoveryProcessesStopped = true;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    recoveryProcessesStopped = false;
                    issues.Add(
                        "RECOVERY_PROCESS_ACTIVE_OR_UNCERTAIN",
                        "error",
                        "transaction",
                        exception.Message,
                        configuration.LockDirectory);
                }

                bool journalBindingValid = journalValid;
                if (journal != null && journalValid)
                {
                    try
                    {
                        PipelineProfile journalProfile = configuration.GetProfile(journal.Profile);
                        ValidateJournalBinding(journal, configuration, journalProfile);
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        journalBindingValid = false;
                        issues.Add(
                            "RECOVERY_JOURNAL_BINDING_INVALID",
                            "error",
                            "transaction",
                            exception.Message,
                            Path.Combine(result.TransactionPath, "journal.json"));
                    }
                }

                canRecover = !transactionInvalid && recoveryProcessesStopped && journalBindingValid;
                return result;
            }

            private static bool InspectJournal(
                PipelineConfiguration configuration,
                string runId,
                PipelineInspectionTransaction result,
                InspectionIssueCollector issues,
                out TransactionJournal? journal)
            {
                journal = null;
                string journalPath = Path.Combine(
                    configuration.TransactionsRoot,
                    runId,
                    "journal.json");
                result.JournalExists = File.Exists(journalPath);
                if (!result.JournalExists)
                {
                    return true;
                }

                try
                {
                    journal = ReadState<TransactionJournal>(journalPath, "transaction journal");
                    ValidateJournal(journal, runId);
                    result.JournalState = journal.State switch
                    {
                        nameof(JournalState.Prepared) => "prepared",
                        nameof(JournalState.Publishing) => "publishing",
                        nameof(JournalState.Committed) => "committed",
                        nameof(JournalState.RecoveryRequired) => "recoveryRequired",
                        _ => "invalid",
                    };
                    return result.JournalState != "invalid";
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    result.JournalState = "invalid";
                    issues.Add(
                        "RECOVERY_JOURNAL_INVALID",
                        "error",
                        "transaction",
                        exception.Message,
                        journalPath);
                    return false;
                }
            }

            private static bool TryFindUnexpectedTransaction(
                PipelineConfiguration configuration,
                string expectedRunId,
                out string unexpected,
                out string error)
            {
                unexpected = string.Empty;
                error = string.Empty;
                if (File.Exists(configuration.TransactionsRoot))
                {
                    error = "The transaction state root is occupied by a file instead of a directory.";
                    return false;
                }

                if (!Directory.Exists(configuration.TransactionsRoot))
                {
                    return false;
                }

                try
                {
                    AssertPhysicalContainedPath(
                        configuration.TransactionsRoot,
                        configuration.SourceRoot,
                        "inspection transaction root",
                        mustExist: true);
                    foreach (string entry in Directory.EnumerateFileSystemEntries(configuration.TransactionsRoot))
                    {
                        string name = Path.GetFileName(entry);
                        if (!string.Equals(name, expectedRunId, StringComparison.Ordinal))
                        {
                            unexpected = entry;
                            return true;
                        }
                    }

                    return false;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    error = exception.Message;
                    return false;
                }
            }

            private static bool HasActiveLubanEvidence(string lockDirectory)
            {
                foreach (string name in new[]
                         {
                             ActiveLubanFileName,
                             ActiveLubanPendingFileName,
                             ActiveLubanStageFileName,
                         })
                {
                    string path = Path.Combine(lockDirectory, name);
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryGetRecordedProcessAlive(
                RecordedProcessIdentity processIdentity,
                out bool alive,
                out string error)
            {
                alive = false;
                error = string.Empty;
                Process process;
                try
                {
                    process = Process.GetProcessById(processIdentity.ProcessId);
                }
                catch (ArgumentException)
                {
                    return true;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    error = exception.Message;
                    return false;
                }

                using (process)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            return true;
                        }

                        alive = process.StartTime.ToUniversalTime().Ticks ==
                                processIdentity.StartTimeUtcTicks;
                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        error = exception.Message;
                        return false;
                    }
                }
            }

            private static string BoundInspectionText(string value)
            {
                string safe = value ?? string.Empty;
                return safe.Length <= InspectionMaximumIssueCharacters
                    ? safe
                    : safe.Substring(0, InspectionMaximumIssueCharacters);
            }
        }
    }
}
