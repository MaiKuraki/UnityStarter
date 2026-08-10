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

            public static void Run(
                ToolArguments arguments,
                StringConstantConfiguration configuration)
            {
                if (configuration == null)
                {
                    throw new ArgumentNullException(nameof(configuration));
                }

                string[] configuredTables = configuration.Tables;
                using var outputSession = new OwnedOutputSession(arguments.CodeOutputDir, arguments.ValidateOnly);
                if (configuredTables.Length > 0)
                {
                    string lineEnding = string.Equals(arguments.LineEnding, "lf", StringComparison.OrdinalIgnoreCase)
                        ? "\n"
                        : "\r\n";

                    LubanTarget target = LubanConf.ReadTarget(arguments.LubanConfPath, arguments.Target);
                    Dictionary<string, string> tableInputs = ReadTableDeclarations(
                        arguments.DataDir,
                        configuredTables);
                    for (int i = 0; i < configuredTables.Length; i++)
                    {
                        GenerateTableConstants(
                            configuredTables[i],
                            tableInputs,
                            target,
                            arguments,
                            configuration.ValueColumn,
                            configuration.CommentColumn,
                            configuration.EnabledColumn,
                            configuration.ScopeColumn,
                            configuration.GeneratedCommentLanguage,
                            lineEnding,
                            outputSession);
                    }
                }

                OwnedOutputPlan ownedOutputPlan = outputSession.BuildPlan();
                if (arguments.ValidateOnly)
                {
                    Console.WriteLine(
                        $"[DataTable.CodeGen] Validation completed. {outputSession.Count} file(s) would be generated, " +
                        $"{ownedOutputPlan.ExistingStaleOutputPaths.Length} stale owned .cs file(s) would be deleted, " +
                        $"and {ownedOutputPlan.MissingStaleRegistrationCount} missing stale registration(s) would be pruned.");
                    return;
                }

                outputSession.Commit(ownedOutputPlan);
            }

            internal static void EnsureDistinctConfiguredTables(string[] configuredTables)
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

            private static Dictionary<string, string> ReadTableDeclarations(
                string dataDir,
                IReadOnlyCollection<string> configuredTables)
            {
                string tableSchemaPath = Path.Combine(dataDir, TABLES_SCHEMA_FILE);
                var visitor = new TableDeclarationVisitor(configuredTables);
                var projection = new XlsxWorkbook.ColumnProjection(FULL_NAME_COLUMN, INPUT_COLUMN);
                XlsxWorkbook.VisitRows(tableSchemaPath, projection, visitor);
                return visitor.InputByTable;
            }

            private static void GenerateTableConstants(
                string tableFullName,
                Dictionary<string, string> tableInputs,
                LubanTarget target,
                ToolArguments arguments,
                string valueColumn,
                string commentColumn,
                string enabledColumn,
                string scopeColumn,
                string generatedCommentLanguage,
                string lineEnding,
                OwnedOutputSession outputSession)
            {
                if (!tableInputs.TryGetValue(tableFullName, out string? inputFile))
                {
                    throw new InvalidOperationException(
                        $"String constant table is not declared in {TABLES_SCHEMA_FILE}: {tableFullName}");
                }

                string workbookPath = ResolveContainedFile(arguments.DataDir, inputFile, "table workbook");
                string tableNamespace = GetNamespacePart(tableFullName);
                string tableName = GetNamePart(tableFullName);
                string namespaceName = CombineNamespace(target.TopModule, tableNamespace);
                string classNameBase = InferConstantClassNameBase(tableName);
                ValidateNamespaceIdentifier(namespaceName, $"generated namespace for table '{tableFullName}'");

                var columns = new ProjectionColumns();
                int valueIndex = columns.AddRequired(valueColumn, "value");
                int commentIndex = columns.AddOptional(commentColumn);
                int enabledIndex = columns.AddOptional(enabledColumn);
                int scopeIndex = columns.AddOptional(scopeColumn);
                var visitor = new ConstantCollectionVisitor(
                    valueIndex,
                    commentIndex,
                    enabledIndex,
                    scopeIndex);
                XlsxWorkbook.ColumnProjection projection = columns.Build();
                XlsxWorkbook.VisitRows(workbookPath, projection, visitor);
                Dictionary<string, List<ConstantEntry>> entriesByScope = visitor.EntriesByScope;
                Dictionary<string, string> scopeByClassName = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (KeyValuePair<string, List<ConstantEntry>> scopePair in entriesByScope.OrderBy(
                             static item => item.Key,
                             StringComparer.Ordinal))
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
                    string outputPath = ResolveContainedOutputPath(
                        arguments.CodeOutputDir,
                        Path.Combine(
                            arguments.CodeOutputDir,
                            tableNamespace.Replace('.', Path.DirectorySeparatorChar),
                            className + ".cs"));
                    outputSession.Stage(
                        outputPath,
                        writer => WriteConstantsFile(
                            writer,
                            namespaceName,
                            className,
                            inputFile,
                            scope,
                            scopePair.Value,
                            generatedCommentLanguage,
                            lineEnding));
                    Console.WriteLine(
                        $"[DataTable.CodeGen] Prepared {scopePair.Value.Count} string constants: {outputPath}");
                }
            }

            private static void WriteConstantsFile(
                TextWriter writer,
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

                void AddLine(string line)
                {
                    writer.Write(line);
                    writer.Write(lineEnding);
                }

                AddLine("//------------------------------------------------------------------------------");
                AddLine("// <auto-generated>");
                AddLine(useChineseHeader
                    ? "//     \u6b64\u6587\u4ef6\u7531 CycloneGames.DataTable.CodeGen \u81ea\u52a8\u751f\u6210\u3002"
                    : "//     This file is generated by CycloneGames.DataTable.CodeGen.");
                AddLine(useChineseHeader
                    ? $"//     \u6765\u6e90\u8868\uff1a{safeInputFile}"
                    : $"//     Source table: {safeInputFile}");

                if (!string.IsNullOrEmpty(scope))
                {
                    AddLine(useChineseHeader ? $"//     \u5206\u7ec4\uff1a{safeScope}" : $"//     Scope: {safeScope}");
                }

                AddLine(useChineseHeader
                    ? "//     \u91cd\u65b0\u8fd0\u884c DataTable \u751f\u6210\u65f6\uff0c\u672c\u6587\u4ef6\u7684\u624b\u52a8\u4fee\u6539\u4f1a\u4e22\u5931\u3002"
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
            }

            private sealed class TableDeclarationVisitor : XlsxWorkbook.IRowVisitor
            {
                private readonly HashSet<string> _requestedTables;

                public TableDeclarationVisitor(IEnumerable<string> requestedTables)
                {
                    _requestedTables = new HashSet<string>(requestedTables, StringComparer.Ordinal);
                    InputByTable = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                public Dictionary<string, string> InputByTable { get; }

                public void Visit(in XlsxWorkbook.ProjectedRow row)
                {
                    string fullName = row.GetValue(0).Trim();
                    if (fullName.Length == 0 || !_requestedTables.Contains(fullName))
                    {
                        return;
                    }

                    string input = row.GetValue(1).Trim();
                    if (input.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"Row '{fullName}' is missing required column: {INPUT_COLUMN}");
                    }

                    if (!InputByTable.TryAdd(fullName, input))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate table declaration '{fullName}' in {TABLES_SCHEMA_FILE}.");
                    }
                }
            }

            private sealed class ConstantCollectionVisitor : XlsxWorkbook.IRowVisitor
            {
                private readonly int _valueIndex;
                private readonly int _commentIndex;
                private readonly int _enabledIndex;
                private readonly int _scopeIndex;
                private readonly Dictionary<string, HashSet<string>> _constantNamesByScope =
                    new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                private readonly Dictionary<string, string> _retainedStrings =
                    new Dictionary<string, string>(StringComparer.Ordinal);

                public ConstantCollectionVisitor(
                    int valueIndex,
                    int commentIndex,
                    int enabledIndex,
                    int scopeIndex)
                {
                    _valueIndex = valueIndex;
                    _commentIndex = commentIndex;
                    _enabledIndex = enabledIndex;
                    _scopeIndex = scopeIndex;
                    EntriesByScope = new Dictionary<string, List<ConstantEntry>>(StringComparer.Ordinal);
                }

                public Dictionary<string, List<ConstantEntry>> EntriesByScope { get; }

                public void Visit(in XlsxWorkbook.ProjectedRow row)
                {
                    string value = row.GetValue(_valueIndex);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return;
                    }

                    if (_enabledIndex >= 0 &&
                        row.HasValue(_enabledIndex) &&
                        !IsEnabled(row.GetValue(_enabledIndex)))
                    {
                        return;
                    }

                    string scope = _scopeIndex >= 0
                        ? Retain(row.GetValue(_scopeIndex).Trim())
                        : string.Empty;
                    string normalizedValue = Retain(value.Trim());
                    string constantName = ToConstantName(normalizedValue, scope);
                    if (!_constantNamesByScope.TryGetValue(scope, out HashSet<string>? constantNames))
                    {
                        if (_constantNamesByScope.Count >= MAX_OWNED_OUTPUT_FILES)
                        {
                            throw new InvalidOperationException(
                                $"Generated scope count exceeds the owned-output limit {MAX_OWNED_OUTPUT_FILES}.");
                        }

                        constantNames = new HashSet<string>(StringComparer.Ordinal);
                        _constantNamesByScope.Add(scope, constantNames);
                    }

                    if (!constantNames.Add(constantName))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate generated constant name in scope '{scope}': {constantName}");
                    }

                    if (!EntriesByScope.TryGetValue(scope, out List<ConstantEntry>? entries))
                    {
                        entries = new List<ConstantEntry>();
                        EntriesByScope.Add(scope, entries);
                    }

                    string comment = _commentIndex >= 0
                        ? Retain(row.GetValue(_commentIndex))
                        : string.Empty;
                    entries.Add(new ConstantEntry(constantName, normalizedValue, comment));
                }

                private string Retain(string value)
                {
                    if (value.Length == 0)
                    {
                        return string.Empty;
                    }

                    if (_retainedStrings.TryGetValue(value, out string? retained))
                    {
                        return retained;
                    }

                    _retainedStrings.Add(value, value);
                    return value;
                }
            }

            private sealed class ProjectionColumns
            {
                private readonly List<string> _names = new List<string>();

                public int AddRequired(string name, string role)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw new InvalidOperationException($"Configured {role} column cannot be empty.");
                    }

                    return Add(name);
                }

                public int AddOptional(string name)
                {
                    return string.IsNullOrEmpty(name) ? -1 : Add(name);
                }

                public XlsxWorkbook.ColumnProjection Build()
                {
                    return new XlsxWorkbook.ColumnProjection(_names.ToArray());
                }

                private int Add(string name)
                {
                    for (int i = 0; i < _names.Count; i++)
                    {
                        if (string.Equals(_names[i], name, StringComparison.Ordinal))
                        {
                            return i;
                        }
                    }

                    _names.Add(name);
                    return _names.Count - 1;
                }
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
