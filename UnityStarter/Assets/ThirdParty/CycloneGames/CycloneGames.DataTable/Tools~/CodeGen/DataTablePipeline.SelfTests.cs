using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            public static void RunSelfTests()
            {
                RunInspectionCommandGrammarSelfTests();
                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[] { "generate", "--config", "x", "--profile", "client", "--profile", "server" }),
                    "duplicate pipeline argument");
                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[] { "generate", "--config", "x", "--profile", "client", "--unknown", "x" }),
                    "unknown pipeline argument");
                AssertThrows<ArgumentException>(
                    () => PipelineCommand.Parse(new[] { "recover", "--config", "x", "--run-id", "unsafe" }),
                    "invalid recovery run identifier");
                AssertThrows<InvalidOperationException>(
                    () => ParseSections("[luban]\nsource_fingerprint=a\nsource_fingerprint=b\n"),
                    "duplicate configuration key");
                AssertThrows<InvalidOperationException>(
                    () => ParseSections("[profile.client]\ncode_output=a\n[PROFILE.CLIENT]\ncode_output=b\n"),
                    "case-colliding configuration section");
                AssertThrows<InvalidOperationException>(
                    () => RejectDuplicateJsonProperties(
                        System.Text.Encoding.UTF8.GetBytes("{\"schema\":\"a\",\"schema\":\"b\"}"),
                        "self-test state"),
                    "duplicate persisted-state property");

                RunAdvancedSelfTests();

                string temporaryRoot = Path.Combine(Path.GetTempPath(), "CycloneGames.DataTable.Pipeline.Tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                try
                {
                    string source = Path.Combine(temporaryRoot, "source.bin");
                    string target = Path.Combine(temporaryRoot, "target.bin");
                    File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
                    string hash = ComputeFileSha256(source);
                    ReplaceFromSource(source, target, new FileInfo(source).Length, hash);
                    if (ComputeFileSha256(target) != hash)
                    {
                        throw new InvalidOperationException("Pipeline replacement self-test produced the wrong hash.");
                    }

                    AssertThrows<InvalidOperationException>(
                        () => ValidatePortableRelativePath("../escape", "self-test"),
                        "relative traversal");
                    AssertThrows<InvalidOperationException>(
                        () => ValidatePortableRelativePath("CON/file.bin", "self-test"),
                        "reserved path");

                    RunTransactionSelfTest(temporaryRoot);
                }
                finally
                {
                    if (Directory.Exists(temporaryRoot))
                    {
                        DeleteTreeSafe(temporaryRoot, Path.GetDirectoryName(temporaryRoot)!);
                    }
                }
            }

            private static void RunTransactionSelfTest(string temporaryRoot)
            {
                string repositoryRoot = Path.Combine(temporaryRoot, "repository");
                string sourceRoot = Path.Combine(repositoryRoot, "DataTable", "Luban");
                string unityAssets = Path.Combine(repositoryRoot, "UnityStarter", "Assets");
                string toolRoot = Path.Combine(
                    repositoryRoot,
                    "UnityStarter", "Assets", "ThirdParty", "CycloneGames", "CycloneGames.DataTable", "Tools~", "CodeGen");
                Directory.CreateDirectory(sourceRoot);
                Directory.CreateDirectory(unityAssets);
                Directory.CreateDirectory(toolRoot);
                string toolProject = Path.Combine(toolRoot, "CycloneGames.DataTable.CodeGen.csproj");
                File.WriteAllText(toolProject, "<Project />\n");
                File.WriteAllText(Path.Combine(sourceRoot, "luban.conf"), "{}\n");
                string configurationPath = Path.Combine(sourceRoot, "build_config.ini");
                string configurationText =
                    "[luban]\n" +
                    "luban_dll=../../Tools/Luban.dll\n" +
                    "windows_executable=\n" +
                    "windows_executable_sha256=\n" +
                    "executable_version=test\n" +
                    "executable_sha256=" + new string('a', 64) + "\n" +
                    "source_fingerprint=" + new string('b', 64) + "\n" +
                    "process_timeout_seconds=60\n" +
                    "[templates]\ncustom_template_dir=\nbridge_files=\n" +
                    "[codegen]\n" +
                    "codegen_project=../../UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/Tools~/CodeGen/CycloneGames.DataTable.CodeGen.csproj\n" +
                    "string_constant_tables=\n" +
                    "string_constant_value_column=name\n" +
                    "string_constant_comment_column=comment\n" +
                    "string_constant_enabled_column=enabled\n" +
                    "string_constant_scope_column=scope\n" +
                    "string_constant_generated_comment_language=en\n" +
                    "[profile.client]\n" +
                    "code_output=../../UnityStarter/Assets/GeneratedCode\n" +
                    "data_output=../../UnityStarter/Assets/GeneratedData\n" +
                    "code_target=cs-bin\n" +
                    "data_target=bin\n" +
                    "line_ending=lf\n";
                File.WriteAllText(configurationPath, configurationText);

                string unknownKeyConfigurationPath = Path.Combine(sourceRoot, "unknown-key.ini");
                File.WriteAllText(
                    unknownKeyConfigurationPath,
                    configurationText.Replace(
                        "process_timeout_seconds=60\n",
                        "process_timeout_seconds=60\nunsupported_key=value\n"));
                AssertThrows<InvalidOperationException>(
                    () => PipelineConfiguration.Load(unknownKeyConfigurationPath),
                    "unknown typed configuration key");

                PipelineConfiguration configuration = PipelineConfiguration.Load(configurationPath);
                RunInspectionSelfTests(configuration, configurationPath);
                string namedCacheDirectory = Path.Combine(sourceRoot, "Datas", "cache");
                Directory.CreateDirectory(namedCacheDirectory);
                string namedCacheInput = Path.Combine(namedCacheDirectory, "input.txt");
                File.WriteAllText(namedCacheInput, "first\n");
                string fingerprintBeforeNamedDirectoryChange = ComputeSourceFingerprint(configuration);
                File.WriteAllText(namedCacheInput, "second\n");
                string fingerprintAfterNamedDirectoryChange = ComputeSourceFingerprint(configuration);
                if (fingerprintBeforeNamedDirectoryChange == fingerprintAfterNamedDirectoryChange)
                {
                    throw new InvalidOperationException(
                        "Source fingerprint silently excluded a legitimate Datas/cache directory.");
                }

                RunWriterLockIdentitySelfTest(configuration);
                PipelineProfile profile = configuration.GetProfile("client");
                var identity = new PipelineIdentity(
                    Path.Combine(repositoryRoot, "Tools", "Luban.dll"),
                    useDotNetHost: true,
                    new string('a', 64),
                    new string('b', 64),
                    new string('c', 64),
                    new string('d', 64));

                PublishSelfTestGeneration(configuration, profile, identity, "one", "stable");
                string stableDataPath = Path.Combine(profile.DataOutputRoot, "table.bytes");
                DateTime stableTimestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(stableDataPath, stableTimestamp);
                PublishSelfTestGeneration(configuration, profile, identity, "two", "stable");

                GenerationReceipt receipt = ReadAndValidateLiveReceipt(profile);
                ValidateLiveOutputs(profile, receipt, identity, requireCurrentIdentity: true);
                if (File.ReadAllText(Path.Combine(profile.CodeOutputRoot, "generated.cs")) != "two" ||
                    File.GetLastWriteTimeUtc(stableDataPath) != stableTimestamp)
                {
                    throw new InvalidOperationException(
                        "Pipeline transaction self-test failed changed-only publication.");
                }

                RunBaselineToctouSelfTests(configuration, profile, identity);
                RunJournalBindingSelfTest(configuration, profile, identity, configurationPath);
                RunFatalPublicationEvidenceSelfTest(configuration, profile, identity);
                RunRollbackDetectsUnchangedDriftSelfTest(configuration, profile, identity);
                RunRollbackSelfTest(configuration, profile, identity, receipt.Generation);
            }

            private static void RunWriterLockIdentitySelfTest(PipelineConfiguration configuration)
            {
                string runId = Guid.NewGuid().ToString("N");
                using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                string ownerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                WriterLockOwner owner = ReadWriterLockOwner(ownerPath);
                if (owner.RunId != runId || owner.Token != writerLock.Token)
                {
                    throw new InvalidOperationException("Writer-lock identity self-test read the wrong owner.");
                }

                AssertThrows<InvalidOperationException>(
                    () => AssertRecordedProcessStopped(owner.ProcessIdentity, "self-test writer"),
                    "recovery while the original writer remains alive");

                using Process current = Process.GetCurrentProcess();
                RecordedProcessIdentity activeIdentity = CaptureProcessIdentity(current);
                writerLock.BeginActiveLubanLaunch();
                writerLock.RecordActiveLubanProcess(activeIdentity);
                ActiveLubanOwner active = ReadActiveLubanOwner(
                    Path.Combine(configuration.LockDirectory, ActiveLubanFileName));
                if (active.RunId != runId || active.Token != writerLock.Token ||
                    active.ProcessIdentity.ProcessId != activeIdentity.ProcessId ||
                    active.ProcessIdentity.StartTimeUtcTicks != activeIdentity.StartTimeUtcTicks)
                {
                    throw new InvalidOperationException("Active Luban identity self-test read the wrong owner.");
                }

                writerLock.ClearActiveLubanEvidence(activeIdentity);
            }

            private static void RunBaselineToctouSelfTests(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity)
            {
                string orphanMetadata = Path.Combine(profile.CodeOutputRoot, "orphan.cs.meta");
                File.WriteAllText(orphanMetadata, "orphan");
                AssertThrows<InvalidOperationException>(
                    () => CaptureBaseline(profile),
                    "orphan Unity metadata in a dedicated output root");
                File.Delete(orphanMetadata);

                string runId = Guid.NewGuid().ToString("N");
                using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                EnsureNoPendingTransactions(configuration);
                BaselineSnapshot baseline = CaptureBaseline(profile);
                PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                File.WriteAllText(Path.Combine(transaction.CandidateCodeRoot, "generated.cs"), "toctou-candidate");
                File.WriteAllText(Path.Combine(transaction.CandidateDataRoot, "table.bytes"), "stable");
                CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                var publicationSafety = new PublicationSafetyState();
                string stableDataPath = Path.Combine(profile.DataOutputRoot, "table.bytes");
                File.WriteAllText(stableDataPath, "external-change");
                AssertThrows<InvalidOperationException>(
                    () => PublishCandidate(transaction, candidate, baseline, publicationSafety),
                    "live baseline drift during candidate generation");
                File.WriteAllText(stableDataPath, "stable");
                ValidateBaselineUnchanged(profile, baseline);
                DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
            }

            private static void RunFatalPublicationEvidenceSelfTest(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity)
            {
                string runId = Guid.NewGuid().ToString("N");
                PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                EnsureNoPendingTransactions(configuration);
                BaselineSnapshot baseline = CaptureBaseline(profile);
                PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                File.WriteAllText(Path.Combine(transaction.CandidateCodeRoot, "generated.cs"), "fatal-candidate");
                File.WriteAllText(Path.Combine(transaction.CandidateDataRoot, "table.bytes"), "fatal-data");
                CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                var publicationSafety = new PublicationSafetyState();

                try
                {
                    PublishCandidate(
                        transaction,
                        candidate,
                        baseline,
                        publicationSafety,
                        operationIndex =>
                        {
                            if (operationIndex == 0)
                            {
                                throw new OutOfMemoryException("Synthetic fatal publication fault.");
                            }
                        });
                    throw new InvalidOperationException(
                        "Pipeline self-test did not inject a fatal partial-publication fault.");
                }
                catch (OutOfMemoryException)
                {
                    if (publicationSafety.RequiresRecoveryEvidence)
                    {
                        writerLock.PreserveForRecovery();
                    }
                }

                if (!publicationSafety.RequiresRecoveryEvidence || !File.Exists(transaction.JournalPath) ||
                    !Directory.EnumerateFiles(transaction.BackupRoot, "*", SearchOption.AllDirectories).Any())
                {
                    throw new InvalidOperationException(
                        "A fatal publication fault did not retain durable recovery state.");
                }

                AssertThrows<InvalidOperationException>(
                    () => ValidateBaselineUnchanged(profile, baseline),
                    "a partially published live output after a fatal fault");

                writerLock.Dispose();
                MarkWriterStoppedForRecoverySelfTest(configuration, runId, writerLock.Token);
                if (Recover(configuration, runId) != 0)
                {
                    throw new InvalidOperationException(
                        "Recovery did not complete after a fatal partial-publication fault.");
                }

                ValidateBaselineUnchanged(profile, baseline);
                if (Directory.Exists(transaction.Root) || Directory.Exists(configuration.LockDirectory))
                {
                    throw new InvalidOperationException(
                        "Verified fatal-fault recovery retained transaction or writer-lock state.");
                }
            }

            private static void RunJournalBindingSelfTest(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity,
                string configurationPath)
            {
                string runId = Guid.NewGuid().ToString("N");
                PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                EnsureNoPendingTransactions(configuration);
                BaselineSnapshot baseline = CaptureBaseline(profile);
                PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                File.WriteAllText(Path.Combine(transaction.CandidateCodeRoot, "generated.cs"), "binding-candidate");
                File.WriteAllText(Path.Combine(transaction.CandidateDataRoot, "table.bytes"), "stable");
                CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                TransactionJournal journal = BuildJournal(transaction, candidate, baseline);
                ValidateJournal(journal, runId);
                ValidateJournalBinding(journal, configuration, profile);

                var wrongRootProfile = new PipelineProfile(
                    profile.Name,
                    profile.CodeOutputRoot + "-moved",
                    profile.DataOutputRoot,
                    profile.CodeTarget,
                    profile.DataTarget,
                    profile.LineEnding);
                AssertThrows<InvalidOperationException>(
                    () => ValidateJournalBinding(journal, configuration, wrongRootProfile),
                    "journal output-root identity drift");

                WriteJournal(transaction.JournalPath, journal);
                writerLock.PreserveForRecovery();
                writerLock.Dispose();
                MarkWriterStoppedForRecoverySelfTest(configuration, runId, writerLock.Token);

                byte[] originalConfiguration = File.ReadAllBytes(configurationPath);
                try
                {
                    string originalText = new System.Text.UTF8Encoding(false, true)
                        .GetString(originalConfiguration)
                        .Replace("process_timeout_seconds=60", "process_timeout_seconds=61", StringComparison.Ordinal);
                    File.WriteAllText(configurationPath, originalText);
                    PipelineConfiguration changed = PipelineConfiguration.Load(configurationPath);
                    if (Recover(changed, runId) != 3)
                    {
                        throw new InvalidOperationException(
                            "Configuration-drift recovery did not return recovery-required status.");
                    }
                    ValidateBaselineUnchanged(profile, baseline);
                    if (!Directory.Exists(transaction.Root) || !Directory.Exists(configuration.LockDirectory))
                    {
                        throw new InvalidOperationException(
                            "Configuration-drift recovery did not preserve transaction evidence.");
                    }

                    string changedRootText = new System.Text.UTF8Encoding(false, true)
                        .GetString(originalConfiguration)
                        .Replace(
                            "code_output=../../UnityStarter/Assets/GeneratedCode",
                            "code_output=../../UnityStarter/Assets/GeneratedCodeMoved",
                            StringComparison.Ordinal);
                    File.WriteAllText(configurationPath, changedRootText);
                    PipelineConfiguration changedRoot = PipelineConfiguration.Load(configurationPath);
                    journal.ConfigurationSha256 = changedRoot.ConfigurationSha256;
                    WriteJournal(transaction.JournalPath, journal);
                    if (Recover(changedRoot, runId) != 3)
                    {
                        throw new InvalidOperationException(
                            "Output-root-drift recovery did not return recovery-required status.");
                    }
                    ValidateBaselineUnchanged(profile, baseline);
                }
                finally
                {
                    File.WriteAllBytes(configurationPath, originalConfiguration);
                }

                journal.ConfigurationSha256 = configuration.ConfigurationSha256;
                WriteJournal(transaction.JournalPath, journal);
                if (Recover(configuration, runId) != 0)
                {
                    throw new InvalidOperationException(
                        "Recovery did not complete after restoring journal-bound configuration.");
                }

                ValidateBaselineUnchanged(profile, baseline);
            }

            private static void MarkWriterStoppedForRecoverySelfTest(
                PipelineConfiguration configuration,
                string runId,
                string token)
            {
                string ownerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                string stoppedOwner =
                    "schema=CycloneGames.DataTable.WriterLock\n" +
                    "version=2\n" +
                    "run_id=" + runId + "\n" +
                    "token=" + token + "\n" +
                    "process_id=" + int.MaxValue + "\n" +
                    "process_start_utc_ticks=1\n";
                File.WriteAllText(ownerPath, stoppedOwner);
            }

            private static void RunRollbackDetectsUnchangedDriftSelfTest(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity)
            {
                string runId = Guid.NewGuid().ToString("N");
                using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                EnsureNoPendingTransactions(configuration);
                BaselineSnapshot baseline = CaptureBaseline(profile);
                PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                File.WriteAllText(Path.Combine(transaction.CandidateCodeRoot, "generated.cs"), "rollback-toctou");
                File.WriteAllText(Path.Combine(transaction.CandidateDataRoot, "table.bytes"), "stable");
                CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                TransactionJournal journal = BuildJournal(transaction, candidate, baseline);
                ValidateJournal(journal, runId);
                PrepareBackups(transaction, journal);
                WriteJournal(transaction.JournalPath, journal);
                journal.State = JournalState.Publishing.ToString();
                WriteJournal(transaction.JournalPath, journal);
                ApplyOperations(transaction, journal);

                string stableDataPath = Path.Combine(profile.DataOutputRoot, "table.bytes");
                File.WriteAllText(stableDataPath, "concurrent-unrelated-drift");
                AssertThrows<InvalidOperationException>(
                    () => RollbackOperations(transaction, journal),
                    "unchanged live-file drift during rollback verification");
                File.WriteAllText(stableDataPath, "stable");
                VerifyRestoredBaseline(profile, journal);
                DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
            }

            private static void RunRollbackSelfTest(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity,
                string previousGeneration)
            {
                string runId = Guid.NewGuid().ToString("N");
                using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                EnsureNoPendingTransactions(configuration);
                BaselineSnapshot baseline = CaptureBaseline(profile);
                PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                File.WriteAllText(Path.Combine(transaction.CandidateCodeRoot, "generated.cs"), "rollback-candidate");
                File.WriteAllText(Path.Combine(transaction.CandidateDataRoot, "table.bytes"), "rollback-data");
                CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                TransactionJournal journal = BuildJournal(transaction, candidate, baseline);
                ValidateJournal(journal, runId);
                PrepareBackups(transaction, journal);
                WriteJournal(transaction.JournalPath, journal);
                journal.State = JournalState.Publishing.ToString();
                WriteJournal(transaction.JournalPath, journal);
                ApplyOperations(transaction, journal);
                RollbackOperations(transaction, journal);
                GenerationReceipt restored = ReadAndValidateLiveReceipt(profile);
                ValidateLiveOutputs(profile, restored, identity, requireCurrentIdentity: true);
                if (restored.Generation != previousGeneration ||
                    File.ReadAllText(Path.Combine(profile.CodeOutputRoot, "generated.cs")) != "two" ||
                    File.ReadAllText(Path.Combine(profile.DataOutputRoot, "table.bytes")) != "stable")
                {
                    throw new InvalidOperationException("Pipeline transaction self-test failed verified rollback.");
                }

                DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
            }

            private static void PublishSelfTestGeneration(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity,
                string codeContent,
                string dataContent)
            {
                string runId = Guid.NewGuid().ToString("N");
                using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                EnsureNoPendingTransactions(configuration);
                BaselineSnapshot baseline = CaptureBaseline(profile);
                PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                File.WriteAllText(Path.Combine(transaction.CandidateCodeRoot, "generated.cs"), codeContent);
                File.WriteAllText(Path.Combine(transaction.CandidateDataRoot, "table.bytes"), dataContent);
                CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                var publicationSafety = new PublicationSafetyState();
                PublishCandidate(transaction, candidate, baseline, publicationSafety);
                DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
                publicationSafety.MarkTransactionCleanupCompleted();
            }

            private static void AssertThrows<TException>(Action action, string description)
                where TException : Exception
            {
                try
                {
                    action();
                }
                catch (TException)
                {
                    return;
                }

                throw new InvalidOperationException("Pipeline self-test did not reject " + description + ".");
            }
        }
    }
}
