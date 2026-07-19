using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class StringConstantGenerator
        {
            private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(
                new[]
                {
                    "abstract", "add", "alias", "allows", "and", "args", "as", "ascending", "async", "await", "base", "bool",
                    "break", "by", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
                    "decimal", "default", "delegate", "descending", "do", "double", "dynamic", "else", "enum",
                    "equals", "event", "explicit", "extension", "extern", "false", "field", "file", "finally", "fixed", "float", "for",
                    "foreach", "from", "get", "global", "goto", "group", "if", "implicit", "in", "init", "int",
                    "interface", "internal", "into", "is", "join", "let", "lock", "long", "managed", "nameof",
                    "namespace", "new", "nint", "not", "notnull", "null", "nuint", "object", "on", "operator",
                    "or", "orderby", "out", "override", "params", "partial", "private", "protected", "public",
                    "readonly", "record", "ref", "remove", "required", "return", "sbyte", "scoped", "sealed",
                    "select", "set", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
                    "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unmanaged", "unsafe",
                    "ushort", "using", "value", "var", "virtual", "void", "volatile", "when", "where", "while",
                    "with", "yield", "__arglist", "__makeref", "__reftype", "__refvalue",
                },
                StringComparer.Ordinal);

            public static void Run(ToolArguments arguments)
            {
                Dictionary<string, string> buildConfig = IniFile.Read(arguments.ConfigPath);
                string[] configuredTables = SplitList(GetOptional(buildConfig, "string_constant_tables"));
                if (configuredTables.Length > MAX_CONFIGURED_TABLES)
                {
                    throw new InvalidOperationException(
                        $"Configured table count {configuredTables.Length} exceeds the limit {MAX_CONFIGURED_TABLES}.");
                }

                EnsureDistinctConfiguredTables(configuredTables);
                Dictionary<string, PendingOutput> pendingOutputs =
                    new Dictionary<string, PendingOutput>(StringComparer.OrdinalIgnoreCase);
                long pendingOutputCharacters = 0;

                if (configuredTables.Length > 0)
                {
                    string valueColumn = GetOptional(buildConfig, "string_constant_value_column", DEFAULT_VALUE_COLUMN);
                    string commentColumn = GetOptionalAllowEmpty(
                        buildConfig,
                        "string_constant_comment_column",
                        DEFAULT_COMMENT_COLUMN);
                    string enabledColumn = GetOptionalAllowEmpty(
                        buildConfig,
                        "string_constant_enabled_column",
                        DEFAULT_ENABLED_COLUMN);
                    string scopeColumn = GetOptional(buildConfig, "string_constant_scope_column");
                    string generatedCommentLanguage = GetOptional(
                        buildConfig,
                        "string_constant_generated_comment_language",
                        DEFAULT_GENERATED_COMMENT_LANGUAGE);
                    string lineEnding = string.Equals(arguments.LineEnding, "lf", StringComparison.OrdinalIgnoreCase)
                        ? "\n"
                        : "\r\n";

                    LubanTarget target = LubanConf.ReadTarget(arguments.LubanConfPath, arguments.Target);
                    Dictionary<string, Dictionary<string, string>> tableRows = ReadTableDeclarations(arguments.DataDir);
                    for (int i = 0; i < configuredTables.Length; i++)
                    {
                        GenerateTableConstants(
                            configuredTables[i],
                            tableRows,
                            target,
                            arguments,
                            valueColumn,
                            commentColumn,
                            enabledColumn,
                            scopeColumn,
                            generatedCommentLanguage,
                            lineEnding,
                            pendingOutputs,
                            ref pendingOutputCharacters);
                    }
                }

                ValidatePendingOutputBudget(pendingOutputs);
                OwnedOutputPlan ownedOutputPlan = BuildOwnedOutputPlan(arguments.CodeOutputDir, pendingOutputs);
                if (arguments.ValidateOnly)
                {
                    Console.WriteLine(
                        $"[DataTable.CodeGen] Validation completed. {pendingOutputs.Count} file(s) would be generated, " +
                        $"{ownedOutputPlan.ExistingStaleOutputPaths.Length} stale owned .cs file(s) would be deleted, " +
                        $"and {ownedOutputPlan.MissingStaleRegistrationCount} missing stale registration(s) would be pruned.");
                    return;
                }

                CommitOutputs(arguments.CodeOutputDir, pendingOutputs, ownedOutputPlan);
            }

            private static void EnsureDistinctConfiguredTables(string[] configuredTables)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < configuredTables.Length; i++)
                {
                    if (!names.Add(configuredTables[i]))
                    {
                        throw new InvalidOperationException("Duplicate configured string constant table: " + configuredTables[i]);
                    }
                }
            }

            private static Dictionary<string, Dictionary<string, string>> ReadTableDeclarations(string dataDir)
            {
                string tableSchemaPath = Path.Combine(dataDir, TABLES_SCHEMA_FILE);
                Dictionary<string, string>[] rows = XlsxWorkbook.ReadRows(tableSchemaPath);
                Dictionary<string, Dictionary<string, string>> tableRows =
                    new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

                for (int i = 0; i < rows.Length; i++)
                {
                    Dictionary<string, string> row = rows[i];
                    if (row.TryGetValue(FULL_NAME_COLUMN, out string? fullName) &&
                        !string.IsNullOrWhiteSpace(fullName))
                    {
                        string normalizedName = fullName.Trim();
                        if (!tableRows.TryAdd(normalizedName, row))
                        {
                            throw new InvalidOperationException(
                                $"Duplicate table declaration '{normalizedName}' in {TABLES_SCHEMA_FILE}.");
                        }
                    }
                }

                return tableRows;
            }

            private static void GenerateTableConstants(
                string tableFullName,
                Dictionary<string, Dictionary<string, string>> tableRows,
                LubanTarget target,
                ToolArguments arguments,
                string valueColumn,
                string commentColumn,
                string enabledColumn,
                string scopeColumn,
                string generatedCommentLanguage,
                string lineEnding,
                Dictionary<string, PendingOutput> pendingOutputs,
                ref long pendingOutputCharacters)
            {
                if (!tableRows.TryGetValue(tableFullName, out Dictionary<string, string>? tableRow))
                {
                    throw new InvalidOperationException($"String constant table is not declared in {TABLES_SCHEMA_FILE}: {tableFullName}");
                }

                string inputFile = GetRequired(tableRow, INPUT_COLUMN, tableFullName);
                string workbookPath = ResolveContainedFile(arguments.DataDir, inputFile, "table workbook");
                string tableNamespace = GetNamespacePart(tableFullName);
                string tableName = GetNamePart(tableFullName);
                string namespaceName = CombineNamespace(target.TopModule, tableNamespace);
                string classNameBase = InferConstantClassNameBase(tableName);
                ValidateNamespaceIdentifier(namespaceName, $"generated namespace for table '{tableFullName}'");

                Dictionary<string, string>[] dataRows = XlsxWorkbook.ReadRows(
                    workbookPath,
                    out HashSet<string> dataColumns);
                ValidateConfiguredColumns(
                    dataColumns,
                    valueColumn,
                    commentColumn,
                    enabledColumn,
                    scopeColumn,
                    workbookPath);
                Dictionary<string, List<ConstantEntry>> entriesByScope = CollectEntriesByScope(
                    dataRows,
                    valueColumn,
                    commentColumn,
                    enabledColumn,
                    scopeColumn);
                Dictionary<string, string> scopeByClassName = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (KeyValuePair<string, List<ConstantEntry>> scopePair in entriesByScope.OrderBy(static item => item.Key, StringComparer.Ordinal))
                {
                    string scope = scopePair.Key;
                    string className = CreateClassName(classNameBase, scope);
                    ValidateCSharpIdentifier(className, $"generated class for table '{tableFullName}'");
                    if (scopeByClassName.TryGetValue(className, out string? existingScope))
                    {
                        throw new InvalidOperationException(
                            $"Scopes '{existingScope}' and '{scope}' both generate class name '{className}'. " +
                            "Rename one scope or split the tables.");
                    }

                    scopeByClassName.Add(className, scope);
                    string outputPath = ResolveContainedOutputPath(arguments.CodeOutputDir, Path.Combine(
                        arguments.CodeOutputDir,
                        tableNamespace.Replace('.', Path.DirectorySeparatorChar),
                        className + ".cs"));

                    string content = BuildConstantsFile(
                        namespaceName,
                        className,
                        inputFile,
                        scope,
                        scopePair.Value,
                        generatedCommentLanguage,
                        lineEnding);

                    if (pendingOutputs.TryGetValue(outputPath, out PendingOutput? existingOutput))
                    {
                        throw new InvalidOperationException(
                            "Generated output path collision (case-insensitive for cross-platform safety):\n" +
                            $"  Existing: {existingOutput.OutputPath}\n" +
                            $"  New     : {outputPath}");
                    }

                    if (pendingOutputs.Count >= MAX_OWNED_OUTPUT_FILES)
                    {
                        throw new InvalidOperationException(
                            $"Generated output count would exceed the owned-output limit {MAX_OWNED_OUTPUT_FILES}.");
                    }

                    long nextTotalCharacters = checked(pendingOutputCharacters + content.Length);
                    if (nextTotalCharacters > MAX_TOTAL_GENERATED_CHARACTERS)
                    {
                        throw new InvalidOperationException(
                            $"Generated output exceeds the total {MAX_TOTAL_GENERATED_CHARACTERS}-character budget.");
                    }

                    pendingOutputs.Add(outputPath, new PendingOutput(outputPath, content));
                    pendingOutputCharacters = nextTotalCharacters;

                    Console.WriteLine($"[DataTable.CodeGen] Prepared {scopePair.Value.Count} string constants: {outputPath}");
                }
            }

            private static void ValidateConfiguredColumns(
                ISet<string> columns,
                string valueColumn,
                string commentColumn,
                string enabledColumn,
                string scopeColumn,
                string workbookPath)
            {
                RequireConfiguredColumn(columns, valueColumn, "value", workbookPath);
                RequireConfiguredColumn(columns, commentColumn, "comment", workbookPath);
                RequireConfiguredColumn(columns, enabledColumn, "enabled", workbookPath);
                RequireConfiguredColumn(columns, scopeColumn, "scope", workbookPath);
            }

            private static void RequireConfiguredColumn(
                ISet<string> columns,
                string columnName,
                string role,
                string workbookPath)
            {
                if (string.IsNullOrEmpty(columnName))
                {
                    return;
                }

                if (!columns.Contains(columnName))
                {
                    throw new InvalidOperationException(
                        $"Configured {role} column '{columnName}' is missing from the ##var header: {workbookPath}");
                }
            }

            private static Dictionary<string, List<ConstantEntry>> CollectEntriesByScope(
                Dictionary<string, string>[] rows,
                string valueColumn,
                string commentColumn,
                string enabledColumn,
                string scopeColumn)
            {
                Dictionary<string, List<ConstantEntry>> entriesByScope =
                    new Dictionary<string, List<ConstantEntry>>(StringComparer.Ordinal);
                Dictionary<string, HashSet<string>> constantNamesByScope =
                    new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

                for (int i = 0; i < rows.Length; i++)
                {
                    Dictionary<string, string> row = rows[i];
                    if (!row.TryGetValue(valueColumn, out string? value) ||
                        string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (row.TryGetValue(enabledColumn, out string? enabledValue) &&
                        !IsEnabled(enabledValue))
                    {
                        continue;
                    }

                    string scope = string.Empty;
                    if (!string.IsNullOrEmpty(scopeColumn) &&
                        row.TryGetValue(scopeColumn, out string? scopeValue))
                    {
                        scope = scopeValue.Trim();
                    }

                    string constantName = ToConstantName(value.Trim(), scope);
                    if (!constantNamesByScope.TryGetValue(scope, out HashSet<string>? constantNames))
                    {
                        if (constantNamesByScope.Count >= MAX_OWNED_OUTPUT_FILES)
                        {
                            throw new InvalidOperationException(
                                $"Generated scope count exceeds the owned-output limit {MAX_OWNED_OUTPUT_FILES}.");
                        }

                        constantNames = new HashSet<string>(StringComparer.Ordinal);
                        constantNamesByScope.Add(scope, constantNames);
                    }

                    if (!constantNames.Add(constantName))
                    {
                        throw new InvalidOperationException($"Duplicate generated constant name in scope '{scope}': {constantName}");
                    }

                    if (!entriesByScope.TryGetValue(scope, out List<ConstantEntry>? entries))
                    {
                        entries = new List<ConstantEntry>();
                        entriesByScope.Add(scope, entries);
                    }

                    row.TryGetValue(commentColumn, out string? comment);
                    entries.Add(new ConstantEntry(constantName, value.Trim(), comment ?? string.Empty));
                }

                return entriesByScope;
            }

            private static string BuildConstantsFile(
                string namespaceName,
                string className,
                string inputFile,
                string scope,
                List<ConstantEntry> entries,
                string generatedCommentLanguage,
                string lineEnding)
            {
                ValidateNamespaceIdentifier(namespaceName, "generated namespace");
                ValidateCSharpIdentifier(className, "generated class");
                bool useChineseHeader = IsChineseGeneratedCommentLanguage(generatedCommentLanguage);
                string safeInputFile = NormalizeGeneratedCommentText(inputFile);
                string safeScope = NormalizeGeneratedCommentText(scope);
                var builder = new StringBuilder(4096, MAX_GENERATED_FILE_CHARACTERS);
                void AddLine(string line)
                {
                    long nextLength = (long)builder.Length + line.Length + lineEnding.Length;
                    if (nextLength > MAX_GENERATED_FILE_CHARACTERS)
                    {
                        throw new InvalidOperationException(
                            $"Generated file for class '{className}' exceeds the " +
                            $"{MAX_GENERATED_FILE_CHARACTERS}-character limit.");
                    }

                    builder.Append(line);
                    builder.Append(lineEnding);
                }

                AddLine("//------------------------------------------------------------------------------");
                AddLine("// <auto-generated>");
                AddLine(useChineseHeader
                    ? "//     此文件由 CycloneGames.DataTable.CodeGen 自动生成。"
                    : "//     This file is generated by CycloneGames.DataTable.CodeGen.");
                AddLine(useChineseHeader
                    ? $"//     来源表：{safeInputFile}"
                    : $"//     Source table: {safeInputFile}");

                if (!string.IsNullOrEmpty(scope))
                {
                    AddLine(useChineseHeader ? $"//     分类：{safeScope}" : $"//     Scope: {safeScope}");
                }

                AddLine(useChineseHeader
                    ? "//     重新执行 DataTable 生成时，本文件的手动修改会丢失。"
                    : "//     Manual changes will be lost when DataTable generation runs again.");
                AddLine("// </auto-generated>");
                AddLine("//------------------------------------------------------------------------------");
                AddLine(string.Empty);
                AddLine($"namespace {namespaceName}");
                AddLine("{");
                AddLine($"    public static class {className}");
                AddLine("    {");

                for (int i = 0; i < entries.Count; i++)
                {
                    ConstantEntry entry = entries[i];
                    ValidateCSharpIdentifier(entry.ConstantName, "generated constant");
                    string comment = EscapeXmlComment(entry.Comment);
                    if (!string.IsNullOrEmpty(comment))
                    {
                        AddLine("        /// <summary>");
                        AddLine("        /// " + comment);
                        AddLine("        /// </summary>");
                    }

                    AddLine($"        public const string {entry.ConstantName} = \"{EscapeCSharpString(entry.Value)}\";");
                }

                AddLine("    }");
                AddLine("}");

                return builder.ToString();
            }

            private static string InferConstantClassNameBase(string tableName)
            {
                string name = tableName;
                if (name.StartsWith("Tb", StringComparison.Ordinal) && name.Length > 2)
                {
                    name = name.Substring(2);
                }

                foreach (string suffix in new[] { "Definitions", "Definition", "Table", "Data" })
                {
                    if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                    {
                        name = name.Substring(0, name.Length - suffix.Length);
                        break;
                    }
                }

                return string.IsNullOrEmpty(name) ? tableName : name;
            }

            private static string CreateClassName(string classNameBase, string scope)
            {
                if (string.IsNullOrWhiteSpace(scope))
                {
                    return classNameBase.EndsWith("Names", StringComparison.Ordinal)
                        ? classNameBase
                        : classNameBase + "Names";
                }

                return classNameBase + ToPascalIdentifier(scope) + "Names";
            }

            private static string ToConstantName(string value, string scope)
            {
                string shortenedValue = RemoveScopePrefix(value, scope);
                StringBuilder builder = new StringBuilder(shortenedValue.Length * 2);
                bool previousWasUnderscore = true;
                bool previousWasLowerOrDigit = false;

                for (int i = 0; i < shortenedValue.Length; i++)
                {
                    char character = shortenedValue[i];
                    if (!char.IsLetterOrDigit(character))
                    {
                        if (!previousWasUnderscore)
                        {
                            builder.Append('_');
                            previousWasUnderscore = true;
                        }

                        previousWasLowerOrDigit = false;
                        continue;
                    }

                    if (char.IsUpper(character) && previousWasLowerOrDigit && !previousWasUnderscore)
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToUpperInvariant(character));
                    previousWasUnderscore = false;
                    previousWasLowerOrDigit = char.IsLower(character) || char.IsDigit(character);
                }

                string constantName = builder.ToString().Trim('_');
                if (string.IsNullOrEmpty(constantName))
                {
                    throw new InvalidOperationException($"Cannot generate constant name from value: {value}");
                }

                string result = char.IsDigit(constantName[0]) ? "VALUE_" + constantName : constantName;
                ValidateCSharpIdentifier(result, $"constant generated from value '{NormalizeGeneratedCommentText(value)}'");
                return result;
            }

            private static string RemoveScopePrefix(string value, string scope)
            {
                if (string.IsNullOrWhiteSpace(scope))
                {
                    return value;
                }

                string[] valueSegments = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
                string[] scopeSegments = scope.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (valueSegments.Length == 0 || scopeSegments.Length == 0 || scopeSegments.Length >= valueSegments.Length)
                {
                    return value;
                }

                for (int start = 0; start <= valueSegments.Length - scopeSegments.Length; start++)
                {
                    bool match = true;
                    for (int i = 0; i < scopeSegments.Length; i++)
                    {
                        if (!string.Equals(valueSegments[start + i], scopeSegments[i], StringComparison.Ordinal))
                        {
                            match = false;
                            break;
                        }
                    }

                    if (!match)
                    {
                        continue;
                    }

                    string[] remaining = valueSegments.Skip(start + scopeSegments.Length).ToArray();
                    return remaining.Length == 0 ? value : string.Join(".", remaining);
                }

                return value;
            }

            private static string ToPascalIdentifier(string value)
            {
                StringBuilder builder = new StringBuilder(value.Length);
                bool upperNext = true;
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    if (!char.IsLetterOrDigit(character))
                    {
                        upperNext = true;
                        continue;
                    }

                    if (builder.Length == 0 && char.IsDigit(character))
                    {
                        builder.Append('_');
                    }

                    builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
                    upperNext = false;
                }

                return builder.Length == 0 ? "Default" : builder.ToString();
            }

            private static string EscapeCSharpString(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(value.Length + 16);
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    switch (character)
                    {
                        case '\0': builder.Append("\\0"); break;
                        case '\a': builder.Append("\\a"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        case '\v': builder.Append("\\v"); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '"': builder.Append("\\\""); break;
                        default:
                            UnicodeCategory category = char.GetUnicodeCategory(character);
                            if (char.IsControl(character) ||
                                category == UnicodeCategory.Format ||
                                category == UnicodeCategory.LineSeparator ||
                                category == UnicodeCategory.ParagraphSeparator ||
                                category == UnicodeCategory.Surrogate)
                            {
                                builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }
                            break;
                    }
                }

                return builder.ToString();
            }

            private static string EscapeXmlComment(string value)
            {
                string normalized = NormalizeGeneratedCommentText(value);
                return string.IsNullOrEmpty(normalized)
                    ? string.Empty
                    : SecurityElement.Escape(normalized) ?? string.Empty;
            }

            private static string NormalizeGeneratedCommentText(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(value.Length);
                bool pendingSpace = false;
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    UnicodeCategory category = char.GetUnicodeCategory(character);
                    bool normalizeToSpace = char.IsWhiteSpace(character) ||
                                            char.IsControl(character) ||
                                            category == UnicodeCategory.Format ||
                                            category == UnicodeCategory.LineSeparator ||
                                            category == UnicodeCategory.ParagraphSeparator;
                    if (normalizeToSpace)
                    {
                        pendingSpace = builder.Length > 0;
                        continue;
                    }

                    if (pendingSpace)
                    {
                        builder.Append(' ');
                        pendingSpace = false;
                    }

                    builder.Append(character);
                }

                return builder.ToString();
            }

            private static void ValidateNamespaceIdentifier(string namespaceName, string description)
            {
                if (string.IsNullOrWhiteSpace(namespaceName))
                {
                    throw new InvalidOperationException(description + " is empty.");
                }

                string[] segments = namespaceName.Split('.');
                for (int i = 0; i < segments.Length; i++)
                {
                    ValidateCSharpIdentifier(segments[i], description + " segment");
                }
            }

            private static void ValidateCSharpIdentifier(string value, string description)
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new InvalidOperationException(description + " is empty.");
                }

                if (CSharpKeywords.Contains(value))
                {
                    throw new InvalidOperationException($"{description} is a reserved or contextual C# keyword: {value}");
                }

                if (!IsAsciiIdentifierStart(value[0]))
                {
                    throw new InvalidOperationException($"{description} is not a conservative C# identifier: {value}");
                }

                for (int i = 1; i < value.Length; i++)
                {
                    if (!IsAsciiIdentifierPart(value[i]))
                    {
                        throw new InvalidOperationException($"{description} is not a conservative C# identifier: {value}");
                    }
                }
            }

            private static bool IsAsciiIdentifierStart(char value)
            {
                return value == '_' || value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z';
            }

            private static bool IsAsciiIdentifierPart(char value)
            {
                return IsAsciiIdentifierStart(value) || value >= '0' && value <= '9';
            }

            private static bool IsChineseGeneratedCommentLanguage(string value)
            {
                return string.Equals(value, "zh", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "sch", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "cn", StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsEnabled(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return true;
                }

                string normalized = value.Trim();
                return !string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
