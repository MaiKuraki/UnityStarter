using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            public static void RunSelfTests()
            {
                RunInspectionCommandGrammarSelfTests();
                RunPortableRelativeListSeparatorSelfTest();
                RunBoundedProcessOutputSelfTest();
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

                string multiProfileConfigurationPath = Path.Combine(sourceRoot, "multi-profile.ini");
                File.WriteAllText(
                    multiProfileConfigurationPath,
                    configurationText +
                    "[profile.server]\n" +
                    "code_output=../../UnityStarter/Assets/GeneratedServerCode\n" +
                    "data_output=../../UnityStarter/Assets/GeneratedServerData\n" +
                    "code_target=cs-bin\n" +
                    "data_target=bin\n" +
                    "line_ending=lf\n");
                PipelineConfiguration multiProfileConfiguration = PipelineConfiguration.Load(
                    multiProfileConfigurationPath);
                RunMultiProfileStringConstantConfigurationSelfTest(multiProfileConfiguration);

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
                RunWriterLockContentionSelfTest(configuration);
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

                RunCheckRejectsPendingTransactionSelfTest(
                    configuration,
                    configurationPath,
                    profile);
                RunCheckRejectsTransactionRootFileSelfTest(
                    configuration,
                    configurationPath,
                    profile);
                RunBaselineToctouSelfTests(configuration, profile, identity);
                RunJournalBindingSelfTest(configuration, profile, identity, configurationPath);
                RunFatalPublicationEvidenceSelfTest(configuration, profile, identity);
                RunRollbackDetectsUnchangedDriftSelfTest(configuration, profile, identity);
                RunRollbackSelfTest(configuration, profile, identity, receipt.Generation);
            }

            private static void RunMultiProfileStringConstantConfigurationSelfTest(
                PipelineConfiguration configuration)
            {
                if (configuration.Profiles.Count != 2 || configuration.StringConstants.Tables.Length != 0)
                {
                    throw new InvalidOperationException(
                        "Multi-profile string-constant self-test loaded the wrong typed configuration.");
                }

                string runId = Guid.NewGuid().ToString("N");
                using (PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId))
                {
                    EnsureNoPendingTransactions(configuration);
                    PipelineProfile profile = configuration.GetProfile("client");
                    PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                    try
                    {
                        RunStringConstantGeneration(configuration, profile, transaction);
                    }
                    finally
                    {
                        if (Directory.Exists(transaction.Root))
                        {
                            DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
                        }
                    }
                }

                if (Directory.Exists(configuration.TransactionsRoot) &&
                    !Directory.EnumerateFileSystemEntries(configuration.TransactionsRoot).Any())
                {
                    Directory.Delete(configuration.TransactionsRoot, recursive: false);
                }
            }

            private static void RunPortableRelativeListSeparatorSelfTest()
            {
                string[] paths = ParsePortableRelativeList(
                    "Runtime/First.cs;Runtime/Second.cs,Runtime/Third.cs",
                    "self-test bridge files",
                    3);
                if (paths.Length != 3 ||
                    paths[0] != "Runtime/First.cs" ||
                    paths[1] != "Runtime/Second.cs" ||
                    paths[2] != "Runtime/Third.cs")
                {
                    throw new InvalidOperationException(
                        "Portable relative-list self-test did not accept comma and semicolon separators consistently.");
                }

                AssertThrows<InvalidOperationException>(
                    () => ParsePortableRelativeList(
                        "Runtime/First.cs;runtime/first.cs",
                        "self-test bridge files",
                        3),
                    "a case-colliding path split by different supported separators");
            }

            private static void RunBoundedProcessOutputSelfTest()
            {
                const int standardOutputMaximumCharacters = 5;
                const int standardErrorMaximumCharacters = 3;
                using var outputReader = new StringReader(new string('o', 17));
                using var errorReader = new StringReader(new string('e', 19));
                using var outputWriter = new StringWriter();
                using var errorWriter = new StringWriter();
                var standardOutputForwarder = new BoundedProcessOutputForwarder(
                    standardOutputMaximumCharacters);
                var standardErrorForwarder = new BoundedProcessOutputForwarder(
                    standardErrorMaximumCharacters);

                PumpProcessStreamAsync(
                        outputReader,
                        outputWriter,
                        standardOutputForwarder)
                    .GetAwaiter()
                    .GetResult();
                PumpProcessStreamAsync(
                        errorReader,
                        errorWriter,
                        standardErrorForwarder)
                    .GetAwaiter()
                    .GetResult();

                if (outputWriter.GetStringBuilder().Length != standardOutputMaximumCharacters ||
                    errorWriter.GetStringBuilder().Length != standardErrorMaximumCharacters ||
                    standardOutputForwarder.OmittedCharacters != 12 ||
                    standardErrorForwarder.OmittedCharacters != 16 ||
                    !standardOutputForwarder.WasTruncated ||
                    !standardErrorForwarder.WasTruncated ||
                    outputReader.Read() != -1 ||
                    errorReader.Read() != -1)
                {
                    throw new InvalidOperationException(
                        "Bounded process-output self-test did not preserve the stderr partition while fully draining both streams.");
                }
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

            private static void RunWriterLockContentionSelfTest(PipelineConfiguration configuration)
            {
                using var contendersReady = new Barrier(2);
                PipelineWriterLock? firstLock = null;
                PipelineWriterLock? secondLock = null;
                Exception? firstError = null;
                Exception? secondError = null;
                string firstRunId = Guid.NewGuid().ToString("N");
                string secondRunId = Guid.NewGuid().ToString("N");

                Action synchronizeAfterPreflight = () =>
                {
                    if (!contendersReady.SignalAndWait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Writer-lock contenders did not reach the acquisition barrier.");
                    }
                };
                Task first = Task.Run(() =>
                {
                    try
                    {
                        firstLock = PipelineWriterLock.Acquire(
                            configuration,
                            firstRunId,
                            synchronizeAfterPreflight);
                    }
                    catch (Exception exception)
                    {
                        firstError = exception;
                    }
                });
                Task second = Task.Run(() =>
                {
                    try
                    {
                        secondLock = PipelineWriterLock.Acquire(
                            configuration,
                            secondRunId,
                            synchronizeAfterPreflight);
                    }
                    catch (Exception exception)
                    {
                        secondError = exception;
                    }
                });

                if (!Task.WaitAll(new[] { first, second }, TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Writer-lock contention self-test did not complete in time.");
                }

                PipelineWriterLock? winner = firstLock ?? secondLock;
                Exception? loserError = firstLock == null ? firstError : secondError;
                if (winner == null || (firstLock != null && secondLock != null) || loserError == null)
                {
                    throw new InvalidOperationException(
                        "Writer-lock contention self-test did not produce exactly one owner and one rejected contender.");
                }

                string ownerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                if (!File.Exists(ownerPath))
                {
                    throw new InvalidOperationException(
                        "A rejected writer-lock contender removed the winning owner's evidence.");
                }

                WriterLockOwner owner = ReadWriterLockOwner(ownerPath);
                if (owner.RunId != winner.RunId || owner.Token != winner.Token)
                {
                    throw new InvalidOperationException(
                        "Writer-lock contention self-test observed ownership evidence from the wrong contender.");
                }

                string ownerContent = File.ReadAllText(ownerPath);
                AssertThrows<InvalidOperationException>(
                    () => PipelineWriterLock.Acquire(configuration, Guid.NewGuid().ToString("N")),
                    "a third writer while the winning owner remains active");
                if (!string.Equals(File.ReadAllText(ownerPath), ownerContent, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A rejected third writer changed the winning writer-lock evidence.");
                }

                winner.Dispose();
                using PipelineWriterLock replacement = PipelineWriterLock.Acquire(
                    configuration,
                    Guid.NewGuid().ToString("N"));
            }

            private static void RunCheckRejectsPendingTransactionSelfTest(
                PipelineConfiguration configuration,
                string configurationPath,
                PipelineProfile profile)
            {
                string orphan = Path.Combine(configuration.TransactionsRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(orphan);
                File.WriteAllText(Path.Combine(orphan, "evidence.txt"), "retained");
                string codePath = Path.Combine(profile.CodeOutputRoot, "generated.cs");
                string dataPath = Path.Combine(profile.DataOutputRoot, "table.bytes");
                string codeBefore = File.ReadAllText(codePath);
                string dataBefore = File.ReadAllText(dataPath);
                try
                {
                    try
                    {
                        Run(
                            new[] { "check", "--config", configurationPath, "--profile", profile.Name },
                            CancellationToken.None);
                        throw new InvalidOperationException(
                            "Pipeline check accepted a retained prior transaction.");
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (!exception.Message.Contains(
                                "prior DataTable transaction remains",
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Pipeline check did not reject retained transaction evidence at its shared safety gate.",
                                exception);
                        }
                    }

                    if (File.ReadAllText(codePath) != codeBefore || File.ReadAllText(dataPath) != dataBefore ||
                        !Directory.Exists(orphan) || Directory.Exists(configuration.LockDirectory))
                    {
                        throw new InvalidOperationException(
                            "Rejected pipeline check changed live output or retained transaction evidence.");
                    }
                }
                finally
                {
                    if (Directory.Exists(orphan))
                    {
                        DeleteTreeSafe(orphan, configuration.TransactionsRoot);
                    }
                }
            }

            private static void RunCheckRejectsTransactionRootFileSelfTest(
                PipelineConfiguration configuration,
                string configurationPath,
                PipelineProfile profile)
            {
                if (Directory.Exists(configuration.TransactionsRoot))
                {
                    if (Directory.EnumerateFileSystemEntries(configuration.TransactionsRoot).Any())
                    {
                        throw new InvalidOperationException(
                            "Transaction-root file self-test requires an empty transaction state directory.");
                    }

                    Directory.Delete(configuration.TransactionsRoot, recursive: false);
                }

                File.WriteAllText(configuration.TransactionsRoot, "retained-file");
                string codePath = Path.Combine(profile.CodeOutputRoot, "generated.cs");
                string dataPath = Path.Combine(profile.DataOutputRoot, "table.bytes");
                string codeBefore = File.ReadAllText(codePath);
                string dataBefore = File.ReadAllText(dataPath);
                try
                {
                    try
                    {
                        Run(
                            new[] { "check", "--config", configurationPath, "--profile", profile.Name },
                            CancellationToken.None);
                        throw new InvalidOperationException(
                            "Pipeline check accepted a transaction state root occupied by a file.");
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (!exception.Message.Contains(
                                "transaction state root is occupied by a file",
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Pipeline check did not reject the invalid transaction-root shape.",
                                exception);
                        }
                    }

                    PipelineInspectionSnapshot inspection = BuildInspectionSnapshot(
                        configuration,
                        profile.Name);
                    if (!inspection.Issues.Any(static issue =>
                            issue.Code == "TRANSACTION_STATE_INVALID") ||
                        File.ReadAllText(configuration.TransactionsRoot) != "retained-file" ||
                        File.ReadAllText(codePath) != codeBefore ||
                        File.ReadAllText(dataPath) != dataBefore ||
                        Directory.Exists(configuration.LockDirectory))
                    {
                        throw new InvalidOperationException(
                            "Invalid transaction-root inspection or rejected check changed authoritative evidence.");
                    }
                }
                finally
                {
                    if (File.Exists(configuration.TransactionsRoot))
                    {
                        File.Delete(configuration.TransactionsRoot);
                    }
                }
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
