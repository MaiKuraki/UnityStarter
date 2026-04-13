#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;
using CycloneGames.Foundation2D.Sample.Runtime;

namespace CycloneGames.Foundation2D.Sample.Editor
{
    [CustomEditor(typeof(SpriteSequencePerformanceBenchmark))]
    public sealed class SpriteSequencePerformanceBenchmarkEditor : UnityEditor.Editor
    {
        private SerializedProperty _enableScaleSweep;
        private SerializedProperty _ignoreInactiveSceneControllers;
        private SerializedProperty _sweepTemplate;
        private SerializedProperty _includeSceneControllersInSweep;

        private SerializedProperty _compareMonoAndBurst;
        private SerializedProperty _enableCapacitySearch;
        private SerializedProperty _capacitySearchTestBurst;
        private SerializedProperty _capacitySearchSamplesPerPoint;
        private SerializedProperty _capacitySearchUseMedian;
        private SerializedProperty _enableNonMonotonicLocalRescan;
        private SerializedProperty _prewarmGeneratedToMaxTargetBeforeRun;
        private SerializedProperty _useFactoryMonoFastPool;

        private void OnEnable()
        {
            _enableScaleSweep = serializedObject.FindProperty("enableScaleSweep");
            _ignoreInactiveSceneControllers = serializedObject.FindProperty("ignoreInactiveSceneControllers");
            _sweepTemplate = serializedObject.FindProperty("sweepTemplate");
            _includeSceneControllersInSweep = serializedObject.FindProperty("includeSceneControllersInSweep");

            _compareMonoAndBurst = serializedObject.FindProperty("compareMonoAndBurst");
            _enableCapacitySearch = serializedObject.FindProperty("enableCapacitySearch");
            _capacitySearchTestBurst = serializedObject.FindProperty("capacitySearchTestBurst");
            _capacitySearchSamplesPerPoint = serializedObject.FindProperty("capacitySearchSamplesPerPoint");
            _capacitySearchUseMedian = serializedObject.FindProperty("capacitySearchUseMedian");
            _enableNonMonotonicLocalRescan = serializedObject.FindProperty("enableNonMonotonicLocalRescan");
            _prewarmGeneratedToMaxTargetBeforeRun = serializedObject.FindProperty("prewarmGeneratedToMaxTargetBeforeRun");
            _useFactoryMonoFastPool = serializedObject.FindProperty("useFactoryMonoFastPool");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            DrawValidationHints();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawValidationHints()
        {
            EditorGUILayout.LabelField("Validation Hints", EditorStyles.boldLabel);

            var allControllers = FindControllers(_ignoreInactiveSceneControllers.boolValue);
            bool hasAnyController = allControllers != null && allControllers.Length > 0;

            if (!hasAnyController)
            {
                EditorGUILayout.HelpBox("No SpriteSequenceController found in scene. Benchmark will have no targets.", MessageType.Warning);
            }

            if (_enableScaleSweep.boolValue)
            {
                bool hasTemplate = _sweepTemplate.objectReferenceValue != null;
                if (!hasTemplate && !_includeSceneControllersInSweep.boolValue)
                {
                    EditorGUILayout.HelpBox("Scale Sweep is enabled, but Sweep Template is empty and Include Scene Controllers is off. Cannot generate benchmark targets.", MessageType.Error);
                }
                else if (!hasTemplate)
                {
                    EditorGUILayout.HelpBox("Scale Sweep will auto-pick the first scene SpriteSequenceController as template if available.", MessageType.Info);
                }

                if (!_prewarmGeneratedToMaxTargetBeforeRun.boolValue)
                {
                    EditorGUILayout.HelpBox("Prewarm Generated To Max Target is off. Instantiate spikes can leak into warmup/sample stability.", MessageType.Warning);
                }

                if (_useFactoryMonoFastPool.boolValue)
                {
                    EditorGUILayout.HelpBox("Factory MonoFastPool is enabled for generated benchmark objects.", MessageType.Info);
                }
            }

            if (_enableCapacitySearch.boolValue)
            {
                if (_capacitySearchSamplesPerPoint.intValue < 3)
                {
                    EditorGUILayout.HelpBox("Capacity Search samples per point is low. Use 3 or 5 for better noise resistance.", MessageType.Info);
                }

                if (!_capacitySearchUseMedian.boolValue)
                {
                    EditorGUILayout.HelpBox("Median aggregation is off. In Editor this can increase non-monotonic anomalies.", MessageType.Warning);
                }

                if (!_enableNonMonotonicLocalRescan.boolValue)
                {
                    EditorGUILayout.HelpBox("Non-monotonic local rescan is off. Reported Best Count may be less stable under Editor jitter.", MessageType.Info);
                }
            }

            bool burstIsRelevant = _compareMonoAndBurst.boolValue || (_enableCapacitySearch.boolValue && _capacitySearchTestBurst.boolValue);
            if (burstIsRelevant)
            {
#if BURST_JOBS
                EditorGUILayout.HelpBox("BURST_JOBS define is enabled.", MessageType.Info);
#else
                EditorGUILayout.HelpBox("BURST_JOBS define is not enabled. BurstManaged tests will fallback to MonoUpdate.", MessageType.Warning);
#endif

                if (!HasActiveBurstManagerInScene())
                {
                    EditorGUILayout.HelpBox("No SpriteSequenceBurstManager found in scene. BurstManaged playback may fallback to MonoUpdate.", MessageType.Warning);
                }
            }

            EditorGUILayout.HelpBox("Benchmark component can be placed on any object; it does not need to be on the SpriteSequenceController object.", MessageType.None);
        }

        private static bool HasActiveBurstManagerInScene()
        {
#if UNITY_2023_1_OR_NEWER
        var managers = Object.FindObjectsByType<SpriteSequenceBurstManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var managers = Object.FindObjectsOfType<SpriteSequenceBurstManager>(true);
#endif
            return managers != null && managers.Length > 0;
        }

        private static SpriteSequenceController[] FindControllers(bool ignoreInactive)
        {
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<SpriteSequenceController>(ignoreInactive ? FindObjectsInactive.Exclude : FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<SpriteSequenceController>(!ignoreInactive);
#endif
        }
    }
}
#endif
