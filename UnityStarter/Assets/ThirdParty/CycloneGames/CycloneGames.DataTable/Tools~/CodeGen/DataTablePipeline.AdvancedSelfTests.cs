using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private static void RunAdvancedSelfTests()
            {
                string temporaryRoot = Path.Combine(
                    Path.GetTempPath(),
                    "CycloneGames.DataTable.Pipeline.AdvancedTests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                try
                {
                    RunStreamingFingerprintSelfTests(temporaryRoot);
                    RunBoundedCleanupSelfTest(temporaryRoot);
                    RunProcessIdentitySelfTest();
                }
                finally
                {
                    if (Directory.Exists(temporaryRoot))
                    {
                        DeleteTreeSafe(temporaryRoot, Path.GetDirectoryName(temporaryRoot)!);
                    }
                }
            }

            private static void RunStreamingFingerprintSelfTests(string temporaryRoot)
            {
                string crossChunkPath = Path.Combine(temporaryRoot, "cross-chunk.txt");
                string crossChunkInput = new string('a', 4098) + "\r\nb";
                File.WriteAllText(crossChunkPath, crossChunkInput, new UTF8Encoding(false));
                string expectedCrossChunkHash = ComputeBytesSha256(
                    Encoding.UTF8.GetBytes(new string('a', 4098) + "\nb"));
                if (ComputeNormalizedTextSha256(crossChunkPath, normalizeSelf: false) != expectedCrossChunkHash)
                {
                    throw new InvalidOperationException(
                        "Streaming fingerprint self-test failed CRLF normalization across a read boundary.");
                }

                string selfPath = Path.Combine(temporaryRoot, "self.ini");
                File.WriteAllText(
                    selfPath,
                    "before\r\nsource_fingerprint=" + new string('f', 9000) + "\r\nafter\n",
                    new UTF8Encoding(false));
                string expectedSelfHash = ComputeBytesSha256(
                    Encoding.UTF8.GetBytes("before\nsource_fingerprint=<self>\nafter\n"));
                if (ComputeNormalizedTextSha256(selfPath, normalizeSelf: true) != expectedSelfHash)
                {
                    throw new InvalidOperationException(
                        "Streaming fingerprint self-test failed source_fingerprint normalization.");
                }

                string bomPath = Path.Combine(temporaryRoot, "bom.txt");
                File.WriteAllBytes(bomPath, new byte[] { 0xef, 0xbb, 0xbf, (byte)'x' });
                AssertThrows<InvalidOperationException>(
                    () => ComputeNormalizedTextSha256(bomPath, normalizeSelf: false),
                    "fingerprint UTF-8 BOM");

                string invalidUtf8Path = Path.Combine(temporaryRoot, "invalid-utf8.txt");
                File.WriteAllBytes(invalidUtf8Path, new byte[] { 0xc3, 0x28 });
                AssertThrows<InvalidOperationException>(
                    () => ComputeNormalizedTextSha256(invalidUtf8Path, normalizeSelf: false),
                    "invalid fingerprint UTF-8");

                string standaloneCarriageReturnPath = Path.Combine(temporaryRoot, "standalone-cr.txt");
                File.WriteAllBytes(
                    standaloneCarriageReturnPath,
                    new byte[] { (byte)'a', (byte)'\r', (byte)'b' });
                AssertThrows<InvalidOperationException>(
                    () => ComputeNormalizedTextSha256(
                        standaloneCarriageReturnPath,
                        normalizeSelf: false),
                    "standalone fingerprint carriage return");
            }

            private static void RunBoundedCleanupSelfTest(string temporaryRoot)
            {
                string parent = Path.Combine(temporaryRoot, "cleanup-parent");
                string tree = Path.Combine(parent, "tree");
                Directory.CreateDirectory(tree);
                for (int index = 0; index < 4; index++)
                {
                    File.WriteAllText(Path.Combine(tree, index + ".txt"), "x");
                }

                AssertThrows<InvalidOperationException>(
                    () => DeleteTreeSafe(tree, parent, maximumEntries: 3),
                    "flat cleanup tree over its entry budget");
                if (!Directory.Exists(tree))
                {
                    throw new InvalidOperationException(
                        "Bounded cleanup self-test continued deleting after its entry budget was exhausted.");
                }

                DeleteTreeSafe(tree, parent, maximumEntries: 16);
            }

            private static void RunProcessIdentitySelfTest()
            {
                using Process current = Process.GetCurrentProcess();
                RecordedProcessIdentity identity = CaptureProcessIdentity(current);
                AssertThrows<InvalidOperationException>(
                    () => AssertRecordedProcessStopped(identity, "self-test current process"),
                    "recovery while the recorded process remains alive");

                var reusedPidIdentity = new RecordedProcessIdentity(
                    identity.ProcessId,
                    identity.StartTimeUtcTicks == long.MaxValue
                        ? identity.StartTimeUtcTicks - 1
                        : identity.StartTimeUtcTicks + 1);
                AssertRecordedProcessStopped(reusedPidIdentity, "self-test reused PID");
            }
        }
    }
}
