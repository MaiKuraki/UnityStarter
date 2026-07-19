using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static class XlsxWorkbook
        {
            private const string SPREADSHEET_NAMESPACE = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            private const string RELATIONSHIPS_NAMESPACE = "http://schemas.openxmlformats.org/package/2006/relationships";
            private const string OFFICE_RELATIONSHIPS_NAMESPACE = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            private const long MAX_WORKBOOK_FILE_BYTES = 64L * 1024 * 1024;
            private const int MAX_ARCHIVE_ENTRIES = 4096;
            private const long MAX_ENTRY_UNCOMPRESSED_BYTES = 64L * 1024 * 1024;
            private const long MAX_TOTAL_UNCOMPRESSED_BYTES = 128L * 1024 * 1024;
            private const int MAX_COMPRESSION_RATIO = 200;
            private const long MAX_XML_CHARACTERS = 64L * 1024 * 1024;
            private const int MAX_ROWS = 100000;
            private const int MAX_COLUMNS = 4096;
            private const int MAX_TOTAL_CELLS = 2 * 1024 * 1024;
            private const int MAX_SHARED_STRINGS = 500000;
            private const long MAX_SHARED_STRING_CHARACTERS = 64L * 1024 * 1024;
            private const int MAX_CELL_CHARACTERS = 65536;

            public static Dictionary<string, string>[] ReadRows(string path)
            {
                return ReadRows(path, out _);
            }

            public static Dictionary<string, string>[] ReadRows(
                string path,
                out HashSet<string> columnNames)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Workbook not found.", path);
                }

                ValidateFileSize(path, MAX_WORKBOOK_FILE_BYTES, "workbook");

                using ZipArchive archive = ZipFile.OpenRead(path);
                ValidateArchive(archive, path);
                string[] sharedStrings = ReadSharedStrings(archive);
                string worksheetPath = ReadFirstWorksheetPath(archive);
                XmlDocument worksheet = ReadXml(archive, worksheetPath);
                XmlNamespaceManager namespaceManager = CreateSpreadsheetNamespaceManager(worksheet);

                SortedDictionary<int, Dictionary<int, string>> rows = new SortedDictionary<int, Dictionary<int, string>>();
                XmlNodeList? rowNodes = worksheet.SelectNodes("/x:worksheet/x:sheetData/x:row", namespaceManager);
                int totalCells = 0;
                if (rowNodes != null)
                {
                    if (rowNodes.Count > MAX_ROWS)
                    {
                        throw new InvalidOperationException(
                            $"Workbook row count {rowNodes.Count} exceeds the limit {MAX_ROWS}: {path}");
                    }

                    foreach (XmlElement rowNode in rowNodes.OfType<XmlElement>())
                    {
                        if (!int.TryParse(
                                rowNode.GetAttribute("r"),
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out int rowIndex) ||
                            rowIndex <= 0)
                        {
                            throw new InvalidOperationException("Workbook contains an invalid row index: " + rowNode.GetAttribute("r"));
                        }

                        Dictionary<int, string> values = new Dictionary<int, string>();
                        XmlNodeList? cells = rowNode.SelectNodes("x:c", namespaceManager);
                        if (cells != null)
                        {
                            if (cells.Count > MAX_COLUMNS)
                            {
                                throw new InvalidOperationException(
                                    $"Workbook row {rowIndex} contains {cells.Count} cells; limit is {MAX_COLUMNS}.");
                            }

                            totalCells = checked(totalCells + cells.Count);
                            if (totalCells > MAX_TOTAL_CELLS)
                            {
                                throw new InvalidOperationException(
                                    $"Workbook cell count exceeds the limit {MAX_TOTAL_CELLS}: {path}");
                            }

                            foreach (XmlElement cell in cells.OfType<XmlElement>())
                            {
                                int columnIndex = ColumnNameToIndex(cell.GetAttribute("r"));
                                if (columnIndex <= 0 || columnIndex > MAX_COLUMNS)
                                {
                                    throw new InvalidOperationException(
                                        $"Workbook cell column exceeds the limit {MAX_COLUMNS}: {cell.GetAttribute("r")}");
                                }

                                if (!values.TryAdd(columnIndex, ReadCellValue(cell, sharedStrings, namespaceManager)))
                                {
                                    throw new InvalidOperationException("Workbook contains a duplicate cell reference: " + cell.GetAttribute("r"));
                                }
                            }
                        }

                        if (!rows.TryAdd(rowIndex, values))
                        {
                            throw new InvalidOperationException("Workbook contains a duplicate row index: " + rowIndex);
                        }
                    }
                }

                int varRowIndex = FindVarRowIndex(rows, path);
                Dictionary<string, int> columnByName = BuildColumnMap(rows[varRowIndex]);
                columnNames = new HashSet<string>(columnByName.Keys, StringComparer.Ordinal);
                var columnNameByIndex = new Dictionary<int, string>(columnByName.Count);
                foreach (KeyValuePair<string, int> column in columnByName)
                {
                    columnNameByIndex.Add(column.Value, column.Key);
                }
                List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();

                foreach (KeyValuePair<int, Dictionary<int, string>> rowPair in rows)
                {
                    if (rowPair.Key < varRowIndex + 4)
                    {
                        continue;
                    }

                    Dictionary<string, string> row = MaterializeDeclaredRow(
                        rowPair.Value,
                        columnNameByIndex);

                    if (row.Values.Any(static value => !string.IsNullOrWhiteSpace(value)))
                    {
                        result.Add(row);
                    }
                }

                return result.ToArray();
            }

            public static Dictionary<string, string> MaterializeDeclaredRow(
                IReadOnlyDictionary<int, string> cellValues,
                IReadOnlyDictionary<int, string> columnNameByIndex)
            {
                var row = new Dictionary<string, string>(
                    Math.Min(cellValues.Count, columnNameByIndex.Count),
                    StringComparer.Ordinal);
                foreach (KeyValuePair<int, string> cell in cellValues)
                {
                    if (columnNameByIndex.TryGetValue(cell.Key, out string? columnName))
                    {
                        row.Add(columnName, cell.Value);
                    }
                }

                return row;
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

                if (nodes.Count > MAX_SHARED_STRINGS)
                {
                    throw new InvalidOperationException(
                        $"Workbook shared string count {nodes.Count} exceeds the limit {MAX_SHARED_STRINGS}.");
                }

                List<string> strings = new List<string>(nodes.Count);
                long totalCharacters = 0;
                foreach (XmlNode node in nodes)
                {
                    string value = ValidateCellText(node.InnerText, "shared string");
                    totalCharacters = checked(totalCharacters + value.Length);
                    if (totalCharacters > MAX_SHARED_STRING_CHARACTERS)
                    {
                        throw new InvalidOperationException(
                            $"Workbook shared strings exceed the {MAX_SHARED_STRING_CHARACTERS}-character budget.");
                    }

                    strings.Add(value);
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

                        if (string.Equals(relationship.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("External worksheet relationships are not supported.");
                        }

                        string target = relationship.GetAttribute("Target").TrimStart('/');
                        string worksheetPath = target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                            ? target
                            : "xl/" + target;
                        return NormalizeArchiveEntryPath(worksheetPath, "worksheet relationship");
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
                    return ValidateCellText(inlineNode?.InnerText ?? string.Empty, "inline cell");
                }

                XmlNode? valueNode = cell.SelectSingleNode("x:v", namespaceManager);
                if (valueNode == null)
                {
                    return string.Empty;
                }

                string raw = valueNode.InnerText;
                if (string.Equals(type, "s", StringComparison.Ordinal))
                {
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int sharedStringIndex) ||
                        sharedStringIndex < 0 ||
                        sharedStringIndex >= sharedStrings.Length)
                    {
                        throw new InvalidOperationException("Workbook contains an invalid shared string index: " + raw);
                    }

                    return sharedStrings[sharedStringIndex];
                }

                return ValidateCellText(raw, "cell");
            }

            private static string ValidateCellText(string value, string description)
            {
                if (value.Length > MAX_CELL_CHARACTERS)
                {
                    throw new InvalidOperationException(
                        $"Workbook {description} exceeds the {MAX_CELL_CHARACTERS}-character limit.");
                }

                return value;
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
                        string name = column.Value.Trim();
                        if (!columnByName.TryAdd(name, column.Key))
                        {
                            throw new InvalidOperationException("Workbook contains a duplicate ##var column: " + name);
                        }
                    }
                }

                return columnByName;
            }

            private static int ColumnNameToIndex(string cellReference)
            {
                int value = 0;
                int letterCount = 0;
                for (int i = 0; i < cellReference.Length; i++)
                {
                    char character = cellReference[i];
                    if (character < 'A' || character > 'Z')
                    {
                        break;
                    }

                    letterCount++;
                    value = checked(value * 26 + character - 'A' + 1);
                }

                return letterCount == 0 ? -1 : value;
            }

            private static XmlDocument ReadXml(ZipArchive archive, string entryName)
            {
                string normalizedEntryName = NormalizeArchiveEntryPath(entryName, "XML entry");
                ZipArchiveEntry? entry = archive.GetEntry(normalizedEntryName);
                if (entry == null)
                {
                    throw new InvalidOperationException($"Workbook entry not found: {normalizedEntryName}");
                }

                return ReadXml(entry);
            }

            private static void ValidateArchive(ZipArchive archive, string workbookPath)
            {
                if (archive.Entries.Count > MAX_ARCHIVE_ENTRIES)
                {
                    throw new InvalidOperationException(
                        $"Workbook archive entry count {archive.Entries.Count} exceeds the limit {MAX_ARCHIVE_ENTRIES}: {workbookPath}");
                }

                long totalLength = 0;
                var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = archive.Entries[i];
                    string normalizedName = NormalizeArchiveEntryPath(entry.FullName, "archive entry");
                    if (!entryNames.Add(normalizedName))
                    {
                        throw new InvalidOperationException(
                            "Workbook contains duplicate or case-colliding archive entries: " + normalizedName);
                    }

                    if (entry.Length > MAX_ENTRY_UNCOMPRESSED_BYTES)
                    {
                        throw new InvalidOperationException(
                            $"Workbook archive entry '{entry.FullName}' exceeds the {MAX_ENTRY_UNCOMPRESSED_BYTES}-byte limit.");
                    }

                    totalLength = checked(totalLength + entry.Length);
                    if (totalLength > MAX_TOTAL_UNCOMPRESSED_BYTES)
                    {
                        throw new InvalidOperationException(
                            $"Workbook uncompressed content exceeds the {MAX_TOTAL_UNCOMPRESSED_BYTES}-byte budget.");
                    }

                    if (entry.Length > 1024 * 1024 &&
                        (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > MAX_COMPRESSION_RATIO))
                    {
                        throw new InvalidOperationException(
                            $"Workbook archive entry '{entry.FullName}' exceeds the compression-ratio limit {MAX_COMPRESSION_RATIO}:1.");
                    }
                }
            }

            private static string NormalizeArchiveEntryPath(string value, string description)
            {
                if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
                {
                    throw new InvalidOperationException("Workbook contains an empty or invalid " + description + " path.");
                }

                string normalized = value.Replace('\\', '/').TrimStart('/');
                string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    throw new InvalidOperationException("Workbook contains an empty " + description + " path.");
                }

                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i] == "." || segments[i] == ".." || segments[i].IndexOf(':') >= 0)
                    {
                        throw new InvalidOperationException(
                            $"Workbook {description} path contains traversal or rooted syntax: {value}");
                    }
                }

                return string.Join("/", segments);
            }

            private static XmlDocument ReadXml(ZipArchiveEntry entry)
            {
                if (entry.Length > MAX_ENTRY_UNCOMPRESSED_BYTES)
                {
                    throw new InvalidOperationException(
                        $"Workbook XML entry '{entry.FullName}' exceeds the {MAX_ENTRY_UNCOMPRESSED_BYTES}-byte limit.");
                }

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MAX_XML_CHARACTERS,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                };
                XmlDocument document = new XmlDocument
                {
                    XmlResolver = null,
                };
                using Stream stream = entry.Open();
                using XmlReader reader = XmlReader.Create(stream, settings);
                document.Load(reader);
                return document;
            }

            private static XmlNamespaceManager CreateSpreadsheetNamespaceManager(XmlDocument document)
            {
                XmlNamespaceManager namespaceManager = new XmlNamespaceManager(document.NameTable);
                namespaceManager.AddNamespace("x", SPREADSHEET_NAMESPACE);
                return namespaceManager;
            }
        }
    }
}
