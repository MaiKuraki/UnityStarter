using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private static void RunInspectionCommandGrammarSelfTests()
            {
                PipelineCommand command = PipelineCommand.Parse(new[]
                {
                    "inspect",
                    "--config", "x",
                    "--profile", "client",
                    "--format", "json",
                });
                if (command.Operation != PipelineOperation.Inspect ||
                    command.ProfileName != "client" || command.Format != "json")
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection command grammar produced the wrong typed command.");
                }

                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[]
                    {
                        "inspect", "--config", "x", "--profile", "client",
                    }),
                    "inspection without an explicit JSON format");
                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[]
                    {
                        "inspect", "--config", "x", "--profile", "client", "--format", "yaml",
                    }),
                    "unsupported inspection format");
                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[]
                    {
                        "inspect", "--config", "x", "--profile", "client", "--format", "json",
                        "--run-id", new string('a', 32),
                    }),
                    "inspection recovery identifier");
                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[]
                    {
                        "generate", "--config", "x", "--profile", "client", "--format", "json",
                    }),
                    "generation inspection format");
            }

            private static void RunInspectionSelfTests(
                PipelineConfiguration configuration,
                string configurationPath)
            {
                bool lockExisted = Directory.Exists(configuration.LockDirectory) ||
                                   File.Exists(configuration.LockDirectory);
                bool transactionsExisted = Directory.Exists(configuration.TransactionsRoot) ||
                                           File.Exists(configuration.TransactionsRoot);
                PipelineInspectionSnapshot snapshot = BuildInspectionSnapshot(configuration, "client");
                if (snapshot.Schema != InspectionSchema ||
                    snapshot.SchemaVersion != InspectionSchemaVersion ||
                    snapshot.SelectedProfile.Name != "client" ||
                    snapshot.Profiles.Length != 1 ||
                    !snapshot.Profiles[0].Selected ||
                    snapshot.Status != "blocked" ||
                    snapshot.Transaction.State != "idle" ||
                    snapshot.CanGenerate || snapshot.CanCheck || snapshot.CanRecover ||
                    !HasInspectionIssue(snapshot, "LUBAN_EXECUTABLE_MISSING") ||
                    !HasInspectionIssue(snapshot, "SCHEMA_WORKBOOK_MISSING"))
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection self-test produced an incomplete blocked snapshot.");
                }

                string json = SerializeInspectionSnapshot(snapshot);
                ValidateInspectionJsonContract(json, expectedActiveLubanEvidence: false);

                var output = new StringWriter();
                TextWriter originalOutput = Console.Out;
                try
                {
                    Console.SetOut(output);
                    if (Inspect(configuration, "client", "json") != 0)
                    {
                        throw new InvalidOperationException(
                            "Pipeline inspection command did not return success for a blocked snapshot.");
                    }
                }
                finally
                {
                    Console.SetOut(originalOutput);
                }

                string captured = output.ToString();
                if (!captured.StartsWith("{", StringComparison.Ordinal) ||
                    !captured.EndsWith("}\n", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection stdout contains non-JSON protocol output.");
                }

                ValidateInspectionJsonContract(captured, expectedActiveLubanEvidence: false);

                PipelineInspectionSnapshot missingProfile = BuildInspectionSnapshot(
                    configuration,
                    "missing-profile");
                if (!HasInspectionIssue(missingProfile, "PROFILE_NOT_FOUND") ||
                    missingProfile.Profiles.Length != 1 ||
                    missingProfile.SelectedProfile.Name.Length != 0)
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection did not preserve profile discovery after a missing selection.");
                }

                RunInspectionPlaceholderSelfTest(configurationPath);
                RunInspectionMissingLubanConfigurationSelfTest(configurationPath);
                RunInspectionActiveWriterSelfTest(configuration);
                RunInspectionRecoveryStateSelfTest(configuration);
                RunInspectionIssueBoundSelfTest();

                bool lockExistsAfter = Directory.Exists(configuration.LockDirectory) ||
                                       File.Exists(configuration.LockDirectory);
                bool transactionsExistAfter = Directory.Exists(configuration.TransactionsRoot) ||
                                              File.Exists(configuration.TransactionsRoot);
                if (lockExisted != lockExistsAfter || transactionsExisted != transactionsExistAfter)
                {
                    throw new InvalidOperationException(
                        "Read-only pipeline inspection created or removed transaction state.");
                }
            }

            private static void RunInspectionPlaceholderSelfTest(string configurationPath)
            {
                string placeholderPath = Path.Combine(
                    Path.GetDirectoryName(configurationPath)!,
                    "inspection-placeholder.ini");
                string text = File.ReadAllText(configurationPath)
                    .Replace("executable_version=test", "executable_version=REPLACE_WITH_APPROVED_VERSION")
                    .Replace(
                        "executable_sha256=" + new string('a', 64),
                        "executable_sha256=REPLACE_WITH_APPROVED_SHA256")
                    .Replace(
                        "source_fingerprint=" + new string('b', 64),
                        "source_fingerprint=REPLACE_WITH_APPROVED_SOURCE_FINGERPRINT");
                File.WriteAllText(placeholderPath, text);
                try
                {
                    PipelineConfiguration placeholder = PipelineConfiguration.LoadForInspection(placeholderPath);
                    PipelineInspectionSnapshot snapshot = BuildInspectionSnapshot(placeholder, "client");
                    if (!HasInspectionIssue(snapshot, "LUBAN_IDENTITY_PLACEHOLDER") ||
                        !HasInspectionIssue(snapshot, "SOURCE_FINGERPRINT_PLACEHOLDER"))
                    {
                        throw new InvalidOperationException(
                            "Pipeline inspection did not report placeholder identities structurally.");
                    }
                }
                finally
                {
                    File.Delete(placeholderPath);
                }
            }

            private static void RunInspectionMissingLubanConfigurationSelfTest(string configurationPath)
            {
                string lubanConfigurationPath = Path.Combine(
                    Path.GetDirectoryName(configurationPath)!,
                    "luban.conf");
                string displacedPath = lubanConfigurationPath + ".inspection-self-test";
                File.Move(lubanConfigurationPath, displacedPath);
                try
                {
                    AssertThrows<FileNotFoundException>(
                        () => PipelineConfiguration.Load(configurationPath),
                        "strict generation configuration without luban.conf");
                    PipelineConfiguration inspectable =
                        PipelineConfiguration.LoadForInspection(configurationPath);
                    PipelineInspectionSnapshot snapshot = BuildInspectionSnapshot(
                        inspectable,
                        "client");
                    if (!HasInspectionIssue(snapshot, "LUBAN_CONFIGURATION_MISSING") ||
                        snapshot.Profiles.Length != 1)
                    {
                        throw new InvalidOperationException(
                            "Pipeline inspection did not tolerate and report a missing luban.conf.");
                    }
                }
                finally
                {
                    File.Move(displacedPath, lubanConfigurationPath);
                }
            }

            private static void RunInspectionIssueBoundSelfTest()
            {
                var issues = new InspectionIssueCollector();
                for (int index = 0; index < InspectionMaximumIssues * 2; index++)
                {
                    issues.Add("TEST", "error", "configuration", index.ToString());
                }

                PipelineInspectionIssue[] snapshot = issues.ToArray();
                if (snapshot.Length != InspectionMaximumIssues ||
                    snapshot.Count(static issue =>
                        issue.Code == "INSPECTION_ISSUES_TRUNCATED") != 1)
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection issue collector exceeded or lost its truncation bound.");
                }
            }

            private static void RunInspectionActiveWriterSelfTest(
                PipelineConfiguration configuration)
            {
                PipelineProfile profile = configuration.GetProfile("client");
                if (Directory.Exists(profile.CodeOutputRoot) || File.Exists(profile.CodeOutputRoot) ||
                    Directory.Exists(profile.DataOutputRoot) || File.Exists(profile.DataOutputRoot) ||
                    Directory.Exists(configuration.TransactionsRoot) ||
                    File.Exists(configuration.TransactionsRoot))
                {
                    throw new InvalidOperationException(
                        "Active-writer inspection self-test requires unused output and transaction roots.");
                }

                Directory.CreateDirectory(profile.CodeOutputRoot);
                Directory.CreateDirectory(profile.DataOutputRoot);
                File.WriteAllText(GetReceiptPath(profile), "{\"incomplete\":");
                File.WriteAllText(Path.Combine(profile.DataOutputRoot, "partial.bytes"), "partial");
                string activeTransactionRoot = string.Empty;
                try
                {
                    string runId = Guid.NewGuid().ToString("N");
                    using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(
                        configuration,
                        runId);
                    activeTransactionRoot = Path.Combine(configuration.TransactionsRoot, runId);
                    Directory.CreateDirectory(activeTransactionRoot);
                    File.WriteAllText(
                        Path.Combine(activeTransactionRoot, "journal.json"),
                        "{\"incomplete\":");
                    writerLock.BeginActiveLubanLaunch();
                    try
                    {
                        PipelineInspectionSnapshot snapshot = BuildInspectionSnapshot(
                            configuration,
                            "client");
                        bool reportedOutputMutation = snapshot.Issues.Any(static issue =>
                            issue.Code.StartsWith("OUTPUT_", StringComparison.Ordinal) &&
                            issue.Code != "OUTPUT_VALIDATION_DEFERRED");
                        if (snapshot.Status != "busy" ||
                            snapshot.Transaction.State != "active" ||
                            !snapshot.Transaction.WriterProcessAlive ||
                            !snapshot.Transaction.ActiveLubanEvidence ||
                            snapshot.Transaction.JournalExists ||
                            snapshot.Output.State != "unavailable" ||
                            !snapshot.Output.ReceiptExists ||
                            snapshot.Output.ReceiptValid ||
                            snapshot.Toolchain.ActualSourceFingerprint.Length != 0 ||
                            snapshot.Toolchain.SchemaSha256.Length != 0 ||
                            reportedOutputMutation ||
                            !HasInspectionIssue(snapshot, "OUTPUT_VALIDATION_DEFERRED") ||
                            !HasInspectionIssue(snapshot, "TOOLCHAIN_DEEP_VALIDATION_DEFERRED"))
                        {
                            throw new InvalidOperationException(
                                "Pipeline inspection scanned or misreported mutable live state while a writer was active.");
                        }

                        ValidateInspectionJsonContract(
                            SerializeInspectionSnapshot(snapshot),
                            expectedActiveLubanEvidence: true);
                    }
                    finally
                    {
                        writerLock.ClearActiveLubanEvidence(processIdentity: null);
                    }
                }
                finally
                {
                    if (activeTransactionRoot.Length != 0 &&
                        Directory.Exists(activeTransactionRoot))
                    {
                        DeleteTreeSafe(activeTransactionRoot, configuration.TransactionsRoot);
                    }

                    if (Directory.Exists(configuration.TransactionsRoot))
                    {
                        Directory.Delete(configuration.TransactionsRoot, recursive: false);
                    }

                    DeleteTreeSafe(
                        profile.CodeOutputRoot,
                        Path.GetDirectoryName(profile.CodeOutputRoot)!);
                    DeleteTreeSafe(
                        profile.DataOutputRoot,
                        Path.GetDirectoryName(profile.DataOutputRoot)!);
                }
            }

            private static void ValidateInspectionJsonContract(
                string json,
                bool expectedActiveLubanEvidence)
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement transaction = root.GetProperty("transaction");
                JsonValueKind expectedEvidenceKind = expectedActiveLubanEvidence
                    ? JsonValueKind.True
                    : JsonValueKind.False;
                if (root.ValueKind != JsonValueKind.Object ||
                    root.GetProperty("schema").GetString() != InspectionSchema ||
                    root.GetProperty("schemaVersion").GetInt32() != InspectionSchemaVersion ||
                    root.GetProperty("status").ValueKind != JsonValueKind.String ||
                    !IsJsonBoolean(root.GetProperty("canGenerate")) ||
                    !IsJsonBoolean(root.GetProperty("canCheck")) ||
                    !IsJsonBoolean(root.GetProperty("canRecover")) ||
                    root.GetProperty("issues").ValueKind != JsonValueKind.Array ||
                    root.GetProperty("profiles").ValueKind != JsonValueKind.Array ||
                    root.GetProperty("selectedProfile").ValueKind != JsonValueKind.Object ||
                    root.GetProperty("toolchain").ValueKind != JsonValueKind.Object ||
                    root.GetProperty("output").ValueKind != JsonValueKind.Object ||
                    transaction.ValueKind != JsonValueKind.Object ||
                    transaction.GetProperty("state").ValueKind != JsonValueKind.String ||
                    transaction.GetProperty("writerProcessId").ValueKind != JsonValueKind.Number ||
                    !IsJsonBoolean(transaction.GetProperty("writerProcessAlive")) ||
                    !IsJsonBoolean(transaction.GetProperty("cancelRequested")) ||
                    transaction.GetProperty("activeLubanEvidence").ValueKind != expectedEvidenceKind ||
                    !IsJsonBoolean(transaction.GetProperty("recoveryRequired")))
                {
                    throw new InvalidOperationException(
                        "Pipeline inspection JSON self-test observed a schema or JSON-type drift.");
                }
            }

            private static void RunInspectionRecoveryStateSelfTest(
                PipelineConfiguration configuration)
            {
                if (Directory.Exists(configuration.LockDirectory) ||
                    File.Exists(configuration.LockDirectory) ||
                    Directory.Exists(configuration.TransactionsRoot) ||
                    File.Exists(configuration.TransactionsRoot))
                {
                    throw new InvalidOperationException(
                        "Recovery inspection self-test requires unused transaction state.");
                }

                string runId = Guid.NewGuid().ToString("N");
                string transactionRoot = Path.Combine(configuration.TransactionsRoot, runId);
                Directory.CreateDirectory(configuration.LockDirectory);
                File.WriteAllText(
                    Path.Combine(configuration.LockDirectory, WriterOwnerFileName),
                    "schema=CycloneGames.DataTable.WriterLock\n" +
                    "version=2\n" +
                    "run_id=" + runId + "\n" +
                    "token=" + new string('a', 32) + "\n" +
                    "process_id=" + Environment.ProcessId + "\n" +
                    "process_start_utc_ticks=1\n");
                try
                {
                    PipelineInspectionSnapshot missingTransaction = BuildInspectionSnapshot(
                        configuration,
                        "client");
                    if (missingTransaction.Status != "recoveryRequired" ||
                        missingTransaction.Transaction.State != "invalid" ||
                        !missingTransaction.Transaction.RecoveryRequired ||
                        missingTransaction.CanRecover ||
                        missingTransaction.CanGenerate ||
                        missingTransaction.CanCheck ||
                        missingTransaction.Output.State != "unavailable" ||
                        missingTransaction.Toolchain.ActualSourceFingerprint.Length != 0 ||
                        !HasInspectionIssue(missingTransaction, "RECOVERY_TRANSACTION_MISSING"))
                    {
                        throw new InvalidOperationException(
                            "Pipeline inspection did not fail closed for incomplete recovery state.");
                    }

                    Directory.CreateDirectory(transactionRoot);
                    PipelineInspectionSnapshot recoverable = BuildInspectionSnapshot(
                        configuration,
                        "client");
                    if (recoverable.Status != "recoveryRequired" ||
                        recoverable.Transaction.State != "recoveryRequired" ||
                        !recoverable.Transaction.RecoveryRequired ||
                        !recoverable.CanRecover ||
                        recoverable.CanGenerate ||
                        recoverable.CanCheck)
                    {
                        throw new InvalidOperationException(
                            "Pipeline inspection did not expose a proven recoverable transaction.");
                    }

                    File.WriteAllText(
                        Path.Combine(transactionRoot, "journal.json"),
                        "{\"incomplete\":");
                    PipelineInspectionSnapshot invalidJournal = BuildInspectionSnapshot(
                        configuration,
                        "client");
                    if (invalidJournal.Status != "recoveryRequired" ||
                        invalidJournal.Transaction.State != "invalid" ||
                        invalidJournal.Transaction.JournalState != "invalid" ||
                        invalidJournal.CanRecover ||
                        invalidJournal.CanGenerate ||
                        invalidJournal.CanCheck ||
                        !HasInspectionIssue(invalidJournal, "RECOVERY_JOURNAL_INVALID"))
                    {
                        throw new InvalidOperationException(
                            "Pipeline inspection did not fail closed for an invalid recovery journal.");
                    }
                }
                finally
                {
                    if (Directory.Exists(transactionRoot))
                    {
                        DeleteTreeSafe(transactionRoot, configuration.TransactionsRoot);
                    }

                    if (Directory.Exists(configuration.TransactionsRoot))
                    {
                        Directory.Delete(configuration.TransactionsRoot, recursive: false);
                    }

                    string ownerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                    if (File.Exists(ownerPath))
                    {
                        File.Delete(ownerPath);
                    }

                    if (Directory.Exists(configuration.LockDirectory))
                    {
                        Directory.Delete(configuration.LockDirectory, recursive: false);
                    }
                }
            }

            private static bool IsJsonBoolean(JsonElement value)
            {
                return value.ValueKind == JsonValueKind.True ||
                       value.ValueKind == JsonValueKind.False;
            }

            private static bool HasInspectionIssue(
                PipelineInspectionSnapshot snapshot,
                string code)
            {
                return snapshot.Issues.Any(issue => issue.Code == code);
            }
        }
    }
}
