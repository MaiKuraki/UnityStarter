using System.IO;
using CycloneGames.BehaviorTree.Runtime.PerformanceTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeBenchmarkWindow : EditorWindow
    {
        private readonly string[] _presetLabels = System.Enum.GetNames(typeof(BehaviorTreeBenchmarkPreset));
        private readonly string[] _complexityLabels = System.Enum.GetNames(typeof(BehaviorTreeBenchmarkComplexity));
        private readonly string[] _schedulingLabels = System.Enum.GetNames(typeof(BehaviorTreeBenchmarkSchedulingProfile));

        private BehaviorTreeBenchmarkConfig _config = new BehaviorTreeBenchmarkConfig();
        private BehaviorTreeBenchmarkResult _lastResult;
        private BehaviorTreeBenchmarkBatchResult _lastBatchResult;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/CycloneGames/Behavior Tree/Behavior Tree Benchmark")]
        public static void OpenWindow()
        {
            var window = GetWindow<BehaviorTreeBenchmarkWindow>();
            window.titleContent = new GUIContent("BT Benchmark");
            window.minSize = new Vector2(460f, 560f);
        }

        private void OnGUI()
        {
            bool createSceneRequested = false;
            bool createPresetSceneRequested = false;
            bool createScaleMatrixSceneRequested = false;
            bool createFullMatrixSceneRequested = false;
            bool createPriorityComparisonSceneRequested = false;
            bool runScaleMatrixRequested = false;
            bool runFullMatrixRequested = false;
            bool runPriorityComparisonRequested = false;

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
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
                    EditorGUILayout.LabelField("Batch Name", _lastBatchResult.BatchName);
                    EditorGUILayout.LabelField("Cases", _lastBatchResult.Results.Length.ToString());
                    for (int i = 0; i < _lastBatchResult.Results.Length; i++)
                    {
                        var result = _lastBatchResult.Results[i];
                        EditorGUILayout.LabelField(result.BenchmarkName,
                            $"{result.Complexity} | {result.SchedulingProfile} | {result.AverageFrameMilliseconds:F4} ms avg | {result.EffectiveTickRatio:P0} effective");
                    }
                }
            }
            finally
            {
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
        }

        private void RunEditorBenchmark()
        {
            _config.Sanitize();
            _lastResult = BehaviorTreeBenchmarkSession.RunImmediate(_config);
            Repaint();
        }

        private void RunScaleMatrix()
        {
            var presets = BehaviorTreeBenchmarkPresetCatalog.GetRecommendedPresets();
            var results = new BehaviorTreeBenchmarkResult[presets.Length];

            for (int i = 0; i < presets.Length; i++)
            {
                var config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(presets[i], _config.Complexity);
                results[i] = BehaviorTreeBenchmarkSession.RunImmediate(config);
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = $"Scale Matrix [{_config.Complexity}]",
                Results = results
            };

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
            var results = new BehaviorTreeBenchmarkResult[presets.Length * complexities.Length];
            int resultIndex = 0;

            for (int complexityIndex = 0; complexityIndex < complexities.Length; complexityIndex++)
            {
                for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
                {
                    var config = BehaviorTreeBenchmarkPresetCatalog.CreateConfig(presets[presetIndex], complexities[complexityIndex]);
                    results[resultIndex++] = BehaviorTreeBenchmarkSession.RunImmediate(config);
                }
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = "Full Matrix",
                Results = results
            };

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

            var results = new BehaviorTreeBenchmarkResult[profiles.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                var config = _config.Clone();
                BehaviorTreeBenchmarkPresetCatalog.ApplySchedulingProfile(config, profiles[i]);
                config.BenchmarkName = $"{config.BenchmarkName} [{profiles[i]}]";
                config.Sanitize();
                results[i] = BehaviorTreeBenchmarkSession.RunImmediate(config);
            }

            _lastBatchResult = new BehaviorTreeBenchmarkBatchResult
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("O"),
                BatchName = $"Priority Comparison [{_config.Complexity}]",
                Results = results
            };

            if (results.Length > 0)
            {
                _lastResult = results[results.Length - 1];
            }

            Repaint();
        }

        private void CreateBenchmarkScene()
        {
            _config.Sanitize();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var runnerObject = new GameObject("BehaviorTree Benchmark Runner");
            var runner = runnerObject.AddComponent<BehaviorTreeBenchmarkRunner>();
            runner.AutoRunOnStart = true;
            runner.RunnerMode = BehaviorTreeBenchmarkRunnerMode.Single;
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
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
            EditorUtility.RevealInFinder(path);
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
            EditorUtility.RevealInFinder(path);
        }

        private void DrawResultSummary(BehaviorTreeBenchmarkResult result)
        {
            EditorGUILayout.LabelField("Generated (UTC)", result.GeneratedAtUtc);
            EditorGUILayout.LabelField("Complexity", result.Complexity);
            EditorGUILayout.LabelField("Scheduling", result.SchedulingProfile);
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
            EditorGUILayout.LabelField("Managed Memory Delta", result.ManagedMemoryDeltaBytes.ToString());
            EditorGUILayout.LabelField("Peak Managed Memory", result.PeakManagedMemoryBytes.ToString());
            EditorGUILayout.LabelField("Soak Frames / Samples", $"{result.SoakFrames} / {result.SoakSampleCount}");
            EditorGUILayout.LabelField("GC Collections", $"Gen0={result.Gen0Collections}, Gen1={result.Gen1Collections}, Gen2={result.Gen2Collections}");
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
            BehaviorTreeBenchmarkPresetCatalog.ApplySchedulingProfile(_config, schedulingProfile);
            Repaint();
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
