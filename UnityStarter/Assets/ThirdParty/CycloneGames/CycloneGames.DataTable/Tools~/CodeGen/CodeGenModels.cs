using System;
using System.Collections.Generic;
using System.Linq;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private sealed class PendingOutput
        {
            public PendingOutput(string outputPath, string content)
            {
                OutputPath = outputPath;
                Content = content;
            }

            public string OutputPath { get; }
            public string Content { get; }
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

        private static string GetRequired(Dictionary<string, string> row, string columnName, string rowName)
        {
            if (!row.TryGetValue(columnName, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Row '{rowName}' is missing required column: {columnName}");
            }

            return value.Trim();
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
