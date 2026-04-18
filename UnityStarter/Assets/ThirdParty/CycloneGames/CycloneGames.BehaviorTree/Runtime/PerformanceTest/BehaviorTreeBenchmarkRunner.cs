using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.PerformanceTest
{
    public class BehaviorTreeBenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private bool _autoRunOnStart = true;
        [SerializeField] private BehaviorTreeBenchmarkRunnerMode _runnerMode = BehaviorTreeBenchmarkRunnerMode.Single;
        [SerializeField] private BehaviorTreeBenchmarkConfig _config = new BehaviorTreeBenchmarkConfig();
        [SerializeField] private bool _autoExportCsv = true;
        [SerializeField] private bool _autoExportJson = true;
        [SerializeField] private string _exportFolderName = "BehaviorTreeBenchmarkResults";
        [SerializeField] private string _matrixBatchName = "Recommended Matrix";

        private Coroutine _runCoroutine;
        private BehaviorTreeBenchmarkResult _currentRunResult;

        public event Action<BehaviorTreeBenchmarkResult> BenchmarkCompleted;
        public event Action<BehaviorTreeBenchmarkBatchResult> BenchmarkMatrixCompleted;

        public bool AutoRunOnStart
        {
            get => _autoRunOnStart;
            set => _autoRunOnStart = value;
        }

        public BehaviorTreeBenchmarkRunnerMode RunnerMode
        {
            get => _runnerMode;
            set => _runnerMode = value;
        }

        public BehaviorTreeBenchmarkConfig Config => _config;
        public bool IsRunning { get; private set; }
        public bool HasCompleted { get; private set; }
        public BehaviorTreeBenchmarkResult LastResult { get; private set; }
        public BehaviorTreeBenchmarkBatchResult LastBatchResult { get; private set; }
        public string LastExportPath { get; private set; }

        private void Start()
        {
            if (_autoRunOnStart)
            {
                BeginBenchmark();
            }
        }

        public void SetConfig(BehaviorTreeBenchmarkConfig config)
        {
            _config = config?.Clone() ?? new BehaviorTreeBenchmarkConfig();
            _config.Sanitize();
        }

        [ContextMenu("Run Benchmark")]
        public void BeginBenchmark()
        {
            if (IsRunning)
            {
                return;
            }

            if (_runCoroutine != null)
            {
                StopCoroutine(_runCoroutine);
            }

            _runCoroutine = StartCoroutine(RunBenchmarkCoroutine());
        }

        private IEnumerator RunBenchmarkCoroutine()
        {
            IsRunning = true;
            HasCompleted = false;
            LastResult = null;
            LastBatchResult = null;
            LastExportPath = null;
            _currentRunResult = null;

            switch (_runnerMode)
            {
                case BehaviorTreeBenchmarkRunnerMode.RecommendedMatrix:
                    yield return RunScaleMatrixCoroutine(_config.Complexity, _matrixBatchName, assignLastResult: true);
                    break;
                case BehaviorTreeBenchmarkRunnerMode.FullMatrix:
                    yield return RunFullMatrixCoroutine();
                    break;
                case BehaviorTreeBenchmarkRunnerMode.PriorityComparison:
                    yield return RunPriorityComparisonCoroutine();
                    break;
                default:
                    yield return RunSingleBenchmarkCoroutine(_config.Clone(), assignAsLastResult: true);
                    ExportSingleResultIfNeeded();
                    break;
            }

            HasCompleted = true;
            IsRunning = false;
            _runCoroutine = null;
        }

        private IEnumerator RunSingleBenchmarkCoroutine(BehaviorTreeBenchmarkConfig config, bool assignAsLastResult)
        {
            using var session = new BehaviorTreeBenchmarkSession(config);
            session.Setup();

            for (int i = 0; i < session.Config.WarmupFrames; i++)
            {
                session.RunWarmupFrame();
                yield return null;
            }

            for (int i = 0; i < session.Config.MeasurementFrames; i++)
            {
                session.RunMeasuredFrame();
                yield return null;
            }

            for (int i = 0; i < session.Config.SoakFrames; i++)
            {
                session.RunSoakFrame();
                yield return null;
            }

            var result = session.Complete();
            _currentRunResult = result;
            if (assignAsLastResult)
            {
                LastResult = result;
            }

            BenchmarkCompleted?.Invoke(result);
        }

        private IEnumerator RunScaleMatrixCoroutine(BehaviorTreeBenchmarkComplexity complexity, string batchName, bool assignLastResult)
        {
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var results = new BehaviorTreeBenchmarkResult[presets.Length];

            for (int i = 0; i < presets.Length; i++)
            {
                var config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(presets[i], complexity);
                yield return RunSingleBenchmarkCoroutine(config, assignAsLastResult: false);
                results[i] = _currentRunResult;
                yield return null;
            }

            LastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                BatchName = string.IsNullOrWhiteSpace(batchName) ? $"Scale Matrix [{complexity}]" : batchName,
                Results = results
            };

            if (assignLastResult && results.Length > 0)
            {
                LastResult = results[results.Length - 1];
            }

            ExportBatchResultIfNeeded();
            BenchmarkMatrixCompleted?.Invoke(LastBatchResult);
        }

        private IEnumerator RunFullMatrixCoroutine()
        {
            var complexities = BehaviorTreeBenchmarkPresetCatalog.GetComplexityTiers();
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var results = new BehaviorTreeBenchmarkResult[complexities.Length * presets.Length];
            int resultIndex = 0;

            for (int complexityIndex = 0; complexityIndex < complexities.Length; complexityIndex++)
            {
                var complexity = complexities[complexityIndex];
                for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
                {
                    var config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(presets[presetIndex], complexity);
                    yield return RunSingleBenchmarkCoroutine(config, assignAsLastResult: false);
                    results[resultIndex++] = _currentRunResult;
                    yield return null;
                }
            }

            LastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                BatchName = "Full Matrix",
                Results = results
            };

            if (results.Length > 0)
            {
                LastResult = results[results.Length - 1];
            }

            ExportBatchResultIfNeeded();
            BenchmarkMatrixCompleted?.Invoke(LastBatchResult);
        }

        private IEnumerator RunPriorityComparisonCoroutine()
        {
            var profiles = new[]
            {
                BehaviorTreeBenchmarkSchedulingProfile.FullRate,
                BehaviorTreeBenchmarkSchedulingProfile.PriorityLod,
                BehaviorTreeBenchmarkSchedulingProfile.PriorityManaged,
                BehaviorTreeBenchmarkSchedulingProfile.UltraLod
            };

            var results = new BehaviorTreeBenchmarkResult[profiles.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                var config = _config.Clone();
                BehaviorTreeBenchmarkPresetCatalog.ApplySchedulingProfile(config, profiles[i]);
                config.BenchmarkName = $"{config.BenchmarkName} [{profiles[i]}]";
                config.Sanitize();
                yield return RunSingleBenchmarkCoroutine(config, assignAsLastResult: false);
                results[i] = _currentRunResult;
                yield return null;
            }

            LastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                BatchName = string.IsNullOrWhiteSpace(_matrixBatchName) ? "Priority Comparison" : _matrixBatchName,
                Results = results
            };

            if (results.Length > 0)
            {
                LastResult = results[results.Length - 1];
            }

            ExportBatchResultIfNeeded();
            BenchmarkMatrixCompleted?.Invoke(LastBatchResult);
        }

        private void ExportSingleResultIfNeeded()
        {
            if (LastResult == null || !LastResult.IsValid || (!_autoExportCsv && !_autoExportJson))
            {
                return;
            }

            LastExportPath = BehaviorTreeBenchmarkExportUtility.WriteResultFiles(GetExportDirectoryPath(), LastResult, _autoExportCsv, _autoExportJson);
            if (!string.IsNullOrEmpty(LastExportPath))
            {
                Debug.Log($"[BehaviorTreeBenchmarkRunner] Exported benchmark result to: {LastExportPath}");
            }
        }

        private void ExportBatchResultIfNeeded()
        {
            if (LastBatchResult == null || LastBatchResult.Results == null || LastBatchResult.Results.Length == 0 || (!_autoExportCsv && !_autoExportJson))
            {
                return;
            }

            LastExportPath = BehaviorTreeBenchmarkExportUtility.WriteBatchFiles(GetExportDirectoryPath(), LastBatchResult, _autoExportCsv, _autoExportJson);
            if (!string.IsNullOrEmpty(LastExportPath))
            {
                Debug.Log($"[BehaviorTreeBenchmarkRunner] Exported benchmark matrix to: {LastExportPath}");
            }
        }

        private string GetExportDirectoryPath()
        {
            string folderName = string.IsNullOrWhiteSpace(_exportFolderName) ? "BehaviorTreeBenchmarkResults" : _exportFolderName.Trim();
            return Path.Combine(Application.persistentDataPath, folderName);
        }
    }
}
