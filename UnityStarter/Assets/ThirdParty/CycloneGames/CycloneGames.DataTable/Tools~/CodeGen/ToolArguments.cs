using System;
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

            public static ToolArguments CreateForPipeline(
                string configPath,
                string lubanConfPath,
                string dataDir,
                string target,
                string codeOutputDir,
                string lineEnding)
            {
                var result = new ToolArguments
                {
                    ConfigPath = Path.GetFullPath(configPath),
                    LubanConfPath = Path.GetFullPath(lubanConfPath),
                    DataDir = Path.GetFullPath(dataDir),
                    Target = target,
                    CodeOutputDir = Path.GetFullPath(codeOutputDir),
                    LineEnding = lineEnding,
                    ValidateOnly = false,
                };
                result.Validate();
                return result;
            }

            private void Validate()
            {
                if (!string.Equals(LineEnding, "crlf", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(LineEnding, "lf", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Pipeline line ending must be 'crlf' or 'lf'.");
                }

                if (Target.Length > 128 || Target.Any(static character =>
                        !char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.'))
                {
                    throw new ArgumentException(
                        "Pipeline target contains unsupported characters or exceeds 128 characters.");
                }

                string? outputRoot = Path.GetPathRoot(CodeOutputDir);
                if (string.IsNullOrEmpty(outputRoot) ||
                    string.Equals(
                        Path.TrimEndingDirectorySeparator(CodeOutputDir),
                        Path.TrimEndingDirectorySeparator(outputRoot),
                        GetPathComparison()))
                {
                    throw new ArgumentException("Pipeline code output must not be a filesystem root.");
                }

                if (PathsOverlap(CodeOutputDir, DataDir))
                {
                    throw new ArgumentException("Pipeline code and data roots must not contain one another.");
                }
            }

        }
    }
}
