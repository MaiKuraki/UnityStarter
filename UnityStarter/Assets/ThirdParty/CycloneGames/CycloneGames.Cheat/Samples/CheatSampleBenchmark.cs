using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using System.Text;
using CycloneGames.Cheat.Runtime;
using System;

namespace CycloneGames.Cheat.Sample
{
    /// <summary>
    /// Performance benchmark for cheat command system. Tests GC allocation and execution time.
    /// </summary>
    public class CheatSampleBenchmark : MonoBehaviour
    {
        private const int WarmupIterations = 100;
        private const int Iterations = 100_000;
        private const int BatchSize = 1000;

        private bool isBenchmarking = false;
        private string lastResult = "";
        private GUIStyle resultStyle;

        public const string BENCHMARK_COMMAND = "Benchmark_Struct";
        public const string WARMUP_COMMAND = "Benchmark_Warmup";

        private void OnGUI()
        {
            if (resultStyle == null)
            {
                resultStyle = new GUIStyle(GUI.skin.textArea)
                {
                    fontSize = 20,
                    wordWrap = true
                };
            }
            
            GUI.color = Color.yellow;
            GUI.backgroundColor = new Color(0, 0, 0, 0.8f);

            GUILayout.BeginArea(new Rect(10, 10, 600, 500), GUI.skin.box);
            
            if (!string.IsNullOrEmpty(lastResult))
            {
                GUILayout.TextArea(lastResult, resultStyle);
            }
            else
            {
                GUILayout.Label("Click 'Run Benchmark' button to start performance test.", resultStyle);
            }

            GUILayout.EndArea();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async UniTaskVoid RunBenchmark()
        {
            if (isBenchmarking) return;
            isBenchmarking = true;
            lastResult = "Benchmark Running...\nPlease wait...";

            var stopwatch = new Stopwatch();
            var gameData = new GameData(Vector3.one, Vector3.up);

            // Warm-up phase to JIT compile and warm caches
            for (int i = 0; i < WarmupIterations; i++)
            {
                await CheatCommandUtility.PublishCheatCommand(WARMUP_COMMAND, gameData);
            }
            
            // Wait for all warmup commands to complete
            await UniTask.DelayFrame(2);

            // Force GC before measurement
            System.GC.Collect(2, GCCollectionMode.Forced, true);
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect(2, GCCollectionMode.Forced, true);
            
            long startMemory = Profiler.GetMonoUsedSizeLong();
            long startGC = Profiler.GetTotalAllocatedMemoryLong();
            stopwatch.Start();

            // Publish commands in batches to avoid overwhelming the system
            int completed = 0;
            for (int batch = 0; batch < Iterations / BatchSize; batch++)
            {
                for (int i = 0; i < BatchSize; i++)
                {
                    CheatCommandUtility.PublishCheatCommand(BENCHMARK_COMMAND, gameData).Forget();
                }
                
                // Yield periodically to allow processing
                if (batch % 10 == 0)
                {
                    await UniTask.Yield();
                }
                completed += BatchSize;
            }

            // Wait for commands to complete
            await UniTask.DelayFrame(5);
            
            stopwatch.Stop();
            
            long endMemory = Profiler.GetMonoUsedSizeLong();
            long endGC = Profiler.GetTotalAllocatedMemoryLong();
            long totalMemory = endMemory - startMemory;
            long totalGC = endGC - startGC;
            
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            double avgTimeUs = (elapsedMs / Iterations) * 1000.0;
            double throughput = Iterations / (elapsedMs / 1000.0);

            var sb = new StringBuilder(256);
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("  Cheat Command System Benchmark");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Iterations:        {Iterations:N0}");
            sb.AppendLine($"Total Time:        {elapsedMs:F2} ms");
            sb.AppendLine($"Avg Time/Cmd:      {avgTimeUs:F4} µs");
            sb.AppendLine($"Throughput:        {throughput:F0} cmd/s");
            sb.AppendLine($"Mono Memory:       {totalMemory / 1024.0:F2} KB");
            sb.AppendLine($"GC Allocation:     {totalGC / 1024.0:F2} KB");
            sb.AppendLine($"GC per Command:    {(totalGC / (double)Iterations):F2} bytes");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Platform:          {Application.platform}");
            sb.AppendLine($"Unity Version:     {Application.unityVersion}");
            sb.AppendLine("═══════════════════════════════════════");
            
            lastResult = sb.ToString();
            UnityEngine.Debug.Log(lastResult);

            isBenchmarking = false;
        }
    }
}
