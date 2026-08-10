using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class StringConstantGenerator
        {
            private const string SelfTestSpreadsheetNamespace =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            private static void RunStreamingAndTransactionSelfTests()
            {
                string root = Path.Combine(
                    Path.GetTempPath(),
                    "cyclonegames-datatable-codegen-selftest-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    TestStreamingProjection(root);
                    TestWorkbookFailures(root);
                    TestOutputChangedOnlyAndEncoding(root);
                    TestUnownedOutputCollision(root);
                    TestOutputRollback(root);
                    TestStaleOutputRollback(root);
                    TestValidationDoesNotCreateOutput(root);
                    TestFailedStagingCleanup(root);
                    TestBoundedWriter();
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            }

            private static void TestStreamingProjection(string root)
            {
                string workbookPath = Path.Combine(root, "projection.xlsx");
                CreateWorkbook(
                    workbookPath,
                    "<sst xmlns=\"" + SelfTestSpreadsheetNamespace + "\">" +
                    "<si><t>Sword</t></si><si><r><t>Shared </t></r><r><t>comment</t></r></si></sst>",
                    CreateWorksheet(
                        "<row r=\"1\">" +
                        InlineCell("A1", "##var") +
                        InlineCell("B1", " name ") +
                        InlineCell("C1", "comment") +
                        InlineCell("D1", "ignored") +
                        "</row>" +
                        "<row r=\"5\"><c r=\"B5\" t=\"s\"><v>0</v></c>" +
                        "<c r=\"C5\" t=\"s\"><v>1</v></c>" +
                        InlineCell("D5", "not projected") +
                        "</row>" +
                        "<row r=\"6\">" + InlineCell("B6", "Shield") + "</row>"));

                var visitor = new CapturingRowVisitor(2);
                var projection = new XlsxWorkbook.ColumnProjection("name", "comment");
                XlsxWorkbook.VisitRows(workbookPath, projection, visitor);
                AssertEqual("2", visitor.Rows.Count.ToString(), "streamed projected row count");
                AssertEqual("Sword", visitor.Rows[0][0], "shared-string projected value");
                AssertEqual("Shared comment", visitor.Rows[0][1], "rich shared-string projected value");
                AssertEqual("Shield", visitor.Rows[1][0], "inline projected value");
                AssertEqual(string.Empty, visitor.Rows[1][1], "missing projected cell");

                ExpectWorkbookFailure(
                    () => XlsxWorkbook.VisitRows(
                        workbookPath,
                        new XlsxWorkbook.ColumnProjection("missing"),
                        new CapturingRowVisitor()),
                    "missing projected column");
            }

            private static void TestWorkbookFailures(string root)
            {
                string duplicateCellPath = Path.Combine(root, "duplicate-cell.xlsx");
                CreateWorkbook(
                    duplicateCellPath,
                    null,
                    CreateWorksheet(
                        "<row r=\"1\">" + InlineCell("A1", "##var") + InlineCell("B1", "name") + "</row>" +
                        "<row r=\"5\">" + InlineCell("B5", "First") + InlineCell("B5", "Second") + "</row>"));
                ExpectWorkbookFailure(
                    () => VisitNameColumn(duplicateCellPath),
                    "duplicate worksheet cell");

                string invalidSharedIndexPath = Path.Combine(root, "invalid-shared-index.xlsx");
                CreateWorkbook(
                    invalidSharedIndexPath,
                    "<sst xmlns=\"" + SelfTestSpreadsheetNamespace + "\"><si><t>Only</t></si></sst>",
                    CreateWorksheet(
                        "<row r=\"1\">" + InlineCell("A1", "##var") + InlineCell("B1", "name") + "</row>" +
                        "<row r=\"5\"><c r=\"B5\" t=\"s\"><v>2</v></c></row>"));
                ExpectWorkbookFailure(
                    () => VisitNameColumn(invalidSharedIndexPath),
                    "out-of-range shared-string index");

                string dtdPath = Path.Combine(root, "dtd.xlsx");
                CreateWorkbook(
                    dtdPath,
                    null,
                    "<!DOCTYPE worksheet [<!ENTITY payload \"unsafe\">]>" +
                    "<worksheet xmlns=\"" + SelfTestSpreadsheetNamespace + "\"><sheetData /></worksheet>");
                ExpectWorkbookFailure(() => VisitNameColumn(dtdPath), "worksheet DTD");

                string depthPath = Path.Combine(root, "xml-depth.xlsx");
                CreateWorkbook(depthPath, null, CreateDeepWorksheet(65));
                ExpectWorkbookFailure(() => VisitNameColumn(depthPath), "worksheet XML depth");

                string archiveTraversalPath = Path.Combine(root, "archive-traversal.xlsx");
                CreateWorkbook(
                    archiveTraversalPath,
                    null,
                    CreateWorksheet("<row r=\"1\">" + InlineCell("A1", "##var") + InlineCell("B1", "name") + "</row>"),
                    extraEntryName: "../escape.xml");
                ExpectWorkbookFailure(() => VisitNameColumn(archiveTraversalPath), "archive traversal entry");

                string rowLimitPath = Path.Combine(root, "row-limit.xlsx");
                CreateWorkbook(
                    rowLimitPath,
                    null,
                    CreateWorksheet(
                        "<row r=\"1\">" + InlineCell("A1", "##var") + InlineCell("B1", "name") + "</row>" +
                        "<row r=\"5\">" + InlineCell("B5", "Value") + "</row>"));
                var limits = new XlsxWorkbook.ReadLimits(
                    maxRows: 1,
                    maxColumns: 16,
                    maxTotalCells: 16,
                    maxSharedStrings: 16,
                    maxSharedStringCharacters: 1024,
                    maxCellCharacters: 128);
                ExpectWorkbookFailure(
                    () => XlsxWorkbook.VisitRows(
                        rowLimitPath,
                        new XlsxWorkbook.ColumnProjection("name"),
                        new CapturingRowVisitor(),
                        limits),
                    "worksheet row limit");

                string sharedStringLimitPath = Path.Combine(root, "shared-string-limit.xlsx");
                CreateWorkbook(
                    sharedStringLimitPath,
                    "<sst xmlns=\"" + SelfTestSpreadsheetNamespace + "\">" +
                    "<si><t>One</t></si><si><t>Two</t></si></sst>",
                    CreateWorksheet("<row r=\"1\">" + InlineCell("A1", "##var") + InlineCell("B1", "name") + "</row>"));
                var sharedLimits = new XlsxWorkbook.ReadLimits(
                    maxRows: 16,
                    maxColumns: 16,
                    maxTotalCells: 32,
                    maxSharedStrings: 1,
                    maxSharedStringCharacters: 1024,
                    maxCellCharacters: 128);
                ExpectWorkbookFailure(
                    () => XlsxWorkbook.VisitRows(
                        sharedStringLimitPath,
                        new XlsxWorkbook.ColumnProjection("name"),
                        new CapturingRowVisitor(),
                        sharedLimits),
                    "shared-string count limit");

                var cellLimits = new XlsxWorkbook.ReadLimits(
                    maxRows: 16,
                    maxColumns: 16,
                    maxTotalCells: 32,
                    maxSharedStrings: 16,
                    maxSharedStringCharacters: 1024,
                    maxCellCharacters: 4);
                ExpectWorkbookFailure(
                    () => XlsxWorkbook.VisitRows(
                        rowLimitPath,
                        new XlsxWorkbook.ColumnProjection("name"),
                        new CapturingRowVisitor(),
                        cellLimits),
                    "cell character limit");
            }

            private static void TestOutputChangedOnlyAndEncoding(string root)
            {
                string outputRoot = Path.Combine(root, "changed-only");
                string outputPath = Path.Combine(outputRoot, "Names.cs");
                using (var session = new OwnedOutputSession(outputRoot, validateOnly: false))
                {
                    session.Stage(outputPath, static writer => writer.Write("line one\nline two\n"));
                    session.Commit(session.BuildPlan());
                }

                byte[] bytes = File.ReadAllBytes(outputPath);
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    throw new InvalidOperationException("Self-test failed: staged UTF-8 output contains a BOM.");
                }

                AssertEqual("line one\nline two\n", Encoding.UTF8.GetString(bytes), "deterministic output line endings");
                DateTime stableTimestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(outputPath, stableTimestamp);
                using (var session = new OwnedOutputSession(outputRoot, validateOnly: false))
                {
                    session.Stage(outputPath, static writer => writer.Write("line one\nline two\n"));
                    session.Commit(session.BuildPlan());
                }

                if (File.GetLastWriteTimeUtc(outputPath) != stableTimestamp)
                {
                    throw new InvalidOperationException(
                        "Self-test failed: changed-only commit rewrote an identical generated output.");
                }
            }

            private static void TestOutputRollback(string root)
            {
                string outputRoot = Path.Combine(root, "rollback");
                string outputPath = Path.Combine(outputRoot, "Names.cs");
                using (var seed = new OwnedOutputSession(outputRoot, validateOnly: false))
                {
                    seed.Stage(outputPath, static writer => writer.Write("original\n"));
                    seed.Commit(seed.BuildPlan());
                }

                bool faultInjected = false;
                using (var session = new OwnedOutputSession(
                           outputRoot,
                           validateOnly: false,
                           (point, _) =>
                           {
                               if (point == CommitFaultPoint.AfterOutputCommitted)
                               {
                                   faultInjected = true;
                                   throw new IOException("Synthetic commit fault.");
                               }
                           }))
                {
                    session.Stage(outputPath, static writer => writer.Write("replacement\n"));
                    ExpectWorkbookFailure(() => session.Commit(session.BuildPlan()), "transaction fault rollback");
                }

                if (!faultInjected)
                {
                    throw new InvalidOperationException("Self-test failed: transaction fault was not injected.");
                }

                AssertEqual("original\n", File.ReadAllText(outputPath), "transaction rollback content");
            }

            private static void TestUnownedOutputCollision(string root)
            {
                string outputRoot = Path.Combine(root, "unowned-collision");
                Directory.CreateDirectory(outputRoot);
                string outputPath = Path.Combine(outputRoot, "Names.cs");
                File.WriteAllText(outputPath, "luban-owned\n", new UTF8Encoding(false));

                using (var session = new OwnedOutputSession(outputRoot, validateOnly: false))
                {
                    session.Stage(outputPath, static writer => writer.Write("constant-projection\n"));
                    ExpectWorkbookFailure(
                        () => session.BuildPlan(),
                        "an existing output that is absent from the prior owned-output manifest");
                }

                AssertEqual(
                    "luban-owned\n",
                    File.ReadAllText(outputPath),
                    "unowned collision preservation");
                if (File.Exists(Path.Combine(outputRoot, OWNED_OUTPUT_MANIFEST_FILE)))
                {
                    throw new InvalidOperationException(
                        "Self-test failed: an unowned collision was adopted into a new manifest.");
                }
            }

            private static void TestValidationDoesNotCreateOutput(string root)
            {
                string outputRoot = Path.Combine(root, "validate-only-output");
                string outputPath = Path.Combine(outputRoot, "Names.cs");
                using (var session = new OwnedOutputSession(outputRoot, validateOnly: true))
                {
                    session.Stage(outputPath, static writer => writer.Write("validated\n"));
                    _ = session.BuildPlan();
                    ExpectWorkbookFailure(
                        () => session.Stage(
                            Path.Combine(outputRoot, "Late.cs"),
                            static writer => writer.Write("late\n")),
                        "staging after output plan freeze");
                }

                if (Directory.Exists(outputRoot))
                {
                    throw new InvalidOperationException(
                        "Self-test failed: validation-only staging created the live output directory.");
                }
            }

            private static void TestStaleOutputRollback(string root)
            {
                string outputRoot = Path.Combine(root, "stale-rollback");
                Directory.CreateDirectory(outputRoot);
                string stalePath = Path.Combine(outputRoot, "Stale.cs");
                File.WriteAllText(stalePath, "stale\n", new UTF8Encoding(false));
                string manifestPath = Path.Combine(outputRoot, OWNED_OUTPUT_MANIFEST_FILE);
                File.WriteAllText(
                    manifestPath,
                    BuildOwnedOutputManifestContent(new[] { "Stale.cs" }),
                    new UTF8Encoding(false));

                using (var session = new OwnedOutputSession(
                           outputRoot,
                           validateOnly: false,
                           (point, _) =>
                           {
                               if (point == CommitFaultPoint.AfterStaleOutputRemoved)
                               {
                                   throw new IOException("Synthetic stale-output commit fault.");
                               }
                           }))
                {
                    ExpectWorkbookFailure(() => session.Commit(session.BuildPlan()), "stale-output rollback");
                }

                AssertEqual("stale\n", File.ReadAllText(stalePath), "stale-output rollback content");
                AssertSequenceEqual(
                    new[] { "Stale.cs" },
                    ReadOwnedOutputManifest(manifestPath),
                    "stale-output rollback manifest");
            }

            private static void TestBoundedWriter()
            {
                using var sink = new StringWriter();
                using var writer = new BoundedTextWriter(sink, 3, 3);
                writer.Write("abc");
                ExpectWorkbookFailure(() => writer.Write('d'), "bounded generated-output writer");
            }

            private static void TestFailedStagingCleanup(string root)
            {
                string outputRoot = Path.Combine(root, "failed-stage-output");
                using (var session = new OwnedOutputSession(outputRoot, validateOnly: false))
                {
                    ExpectWorkbookFailure(
                        () => session.Stage(
                            Path.Combine(outputRoot, "Names.cs"),
                            static writer =>
                            {
                                writer.Write("partial");
                                throw new InvalidOperationException("Synthetic staging fault.");
                            }),
                        "staged writer fault");
                }

                if (Directory.Exists(outputRoot))
                {
                    throw new InvalidOperationException(
                        "Self-test failed: failed staging left an output root created by the session.");
                }
            }

            private static void VisitNameColumn(string path)
            {
                var projection = new XlsxWorkbook.ColumnProjection("name");
                XlsxWorkbook.VisitRows(path, projection, new CapturingRowVisitor(1));
            }

            private static void ExpectWorkbookFailure(Action action, string description)
            {
                try
                {
                    action();
                }
                catch (Exception exception) when (exception is InvalidOperationException ||
                                                   exception is IOException ||
                                                   exception is XmlException)
                {
                    return;
                }

                throw new InvalidOperationException("Self-test failed to reject " + description + ".");
            }

            private static string InlineCell(string reference, string value)
            {
                return "<c r=\"" + reference + "\" t=\"inlineStr\"><is><t>" +
                       SecurityElement.Escape(value) +
                       "</t></is></c>";
            }

            private static string CreateWorksheet(string rows)
            {
                return "<worksheet xmlns=\"" + SelfTestSpreadsheetNamespace +
                       "\"><sheetData>" + rows + "</sheetData></worksheet>";
            }

            private static string CreateDeepWorksheet(int depth)
            {
                var builder = new StringBuilder();
                builder.Append("<worksheet xmlns=\"")
                    .Append(SelfTestSpreadsheetNamespace)
                    .Append("\">");
                for (int i = 0; i < depth; i++)
                {
                    builder.Append("<nested>");
                }

                for (int i = 0; i < depth; i++)
                {
                    builder.Append("</nested>");
                }

                builder.Append("</worksheet>");
                return builder.ToString();
            }

            private static void CreateWorkbook(
                string path,
                string? sharedStrings,
                string worksheet,
                string? extraEntryName = null)
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                WriteArchiveEntry(
                    archive,
                    "xl/workbook.xml",
                    "<workbook xmlns=\"" + SelfTestSpreadsheetNamespace +
                    "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\" /></sheets></workbook>");
                WriteArchiveEntry(
                    archive,
                    "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" " +
                    "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" " +
                    "Target=\"worksheets/sheet1.xml\" /></Relationships>");
                WriteArchiveEntry(archive, "xl/worksheets/sheet1.xml", worksheet);
                if (sharedStrings != null)
                {
                    WriteArchiveEntry(archive, "xl/sharedStrings.xml", sharedStrings);
                }

                if (extraEntryName != null)
                {
                    WriteArchiveEntry(archive, extraEntryName, "invalid");
                }
            }

            private static void WriteArchiveEntry(ZipArchive archive, string name, string content)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using Stream stream = entry.Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: false);
                writer.Write(content);
            }

            private sealed class CapturingRowVisitor : XlsxWorkbook.IRowVisitor
            {
                private readonly int _columnCount;

                public CapturingRowVisitor(int columnCount = 1)
                {
                    _columnCount = columnCount;
                }

                public List<string[]> Rows { get; } = new List<string[]>();

                public void Visit(in XlsxWorkbook.ProjectedRow row)
                {
                    var copy = new string[_columnCount];
                    for (int i = 0; i < copy.Length; i++)
                    {
                        copy[i] = row.GetValue(i);
                    }

                    Rows.Add(copy);
                }
            }
        }
    }
}
