using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class StringConstantGenerator
        {
            private static void ValidatePendingOutputBudget(Dictionary<string, PendingOutput> pendingOutputs)
            {
                long totalCharacters = 0;
                foreach (PendingOutput output in pendingOutputs.Values)
                {
                    totalCharacters = checked(totalCharacters + output.Content.Length);
                    if (totalCharacters > MAX_TOTAL_GENERATED_CHARACTERS)
                    {
                        throw new InvalidOperationException(
                            $"Generated output exceeds the total {MAX_TOTAL_GENERATED_CHARACTERS}-character budget.");
                    }
                }
            }

            private static OwnedOutputPlan BuildOwnedOutputPlan(
                string outputRoot,
                Dictionary<string, PendingOutput> pendingOutputs)
            {
                if (pendingOutputs.Count > MAX_OWNED_OUTPUT_FILES)
                {
                    throw new InvalidOperationException(
                        $"Generated output count {pendingOutputs.Count} exceeds the owned-output limit {MAX_OWNED_OUTPUT_FILES}.");
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
                string[] nextRelativePaths = pendingOutputs.Values
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

            private static void CommitOutputs(
                string outputRoot,
                Dictionary<string, PendingOutput> pendingOutputs,
                OwnedOutputPlan ownedOutputPlan)
            {
                if (pendingOutputs.Count == 0 &&
                    ownedOutputPlan.ExistingStaleOutputPaths.Length == 0 &&
                    !ownedOutputPlan.ManifestNeedsWrite)
                {
                    return;
                }

                Directory.CreateDirectory(outputRoot);
                string stagingRoot = ResolveContainedOutputPath(
                    outputRoot,
                    Path.Combine(outputRoot, ".datatable-codegen-" + Guid.NewGuid().ToString("N")));
                string stagedFilesRoot = Path.Combine(stagingRoot, "files");
                string backupFilesRoot = Path.Combine(stagingRoot, "backup");
                string stagedManifestPath = ResolveContainedOutputPath(
                    stagingRoot,
                    Path.Combine(stagingRoot, "manifest", OWNED_OUTPUT_MANIFEST_FILE));
                var orderedOutputs = pendingOutputs.Values
                    .OrderBy(static output => output.OutputPath, StringComparer.Ordinal)
                    .ToArray();
                var committed = new List<(string OutputPath, string BackupPath, bool HadOriginal)>(
                    orderedOutputs.Length + ownedOutputPlan.ExistingStaleOutputPaths.Length + 1);
                bool preserveStagingDirectory = true;

                try
                {
                    for (int i = 0; i < orderedOutputs.Length; i++)
                    {
                        PendingOutput output = orderedOutputs[i];
                        string relativePath = GetOwnedRelativePath(outputRoot, output.OutputPath)
                            .Replace('/', Path.DirectorySeparatorChar);
                        string stagedPath = ResolveContainedOutputPath(
                            stagedFilesRoot,
                            Path.Combine(stagedFilesRoot, relativePath));
                        string? stagedDirectory = Path.GetDirectoryName(stagedPath);
                        if (!string.IsNullOrEmpty(stagedDirectory))
                        {
                            Directory.CreateDirectory(stagedDirectory);
                        }

                        File.WriteAllText(stagedPath, output.Content, new UTF8Encoding(false));
                    }

                    if (ownedOutputPlan.ManifestNeedsWrite)
                    {
                        string? stagedManifestDirectory = Path.GetDirectoryName(stagedManifestPath);
                        if (!string.IsNullOrEmpty(stagedManifestDirectory))
                        {
                            Directory.CreateDirectory(stagedManifestDirectory);
                        }

                        File.WriteAllText(
                            stagedManifestPath,
                            ownedOutputPlan.ManifestContent,
                            new UTF8Encoding(false));
                    }

                    for (int i = 0; i < ownedOutputPlan.ExistingStaleOutputPaths.Length; i++)
                    {
                        string stalePath = ownedOutputPlan.ExistingStaleOutputPaths[i];
                        string staleRelativePath = GetOwnedRelativePath(outputRoot, stalePath)
                            .Replace('/', Path.DirectorySeparatorChar);
                        string staleBackupPath = ResolveContainedOutputPath(
                            backupFilesRoot,
                            Path.Combine(backupFilesRoot, staleRelativePath));
                        string? staleBackupDirectory = Path.GetDirectoryName(staleBackupPath);
                        if (!string.IsNullOrEmpty(staleBackupDirectory))
                        {
                            Directory.CreateDirectory(staleBackupDirectory);
                        }

                        File.Move(stalePath, staleBackupPath);
                        committed.Add((stalePath, staleBackupPath, true));
                        Console.WriteLine("[DataTable.CodeGen] Removed stale owned output: " + stalePath);
                    }

                    for (int i = 0; i < orderedOutputs.Length; i++)
                    {
                        PendingOutput output = orderedOutputs[i];
                        string relativePath = GetOwnedRelativePath(outputRoot, output.OutputPath)
                            .Replace('/', Path.DirectorySeparatorChar);
                        string stagedPath = ResolveContainedOutputPath(
                            stagedFilesRoot,
                            Path.Combine(stagedFilesRoot, relativePath));
                        string backupPath = ResolveContainedOutputPath(
                            backupFilesRoot,
                            Path.Combine(backupFilesRoot, relativePath));
                        string? outputDirectory = Path.GetDirectoryName(output.OutputPath);
                        if (!string.IsNullOrEmpty(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }

                        bool hadOriginal = File.Exists(output.OutputPath);
                        if (hadOriginal)
                        {
                            string? backupDirectory = Path.GetDirectoryName(backupPath);
                            if (!string.IsNullOrEmpty(backupDirectory))
                            {
                                Directory.CreateDirectory(backupDirectory);
                            }

                            File.Move(output.OutputPath, backupPath);
                        }

                        committed.Add((output.OutputPath, backupPath, hadOriginal));
                        File.Move(stagedPath, output.OutputPath);

                        Console.WriteLine("[DataTable.CodeGen] Committed: " + output.OutputPath);
                    }

                    if (ownedOutputPlan.ManifestNeedsWrite)
                    {
                        string manifestBackupPath = ResolveContainedOutputPath(
                            backupFilesRoot,
                            Path.Combine(backupFilesRoot, "manifest", OWNED_OUTPUT_MANIFEST_FILE));
                        bool hadManifest = File.Exists(ownedOutputPlan.ManifestPath);
                        if (hadManifest)
                        {
                            string? manifestBackupDirectory = Path.GetDirectoryName(manifestBackupPath);
                            if (!string.IsNullOrEmpty(manifestBackupDirectory))
                            {
                                Directory.CreateDirectory(manifestBackupDirectory);
                            }

                            File.Move(ownedOutputPlan.ManifestPath, manifestBackupPath);
                        }

                        committed.Add((ownedOutputPlan.ManifestPath, manifestBackupPath, hadManifest));
                        File.Move(stagedManifestPath, ownedOutputPlan.ManifestPath);
                        Console.WriteLine("[DataTable.CodeGen] Committed owned-output manifest: " + ownedOutputPlan.ManifestPath);
                    }

                    preserveStagingDirectory = false;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    string rollbackError = RollBackCommittedOutputs(committed);
                    preserveStagingDirectory = !string.IsNullOrEmpty(rollbackError);
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(rollbackError)
                            ? "Code generation commit failed; previously committed files were restored."
                            : "Code generation commit failed and rollback was incomplete. " +
                              $"Recovery files were preserved at '{stagingRoot}'. Details: {rollbackError}",
                        exception);
                }
                finally
                {
                    if (!preserveStagingDirectory)
                    {
                        TryDeleteStagingDirectory(outputRoot, stagingRoot);
                    }
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

            private static void TryDeleteStagingDirectory(string outputRoot, string stagingRoot)
            {
                try
                {
                    string validatedStagingRoot = ResolveContainedOutputPath(outputRoot, stagingRoot);
                    if (Directory.Exists(validatedStagingRoot))
                    {
                        Directory.Delete(validatedStagingRoot, true);
                    }
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    Console.Error.WriteLine(
                        "[DataTable.CodeGen] Warning: failed to remove rebuildable staging directory: " +
                        exception.Message);
                }
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
