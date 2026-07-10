using System;
using System.Diagnostics;
using System.IO;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.IO.Editor
{
    public sealed class FileIOBenchmarkWindow : EditorWindow
    {
        private const int MIN_PAYLOAD_MIB = 1;
        private const int MAX_PAYLOAD_MIB = 1024;
        private const int MIN_ITERATIONS = 1;
        private const int MAX_ITERATIONS = 100;

        [SerializeField] private int PayloadSizeMiB = 16;
        [SerializeField] private int Iterations = 5;

        private Vector2 _scrollPosition;
        private string _results = "No benchmark has been run.";

        [MenuItem("Window/CycloneGames/IO Benchmark")]
        public static void ShowWindow()
        {
            GetWindow<FileIOBenchmarkWindow>("IO Benchmark");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("System IO Benchmark", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Results describe this machine and current filesystem cache state. They are diagnostic measurements, not portable pass/fail thresholds.",
                MessageType.Info);

            PayloadSizeMiB = EditorGUILayout.IntSlider(
                "Payload (MiB)",
                PayloadSizeMiB,
                MIN_PAYLOAD_MIB,
                MAX_PAYLOAD_MIB);
            Iterations = EditorGUILayout.IntSlider(
                "Iterations",
                Iterations,
                MIN_ITERATIONS,
                MAX_ITERATIONS);

            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                if (GUILayout.Button("Run Benchmark"))
                {
                    RunBenchmark();
                }
            }

            EditorGUILayout.Space();
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.TextArea(_results, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RunBenchmark()
        {
            string directoryPath = Path.Combine(
                Application.temporaryCachePath,
                "CycloneGames.IO.Benchmark",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryPath);

            try
            {
                int byteCount = checked(PayloadSizeMiB * 1024 * 1024);
                byte[] first = CreatePayload(byteCount);
                byte[] second = (byte[])first.Clone();
                string firstPath = Path.Combine(directoryPath, "first.bin");
                string secondPath = Path.Combine(directoryPath, "second.bin");
                SystemFileStore.Default.WriteBytesAtomically(firstPath, first);
                SystemFileStore.Default.WriteBytesAtomically(secondPath, second);

                var results = new StringBuilder(512);
                results.AppendLine($"Payload: {PayloadSizeMiB} MiB");
                results.AppendLine($"Iterations: {Iterations}");
                results.AppendLine();

                AppendMeasurement(
                    results,
                    "Atomic replace",
                    byteCount,
                    Iterations,
                    () => SystemFileStore.Default.WriteBytesAtomically(firstPath, first));
                AppendMeasurement(
                    results,
                    "Exact memory comparison",
                    byteCount,
                    Iterations,
                    () => Consume(BinaryContentComparer.AreEqual(first, second)));
                AppendMeasurement(
                    results,
                    "SHA-256 file hash",
                    byteCount,
                    Iterations,
                    () => Consume(FileHasher.ComputeHex(firstPath, FileHashAlgorithm.Sha256)));
                AppendMeasurement(
                    results,
                    "xxHash64 file hash",
                    byteCount,
                    Iterations,
                    () => Consume(FileHasher.ComputeHex(firstPath, FileHashAlgorithm.XxHash64)));

                _results = results.ToString();
            }
            catch (Exception exception)
            {
                _results = $"Benchmark failed: {exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                try
                {
                    Directory.Delete(directoryPath, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static void AppendMeasurement(
            StringBuilder results,
            string name,
            int byteCount,
            int iterations,
            Action operation)
        {
            operation();
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }

            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon);
            double processedMiB = ((double)byteCount * iterations) / (1024.0 * 1024.0);
            results.Append(name);
            results.Append(": ");
            results.Append((processedMiB / seconds).ToString("F2"));
            results.Append(" MiB/s, ");
            results.Append((allocatedBytes / (double)iterations).ToString("F0"));
            results.AppendLine(" B allocated/iteration");
        }

        private static byte[] CreatePayload(int length)
        {
            var payload = new byte[length];
            uint state = 0x9E3779B9U;
            for (int i = 0; i < payload.Length; i++)
            {
                state = unchecked((state * 1664525U) + 1013904223U);
                payload[i] = (byte)(state >> 24);
            }

            return payload;
        }

        private static void Consume(bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException("Benchmark comparison unexpectedly failed.");
            }
        }

        private static void Consume(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException("Benchmark hash unexpectedly failed.");
            }
        }
    }
}
