using System;
using Microsoft.CodeAnalysis;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Defines the assemblies and source locations governed by the unified logging rules.
    /// Samples and benchmarks remain in scope because they are copyable package guidance;
    /// tests, tools, and code generation retain explicit output-boundary exemptions.
    /// </summary>
    internal static class LoggingAnalyzerScope
    {
        private const string AssemblyPrefix = "CycloneGames.";
        private const string LoggerBackendAssembly = "CycloneGames.Logger";
        private const string UnityLoggerBackendAssembly = "CycloneGames.Logger.Unity";
        private const string EditorLoggerBackendAssembly = "CycloneGames.Logger.Editor";

        private static readonly string[] ExemptAssemblySegments =
        {
            "Test",
            "Tests",
            "Tool",
            "Tools",
            "CodeGen"
        };

        private static readonly string[] ExemptPathSegments =
        {
            "Test",
            "Test~",
            "Tests",
            "Tests~",
            "Tool",
            "Tool~",
            "Tools",
            "Tools~",
            "CodeGen",
            "CodeGen~"
        };

        internal static bool IsEnforcedAssembly(string? assemblyName)
        {
            if (assemblyName == null ||
                assemblyName.Length == 0 ||
                !assemblyName.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(assemblyName, LoggerBackendAssembly, StringComparison.Ordinal) ||
                string.Equals(assemblyName, UnityLoggerBackendAssembly, StringComparison.Ordinal) ||
                string.Equals(assemblyName, EditorLoggerBackendAssembly, StringComparison.Ordinal))
            {
                return false;
            }

            var segments = assemblyName.Split('.');
            for (int i = 0; i < segments.Length; i++)
            {
                for (int j = 0; j < ExemptAssemblySegments.Length; j++)
                {
                    if (string.Equals(segments[i], ExemptAssemblySegments[j], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool ShouldAnalyze(Compilation compilation, SyntaxTree syntaxTree)
        {
            return IsEnforcedAssembly(compilation.AssemblyName) &&
                   !IsExemptPath(syntaxTree.FilePath);
        }

        internal static bool IsExemptPath(string? filePath)
        {
            if (filePath == null || filePath.Length == 0)
            {
                return false;
            }

            string normalized = "/" + filePath.Replace('\\', '/').Trim('/') + "/";
            for (int i = 0; i < ExemptPathSegments.Length; i++)
            {
                string segment = "/" + ExemptPathSegments[i] + "/";
                if (normalized.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
