using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private const string ReceiptSchema = "CycloneGames.DataTable.GenerationReceipt";
            private const string JournalSchema = "CycloneGames.DataTable.GenerationTransaction";
            private const int PipelineStateVersion = 1;
            private const int PipelineStateMaximumBytes = 64 * 1024 * 1024;

            private enum OutputRootKind
            {
                Code,
                Data,
            }

            private enum TransactionAction
            {
                Write,
                Delete,
            }

            private enum JournalState
            {
                Prepared,
                Publishing,
                Committed,
                RecoveryRequired,
            }

            private sealed class ReceiptFile
            {
                public string Root { get; set; } = string.Empty;
                public string Path { get; set; } = string.Empty;
                public long Length { get; set; }
                public string Sha256 { get; set; } = string.Empty;
            }

            private sealed class GenerationReceipt
            {
                public string Schema { get; set; } = ReceiptSchema;
                public int Version { get; set; } = PipelineStateVersion;
                public string Profile { get; set; } = string.Empty;
                public string Generation { get; set; } = string.Empty;
                public string ToolSha256 { get; set; } = string.Empty;
                public string LubanSha256 { get; set; } = string.Empty;
                public string SourceFingerprint { get; set; } = string.Empty;
                public string SchemaSha256 { get; set; } = string.Empty;
                public string CodeOutputSha256 { get; set; } = string.Empty;
                public string DataOutputSha256 { get; set; } = string.Empty;
                public ReceiptFile[] Files { get; set; } = Array.Empty<ReceiptFile>();
            }

            private sealed class TransactionOperationModel
            {
                public string Root { get; set; } = string.Empty;
                public string Path { get; set; } = string.Empty;
                public string Action { get; set; } = string.Empty;
                public bool HadOriginal { get; set; }
                public long PreviousLength { get; set; }
                public string PreviousSha256 { get; set; } = string.Empty;
                public long CandidateLength { get; set; }
                public string CandidateSha256 { get; set; } = string.Empty;
                public string BackupPath { get; set; } = string.Empty;
            }

            private sealed class TransactionJournal
            {
                public string Schema { get; set; } = JournalSchema;
                public int Version { get; set; } = PipelineStateVersion;
                public string RunId { get; set; } = string.Empty;
                public string Profile { get; set; } = string.Empty;
                public string ConfigurationSha256 { get; set; } = string.Empty;
                public string CodeOutputRoot { get; set; } = string.Empty;
                public string DataOutputRoot { get; set; } = string.Empty;
                public string State { get; set; } = JournalState.Prepared.ToString();
                public string Generation { get; set; } = string.Empty;
                public string PreviousGeneration { get; set; } = string.Empty;
                public string PreviousReceiptSha256 { get; set; } = string.Empty;
                public string[] CreatedDirectories { get; set; } = Array.Empty<string>();
                public TransactionOperationModel[] Operations { get; set; } = Array.Empty<TransactionOperationModel>();
            }

            private sealed class CandidateSnapshot
            {
                public CandidateSnapshot(
                    GenerationReceipt receipt,
                    Dictionary<string, ReceiptFile> codeFiles,
                    Dictionary<string, ReceiptFile> dataFiles,
                    string receiptContent)
                {
                    Receipt = receipt;
                    CodeFiles = codeFiles;
                    DataFiles = dataFiles;
                    ReceiptContent = receiptContent;
                }

                public GenerationReceipt Receipt { get; }
                public Dictionary<string, ReceiptFile> CodeFiles { get; }
                public Dictionary<string, ReceiptFile> DataFiles { get; }
                public string ReceiptContent { get; }
            }

            private sealed class BaselineSnapshot
            {
                public BaselineSnapshot(
                    GenerationReceipt? receipt,
                    string receiptSha256,
                    long receiptLength,
                    Dictionary<string, ReceiptFile> codeFiles,
                    Dictionary<string, ReceiptFile> dataFiles,
                    Dictionary<string, ReceiptFile> codeMetadata,
                    Dictionary<string, ReceiptFile> dataMetadata)
                {
                    Receipt = receipt;
                    ReceiptSha256 = receiptSha256;
                    ReceiptLength = receiptLength;
                    CodeFiles = codeFiles;
                    DataFiles = dataFiles;
                    CodeMetadata = codeMetadata;
                    DataMetadata = dataMetadata;
                }

                public GenerationReceipt? Receipt { get; }
                public string ReceiptSha256 { get; }
                public long ReceiptLength { get; }
                public Dictionary<string, ReceiptFile> CodeFiles { get; }
                public Dictionary<string, ReceiptFile> DataFiles { get; }
                public Dictionary<string, ReceiptFile> CodeMetadata { get; }
                public Dictionary<string, ReceiptFile> DataMetadata { get; }
            }

            private sealed class PipelineTransaction
            {
                public PipelineTransaction(PipelineConfiguration configuration, PipelineProfile profile, string runId)
                {
                    Configuration = configuration;
                    Profile = profile;
                    RunId = runId;
                    Root = Path.Combine(configuration.TransactionsRoot, runId);
                    CandidateCodeRoot = Path.Combine(Root, "candidate", "code");
                    CandidateDataRoot = Path.Combine(Root, "candidate", "data");
                    BackupRoot = Path.Combine(Root, "backup");
                    JournalPath = Path.Combine(Root, "journal.json");
                }

                public PipelineConfiguration Configuration { get; }
                public PipelineProfile Profile { get; }
                public string RunId { get; }
                public string Root { get; }
                public string CandidateCodeRoot { get; }
                public string CandidateDataRoot { get; }
                public string BackupRoot { get; }
                public string JournalPath { get; }
            }

            private static readonly JsonSerializerOptions PipelineJsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            };

            private static string SerializeState<T>(T value)
            {
                string content = JsonSerializer.Serialize(value, PipelineJsonOptions) + "\n";
                if (Encoding.UTF8.GetByteCount(content) > PipelineStateMaximumBytes)
                {
                    throw new InvalidOperationException("Pipeline state exceeds the 64 MiB limit.");
                }

                return content;
            }

            private static T ReadState<T>(string path, string description)
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > PipelineStateMaximumBytes)
                {
                    throw new InvalidOperationException(description + " is missing, empty, or oversized: " + path);
                }

                AssertNotReparsePoint(path, description);
                byte[] bytes = File.ReadAllBytes(path);
                RejectUtf8Bom(bytes, path);
                RejectDuplicateJsonProperties(bytes, description);
                T? value = JsonSerializer.Deserialize<T>(bytes, PipelineJsonOptions);
                return value ?? throw new InvalidOperationException(description + " deserialized to null: " + path);
            }

            private static void RejectDuplicateJsonProperties(byte[] bytes, string description)
            {
                var reader = new Utf8JsonReader(
                    bytes,
                    new JsonReaderOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 64,
                    });
                var objectProperties = new Stack<HashSet<string>>();
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                            break;
                        case JsonTokenType.EndObject:
                            objectProperties.Pop();
                            break;
                        case JsonTokenType.PropertyName:
                            string propertyName = reader.GetString() ?? string.Empty;
                            if (objectProperties.Count == 0 || !objectProperties.Peek().Add(propertyName))
                            {
                                throw new InvalidOperationException(
                                    description + " contains a duplicate JSON property: " + propertyName);
                            }

                            break;
                    }
                }
            }

            private static void ValidateReceipt(GenerationReceipt receipt, PipelineProfile profile)
            {
                if (receipt.Schema != ReceiptSchema || receipt.Version != PipelineStateVersion ||
                    !string.Equals(receipt.Profile, profile.Name, StringComparison.OrdinalIgnoreCase) ||
                    !IsSha256(receipt.Generation) || !IsSha256(receipt.ToolSha256) ||
                    !IsSha256(receipt.LubanSha256) || !IsSha256(receipt.SourceFingerprint) ||
                    !IsSha256(receipt.SchemaSha256) || !IsSha256(receipt.CodeOutputSha256) ||
                    !IsSha256(receipt.DataOutputSha256) || receipt.Files.Length > PipelineMaximumFiles)
                {
                    throw new InvalidOperationException("Generation receipt header is invalid or unsupported.");
                }

                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ReceiptFile file in receipt.Files)
                {
                    ParseRootKind(file.Root);
                    ValidatePortableRelativePath(file.Path, "receipt file path");
                    if (file.Length < 0 || file.Length > PipelineMaximumFileBytes || !IsSha256(file.Sha256) ||
                        !paths.Add(file.Root + ":" + file.Path))
                    {
                        throw new InvalidOperationException("Generation receipt contains an invalid or duplicate file entry.");
                    }
                }
            }

            private static void ValidateJournal(TransactionJournal journal, string expectedRunId)
            {
                if (journal.Schema != JournalSchema || journal.Version != PipelineStateVersion ||
                    !string.Equals(journal.RunId, expectedRunId, StringComparison.Ordinal) ||
                    !Enum.TryParse(journal.State, ignoreCase: false, out JournalState _) ||
                    !IsSha256(journal.Generation) || !IsSha256(journal.ConfigurationSha256) ||
                    journal.Operations.Length > PipelineMaximumFiles ||
                    journal.CreatedDirectories.Length > PipelineMaximumFiles ||
                    (journal.PreviousGeneration.Length == 0) != (journal.PreviousReceiptSha256.Length == 0) ||
                    (journal.PreviousGeneration.Length != 0 &&
                     (!IsSha256(journal.PreviousGeneration) || !IsSha256(journal.PreviousReceiptSha256))))
                {
                    throw new InvalidOperationException("Transaction journal header is invalid or unsupported.");
                }

                ValidatePortableName(journal.Profile, "journal profile", 128);
                ValidateJournalRootIdentity(journal.CodeOutputRoot, "journal code output root");
                ValidateJournalRootIdentity(journal.DataOutputRoot, "journal data output root");
                if (PathsOverlap(journal.CodeOutputRoot, journal.DataOutputRoot))
                {
                    throw new InvalidOperationException("Journal output roots must not contain one another.");
                }

                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string directory in journal.CreatedDirectories)
                {
                    if (directory.Length < 2 || directory[1] != ':' ||
                        (directory[0] != 'C' && directory[0] != 'D'))
                    {
                        throw new InvalidOperationException("Journal contains an invalid created-directory entry.");
                    }

                    if (!createdDirectories.Add(directory))
                    {
                        throw new InvalidOperationException(
                            "Journal contains a duplicate or case-colliding created-directory entry.");
                    }

                    if (directory.Length > 2)
                    {
                        ValidatePortableRelativePath(directory.Substring(2), "journal created directory");
                    }
                }

                long aggregateOperationBytes = 0;
                long aggregateBackupBytes = 0;
                foreach (TransactionOperationModel operation in journal.Operations)
                {
                    ParseRootKind(operation.Root);
                    if (!Enum.TryParse(operation.Action, ignoreCase: false, out TransactionAction action))
                    {
                        throw new InvalidOperationException("Journal contains an unsupported operation action.");
                    }

                    ValidatePortableRelativePath(operation.Path, "journal operation path");
                    ValidatePortableRelativePath(operation.BackupPath, "journal backup path");
                    if (!paths.Add(operation.Root + ":" + operation.Path) ||
                        operation.PreviousLength < 0 || operation.PreviousLength > PipelineMaximumFileBytes ||
                        operation.CandidateLength < 0 || operation.CandidateLength > PipelineMaximumFileBytes ||
                        operation.HadOriginal != IsSha256(operation.PreviousSha256) ||
                        (!operation.HadOriginal && operation.PreviousLength != 0) ||
                        (action == TransactionAction.Write) != IsSha256(operation.CandidateSha256) ||
                        (action == TransactionAction.Delete && operation.CandidateLength != 0))
                    {
                        throw new InvalidOperationException("Journal contains an invalid or duplicate operation.");
                    }


                    try
                    {
                        aggregateOperationBytes = checked(
                            aggregateOperationBytes + operation.PreviousLength + operation.CandidateLength);
                        if (operation.HadOriginal)
                        {
                            aggregateBackupBytes = checked(aggregateBackupBytes + operation.PreviousLength);
                        }
                    }
                    catch (OverflowException exception)
                    {
                        throw new InvalidOperationException("Journal byte accounting overflowed.", exception);
                    }

                    if (aggregateOperationBytes > PipelineMaximumTotalBytes ||
                        aggregateBackupBytes > PipelineMaximumTotalBytes)
                    {
                        throw new InvalidOperationException("Journal exceeds its operation or backup byte budget.");
                    }
                }
            }

            private static void ValidateJournalRootIdentity(string path, string description)
            {
                if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || !Path.IsPathFullyQualified(path))
                {
                    throw new InvalidOperationException(description + " is not a bounded absolute path.");
                }

                string canonical = Path.GetFullPath(path);
                if (!string.Equals(canonical, path, GetPathComparison()))
                {
                    throw new InvalidOperationException(description + " is not canonical.");
                }
            }

            private static OutputRootKind ParseRootKind(string value)
            {
                return value switch
                {
                    "code" => OutputRootKind.Code,
                    "data" => OutputRootKind.Data,
                    _ => throw new InvalidOperationException("Unsupported output root kind: " + value),
                };
            }

            private static string RootKindName(OutputRootKind rootKind)
            {
                return rootKind == OutputRootKind.Code ? "code" : "data";
            }

            private static string GetOutputRoot(PipelineProfile profile, OutputRootKind rootKind)
            {
                return rootKind == OutputRootKind.Code ? profile.CodeOutputRoot : profile.DataOutputRoot;
            }

            private static string GetCandidateRoot(PipelineTransaction transaction, OutputRootKind rootKind)
            {
                return rootKind == OutputRootKind.Code
                    ? transaction.CandidateCodeRoot
                    : transaction.CandidateDataRoot;
            }

            private static string ResolveRelativePath(string root, string relativePath, string description)
            {
                ValidatePortableRelativePath(relativePath, description);
                string path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsStrictPipelineChildPath(root, path))
                {
                    throw new InvalidOperationException(description + " escapes its root: " + relativePath);
                }

                return path;
            }

            private static bool IsStrictPipelineChildPath(string parentPath, string childPath)
            {
                string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath)) + Path.DirectorySeparatorChar;
                string child = Path.GetFullPath(childPath);
                return child.StartsWith(parent, GetPathComparison());
            }

            private static string GetRelativeOutputPath(string root, string path)
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                ValidatePortableRelativePath(relative, "generated output path");
                return relative;
            }
        }
    }
}
