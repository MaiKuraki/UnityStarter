using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using System.Text;

namespace CycloneGames.Cheat.Sample
{
    public class CheatSampleBenchmark : MonoBehaviour
    {
        private const int Iterations = 100_000;
        private bool isBenchmarking = false;
        private string lastResult = "";
        private GUIStyle resultStyle;

        public const string BENCHMARK_COMMAND = "Benchmark_Struct";

        private void OnGUI()
        {
            // Lazily initialize the style inside OnGUI, where it's safe to access GUI.skin.
            if (resultStyle == null)
            {
                resultStyle = new GUIStyle(GUI.skin.textArea)
                {
                    fontSize = 24 // Use a fixed, large font size for clarity.
                };
            }
            
            GUI.color = Color.yellow;
            GUI.backgroundColor = Color.black;

            // Use a larger area to be safe
            GUILayout.BeginArea(new Rect(10, 10, 500, 400), GUI.skin.box);

            // This single TextArea will now display both the "Running..." state and the final result.
            if (!string.IsNullOrEmpty(lastResult))
            {
                GUILayout.TextArea(lastResult, resultStyle);
            }

            GUILayout.EndArea();
        }

        public async UniTaskVoid RunBenchmark()
        {
            if (isBenchmarking) return;
            isBenchmarking = true;
            lastResult = "Benchmark Running...";

            var stopwatch = new Stopwatch();
            var gameData = new GameData(Vector3.one, Vector3.up);

            // --- Warm-up phase ---
            for (int i = 0; i < 100; i++)
            {
                await CheatCommandUtility.PublishCheatCommand("Benchmark_Warmup", gameData);
            }
            await UniTask.Yield();

            // --- Measurement phase ---
            // Force GC collection before measurement to get a cleaner baseline.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            
            long startMemory = Profiler.GetMonoUsedSizeLong();
            stopwatch.Start();

            for (int i = 0; i < Iterations; i++)
            {
                CheatCommandUtility.PublishCheatCommand(BENCHMARK_COMMAND, gameData).Forget();
            }
            
            await UniTask.Yield(); // Let the main thread process some commands

            stopwatch.Stop();
            
            // Force GC collection again to measure the memory that remains after the test.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            
            long endMemory = Profiler.GetMonoUsedSizeLong();
            long totalMemory = endMemory - startMemory;
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            double avgTime = elapsedMs / Iterations;

            var sb = new StringBuilder();
            sb.AppendLine("[Benchmark] Finished.");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"Total Time: {elapsedMs:F2} ms");
            sb.AppendLine($"Iterations: {Iterations}");
            sb.AppendLine($"Avg Time/Cmd: {avgTime * 1000:F4} µs");
            sb.AppendLine($"GC Allocation: {totalMemory / 1024.0f:F2} KB");
            sb.AppendLine("--------------------------------------------------");
            lastResult = sb.ToString();
            
            // Log to console once at the end
            UnityEngine.Debug.Log(lastResult);

            isBenchmarking = false;
        }
    }
}
