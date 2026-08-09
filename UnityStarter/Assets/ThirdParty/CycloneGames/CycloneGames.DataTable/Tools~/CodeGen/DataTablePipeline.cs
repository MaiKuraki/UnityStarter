using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            public static int Run(string[] args, CancellationToken cancellationToken)
            {
                PipelineCommand command = PipelineCommand.Parse(args);
                PipelineConfiguration configuration = command.Operation == PipelineOperation.Inspect
                    ? PipelineConfiguration.LoadForInspection(command.ConfigurationPath)
                    : PipelineConfiguration.Load(command.ConfigurationPath);
                if (command.Operation == PipelineOperation.Inspect)
                {
                    return Inspect(configuration, command.ProfileName, command.Format);
                }

                if (command.Operation == PipelineOperation.Recover)
                {
                    return Recover(configuration, command.RunId);
                }

                string runId = Guid.NewGuid().ToString("N");
                using PipelineWriterLock writerLock = PipelineWriterLock.Acquire(configuration, runId);
                try
                {
                    PipelineProfile profile = configuration.GetProfile(command.ProfileName);
                    PipelineIdentity identity = ValidateIdentity(configuration);
                    writerLock.ThrowIfCancellationRequestedAtSafePoint(cancellationToken);
                    if (command.Operation == PipelineOperation.Check)
                    {
                        GenerationReceipt receipt = ReadAndValidateLiveReceipt(profile);
                        ValidateLiveOutputs(profile, receipt, identity, requireCurrentIdentity: true);
                        Console.WriteLine("[DataTable.Pipeline] Check successful; live outputs exactly match their receipt.");
                        return 0;
                    }

                    EnsureNoPendingTransactions(configuration);
                    BaselineSnapshot baseline = CaptureBaseline(profile);
                    PipelineTransaction transaction = CreateTransaction(configuration, profile, runId);
                    var publicationSafety = new PublicationSafetyState();
                    try
                    {
                        ValidateTransactionRoots(transaction, "pre-Luban transaction roots");
                        RunLuban(configuration, profile, identity, transaction, writerLock, cancellationToken);
                        ValidateTransactionRoots(transaction, "post-Luban transaction roots");
                        writerLock.ThrowIfCancellationRequestedAtSafePoint(cancellationToken);
                        RunStringConstantGeneration(configuration, profile, transaction);
                        ValidateTransactionRoots(transaction, "post-CodeGen transaction roots");
                        CopyBridgeFiles(configuration, transaction);
                        ValidateTransactionRoots(transaction, "post-bridge transaction roots");
                        CandidateSnapshot candidate = BuildCandidateSnapshot(profile, identity, transaction);
                        writerLock.ThrowIfCancellationRequestedAtSafePoint(cancellationToken);
                        PublishCandidate(transaction, candidate, baseline, publicationSafety);
                        DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
                        publicationSafety.MarkTransactionCleanupCompleted();
                        Console.WriteLine(
                            "[DataTable.Pipeline] Generation committed: " + candidate.Receipt.Generation);
                        return 0;
                    }
                    catch (RecoveryRequiredException exception)
                    {
                        writerLock.PreserveForRecovery();
                        Console.Error.WriteLine("[DataTable.Pipeline] RECOVERY REQUIRED: " + exception.Message);
                        Console.Error.WriteLine(
                            "[DataTable.Pipeline] Run 'pipeline recover --config <file> --run-id " + runId + "'.");
                        return 3;
                    }
                    catch
                    {
                        if (publicationSafety.RequiresRecoveryEvidence)
                        {
                            writerLock.PreserveForRecovery();
                        }
                        else if (Directory.Exists(transaction.Root))
                        {
                            DeleteTreeSafe(transaction.Root, configuration.TransactionsRoot);
                        }

                        throw;
                    }
                }
                catch (OperationCanceledException exception)
                {
                    Console.Error.WriteLine("[DataTable.Pipeline] " + exception.Message);
                    return 2;
                }
            }

            private static PipelineTransaction CreateTransaction(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                string runId)
            {
                Directory.CreateDirectory(configuration.TransactionsRoot);
                AssertPhysicalContainedPath(
                    configuration.TransactionsRoot,
                    configuration.SourceRoot,
                    "transaction state root",
                    mustExist: true);
                var transaction = new PipelineTransaction(configuration, profile, runId);
                if (Directory.Exists(transaction.Root) || File.Exists(transaction.Root))
                {
                    throw new InvalidOperationException("Transaction identifier collision: " + runId);
                }

                Directory.CreateDirectory(transaction.CandidateCodeRoot);
                Directory.CreateDirectory(transaction.CandidateDataRoot);
                Directory.CreateDirectory(transaction.BackupRoot);
                ValidateTransactionRoots(transaction, "new transaction roots");
                return transaction;
            }

            private static void ValidateTransactionRoots(
                PipelineTransaction transaction,
                string description)
            {
                AssertPhysicalContainedPath(
                    transaction.Root,
                    transaction.Configuration.TransactionsRoot,
                    description + " transaction root",
                    mustExist: true);
                AssertPhysicalContainedPath(
                    transaction.CandidateCodeRoot,
                    transaction.Root,
                    description + " candidate code root",
                    mustExist: true);
                AssertPhysicalContainedPath(
                    transaction.CandidateDataRoot,
                    transaction.Root,
                    description + " candidate data root",
                    mustExist: true);
                AssertPhysicalContainedPath(
                    transaction.BackupRoot,
                    transaction.Root,
                    description + " backup root",
                    mustExist: true);
            }

            private static void EnsureNoPendingTransactions(PipelineConfiguration configuration)
            {
                if (!Directory.Exists(configuration.TransactionsRoot))
                {
                    return;
                }

                AssertNotReparsePoint(configuration.TransactionsRoot, "transaction state root");
                string? pending = Directory.EnumerateFileSystemEntries(configuration.TransactionsRoot).FirstOrDefault();
                if (pending != null)
                {
                    throw new InvalidOperationException(
                        "A prior DataTable transaction remains. Recover or audit it before generating: " + pending);
                }
            }

            private static void RunStringConstantGeneration(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineTransaction transaction)
            {
                ValidateTransactionRoots(transaction, "CodeGen input roots");
                ToolArguments arguments = ToolArguments.CreateForPipeline(
                    configuration.ConfigurationPath,
                    configuration.LubanConfigurationPath,
                    Path.Combine(configuration.SourceRoot, "Datas"),
                    profile.Name,
                    transaction.CandidateCodeRoot,
                    profile.LineEnding);
                StringConstantGenerator.Run(arguments);
            }

            private static void CopyBridgeFiles(
                PipelineConfiguration configuration,
                PipelineTransaction transaction)
            {
                ValidateTransactionRoots(transaction, "bridge staging roots");
                long totalBytes = 0;
                foreach (string relativePath in configuration.BridgeFiles)
                {
                    string source = ResolveRelativePath(
                        configuration.CustomTemplateRoot,
                        relativePath,
                        "bridge source");
                    AssertPhysicalContainedPath(
                        source,
                        configuration.CustomTemplateRoot,
                        "bridge source",
                        mustExist: true);
                    if (!File.Exists(source))
                    {
                        throw new FileNotFoundException("Bridge source is not a physical file.", source);
                    }

                    long length = new FileInfo(source).Length;
                    if (length > 16L * 1024 * 1024 || totalBytes > 64L * 1024 * 1024 - length)
                    {
                        throw new InvalidOperationException("Bridge files exceed their 64 MiB aggregate budget.");
                    }

                    totalBytes += length;
                    string destination = ResolveRelativePath(
                        transaction.CandidateCodeRoot,
                        relativePath,
                        "bridge candidate output");
                    if (File.Exists(destination) || Directory.Exists(destination))
                    {
                        throw new InvalidOperationException(
                            "Bridge output collides with generated candidate content: " + relativePath);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    AssertPhysicalContainedPath(
                        destination,
                        transaction.CandidateCodeRoot,
                        "bridge candidate output",
                        mustExist: false);
                    File.Copy(source, destination, overwrite: false);
                    if (ComputeFileSha256(source) != ComputeFileSha256(destination))
                    {
                        throw new InvalidOperationException("Bridge staging verification failed: " + relativePath);
                    }
                }
            }

            private static CandidateSnapshot BuildCandidateSnapshot(
                PipelineProfile profile,
                PipelineIdentity identity,
                PipelineTransaction transaction)
            {
                Dictionary<string, ReceiptFile> codeFiles = EnumerateOutputFiles(
                    transaction.CandidateCodeRoot,
                    OutputRootKind.Code,
                    candidate: true);
                Dictionary<string, ReceiptFile> dataFiles = EnumerateOutputFiles(
                    transaction.CandidateDataRoot,
                    OutputRootKind.Data,
                    candidate: true);
                ValidateCombinedFileBudget(
                    codeFiles.Values.Concat(dataFiles.Values),
                    "generated candidate");
                if (codeFiles.Count == 0 || dataFiles.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Luban candidate must contain at least one code file and one data file.");
                }

                string codeHash = ComputeOutputAggregate(codeFiles.Values);
                string dataHash = ComputeOutputAggregate(dataFiles.Values);
                string generation = ComputeBytesSha256(Encoding.UTF8.GetBytes(
                    profile.Name + "\n" + identity.ToolHash + "\n" + identity.LubanHash + "\n" +
                    identity.SourceFingerprint + "\n" + identity.SchemaHash + "\n" + codeHash + "\n" + dataHash + "\n"));
                var files = codeFiles.Values.Concat(dataFiles.Values)
                    .OrderBy(static file => file.Root, StringComparer.Ordinal)
                    .ThenBy(static file => file.Path, StringComparer.Ordinal)
                    .ToArray();
                var receipt = new GenerationReceipt
                {
                    Profile = profile.Name,
                    Generation = generation,
                    ToolSha256 = identity.ToolHash,
                    LubanSha256 = identity.LubanHash,
                    SourceFingerprint = identity.SourceFingerprint,
                    SchemaSha256 = identity.SchemaHash,
                    CodeOutputSha256 = codeHash,
                    DataOutputSha256 = dataHash,
                    Files = files,
                };
                string receiptContent = SerializeState(receipt);
                ValidateReceipt(receipt, profile);
                string receiptPath = Path.Combine(transaction.CandidateCodeRoot, ReceiptFileName);
                WriteDurableText(receiptPath, receiptContent, overwrite: false);
                return new CandidateSnapshot(receipt, codeFiles, dataFiles, receiptContent);
            }

            private static Dictionary<string, ReceiptFile> EnumerateOutputFiles(
                string root,
                OutputRootKind rootKind,
                bool candidate)
            {
                var files = new Dictionary<string, ReceiptFile>(StringComparer.OrdinalIgnoreCase);
                if (!Directory.Exists(root))
                {
                    return files;
                }

                AssertNotReparsePoint(root, "output root");
                var pending = new Stack<string>();
                pending.Push(root);
                long totalBytes = 0;
                while (pending.Count != 0)
                {
                    string directory = pending.Pop();
                    foreach (string childDirectory in Directory.EnumerateDirectories(directory))
                    {
                        AssertNotReparsePoint(childDirectory, "output directory");
                        pending.Push(childDirectory);
                    }

                    foreach (string file in Directory.EnumerateFiles(directory))
                    {
                        AssertNotReparsePoint(file, "output file");
                        string relative = GetRelativeOutputPath(root, file);
                        if (string.Equals(relative, ReceiptFileName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            if (candidate)
                            {
                                throw new InvalidOperationException(
                                    "Candidate output must not contain Unity metadata: " + relative);
                            }

                            continue;
                        }

                        var info = new FileInfo(file);
                        if (info.Length > PipelineMaximumFileBytes ||
                            files.Count >= PipelineMaximumFiles ||
                            totalBytes > PipelineMaximumTotalBytes - info.Length)
                        {
                            throw new InvalidOperationException("Generated output exceeds its bounded file budget: " + file);
                        }

                        var receiptFile = new ReceiptFile
                        {
                            Root = RootKindName(rootKind),
                            Path = relative,
                            Length = info.Length,
                            Sha256 = ComputeFileSha256(file),
                        };
                        if (!files.TryAdd(relative, receiptFile))
                        {
                            throw new InvalidOperationException(
                                "Generated output contains duplicate or case-colliding paths: " + relative);
                        }

                        totalBytes += info.Length;
                    }
                }

                return files;
            }

            private static Dictionary<string, ReceiptFile> EnumerateMetadataFiles(
                string root,
                OutputRootKind rootKind)
            {
                var files = new Dictionary<string, ReceiptFile>(StringComparer.OrdinalIgnoreCase);
                if (!Directory.Exists(root))
                {
                    return files;
                }

                AssertNotReparsePoint(root, "metadata output root");
                var pending = new Stack<string>();
                pending.Push(root);
                long totalBytes = 0;
                int visitedEntries = 0;
                while (pending.Count != 0)
                {
                    string directory = pending.Pop();
                    foreach (string childDirectory in Directory.EnumerateDirectories(directory))
                    {
                        if (++visitedEntries > PipelineMaximumFiles * 2)
                        {
                            throw new InvalidOperationException("Metadata traversal exceeds its entry budget.");
                        }

                        AssertNotReparsePoint(childDirectory, "metadata output directory");
                        pending.Push(childDirectory);
                    }

                    foreach (string file in Directory.EnumerateFiles(directory))
                    {
                        if (++visitedEntries > PipelineMaximumFiles * 2)
                        {
                            throw new InvalidOperationException("Metadata traversal exceeds its entry budget.");
                        }

                        AssertNotReparsePoint(file, "metadata output file");
                        string relative = GetRelativeOutputPath(root, file);
                        if (!relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string ownerRelative = relative.Substring(0, relative.Length - ".meta".Length);
                        if (ownerRelative.Length == 0)
                        {
                            throw new InvalidOperationException("Orphan Unity metadata is not allowed: " + file);
                        }

                        string ownerPath = ResolveRelativePath(root, ownerRelative, "metadata owner");
                        if (!File.Exists(ownerPath) && !Directory.Exists(ownerPath))
                        {
                            throw new InvalidOperationException("Orphan Unity metadata is not allowed: " + file);
                        }

                        var info = new FileInfo(file);
                        if (info.Length > PipelineMaximumFileBytes || files.Count >= PipelineMaximumFiles ||
                            totalBytes > PipelineMaximumTotalBytes - info.Length)
                        {
                            throw new InvalidOperationException("Unity metadata exceeds its bounded file budget: " + file);
                        }

                        var receiptFile = new ReceiptFile
                        {
                            Root = RootKindName(rootKind),
                            Path = relative,
                            Length = info.Length,
                            Sha256 = ComputeFileSha256(file),
                        };
                        if (!files.TryAdd(relative, receiptFile))
                        {
                            throw new InvalidOperationException(
                                "Unity metadata contains duplicate or case-colliding paths: " + relative);
                        }

                        totalBytes += info.Length;
                    }
                }

                return files;
            }

            private static void ValidateCombinedFileBudget(
                IEnumerable<ReceiptFile> files,
                string description)
            {
                long totalBytes = 0;
                int fileCount = 0;
                foreach (ReceiptFile file in files)
                {
                    if (++fileCount > PipelineMaximumFiles || file.Length < 0 ||
                        file.Length > PipelineMaximumFileBytes ||
                        totalBytes > PipelineMaximumTotalBytes - file.Length)
                    {
                        throw new InvalidOperationException(description + " exceeds the combined file budget.");
                    }

                    totalBytes += file.Length;
                }
            }

            private static string ComputeOutputAggregate(IEnumerable<ReceiptFile> files)
            {
                var builder = new StringBuilder();
                foreach (ReceiptFile file in files.OrderBy(static file => file.Path, StringComparer.Ordinal))
                {
                    builder.Append(file.Path).Append('\0')
                        .Append(file.Length).Append('\0')
                        .Append(file.Sha256).Append('\n');
                }

                return ComputeBytesSha256(Encoding.UTF8.GetBytes(builder.ToString()));
            }

            private static string GetReceiptPath(PipelineProfile profile)
            {
                return Path.Combine(profile.CodeOutputRoot, ReceiptFileName);
            }

            private static GenerationReceipt ReadAndValidateLiveReceipt(PipelineProfile profile)
            {
                string receiptPath = GetReceiptPath(profile);
                GenerationReceipt receipt = ReadState<GenerationReceipt>(receiptPath, "generation receipt");
                ValidateReceipt(receipt, profile);
                return receipt;
            }

            private static BaselineSnapshot CaptureBaseline(PipelineProfile profile)
            {
                string receiptPath = GetReceiptPath(profile);
                if (!File.Exists(receiptPath))
                {
                    EnsureUnreceiptedRootsEmpty(profile);
                    return new BaselineSnapshot(
                        receipt: null,
                        receiptSha256: string.Empty,
                        receiptLength: 0,
                        new Dictionary<string, ReceiptFile>(StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, ReceiptFile>(StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, ReceiptFile>(StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, ReceiptFile>(StringComparer.OrdinalIgnoreCase));
                }

                var receiptInfo = new FileInfo(receiptPath);
                if (receiptInfo.Length > PipelineMaximumFileBytes)
                {
                    throw new InvalidOperationException("Live generation receipt exceeds the per-file budget.");
                }

                string receiptSha256 = ComputeFileSha256(receiptPath);
                GenerationReceipt receipt = ReadAndValidateLiveReceipt(profile);
                ValidateLiveOutputs(profile, receipt, identity: null, requireCurrentIdentity: false);
                var baseline = new BaselineSnapshot(
                    receipt,
                    receiptSha256,
                    receiptInfo.Length,
                    receipt.Files.Where(static file => file.Root == "code")
                        .ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase),
                    receipt.Files.Where(static file => file.Root == "data")
                        .ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase),
                    EnumerateMetadataFiles(profile.CodeOutputRoot, OutputRootKind.Code),
                    EnumerateMetadataFiles(profile.DataOutputRoot, OutputRootKind.Data));
                ValidateCombinedFileBudget(
                    baseline.CodeFiles.Values.Concat(baseline.DataFiles.Values)
                        .Concat(baseline.CodeMetadata.Values)
                        .Concat(baseline.DataMetadata.Values),
                    "live baseline");
                ValidateBaselineUnchanged(profile, baseline);
                return baseline;
            }

            private static void ValidateBaselineUnchanged(
                PipelineProfile profile,
                BaselineSnapshot baseline)
            {
                string receiptPath = GetReceiptPath(profile);
                if (baseline.Receipt == null)
                {
                    if (File.Exists(receiptPath))
                    {
                        throw new InvalidOperationException(
                            "A generation receipt appeared after the empty baseline was captured.");
                    }

                    EnsureUnreceiptedRootsEmpty(profile);
                    return;
                }

                if (!File.Exists(receiptPath) || new FileInfo(receiptPath).Length != baseline.ReceiptLength ||
                    ComputeFileSha256(receiptPath) != baseline.ReceiptSha256)
                {
                    throw new InvalidOperationException(
                        "Live generation receipt changed after the immutable baseline was captured.");
                }

                GenerationReceipt currentReceipt = ReadAndValidateLiveReceipt(profile);
                if (currentReceipt.Generation != baseline.Receipt.Generation)
                {
                    throw new InvalidOperationException("Live baseline generation changed during candidate generation.");
                }

                ValidateLiveOutputs(profile, currentReceipt, identity: null, requireCurrentIdentity: false);
                ValidateExactOutputSet(
                    "code metadata",
                    baseline.CodeMetadata,
                    EnumerateMetadataFiles(profile.CodeOutputRoot, OutputRootKind.Code));
                ValidateExactOutputSet(
                    "data metadata",
                    baseline.DataMetadata,
                    EnumerateMetadataFiles(profile.DataOutputRoot, OutputRootKind.Data));
            }

            private static void EnsureUnreceiptedRootsEmpty(PipelineProfile profile)
            {
                EnsureOutputRootHasNoEntries(profile.CodeOutputRoot, "code");
                EnsureOutputRootHasNoEntries(profile.DataOutputRoot, "data");
            }

            private static void EnsureOutputRootHasNoEntries(string root, string description)
            {
                if (!Directory.Exists(root))
                {
                    return;
                }

                AssertNotReparsePoint(root, "unreceipted " + description + " output root");
                if (Directory.EnumerateFileSystemEntries(root).Any())
                {
                    throw new InvalidOperationException(
                        "Live " + description + " output is not empty but has no generation receipt. " +
                        "Move or remove the unowned entries after review; the pipeline does not adopt them.");
                }
            }

            private static void ValidateLiveOutputs(
                PipelineProfile profile,
                GenerationReceipt receipt,
                PipelineIdentity? identity,
                bool requireCurrentIdentity)
            {
                if (requireCurrentIdentity &&
                    (identity == null || receipt.ToolSha256 != identity.ToolHash ||
                     receipt.LubanSha256 != identity.LubanHash ||
                     receipt.SourceFingerprint != identity.SourceFingerprint ||
                     receipt.SchemaSha256 != identity.SchemaHash))
                {
                    throw new InvalidOperationException(
                        "Live output receipt does not match the current approved tool/source/schema identity.");
                }

                Dictionary<string, ReceiptFile> actualCode = EnumerateOutputFiles(
                    profile.CodeOutputRoot,
                    OutputRootKind.Code,
                    candidate: false);
                Dictionary<string, ReceiptFile> actualData = EnumerateOutputFiles(
                    profile.DataOutputRoot,
                    OutputRootKind.Data,
                    candidate: false);
                Dictionary<string, ReceiptFile> actualCodeMetadata = EnumerateMetadataFiles(
                    profile.CodeOutputRoot,
                    OutputRootKind.Code);
                Dictionary<string, ReceiptFile> actualDataMetadata = EnumerateMetadataFiles(
                    profile.DataOutputRoot,
                    OutputRootKind.Data);
                ValidateCombinedFileBudget(
                    actualCode.Values.Concat(actualData.Values)
                        .Concat(actualCodeMetadata.Values)
                        .Concat(actualDataMetadata.Values),
                    "live output");
                var expectedCode = receipt.Files
                    .Where(static file => file.Root == "code")
                    .ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);
                var expectedData = receipt.Files
                    .Where(static file => file.Root == "data")
                    .ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);
                ValidateExactOutputSet("code", expectedCode, actualCode);
                ValidateExactOutputSet("data", expectedData, actualData);
                if (ComputeOutputAggregate(actualCode.Values) != receipt.CodeOutputSha256 ||
                    ComputeOutputAggregate(actualData.Values) != receipt.DataOutputSha256)
                {
                    throw new InvalidOperationException("Live output aggregate hash differs from its receipt.");
                }
            }

            private static void ValidateExactOutputSet(
                string description,
                Dictionary<string, ReceiptFile> expected,
                Dictionary<string, ReceiptFile> actual)
            {
                if (expected.Count != actual.Count)
                {
                    throw new InvalidOperationException(
                        $"Live {description} output file count drifted: expected {expected.Count}, actual {actual.Count}.");
                }

                foreach (KeyValuePair<string, ReceiptFile> pair in expected)
                {
                    if (!actual.TryGetValue(pair.Key, out ReceiptFile? actualFile) ||
                        pair.Value.Path != actualFile.Path || pair.Value.Length != actualFile.Length ||
                        pair.Value.Sha256 != actualFile.Sha256)
                    {
                        throw new InvalidOperationException("Live output content drifted: " + description + "/" + pair.Key);
                    }
                }
            }

            private static void WriteDurableText(string path, string content, bool overwrite)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(
                    path,
                    overwrite ? FileMode.Create : FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }
    }
}
