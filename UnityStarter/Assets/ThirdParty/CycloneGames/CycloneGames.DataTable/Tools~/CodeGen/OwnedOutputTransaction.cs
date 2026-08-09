using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class StringConstantGenerator
        {
            private static OwnedOutputPlan BuildOwnedOutputPlan(
                string outputRoot,
                IReadOnlyCollection<StagedOutput> stagedOutputs)
            {
                if (stagedOutputs.Count > MAX_OWNED_OUTPUT_FILES)
                {
                    throw new InvalidOperationException(
                        $"Generated output count {stagedOutputs.Count} exceeds the owned-output limit {MAX_OWNED_OUTPUT_FILES}.");
                }

                string manifestPath = ResolveContainedOutputPath(
                    outputRoot,
                    Path.Combine(outputRoot, OWNED_OUTPUT_MANIFEST_FILE));
                if (Directory.Exists(manifestPath))
                {
                    throw new InvalidOperationException("Owned-output manifest path is a directory: " + manifestPath);
                }

                bool manifestExists = File.Exists(manifestPath);
                string[] previousRelativePaths = manifestExists
                    ? ReadOwnedOutputManifest(manifestPath)
                    : Array.Empty<string>();
                string[] nextRelativePaths = stagedOutputs
                    .Select(output => GetOwnedRelativePath(outputRoot, output.OutputPath))
                    .OrderBy(static path => path, StringComparer.Ordinal)
                    .ToArray();
                EnsureNoCaseCollidingOwnedPaths(nextRelativePaths, "generated output");

                string[] staleRelativePaths = CalculateStaleOwnedRelativePaths(previousRelativePaths, nextRelativePaths);
                var existingStalePaths = new List<string>(staleRelativePaths.Length);
                for (int i = 0; i < staleRelativePaths.Length; i++)
                {
                    string stalePath = ResolveOwnedOutputPath(outputRoot, staleRelativePaths[i]);
                    if (File.Exists(stalePath))
                    {
                        existingStalePaths.Add(stalePath);
                    }
                }

                bool manifestNeedsWrite = manifestExists
                    ? !previousRelativePaths.SequenceEqual(nextRelativePaths, StringComparer.Ordinal)
                    : nextRelativePaths.Length > 0;
                string manifestContent = manifestNeedsWrite
                    ? BuildOwnedOutputManifestContent(nextRelativePaths)
                    : string.Empty;
                return new OwnedOutputPlan(
                    manifestPath,
                    manifestNeedsWrite,
                    manifestContent,
                    existingStalePaths.ToArray(),
                    staleRelativePaths.Length - existingStalePaths.Count);
            }

            private static string[] CalculateStaleOwnedRelativePaths(
                IReadOnlyList<string> previousRelativePaths,
                IReadOnlyList<string> nextRelativePaths)
            {
                if (previousRelativePaths.Count > MAX_OWNED_OUTPUT_FILES ||
                    nextRelativePaths.Count > MAX_OWNED_OUTPUT_FILES)
                {
                    throw new InvalidOperationException(
                        $"Owned-output path count exceeds the {MAX_OWNED_OUTPUT_FILES}-file limit.");
                }

                var validatedPreviousPaths = new string[previousRelativePaths.Count];
                for (int i = 0; i < previousRelativePaths.Count; i++)
                {
                    validatedPreviousPaths[i] = ValidateOwnedRelativePath(previousRelativePaths[i]);
                }

                var validatedNextPaths = new string[nextRelativePaths.Count];
                for (int i = 0; i < nextRelativePaths.Count; i++)
                {
                    validatedNextPaths[i] = ValidateOwnedRelativePath(nextRelativePaths[i]);
                }

                EnsureNoCaseCollidingOwnedPaths(validatedPreviousPaths, "previous owned-output set");
                EnsureNoCaseCollidingOwnedPaths(validatedNextPaths, "next owned-output set");
                EnsureCompatibleOwnedPathCasing(validatedPreviousPaths, validatedNextPaths);
                var nextPaths = new HashSet<string>(validatedNextPaths, StringComparer.Ordinal);
                var stalePaths = new List<string>();
                for (int i = 0; i < validatedPreviousPaths.Length; i++)
                {
                    string previousPath = validatedPreviousPaths[i];
                    if (!nextPaths.Contains(previousPath))
                    {
                        stalePaths.Add(previousPath);
                    }
                }

                stalePaths.Sort(StringComparer.Ordinal);
                return stalePaths.ToArray();
            }

            private static string[] ReadOwnedOutputManifest(string manifestPath)
            {
                using var stream = new FileStream(
                    manifestPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                long length = stream.Length;
                if (length > MAX_OWNED_OUTPUT_MANIFEST_BYTES)
                {
                    throw new InvalidOperationException(
                        $"Owned-output manifest size {length} bytes exceeds the limit " +
                        $"{MAX_OWNED_OUTPUT_MANIFEST_BYTES}: {manifestPath}");
                }

                var content = new byte[(int)length];
                int offset = 0;
                while (offset < content.Length)
                {
                    int read = stream.Read(content, offset, content.Length - offset);
                    if (read == 0)
                    {
                        throw new InvalidOperationException(
                            "Owned-output manifest changed or was truncated while being read: " + manifestPath);
                    }

                    offset += read;
                }

                if (stream.ReadByte() >= 0)
                {
                    throw new InvalidOperationException(
                        "Owned-output manifest changed or exceeded its size limit while being read: " + manifestPath);
                }

                using var boundedContent = new MemoryStream(content, writable: false);
                return ParseOwnedOutputManifest(boundedContent, manifestPath);
            }

            private static string[] ParseOwnedOutputManifest(Stream stream, string sourceDescription)
            {
                using JsonDocument document = JsonDocument.Parse(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 8,
                    });
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Owned-output manifest root must be an object: " + sourceDescription);
                }

                bool sawSchema = false;
                bool sawVersion = false;
                bool sawOwnedFiles = false;
                string schema = string.Empty;
                int version = 0;
                JsonElement ownedFiles = default;
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "schema":
                            if (sawSchema || property.Value.ValueKind != JsonValueKind.String)
                            {
                                throw new InvalidOperationException("Owned-output manifest has an invalid or duplicate 'schema'.");
                            }

                            sawSchema = true;
                            schema = property.Value.GetString() ?? string.Empty;
                            break;
                        case "version":
                            if (sawVersion || !property.Value.TryGetInt32(out version))
                            {
                                throw new InvalidOperationException("Owned-output manifest has an invalid or duplicate 'version'.");
                            }

                            sawVersion = true;
                            break;
                        case "ownedFiles":
                            if (sawOwnedFiles || property.Value.ValueKind != JsonValueKind.Array)
                            {
                                throw new InvalidOperationException("Owned-output manifest has an invalid or duplicate 'ownedFiles'.");
                            }

                            sawOwnedFiles = true;
                            ownedFiles = property.Value;
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Owned-output manifest contains unsupported property '{property.Name}'.");
                    }
                }

                if (!sawSchema || !string.Equals(schema, OWNED_OUTPUT_MANIFEST_SCHEMA, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Owned-output manifest schema is missing or unsupported.");
                }

                if (!sawVersion || version != OWNED_OUTPUT_MANIFEST_VERSION)
                {
                    throw new InvalidOperationException(
                        $"Owned-output manifest version {version} is unsupported; expected {OWNED_OUTPUT_MANIFEST_VERSION}.");
                }

                if (!sawOwnedFiles || ownedFiles.GetArrayLength() > MAX_OWNED_OUTPUT_FILES)
                {
                    throw new InvalidOperationException(
                        $"Owned-output manifest file count is missing or exceeds {MAX_OWNED_OUTPUT_FILES}.");
                }

                var relativePaths = new List<string>(ownedFiles.GetArrayLength());
                foreach (JsonElement file in ownedFiles.EnumerateArray())
                {
                    if (file.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException("Owned-output manifest paths must be strings.");
                    }

                    relativePaths.Add(ValidateOwnedRelativePath(file.GetString() ?? string.Empty));
                }

                string[] result = relativePaths.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
                EnsureNoCaseCollidingOwnedPaths(result, "owned-output manifest");
                return result;
            }

            private static string BuildOwnedOutputManifestContent(IReadOnlyList<string> relativePaths)
            {
                if (relativePaths.Count > MAX_OWNED_OUTPUT_FILES)
                {
                    throw new InvalidOperationException(
                        $"Owned-output manifest file count exceeds {MAX_OWNED_OUTPUT_FILES}.");
                }

                var validatedPaths = new string[relativePaths.Count];
                for (int i = 0; i < relativePaths.Count; i++)
                {
                    validatedPaths[i] = ValidateOwnedRelativePath(relativePaths[i]);
                }

                EnsureNoCaseCollidingOwnedPaths(validatedPaths, "owned-output manifest");
                Array.Sort(validatedPaths, StringComparer.Ordinal);
                using var stream = new MemoryStream(4096);
                using (var writer = new Utf8JsonWriter(
                           stream,
                           new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteString("schema", OWNED_OUTPUT_MANIFEST_SCHEMA);
                    writer.WriteNumber("version", OWNED_OUTPUT_MANIFEST_VERSION);
                    writer.WritePropertyName("ownedFiles");
                    writer.WriteStartArray();
                    for (int i = 0; i < validatedPaths.Length; i++)
                    {
                        writer.WriteStringValue(validatedPaths[i]);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                if (stream.Length + 1 > MAX_OWNED_OUTPUT_MANIFEST_BYTES)
                {
                    throw new InvalidOperationException(
                        $"Owned-output manifest exceeds the {MAX_OWNED_OUTPUT_MANIFEST_BYTES}-byte limit.");
                }

                return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
            }

            private static string GetOwnedRelativePath(string outputRoot, string outputPath)
            {
                EnsureStrictChildPath(outputRoot, outputPath, "owned generated output");
                string relativePath = Path.GetRelativePath(Path.GetFullPath(outputRoot), Path.GetFullPath(outputPath))
                    .Replace('\\', '/');
                return ValidateOwnedRelativePath(relativePath);
            }

            private static string ResolveOwnedOutputPath(string outputRoot, string relativePath)
            {
                string validatedRelativePath = ValidateOwnedRelativePath(relativePath);
                string platformPath = validatedRelativePath.Replace('/', Path.DirectorySeparatorChar);
                return ResolveContainedOutputPath(outputRoot, Path.Combine(outputRoot, platformPath));
            }

            private static string ValidateOwnedRelativePath(string relativePath)
            {
                if (string.IsNullOrEmpty(relativePath) ||
                    relativePath.Length > MAX_OWNED_RELATIVE_PATH_CHARACTERS ||
                    Path.IsPathRooted(relativePath) ||
                    relativePath.IndexOf('\\') >= 0 ||
                    relativePath.IndexOf(':') >= 0 ||
                    !relativePath.EndsWith(".cs", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Invalid owned-output relative .cs path: " + relativePath);
                }

                string[] segments = relativePath.Split('/');
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                    {
                        throw new InvalidOperationException("Owned-output path contains an empty or traversal segment: " + relativePath);
                    }

                    if (char.IsWhiteSpace(segments[i][0]) ||
                        char.IsWhiteSpace(segments[i][segments[i].Length - 1]) ||
                        segments[i][segments[i].Length - 1] == '.' ||
                        IsReservedWindowsOwnedPathSegment(segments[i]))
                    {
                        throw new InvalidOperationException(
                            "Owned-output path contains a non-portable segment: " + relativePath);
                    }

                    for (int j = 0; j < segments[i].Length; j++)
                    {
                        char character = segments[i][j];
                        bool asciiLetterOrDigit =
                            character >= 'A' && character <= 'Z' ||
                            character >= 'a' && character <= 'z' ||
                            character >= '0' && character <= '9';
                        if (!asciiLetterOrDigit && character != '_' && character != '.')
                        {
                            throw new InvalidOperationException(
                                "Owned-output paths use the generated ASCII identifier character set only: " + relativePath);
                        }
                    }
                }

                return relativePath;
            }

            private static bool IsReservedWindowsOwnedPathSegment(string segment)
            {
                int dotIndex = segment.IndexOf('.');
                string baseName = dotIndex >= 0 ? segment.Substring(0, dotIndex) : segment;
                if (string.Equals(baseName, "CON", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, "PRN", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, "AUX", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, "NUL", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, "CLOCK$", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (baseName.Length != 4)
                {
                    return false;
                }

                char suffix = baseName[3];
                return suffix >= '1' && suffix <= '9' &&
                       (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                        baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
            }

            private static void EnsureNoCaseCollidingOwnedPaths(IEnumerable<string> paths, string description)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var directoryCasing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    if (!seen.Add(path))
                    {
                        throw new InvalidOperationException(
                            $"{description} contains duplicate or case-colliding path: {path}");
                    }

                    int separatorIndex = path.IndexOf('/');
                    while (separatorIndex >= 0)
                    {
                        string directoryPrefix = path.Substring(0, separatorIndex);
                        if (directoryCasing.TryGetValue(directoryPrefix, out string? existingPrefix) &&
                            !string.Equals(existingPrefix, directoryPrefix, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"{description} contains case-colliding directory paths: " +
                                $"'{existingPrefix}' and '{directoryPrefix}'.");
                        }

                        directoryCasing[directoryPrefix] = directoryPrefix;
                        separatorIndex = path.IndexOf('/', separatorIndex + 1);
                    }
                }
            }

            private static void EnsureCompatibleOwnedPathCasing(
                IReadOnlyList<string> previousRelativePaths,
                IReadOnlyList<string> nextRelativePaths)
            {
                var exactPaths = new HashSet<string>(StringComparer.Ordinal);
                var combinedPaths = new List<string>(previousRelativePaths.Count + nextRelativePaths.Count);
                for (int i = 0; i < previousRelativePaths.Count; i++)
                {
                    if (exactPaths.Add(previousRelativePaths[i]))
                    {
                        combinedPaths.Add(previousRelativePaths[i]);
                    }
                }

                for (int i = 0; i < nextRelativePaths.Count; i++)
                {
                    if (exactPaths.Add(nextRelativePaths[i]))
                    {
                        combinedPaths.Add(nextRelativePaths[i]);
                    }
                }

                try
                {
                    EnsureNoCaseCollidingOwnedPaths(combinedPaths, "owned-output transition");
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException(
                        "Case-only output file or directory changes are not automatic. " +
                        "Perform an explicit two-step filesystem/version-control rename and reset the owned-output manifest " +
                        "after auditing obsolete generated files. Details: " + exception.Message,
                        exception);
                }
            }

            private enum CommitFaultPoint
            {
                AfterStaleOutputRemoved,
                AfterOutputCommitted,
                BeforeManifestCommitted,
            }

            private sealed class OwnedOutputSession : IDisposable
            {
                private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);
                private readonly string _outputRoot;
                private readonly bool _validateOnly;
                private readonly Dictionary<string, StagedOutput> _outputs =
                    new Dictionary<string, StagedOutput>(StringComparer.OrdinalIgnoreCase);
                private readonly Action<CommitFaultPoint, string>? _faultInjector;
                private string? _stagingRoot;
                private OwnedOutputPlan? _plan;
                private long _totalCharacters;
                private bool _createdOutputRoot;
                private bool _planBuilt;
                private bool _preserveStaging;
                private bool _committed;
                private bool _disposed;

                public OwnedOutputSession(
                    string outputRoot,
                    bool validateOnly,
                    Action<CommitFaultPoint, string>? faultInjector = null)
                {
                    _outputRoot = Path.GetFullPath(outputRoot);
                    _validateOnly = validateOnly;
                    _faultInjector = faultInjector;
                }

                public int Count => _outputs.Count;

                public void Stage(string outputPath, Action<TextWriter> writeContent)
                {
                    ThrowIfUnavailable();
                    if (_planBuilt)
                    {
                        throw new InvalidOperationException("Generated outputs cannot be staged after the plan is built.");
                    }

                    ArgumentNullException.ThrowIfNull(writeContent);
                    string validatedOutputPath = ResolveContainedOutputPath(_outputRoot, outputPath);
                    if (_outputs.ContainsKey(validatedOutputPath))
                    {
                        throw new InvalidOperationException(
                            "Generated output path collision (case-insensitive for cross-platform safety): " +
                            validatedOutputPath);
                    }

                    if (_outputs.Count >= MAX_OWNED_OUTPUT_FILES)
                    {
                        throw new InvalidOperationException(
                            $"Generated output count would exceed the owned-output limit {MAX_OWNED_OUTPUT_FILES}.");
                    }

                    string stagingRoot = EnsureStaging();
                    string stagedFilesRoot = Path.Combine(stagingRoot, "files");
                    string relativePath = GetOwnedRelativePath(_outputRoot, validatedOutputPath)
                        .Replace('/', Path.DirectorySeparatorChar);
                    string stagedPath = Path.GetFullPath(Path.Combine(stagedFilesRoot, relativePath));
                    EnsureStrictChildPath(stagedFilesRoot, stagedPath, "staged generated output");
                    string? stagedDirectory = Path.GetDirectoryName(stagedPath);
                    if (!string.IsNullOrEmpty(stagedDirectory))
                    {
                        Directory.CreateDirectory(stagedDirectory);
                    }

                    try
                    {
                        long characterLength;
                        using (var stream = new FileStream(
                                   stagedPath,
                                   FileMode.CreateNew,
                                   FileAccess.Write,
                                   FileShare.None,
                                   65536,
                                   FileOptions.SequentialScan))
                        using (var streamWriter = new StreamWriter(stream, Utf8NoBom, 65536, leaveOpen: false))
                        using (var writer = new BoundedTextWriter(
                                   streamWriter,
                                   MAX_GENERATED_FILE_CHARACTERS,
                                   MAX_TOTAL_GENERATED_CHARACTERS - _totalCharacters))
                        {
                            writeContent(writer);
                            writer.Flush();
                            characterLength = writer.CharacterCount;
                        }

                        var file = new FileInfo(stagedPath);
                        string sha256 = ComputeSha256(stagedPath);
                        var output = new StagedOutput(
                            validatedOutputPath,
                            stagedPath,
                            sha256,
                            file.Length);
                        _outputs.Add(validatedOutputPath, output);
                        _totalCharacters = checked(_totalCharacters + characterLength);
                    }
                    catch
                    {
                        TryDeleteFile(stagedPath);
                        throw;
                    }
                }

                public OwnedOutputPlan BuildPlan()
                {
                    ThrowIfUnavailable();
                    if (!_planBuilt)
                    {
                        _plan = BuildOwnedOutputPlan(_outputRoot, _outputs.Values);
                        _planBuilt = true;
                    }

                    return _plan!;
                }

                public void Commit(OwnedOutputPlan plan)
                {
                    ThrowIfUnavailable();
                    ArgumentNullException.ThrowIfNull(plan);
                    if (!_planBuilt || !ReferenceEquals(_plan, plan))
                    {
                        throw new InvalidOperationException("The commit plan does not belong to this frozen output session.");
                    }

                    if (_validateOnly)
                    {
                        throw new InvalidOperationException("A validation-only output session cannot commit.");
                    }

                    foreach (StagedOutput output in _outputs.Values)
                    {
                        if (!FileContentsEqual(output.StagedPath, output))
                        {
                            throw new InvalidOperationException(
                                "Staged generated output changed after its hash receipt was recorded: " +
                                output.OutputPath);
                        }
                    }

                    StagedOutput[] changedOutputs = _outputs.Values
                        .Where(static output => !FileContentsEqual(output.OutputPath, output))
                        .OrderBy(static output => output.OutputPath, StringComparer.Ordinal)
                        .ToArray();
                    if (changedOutputs.Length == 0 &&
                        plan.ExistingStaleOutputPaths.Length == 0 &&
                        !plan.ManifestNeedsWrite)
                    {
                        _committed = true;
                        return;
                    }

                    string stagingRoot = EnsureStaging();
                    string backupFilesRoot = Path.Combine(stagingRoot, "backup");
                    string stagedManifestPath = Path.Combine(
                        stagingRoot,
                        "manifest",
                        OWNED_OUTPUT_MANIFEST_FILE);
                    if (plan.ManifestNeedsWrite)
                    {
                        string? manifestDirectory = Path.GetDirectoryName(stagedManifestPath);
                        if (!string.IsNullOrEmpty(manifestDirectory))
                        {
                            Directory.CreateDirectory(manifestDirectory);
                        }

                        File.WriteAllText(stagedManifestPath, plan.ManifestContent, Utf8NoBom);
                    }

                    var committed = new List<(string OutputPath, string BackupPath, bool HadOriginal)>(
                        changedOutputs.Length + plan.ExistingStaleOutputPaths.Length + 1);
                    try
                    {
                        for (int i = 0; i < plan.ExistingStaleOutputPaths.Length; i++)
                        {
                            string stalePath = plan.ExistingStaleOutputPaths[i];
                            string relativePath = GetOwnedRelativePath(_outputRoot, stalePath)
                                .Replace('/', Path.DirectorySeparatorChar);
                            string backupPath = Path.GetFullPath(Path.Combine(backupFilesRoot, relativePath));
                            EnsureStrictChildPath(backupFilesRoot, backupPath, "stale-output backup");
                            CreateParentDirectory(backupPath);
                            File.Move(stalePath, backupPath);
                            committed.Add((stalePath, backupPath, true));
                            _faultInjector?.Invoke(CommitFaultPoint.AfterStaleOutputRemoved, stalePath);
                            Console.WriteLine("[DataTable.CodeGen] Removed stale owned output: " + stalePath);
                        }

                        for (int i = 0; i < changedOutputs.Length; i++)
                        {
                            StagedOutput output = changedOutputs[i];
                            string relativePath = GetOwnedRelativePath(_outputRoot, output.OutputPath)
                                .Replace('/', Path.DirectorySeparatorChar);
                            string backupPath = Path.GetFullPath(Path.Combine(backupFilesRoot, relativePath));
                            EnsureStrictChildPath(backupFilesRoot, backupPath, "generated-output backup");
                            CreateParentDirectory(output.OutputPath);
                            bool hadOriginal = File.Exists(output.OutputPath);
                            if (hadOriginal)
                            {
                                CreateParentDirectory(backupPath);
                                File.Move(output.OutputPath, backupPath);
                            }

                            committed.Add((output.OutputPath, backupPath, hadOriginal));
                            File.Move(output.StagedPath, output.OutputPath);
                            _faultInjector?.Invoke(CommitFaultPoint.AfterOutputCommitted, output.OutputPath);
                            Console.WriteLine("[DataTable.CodeGen] Committed: " + output.OutputPath);
                        }

                        if (plan.ManifestNeedsWrite)
                        {
                            _faultInjector?.Invoke(CommitFaultPoint.BeforeManifestCommitted, plan.ManifestPath);
                            string manifestBackupPath = Path.Combine(
                                backupFilesRoot,
                                "manifest",
                                OWNED_OUTPUT_MANIFEST_FILE);
                            bool hadManifest = File.Exists(plan.ManifestPath);
                            if (hadManifest)
                            {
                                CreateParentDirectory(manifestBackupPath);
                                File.Move(plan.ManifestPath, manifestBackupPath);
                            }

                            CreateParentDirectory(plan.ManifestPath);
                            committed.Add((plan.ManifestPath, manifestBackupPath, hadManifest));
                            File.Move(stagedManifestPath, plan.ManifestPath);
                            Console.WriteLine("[DataTable.CodeGen] Committed owned-output manifest: " + plan.ManifestPath);
                        }

                        _committed = true;
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        string rollbackError = RollBackCommittedOutputs(committed);
                        _preserveStaging = !string.IsNullOrEmpty(rollbackError);
                        throw new InvalidOperationException(
                            string.IsNullOrEmpty(rollbackError)
                                ? "Code generation commit failed; previously committed files were restored."
                                : "Code generation commit failed and rollback was incomplete. " +
                                  $"Recovery files were preserved at '{stagingRoot}'. Details: {rollbackError}",
                            exception);
                    }
                }

                public void Dispose()
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    if (_preserveStaging)
                    {
                        return;
                    }

                    try
                    {
                        if (_stagingRoot != null && Directory.Exists(_stagingRoot))
                        {
                            Directory.Delete(_stagingRoot, recursive: true);
                        }

                        if (_createdOutputRoot && !_committed && Directory.Exists(_outputRoot))
                        {
                            Directory.Delete(_outputRoot, recursive: false);
                        }
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        Console.Error.WriteLine(
                            "[DataTable.CodeGen] Warning: failed to remove rebuildable staging directory: " +
                            exception.Message);
                    }
                }

                private string EnsureStaging()
                {
                    if (_stagingRoot != null)
                    {
                        return _stagingRoot;
                    }

                    if (_validateOnly)
                    {
                        _stagingRoot = Path.Combine(
                            Path.GetTempPath(),
                            "cyclonegames-datatable-codegen-" + Guid.NewGuid().ToString("N"));
                    }
                    else
                    {
                        _createdOutputRoot = !Directory.Exists(_outputRoot);
                        Directory.CreateDirectory(_outputRoot);
                        _stagingRoot = ResolveContainedOutputPath(
                            _outputRoot,
                            Path.Combine(_outputRoot, ".datatable-codegen-" + Guid.NewGuid().ToString("N")));
                    }

                    Directory.CreateDirectory(_stagingRoot);
                    return _stagingRoot;
                }

                private void ThrowIfUnavailable()
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_committed)
                    {
                        throw new InvalidOperationException("The owned-output session has already committed.");
                    }
                }

                private static bool FileContentsEqual(string path, StagedOutput staged)
                {
                    if (!File.Exists(path))
                    {
                        return false;
                    }

                    var file = new FileInfo(path);
                    return file.Length == staged.ByteLength &&
                           string.Equals(ComputeSha256(path), staged.Sha256, StringComparison.Ordinal);
                }

                private static string ComputeSha256(string path)
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        65536,
                        FileOptions.SequentialScan);
                    return Convert.ToHexString(SHA256.HashData(stream));
                }

                private static void CreateParentDirectory(string path)
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }

                private static void TryDeleteFile(string path)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        Console.Error.WriteLine(
                            "[DataTable.CodeGen] Warning: failed to remove incomplete staged output: " +
                            exception.Message);
                    }
                }
            }

            private sealed class BoundedTextWriter : TextWriter
            {
                private readonly TextWriter _inner;
                private readonly long _maximumFileCharacters;
                private readonly long _maximumRemainingCharacters;

                public BoundedTextWriter(
                    TextWriter inner,
                    long maximumFileCharacters,
                    long maximumRemainingCharacters)
                {
                    _inner = inner;
                    _maximumFileCharacters = maximumFileCharacters;
                    _maximumRemainingCharacters = maximumRemainingCharacters;
                }

                public override Encoding Encoding => _inner.Encoding;

                public long CharacterCount { get; private set; }

                public override void Write(char value)
                {
                    Charge(1);
                    _inner.Write(value);
                }

                public override void Write(char[] buffer, int index, int count)
                {
                    Charge(count);
                    _inner.Write(buffer, index, count);
                }

                public override void Write(string? value)
                {
                    int count = value?.Length ?? 0;
                    Charge(count);
                    _inner.Write(value);
                }

                public override void Write(ReadOnlySpan<char> buffer)
                {
                    Charge(buffer.Length);
                    _inner.Write(buffer);
                }

                public override void Flush()
                {
                    _inner.Flush();
                }

                private void Charge(int count)
                {
                    long next = checked(CharacterCount + count);
                    if (next > _maximumFileCharacters)
                    {
                        throw new InvalidOperationException(
                            $"Generated file exceeds the {_maximumFileCharacters}-character limit.");
                    }

                    if (next > _maximumRemainingCharacters)
                    {
                        throw new InvalidOperationException(
                            $"Generated output exceeds the total {MAX_TOTAL_GENERATED_CHARACTERS}-character budget.");
                    }

                    CharacterCount = next;
                }
            }

            private static string RollBackCommittedOutputs(
                List<(string OutputPath, string BackupPath, bool HadOriginal)> committed)
            {
                StringBuilder? errors = null;
                for (int i = committed.Count - 1; i >= 0; i--)
                {
                    var item = committed[i];
                    if (item.HadOriginal && !File.Exists(item.BackupPath))
                    {
                        AppendRollbackError(
                            ref errors,
                            item.OutputPath,
                            "required original-file backup is missing; the current target was left untouched");
                        continue;
                    }

                    try
                    {
                        if (File.Exists(item.OutputPath))
                        {
                            File.Delete(item.OutputPath);
                        }

                        if (item.HadOriginal)
                        {
                            File.Move(item.BackupPath, item.OutputPath);
                        }
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        AppendRollbackError(ref errors, item.OutputPath, exception.Message);
                    }
                }

                return errors?.ToString() ?? string.Empty;
            }

            private static void AppendRollbackError(
                ref StringBuilder? errors,
                string outputPath,
                string message)
            {
                errors ??= new StringBuilder();
                if (errors.Length > 0)
                {
                    errors.Append(" | ");
                }

                errors.Append(outputPath).Append(": ").Append(message);
            }

            private static string ResolveContainedFile(string rootDirectory, string relativePath, string description)
            {
                if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                {
                    throw new InvalidOperationException($"{description} path must be relative to the data directory: {relativePath}");
                }

                string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
                EnsureStrictChildPath(rootDirectory, fullPath, description);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException(description + " not found.", fullPath);
                }

                return fullPath;
            }

            private static string ResolveContainedOutputPath(string outputRoot, string candidatePath)
            {
                string fullPath = Path.GetFullPath(candidatePath);
                EnsureStrictChildPath(outputRoot, fullPath, "generated output");
                return fullPath;
            }
        }

        private static void ValidateFileSize(string path, long maximumBytes, string description)
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException(description + " not found.", path);
            }

            if (file.Length > maximumBytes)
            {
                throw new InvalidOperationException(
                    $"{description} size {file.Length} bytes exceeds the limit {maximumBytes}: {path}");
            }
        }

        private static void EnsureStrictChildPath(string parentPath, string childPath, string description)
        {
            string parent = EnsureTrailingDirectorySeparator(ResolvePathForContainment(parentPath));
            string child = ResolvePathForContainment(childPath);
            if (!child.StartsWith(parent, GetPathComparison()))
            {
                throw new InvalidOperationException(
                    $"{description} path escapes its approved root:\n  Root: {parentPath}\n  Path: {childPath}");
            }
        }

        private static bool PathsOverlap(string firstPath, string secondPath)
        {
            string first = ResolvePathForContainment(firstPath);
            string second = ResolvePathForContainment(secondPath);
            return string.Equals(
                       Path.TrimEndingDirectorySeparator(first),
                       Path.TrimEndingDirectorySeparator(second),
                       GetPathComparison()) ||
                   first.StartsWith(EnsureTrailingDirectorySeparator(second), GetPathComparison()) ||
                   second.StartsWith(EnsureTrailingDirectorySeparator(first), GetPathComparison());
        }

        private static string ResolvePathForContainment(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("Path has no filesystem root: " + path);
            string current = root;
            string relative = fullPath.Substring(root.Length);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < segments.Length; i++)
            {
                string next = Path.Combine(current, segments[i]);
                FileSystemInfo? info = Directory.Exists(next)
                    ? new DirectoryInfo(next)
                    : File.Exists(next) ? new FileInfo(next) : null;
                if (info == null)
                {
                    current = next;
                    continue;
                }

                FileSystemInfo? target = info.ResolveLinkTarget(true);
                current = target == null ? info.FullName : target.FullName;
            }

            return Path.GetFullPath(current);
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            char last = path[path.Length - 1];
            return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
            return OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
