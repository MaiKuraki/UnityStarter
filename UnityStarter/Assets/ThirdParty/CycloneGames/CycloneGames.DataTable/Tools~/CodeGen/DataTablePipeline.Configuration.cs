using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private const int PipelineConfigurationMaximumBytes = 1024 * 1024;
            private const int PipelineConfigurationMaximumLines = 16384;
            private const int PipelineConfigurationMaximumLineCharacters = 16384;
            private const int PipelineMaximumFiles = 200000;
            private const long PipelineMaximumFileBytes = 2L * 1024 * 1024 * 1024;
            private const long PipelineMaximumTotalBytes = 32L * 1024 * 1024 * 1024;
            private const string ReceiptFileName = ".cyclonegames-datatable-generation-receipt.json";
            private const string LockDirectoryName = ".cyclonegames-datatable-writer.lock";
            private const string TransactionDirectoryName = ".cyclonegames-datatable-transactions";

            private enum PipelineOperation
            {
                Generate,
                Check,
                Recover,
                Inspect,
            }

            private sealed class PipelineCommand
            {
                public PipelineOperation Operation { get; private init; }
                public string ConfigurationPath { get; private init; } = string.Empty;
                public string ProfileName { get; private init; } = string.Empty;
                public string RunId { get; private init; } = string.Empty;
                public string Format { get; private init; } = string.Empty;

                public static PipelineCommand Parse(string[] args)
                {
                    if (args.Length == 0)
                    {
                        throw new ArgumentException(
                            "Pipeline operation is required: generate, check, recover, or inspect.");
                    }

                    PipelineOperation operation = args[0].ToLowerInvariant() switch
                    {
                        "generate" => PipelineOperation.Generate,
                        "check" => PipelineOperation.Check,
                        "recover" => PipelineOperation.Recover,
                        "inspect" => PipelineOperation.Inspect,
                        _ => throw new ArgumentException("Unknown pipeline operation: " + args[0]),
                    };

                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int index = 1; index < args.Length; index++)
                    {
                        string key = args[index];
                        if (!key.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException("Unexpected pipeline argument: " + key);
                        }

                        string normalized = key.Substring(2);
                        if (!string.Equals(normalized, "config", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(normalized, "profile", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(normalized, "run-id", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(normalized, "format", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ArgumentException("Unknown pipeline argument: " + key);
                        }

                        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException("Missing value for pipeline argument: " + key);
                        }

                        if (!values.TryAdd(normalized, args[++index]))
                        {
                            throw new ArgumentException("Duplicate pipeline argument: " + key);
                        }
                    }

                    string configurationPath = RequireCommandValue(values, "config");
                    string profileName = values.TryGetValue("profile", out string? profile) ? profile : string.Empty;
                    string runId = values.TryGetValue("run-id", out string? id) ? id : string.Empty;
                    string format = values.TryGetValue("format", out string? requestedFormat)
                        ? requestedFormat
                        : string.Empty;
                    if (operation == PipelineOperation.Recover)
                    {
                        if (profileName.Length != 0)
                        {
                            throw new ArgumentException("recover does not accept --profile.");
                        }

                        ValidateRunId(runId);
                        if (format.Length != 0)
                        {
                            throw new ArgumentException("--format is valid only for inspect.");
                        }
                    }
                    else if (operation == PipelineOperation.Inspect)
                    {
                        ValidatePortableName(profileName, "--profile", 128);
                        if (runId.Length != 0)
                        {
                            throw new ArgumentException("inspect does not accept --run-id.");
                        }

                        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ArgumentException("inspect requires '--format json'.");
                        }
                    }
                    else
                    {
                        ValidatePortableName(profileName, "--profile", 128);
                        if (runId.Length != 0)
                        {
                            throw new ArgumentException("--run-id is valid only for recover.");
                        }

                        if (format.Length != 0)
                        {
                            throw new ArgumentException("--format is valid only for inspect.");
                        }
                    }

                    return new PipelineCommand
                    {
                        Operation = operation,
                        ConfigurationPath = Path.GetFullPath(configurationPath),
                        ProfileName = profileName,
                        RunId = runId,
                        Format = format.ToLowerInvariant(),
                    };
                }

                private static string RequireCommandValue(Dictionary<string, string> values, string key)
                {
                    if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException("Missing required pipeline argument: --" + key);
                    }

                    return value;
                }
            }

            private sealed class PipelineProfile
            {
                public PipelineProfile(
                    string name,
                    string codeOutputRoot,
                    string dataOutputRoot,
                    string codeTarget,
                    string dataTarget,
                    string lineEnding)
                {
                    Name = name;
                    CodeOutputRoot = codeOutputRoot;
                    DataOutputRoot = dataOutputRoot;
                    CodeTarget = codeTarget;
                    DataTarget = dataTarget;
                    LineEnding = lineEnding;
                }

                public string Name { get; }
                public string CodeOutputRoot { get; }
                public string DataOutputRoot { get; }
                public string CodeTarget { get; }
                public string DataTarget { get; }
                public string LineEnding { get; }
            }

            private sealed class PipelineConfiguration
            {
                private PipelineConfiguration(
                    string configurationPath,
                    string configurationSha256,
                    string repositoryRoot,
                    string lubanConfigurationPath,
                    string lubanPath,
                    string lubanVersion,
                    string lubanSha256,
                    string windowsLubanPath,
                    string windowsLubanSha256,
                    string sourceFingerprint,
                    int processTimeoutSeconds,
                    string codegenProjectPath,
                    string customTemplateRoot,
                    string[] bridgeFiles,
                    Dictionary<string, PipelineProfile> profiles)
                {
                    ConfigurationPath = configurationPath;
                    ConfigurationSha256 = configurationSha256;
                    RepositoryRoot = repositoryRoot;
                    LubanConfigurationPath = lubanConfigurationPath;
                    LubanPath = lubanPath;
                    LubanVersion = lubanVersion;
                    LubanSha256 = lubanSha256;
                    WindowsLubanPath = windowsLubanPath;
                    WindowsLubanSha256 = windowsLubanSha256;
                    SourceFingerprint = sourceFingerprint;
                    ProcessTimeoutSeconds = processTimeoutSeconds;
                    CodegenProjectPath = codegenProjectPath;
                    CustomTemplateRoot = customTemplateRoot;
                    BridgeFiles = bridgeFiles;
                    Profiles = profiles;
                    SourceRoot = Path.GetDirectoryName(configurationPath)!;
                    LockDirectory = Path.Combine(SourceRoot, LockDirectoryName);
                    TransactionsRoot = Path.Combine(SourceRoot, TransactionDirectoryName);
                }

                public string ConfigurationPath { get; }
                public string ConfigurationSha256 { get; }
                public string RepositoryRoot { get; }
                public string SourceRoot { get; }
                public string LubanConfigurationPath { get; }
                public string LubanPath { get; }
                public string LubanVersion { get; }
                public string LubanSha256 { get; }
                public string WindowsLubanPath { get; }
                public string WindowsLubanSha256 { get; }
                public string SourceFingerprint { get; }
                public int ProcessTimeoutSeconds { get; }
                public string CodegenProjectPath { get; }
                public string CustomTemplateRoot { get; }
                public string[] BridgeFiles { get; }
                public Dictionary<string, PipelineProfile> Profiles { get; }
                public string LockDirectory { get; }
                public string TransactionsRoot { get; }

                public PipelineProfile GetProfile(string name)
                {
                    if (!Profiles.TryGetValue(name, out PipelineProfile? profile))
                    {
                        throw new InvalidOperationException("DataTable pipeline profile not found: " + name);
                    }

                    return profile;
                }

                public static PipelineConfiguration Load(string configurationPath)
                {
                    return Load(configurationPath, requireDependentInputs: true);
                }

                public static PipelineConfiguration LoadForInspection(string configurationPath)
                {
                    return Load(configurationPath, requireDependentInputs: false);
                }

                private static PipelineConfiguration Load(
                    string configurationPath,
                    bool requireDependentInputs)
                {
                    if (!File.Exists(configurationPath))
                    {
                        throw new FileNotFoundException("DataTable pipeline configuration not found.", configurationPath);
                    }

                    ValidateFileSize(configurationPath, PipelineConfigurationMaximumBytes, "pipeline configuration");
                    byte[] configurationBytes = File.ReadAllBytes(configurationPath);
                    RejectUtf8Bom(configurationBytes, configurationPath);
                    string configurationText = new UTF8Encoding(false, true).GetString(configurationBytes);
                    Dictionary<string, Dictionary<string, string>> sections = ParseSections(configurationText);

                    string sourceRoot = Path.GetDirectoryName(configurationPath)!;
                    string repositoryRoot = FindRepositoryRoot(sourceRoot);
                    AssertPhysicalContainedPath(configurationPath, repositoryRoot, "pipeline configuration", mustExist: true);
                    string lubanConfigurationPath = Path.Combine(sourceRoot, "luban.conf");
                    AssertPhysicalContainedPath(
                        lubanConfigurationPath,
                        repositoryRoot,
                        "Luban configuration",
                        mustExist: requireDependentInputs);

                    Dictionary<string, string> luban = RequireSection(sections, "luban");
                    Dictionary<string, string> templates = RequireSection(sections, "templates");
                    Dictionary<string, string> codegen = RequireSection(sections, "codegen");
                    ValidateKnownKeys(
                        luban,
                        "luban",
                        "luban_dll", "executable_version", "executable_sha256",
                        "windows_executable", "windows_executable_sha256", "source_fingerprint",
                        "process_timeout_seconds");
                    ValidateKnownKeys(templates, "templates", "custom_template_dir", "bridge_files");
                    ValidateKnownKeys(
                        codegen,
                        "codegen",
                        "codegen_project", "string_constant_tables", "string_constant_value_column",
                        "string_constant_comment_column", "string_constant_enabled_column",
                        "string_constant_scope_column", "string_constant_generated_comment_language");

                    string lubanPath = ResolveConfigurationPath(sourceRoot, RequireValue(luban, "luban_dll", "luban"));
                    string windowsLubanValue = GetOptionalValue(luban, "windows_executable");
                    string windowsLubanPath = windowsLubanValue.Length == 0
                        ? string.Empty
                        : ResolveConfigurationPath(sourceRoot, windowsLubanValue);
                    string codegenProjectPath = ResolveConfigurationPath(
                        sourceRoot,
                        RequireValue(codegen, "codegen_project", "codegen"));
                    AssertPhysicalContainedPath(
                        codegenProjectPath,
                        repositoryRoot,
                        "CodeGen project",
                        mustExist: requireDependentInputs);

                    string customTemplateValue = GetOptionalValue(templates, "custom_template_dir");
                    string customTemplateRoot = customTemplateValue.Length == 0
                        ? string.Empty
                        : ResolveConfigurationPath(sourceRoot, customTemplateValue);
                    if (customTemplateRoot.Length != 0)
                    {
                        AssertPhysicalContainedPath(
                            customTemplateRoot,
                            sourceRoot,
                            "custom template root",
                            mustExist: requireDependentInputs);
                        if (requireDependentInputs && !Directory.Exists(customTemplateRoot))
                        {
                            throw new DirectoryNotFoundException("Custom template root is not a directory: " + customTemplateRoot);
                        }
                    }

                    string[] bridgeFiles = ParsePortableRelativeList(GetOptionalValue(templates, "bridge_files"), "bridge_files", 256);
                    if (bridgeFiles.Length != 0 && customTemplateRoot.Length == 0)
                    {
                        throw new InvalidOperationException("bridge_files requires custom_template_dir.");
                    }

                    int timeoutSeconds = ParseBoundedInt(
                        RequireValue(luban, "process_timeout_seconds", "luban"),
                        "process_timeout_seconds",
                        1,
                        86400);

                    var profiles = new Dictionary<string, PipelineProfile>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, Dictionary<string, string>> section in sections)
                    {
                        if (!section.Key.StartsWith("profile.", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.Equals(section.Key, "luban", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(section.Key, "templates", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(section.Key, "codegen", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException("Unsupported build configuration section: [" + section.Key + "]");
                            }

                            continue;
                        }

                        string profileName = section.Key.Substring("profile.".Length);
                        ValidatePortableName(profileName, "profile name", 128);
                        ValidateKnownKeys(
                            section.Value,
                            section.Key,
                            "code_output", "data_output", "code_target", "data_target", "line_ending");
                        string codeOutput = ResolveConfigurationPath(
                            sourceRoot,
                            RequireValue(section.Value, "code_output", section.Key));
                        string dataOutput = ResolveConfigurationPath(
                            sourceRoot,
                            RequireValue(section.Value, "data_output", section.Key));
                        ValidateOutputRoot(repositoryRoot, codeOutput, profileName + " code output");
                        ValidateOutputRoot(repositoryRoot, dataOutput, profileName + " data output");
                        if (PathsOverlap(codeOutput, dataOutput))
                        {
                            throw new InvalidOperationException(
                                $"Profile '{profileName}' code and data output roots must not contain one another.");
                        }

                        string codeTarget = RequireValue(section.Value, "code_target", section.Key);
                        string dataTarget = RequireValue(section.Value, "data_target", section.Key);
                        ValidatePortableName(codeTarget, profileName + " code_target", 128);
                        ValidatePortableName(dataTarget, profileName + " data_target", 128);
                        string lineEnding = RequireValue(section.Value, "line_ending", section.Key).ToLowerInvariant();
                        if (lineEnding != "lf" && lineEnding != "crlf")
                        {
                            throw new InvalidOperationException(
                                $"Profile '{profileName}' line_ending must be 'lf' or 'crlf'.");
                        }

                        if (!profiles.TryAdd(
                                profileName,
                                new PipelineProfile(profileName, codeOutput, dataOutput, codeTarget, dataTarget, lineEnding)))
                        {
                            throw new InvalidOperationException("Duplicate pipeline profile: " + profileName);
                        }
                    }

                    if (profiles.Count == 0)
                    {
                        throw new InvalidOperationException("At least one [profile.<name>] section is required.");
                    }

                    EnsureProfileOutputsDoNotOverlap(profiles.Values);
                    return new PipelineConfiguration(
                        configurationPath,
                        ComputeBytesSha256(configurationBytes),
                        repositoryRoot,
                        lubanConfigurationPath,
                        lubanPath,
                        RequireValue(luban, "executable_version", "luban"),
                        RequireValue(luban, "executable_sha256", "luban"),
                        windowsLubanPath,
                        GetOptionalValue(luban, "windows_executable_sha256"),
                        RequireValue(luban, "source_fingerprint", "luban"),
                        timeoutSeconds,
                        codegenProjectPath,
                        customTemplateRoot,
                        bridgeFiles,
                        profiles);
                }
            }

            private static Dictionary<string, Dictionary<string, string>> ParseSections(string text)
            {
                var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string>? current = null;
                string currentName = string.Empty;
                using var reader = new StringReader(text);
                string? raw;
                int lineNumber = 0;
                while ((raw = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (lineNumber > PipelineConfigurationMaximumLines ||
                        raw.Length > PipelineConfigurationMaximumLineCharacters)
                    {
                        throw new InvalidOperationException("Pipeline configuration exceeds its bounded grammar.");
                    }

                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }

                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        currentName = line.Substring(1, line.Length - 2).Trim();
                        ValidateSectionName(currentName);
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (!sections.TryAdd(currentName, current))
                        {
                            throw new InvalidOperationException(
                                $"Duplicate build configuration section '[{currentName}]' at line {lineNumber}.");
                        }

                        continue;
                    }

                    if (current == null)
                    {
                        throw new InvalidOperationException(
                            $"Build configuration key appears before a section at line {lineNumber}.");
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Malformed build configuration entry at line {lineNumber}.");
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    ValidatePortableName(key, "configuration key", 128);
                    if (!current.TryAdd(key, value))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate key '{key}' in section '[{currentName}]' at line {lineNumber}.");
                    }
                }

                return sections;
            }

            private static void ValidateSectionName(string sectionName)
            {
                if (sectionName.StartsWith("profile.", StringComparison.OrdinalIgnoreCase))
                {
                    ValidatePortableName(sectionName.Substring("profile.".Length), "profile name", 128);
                    return;
                }

                ValidatePortableName(sectionName, "section name", 128);
            }

            private static Dictionary<string, string> RequireSection(
                Dictionary<string, Dictionary<string, string>> sections,
                string name)
            {
                if (!sections.TryGetValue(name, out Dictionary<string, string>? section))
                {
                    throw new InvalidOperationException("Required build configuration section is missing: [" + name + "]");
                }

                return section;
            }

            private static void ValidateKnownKeys(
                Dictionary<string, string> values,
                string sectionName,
                params string[] knownKeys)
            {
                var known = new HashSet<string>(knownKeys, StringComparer.OrdinalIgnoreCase);
                foreach (string key in values.Keys)
                {
                    if (!known.Contains(key))
                    {
                        throw new InvalidOperationException(
                            $"Unsupported key '{key}' in build configuration section '[{sectionName}]'.");
                    }
                }
            }

            private static string RequireValue(Dictionary<string, string> values, string key, string section)
            {
                if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Required key '{key}' is missing or empty in section '[{section}]'.");
                }

                return value;
            }

            private static string GetOptionalValue(Dictionary<string, string> values, string key)
            {
                return values.TryGetValue(key, out string? value) ? value : string.Empty;
            }

            private static string FindRepositoryRoot(string startDirectory)
            {
                var current = new DirectoryInfo(startDirectory);
                for (int depth = 0; current != null && depth < 16; depth++, current = current.Parent)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "UnityStarter")) &&
                        Directory.Exists(Path.Combine(current.FullName, "DataTable")))
                    {
                        return Path.GetFullPath(current.FullName);
                    }
                }

                throw new InvalidOperationException(
                    "Could not discover the repository root containing both UnityStarter/ and DataTable/.");
            }

            private static string ResolveConfigurationPath(string configurationDirectory, string value)
            {
                return Path.GetFullPath(Path.Combine(configurationDirectory, value));
            }

            private static void ValidateOutputRoot(string repositoryRoot, string path, string description)
            {
                string unityAssets = Path.Combine(repositoryRoot, "UnityStarter", "Assets");
                string generatedRoot = Path.Combine(repositoryRoot, "DataTable", "Luban", "Generated");
                if (!IsStrictPipelineChildPath(unityAssets, path) && !IsStrictPipelineChildPath(generatedRoot, path))
                {
                    throw new InvalidOperationException(
                        $"{description} must be a strict child of UnityStarter/Assets or DataTable/Luban/Generated: {path}");
                }

                AssertPhysicalContainedPath(path, repositoryRoot, description, mustExist: false);
            }

            private static void EnsureProfileOutputsDoNotOverlap(IEnumerable<PipelineProfile> profiles)
            {
                var roots = profiles
                    .SelectMany(static profile => new[] { profile.CodeOutputRoot, profile.DataOutputRoot })
                    .ToArray();
                for (int left = 0; left < roots.Length; left++)
                {
                    for (int right = left + 1; right < roots.Length; right++)
                    {
                        if (PathsOverlap(roots[left], roots[right]))
                        {
                            throw new InvalidOperationException(
                                "Pipeline profile output roots must not contain one another across profiles: " +
                                roots[left] + " and " + roots[right]);
                        }
                    }
                }
            }

            private static void AssertPhysicalContainedPath(
                string path,
                string approvedRoot,
                string description,
                bool mustExist)
            {
                string fullPath = Path.GetFullPath(path);
                string fullRoot = Path.GetFullPath(approvedRoot);
                if (!string.Equals(fullPath, fullRoot, GetPathComparison()) &&
                    !IsStrictPipelineChildPath(fullRoot, fullPath))
                {
                    throw new InvalidOperationException(description + " escapes its approved root: " + fullPath);
                }

                if (mustExist && !File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    throw new FileNotFoundException(description + " not found.", fullPath);
                }

                if ((File.Exists(fullRoot) || Directory.Exists(fullRoot)) &&
                    (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(description + " approved root is a reparse point: " + fullRoot);
                }

                string probe = fullPath;
                while (!string.Equals(probe, fullRoot, GetPathComparison()))
                {
                    if (File.Exists(probe) || Directory.Exists(probe))
                    {
                        FileAttributes attributes = File.GetAttributes(probe);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            throw new InvalidOperationException(description + " traverses a reparse point: " + probe);
                        }
                    }

                    string? parent = Path.GetDirectoryName(probe);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, probe, GetPathComparison()))
                    {
                        throw new InvalidOperationException(description + " did not reach its approved root: " + fullPath);
                    }

                    probe = parent;
                }
            }

            private static string[] ParsePortableRelativeList(string value, string description, int maximumCount)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return Array.Empty<string>();
                }

                string[] results = value.Split(',').Select(static item => item.Trim()).ToArray();
                if (results.Length > maximumCount || results.Any(static item => item.Length == 0))
                {
                    throw new InvalidOperationException(description + " exceeds its count limit or contains an empty item.");
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < results.Length; index++)
                {
                    ValidatePortableRelativePath(results[index], description);
                    if (!seen.Add(results[index]))
                    {
                        throw new InvalidOperationException(description + " contains a duplicate or case-colliding path: " + results[index]);
                    }
                }

                return results;
            }

            private static void ValidatePortableRelativePath(string path, string description)
            {
                if (path.Length > 1024 || Path.IsPathRooted(path) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0)
                {
                    throw new InvalidOperationException(description + " contains a non-portable path: " + path);
                }

                string[] segments = path.Split('/');
                foreach (string segment in segments)
                {
                    if (segment.Length == 0 || segment == "." || segment == ".." || segment.Length > 255 ||
                        segment[segment.Length - 1] == '.' || IsWindowsReservedName(segment))
                    {
                        throw new InvalidOperationException(description + " contains a non-portable path segment: " + path);
                    }

                    for (int characterIndex = 0; characterIndex < segment.Length; characterIndex++)
                    {
                        char character = segment[characterIndex];
                        bool supported = character >= 'A' && character <= 'Z' ||
                                         character >= 'a' && character <= 'z' ||
                                         character >= '0' && character <= '9' ||
                                         character == '_' || character == '-' || character == '.';
                        if (!supported)
                        {
                            throw new InvalidOperationException(description + " uses an unsupported path character: " + path);
                        }
                    }
                }
            }

            private static bool IsWindowsReservedName(string segment)
            {
                string baseName = segment.Split('.')[0];
                if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return baseName.Length == 4 && baseName[3] >= '1' && baseName[3] <= '9' &&
                       (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                        baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
            }

            private static void ValidatePortableName(string value, string description, int maximumCharacters)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > maximumCharacters)
                {
                    throw new ArgumentException(description + " is empty or too long.");
                }

                for (int index = 0; index < value.Length; index++)
                {
                    char character = value[index];
                    bool supported = character >= 'A' && character <= 'Z' ||
                                     character >= 'a' && character <= 'z' ||
                                     character >= '0' && character <= '9' ||
                                     character == '_' || character == '-' || character == '.';
                    if (!supported)
                    {
                        throw new ArgumentException(description + " contains unsupported characters: " + value);
                    }
                }
            }

            private static void ValidateRunId(string value)
            {
                if (value.Length != 32 || value.Any(static character => !Uri.IsHexDigit(character)))
                {
                    throw new ArgumentException("--run-id must be a 32-character hexadecimal transaction identifier.");
                }
            }

            private static int ParseBoundedInt(string value, string description, int minimum, int maximum)
            {
                if (!int.TryParse(value, out int parsed) || parsed < minimum || parsed > maximum)
                {
                    throw new InvalidOperationException(
                        $"{description} must be an integer in [{minimum}, {maximum}].");
                }

                return parsed;
            }

            private static void RejectUtf8Bom(byte[] bytes, string path)
            {
                if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
                {
                    throw new InvalidOperationException("UTF-8 BOM is not allowed: " + path);
                }
            }
        }
    }
}
