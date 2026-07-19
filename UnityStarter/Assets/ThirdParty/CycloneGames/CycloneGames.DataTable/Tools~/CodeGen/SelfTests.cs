using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class StringConstantGenerator
        {
            public static void RunSelfTests()
            {
                ValidateNamespaceIdentifier("Game.Config", "self-test namespace");
                ValidateCSharpIdentifier("GameplayTagNames", "self-test class");
                AssertRejects(() => ValidateNamespaceIdentifier("Game.class", "self-test namespace"), "keyword namespace");
                AssertRejects(() => ValidateCSharpIdentifier("9Invalid", "self-test class"), "numeric identifier");
                AssertRejects(() => ValidateCSharpIdentifier("Has-Dash", "self-test class"), "punctuated identifier");

                AssertEqual(
                    "line one line two line three line four",
                    NormalizeGeneratedCommentText("line one\r\nline two\u2028line three\u2029line four"),
                    "comment line normalization");
                AssertEqual(
                    "A\\r\\n\\u2028\\u2029\\t\\0",
                    EscapeCSharpString("A\r\n\u2028\u2029\t\0"),
                    "C# string escaping");
                AssertEqual(
                    "first second &lt;tag&gt;",
                    EscapeXmlComment("first\r\nsecond\u2028<tag>"),
                    "XML documentation normalization");

                var entries = new List<ConstantEntry>
                {
                    new ConstantEntry("SAFE_NAME", "Value\u2028Tail", "Comment\r\nInjected"),
                };
                string generated = BuildConstantsFile(
                    "Game.Config",
                    "SafeNames",
                    "table.xlsx\r\nnamespace Injected",
                    "Scope\u2029public class Injected",
                    entries,
                    "en",
                    "\n");
                AssertContains(generated, "Source table: table.xlsx namespace Injected", "source comment normalization");
                AssertContains(generated, "Scope: Scope public class Injected", "scope comment normalization");
                AssertContains(generated, "/// Comment Injected", "XML documentation line normalization");
                AssertContains(generated, "\"Value\\u2028Tail\"", "generated string separator escaping");
                if (generated.IndexOf('\u2028') >= 0 || generated.IndexOf('\u2029') >= 0)
                {
                    throw new InvalidOperationException("Self-test failed: generated source contains a raw Unicode line separator.");
                }

                AssertEqual(
                    "Scopes/AbilityNames.cs",
                    ValidateOwnedRelativePath("Scopes/AbilityNames.cs"),
                    "owned-output relative path");
                AssertRejects(() => ValidateOwnedRelativePath("../Escape.cs"), "owned-output traversal path");
                AssertRejects(() => ValidateOwnedRelativePath("/Rooted.cs"), "rooted owned-output path");
                AssertRejects(() => ValidateOwnedRelativePath("Scopes\\Names.cs"), "backslash owned-output path");
                AssertRejects(() => ValidateOwnedRelativePath("Scopes/Names.txt"), "non-C# owned-output path");
                AssertRejects(() => ValidateOwnedRelativePath("Scopes./Names.cs"), "trailing-dot owned-output directory");
                AssertRejects(() => ValidateOwnedRelativePath("Scopes/CON.cs"), "reserved owned-output filename");
                AssertRejects(() => ValidateOwnedRelativePath("Scopes/Name?.cs"), "wildcard owned-output path");
                AssertRejects(() => ValidateOwnedRelativePath("Scop\u200Bes/Names.cs"), "format-character owned-output path");
                AssertArgumentRejects(
                    () => ToolArguments.Parse(new[] { "--validate-onli", "true" }),
                    "unknown generation argument");
                AssertArgumentRejects(
                    () => ToolArguments.Parse(new[] { "--validate-only", "--validate-only" }),
                    "duplicate validate-only argument");

                var configuredColumns = new HashSet<string>(StringComparer.Ordinal)
                {
                    "name",
                    "comment",
                    "enabled",
                    "scope",
                };
                ValidateConfiguredColumns(
                    configuredColumns,
                    "name",
                    "comment",
                    "enabled",
                    "scope",
                    "self-test workbook");
                AssertRejects(
                    () => ValidateConfiguredColumns(
                        configuredColumns,
                        "missing_name",
                        "comment",
                        "enabled",
                        "scope",
                        "self-test workbook"),
                    "missing configured workbook column");

                Dictionary<string, string> sparseRow = XlsxWorkbook.MaterializeDeclaredRow(
                    new Dictionary<int, string>
                    {
                        { 1, "Sword" },
                        { 4096, "ignored undeclared cell" },
                    },
                    new Dictionary<int, string>
                    {
                        { 1, "name" },
                        { 2, "comment" },
                        { 3, "enabled" },
                    });
                if (sparseRow.Count != 1 || sparseRow["name"] != "Sword")
                {
                    throw new InvalidOperationException(
                        "Self-test failed: sparse workbook rows materialized missing or undeclared cells.");
                }

                string[] stalePaths = CalculateStaleOwnedRelativePaths(
                    new[] { "GameplayTagNames.cs", "Scopes/OldNames.cs" },
                    new[] { "GameplayTagNames.cs", "Scopes/NewNames.cs" });
                AssertSequenceEqual(
                    new[] { "Scopes/OldNames.cs" },
                    stalePaths,
                    "stale owned-output calculation");
                AssertRejects(
                    () => CalculateStaleOwnedRelativePaths(
                        new[] { "Case/Names.cs" },
                        new[] { "case/Names.cs" }),
                    "case-only owned-output transition");
                AssertRejects(
                    () => CalculateStaleOwnedRelativePaths(
                        new[] { "Case/OldNames.cs" },
                        new[] { "case/NewNames.cs" }),
                    "case-only owned-output directory transition");

                string manifestContent = BuildOwnedOutputManifestContent(
                    new[] { "GameplayTagNames.cs", "Scopes/AbilityNames.cs" });
                using (var manifestStream = new MemoryStream(Encoding.UTF8.GetBytes(manifestContent)))
                {
                    AssertSequenceEqual(
                        new[] { "GameplayTagNames.cs", "Scopes/AbilityNames.cs" },
                        ParseOwnedOutputManifest(manifestStream, "self-test manifest"),
                        "owned-output manifest round trip");
                }

                AssertRejects(
                    () => ParseOwnedOutputManifest(
                        new MemoryStream(Encoding.UTF8.GetBytes(
                            "{\"schema\":\"" + OWNED_OUTPUT_MANIFEST_SCHEMA +
                            "\",\"version\":" + OWNED_OUTPUT_MANIFEST_VERSION +
                            ",\"ownedFiles\":[\"../Escape.cs\"]}")),
                        "self-test traversal manifest"),
                    "manifest traversal path");
                AssertRejects(
                    () => ParseOwnedOutputManifest(
                        new MemoryStream(Encoding.UTF8.GetBytes(
                            "{\"schema\":\"" + OWNED_OUTPUT_MANIFEST_SCHEMA +
                            "\",\"version\":" + (OWNED_OUTPUT_MANIFEST_VERSION + 1) +
                            ",\"ownedFiles\":[]}")),
                        "self-test unsupported manifest"),
                    "unsupported manifest version");
                AssertRejects(
                    () => ParseOwnedOutputManifest(
                        new MemoryStream(Encoding.UTF8.GetBytes(
                            "{\"schema\":\"" + OWNED_OUTPUT_MANIFEST_SCHEMA +
                            "\",\"version\":" + OWNED_OUTPUT_MANIFEST_VERSION +
                            ",\"ownedFiles\":[\"Case/Names.cs\",\"case/Names.cs\"]}")),
                        "self-test case-colliding manifest"),
                    "manifest case-only collision");
            }

            private static void AssertRejects(Action action, string description)
            {
                try
                {
                    action();
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                throw new InvalidOperationException("Self-test failed to reject " + description + ".");
            }

            private static void AssertArgumentRejects(Action action, string description)
            {
                try
                {
                    action();
                }
                catch (ArgumentException)
                {
                    return;
                }

                throw new InvalidOperationException("Self-test failed to reject " + description + ".");
            }

            private static void AssertEqual(string expected, string actual, string description)
            {
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Self-test failed for {description}. Expected '{expected}', actual '{actual}'.");
                }
            }

            private static void AssertContains(string value, string expected, string description)
            {
                if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        $"Self-test failed for {description}. Missing fragment '{expected}'.");
                }
            }

            private static void AssertSequenceEqual(
                IReadOnlyList<string> expected,
                IReadOnlyList<string> actual,
                string description)
            {
                if (expected.Count != actual.Count)
                {
                    throw new InvalidOperationException(
                        $"Self-test failed for {description}. Expected {expected.Count} item(s), actual {actual.Count}.");
                }

                for (int i = 0; i < expected.Count; i++)
                {
                    if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Self-test failed for {description} at index {i}. " +
                            $"Expected '{expected[i]}', actual '{actual[i]}'.");
                    }
                }
            }
        }
    }
}
