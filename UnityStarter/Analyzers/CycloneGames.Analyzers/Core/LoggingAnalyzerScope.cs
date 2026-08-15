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
        private const string PipelineBackendAssembly = "CycloneGames.Logging.Pipeline";
        private const string UnityBackendAssembly = "CycloneGames.Logging.Unity";
        private const string UnityEditorBackendAssembly = "CycloneGames.Logging.Unity.Editor";
        private const string UnitySamplesAssembly = "CycloneGames.Logging.Unity.Samples";

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

            if (string.Equals(assemblyName, PipelineBackendAssembly, StringComparison.Ordinal) ||
                string.Equals(assemblyName, UnityBackendAssembly, StringComparison.Ordinal) ||
                string.Equals(assemblyName, UnityEditorBackendAssembly, StringComparison.Ordinal))
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
                   AnalyzerSourceScope.IsRepositoryOwned(syntaxTree) &&
                   !IsExemptPath(syntaxTree.FilePath);
        }

        /// <summary>
        /// The Unity package sample is an explicit composition example and benchmark. It may
        /// construct a pipeline, but remains governed for direct Unity and Console output APIs.
        /// </summary>
        internal static bool MayReferenceBackendPipeline(string? assemblyName)
        {
            return string.Equals(assemblyName, UnitySamplesAssembly, StringComparison.Ordinal);
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
