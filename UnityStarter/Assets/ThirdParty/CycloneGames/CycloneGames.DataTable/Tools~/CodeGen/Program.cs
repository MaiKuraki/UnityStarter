using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace CycloneGames.DataTable.CodeGen
{
    internal static class Program
    {
        private const string TABLES_SCHEMA_FILE = "__tables__.xlsx";
        private const string FULL_NAME_COLUMN = "full_name";
        private const string INPUT_COLUMN = "input";
        private const string DEFAULT_VALUE_COLUMN = "name";
        private const string DEFAULT_COMMENT_COLUMN = "comment";
        private const string DEFAULT_ENABLED_COLUMN = "enabled";
        private const string DEFAULT_GENERATED_COMMENT_LANGUAGE = "en";

        private static int Main(string[] args)
        {
            try
            {
                ToolArguments arguments = ToolArguments.Parse(args);
                StringConstantGenerator.Run(arguments);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[DataTable.CodeGen] " + exception.Message);
                return 1;
            }
        }

        private static class StringConstantGenerator
        {
            public static void Run(ToolArguments arguments)
            {
                Dictionary<string, string> buildConfig = IniFile.Read(arguments.ConfigPath);
                string[] configuredTables = SplitList(GetOptional(buildConfig, "string_constant_tables"));
                if (configuredTables.Length == 0)
                {
                    return;
                }

                string valueColumn = GetOptional(buildConfig, "string_constant_value_column", DEFAULT_VALUE_COLUMN);
                string commentColumn = GetOptionalAllowEmpty(
                    buildConfig,
                    "string_constant_comment_column",
                    DEFAULT_COMMENT_COLUMN);
                string enabledColumn = GetOptional(buildConfig, "string_constant_enabled_column", DEFAULT_ENABLED_COLUMN);
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
                        lineEnding);
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
                        tableRows[fullName.Trim()] = row;
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
                string lineEnding)
            {
                if (!tableRows.TryGetValue(tableFullName, out Dictionary<string, string>? tableRow))
                {
                    throw new InvalidOperationException($"String constant table is not declared in {TABLES_SCHEMA_FILE}: {tableFullName}");
                }

                string inputFile = GetRequired(tableRow, INPUT_COLUMN, tableFullName);
                string workbookPath = Path.GetFullPath(Path.Combine(arguments.DataDir, inputFile));
                string tableNamespace = GetNamespacePart(tableFullName);
                string tableName = GetNamePart(tableFullName);
                string namespaceName = CombineNamespace(target.TopModule, tableNamespace);
                string classNameBase = InferConstantClassNameBase(tableName);

                Dictionary<string, string>[] dataRows = XlsxWorkbook.ReadRows(workbookPath);
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
                    if (scopeByClassName.TryGetValue(className, out string? existingScope))
                    {
                        throw new InvalidOperationException(
                            $"Scopes '{existingScope}' and '{scope}' both generate class name '{className}'. " +
                            "Rename one scope or split the tables.");
                    }

                    scopeByClassName.Add(className, scope);
                    string outputPath = Path.Combine(
                        arguments.CodeOutputDir,
                        tableNamespace.Replace('.', Path.DirectorySeparatorChar),
                        className + ".cs");

                    WriteConstantsFile(
                        outputPath,
                        namespaceName,
                        className,
                        inputFile,
                        scope,
                        scopePair.Value,
                        generatedCommentLanguage,
                        lineEnding);

                    Console.WriteLine($"[DataTable.CodeGen] Generated {scopePair.Value.Count} string constants: {outputPath}");
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

            private static void WriteConstantsFile(
                string outputPath,
                string namespaceName,
                string className,
                string inputFile,
                string scope,
                List<ConstantEntry> entries,
                string generatedCommentLanguage,
                string lineEnding)
            {
                bool useChineseHeader = IsChineseGeneratedCommentLanguage(generatedCommentLanguage);
                List<string> lines = new List<string>(entries.Count * 5 + 18)
                {
                    "//------------------------------------------------------------------------------",
                    "// <auto-generated>",
                    useChineseHeader
                        ? "//     此文件由 CycloneGames.DataTable.CodeGen 自动生成。"
                        : "//     This file is generated by CycloneGames.DataTable.CodeGen.",
                    useChineseHeader
                        ? $"//     来源表：{inputFile}"
                        : $"//     Source table: {inputFile}",
                };

                if (!string.IsNullOrEmpty(scope))
                {
                    lines.Add(useChineseHeader ? $"//     分类：{scope}" : $"//     Scope: {scope}");
                }

                lines.Add(useChineseHeader
                    ? "//     重新执行 DataTable 生成时，本文件的手动修改会丢失。"
                    : "//     Manual changes will be lost when DataTable generation runs again.");
                lines.Add("// </auto-generated>");
                lines.Add("//------------------------------------------------------------------------------");
                lines.Add(string.Empty);
                lines.Add($"namespace {namespaceName}");
                lines.Add("{");
                lines.Add($"    public static class {className}");
                lines.Add("    {");

                for (int i = 0; i < entries.Count; i++)
                {
                    ConstantEntry entry = entries[i];
                    string comment = EscapeXmlComment(entry.Comment);
                    if (!string.IsNullOrEmpty(comment))
                    {
                        lines.Add("        /// <summary>");
                        lines.Add("        /// " + comment);
                        lines.Add("        /// </summary>");
                    }

                    lines.Add($"        public const string {entry.ConstantName} = \"{EscapeCSharpString(entry.Value)}\";");
                }

                lines.Add("    }");
                lines.Add("}");

                string? outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                File.WriteAllText(outputPath, string.Join(lineEnding, lines) + lineEnding, new UTF8Encoding(false));
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

                return char.IsDigit(constantName[0]) ? "VALUE_" + constantName : constantName;
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
                return value
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal)
                    .Replace("\r", "\\r", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);
            }

            private static string EscapeXmlComment(string value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : SecurityElement.Escape(value.Trim()) ?? string.Empty;
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

        private sealed class ToolArguments
        {
            public string ConfigPath { get; private init; } = string.Empty;
            public string LubanConfPath { get; private init; } = string.Empty;
            public string DataDir { get; private init; } = string.Empty;
            public string Target { get; private init; } = string.Empty;
            public string CodeOutputDir { get; private init; } = string.Empty;
            public string LineEnding { get; private init; } = "crlf";

            public static ToolArguments Parse(string[] args)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i];
                    if (!key.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unexpected argument: {key}");
                    }

                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"Missing value for argument: {key}");
                    }

                    values[key.Substring(2)] = args[++i];
                }

                return new ToolArguments
                {
                    ConfigPath = RequirePath(values, "config"),
                    LubanConfPath = RequirePath(values, "luban-conf"),
                    DataDir = RequirePath(values, "data-dir"),
                    Target = RequireValue(values, "target"),
                    CodeOutputDir = RequirePath(values, "code-output"),
                    LineEnding = values.TryGetValue("line-ending", out string? lineEnding) ? lineEnding : "crlf",
                };
            }

            private static string RequirePath(Dictionary<string, string> values, string key)
            {
                return Path.GetFullPath(RequireValue(values, key));
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

        private readonly struct LubanTarget
        {
            public LubanTarget(string name, string topModule)
            {
                Name = name;
                TopModule = topModule;
            }

            public string Name { get; }
            public string TopModule { get; }
        }

        private static class LubanConf
        {
            public static LubanTarget ReadTarget(string path, string targetName)
            {
                using FileStream stream = File.OpenRead(path);
                using JsonDocument document = JsonDocument.Parse(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    });

                JsonElement targets = document.RootElement.GetProperty("targets");
                foreach (JsonElement target in targets.EnumerateArray())
                {
                    string? name = target.GetProperty("name").GetString();
                    if (!string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string topModule = target.TryGetProperty("topModule", out JsonElement topModuleValue)
                        ? topModuleValue.GetString() ?? string.Empty
                        : string.Empty;
                    return new LubanTarget(name ?? targetName, topModule);
                }

                throw new InvalidOperationException($"Luban target not found: {targetName}");
            }
        }

        private static class IniFile
        {
            public static Dictionary<string, string> Read(string path)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 ||
                        line[0] == '#' ||
                        line[0] == ';' ||
                        line[0] == '[')
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    string value = line.Substring(separatorIndex + 1).Trim();
                    values[key] = value;
                }

                return values;
            }
        }

        private static class XlsxWorkbook
        {
            private const string SPREADSHEET_NAMESPACE = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            private const string RELATIONSHIPS_NAMESPACE = "http://schemas.openxmlformats.org/package/2006/relationships";
            private const string OFFICE_RELATIONSHIPS_NAMESPACE = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            public static Dictionary<string, string>[] ReadRows(string path)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Workbook not found.", path);
                }

                using ZipArchive archive = ZipFile.OpenRead(path);
                string[] sharedStrings = ReadSharedStrings(archive);
                string worksheetPath = ReadFirstWorksheetPath(archive);
                XmlDocument worksheet = ReadXml(archive, worksheetPath);
                XmlNamespaceManager namespaceManager = CreateSpreadsheetNamespaceManager(worksheet);

                SortedDictionary<int, Dictionary<int, string>> rows = new SortedDictionary<int, Dictionary<int, string>>();
                XmlNodeList? rowNodes = worksheet.SelectNodes("/x:worksheet/x:sheetData/x:row", namespaceManager);
                if (rowNodes != null)
                {
                    foreach (XmlElement rowNode in rowNodes.OfType<XmlElement>())
                    {
                        int rowIndex = int.Parse(rowNode.GetAttribute("r"));
                        Dictionary<int, string> values = new Dictionary<int, string>();
                        XmlNodeList? cells = rowNode.SelectNodes("x:c", namespaceManager);
                        if (cells != null)
                        {
                            foreach (XmlElement cell in cells.OfType<XmlElement>())
                            {
                                int columnIndex = ColumnNameToIndex(cell.GetAttribute("r"));
                                values[columnIndex] = ReadCellValue(cell, sharedStrings, namespaceManager);
                            }
                        }

                        rows[rowIndex] = values;
                    }
                }

                int varRowIndex = FindVarRowIndex(rows, path);
                Dictionary<string, int> columnByName = BuildColumnMap(rows[varRowIndex]);
                List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();

                foreach (KeyValuePair<int, Dictionary<int, string>> rowPair in rows)
                {
                    if (rowPair.Key < varRowIndex + 4)
                    {
                        continue;
                    }

                    Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, int> column in columnByName)
                    {
                        row[column.Key] = rowPair.Value.TryGetValue(column.Value, out string? value) ? value : string.Empty;
                    }

                    if (row.Values.Any(static value => !string.IsNullOrWhiteSpace(value)))
                    {
                        result.Add(row);
                    }
                }

                return result.ToArray();
            }

            private static string[] ReadSharedStrings(ZipArchive archive)
            {
                ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
                if (entry == null)
                {
                    return Array.Empty<string>();
                }

                XmlDocument document = ReadXml(entry);
                XmlNamespaceManager namespaceManager = CreateSpreadsheetNamespaceManager(document);
                XmlNodeList? nodes = document.SelectNodes("/x:sst/x:si", namespaceManager);
                if (nodes == null)
                {
                    return Array.Empty<string>();
                }

                List<string> strings = new List<string>(nodes.Count);
                foreach (XmlNode node in nodes)
                {
                    strings.Add(node.InnerText);
                }

                return strings.ToArray();
            }

            private static string ReadFirstWorksheetPath(ZipArchive archive)
            {
                XmlDocument workbook = ReadXml(archive, "xl/workbook.xml");
                XmlDocument relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
                XmlNamespaceManager workbookNamespace = CreateSpreadsheetNamespaceManager(workbook);
                workbookNamespace.AddNamespace("r", OFFICE_RELATIONSHIPS_NAMESPACE);

                XmlElement? firstSheet = workbook.SelectSingleNode("/x:workbook/x:sheets/x:sheet[1]", workbookNamespace) as XmlElement;
                if (firstSheet == null)
                {
                    throw new InvalidOperationException("Workbook does not contain a worksheet.");
                }

                string relationshipId = firstSheet.GetAttribute("id", OFFICE_RELATIONSHIPS_NAMESPACE);
                XmlNamespaceManager relationshipNamespace = new XmlNamespaceManager(relationships.NameTable);
                relationshipNamespace.AddNamespace("p", RELATIONSHIPS_NAMESPACE);
                XmlNodeList? relationshipNodes = relationships.SelectNodes("/p:Relationships/p:Relationship", relationshipNamespace);
                if (relationshipNodes != null)
                {
                    foreach (XmlElement relationship in relationshipNodes.OfType<XmlElement>())
                    {
                        if (!string.Equals(relationship.GetAttribute("Id"), relationshipId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string target = relationship.GetAttribute("Target").TrimStart('/');
                        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                            ? target
                            : "xl/" + target;
                    }
                }

                throw new InvalidOperationException($"Cannot resolve worksheet relationship: {relationshipId}");
            }

            private static string ReadCellValue(XmlElement cell, string[] sharedStrings, XmlNamespaceManager namespaceManager)
            {
                string type = cell.GetAttribute("t");
                if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
                {
                    XmlNode? inlineNode = cell.SelectSingleNode("x:is", namespaceManager);
                    return inlineNode?.InnerText ?? string.Empty;
                }

                XmlNode? valueNode = cell.SelectSingleNode("x:v", namespaceManager);
                if (valueNode == null)
                {
                    return string.Empty;
                }

                string raw = valueNode.InnerText;
                if (string.Equals(type, "s", StringComparison.Ordinal) &&
                    int.TryParse(raw, out int sharedStringIndex) &&
                    sharedStringIndex >= 0 &&
                    sharedStringIndex < sharedStrings.Length)
                {
                    return sharedStrings[sharedStringIndex];
                }

                return raw;
            }

            private static int FindVarRowIndex(SortedDictionary<int, Dictionary<int, string>> rows, string path)
            {
                foreach (KeyValuePair<int, Dictionary<int, string>> row in rows)
                {
                    if (row.Value.TryGetValue(1, out string? value) &&
                        string.Equals(value, "##var", StringComparison.Ordinal))
                    {
                        return row.Key;
                    }
                }

                throw new InvalidOperationException($"Cannot find ##var row in workbook: {path}");
            }

            private static Dictionary<string, int> BuildColumnMap(Dictionary<int, string> varRow)
            {
                Dictionary<string, int> columnByName = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<int, string> column in varRow)
                {
                    if (!string.IsNullOrWhiteSpace(column.Value))
                    {
                        columnByName[column.Value.Trim()] = column.Key;
                    }
                }

                return columnByName;
            }

            private static int ColumnNameToIndex(string cellReference)
            {
                int value = 0;
                for (int i = 0; i < cellReference.Length; i++)
                {
                    char character = cellReference[i];
                    if (character < 'A' || character > 'Z')
                    {
                        break;
                    }

                    value = value * 26 + character - 'A' + 1;
                }

                return value;
            }

            private static XmlDocument ReadXml(ZipArchive archive, string entryName)
            {
                ZipArchiveEntry? entry = archive.GetEntry(entryName);
                if (entry == null)
                {
                    throw new InvalidOperationException($"Workbook entry not found: {entryName}");
                }

                return ReadXml(entry);
            }

            private static XmlDocument ReadXml(ZipArchiveEntry entry)
            {
                XmlDocument document = new XmlDocument();
                using Stream stream = entry.Open();
                document.Load(stream);
                return document;
            }

            private static XmlNamespaceManager CreateSpreadsheetNamespaceManager(XmlDocument document)
            {
                XmlNamespaceManager namespaceManager = new XmlNamespaceManager(document.NameTable);
                namespaceManager.AddNamespace("x", SPREADSHEET_NAMESPACE);
                return namespaceManager;
            }
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
