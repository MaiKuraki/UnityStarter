using System;
using System.Diagnostics;
using System.Text;
using CycloneGames.Cheat.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

namespace CycloneGames.Cheat.Sample
{
    /// <summary>
    /// Performance benchmark for cheat command publishing overhead.
    /// </summary>
    public sealed class CheatSampleBenchmark : MonoBehaviour
    {
        public const string BENCHMARK_COMMAND = "Benchmark_Struct";
        public const string WARMUP_COMMAND = "Benchmark_Warmup";

        private const float PanelPadding = 12f;
        private const float MinPanelWidth = 360f;
        private const float MaxPanelWidth = 640f;
        private const float PanelWidthRatio = 0.42f;
        private const float MinPanelHeight = 260f;
        private const float PanelHeightRatio = 0.9f;
        private const string Separator = "============================================================";

        [SerializeField, Min(0)] private int WarmupIterations = 1_000;
        [SerializeField, Min(1)] private int BenchmarkIterations = 100_000;
        [SerializeField] private bool LogResultToConsole = true;

        private readonly StringBuilder _resultBuilder = new StringBuilder(1024);
        private bool _isBenchmarking;
        private string _lastResult = "";
        private GUIStyle _resultStyle;
        private Vector2 _scrollPosition;
        private ICheatCommandRuntime _runtime;

        private void Awake()
        {
            _runtime = new CheatCommandRuntime(new UnityDebugCheatLogger());
        }

        private void OnDestroy()
        {
            _runtime?.Dispose();
        }

        private void OnGUI()
        {
            _resultStyle ??= new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 20,
                wordWrap = true
            };

            GUI.color = Color.yellow;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.8f);

            Rect panelRect = GetResultPanelRect();
            GUILayout.BeginArea(panelRect, GUI.skin.box);
            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                false,
                true,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            GUILayout.TextArea(
                string.IsNullOrEmpty(_lastResult)
                    ? "Click 'Run Benchmark' button to start performance test."
                    : _lastResult,
                _resultStyle,
                GUILayout.ExpandWidth(true));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public async UniTaskVoid RunBenchmark()
        {
            if (_isBenchmarking)
            {
                return;
            }

            _isBenchmarking = true;
            _lastResult = "Benchmark Running...\nPlease wait...";

            try
            {
                await RunBenchmarkCore();
            }
            catch (Exception exception)
            {
                _lastResult = string.Concat("Benchmark failed:\n", exception);
                UnityEngine.Debug.LogException(exception);
            }
            finally
            {
                _isBenchmarking = false;
            }
        }

        private async UniTask RunBenchmarkCore()
        {
            int warmupIterations = Math.Max(0, WarmupIterations);
            int benchmarkIterations = Math.Max(1, BenchmarkIterations);
            var gameData = new GameData(Vector3.one, Vector3.up);

            for (int i = 0; i < warmupIterations; i++)
            {
                await _runtime.PublishAsync(WARMUP_COMMAND, gameData);
            }

            await UniTask.DelayFrame(2);

            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true);

            var startMetrics = _runtime.Metrics;
            long startMonoUsedBytes = Profiler.GetMonoUsedSizeLong();
            long startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            int startGen0 = GC.CollectionCount(0);
            int startGen1 = GC.CollectionCount(1);
            int startGen2 = GC.CollectionCount(2);
            long startTimestamp = Stopwatch.GetTimestamp();

            for (int i = 0; i < benchmarkIterations; i++)
            {
                await _runtime.PublishAsync(BENCHMARK_COMMAND, gameData);
            }

            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            long endMonoUsedBytes = Profiler.GetMonoUsedSizeLong();
            int endGen0 = GC.CollectionCount(0);
            int endGen1 = GC.CollectionCount(1);
            int endGen2 = GC.CollectionCount(2);
            var endMetrics = _runtime.Metrics;

            double elapsedMs = ((endTimestamp - startTimestamp) * 1000.0) / Stopwatch.Frequency;
            double avgTimeUs = (elapsedMs / benchmarkIterations) * 1000.0;
            double elapsedSeconds = elapsedMs / 1000.0;
            double throughput = elapsedSeconds > 0.0 ? benchmarkIterations / elapsedSeconds : 0.0;
            long allocatedBytes = endAllocatedBytes - startAllocatedBytes;
            long monoUsedDeltaBytes = endMonoUsedBytes - startMonoUsedBytes;

            StringBuilder sb = _resultBuilder;
            sb.Clear();
            sb.AppendLine(Separator);
            sb.AppendLine("  Cheat Command System Benchmark");
            sb.AppendLine(Separator);
            sb.AppendLine($"Runtime Enabled:   {(_runtime.IsEnabled ? "Yes" : "No (disabled no-op)")}");
            sb.AppendLine("Measurement:       Awaited serial publish completion");
            sb.AppendLine($"Warmup Iterations: {warmupIterations:N0}");
            sb.AppendLine($"Iterations:        {benchmarkIterations:N0}");
            sb.AppendLine($"Total Time:        {elapsedMs:F2} ms");
            sb.AppendLine($"Avg Time/Cmd:      {avgTimeUs:F4} us");
            sb.AppendLine($"Throughput:        {throughput:F0} cmd/s");
            sb.AppendLine($"Managed Alloc:     {allocatedBytes / 1024.0:F2} KB");
            sb.AppendLine($"Alloc per Command: {allocatedBytes / (double)benchmarkIterations:F2} bytes");
            sb.AppendLine($"Mono Used Delta:   {monoUsedDeltaBytes / 1024.0:F2} KB");
            sb.AppendLine($"GC Collections:    Gen0 {endGen0 - startGen0}, Gen1 {endGen1 - startGen1}, Gen2 {endGen2 - startGen2}");
            sb.AppendLine($"Published Delta:   {endMetrics.PublishedCommandCount - startMetrics.PublishedCommandCount:N0}");
            sb.AppendLine($"Completed Delta:   {endMetrics.CompletedCommandCount - startMetrics.CompletedCommandCount:N0}");
            sb.AppendLine($"Dropped Delta:     {endMetrics.DroppedDuplicateCount - startMetrics.DroppedDuplicateCount:N0}");
            sb.AppendLine($"Faulted Delta:     {endMetrics.FaultedCommandCount - startMetrics.FaultedCommandCount:N0}");
            if (!_runtime.IsEnabled)
            {
                sb.AppendLine("Note:              ENABLE_CHEAT is not defined; this result measures disabled no-op overhead.");
            }
            sb.AppendLine(Separator);
            sb.AppendLine($"Platform:          {Application.platform}");
            sb.AppendLine($"Unity Version:     {Application.unityVersion}");
            sb.AppendLine(Separator);

            _lastResult = sb.ToString();
            if (LogResultToConsole)
            {
                UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", _lastResult);
            }
        }

        private static Rect GetResultPanelRect()
        {
            float width = Mathf.Clamp(Screen.width * PanelWidthRatio, MinPanelWidth, MaxPanelWidth);
            float height = Mathf.Max(MinPanelHeight, Screen.height * PanelHeightRatio);
            height = Mathf.Min(height, Screen.height - (PanelPadding * 2f));
            return new Rect(PanelPadding, PanelPadding, width, height);
        }
    }
}
