using System;
using System.Collections.Generic;
using System.Linq;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private sealed class StagedOutput
        {
            public StagedOutput(
                string outputPath,
                string stagedPath,
                string sha256,
                long byteLength)
            {
                OutputPath = outputPath;
                StagedPath = stagedPath;
                Sha256 = sha256;
                ByteLength = byteLength;
            }

            public string OutputPath { get; }
            public string StagedPath { get; }
            public string Sha256 { get; }
            public long ByteLength { get; }
        }

        private sealed class OwnedOutputPlan
        {
            public OwnedOutputPlan(
                string manifestPath,
                bool manifestNeedsWrite,
                string manifestContent,
                string[] existingStaleOutputPaths,
                int missingStaleRegistrationCount)
            {
                ManifestPath = manifestPath;
                ManifestNeedsWrite = manifestNeedsWrite;
                ManifestContent = manifestContent;
                ExistingStaleOutputPaths = existingStaleOutputPaths;
                MissingStaleRegistrationCount = missingStaleRegistrationCount;
            }

            public string ManifestPath { get; }
            public bool ManifestNeedsWrite { get; }
            public string ManifestContent { get; }
            public string[] ExistingStaleOutputPaths { get; }
            public int MissingStaleRegistrationCount { get; }
        }

        private sealed class StringConstantConfiguration
        {
            public StringConstantConfiguration(
                string[] tables,
                string valueColumn,
                string commentColumn,
                string enabledColumn,
                string scopeColumn,
                string generatedCommentLanguage)
            {
                Tables = tables ?? throw new ArgumentNullException(nameof(tables));
                ValueColumn = valueColumn ?? throw new ArgumentNullException(nameof(valueColumn));
                CommentColumn = commentColumn ?? throw new ArgumentNullException(nameof(commentColumn));
                EnabledColumn = enabledColumn ?? throw new ArgumentNullException(nameof(enabledColumn));
                ScopeColumn = scopeColumn ?? throw new ArgumentNullException(nameof(scopeColumn));
                GeneratedCommentLanguage = generatedCommentLanguage ??
                                           throw new ArgumentNullException(nameof(generatedCommentLanguage));
            }

            public string[] Tables { get; }
            public string ValueColumn { get; }
            public string CommentColumn { get; }
            public string EnabledColumn { get; }
            public string ScopeColumn { get; }
            public string GeneratedCommentLanguage { get; }
        }

        private readonly struct ConstantEntry
        {
            public ConstantEntry(string constantName, string value, string comment)
            {
                ConstantName = constantName;
                Value = value;
                Comment = comment;
            }

            public string ConstantName { get; }
            public string Value { get; }
            public string Comment { get; }
        }

        private static string GetOptional(Dictionary<string, string> values, string key, string defaultValue = "")
        {
            return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : defaultValue;
        }

        private static string GetOptionalAllowEmpty(Dictionary<string, string> values, string key, string defaultValue = "")
        {
            return values.TryGetValue(key, out string? value)
                ? value.Trim()
                : defaultValue;
        }

        private static string[] SplitList(string value)
        {
            return value
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => item.Trim())
                .Where(static item => item.Length > 0)
                .ToArray();
        }

        private static string GetNamespacePart(string fullName)
        {
            int index = fullName.LastIndexOf('.');
            return index < 0 ? string.Empty : fullName.Substring(0, index);
        }

        private static string GetNamePart(string fullName)
        {
            int index = fullName.LastIndexOf('.');
            return index < 0 ? fullName : fullName.Substring(index + 1);
        }

        private static string CombineNamespace(string topModule, string childNamespace)
        {
            if (string.IsNullOrWhiteSpace(topModule))
            {
                return childNamespace;
            }

            if (string.IsNullOrWhiteSpace(childNamespace))
            {
                return topModule;
            }

            return topModule + "." + childNamespace;
        }
    }
}
