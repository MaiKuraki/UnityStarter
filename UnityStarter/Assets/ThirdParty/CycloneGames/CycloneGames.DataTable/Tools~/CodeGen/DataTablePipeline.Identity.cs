using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private static readonly HashSet<string> FingerprintTextExtensions = new HashSet<string>(
                new[]
                {
                    ".bat", ".c", ".cc", ".cmd", ".conf", ".config", ".cpp", ".cs", ".csproj", ".csv",
                    ".go", ".h", ".hpp", ".ini", ".java", ".js", ".json", ".json5", ".kt", ".kts",
                    ".liquid", ".lua", ".md", ".mustache", ".props", ".ps1", ".py", ".rs", ".sbn",
                    ".scriban", ".sh", ".sln", ".targets", ".template", ".toml", ".tpl", ".ts", ".tsv",
                    ".txt", ".xml", ".yaml", ".yml",
                },
                StringComparer.OrdinalIgnoreCase);

            private static readonly HashSet<string> FingerprintTextFileNames = new HashSet<string>(
                new[] { ".editorconfig", ".gitattributes", ".gitignore" },
                StringComparer.OrdinalIgnoreCase);

            private static readonly HashSet<string> FingerprintBinaryExtensions = new HashSet<string>(
                new[]
                {
                    ".7z", ".bin", ".bmp", ".bytes", ".dll", ".exe", ".gif", ".gz", ".jpeg", ".jpg",
                    ".ods", ".pdb", ".png", ".psd", ".tar", ".tga", ".webp", ".xls", ".xlsb", ".xlsm",
                    ".xlsx", ".zip",
                },
                StringComparer.OrdinalIgnoreCase);

            private sealed class PipelineIdentity
            {
                public PipelineIdentity(
                    string lubanExecutablePath,
                    bool useDotNetHost,
                    string lubanHash,
                    string sourceFingerprint,
                    string schemaHash,
                    string toolHash)
                {
                    LubanExecutablePath = lubanExecutablePath;
                    UseDotNetHost = useDotNetHost;
                    LubanHash = lubanHash;
                    SourceFingerprint = sourceFingerprint;
                    SchemaHash = schemaHash;
                    ToolHash = toolHash;
                }

                public string LubanExecutablePath { get; }
                public bool UseDotNetHost { get; }
                public string LubanHash { get; }
                public string SourceFingerprint { get; }
                public string SchemaHash { get; }
                public string ToolHash { get; }
            }

            private static PipelineIdentity ValidateIdentity(PipelineConfiguration configuration)
            {
                string sourceFingerprint = ComputeSourceFingerprint(configuration);
                if (!IsSha256(configuration.SourceFingerprint) ||
                    !string.Equals(sourceFingerprint, configuration.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Source fingerprint validation failed closed.\n" +
                        "  Expected: " + configuration.SourceFingerprint + "\n" +
                        "  Actual  : " + sourceFingerprint + "\n" +
                        "Review every source-input change before updating source_fingerprint.");
                }

                ValidateRequiredWorkbooks(configuration.SourceRoot);
                string schemaHash = ComputeSchemaHash(configuration);
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

                AssertPhysicalContainedPath(
                    executablePath,
                    configuration.RepositoryRoot,
                    "Luban executable",
                    mustExist: true);
                if (!File.Exists(executablePath))
                {
                    throw new FileNotFoundException("Luban executable is not a physical file.", executablePath);
                }

                if (IsPlaceholder(configuration.LubanVersion) || !IsSha256(expectedHash))
                {
                    throw new InvalidOperationException(
                        "Luban executable identity is not approved. Set a reviewed version label and exact SHA-256.");
                }

                string executableHash = ComputeFileSha256(executablePath);
                if (!string.Equals(executableHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Luban executable SHA-256 mismatch.\n" +
                        "  Expected: " + expectedHash.ToLowerInvariant() + "\n" +
                        "  Actual  : " + executableHash + "\n" +
                        "Restore the approved artifact or explicitly review and pin the replacement.");
                }

                string toolHash = ComputeToolSourceHash(configuration);
                Console.WriteLine("[DataTable.Pipeline] Luban version: " + configuration.LubanVersion);
                Console.WriteLine("[DataTable.Pipeline] Luban SHA-256: " + executableHash);
                Console.WriteLine("[DataTable.Pipeline] Tool source SHA-256: " + toolHash);
                Console.WriteLine("[DataTable.Pipeline] Source fingerprint: " + sourceFingerprint);
                Console.WriteLine("[DataTable.Pipeline] Schema hash: " + schemaHash);
                return new PipelineIdentity(
                    executablePath,
                    useDotNetHost,
                    executableHash,
                    sourceFingerprint,
                    schemaHash,
                    toolHash);
            }

            private static string ComputeSourceFingerprint(
                PipelineConfiguration configuration,
                bool writeSummary = true)
            {
                var entries = new List<string>(4096);
                long totalBytes = 0;
                int fileCount = 0;
                AddFingerprintFile(
                    configuration,
                    configuration.ConfigurationPath,
                    entries,
                    ref fileCount,
                    ref totalBytes,
                    normalizeSelf: true);
                AddFingerprintFile(
                    configuration,
                    configuration.LubanConfigurationPath,
                    entries,
                    ref fileCount,
                    ref totalBytes,
                    normalizeSelf: false);

                AddFingerprintDirectory(
                    configuration,
                    Path.Combine(configuration.SourceRoot, "Datas"),
                    "DataTable/Luban/Datas",
                    excludeDirectToolBuildArtifacts: false,
                    entries,
                    ref fileCount,
                    ref totalBytes);
                AddFingerprintDirectory(
                    configuration,
                    Path.Combine(configuration.SourceRoot, "Defines"),
                    "DataTable/Luban/Defines",
                    excludeDirectToolBuildArtifacts: false,
                    entries,
                    ref fileCount,
                    ref totalBytes);
                AddFingerprintDirectory(
                    configuration,
                    Path.Combine(configuration.SourceRoot, "config"),
                    "DataTable/Luban/config",
                    excludeDirectToolBuildArtifacts: false,
                    entries,
                    ref fileCount,
                    ref totalBytes);
                AddFingerprintDirectory(
                    configuration,
                    Path.GetDirectoryName(configuration.CodegenProjectPath)!,
                    GetRepositoryRelativePath(configuration.RepositoryRoot, Path.GetDirectoryName(configuration.CodegenProjectPath)!),
                    excludeDirectToolBuildArtifacts: true,
                    entries,
                    ref fileCount,
                    ref totalBytes);
                if (configuration.CustomTemplateRoot.Length != 0)
                {
                    AddFingerprintDirectory(
                        configuration,
                        configuration.CustomTemplateRoot,
                        GetRepositoryRelativePath(configuration.RepositoryRoot, configuration.CustomTemplateRoot),
                        excludeDirectToolBuildArtifacts: false,
                        entries,
                        ref fileCount,
                        ref totalBytes);
                }

                entries.Sort(StringComparer.Ordinal);
                string manifest = string.Join("\n", entries) + "\n";
                string fingerprint = ComputeBytesSha256(Encoding.UTF8.GetBytes(manifest));
                if (writeSummary)
                {
                    Console.WriteLine(
                        $"[DataTable.Pipeline] Source fingerprint inputs: {fileCount} files, {totalBytes} bytes");
                }

                return fingerprint;
            }

            private static string ComputeToolSourceHash(PipelineConfiguration configuration)
            {
                string toolRoot = Path.GetDirectoryName(configuration.CodegenProjectPath) ??
                                  throw new InvalidOperationException("Code-generation project has no parent directory.");
                var entries = new List<string>(512);
                long totalBytes = 0;
                int fileCount = 0;
                AddFingerprintDirectory(
                    configuration,
                    toolRoot,
                    GetRepositoryRelativePath(configuration.RepositoryRoot, toolRoot),
                    excludeDirectToolBuildArtifacts: true,
                    entries,
                    ref fileCount,
                    ref totalBytes);
                entries.Sort(StringComparer.Ordinal);
                return ComputeBytesSha256(Encoding.UTF8.GetBytes(string.Join("\n", entries) + "\n"));
            }

            private static string ComputeSchemaHash(PipelineConfiguration configuration)
            {
                var entries = new List<string>(2048);
                long totalBytes = 0;
                int fileCount = 0;
                AddFingerprintFile(
                    configuration,
                    configuration.LubanConfigurationPath,
                    entries,
                    ref fileCount,
                    ref totalBytes,
                    normalizeSelf: false);
                foreach (string directory in new[] { "Datas", "Defines", "config" })
                {
                    AddFingerprintDirectory(
                        configuration,
                        Path.Combine(configuration.SourceRoot, directory),
                        "DataTable/Luban/" + directory,
                        excludeDirectToolBuildArtifacts: false,
                        entries,
                        ref fileCount,
                        ref totalBytes);
                }

                entries.Sort(StringComparer.Ordinal);
                return ComputeBytesSha256(Encoding.UTF8.GetBytes(string.Join("\n", entries) + "\n"));
            }

            private static void AddFingerprintDirectory(
                PipelineConfiguration configuration,
                string directory,
                string marker,
                bool excludeDirectToolBuildArtifacts,
                List<string> entries,
                ref int fileCount,
                ref long totalBytes)
            {
                marker = marker.Replace('\\', '/');
                if (!Directory.Exists(directory))
                {
                    entries.Add("R missing " + marker + "/");
                    return;
                }

                AssertPhysicalContainedPath(directory, configuration.RepositoryRoot, "fingerprint source", mustExist: true);
                entries.Add("R present " + marker + "/");
                var pending = new Stack<string>();
                pending.Push(directory);
                while (pending.Count != 0)
                {
                    string current = pending.Pop();
                    foreach (string childDirectory in Directory.EnumerateDirectories(current))
                    {
                        string name = Path.GetFileName(childDirectory);
                        bool isDirectToolArtifact = excludeDirectToolBuildArtifacts &&
                                                    string.Equals(current, directory, GetPathComparison()) &&
                                                    (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                                     name.Equals("obj", StringComparison.OrdinalIgnoreCase));
                        if (!isDirectToolArtifact)
                        {
                            AssertNotReparsePoint(childDirectory, "fingerprint directory");
                            pending.Push(childDirectory);
                        }
                    }

                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        AssertNotReparsePoint(file, "fingerprint file");
                        AddFingerprintFile(
                            configuration,
                            file,
                            entries,
                            ref fileCount,
                            ref totalBytes,
                            normalizeSelf: false);
                    }
                }
            }

            private static void AddFingerprintFile(
                PipelineConfiguration configuration,
                string file,
                List<string> entries,
                ref int fileCount,
                ref long totalBytes,
                bool normalizeSelf)
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > PipelineMaximumFileBytes ||
                    fileCount >= PipelineMaximumFiles ||
                    totalBytes > PipelineMaximumTotalBytes - info.Length)
                {
                    throw new InvalidOperationException("Fingerprint input exceeds its bounded file budget: " + file);
                }

                string relativePath = GetRepositoryRelativePath(configuration.RepositoryRoot, file);
                string fileName = Path.GetFileName(file);
                string extension = Path.GetExtension(file);
                string kind;
                string hash;
                if (FingerprintTextFileNames.Contains(fileName) || FingerprintTextExtensions.Contains(extension))
                {
                    kind = "text";
                    hash = ComputeNormalizedTextSha256(file, normalizeSelf);
                }
                else if (FingerprintBinaryExtensions.Contains(extension))
                {
                    if (normalizeSelf)
                    {
                        throw new InvalidOperationException("Fingerprint self-normalization requires text: " + file);
                    }

                    kind = "binary";
                    hash = ComputeFileSha256(file);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Source fingerprint cannot classify input as text or binary: " + file);
                }

                entries.Add("F " + kind + " " + hash + " " + relativePath);
                fileCount++;
                totalBytes += info.Length;
            }

            private static string ComputeNormalizedTextSha256(string path, bool normalizeSelf)
            {
                var info = new FileInfo(path);
                if (info.Length > 64L * 1024 * 1024)
                {
                    throw new InvalidOperationException("Fingerprint text input exceeds 64 MiB: " + path);
                }

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan);
                using var hashWriter = new IncrementalHashWriter();
                var normalizer = new NormalizedTextHashNormalizer(hashWriter, normalizeSelf, path);
                Decoder decoder = new UTF8Encoding(false, true).GetDecoder();
                var validationCharacters = new char[4096];
                var prefix = new byte[3];
                int prefixCount = 0;
                while (prefixCount < prefix.Length)
                {
                    int count = stream.Read(prefix, prefixCount, prefix.Length - prefixCount);
                    if (count == 0)
                    {
                        break;
                    }

                    prefixCount += count;
                }

                if (prefixCount == 3 && prefix[0] == 0xef && prefix[1] == 0xbb && prefix[2] == 0xbf)
                {
                    throw new InvalidOperationException("UTF-8 BOM is not allowed: " + path);
                }

                try
                {
                    ValidateUtf8Chunk(decoder, prefix, 0, prefixCount, flush: false, validationCharacters);
                    normalizer.Append(prefix, prefixCount);
                    var buffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        ValidateUtf8Chunk(decoder, buffer, 0, bytesRead, flush: false, validationCharacters);
                        normalizer.Append(buffer, bytesRead);
                    }

                    ValidateUtf8Chunk(
                        decoder,
                        Array.Empty<byte>(),
                        0,
                        0,
                        flush: true,
                        validationCharacters);
                    normalizer.Complete();
                    return hashWriter.GetHash();
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidOperationException("Fingerprint text is not strict UTF-8: " + path, exception);
                }
            }

            private static void ValidateUtf8Chunk(
                Decoder decoder,
                byte[] bytes,
                int offset,
                int count,
                bool flush,
                char[] characters)
            {
                bool completed;
                do
                {
                    decoder.Convert(
                        bytes,
                        offset,
                        count,
                        characters,
                        0,
                        characters.Length,
                        flush,
                        out int bytesUsed,
                        out _,
                        out completed);
                    offset += bytesUsed;
                    count -= bytesUsed;
                }
                while (!completed);
            }

            private sealed class IncrementalHashWriter : IDisposable
            {
                private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                private readonly byte[] _buffer = new byte[4096];
                private int _count;

                public void Append(byte value)
                {
                    if (_count == _buffer.Length)
                    {
                        Flush();
                    }

                    _buffer[_count++] = value;
                }

                public void Append(byte[] values, int count)
                {
                    for (int index = 0; index < count; index++)
                    {
                        Append(values[index]);
                    }
                }

                public void AppendAscii(string value)
                {
                    for (int index = 0; index < value.Length; index++)
                    {
                        char character = value[index];
                        if (character > 0x7f)
                        {
                            throw new InvalidOperationException("Fingerprint normalization literals must be ASCII.");
                        }

                        Append((byte)character);
                    }
                }

                public string GetHash()
                {
                    Flush();
                    return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
                }

                public void Dispose()
                {
                    _hash.Dispose();
                }

                private void Flush()
                {
                    if (_count == 0)
                    {
                        return;
                    }

                    _hash.AppendData(_buffer, 0, _count);
                    _count = 0;
                }
            }

            private sealed class NormalizedTextHashNormalizer
            {
                private static readonly byte[] SelfPrefix = Encoding.ASCII.GetBytes("source_fingerprint=");
                private const string SelfReplacement = "source_fingerprint=<self>";

                private readonly IncrementalHashWriter _destination;
                private readonly bool _normalizeSelf;
                private readonly string _path;
                private bool _pendingCarriageReturn;
                private bool _atLineStart = true;
                private bool _skippingSelfValue;
                private int _matchedSelfPrefixBytes;

                public NormalizedTextHashNormalizer(
                    IncrementalHashWriter destination,
                    bool normalizeSelf,
                    string path)
                {
                    _destination = destination;
                    _normalizeSelf = normalizeSelf;
                    _path = path;
                }

                public void Append(byte[] bytes, int count)
                {
                    for (int index = 0; index < count; index++)
                    {
                        byte value = bytes[index];
                        if (_pendingCarriageReturn)
                        {
                            if (value != (byte)'\n')
                            {
                                throw new InvalidOperationException(
                                    "Fingerprint text contains a standalone CR character: " + _path);
                            }

                            _pendingCarriageReturn = false;
                            AppendNormalized((byte)'\n');
                            continue;
                        }

                        if (value == (byte)'\r')
                        {
                            _pendingCarriageReturn = true;
                            continue;
                        }

                        AppendNormalized(value);
                    }
                }

                public void Complete()
                {
                    if (_pendingCarriageReturn)
                    {
                        throw new InvalidOperationException(
                            "Fingerprint text contains a trailing standalone CR character: " + _path);
                    }

                    FlushPartialSelfPrefix();
                }

                private void AppendNormalized(byte value)
                {
                    if (!_normalizeSelf)
                    {
                        _destination.Append(value);
                        return;
                    }

                    if (_skippingSelfValue)
                    {
                        if (value == (byte)'\n')
                        {
                            _destination.Append(value);
                            _skippingSelfValue = false;
                            _atLineStart = true;
                        }

                        return;
                    }

                    if (_atLineStart)
                    {
                        if (value == SelfPrefix[_matchedSelfPrefixBytes])
                        {
                            _matchedSelfPrefixBytes++;
                            if (_matchedSelfPrefixBytes == SelfPrefix.Length)
                            {
                                _destination.AppendAscii(SelfReplacement);
                                _matchedSelfPrefixBytes = 0;
                                _atLineStart = false;
                                _skippingSelfValue = true;
                            }

                            return;
                        }

                        FlushPartialSelfPrefix();
                        _atLineStart = false;
                    }

                    _destination.Append(value);
                    if (value == (byte)'\n')
                    {
                        _atLineStart = true;
                    }
                }

                private void FlushPartialSelfPrefix()
                {
                    if (_matchedSelfPrefixBytes == 0)
                    {
                        return;
                    }

                    _destination.Append(SelfPrefix, _matchedSelfPrefixBytes);
                    _matchedSelfPrefixBytes = 0;
                }
            }

            private static string GetRepositoryRelativePath(string repositoryRoot, string path)
            {
                string relative = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
                if (relative.StartsWith("../", StringComparison.Ordinal) ||
                    relative.IndexOf('\r') >= 0 ||
                    relative.IndexOf('\n') >= 0 ||
                    relative.Any(static character => character < 0x20 || character > 0x7e))
                {
                    throw new InvalidOperationException(
                        "Fingerprint path must be contained and printable ASCII: " + relative);
                }

                return relative;
            }

            private static void ValidateRequiredWorkbooks(string sourceRoot)
            {
                foreach (string workbook in new[] { "__tables__.xlsx", "__beans__.xlsx", "__enums__.xlsx" })
                {
                    string path = Path.Combine(sourceRoot, "Datas", workbook);
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException("Required Luban schema workbook not found.", path);
                    }

                    AssertNotReparsePoint(path, "schema workbook");
                }
            }

            private static bool IsPlaceholder(string value)
            {
                return string.IsNullOrWhiteSpace(value) ||
                       value.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase) ||
                       value.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
                       value.StartsWith("UNSET", StringComparison.OrdinalIgnoreCase) ||
                       value.StartsWith("<", StringComparison.Ordinal);
            }

            private static bool IsSha256(string value)
            {
                return value.Length == 64 &&
                       value.Any(static character => character != '0') &&
                       value.All(static character => Uri.IsHexDigit(character));
            }

            private static string ComputeFileSha256(string path)
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);
                using SHA256 sha256 = SHA256.Create();
                return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
            }

            private static string ComputeBytesSha256(byte[] bytes)
            {
                using SHA256 sha256 = SHA256.Create();
                return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
            }

            private static void AssertNotReparsePoint(string path, string description)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(description + " is a reparse point: " + path);
                }
            }
        }
    }
}
