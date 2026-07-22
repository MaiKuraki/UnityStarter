using System.IO;
using CycloneGames.BehaviorTree.Runtime.PerformanceTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeBenchmarkWindow : EditorWindow
    {
        private const int MaxAgentCount = 100000;
        private const int MaxLeafNodesPerTree = 512;
        private const int MaxBlackboardOperationsPerLeaf = 256;
        private const int MaxDecoratorLayersPerLeaf = 64;
        private const int MaxSimulatedWorkIterationsPerLeaf = 100000;
        private const int MaxTrackedKeysPerAgent = 8192;
        private const int MaxFrameCount = 1000000;
        private const int MaxTicksPerFrame = 64;
        private const long MaxTotalRuntimeNodes = 2000000L;
        private const long MaxTotalTrackedKeys = 20000000L;
        private const long MaxWorkUnitsPerFrame = 25000000L;
        private const long MaxTotalWorkUnits = 1000000000L;

        private readonly string[] _presetLabels = System.Enum.GetNames(typeof(BehaviorTreeBenchmarkPreset));
        private readonly string[] _complexityLabels = System.Enum.GetNames(typeof(BehaviorTreeBenchmarkComplexity));
        private readonly string[] _schedulingLabels = System.Enum.GetNames(typeof(BehaviorTreeBenchmarkSchedulingProfile));

        private BehaviorTreeBenchmarkConfig _config = new BehaviorTreeBenchmarkConfig();
        private BehaviorTreeBenchmarkResult _lastResult;
        private BehaviorTreeBenchmarkBatchResult _lastBatchResult;
        private string _lastExportPath;
        private Vector2 _scrollPosition;
        private GUIStyle _wordWrappedLabelStyle;

        [MenuItem("Tools/CycloneGames/Behavior Tree/Behavior Tree Benchmark")]
        public static void OpenWindow()
        {
            var window = GetWindow<BehaviorTreeBenchmarkWindow>();
            window.titleContent = new GUIContent("BT Benchmark");
            window.minSize = new Vector2(640f, 620f);
        }

        private void OnGUI()
        {
            bool createSceneRequested = false;
            bool createPresetSceneRequested = false;
            bool createScaleMatrixSceneRequested = false;
            bool createFullMatrixSceneRequested = false;
            bool createPriorityComparisonSceneRequested = false;
            bool createConfiguredBudgetSceneRequested = false;
            bool runScaleMatrixRequested = false;
            bool runFullMatrixRequested = false;
            bool runPriorityComparisonRequested = false;
            bool runConfiguredBudgetRequested = false;

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            bool previousWideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.wideMode = true;
            EditorGUIUtility.labelWidth = GetResponsiveLabelWidth(position.width);

            try
            {
                EditorGUILayout.LabelField("Benchmark Configuration", EditorStyles.boldLabel);

                var selectedPreset = (BehaviorTreeBenchmarkPreset)EditorGUILayout.Popup("Preset", (int)_config.Preset, _presetLabels);
                if (selectedPreset != _config.Preset)
                {
                    ApplyPreset(selectedPreset);
                }

                var selectedComplexity = (BehaviorTreeBenchmarkComplexity)EditorGUILayout.Popup("Complexity", (int)_config.Complexity, _complexityLabels);
                if (selectedComplexity != _config.Complexity)
                {
                    ApplyComplexity(selectedComplexity);
                }

                var selectedScheduling = (BehaviorTreeBenchmarkSchedulingProfile)EditorGUILayout.Popup("Scheduling", (int)_config.SchedulingProfile, _schedulingLabels);
                if (selectedScheduling != _config.SchedulingProfile)
                {
                    ApplyScheduling(selectedScheduling);
                }

                _config.BenchmarkName = EditorGUILayout.TextField("Benchmark Name", _config.BenchmarkName);
                _config.AgentCount = EditorGUILayout.IntField("Agent Count", _config.AgentCount);
                _config.LeafNodesPerTree = EditorGUILayout.IntField("Leaf Nodes Per Tree", _config.LeafNodesPerTree);
                _config.BlackboardReadsPerLeafPerTick = EditorGUILayout.IntField("Reads Per Leaf/Tick", _config.BlackboardReadsPerLeafPerTick);
                _config.WritesPerLeafPerTick = EditorGUILayout.IntField("Writes Per Leaf/Tick", _config.WritesPerLeafPerTick);
                _config.DecoratorLayersPerLeaf = EditorGUILayout.IntField("Decorator Layers", _config.DecoratorLayersPerLeaf);
                _config.SimulatedWorkIterationsPerLeaf = EditorGUILayout.IntField("Work Iterations/Leaf", _config.SimulatedWorkIterationsPerLeaf);
                _config.TrackedKeysPerAgent = EditorGUILayout.IntField("Tracked Keys/Agent", _config.TrackedKeysPerAgent);
                _config.WarmupFrames = EditorGUILayout.IntField("Warmup Frames", _config.WarmupFrames);
                _config.MeasurementFrames = EditorGUILayout.IntField("Measurement Frames", _config.MeasurementFrames);
                _config.TicksPerFrame = EditorGUILayout.IntField("Ticks Per Frame", _config.TicksPerFrame);
                _config.EnableDeltaFlush = EditorGUILayout.Toggle("Enable Delta Flush", _config.EnableDeltaFlush);
                _config.EnableDeterministicHashCheck = EditorGUILayout.Toggle("Deterministic Hash Check", _config.EnableDeterministicHashCheck);
                _config.HashCheckIntervalFrames = EditorGUILayout.IntField("Hash Check Interval", _config.HashCheckIntervalFrames);
                _config.SoakFrames = EditorGUILayout.IntField("Soak Frames", _config.SoakFrames);
                _config.SoakSampleIntervalFrames = EditorGUILayout.IntField("Soak Sample Interval", _config.SoakSampleIntervalFrames);
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Production Budgets", EditorStyles.boldLabel);
                _config.TargetAverageFrameMilliseconds = EditorGUILayout.DoubleField("Target Average Frame (ms)", _config.TargetAverageFrameMilliseconds);
                _config.TargetMaxFrameMilliseconds = EditorGUILayout.DoubleField("Target Max Frame (ms)", _config.TargetMaxFrameMilliseconds);
                _config.MaxManagedMemoryDeltaBytes = EditorGUILayout.LongField("Max Memory Delta (bytes)", _config.MaxManagedMemoryDeltaBytes);
                _config.MaxGcCollections = EditorGUILayout.IntField("Max GC Collections", _config.MaxGcCollections);
                _config.MinimumEffectiveTickRatio = EditorGUILayout.Slider("Min Effective Tick Ratio", (float)_config.MinimumEffectiveTickRatio, 0f, 1f);
                _config.Sanitize();

                bool requestIsValid = TryValidateRequest(_config, out string requestValidationError);
                if (!requestIsValid)
                {
                    EditorGUILayout.HelpBox(requestValidationError, MessageType.Error);
                }

                EditorGUILayout.LabelField("Memory Budget Display", FormatBytes(_config.MaxManagedMemoryDeltaBytes));
                if (requestIsValid)
                {
                    long recommendedMemoryBudgetBytes = BehaviorTreeBenchmarkPresetCatalog.EstimateManagedMemoryDeltaBudgetBytes(_config);
                    EditorGUILayout.LabelField("Recommended Memory Budget", FormatBytes(recommendedMemoryBudgetBytes));
                    if (GUILayout.Button("Apply Recommended Production Budgets", GUILayout.Height(24f)))
                    {
                        BehaviorTreeBenchmarkPresetCatalog.ApplyProductionBudgets(_config);
                    }
                }

                _config.Sanitize();

                EditorGUILayout.Space(12f);
                EditorGUILayout.LabelField("Single Run", EditorStyles.boldLabel);

                if (GUILayout.Button("Run Editor Benchmark", GUILayout.Height(28f)))
                {
                    RunEditorBenchmark();
                }

                if (GUILayout.Button("Create PlayMode Benchmark Scene", GUILayout.Height(28f)))
                {
                    createSceneRequested = true;
                }

                if (GUILayout.Button("Create Scene From Preset", GUILayout.Height(28f)))
                {
                    createPresetSceneRequested = true;
                }

                EditorGUILayout.Space(12f);
                EditorGUILayout.LabelField("Matrix Runs", EditorStyles.boldLabel);

                if (GUILayout.Button("Run Scale Matrix For Selected Complexity", GUILayout.Height(28f)))
                {
                    runScaleMatrixRequested = true;
                }

                if (GUILayout.Button("Run Full Matrix (Scale x Complexity)", GUILayout.Height(28f)))
                {
                    runFullMatrixRequested = true;
                }

                if (GUILayout.Button("Run PriorityManaged Comparison", GUILayout.Height(28f)))
                {
                    runPriorityComparisonRequested = true;
                }

                if (GUILayout.Button("Run Configured Budget Matrix", GUILayout.Height(28f)))
                {
                    runConfiguredBudgetRequested = true;
                }

                if (GUILayout.Button("Create Scale Matrix Scene", GUILayout.Height(28f)))
                {
                    createScaleMatrixSceneRequested = true;
                }

                if (GUILayout.Button("Create Full Matrix Scene", GUILayout.Height(28f)))
                {
                    createFullMatrixSceneRequested = true;
                }

                if (GUILayout.Button("Create PriorityManaged Comparison Scene", GUILayout.Height(28f)))
                {
                    createPriorityComparisonSceneRequested = true;
                }

                if (GUILayout.Button("Create Configured Budget Scene", GUILayout.Height(28f)))
                {
                    createConfiguredBudgetSceneRequested = true;
                }

                using (new EditorGUI.DisabledScope(_lastResult == null || !_lastResult.IsValid))
                {
                    if (GUILayout.Button("Export Last Result as CSV", GUILayout.Height(26f)))
                    {
                        ExportLastResult("csv");
                    }

                    if (GUILayout.Button("Export Last Result as JSON", GUILayout.Height(26f)))
                    {
                        ExportLastResult("json");
                    }

                    if (GUILayout.Button("Export Last Result to Default Folder", GUILayout.Height(26f)))
                    {
                        ExportLastResultToDefaultFolder();
                    }
                }

                using (new EditorGUI.DisabledScope(_lastBatchResult == null || _lastBatchResult.Results == null || _lastBatchResult.Results.Length == 0))
                {
                    if (GUILayout.Button("Export Last Matrix as CSV", GUILayout.Height(26f)))
                    {
                        ExportLastBatchResult("csv");
                    }

                    if (GUILayout.Button("Export Last Matrix as JSON", GUILayout.Height(26f)))
                    {
                        ExportLastBatchResult("json");
                    }

                    if (GUILayout.Button("Export Last Matrix to Default Folder", GUILayout.Height(26f)))
                    {
                        ExportLastBatchToDefaultFolder();
                    }
                }

                if (!string.IsNullOrEmpty(_lastExportPath))
                {
                    EditorGUILayout.Space(6f);
                    DrawWrappedMetric("Last Export", _lastExportPath);
                }

                if (_lastResult != null && _lastResult.IsValid)
                {
                    EditorGUILayout.Space(12f);
                    EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
                    DrawResultSummary(_lastResult);
                }

                if (_lastBatchResult != null && _lastBatchResult.Results != null && _lastBatchResult.Results.Length > 0)
                {
                    EditorGUILayout.Space(12f);
                    EditorGUILayout.LabelField("Last Matrix", EditorStyles.boldLabel);
                    DrawBatchSummary(_lastBatchResult);
                    for (int i = 0; i < _lastBatchResult.Results.Length; i++)
                    {
                        var result = _lastBatchResult.Results[i];
                        DrawMatrixResultRow(result);
                    }
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUIUtility.wideMode = previousWideMode;
                EditorGUILayout.EndScrollView();
            }

            if (createSceneRequested)
            {
                EditorApplication.delayCall += CreateBenchmarkScene;
                GUIUtility.ExitGUI();
            }

            if (createPresetSceneRequested)
            {
                EditorApplication.delayCall += CreatePresetBenchmarkScene;
                GUIUtility.ExitGUI();
            }

            if (createScaleMatrixSceneRequested)
            {
                EditorApplication.delayCall += CreateScaleMatrixScene;
                GUIUtility.ExitGUI();
            }

            if (createFullMatrixSceneRequested)
            {
                EditorApplication.delayCall += CreateFullMatrixScene;
                GUIUtility.ExitGUI();
            }

            if (createPriorityComparisonSceneRequested)
            {
                EditorApplication.delayCall += CreatePriorityComparisonScene;
                GUIUtility.ExitGUI();
            }

            if (createConfiguredBudgetSceneRequested)
            {
                EditorApplication.delayCall += CreateConfiguredBudgetScene;
                GUIUtility.ExitGUI();
            }

            if (runScaleMatrixRequested)
            {
                EditorApplication.delayCall += RunScaleMatrix;
                GUIUtility.ExitGUI();
            }

            if (runFullMatrixRequested)
            {
                EditorApplication.delayCall += RunFullMatrix;
                GUIUtility.ExitGUI();
            }

            if (runPriorityComparisonRequested)
            {
                EditorApplication.delayCall += RunPriorityComparison;
                GUIUtility.ExitGUI();
            }

            if (runConfiguredBudgetRequested)
            {
                EditorApplication.delayCall += RunConfiguredBudgetMatrix;
                GUIUtility.ExitGUI();
            }
        }

        private void RunEditorBenchmark()
        {
            if (!ValidateBeforeExecution(_config, "Run Editor Benchmark"))
            {
                return;
            }

            _lastResult = BehaviorTreeBenchmarkSession.RunImmediate(_config);
            _lastBatchResult = null;
            Repaint();
        }

        private void RunScaleMatrix()
        {
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var configs = new BehaviorTreeBenchmarkConfig[presets.Length];
            var results = new BehaviorTreeBenchmarkResult[presets.Length];

            for (int i = 0; i < presets.Length; i++)
            {
                configs[i] = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(presets[i], _config.Complexity);
            }

            if (!ValidateBeforeExecution(configs, "Run Scale Matrix"))
            {
                return;
            }

            for (int i = 0; i < configs.Length; i++)
            {
                results[i] = BehaviorTreeBenchmarkSession.RunImmediate(configs[i]);
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = $"Scale Matrix [{_config.Complexity}]",
                Results = results
            };
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(_lastBatchResult);

            if (results.Length > 0)
            {
                _lastResult = results[results.Length - 1];
            }

            Repaint();
        }

        private void RunFullMatrix()
        {
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var complexities = BehaviorTreeBenchmarkPresetCatalog.GetComplexityTiers();
            var configs = new BehaviorTreeBenchmarkConfig[presets.Length * complexities.Length];
            int resultIndex = 0;

            for (int complexityIndex = 0; complexityIndex < complexities.Length; complexityIndex++)
            {
                for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
                {
                    configs[resultIndex++] = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(
                        presets[presetIndex],
                        complexities[complexityIndex]);
                }
            }

            if (!ValidateBeforeExecution(configs, "Run Full Matrix"))
            {
                return;
            }

            var results = new BehaviorTreeBenchmarkResult[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                results[i] = BehaviorTreeBenchmarkSession.RunImmediate(configs[i]);
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = "Full Matrix",
                Results = results
            };
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(_lastBatchResult);

            if (results.Length > 0)
            {
                _lastResult = results[results.Length - 1];
            }

            Repaint();
        }

        private void RunPriorityComparison()
        {
            var profiles = new[]
            {
                BehaviorTreeBenchmarkSchedulingProfile.FullRate,
                BehaviorTreeBenchmarkSchedulingProfile.PriorityLod,
                BehaviorTreeBenchmarkSchedulingProfile.PriorityManaged,
                BehaviorTreeBenchmarkSchedulingProfile.UltraLod
            };

            var configs = new BehaviorTreeBenchmarkConfig[profiles.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                var config = _config.Clone();
                BehaviorTreeBenchmarkPresetCatalog.ApplySchedulingProfile(config, profiles[i]);
                BehaviorTreeBenchmarkPresetCatalog.ApplyProductionBudgets(config);
                config.BenchmarkName = $"{config.BenchmarkName} [{profiles[i]}]";
                configs[i] = config;
            }

            if (!ValidateBeforeExecution(configs, "Run Priority Comparison"))
            {
                return;
            }

            var results = new BehaviorTreeBenchmarkResult[profiles.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                results[i] = BehaviorTreeBenchmarkSession.RunImmediate(configs[i]);
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = $"Priority Comparison [{_config.Complexity}]",
                Results = results
            };
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(_lastBatchResult);

            if (results.Length > 0)
            {
                _lastResult = results[results.Length - 1];
            }

            Repaint();
        }

        private void RunConfiguredBudgetMatrix()
        {
            BehaviorTreeBenchmarkConfig[] configs = BehaviorTreeBenchmarkPresetCatalog.CreateConfiguredBudgetMatrixConfigs();
            if (!ValidateBeforeExecution(configs, "Run Configured Budget Matrix"))
            {
                return;
            }

            var results = new BehaviorTreeBenchmarkResult[configs.Length];

            for (int i = 0; i < configs.Length; i++)
            {
                results[i] = BehaviorTreeBenchmarkSession.RunImmediate(configs[i]);
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = "Configured Budget Matrix",
                Results = results
            };
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(_lastBatchResult);

            if (results.Length > 0)
            {
                _lastResult = results[results.Length - 1];
            }

            Repaint();
        }

        private void CreateBenchmarkScene()
        {
            if (!ValidateBeforeExecution(_config, "Create PlayMode Benchmark Scene") ||
                !TryCreateBenchmarkScene(out var scene))
            {
                return;
            }

            var runnerObject = new GameObject("BehaviorTree Benchmark Runner");
            var runner = runnerObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = true;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.Single;
            runner.SetConfig(_config);

            Selection.activeGameObject = runnerObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void CreateConfiguredBudgetScene()
        {
            BehaviorTreeBenchmarkConfig[] configs = BehaviorTreeBenchmarkPresetCatalog.CreateConfiguredBudgetMatrixConfigs();
            if (!ValidateBeforeExecution(configs, "Create Configured Budget Scene") ||
                !TryCreateBenchmarkScene(out var scene))
            {
                return;
            }

            var runnerObject = new GameObject("BehaviorTree Configured Budget Benchmark Runner");
            var runner = runnerObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = true;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.ConfiguredBudgetMatrix;
            runner.SetConfig(_config);

            Selection.activeGameObject = runnerObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void CreatePresetBenchmarkScene()
        {
            if (_config.Preset == BehaviorTreeBenchmarkPreset.Custom)
            {
                CreateBenchmarkScene();
                return;
            }

            _config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(_config.Preset, _config.Complexity);
            CreateBenchmarkScene();
        }

        private void CreateScaleMatrixScene()
        {
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var configs = new BehaviorTreeBenchmarkConfig[presets.Length];
            for (int i = 0; i < presets.Length; i++)
            {
                configs[i] = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(presets[i], _config.Complexity);
            }

            if (!ValidateBeforeExecution(configs, "Create Scale Matrix Scene") ||
                !TryCreateBenchmarkScene(out var scene))
            {
                return;
            }

            var runnerObject = new GameObject("BehaviorTree Benchmark Scale Matrix Runner");
            var runner = runnerObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = true;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.RecommendedMatrix;
            runner.SetConfig(BehaviorTreeBenchmarkPresetCatalog.CreateConfig(BehaviorTreeBenchmarkPreset.AiCrowd1000, _config.Complexity));

            Selection.activeGameObject = runnerObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void CreateFullMatrixScene()
        {
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var complexities = BehaviorTreeBenchmarkPresetCatalog.GetComplexityTiers();
            var configs = new BehaviorTreeBenchmarkConfig[presets.Length * complexities.Length];
            int configIndex = 0;
            for (int complexityIndex = 0; complexityIndex < complexities.Length; complexityIndex++)
            {
                for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
                {
                    configs[configIndex++] = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(
                        presets[presetIndex],
                        complexities[complexityIndex]);
                }
            }

            if (!ValidateBeforeExecution(configs, "Create Full Matrix Scene") ||
                !TryCreateBenchmarkScene(out var scene))
            {
                return;
            }

            var runnerObject = new GameObject("BehaviorTree Benchmark Full Matrix Runner");
            var runner = runnerObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = true;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.FullMatrix;
            runner.SetConfig(BehaviorTreeBenchmarkPresetCatalog.CreateConfig(BehaviorTreeBenchmarkPreset.AiCrowd1000, _config.Complexity));

            Selection.activeGameObject = runnerObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void CreatePriorityComparisonScene()
        {
            if (!ValidateBeforeExecution(_config, "Create Priority Comparison Scene") ||
                !TryCreateBenchmarkScene(out var scene))
            {
                return;
            }

            var runnerObject = new GameObject("BehaviorTree Benchmark Priority Comparison Runner");
            var runner = runnerObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = true;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.PriorityComparison;
            runner.SetConfig(_config);

            Selection.activeGameObject = runnerObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void ExportLastResult(string format)
        {
            if (_lastResult == null || !_lastResult.IsValid)
            {
                return;
            }

            string extension = format == "json" ? "json" : "csv";
            string defaultName = SanitizeFileName(_lastResult.BenchmarkName) + "." + extension;
            string path = EditorUtility.SaveFilePanel("Export BehaviorTree Benchmark", Application.dataPath, defaultName, extension);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string content = format == "json"
                ? BehaviorTreeBenchmarkExportUtility.ToJson(_lastResult)
                : BehaviorTreeBenchmarkExportUtility.ToCsv(_lastResult);

            File.WriteAllText(path, content);
            _lastExportPath = path;
            EditorUtility.RevealInFinder(path);
        }

        private void ExportLastResultToDefaultFolder()
        {
            if (_lastResult == null || !_lastResult.IsValid)
            {
                return;
            }

            _lastExportPath = BehaviorTreeBenchmarkExportUtility.WriteResultFiles(GetDefaultExportDirectory(), _lastResult, writeCsv: true, writeJson: true);
            RevealLastExport();
        }

        private void ExportLastBatchResult(string format)
        {
            if (_lastBatchResult == null || _lastBatchResult.Results == null || _lastBatchResult.Results.Length == 0)
            {
                return;
            }

            string extension = format == "json" ? "json" : "csv";
            string defaultName = "behavior-tree-benchmark-matrix." + extension;
            string path = EditorUtility.SaveFilePanel("Export BehaviorTree Benchmark Matrix", Application.dataPath, defaultName, extension);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string content = format == "json"
                ? BehaviorTreeBenchmarkExportUtility.ToJson(_lastBatchResult)
                : BehaviorTreeBenchmarkExportUtility.ToCsv(_lastBatchResult);

            File.WriteAllText(path, content);
            _lastExportPath = path;
            EditorUtility.RevealInFinder(path);
        }

        private void ExportLastBatchToDefaultFolder()
        {
            if (_lastBatchResult == null || _lastBatchResult.Results == null || _lastBatchResult.Results.Length == 0)
            {
                return;
            }

            _lastExportPath = BehaviorTreeBenchmarkExportUtility.WriteBatchFiles(GetDefaultExportDirectory(), _lastBatchResult, writeCsv: true, writeJson: true);
            RevealLastExport();
        }

        private void DrawResultSummary(BehaviorTreeBenchmarkResult result)
        {
            EditorGUILayout.HelpBox(result.BudgetSummary, result.ProductionBudgetPassed ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField("Generated (UTC)", result.GeneratedAtUtc);
            EditorGUILayout.LabelField("Complexity", result.Complexity);
            EditorGUILayout.LabelField("Scheduling", result.SchedulingProfile);
            EditorGUILayout.LabelField("Production Budget", result.ProductionBudgetPassed ? "PASS" : "FAIL");
            EditorGUILayout.LabelField("Average Budget (ms)", $"{result.AverageFrameMilliseconds:F4} / {result.TargetAverageFrameMilliseconds:F4}");
            EditorGUILayout.LabelField("Max Budget (ms)", $"{result.MaxFrameMilliseconds:F4} / {result.TargetMaxFrameMilliseconds:F4}");
            EditorGUILayout.LabelField("Memory Delta Budget", $"{FormatBytes(result.ManagedMemoryDeltaBytes)} / {FormatBytes(result.MaxManagedMemoryDeltaBytes)}");
            EditorGUILayout.LabelField("GC Budget", $"{result.Gen0Collections + result.Gen1Collections + result.Gen2Collections} / {result.MaxGcCollections}");
            EditorGUILayout.LabelField("Effective Ratio Budget", $"{result.EffectiveTickRatio:P2} / {result.MinimumEffectiveTickRatio:P2}");
            EditorGUILayout.LabelField("Leaf Nodes", result.LeafNodesPerTree.ToString());
            EditorGUILayout.LabelField("Reads/Writes", $"{result.BlackboardReadsPerLeafPerTick}/{result.WritesPerLeafPerTick}");
            EditorGUILayout.LabelField("Decorator Layers", result.DecoratorLayersPerLeaf.ToString());
            EditorGUILayout.LabelField("Work Iterations", result.SimulatedWorkIterationsPerLeaf.ToString());
            EditorGUILayout.LabelField("Potential Ticks", result.PotentialTicks.ToString());
            EditorGUILayout.LabelField("Executed Ticks", result.TotalTicks.ToString());
            EditorGUILayout.LabelField("Effective Tick Ratio", result.EffectiveTickRatio.ToString("P2"));
            EditorGUILayout.LabelField("Avg/Peak Active Agents", $"{result.AverageActiveAgentsPerFrame:F2} / {result.PeakActiveAgentsPerFrame}");
            EditorGUILayout.LabelField("Total Delta Flushes", result.TotalDeltaFlushes.ToString());
            EditorGUILayout.LabelField("Warmup Delta Flushes", result.WarmupDeltaFlushes.ToString());
            EditorGUILayout.LabelField("Measured Delta Flushes", result.MeasuredDeltaFlushes.ToString());
            EditorGUILayout.LabelField("Total Hash Checks", result.TotalHashChecks.ToString());
            EditorGUILayout.LabelField("Average Frame (ms)", result.AverageFrameMilliseconds.ToString("F4"));
            EditorGUILayout.LabelField("Max Frame (ms)", result.MaxFrameMilliseconds.ToString("F4"));
            EditorGUILayout.LabelField("Ticks / Second", result.TicksPerSecond.ToString("F2"));
            EditorGUILayout.LabelField("Managed Memory Delta", FormatBytes(result.ManagedMemoryDeltaBytes));
            EditorGUILayout.LabelField("Peak Managed Memory", FormatBytes(result.PeakManagedMemoryBytes));
            EditorGUILayout.LabelField("Soak Frames / Samples", $"{result.SoakFrames} / {result.SoakSampleCount}");
            EditorGUILayout.LabelField("GC Collections", $"Gen0={result.Gen0Collections}, Gen1={result.Gen1Collections}, Gen2={result.Gen2Collections}");
        }

        private void DrawBatchSummary(BehaviorTreeBenchmarkBatchResult batch)
        {
            BehaviorTreeBenchmarkAssessmentUtility.PopulateBatchSummary(batch);
            EditorGUILayout.HelpBox(batch.Summary, batch.FailedCount == 0 ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField("Batch Name", batch.BatchName);
            EditorGUILayout.LabelField("Cases", batch.CaseCount.ToString());
            EditorGUILayout.LabelField("Passed / Failed", $"{batch.PassedCount} / {batch.FailedCount}");
            EditorGUILayout.LabelField("Slowest Average (ms)", batch.SlowestAverageFrameMilliseconds.ToString("F4"));
            EditorGUILayout.LabelField("Slowest Max (ms)", batch.SlowestMaxFrameMilliseconds.ToString("F4"));
            EditorGUILayout.LabelField("Peak Managed Memory", FormatBytes(batch.PeakManagedMemoryBytes));
        }

        private void DrawMatrixResultRow(BehaviorTreeBenchmarkResult result)
        {
            string status = result.ProductionBudgetPassed ? "PASS" : "FAIL";
            string row = $"{status} | {result.BenchmarkName} | {result.Complexity} | {result.SchedulingProfile} | {result.AverageFrameMilliseconds:F4} ms avg | {result.MaxFrameMilliseconds:F4} ms max | {result.EffectiveTickRatio:P0} effective";
            EditorGUILayout.LabelField(row, WordWrappedLabelStyle);
        }

        private void DrawWrappedMetric(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            EditorGUILayout.LabelField(value ?? string.Empty, WordWrappedLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private GUIStyle WordWrappedLabelStyle
        {
            get
            {
                _wordWrappedLabelStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    wordWrap = true
                };

                return _wordWrappedLabelStyle;
            }
        }

        private static float GetResponsiveLabelWidth(float windowWidth)
        {
            return Mathf.Clamp(windowWidth * 0.38f, 220f, 300f);
        }

        private static string FormatBytes(long value)
        {
            const double bytesPerMib = 1024d * 1024d;
            return $"{value / bytesPerMib:F2} MiB ({value})";
        }

        internal static bool TryValidateRequest(BehaviorTreeBenchmarkConfig config, out string reason)
        {
            if (config == null)
            {
                reason = "Benchmark configuration is null.";
                return false;
            }

            if (!AreScalarValuesWithinHardLimits(config))
            {
                reason = "One or more values exceed the benchmark editor hard limits.";
                return false;
            }

            try
            {
                long nodesPerAgent = checked(1L + checked((long)config.LeafNodesPerTree * (1L + config.DecoratorLayersPerLeaf)));
                long totalRuntimeNodes = checked(nodesPerAgent * config.AgentCount);
                if (totalRuntimeNodes > MaxTotalRuntimeNodes)
                {
                    reason = $"The request would create approximately {totalRuntimeNodes:N0} runtime nodes; the editor limit is {MaxTotalRuntimeNodes:N0}.";
                    return false;
                }

                long totalTrackedKeys = checked((long)config.AgentCount * config.TrackedKeysPerAgent);
                if (totalTrackedKeys > MaxTotalTrackedKeys)
                {
                    reason = $"The request would create approximately {totalTrackedKeys:N0} tracked keys; the editor limit is {MaxTotalTrackedKeys:N0}.";
                    return false;
                }

                long frameCount = checked((long)config.WarmupFrames + config.MeasurementFrames + config.SoakFrames);
                long workPerLeaf = checked(
                    1L +
                    config.BlackboardReadsPerLeafPerTick +
                    config.WritesPerLeafPerTick +
                    config.DecoratorLayersPerLeaf +
                    config.SimulatedWorkIterationsPerLeaf);
                long workUnitsPerFrame = checked(
                    checked((long)config.AgentCount * config.LeafNodesPerTree) *
                    config.TicksPerFrame *
                    workPerLeaf);
                if (workUnitsPerFrame > MaxWorkUnitsPerFrame)
                {
                    reason = $"The request requires approximately {workUnitsPerFrame:N0} work units per frame; the editor limit is {MaxWorkUnitsPerFrame:N0}.";
                    return false;
                }

                long totalWorkUnits = checked(workUnitsPerFrame * frameCount);
                if (totalWorkUnits > MaxTotalWorkUnits)
                {
                    reason = $"The request requires approximately {totalWorkUnits:N0} total work units; the editor limit is {MaxTotalWorkUnits:N0}. Reduce agents, leaves, frames, ticks, or simulated work.";
                    return false;
                }
            }
            catch (System.OverflowException)
            {
                reason = "The derived benchmark workload exceeds the supported numeric range.";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool AreScalarValuesWithinHardLimits(BehaviorTreeBenchmarkConfig config)
        {
            return config != null &&
                config.AgentCount >= 1 && config.AgentCount <= MaxAgentCount &&
                config.LeafNodesPerTree >= 1 && config.LeafNodesPerTree <= MaxLeafNodesPerTree &&
                config.BlackboardReadsPerLeafPerTick >= 0 && config.BlackboardReadsPerLeafPerTick <= MaxBlackboardOperationsPerLeaf &&
                config.WritesPerLeafPerTick >= 1 && config.WritesPerLeafPerTick <= MaxBlackboardOperationsPerLeaf &&
                config.DecoratorLayersPerLeaf >= 0 && config.DecoratorLayersPerLeaf <= MaxDecoratorLayersPerLeaf &&
                config.SimulatedWorkIterationsPerLeaf >= 0 && config.SimulatedWorkIterationsPerLeaf <= MaxSimulatedWorkIterationsPerLeaf &&
                config.TrackedKeysPerAgent >= 0 && config.TrackedKeysPerAgent <= MaxTrackedKeysPerAgent &&
                config.WarmupFrames >= 0 && config.WarmupFrames <= MaxFrameCount &&
                config.MeasurementFrames >= 1 && config.MeasurementFrames <= MaxFrameCount &&
                config.TicksPerFrame >= 1 && config.TicksPerFrame <= MaxTicksPerFrame &&
                config.HashCheckIntervalFrames >= 1 && config.HashCheckIntervalFrames <= MaxFrameCount &&
                config.SoakFrames >= 0 && config.SoakFrames <= MaxFrameCount &&
                config.SoakSampleIntervalFrames >= 1 && config.SoakSampleIntervalFrames <= MaxFrameCount;
        }

        private static bool ValidateBeforeExecution(BehaviorTreeBenchmarkConfig config, string operation)
        {
            config?.Sanitize();
            if (TryValidateRequest(config, out string reason))
            {
                return true;
            }

            EditorUtility.DisplayDialog(operation, reason, "OK");
            return false;
        }

        private static bool ValidateBeforeExecution(BehaviorTreeBenchmarkConfig[] configs, string operation)
        {
            if (configs == null || configs.Length == 0)
            {
                EditorUtility.DisplayDialog(operation, "The benchmark matrix is empty.", "OK");
                return false;
            }

            for (int i = 0; i < configs.Length; i++)
            {
                BehaviorTreeBenchmarkConfig config = configs[i];
                config?.Sanitize();
                if (!TryValidateRequest(config, out string reason))
                {
                    string benchmarkName = config?.BenchmarkName ?? $"Case {i + 1}";
                    EditorUtility.DisplayDialog(operation, $"{benchmarkName}: {reason}", "OK");
                    return false;
                }
            }

            return true;
        }

        private static bool TryCreateBenchmarkScene(out UnityEngine.SceneManagement.Scene scene)
        {
            scene = default;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            return scene.IsValid();
        }

        private void ApplyPreset(BehaviorTreeBenchmarkPreset preset)
        {
            if (preset == BehaviorTreeBenchmarkPreset.Custom)
            {
                _config.Preset = preset;
                _config.BenchmarkName = "Custom Benchmark";
                return;
            }

            _config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(preset, _config.Complexity);
            Repaint();
        }

        private void ApplyComplexity(BehaviorTreeBenchmarkComplexity complexity)
        {
            if (!AreScalarValuesWithinHardLimits(_config))
            {
                EditorUtility.DisplayDialog(
                    "Behavior Tree Benchmark",
                    "Correct values that exceed the editor hard limits before changing the complexity profile.",
                    "OK");
                return;
            }

            if (_config.Preset == BehaviorTreeBenchmarkPreset.Custom)
            {
                _config.Complexity = complexity;
                _config.BenchmarkName = $"Custom Benchmark [{complexity}]";
            }
            else
            {
                _config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(_config.Preset, complexity);
            }

            Repaint();
        }

        private void ApplyScheduling(BehaviorTreeBenchmarkSchedulingProfile schedulingProfile)
        {
            if (!AreScalarValuesWithinHardLimits(_config))
            {
                EditorUtility.DisplayDialog(
                    "Behavior Tree Benchmark",
                    "Correct values that exceed the editor hard limits before changing the scheduling profile.",
                    "OK");
                return;
            }

            BehaviorTreeBenchmarkPresetCatalog.ApplySchedulingProfile(_config, schedulingProfile);
            BehaviorTreeBenchmarkPresetCatalog.ApplyProductionBudgets(_config);
            Repaint();
        }

        private static string GetDefaultExportDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "BehaviorTreeBenchmarkResults");
        }

        private void RevealLastExport()
        {
            if (!string.IsNullOrEmpty(_lastExportPath))
            {
                EditorUtility.RevealInFinder(_lastExportPath);
                Repaint();
            }
        }

        private static string SanitizeFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "behavior-tree-benchmark";
            }

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(invalidChar, '_');
            }

            return input;
        }
    }
}
