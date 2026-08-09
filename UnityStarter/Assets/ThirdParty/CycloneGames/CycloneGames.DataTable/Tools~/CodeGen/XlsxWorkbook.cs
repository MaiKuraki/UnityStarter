using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static class XlsxWorkbook
        {
            private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
            private const string OfficeRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            private const long MaxWorkbookFileBytes = 64L * 1024 * 1024;
            private const int MaxArchiveEntries = 4096;
            private const long MaxEntryUncompressedBytes = 64L * 1024 * 1024;
            private const long MaxTotalUncompressedBytes = 128L * 1024 * 1024;
            private const int MaxCompressionRatio = 200;
            private const long MaxXmlCharacters = 64L * 1024 * 1024;
            private const int MaxXmlDepth = 64;
            private const int MaxArchivePathCharacters = 1024;

            internal static readonly ReadLimits DefaultLimits = new ReadLimits(
                maxRows: 100000,
                maxColumns: 4096,
                maxTotalCells: 2 * 1024 * 1024,
                maxSharedStrings: 500000,
                maxSharedStringCharacters: 64L * 1024 * 1024,
                maxCellCharacters: 65536);

            internal interface IRowVisitor
            {
                void Visit(in ProjectedRow row);
            }

            internal readonly struct ColumnProjection
            {
                private readonly string[] _columnNames;

                public ColumnProjection(params string[] columnNames)
                {
                    ArgumentNullException.ThrowIfNull(columnNames);
                    if (columnNames.Length == 0)
                    {
                        throw new ArgumentException("At least one worksheet column must be projected.", nameof(columnNames));
                    }

                    _columnNames = new string[columnNames.Length];
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < columnNames.Length; i++)
                    {
                        string name = columnNames[i]?.Trim() ?? string.Empty;
                        if (name.Length == 0)
                        {
                            throw new ArgumentException("Projected worksheet column names cannot be empty.", nameof(columnNames));
                        }

                        if (!seen.Add(name))
                        {
                            throw new ArgumentException("Duplicate projected worksheet column: " + name, nameof(columnNames));
                        }

                        _columnNames[i] = name;
                    }
                }

                public int Count => _columnNames?.Length ?? 0;

                public string this[int index] => _columnNames[index];
            }

            internal readonly struct ProjectedRow
            {
                private readonly string?[] _values;

                internal ProjectedRow(int rowIndex, string?[] values)
                {
                    RowIndex = rowIndex;
                    _values = values;
                }

                public int RowIndex { get; }

                public string GetValue(int projectionIndex)
                {
                    return _values[projectionIndex] ?? string.Empty;
                }

                public bool HasValue(int projectionIndex)
                {
                    return _values[projectionIndex] != null;
                }
            }

            internal readonly struct ReadLimits
            {
                public ReadLimits(
                    int maxRows,
                    int maxColumns,
                    int maxTotalCells,
                    int maxSharedStrings,
                    long maxSharedStringCharacters,
                    int maxCellCharacters)
                {
                    if (maxRows <= 0 || maxColumns <= 0 || maxTotalCells <= 0 ||
                        maxSharedStrings <= 0 || maxSharedStringCharacters <= 0 || maxCellCharacters <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(maxRows), "XLSX read limits must be positive.");
                    }

                    MaxRows = maxRows;
                    MaxColumns = maxColumns;
                    MaxTotalCells = maxTotalCells;
                    MaxSharedStrings = maxSharedStrings;
                    MaxSharedStringCharacters = maxSharedStringCharacters;
                    MaxCellCharacters = maxCellCharacters;
                }

                public int MaxRows { get; }
                public int MaxColumns { get; }
                public int MaxTotalCells { get; }
                public int MaxSharedStrings { get; }
                public long MaxSharedStringCharacters { get; }
                public int MaxCellCharacters { get; }
            }

            public static void VisitRows(string path, in ColumnProjection projection, IRowVisitor visitor)
            {
                VisitRows(path, projection, visitor, DefaultLimits);
            }

            internal static void VisitRows(
                string path,
                in ColumnProjection projection,
                IRowVisitor visitor,
                in ReadLimits limits)
            {
                ArgumentNullException.ThrowIfNull(path);
                ArgumentNullException.ThrowIfNull(visitor);
                if (projection.Count == 0)
                {
                    throw new ArgumentException("At least one worksheet column must be projected.", nameof(projection));
                }

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Workbook not found.", path);
                }

                ValidateFileSize(path, MaxWorkbookFileBytes, "workbook");
                using ZipArchive archive = ZipFile.OpenRead(path);
                ValidateArchive(archive, path);
                using SharedStringStore sharedStrings = ReadSharedStrings(archive, limits);
                string worksheetPath = ReadFirstWorksheetPath(archive);
                ZipArchiveEntry worksheetEntry = GetRequiredEntry(archive, worksheetPath);
                using Stream worksheetStream = worksheetEntry.Open();
                using XmlReader reader = CreateXmlReader(worksheetStream);
                VisitWorksheet(reader, path, projection, visitor, sharedStrings, limits);
            }

            private static void VisitWorksheet(
                XmlReader reader,
                string workbookPath,
                in ColumnProjection projection,
                IRowVisitor visitor,
                SharedStringStore sharedStrings,
                in ReadLimits limits)
            {
                int rowCount = 0;
                int totalCells = 0;
                int previousRowIndex = 0;
                int dataStartRow = int.MaxValue;
                int sheetDataDepth = -1;
                bool sawSheetData = false;
                int[]? projectedColumns = null;
                var projectedValues = new string?[projection.Count];
                var textBuffer = new char[4096];

                while (ReadNext(reader))
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.LocalName == "sheetData" &&
                        reader.NamespaceURI == SpreadsheetNamespace)
                    {
                        if (sawSheetData)
                        {
                            throw new InvalidOperationException("Workbook contains multiple worksheet data elements.");
                        }

                        sawSheetData = true;
                        sheetDataDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                        continue;
                    }

                    if (reader.NodeType == XmlNodeType.EndElement &&
                        reader.Depth == sheetDataDepth &&
                        reader.LocalName == "sheetData" &&
                        reader.NamespaceURI == SpreadsheetNamespace)
                    {
                        sheetDataDepth = -1;
                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element ||
                        reader.LocalName != "row" ||
                        reader.NamespaceURI != SpreadsheetNamespace ||
                        sheetDataDepth < 0 ||
                        reader.Depth != sheetDataDepth + 1)
                    {
                        continue;
                    }

                    rowCount++;
                    if (rowCount > limits.MaxRows)
                    {
                        throw new InvalidOperationException(
                            $"Workbook row count exceeds the limit {limits.MaxRows}: {workbookPath}");
                    }

                    int rowIndex = ParsePositiveIndex(reader.GetAttribute("r"), "row");
                    if (rowIndex <= previousRowIndex)
                    {
                        throw new InvalidOperationException(
                            $"Workbook rows must have strictly increasing indices. Row {rowIndex} follows row {previousRowIndex}: {workbookPath}");
                    }

                    previousRowIndex = rowIndex;
                    Array.Clear(projectedValues, 0, projectedValues.Length);
                    bool headerRow = false;
                    HashSet<string>? headerNames = null;
                    int[]? headerMatches = null;
                    bool dataRow = projectedColumns != null && rowIndex >= dataStartRow;
                    int previousColumn = 0;
                    int rowCellCount = 0;

                    using XmlReader rowReader = reader.ReadSubtree();
                    ReadNext(rowReader);
                    while (ReadNext(rowReader))
                    {
                        if (rowReader.NodeType != XmlNodeType.Element ||
                            rowReader.LocalName != "c" ||
                            rowReader.NamespaceURI != SpreadsheetNamespace ||
                            rowReader.Depth != 1)
                        {
                            continue;
                        }

                        rowCellCount++;
                        totalCells++;
                        if (rowCellCount > limits.MaxColumns || totalCells > limits.MaxTotalCells)
                        {
                            throw new InvalidOperationException(
                                $"Workbook cell budget exceeded at row {rowIndex}: {workbookPath}");
                        }

                        string cellReference = rowReader.GetAttribute("r") ?? string.Empty;
                        (int columnIndex, int referencedRow) = ParseCellReference(cellReference, limits.MaxColumns);
                        if (referencedRow != rowIndex)
                        {
                            throw new InvalidOperationException(
                                $"Cell reference '{cellReference}' does not belong to row {rowIndex}: {workbookPath}");
                        }

                        if (columnIndex <= previousColumn)
                        {
                            throw new InvalidOperationException(
                                $"Cells in row {rowIndex} must have strictly increasing columns: {workbookPath}");
                        }

                        previousColumn = columnIndex;
                        int projectionIndex = projectedColumns == null
                            ? -1
                            : FindProjectionIndex(projectedColumns, columnIndex);
                        bool mustRead = dataRow && projectionIndex >= 0 ||
                                        projectedColumns == null && (columnIndex == 1 || headerRow);
                        string value = mustRead
                            ? ReadCellValue(rowReader, sharedStrings, limits.MaxCellCharacters, textBuffer)
                            : SkipCell(rowReader);

                        if (projectedColumns == null)
                        {
                            if (columnIndex == 1 && string.Equals(value, "##var", StringComparison.Ordinal))
                            {
                                headerRow = true;
                                headerNames = new HashSet<string>(StringComparer.Ordinal);
                                headerMatches = CreateMissingProjectionMap(projection.Count);
                            }

                            if (headerRow && value.Length > 0)
                            {
                                string headerName = value.Trim();
                                if (headerName.Length == 0)
                                {
                                    continue;
                                }

                                if (!headerNames!.Add(headerName))
                                {
                                    throw new InvalidOperationException(
                                        $"Duplicate ##var column at index {columnIndex}: {workbookPath}");
                                }

                                for (int i = 0; i < projection.Count; i++)
                                {
                                    if (string.Equals(projection[i], headerName, StringComparison.Ordinal))
                                    {
                                        headerMatches![i] = columnIndex;
                                    }
                                }
                            }
                        }
                        else if (dataRow && projectionIndex >= 0)
                        {
                            projectedValues[projectionIndex] = value;
                        }
                    }

                    if (projectedColumns == null && headerRow)
                    {
                        for (int i = 0; i < headerMatches!.Length; i++)
                        {
                            if (headerMatches[i] < 0)
                            {
                                throw new InvalidOperationException(
                                    $"Projected column '{projection[i]}' is missing from the ##var header: {workbookPath}");
                            }
                        }

                        projectedColumns = headerMatches;
                        if (rowIndex > int.MaxValue - 4)
                        {
                            throw new InvalidOperationException("Workbook ##var row index is too large: " + rowIndex);
                        }

                        dataStartRow = rowIndex + 4;
                    }
                    else if (dataRow)
                    {
                        var projectedRow = new ProjectedRow(rowIndex, projectedValues);
                        visitor.Visit(in projectedRow);
                    }
                }

                if (projectedColumns == null)
                {
                    throw new InvalidOperationException("Workbook does not contain a ##var header row: " + workbookPath);
                }
            }

            private static int[] CreateMissingProjectionMap(int count)
            {
                var result = new int[count];
                Array.Fill(result, -1);
                return result;
            }

            private static int FindProjectionIndex(int[] projectedColumns, int columnIndex)
            {
                for (int i = 0; i < projectedColumns.Length; i++)
                {
                    if (projectedColumns[i] == columnIndex)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static string SkipCell(XmlReader cellReader)
            {
                using XmlReader ignored = cellReader.ReadSubtree();
                while (ReadNext(ignored))
                {
                }

                return string.Empty;
            }

            private static string ReadCellValue(
                XmlReader cellReader,
                SharedStringStore sharedStrings,
                int maximumCharacters,
                char[] textBuffer)
            {
                string cellType = cellReader.GetAttribute("t") ?? string.Empty;
                string value = string.Empty;
                bool sawValue = false;
                using XmlReader contentReader = cellReader.ReadSubtree();
                ReadNext(contentReader);
                while (ReadNext(contentReader))
                {
                    if (contentReader.NodeType != XmlNodeType.Element ||
                        contentReader.NamespaceURI != SpreadsheetNamespace ||
                        contentReader.Depth != 1)
                    {
                        continue;
                    }

                    if (contentReader.LocalName == "v")
                    {
                        if (sawValue)
                        {
                            throw new InvalidOperationException("Workbook cell contains multiple values.");
                        }

                        sawValue = true;
                        value = ReadElementText(contentReader, maximumCharacters, textBuffer, "cell value");
                    }
                    else if (contentReader.LocalName == "is")
                    {
                        if (sawValue)
                        {
                            throw new InvalidOperationException("Workbook cell contains multiple values.");
                        }

                        sawValue = true;
                        value = ReadRichText(contentReader, maximumCharacters, textBuffer, "inline string");
                    }
                }

                if (string.Equals(cellType, "s", StringComparison.Ordinal))
                {
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int sharedIndex))
                    {
                        throw new InvalidOperationException("Workbook contains an invalid shared-string index.");
                    }

                    return sharedStrings.Get(sharedIndex);
                }

                return ValidateCellText(value, maximumCharacters, "cell value");
            }

            private static SharedStringStore ReadSharedStrings(ZipArchive archive, in ReadLimits limits)
            {
                var store = new SharedStringStore(limits.MaxSharedStrings, limits.MaxSharedStringCharacters);
                ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
                if (entry == null)
                {
                    return store;
                }

                try
                {
                    using Stream stream = entry.Open();
                    using XmlReader reader = CreateXmlReader(stream);
                    var textBuffer = new char[4096];
                    while (ReadNext(reader))
                    {
                        if (reader.NodeType == XmlNodeType.Element &&
                            reader.LocalName == "si" &&
                            reader.NamespaceURI == SpreadsheetNamespace &&
                            reader.Depth == 1)
                        {
                            string value = ReadRichText(reader, limits.MaxCellCharacters, textBuffer, "shared string");
                            store.Add(value);
                        }
                    }

                    return store;
                }
                catch
                {
                    store.Dispose();
                    throw;
                }
            }

            private static string ReadRichText(
                XmlReader containerReader,
                int maximumCharacters,
                char[] textBuffer,
                string description)
            {
                var builder = new StringBuilder(Math.Min(256, maximumCharacters));
                using XmlReader reader = containerReader.ReadSubtree();
                ReadNext(reader);
                while (ReadNext(reader))
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.LocalName == "t" &&
                        reader.NamespaceURI == SpreadsheetNamespace)
                    {
                        AppendElementText(reader, builder, maximumCharacters, textBuffer, description);
                    }
                }

                return builder.ToString();
            }

            private static string ReadElementText(
                XmlReader reader,
                int maximumCharacters,
                char[] textBuffer,
                string description)
            {
                var builder = new StringBuilder(Math.Min(64, maximumCharacters));
                AppendElementText(reader, builder, maximumCharacters, textBuffer, description);
                return builder.ToString();
            }

            private static void AppendElementText(
                XmlReader reader,
                StringBuilder builder,
                int maximumCharacters,
                char[] textBuffer,
                string description)
            {
                if (reader.IsEmptyElement)
                {
                    return;
                }

                int elementDepth = reader.Depth;
                while (ReadNext(reader))
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == elementDepth)
                    {
                        return;
                    }

                    if (reader.Depth != elementDepth + 1 ||
                        (reader.NodeType != XmlNodeType.Text &&
                         reader.NodeType != XmlNodeType.CDATA &&
                         reader.NodeType != XmlNodeType.Whitespace &&
                         reader.NodeType != XmlNodeType.SignificantWhitespace))
                    {
                        continue;
                    }

                    int read;
                    do
                    {
                        read = reader.ReadValueChunk(textBuffer, 0, textBuffer.Length);
                        if ((long)builder.Length + read > maximumCharacters)
                        {
                            throw new InvalidOperationException(
                                $"Workbook {description} exceeds the {maximumCharacters}-character limit.");
                        }

                        builder.Append(textBuffer, 0, read);
                    }
                    while (read > 0);
                }

                throw new InvalidOperationException("Unexpected end of XML while reading workbook " + description + ".");
            }

            private static string ValidateCellText(string value, int maximumCharacters, string description)
            {
                if (value.Length > maximumCharacters)
                {
                    throw new InvalidOperationException(
                        $"Workbook {description} exceeds the {maximumCharacters}-character limit.");
                }

                return value;
            }

            private static string ReadFirstWorksheetPath(ZipArchive archive)
            {
                ZipArchiveEntry workbookEntry = GetRequiredEntry(archive, "xl/workbook.xml");
                string? relationshipId = null;
                using (Stream stream = workbookEntry.Open())
                using (XmlReader reader = CreateXmlReader(stream))
                {
                    while (ReadNext(reader))
                    {
                        if (reader.NodeType == XmlNodeType.Element &&
                            reader.LocalName == "sheet" &&
                            reader.NamespaceURI == SpreadsheetNamespace &&
                            reader.Depth == 2)
                        {
                            relationshipId = reader.GetAttribute("id", OfficeRelationshipsNamespace);
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(relationshipId))
                {
                    throw new InvalidOperationException("Workbook does not contain a worksheet relationship.");
                }

                ZipArchiveEntry relationshipsEntry = GetRequiredEntry(archive, "xl/_rels/workbook.xml.rels");
                using Stream relationshipsStream = relationshipsEntry.Open();
                using XmlReader relationshipsReader = CreateXmlReader(relationshipsStream);
                while (ReadNext(relationshipsReader))
                {
                    if (relationshipsReader.NodeType != XmlNodeType.Element ||
                        relationshipsReader.LocalName != "Relationship" ||
                        relationshipsReader.NamespaceURI != RelationshipsNamespace ||
                        relationshipsReader.Depth != 1 ||
                        !string.Equals(relationshipsReader.GetAttribute("Id"), relationshipId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.Equals(relationshipsReader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("External worksheet relationships are not supported.");
                    }

                    string relationshipType = relationshipsReader.GetAttribute("Type") ?? string.Empty;
                    if (!relationshipType.EndsWith("/worksheet", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Workbook sheet relationship does not target a worksheet.");
                    }

                    string target = relationshipsReader.GetAttribute("Target") ?? string.Empty;
                    return ResolveRelationshipTarget(target);
                }

                throw new InvalidOperationException("Workbook worksheet relationship was not found: " + relationshipId);
            }

            private static int ParsePositiveIndex(string? value, string description)
            {
                if (string.IsNullOrEmpty(value) ||
                    value.Length > 10 ||
                    !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) ||
                    result <= 0)
                {
                    throw new InvalidOperationException($"Workbook contains an invalid {description} index.");
                }

                return result;
            }

            private static (int Column, int Row) ParseCellReference(string value, int maximumColumn)
            {
                if (string.IsNullOrEmpty(value) || value.Length > 32)
                {
                    throw new InvalidOperationException("Workbook cell reference is empty.");
                }

                int index = 0;
                int column = 0;
                while (index < value.Length)
                {
                    char character = value[index];
                    if (character < 'A' || character > 'Z')
                    {
                        break;
                    }

                    column = checked(column * 26 + character - 'A' + 1);
                    if (column > maximumColumn)
                    {
                        throw new InvalidOperationException(
                            $"Workbook column in cell '{value}' exceeds the limit {maximumColumn}.");
                    }

                    index++;
                }

                if (index == 0 || index == value.Length)
                {
                    throw new InvalidOperationException("Workbook contains an invalid cell reference: " + value);
                }

                int row = 0;
                for (; index < value.Length; index++)
                {
                    char character = value[index];
                    if (character < '0' || character > '9')
                    {
                        throw new InvalidOperationException("Workbook contains an invalid cell reference: " + value);
                    }

                    int digit = character - '0';
                    if (row > (int.MaxValue - digit) / 10)
                    {
                        throw new InvalidOperationException("Workbook cell row index is too large: " + value);
                    }

                    row = row * 10 + digit;
                }

                if (row <= 0)
                {
                    throw new InvalidOperationException("Workbook contains an invalid cell reference: " + value);
                }

                return (column, row);
            }

            private static XmlReader CreateXmlReader(Stream stream)
            {
                return XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaxXmlCharacters,
                    MaxCharactersFromEntities = 0,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    CloseInput = false,
                });
            }

            private static bool ReadNext(XmlReader reader)
            {
                bool hasNode = reader.Read();
                if (hasNode)
                {
                    ValidateXmlDepth(reader);
                }

                return hasNode;
            }

            private static void ValidateXmlDepth(XmlReader reader)
            {
                if (reader.Depth > MaxXmlDepth)
                {
                    throw new InvalidOperationException(
                        $"Workbook XML depth exceeds the limit {MaxXmlDepth}.");
                }
            }

            private static void ValidateArchive(ZipArchive archive, string workbookPath)
            {
                if (archive.Entries.Count > MaxArchiveEntries)
                {
                    throw new InvalidOperationException(
                        $"Workbook entry count {archive.Entries.Count} exceeds the limit {MaxArchiveEntries}: {workbookPath}");
                }

                long totalUncompressedBytes = 0;
                var normalizedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string normalizedName = NormalizeArchiveEntryPath(entry.FullName, "archive entry");
                    if (!normalizedNames.Add(normalizedName))
                    {
                        throw new InvalidOperationException("Workbook contains duplicate or case-colliding entries: " + entry.FullName);
                    }

                    if (entry.Length > MaxEntryUncompressedBytes)
                    {
                        throw new InvalidOperationException(
                            $"Workbook entry exceeds the {MaxEntryUncompressedBytes}-byte limit: {entry.FullName}");
                    }

                    totalUncompressedBytes = checked(totalUncompressedBytes + entry.Length);
                    if (totalUncompressedBytes > MaxTotalUncompressedBytes)
                    {
                        throw new InvalidOperationException(
                            $"Workbook uncompressed content exceeds the {MaxTotalUncompressedBytes}-byte limit: {workbookPath}");
                    }

                    if (entry.Length > 1024L * 1024 &&
                        (entry.CompressedLength == 0 || entry.Length > entry.CompressedLength * MaxCompressionRatio))
                    {
                        throw new InvalidOperationException(
                            $"Workbook entry compression ratio exceeds {MaxCompressionRatio}:1: {entry.FullName}");
                    }
                }
            }

            private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string entryName)
            {
                ZipArchiveEntry? entry = archive.GetEntry(entryName);
                return entry ?? throw new InvalidOperationException("Workbook is missing required entry: " + entryName);
            }

            private static string NormalizeArchiveEntryPath(string value, string description)
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    value.Length > MaxArchivePathCharacters ||
                    value[0] == '/' ||
                    value[0] == '\\' ||
                    value.IndexOf('\\') >= 0)
                {
                    throw new InvalidOperationException("Workbook contains an invalid " + description + " path: " + value);
                }

                var segments = new List<string>();
                foreach (string segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (segment == "." || segment == "..")
                    {
                        throw new InvalidOperationException(
                            "Workbook " + description + " contains a traversal segment: " + value);
                    }

                    if (segment.IndexOf(':') >= 0 || segment.IndexOf('\0') >= 0)
                    {
                        throw new InvalidOperationException("Workbook contains an invalid " + description + " path: " + value);
                    }

                    segments.Add(segment);
                }

                if (segments.Count == 0)
                {
                    throw new InvalidOperationException("Workbook contains an empty " + description + " path: " + value);
                }

                return string.Join('/', segments);
            }

            private static string ResolveRelationshipTarget(string target)
            {
                if (string.IsNullOrWhiteSpace(target) ||
                    target.Length > MaxArchivePathCharacters ||
                    target.IndexOf('\\') >= 0 ||
                    target.IndexOf(':') >= 0 ||
                    target.IndexOf('?') >= 0 ||
                    target.IndexOf('#') >= 0 ||
                    target.IndexOf('\0') >= 0)
                {
                    throw new InvalidOperationException("Workbook contains an invalid worksheet relationship path: " + target);
                }

                bool packageAbsolute = target[0] == '/';
                var segments = packageAbsolute
                    ? new List<string>()
                    : new List<string> { "xl" };
                foreach (string segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (segment == ".")
                    {
                        continue;
                    }

                    if (segment == "..")
                    {
                        if (segments.Count == 0)
                        {
                            throw new InvalidOperationException(
                                "Workbook worksheet relationship escapes the archive root: " + target);
                        }

                        segments.RemoveAt(segments.Count - 1);
                        continue;
                    }

                    segments.Add(segment);
                }

                if (segments.Count == 0)
                {
                    throw new InvalidOperationException("Workbook worksheet relationship path is empty: " + target);
                }

                return NormalizeArchiveEntryPath(string.Join('/', segments), "worksheet relationship");
            }

            private sealed class SharedStringStore : IDisposable
            {
                private const int CacheCapacity = 256;
                private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
                private readonly FileStream _stream;
                private readonly BinaryWriter _writer;
                private readonly BinaryReader _reader;
                private readonly List<long> _offsets;
                private readonly Dictionary<int, int> _cacheSlots =
                    new Dictionary<int, int>(CacheCapacity);
                private readonly int[] _cacheKeys = new int[CacheCapacity];
                private readonly string?[] _cacheValues = new string?[CacheCapacity];
                private readonly int _maximumCount;
                private readonly long _maximumCharacters;
                private long _totalCharacters;
                private int _cachedCount;
                private int _nextCacheSlot;
                private bool _disposed;

                public SharedStringStore(int maximumCount, long maximumCharacters)
                {
                    _maximumCount = maximumCount;
                    _maximumCharacters = maximumCharacters;
                    _offsets = new List<long>(Math.Min(maximumCount, 4096));
                    string path = Path.Combine(
                        Path.GetTempPath(),
                        "cyclonegames-datatable-shared-" + Guid.NewGuid().ToString("N") + ".tmp");
                    _stream = new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        4096,
                        FileOptions.DeleteOnClose | FileOptions.RandomAccess);
                    _writer = new BinaryWriter(_stream, Utf8, true);
                    _reader = new BinaryReader(_stream, Utf8, true);
                }

                public void Add(string value)
                {
                    if (_offsets.Count >= _maximumCount)
                    {
                        throw new InvalidOperationException(
                            $"Workbook shared-string count exceeds the limit {_maximumCount}.");
                    }

                    long nextCharacters = checked(_totalCharacters + value.Length);
                    if (nextCharacters > _maximumCharacters)
                    {
                        throw new InvalidOperationException(
                            $"Workbook shared strings exceed the {_maximumCharacters}-character limit.");
                    }

                    _offsets.Add(_stream.Position);
                    _writer.Write(value);
                    _totalCharacters = nextCharacters;
                }

                public string Get(int index)
                {
                    if ((uint)index >= (uint)_offsets.Count)
                    {
                        throw new InvalidOperationException("Shared-string index is outside the workbook table: " + index);
                    }

                    if (_cacheSlots.TryGetValue(index, out int cachedSlot))
                    {
                        return _cacheValues[cachedSlot]!;
                    }

                    _writer.Flush();
                    _stream.Position = _offsets[index];
                    string value = _reader.ReadString();

                    int targetSlot;
                    if (_cachedCount < CacheCapacity)
                    {
                        targetSlot = _cachedCount++;
                    }
                    else
                    {
                        targetSlot = _nextCacheSlot;
                        _cacheSlots.Remove(_cacheKeys[targetSlot]);
                    }

                    _cacheKeys[targetSlot] = index;
                    _cacheValues[targetSlot] = value;
                    _cacheSlots.Add(index, targetSlot);
                    _nextCacheSlot = (targetSlot + 1) & (CacheCapacity - 1);

                    return value;
                }

                public void Dispose()
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _reader.Dispose();
                    _writer.Dispose();
                    _stream.Dispose();
                }
            }
        }
    }
}
