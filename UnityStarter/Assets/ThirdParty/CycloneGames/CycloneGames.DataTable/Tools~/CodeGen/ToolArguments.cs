using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private sealed class ToolArguments
        {
            public string ConfigPath { get; private init; } = string.Empty;
            public string LubanConfPath { get; private init; } = string.Empty;
            public string DataDir { get; private init; } = string.Empty;
            public string Target { get; private init; } = string.Empty;
            public string CodeOutputDir { get; private init; } = string.Empty;
            public string LineEnding { get; private init; } = "crlf";
            public bool ValidateOnly { get; private init; }

            public static ToolArguments Parse(string[] args)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool validateOnly = false;
                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i];
                    if (!key.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unexpected argument: {key}");
                    }

                    if (string.Equals(key, "--validate-only", StringComparison.OrdinalIgnoreCase))
                    {
                        if (validateOnly)
                        {
                            throw new ArgumentException("Duplicate argument: " + key);
                        }

                        validateOnly = true;
                        continue;
                    }

                    string normalizedKey = key.Substring(2);
                    if (!IsKnownValueArgument(normalizedKey))
                    {
                        throw new ArgumentException("Unknown argument: " + key);
                    }

                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"Missing value for argument: {key}");
                    }

                    if (values.ContainsKey(normalizedKey))
                    {
                        throw new ArgumentException("Duplicate argument: " + key);
                    }

                    values.Add(normalizedKey, args[++i]);
                }

                var result = new ToolArguments
                {
                    ConfigPath = RequireExistingFile(values, "config"),
                    LubanConfPath = RequireExistingFile(values, "luban-conf"),
                    DataDir = RequireExistingDirectory(values, "data-dir"),
                    Target = RequireValue(values, "target"),
                    CodeOutputDir = RequirePath(values, "code-output"),
                    LineEnding = values.TryGetValue("line-ending", out string? lineEnding) ? lineEnding : "crlf",
                    ValidateOnly = validateOnly,
                };

                result.Validate();
                return result;
            }

            private static bool IsKnownValueArgument(string key)
            {
                return string.Equals(key, "config", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(key, "luban-conf", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(key, "data-dir", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(key, "target", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(key, "code-output", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(key, "line-ending", StringComparison.OrdinalIgnoreCase);
            }

            private void Validate()
            {
                if (!string.Equals(LineEnding, "crlf", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(LineEnding, "lf", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("--line-ending must be 'crlf' or 'lf'.");
                }

                if (Target.Length > 128 || Target.Any(static character =>
                        !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
                {
                    throw new ArgumentException("--target contains unsupported characters or exceeds 128 characters.");
                }

                string? outputRoot = Path.GetPathRoot(CodeOutputDir);
                if (string.IsNullOrEmpty(outputRoot) ||
                    string.Equals(
                        Path.TrimEndingDirectorySeparator(CodeOutputDir),
                        Path.TrimEndingDirectorySeparator(outputRoot),
                        GetPathComparison()))
                {
                    throw new ArgumentException("--code-output must not be a filesystem root.");
                }

                if (PathsOverlap(CodeOutputDir, DataDir))
                {
                    throw new ArgumentException("--code-output and --data-dir must not contain one another.");
                }
            }

            private static string RequirePath(Dictionary<string, string> values, string key)
            {
                return Path.GetFullPath(RequireValue(values, key));
            }

            private static string RequireExistingFile(Dictionary<string, string> values, string key)
            {
                string path = RequirePath(values, key);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"Required --{key} file not found.", path);
                }

                return path;
            }

            private static string RequireExistingDirectory(Dictionary<string, string> values, string key)
            {
                string path = RequirePath(values, key);
                if (!Directory.Exists(path))
                {
                    throw new DirectoryNotFoundException($"Required --{key} directory not found: {path}");
                }

                return path;
            }

            private static string RequireValue(Dictionary<string, string> values, string key)
            {
                if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException($"Missing required argument: --{key}");
                }

                return value;
            }
        }
    }
}
