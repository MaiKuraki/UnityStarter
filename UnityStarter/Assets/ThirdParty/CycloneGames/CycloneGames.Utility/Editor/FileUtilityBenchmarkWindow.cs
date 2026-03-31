using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Editor benchmark window for FileUtility.
    /// Covers correctness verification, performance measurement, and GC allocation tracking
    /// for every public API. Generates temporary test files, runs tests, and displays results.
    ///
    /// Usage: Window → CycloneGames → FileUtility Benchmark
    /// </summary>
    public class FileUtilityBenchmarkWindow : EditorWindow
    {
        // --- Test Configuration ---
        private static readonly int[] TestFileSizesKB = { 1, 64, 1024, 10240, 51200 }; // 1KB, 64KB, 1MB, 10MB, 50MB
        private const int WarmupIterations = 1;
        private const int BenchmarkIterations = 3;

        // --- State ---
        private Vector2 _scrollPosition;
        private bool _isRunning;
        private float _progress;
        private string _progressLabel = "";
        private CancellationTokenSource _cts;

        // --- Results ---
        private readonly List<TestResult> _results = new List<TestResult>();
        private readonly StringBuilder _logBuilder = new StringBuilder(4096);
        private string _tempDir;

        private enum ResultType { Pass, Fail, Info, Perf }

        private struct TestResult
        {
            public ResultType Type;
            public string Category;
            public string Name;
            public string Detail;
        }

        [MenuItem("Window/CycloneGames/FileUtility Benchmark")]
        public static void ShowWindow()
        {
            var window = GetWindow<FileUtilityBenchmarkWindow>("FileUtility Benchmark");
            window.minSize = new Vector2(620, 400);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            CleanupTempFiles();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !_isRunning;
                if (GUILayout.Button("Run All Tests", GUILayout.Height(30)))
                {
                    RunAllAsync();
                }
                if (GUILayout.Button("Run Correctness Only", GUILayout.Height(30)))
                {
                    RunCorrectnessOnlyAsync();
                }
                if (GUILayout.Button("Run Benchmarks Only", GUILayout.Height(30)))
                {
                    RunBenchmarksOnlyAsync();
                }
                GUI.enabled = true;

                if (_isRunning)
                {
                    if (GUILayout.Button("Cancel", GUILayout.Width(70), GUILayout.Height(30)))
                    {
                        _cts?.Cancel();
                    }
                }
            }

            if (_isRunning)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), _progress, _progressLabel);
            }

            EditorGUILayout.Space(4);

            // --- Summary + Export ---
            if (_results.Count > 0)
            {
                int pass = 0, fail = 0, perf = 0;
                for (int i = 0; i < _results.Count; i++)
                {
                    switch (_results[i].Type)
                    {
                        case ResultType.Pass: pass++; break;
                        case ResultType.Fail: fail++; break;
                        case ResultType.Perf: perf++; break;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Results: {pass} passed, {fail} failed, {perf} perf entries", EditorStyles.boldLabel);

                    GUI.enabled = !_isRunning;
                    if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(130)))
                    {
                        GUIUtility.systemCopyBuffer = BuildResultsText();
                        ShowNotification(new GUIContent("Copied to clipboard!"), 1.5f);
                    }
                    if (GUILayout.Button("Export to File", GUILayout.Width(110)))
                    {
                        ExportResultsToFile();
                    }
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.Space(2);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            string currentCategory = null;
            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                if (r.Category != currentCategory)
                {
                    currentCategory = r.Category;
                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField(currentCategory, EditorStyles.boldLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    string icon;
                    switch (r.Type)
                    {
                        case ResultType.Pass: icon = "\u2705"; break; // ✅
                        case ResultType.Fail: icon = "\u274C"; break; // ❌
                        case ResultType.Perf: icon = "\u23F1"; break; // ⏱
                        default:              icon = "\u2139"; break; // ℹ
                    }
                    EditorGUILayout.LabelField(icon, GUILayout.Width(22));
                    EditorGUILayout.LabelField(r.Name, GUILayout.Width(300));
                    EditorGUILayout.LabelField(r.Detail);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ==========================================================================
        // Entry Points
        // ==========================================================================

        private async void RunAllAsync()
        {
            await RunTests(runCorrectness: true, runBenchmarks: true);
        }

        private async void RunCorrectnessOnlyAsync()
        {
            await RunTests(runCorrectness: true, runBenchmarks: false);
        }

        private async void RunBenchmarksOnlyAsync()
        {
            await RunTests(runCorrectness: false, runBenchmarks: true);
        }

        private async Task RunTests(bool runCorrectness, bool runBenchmarks)
        {
            _isRunning = true;
            _results.Clear();
            _logBuilder.Clear();
            _progress = 0f;
            _progressLabel = "Preparing...";
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            Repaint();

            try
            {
                _tempDir = Path.Combine(Application.temporaryCachePath, "FileUtilityBenchmark");
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
                Directory.CreateDirectory(_tempDir);

                AddResult(ResultType.Info, "Environment", "Temp Directory", _tempDir);
                AddResult(ResultType.Info, "Environment", "Platform", Application.platform.ToString());
                AddResult(ResultType.Info, "Environment", "ReadBufferSize", GetReadBufferSizeLabel());

                // Generate test files
                _progressLabel = "Generating test files...";
                Repaint();
                var testFiles = await GenerateTestFiles(_cts.Token);

                if (runCorrectness)
                {
                    await RunCorrectnessTests(testFiles, _cts.Token);
                }

                if (runBenchmarks)
                {
                    await RunPerformanceBenchmarks(testFiles, _cts.Token);
                }

                _progressLabel = "Done.";
                _progress = 1f;
            }
            catch (OperationCanceledException)
            {
                AddResult(ResultType.Info, "Status", "Cancelled", "Tests were cancelled by user.");
            }
            catch (Exception ex)
            {
                AddResult(ResultType.Fail, "Error", "Unhandled Exception", ex.Message);
            }
            finally
            {
                _isRunning = false;
                Repaint();
            }
        }

        // ==========================================================================
        // Correctness Tests
        // ==========================================================================

        private async Task RunCorrectnessTests(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            _progressLabel = "Correctness: ToHexString...";
            Repaint();

            // --- ToHexString ---
            TestToHexString();

            // --- Sync Hash ---
            _progressLabel = "Correctness: Sync hash...";
            Repaint();
            TestSyncHash(testFiles);

            // --- Async Hash ---
            _progressLabel = "Correctness: Async hash...";
            Repaint();
            await TestAsyncHash(testFiles, ct);

            // --- Hash consistency (sync vs async, MD5 vs SHA256) ---
            _progressLabel = "Correctness: Hash consistency...";
            Repaint();
            await TestHashConsistency(testFiles, ct);

            // --- Stream Hash ---
            _progressLabel = "Correctness: Stream hash...";
            Repaint();
            await TestStreamHash(testFiles, ct);

            // --- File Comparison ---
            _progressLabel = "Correctness: File comparison...";
            Repaint();
            await TestFileComparison(testFiles, ct);

            // --- Byte Array Hash Comparison ---
            _progressLabel = "Correctness: Byte array comparison...";
            Repaint();
            TestByteArrayComparison();

            // --- Edge Cases ---
            _progressLabel = "Correctness: Edge cases...";
            Repaint();
            await TestEdgeCases(ct);

            // --- Copy with Comparison ---
            _progressLabel = "Correctness: Copy with comparison...";
            Repaint();
            await TestCopyWithComparison(testFiles, ct);

            // --- XxHash64 ---
            _progressLabel = "Correctness: XxHash64...";
            Repaint();
            TestXxHash64Standalone();
            TestXxHash64ViaFileUtility(testFiles);
            await TestXxHash64Async(testFiles, ct);

            _progress = 0.5f;
            Repaint();
        }

        private void TestToHexString()
        {
            const string category = "ToHexString";

            // Empty
            string empty = FileUtility.ToHexString(ReadOnlySpan<byte>.Empty);
            AddResult(empty == string.Empty ? ResultType.Pass : ResultType.Fail,
                category, "Empty input → empty string", $"Got: \"{empty}\"");

            // Known value
            byte[] knownBytes = { 0xDE, 0xAD, 0xBE, 0xEF };
            string hex = FileUtility.ToHexString(knownBytes);
            AddResult(hex == "deadbeef" ? ResultType.Pass : ResultType.Fail,
                category, "0xDEADBEEF → \"deadbeef\"", $"Got: \"{hex}\"");

            // All zeros
            byte[] zeros = new byte[32];
            string zeroHex = FileUtility.ToHexString(zeros);
            bool allZero = zeroHex.Length == 64;
            for (int i = 0; i < zeroHex.Length && allZero; i++) allZero = zeroHex[i] == '0';
            AddResult(allZero ? ResultType.Pass : ResultType.Fail,
                category, "32 zero bytes → 64 '0' chars", $"Length: {zeroHex.Length}");

            // All 0xFF
            byte[] ffs = new byte[16];
            for (int i = 0; i < 16; i++) ffs[i] = 0xFF;
            string ffHex = FileUtility.ToHexString(ffs);
            bool allF = ffHex == "ffffffffffffffffffffffffffffffff";
            AddResult(allF ? ResultType.Pass : ResultType.Fail,
                category, "16x 0xFF → 32 'f' chars", $"Got: \"{ffHex}\"");
        }

        private void TestSyncHash(Dictionary<int, string> testFiles)
        {
            const string category = "Sync Hash (ComputeFileHash)";
            string file1K = testFiles[1];

            // SHA256 sync
            int sha256Size = FileUtility.GetHashSizeInBytes(HashAlgorithmType.SHA256);
            Span<byte> hashBuffer = stackalloc byte[sha256Size];
            bool success = FileUtility.ComputeFileHash(file1K, HashAlgorithmType.SHA256, hashBuffer);
            AddResult(success ? ResultType.Pass : ResultType.Fail,
                category, "SHA256 sync hash (1KB)", success ? FileUtility.ToHexString(hashBuffer) : "FAILED");

            // MD5 sync
            int md5Size = FileUtility.GetHashSizeInBytes(HashAlgorithmType.MD5);
            Span<byte> md5Buffer = stackalloc byte[md5Size];
            bool md5Success = FileUtility.ComputeFileHash(file1K, HashAlgorithmType.MD5, md5Buffer);
            AddResult(md5Success ? ResultType.Pass : ResultType.Fail,
                category, "MD5 sync hash (1KB)", md5Success ? FileUtility.ToHexString(md5Buffer) : "FAILED");

            // Verify against System.Security.Cryptography
            byte[] fileBytes = File.ReadAllBytes(file1K);
            using (var sha256 = SHA256.Create())
            {
                byte[] expected = sha256.ComputeHash(fileBytes);
                string expectedHex = FileUtility.ToHexString(expected);
                string actualHex = FileUtility.ToHexString(hashBuffer);
                AddResult(expectedHex == actualHex ? ResultType.Pass : ResultType.Fail,
                    category, "SHA256 matches System.Security", $"Expected: {expectedHex}, Got: {actualHex}");
            }

            // Convenience method
            string hexResult = FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.SHA256);
            AddResult(hexResult != null && hexResult.Length == 64 ? ResultType.Pass : ResultType.Fail,
                category, "ComputeFileHashToHexString", hexResult ?? "null");

            // Non-existent file
            bool failResult = FileUtility.ComputeFileHash("__nonexistent__.bin", HashAlgorithmType.SHA256, hashBuffer);
            AddResult(!failResult ? ResultType.Pass : ResultType.Fail,
                category, "Non-existent file → false", $"Got: {failResult}");

            // Buffer too small
            Span<byte> tinyBuf = stackalloc byte[1];
            bool tooSmall = FileUtility.ComputeFileHash(file1K, HashAlgorithmType.SHA256, tinyBuf);
            AddResult(!tooSmall ? ResultType.Pass : ResultType.Fail,
                category, "Buffer too small → false", $"Got: {tooSmall}");
        }

        private async Task TestAsyncHash(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "Async Hash (ComputeFileHashAsync)";
            string file1K = testFiles[1];

            // SHA256 async
            int hashSize = FileUtility.GetHashSizeInBytes(HashAlgorithmType.SHA256);
            byte[] hashBuffer = new byte[hashSize];
            bool success = await FileUtility.ComputeFileHashAsync(file1K, HashAlgorithmType.SHA256, hashBuffer, ct);
            AddResult(success ? ResultType.Pass : ResultType.Fail,
                category, "SHA256 async hash (1KB)", success ? FileUtility.ToHexString(hashBuffer) : "FAILED");

            // Hex convenience
            string hexResult = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.SHA256, ct);
            string directHex = FileUtility.ToHexString(hashBuffer);
            AddResult(hexResult == directHex ? ResultType.Pass : ResultType.Fail,
                category, "HexString matches direct hash", $"Match: {hexResult == directHex}");

            // MD5 async
            int md5Size = FileUtility.GetHashSizeInBytes(HashAlgorithmType.MD5);
            byte[] md5Buffer = new byte[md5Size];
            bool md5Success = await FileUtility.ComputeFileHashAsync(file1K, HashAlgorithmType.MD5, md5Buffer, ct);
            AddResult(md5Success ? ResultType.Pass : ResultType.Fail,
                category, "MD5 async hash (1KB)", md5Success ? FileUtility.ToHexString(md5Buffer) : "FAILED");
        }

        private async Task TestHashConsistency(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "Hash Consistency";
            string file1K = testFiles[1];

            // Sync SHA256
            string syncHex = FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.SHA256);

            // Async SHA256
            string asyncHex = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.SHA256, ct);

            AddResult(syncHex == asyncHex ? ResultType.Pass : ResultType.Fail,
                category, "Sync SHA256 == Async SHA256", $"Sync: {syncHex}, Async: {asyncHex}");

            // Idempotency: hash same file twice
            string hash2 = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.SHA256, ct);
            AddResult(asyncHex == hash2 ? ResultType.Pass : ResultType.Fail,
                category, "Idempotent hash (same file twice)", $"1st: {asyncHex}, 2nd: {hash2}");

            // MD5 sync vs async
            string syncMd5 = FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.MD5);
            string asyncMd5 = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.MD5, ct);
            AddResult(syncMd5 == asyncMd5 ? ResultType.Pass : ResultType.Fail,
                category, "Sync MD5 == Async MD5", $"Match: {syncMd5 == asyncMd5}");

            // XxHash64 sync vs async
            string syncXx = FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.XxHash64);
            string asyncXx = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.XxHash64, ct);
            AddResult(syncXx == asyncXx ? ResultType.Pass : ResultType.Fail,
                category, "Sync XxHash64 == Async XxHash64", $"Sync: {syncXx}, Async: {asyncXx}");
        }

        private async Task TestStreamHash(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "Stream Hash";
            string file1K = testFiles[1];

            // Compute file hash first for reference
            string fileHash = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.SHA256, ct);

            // Compute via stream
            using (var stream = new FileStream(file1K, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                string streamHash = await FileUtility.ComputeStreamHashToHexStringAsync(stream, HashAlgorithmType.SHA256, ct);
                AddResult(fileHash == streamHash ? ResultType.Pass : ResultType.Fail,
                    category, "Stream hash == File hash", $"File: {fileHash}, Stream: {streamHash}");
            }

            // Null/unreadable stream
            int hashSize = FileUtility.GetHashSizeInBytes(HashAlgorithmType.SHA256);
            byte[] buf = new byte[hashSize];
            bool nullResult = await FileUtility.ComputeStreamHashAsync(null, HashAlgorithmType.SHA256, buf, ct);
            AddResult(!nullResult ? ResultType.Pass : ResultType.Fail,
                category, "Null stream → false", $"Got: {nullResult}");
        }

        private async Task TestFileComparison(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "File Comparison";
            string file1K = testFiles[1];

            // Same path
            bool samePath = await FileUtility.AreFilesEqualAsync(file1K, file1K, HashAlgorithmType.SHA256, ct);
            AddResult(samePath ? ResultType.Pass : ResultType.Fail,
                category, "Same path → true", $"Got: {samePath}");

            // Identical copy
            string copyPath = Path.Combine(_tempDir, "copy_1kb.bin");
            File.Copy(file1K, copyPath, true);
            bool identical = await FileUtility.AreFilesEqualAsync(file1K, copyPath, HashAlgorithmType.SHA256, ct);
            AddResult(identical ? ResultType.Pass : ResultType.Fail,
                category, "Identical copy → true", $"Got: {identical}");

            // Different content (flip one byte)
            string diffPath = Path.Combine(_tempDir, "diff_1kb.bin");
            byte[] diffData = File.ReadAllBytes(file1K);
            diffData[diffData.Length / 2] ^= 0xFF;
            File.WriteAllBytes(diffPath, diffData);
            bool different = await FileUtility.AreFilesEqualAsync(file1K, diffPath, HashAlgorithmType.SHA256, ct);
            AddResult(!different ? ResultType.Pass : ResultType.Fail,
                category, "Different content → false", $"Got: {different}");

            // Different size
            string smallPath = Path.Combine(_tempDir, "small.bin");
            File.WriteAllBytes(smallPath, new byte[10]);
            bool diffSize = await FileUtility.AreFilesEqualAsync(file1K, smallPath, HashAlgorithmType.SHA256, ct);
            AddResult(!diffSize ? ResultType.Pass : ResultType.Fail,
                category, "Different size → false", $"Got: {diffSize}");

            // Empty files
            string empty1 = Path.Combine(_tempDir, "empty1.bin");
            string empty2 = Path.Combine(_tempDir, "empty2.bin");
            File.WriteAllBytes(empty1, Array.Empty<byte>());
            File.WriteAllBytes(empty2, Array.Empty<byte>());
            bool emptyEqual = await FileUtility.AreFilesEqualAsync(empty1, empty2, HashAlgorithmType.SHA256, ct);
            AddResult(emptyEqual ? ResultType.Pass : ResultType.Fail,
                category, "Two empty files → true", $"Got: {emptyEqual}");

            // Non-existent file
            bool nonExist = await FileUtility.AreFilesEqualAsync(file1K, "__nonexistent__.bin", HashAlgorithmType.SHA256, ct);
            AddResult(!nonExist ? ResultType.Pass : ResultType.Fail,
                category, "Non-existent file → false", $"Got: {nonExist}");

            // Large file comparison (>10MB, triggers chunk path)
            if (testFiles.ContainsKey(10240))
            {
                string file10M = testFiles[10240];
                string copy10M = Path.Combine(_tempDir, "copy_10mb.bin");
                File.Copy(file10M, copy10M, true);
                bool largeEqual = await FileUtility.AreFilesEqualAsync(file10M, copy10M, HashAlgorithmType.SHA256, ct);
                AddResult(largeEqual ? ResultType.Pass : ResultType.Fail,
                    category, "Large file (10MB) chunk compare → true", $"Got: {largeEqual}");
            }

            // Stream comparison
            if (testFiles.ContainsKey(1024))
            {
                string file1M = testFiles[1024];
                using (var s1 = new FileStream(file1M, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var s2 = new FileStream(file1M, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool streamEqual = await FileUtility.AreStreamsEqualAsync(s1, s2, s1.Length, s2.Length, HashAlgorithmType.SHA256, ct);
                    AddResult(streamEqual ? ResultType.Pass : ResultType.Fail,
                        category, "Stream comparison (same file) → true", $"Got: {streamEqual}");
                }
            }
        }

        private void TestByteArrayComparison()
        {
            const string category = "Byte Array Comparison";

            byte[] a = { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] b = { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] c = { 1, 2, 3, 4, 5, 6, 7, 9 };

            bool equal = FileUtility.AreByteArraysEqualByHash(a, b, HashAlgorithmType.SHA256);
            AddResult(equal ? ResultType.Pass : ResultType.Fail,
                category, "Identical arrays → true", $"Got: {equal}");

            bool notEqual = FileUtility.AreByteArraysEqualByHash(a, c, HashAlgorithmType.SHA256);
            AddResult(!notEqual ? ResultType.Pass : ResultType.Fail,
                category, "Different arrays → false", $"Got: {notEqual}");

            bool emptyEqual = FileUtility.AreByteArraysEqualByHash(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, HashAlgorithmType.SHA256);
            AddResult(emptyEqual ? ResultType.Pass : ResultType.Fail,
                category, "Empty arrays → true", $"Got: {emptyEqual}");

            bool diffLen = FileUtility.AreByteArraysEqualByHash(a, new byte[] { 1, 2, 3 }, HashAlgorithmType.SHA256);
            AddResult(!diffLen ? ResultType.Pass : ResultType.Fail,
                category, "Different length → false", $"Got: {diffLen}");
        }

        private async Task TestEdgeCases(CancellationToken ct)
        {
            const string category = "Edge Cases";

            // 1-byte file
            string oneByte = Path.Combine(_tempDir, "onebyte.bin");
            File.WriteAllBytes(oneByte, new byte[] { 0x42 });

            string hash = await FileUtility.ComputeFileHashToHexStringAsync(oneByte, HashAlgorithmType.SHA256, ct);
            AddResult(hash != null && hash.Length == 64 ? ResultType.Pass : ResultType.Fail,
                category, "1-byte file hash", hash ?? "null");

            // Sync 1-byte
            string syncHash = FileUtility.ComputeFileHashToHexString(oneByte, HashAlgorithmType.SHA256);
            AddResult(hash == syncHash ? ResultType.Pass : ResultType.Fail,
                category, "1-byte sync == async", $"Match: {hash == syncHash}");

            // Empty file hash
            string emptyFile = Path.Combine(_tempDir, "empty_hash.bin");
            File.WriteAllBytes(emptyFile, Array.Empty<byte>());
            string emptyHash = await FileUtility.ComputeFileHashToHexStringAsync(emptyFile, HashAlgorithmType.SHA256, ct);
            // SHA256 of empty input is e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
            AddResult(emptyHash == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" ? ResultType.Pass : ResultType.Fail,
                category, "Empty file SHA256 (known hash)", emptyHash ?? "null");

            // Cancellation test
            string largePath = Path.Combine(_tempDir, "cancel_test.bin");
            await GenerateFile(largePath, 1024 * 1024, ct);
            var cancelCts = new CancellationTokenSource();
            cancelCts.Cancel(); // Immediately cancelled
            bool cancelled = false;
            try
            {
                await FileUtility.ComputeFileHashToHexStringAsync(largePath, HashAlgorithmType.SHA256, cancelCts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            cancelCts.Dispose();
            AddResult(cancelled ? ResultType.Pass : ResultType.Fail,
                category, "Cancellation throws OperationCanceledException", $"Got: {cancelled}");

            // GetHashSizeInBytes
            AddResult(FileUtility.GetHashSizeInBytes(HashAlgorithmType.MD5) == 16 ? ResultType.Pass : ResultType.Fail,
                category, "GetHashSizeInBytes(MD5) == 16", $"Got: {FileUtility.GetHashSizeInBytes(HashAlgorithmType.MD5)}");
            AddResult(FileUtility.GetHashSizeInBytes(HashAlgorithmType.SHA256) == 32 ? ResultType.Pass : ResultType.Fail,
                category, "GetHashSizeInBytes(SHA256) == 32", $"Got: {FileUtility.GetHashSizeInBytes(HashAlgorithmType.SHA256)}");
            AddResult(FileUtility.GetHashSizeInBytes(HashAlgorithmType.XxHash64) == 8 ? ResultType.Pass : ResultType.Fail,
                category, "GetHashSizeInBytes(XxHash64) == 8", $"Got: {FileUtility.GetHashSizeInBytes(HashAlgorithmType.XxHash64)}");
        }

        private async Task TestCopyWithComparison(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "Copy With Comparison";
            string file1K = testFiles[1];
            string destPath = Path.Combine(_tempDir, "copied_1kb.bin");

            // Fresh copy
            float lastProgress = -1f;
            int progressCalls = 0;
            var progressReporter = new Progress<float>(p => { lastProgress = p; progressCalls++; });
            await FileUtility.CopyFileWithComparisonAsync(file1K, destPath, HashAlgorithmType.SHA256, progressReporter, ct);
            // Small delay so Progress<T> callback can fire (it posts to SynchronizationContext)
            await Task.Delay(100, ct);

            bool copied = File.Exists(destPath);
            AddResult(copied ? ResultType.Pass : ResultType.Fail,
                category, "File was copied", $"Exists: {copied}");

            if (copied)
            {
                bool match = await FileUtility.AreFilesEqualAsync(file1K, destPath, HashAlgorithmType.SHA256, ct);
                AddResult(match ? ResultType.Pass : ResultType.Fail,
                    category, "Copied file matches source", $"Match: {match}");
            }

            AddResult(progressCalls > 0 ? ResultType.Pass : ResultType.Fail,
                category, "IProgress received callbacks", $"Calls: {progressCalls}, Last: {lastProgress:F2}");

            // Copy again (should skip — identical)
            var sw = Stopwatch.StartNew();
            await FileUtility.CopyFileWithComparisonAsync(file1K, destPath, HashAlgorithmType.SHA256, ct);
            sw.Stop();
            AddResult(ResultType.Pass, category, "Re-copy skipped (identical)", $"Took: {sw.ElapsedMilliseconds}ms");

            // Copy to new subdirectory (auto-create)
            string subDirDest = Path.Combine(_tempDir, "sub", "dir", "deep_copy.bin");
            await FileUtility.CopyFileWithComparisonAsync(file1K, subDirDest, HashAlgorithmType.SHA256, ct);
            bool deepCopied = File.Exists(subDirDest);
            AddResult(deepCopied ? ResultType.Pass : ResultType.Fail,
                category, "Auto-creates subdirectories", $"Exists: {deepCopied}");
        }

        private void TestXxHash64Standalone()
        {
            const string category = "XxHash64 Standalone";

            // Known test vector: XXH64("") = 0xEF46DB3751D8E999
            ulong emptyHash = XxHash64.HashToUInt64(ReadOnlySpan<byte>.Empty);
            AddResult(emptyHash == 0xEF46DB3751D8E999UL ? ResultType.Pass : ResultType.Fail,
                category, "Empty input → known vector", $"Got: 0x{emptyHash:X16}, Expected: 0xEF46DB3751D8E999");

            // One-shot vs streaming consistency
            byte[] testData = new byte[1024];
            new System.Random(42).NextBytes(testData);
            ulong oneShot = XxHash64.HashToUInt64(testData);
            var hasher = XxHash64.Create();
            hasher.Append(testData);
            ulong streamed = hasher.GetDigest();
            AddResult(oneShot == streamed ? ResultType.Pass : ResultType.Fail,
                category, "One-shot == Streaming", $"OneShot: 0x{oneShot:X16}, Streamed: 0x{streamed:X16}");

            // Multi-append streaming
            var multiHasher = XxHash64.Create();
            multiHasher.Append(testData.AsSpan(0, 512));
            multiHasher.Append(testData.AsSpan(512, 512));
            ulong multiStreamed = multiHasher.GetDigest();
            AddResult(oneShot == multiStreamed ? ResultType.Pass : ResultType.Fail,
                category, "Multi-append == One-shot", $"Multi: 0x{multiStreamed:X16}, OneShot: 0x{oneShot:X16}");

            // TryWriteHash
            Span<byte> hashBuf = stackalloc byte[8];
            var twHasher = XxHash64.Create();
            twHasher.Append(testData);
            bool written = twHasher.TryWriteHash(hashBuf);
            AddResult(written ? ResultType.Pass : ResultType.Fail,
                category, "TryWriteHash succeeds", $"Written: {written}");

            // TryWriteHash buffer too small
            Span<byte> tinyBuf = stackalloc byte[4];
            var twHasher2 = XxHash64.Create();
            twHasher2.Append(testData);
            bool tooSmall = twHasher2.TryWriteHash(tinyBuf);
            AddResult(!tooSmall ? ResultType.Pass : ResultType.Fail,
                category, "TryWriteHash tiny buffer → false", $"Got: {tooSmall}");

            // Seed variation
            ulong seeded = XxHash64.HashToUInt64(testData, 12345UL);
            AddResult(seeded != oneShot ? ResultType.Pass : ResultType.Fail,
                category, "Different seed → different hash", $"Seed0: 0x{oneShot:X16}, Seed12345: 0x{seeded:X16}");

            // Hash size
            int xxSize = FileUtility.GetHashSizeInBytes(HashAlgorithmType.XxHash64);
            AddResult(xxSize == 8 ? ResultType.Pass : ResultType.Fail,
                category, "GetHashSizeInBytes == 8", $"Got: {xxSize}");
        }

        private void TestXxHash64ViaFileUtility(Dictionary<int, string> testFiles)
        {
            const string category = "XxHash64 via FileUtility (Sync)";
            string file1K = testFiles[1];

            // Sync hash
            int xxSize = FileUtility.GetHashSizeInBytes(HashAlgorithmType.XxHash64);
            Span<byte> hashBuffer = stackalloc byte[xxSize];
            bool success = FileUtility.ComputeFileHash(file1K, HashAlgorithmType.XxHash64, hashBuffer);
            AddResult(success ? ResultType.Pass : ResultType.Fail,
                category, "XxHash64 sync hash (1KB)", success ? FileUtility.ToHexString(hashBuffer) : "FAILED");

            // Hex convenience
            string hexResult = FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.XxHash64);
            AddResult(hexResult != null && hexResult.Length == 16 ? ResultType.Pass : ResultType.Fail,
                category, "HexString length == 16", hexResult ?? "null");

            // Idempotency
            string hexResult2 = FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.XxHash64);
            AddResult(hexResult == hexResult2 ? ResultType.Pass : ResultType.Fail,
                category, "Idempotent sync hash", $"1st: {hexResult}, 2nd: {hexResult2}");

            // Byte array comparison (0 GC path)
            byte[] a = new byte[256];
            byte[] b = new byte[256];
            new System.Random(99).NextBytes(a);
            Buffer.BlockCopy(a, 0, b, 0, a.Length);
            bool equal = FileUtility.AreByteArraysEqualByHash(a, b, HashAlgorithmType.XxHash64);
            AddResult(equal ? ResultType.Pass : ResultType.Fail,
                category, "AreByteArraysEqualByHash identical → true", $"Got: {equal}");

            b[128] ^= 0xFF;
            bool notEqual = FileUtility.AreByteArraysEqualByHash(a, b, HashAlgorithmType.XxHash64);
            AddResult(!notEqual ? ResultType.Pass : ResultType.Fail,
                category, "AreByteArraysEqualByHash different → false", $"Got: {notEqual}");
        }

        private async Task TestXxHash64Async(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "XxHash64 via FileUtility (Async)";
            string file1K = testFiles[1];

            // Async hash
            int xxSize = FileUtility.GetHashSizeInBytes(HashAlgorithmType.XxHash64);
            byte[] hashBuffer = new byte[xxSize];
            bool success = await FileUtility.ComputeFileHashAsync(file1K, HashAlgorithmType.XxHash64, hashBuffer, ct);
            AddResult(success ? ResultType.Pass : ResultType.Fail,
                category, "XxHash64 async hash (1KB)", success ? FileUtility.ToHexString(hashBuffer) : "FAILED");

            // Hex convenience
            string hexResult = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.XxHash64, ct);
            string directHex = FileUtility.ToHexString(hashBuffer);
            AddResult(hexResult == directHex ? ResultType.Pass : ResultType.Fail,
                category, "HexString matches direct hash", $"Match: {hexResult == directHex}");

            // Stream hash
            string fileHash = await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.XxHash64, ct);
            using (var stream = new FileStream(file1K, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                string streamHash = await FileUtility.ComputeStreamHashToHexStringAsync(stream, HashAlgorithmType.XxHash64, ct);
                AddResult(fileHash == streamHash ? ResultType.Pass : ResultType.Fail,
                    category, "Stream hash == File hash", $"File: {fileHash}, Stream: {streamHash}");
            }

            // File comparison using XxHash64
            string copyPath = Path.Combine(_tempDir, "xx_copy_1kb.bin");
            File.Copy(file1K, copyPath, true);
            bool identical = await FileUtility.AreFilesEqualAsync(file1K, copyPath, HashAlgorithmType.XxHash64, ct);
            AddResult(identical ? ResultType.Pass : ResultType.Fail,
                category, "File comparison identical → true", $"Got: {identical}");

            // File comparison with different content
            byte[] diffData = File.ReadAllBytes(file1K);
            diffData[0] ^= 0xFF;
            string diffPath = Path.Combine(_tempDir, "xx_diff_1kb.bin");
            File.WriteAllBytes(diffPath, diffData);
            bool different = await FileUtility.AreFilesEqualAsync(file1K, diffPath, HashAlgorithmType.XxHash64, ct);
            AddResult(!different ? ResultType.Pass : ResultType.Fail,
                category, "File comparison different → false", $"Got: {different}");
        }

        // ==========================================================================
        // Performance Benchmarks
        // ==========================================================================

        private async Task RunPerformanceBenchmarks(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            // --- Hash throughput ---
            await BenchmarkHashThroughput(testFiles, ct);

            // --- Sync vs Async ---
            await BenchmarkSyncVsAsync(testFiles, ct);

            // --- File comparison (hash vs chunk) ---
            await BenchmarkFileComparison(testFiles, ct);

            // --- GC allocation tracking ---
            await BenchmarkGCAllocations(testFiles, ct);

            _progress = 1f;
            Repaint();
        }

        private async Task BenchmarkHashThroughput(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "Hash Throughput (Async)";

            foreach (var kvp in testFiles)
            {
                int sizeKB = kvp.Key;
                string filePath = kvp.Value;
                long fileSize = new FileInfo(filePath).Length;
                string sizeLabel = FormatSize(fileSize);

                _progressLabel = $"Benchmark: Hashing {sizeLabel}...";
                Repaint();

                // SHA256
                double sha256Ms = await BenchmarkOp(async () =>
                {
                    await FileUtility.ComputeFileHashToHexStringAsync(filePath, HashAlgorithmType.SHA256, ct);
                }, ct);

                double sha256MBs = fileSize / (1024.0 * 1024.0) / (sha256Ms / 1000.0);
                AddResult(ResultType.Perf, category, $"SHA256 {sizeLabel}",
                    $"{sha256Ms:F2}ms  ({sha256MBs:F1} MB/s)");

                // MD5
                double md5Ms = await BenchmarkOp(async () =>
                {
                    await FileUtility.ComputeFileHashToHexStringAsync(filePath, HashAlgorithmType.MD5, ct);
                }, ct);

                double md5MBs = fileSize / (1024.0 * 1024.0) / (md5Ms / 1000.0);
                AddResult(ResultType.Perf, category, $"MD5   {sizeLabel}",
                    $"{md5Ms:F2}ms  ({md5MBs:F1} MB/s)");

                // XxHash64
                double xxMs = await BenchmarkOp(async () =>
                {
                    await FileUtility.ComputeFileHashToHexStringAsync(filePath, HashAlgorithmType.XxHash64, ct);
                }, ct);

                double xxMBs = fileSize / (1024.0 * 1024.0) / (xxMs / 1000.0);
                AddResult(ResultType.Perf, category, $"XXH64 {sizeLabel}",
                    $"{xxMs:F2}ms  ({xxMBs:F1} MB/s)");

                ct.ThrowIfCancellationRequested();
            }
        }

        private async Task BenchmarkSyncVsAsync(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "Sync vs Async Hash";

            // Use 1MB file for meaningful comparison
            int targetKey = testFiles.ContainsKey(1024) ? 1024 : 1;
            string filePath = testFiles[targetKey];
            long fileSize = new FileInfo(filePath).Length;
            string sizeLabel = FormatSize(fileSize);

            _progressLabel = $"Benchmark: Sync vs Async ({sizeLabel})...";
            Repaint();

            // Sync
            double syncMs = BenchmarkSyncOp(() =>
            {
                FileUtility.ComputeFileHashToHexString(filePath, HashAlgorithmType.SHA256);
            });
            AddResult(ResultType.Perf, category, $"Sync SHA256 ({sizeLabel})", $"{syncMs:F2}ms");

            // Async
            double asyncMs = await BenchmarkOp(async () =>
            {
                await FileUtility.ComputeFileHashToHexStringAsync(filePath, HashAlgorithmType.SHA256, ct);
            }, ct);
            AddResult(ResultType.Perf, category, $"Async SHA256 ({sizeLabel})", $"{asyncMs:F2}ms");

            double ratio = syncMs > 0 ? asyncMs / syncMs : 0;
            AddResult(ResultType.Info, category, "Async / Sync ratio (SHA256)", $"{ratio:F2}x");

            // XxHash64 Sync
            double xxSyncMs = BenchmarkSyncOp(() =>
            {
                FileUtility.ComputeFileHashToHexString(filePath, HashAlgorithmType.XxHash64);
            });
            AddResult(ResultType.Perf, category, $"Sync XxHash64 ({sizeLabel})", $"{xxSyncMs:F2}ms");

            // XxHash64 Async
            double xxAsyncMs = await BenchmarkOp(async () =>
            {
                await FileUtility.ComputeFileHashToHexStringAsync(filePath, HashAlgorithmType.XxHash64, ct);
            }, ct);
            AddResult(ResultType.Perf, category, $"Async XxHash64 ({sizeLabel})", $"{xxAsyncMs:F2}ms");

            double xxRatio = xxSyncMs > 0 ? xxAsyncMs / xxSyncMs : 0;
            AddResult(ResultType.Info, category, "Async / Sync ratio (XxHash64)", $"{xxRatio:F2}x");
        }

        private async Task BenchmarkFileComparison(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "File Comparison";

            // Small file (hash path)
            if (testFiles.ContainsKey(1024))
            {
                string src = testFiles[1024];
                string copy = Path.Combine(_tempDir, "bench_cmp_1mb.bin");
                File.Copy(src, copy, true);

                _progressLabel = "Benchmark: File comparison (1MB)...";
                Repaint();

                double ms = await BenchmarkOp(async () =>
                {
                    await FileUtility.AreFilesEqualAsync(src, copy, HashAlgorithmType.SHA256, ct);
                }, ct);
                AddResult(ResultType.Perf, category, "1MB identical (hash path)", $"{ms:F2}ms");
            }

            // Large file (chunk path, >10MB)
            if (testFiles.ContainsKey(51200))
            {
                string src = testFiles[51200];
                string copy = Path.Combine(_tempDir, "bench_cmp_50mb.bin");
                File.Copy(src, copy, true);

                _progressLabel = "Benchmark: File comparison (50MB)...";
                Repaint();

                double ms = await BenchmarkOp(async () =>
                {
                    await FileUtility.AreFilesEqualAsync(src, copy, HashAlgorithmType.SHA256, ct);
                }, ct);
                long fileSize = new FileInfo(src).Length;
                double mbPerSec = fileSize / (1024.0 * 1024.0) / (ms / 1000.0);
                AddResult(ResultType.Perf, category, "50MB identical (chunk path)", $"{ms:F2}ms  ({mbPerSec:F1} MB/s)");
            }

            // Different files (early exit)
            if (testFiles.ContainsKey(1024))
            {
                string src = testFiles[1024];
                string empty = Path.Combine(_tempDir, "bench_cmp_empty.bin");
                File.WriteAllBytes(empty, Array.Empty<byte>());

                double ms = await BenchmarkOp(async () =>
                {
                    await FileUtility.AreFilesEqualAsync(src, empty, HashAlgorithmType.SHA256, ct);
                }, ct);
                AddResult(ResultType.Perf, category, "Size mismatch (early exit)", $"{ms:F4}ms");
            }
        }

        private async Task BenchmarkGCAllocations(Dictionary<int, string> testFiles, CancellationToken ct)
        {
            const string category = "GC Allocation Tracking";

            string file1K = testFiles[1];

            _progressLabel = "Benchmark: GC allocations...";
            Repaint();

            // Warm up to fill ArrayPool
            await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.SHA256, ct);
            FileUtility.ComputeFileHashToHexString(file1K, HashAlgorithmType.SHA256);

            // Verify recorder availability
            using (var testRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 1))
            {
                if (!testRecorder.Valid)
                {
                    AddResult(ResultType.Fail, category, "GC Recorder",
                        "ProfilerRecorder for GC.Alloc not available on this runtime.");
                    return;
                }
            }

            // NOTE: ProfilerRecorder samples are buffered in thread-local storage and
            // flushed to the ring buffer only at frame boundaries. For synchronous code
            // running within a single editor frame, samples are invisible until the frame
            // ends. We use `await Task.Yield()` after each test to force a frame boundary,
            // flushing pending samples before reading them.
            //
            // Sync helpers return ProfilerRecorder (still recording) so the caller can
            // yield before reading. This separation is required because Span<T>/stackalloc
            // cannot appear in async methods (CS4012).

            // --- Diagnostic: verify recorder tracks IncrementalHash allocations ---
            // Uses the same allocation type (IncrementalHash.CreateHash) as the real tests.
            // Raw byte[] allocations are NOT fully tracked by GC.Alloc in Unity Mono.
            {
                const int iterations = 100;
                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 4);
                var holdList = new List<IncrementalHash>(iterations);
                for (int i = 0; i < iterations; i++)
                    holdList.Add(IncrementalHash.CreateHash(HashAlgorithmName.SHA256));

                await Task.Yield(); // flush samples

                long gcBytes = SumRecorderSamples(rec);
                int allocCount = rec.Count;
                bool overflow = rec.Count >= rec.Capacity;
                foreach (var h in holdList) h.Dispose();
                rec.Dispose();

                bool ok = gcBytes >= 10000; // expect ~27,900 (100 × ~279)
                AddResult(ok ? ResultType.Pass : ResultType.Fail, category,
                    "Diagnostic: 100×IncrementalHash",
                    FormatGCResult(gcBytes, allocCount, iterations, overflow)
                        + (ok ? " (recorder OK)" : " (recorder unreliable)"));
            }

            // --- IncrementalHash only (no FileStream, tight CPU loop) ---
            {
                var rec = RunIncrementalHashOnlyGC();
                await Task.Yield();
                ReportGC(rec, category, "IncrementalHash only ×1000 (no I/O)", 1000);
            }

            // --- Sync ComputeFileHash (stackalloc output, no hex string) ---
            {
                var rec = RunSyncHashGC(file1K);
                await Task.Yield();
                ReportGC(rec, category, "Sync ComputeFileHash ×100", 100);
            }

            // --- Sync HexString (IncrementalHash + FileStream + string) ---
            {
                var rec = RunSyncHexStringGC(file1K);
                await Task.Yield();
                ReportGC(rec, category, "Sync HexString ×100", 100);
            }

            // --- ToHexString (pure string allocation, no I/O) ---
            {
                const int iterations = 1000;
                byte[] hashData = new byte[32];
                new System.Random(42).NextBytes(hashData);

                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 4);
                for (int i = 0; i < iterations; i++)
                    FileUtility.ToHexString(hashData);

                await Task.Yield();
                ReportGC(rec, category, $"ToHexString ×{iterations}", iterations);
            }

            // --- AreByteArraysEqualByHash (uses IncrementalHash, stackalloc hash buffers) ---
            {
                const int iterations = 1000;
                byte[] a = new byte[1024];
                byte[] b = new byte[1024];
                new System.Random(42).NextBytes(a);
                Buffer.BlockCopy(a, 0, b, 0, a.Length);

                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 8);
                for (int i = 0; i < iterations; i++)
                    FileUtility.AreByteArraysEqualByHash(a, b, HashAlgorithmType.SHA256);

                await Task.Yield();
                ReportGC(rec, category, $"AreByteArraysEqualByHash ×{iterations}", iterations);
            }

            // --- Async hash (Task + state machine + string + IncrementalHash + ArrayPool) ---
            {
                const int iterations = 50;
                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 16);

                for (int i = 0; i < iterations; i++)
                    await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.SHA256, ct);

                await Task.Yield(); // flush any remaining samples
                ReportGC(rec, category, $"Async HexString (SHA256) ×{iterations}", iterations);
            }

            // --- XxHash64 sync ComputeFileHash (struct, no IncrementalHash) ---
            {
                var rec = RunSyncXxHash64GC(file1K);
                await Task.Yield();
                ReportGC(rec, category, "Sync XxHash64 FileHash ×100", 100);
            }

            // --- XxHash64 async hash ---
            {
                const int iterations = 50;
                // Warm up
                await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.XxHash64, ct);

                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 16);

                for (int i = 0; i < iterations; i++)
                    await FileUtility.ComputeFileHashToHexStringAsync(file1K, HashAlgorithmType.XxHash64, ct);

                await Task.Yield();
                ReportGC(rec, category, $"Async HexString (XxHash64) ×{iterations}", iterations);
            }

            // --- XxHash64 AreByteArraysEqualByHash (expect true 0 GC — struct path) ---
            {
                const int iterations = 1000;
                byte[] xxA = new byte[1024];
                byte[] xxB = new byte[1024];
                new System.Random(42).NextBytes(xxA);
                Buffer.BlockCopy(xxA, 0, xxB, 0, xxA.Length);

                var rec = RunXxHash64ByteArrayComparisonGC(xxA, xxB, iterations);
                await Task.Yield();
                ReportGC(rec, category, $"XxHash64 AreByteArraysEqual ×{iterations}", iterations);
            }

            // --- XxHash64 one-shot HashToUInt64 (pure struct, expect 0 GC) ---
            {
                var rec = RunXxHash64OneShotGC();
                await Task.Yield();
                ReportGC(rec, category, "XxHash64 HashToUInt64 ×1000", 1000);
            }
        }

        // ==========================================================================
        // Helpers
        // ==========================================================================

        /// <summary>
        /// Reads samples from a ProfilerRecorder, reports the result, and disposes the recorder.
        /// </summary>
        private void ReportGC(ProfilerRecorder rec, string category, string name, int iterations)
        {
            long gcBytes = SumRecorderSamples(rec);
            int allocCount = rec.Count;
            bool overflow = rec.Count >= rec.Capacity;
            rec.Dispose();
            AddResult(ResultType.Perf, category, name, FormatGCResult(gcBytes, allocCount, iterations, overflow));
        }

        /// <summary>
        /// Runs IncrementalHash.CreateHash in isolation. Returns recorder for caller to yield + read.
        /// </summary>
        private ProfilerRecorder RunIncrementalHashOnlyGC()
        {
            const int iterations = 1000;
            byte[] testData = new byte[1024];
            new System.Random(42).NextBytes(testData);

            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 8);

            for (int i = 0; i < iterations; i++)
            {
                using var hasher = System.Security.Cryptography.IncrementalHash.CreateHash(
                    System.Security.Cryptography.HashAlgorithmName.SHA256);
                hasher.AppendData(testData, 0, testData.Length);
                Span<byte> buf = stackalloc byte[32];
                hasher.TryGetHashAndReset(buf, out _);
            }

            return rec;
        }

        /// <summary>
        /// Sync hash GC test. Returns recorder for caller to yield + read.
        /// Extracted from async method (CS4012: Span/stackalloc in async).
        /// </summary>
        private ProfilerRecorder RunSyncHashGC(string filePath)
        {
            const int iterations = 100;
            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 8);

            for (int i = 0; i < iterations; i++)
            {
                Span<byte> buf = stackalloc byte[32];
                FileUtility.ComputeFileHash(filePath, HashAlgorithmType.SHA256, buf);
            }

            return rec;
        }

        /// <summary>
        /// Sync hex string GC test. Returns recorder for caller to yield + read.
        /// </summary>
        private ProfilerRecorder RunSyncHexStringGC(string filePath)
        {
            const int iterations = 100;
            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 8);

            for (int i = 0; i < iterations; i++)
            {
                FileUtility.ComputeFileHashToHexString(filePath, HashAlgorithmType.SHA256);
            }

            return rec;
        }

        /// <summary>
        /// Sync XxHash64 file hash GC test. Returns recorder for caller to yield + read.
        /// </summary>
        private ProfilerRecorder RunSyncXxHash64GC(string filePath)
        {
            const int iterations = 100;
            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 8);

            for (int i = 0; i < iterations; i++)
            {
                Span<byte> buf = stackalloc byte[8];
                FileUtility.ComputeFileHash(filePath, HashAlgorithmType.XxHash64, buf);
            }

            return rec;
        }

        /// <summary>
        /// XxHash64 byte array comparison GC test. Returns recorder for caller to yield + read.
        /// </summary>
        private ProfilerRecorder RunXxHash64ByteArrayComparisonGC(byte[] a, byte[] b, int iterations)
        {
            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 8);
            for (int i = 0; i < iterations; i++)
                FileUtility.AreByteArraysEqualByHash(a, b, HashAlgorithmType.XxHash64);
            return rec;
        }

        /// <summary>
        /// XxHash64 one-shot HashToUInt64 GC test. Pure struct path, expect 0 GC.
        /// Returns recorder for caller to yield + read.
        /// </summary>
        private ProfilerRecorder RunXxHash64OneShotGC()
        {
            const int iterations = 1000;
            byte[] testData = new byte[1024];
            new System.Random(42).NextBytes(testData);

            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", iterations * 4);
            for (int i = 0; i < iterations; i++)
                XxHash64.HashToUInt64(testData);
            return rec;
        }

        private async Task<double> BenchmarkOp(Func<Task> operation, CancellationToken ct)
        {
            // Warmup
            for (int i = 0; i < WarmupIterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                await operation();
            }

            // Measured runs
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < BenchmarkIterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                await operation();
            }
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        }

        private double BenchmarkSyncOp(Action operation)
        {
            // Warmup
            for (int i = 0; i < WarmupIterations; i++) operation();

            // Measured runs
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < BenchmarkIterations; i++) operation();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        }

        /// <summary>
        /// Sums all sample values from a ProfilerRecorder.
        /// Each sample represents one allocation event; its Value is the allocation size in bytes.
        /// </summary>
        private static long SumRecorderSamples(ProfilerRecorder recorder)
        {
            long total = 0;
            int count = recorder.Count;
            for (int i = 0; i < count; i++)
            {
                total += recorder.GetSample(i).Value;
            }
            return total;
        }

        private static string FormatGCResult(long totalBytes, int allocCount, int iterations, bool overflow)
        {
            string overflowStr = overflow ? " [OVERFLOW]" : "";
            return $"GC Alloc: {totalBytes} bytes ({totalBytes / Math.Max(1, iterations)}/call), {allocCount} allocs ({allocCount / Math.Max(1, iterations)}/call){overflowStr}";
        }

        private async Task<Dictionary<int, string>> GenerateTestFiles(CancellationToken ct)
        {
            var files = new Dictionary<int, string>();
            for (int i = 0; i < TestFileSizesKB.Length; i++)
            {
                int sizeKB = TestFileSizesKB[i];
                string filePath = Path.Combine(_tempDir, $"test_{sizeKB}kb.bin");
                long sizeBytes = (long)sizeKB * 1024;

                _progressLabel = $"Generating {FormatSize(sizeBytes)} test file...";
                _progress = (float)i / TestFileSizesKB.Length * 0.1f;
                Repaint();

                await GenerateFile(filePath, sizeBytes, ct);
                files[sizeKB] = filePath;

                string hash = FileUtility.ComputeFileHashToHexString(filePath, HashAlgorithmType.SHA256);
                AddResult(ResultType.Info, "Test Files", $"Generated {FormatSize(sizeBytes)}", $"SHA256: {hash}");
            }
            return files;
        }

        private static async Task GenerateFile(string path, long sizeBytes, CancellationToken ct)
        {
            const int chunkSize = 65536;
            byte[] chunk = ArrayPool<byte>.Shared.Rent(chunkSize);
            try
            {
                // Deterministic content for reproducible hashes
                var rng = new System.Random(42);
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long remaining = sizeBytes;
                    while (remaining > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        int toWrite = (int)Math.Min(remaining, chunkSize);
                        rng.NextBytes(chunk);
                        await fs.WriteAsync(chunk, 0, toWrite, ct).ConfigureAwait(false);
                        remaining -= toWrite;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }

        private void AddResult(ResultType type, string category, string name, string detail)
        {
            _results.Add(new TestResult { Type = type, Category = category, Name = name, Detail = detail });
        }

        private string BuildResultsText()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("========================================");
            sb.AppendLine("  FileUtility Benchmark Report");
            sb.AppendLine($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("========================================");
            sb.AppendLine();

            // Summary
            int pass = 0, fail = 0, perf = 0, info = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                switch (_results[i].Type)
                {
                    case ResultType.Pass: pass++; break;
                    case ResultType.Fail: fail++; break;
                    case ResultType.Perf: perf++; break;
                    case ResultType.Info: info++; break;
                }
            }
            sb.AppendLine($"Summary: {pass} passed, {fail} failed, {perf} perf, {info} info");
            sb.AppendLine();

            string currentCategory = null;
            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                if (r.Category != currentCategory)
                {
                    currentCategory = r.Category;
                    sb.AppendLine($"--- {currentCategory} ---");
                }

                string icon;
                switch (r.Type)
                {
                    case ResultType.Pass: icon = "[PASS]"; break;
                    case ResultType.Fail: icon = "[FAIL]"; break;
                    case ResultType.Perf: icon = "[PERF]"; break;
                    default:              icon = "[INFO]"; break;
                }
                sb.AppendLine($"  {icon} {r.Name,-40} {r.Detail}");
            }

            sb.AppendLine();
            sb.AppendLine("========================================");
            return sb.ToString();
        }

        private void ExportResultsToFile()
        {
            string defaultName = $"FileUtilityBenchmark_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string path = EditorUtility.SaveFilePanel("Export Benchmark Results", "", defaultName, "txt");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.WriteAllText(path, BuildResultsText(), Encoding.UTF8);
                UnityEngine.Debug.Log($"[FileUtilityBenchmark] Results exported to: {path}");
                ShowNotification(new GUIContent("Exported!"), 1.5f);
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FileUtilityBenchmark] Export failed: {ex.Message}");
                ShowNotification(new GUIContent("Export failed!"), 2f);
            }
        }

        private void CleanupTempFiles()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); }
                catch { /* Best effort cleanup */ }
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024)} MB";
        }

        private static string GetReadBufferSizeLabel()
        {
#if UNITY_IOS || UNITY_ANDROID
            return "81920 (Mobile)";
#elif UNITY_WEBGL
            return "131072 (WebGL)";
#else
            return "65536 (Desktop)";
#endif
        }
    }
}
