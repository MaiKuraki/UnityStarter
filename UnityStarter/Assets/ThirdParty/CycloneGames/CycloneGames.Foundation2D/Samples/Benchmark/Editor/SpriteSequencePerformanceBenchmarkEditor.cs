#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;
using CycloneGames.Foundation2D.Sample.Runtime;

namespace CycloneGames.Foundation2D.Sample.Editor
{
    [CustomEditor(typeof(SpriteSequencePerformanceBenchmark))]
    [CanEditMultipleObjects]
    public sealed class SpriteSequencePerformanceBenchmarkEditor : UnityEditor.Editor
    {
        private SerializedProperty _enableScaleSweep;
        private SerializedProperty _ignoreInactiveSceneControllers;
        private SerializedProperty _sweepTemplate;
        private SerializedProperty _includeSceneControllersInSweep;

        private SerializedProperty _compareMonoAndBurst;
        private SerializedProperty _burstManager;
        private SerializedProperty _enableCapacitySearch;
        private SerializedProperty _capacitySearchTestBurst;
        private SerializedProperty _capacitySearchSamplesPerPoint;
        private SerializedProperty _capacitySearchUseMedian;
        private SerializedProperty _enableNonMonotonicLocalRescan;
        private SerializedProperty _prewarmGeneratedToMaxTargetBeforeRun;
        private SerializedProperty _useFactoryMonoFastPool;
        private bool _sceneValidationAvailable;
        private int _controllerCount;

        private void OnEnable()
        {
            _enableScaleSweep = serializedObject.FindProperty("enableScaleSweep");
            _ignoreInactiveSceneControllers = serializedObject.FindProperty("ignoreInactiveSceneControllers");
            _sweepTemplate = serializedObject.FindProperty("sweepTemplate");
            _includeSceneControllersInSweep = serializedObject.FindProperty("includeSceneControllersInSweep");

            _compareMonoAndBurst = serializedObject.FindProperty("compareMonoAndBurst");
            _burstManager = serializedObject.FindProperty("burstManager");
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

            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
            {
                if (GUILayout.Button("Refresh Scene Validation"))
                {
                    RefreshSceneValidation();
                }
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Scene and dependency diagnostics require one benchmark component. Common serialized settings above remain available for multi-object editing.",
                    MessageType.Info);
                return;
            }
            else if (!_sceneValidationAvailable)
            {
                EditorGUILayout.HelpBox("Scene queries are performed only on request so Inspector repaint cost stays independent of scene size.", MessageType.None);
            }
            else if (_controllerCount == 0)
            {
                EditorGUILayout.HelpBox("No SpriteSequenceController found in the selected scene scope. Benchmark will have no targets.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField($"Controllers in selected scope: {_controllerCount}", EditorStyles.miniLabel);
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
                EditorGUILayout.HelpBox("This sample assembly is compiled only when the optional Burst and Collections integration is available.", MessageType.Info);
                if (_burstManager.hasMultipleDifferentValues)
                {
                    EditorGUILayout.HelpBox("Selected benchmark components use different Burst Manager assignments.", MessageType.Info);
                }
                else if (_burstManager.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Assign an active SpriteSequenceBurstManager. Burst phases are cancelled instead of silently measuring MonoUpdate fallback.", MessageType.Error);
                }
                else if (_burstManager.objectReferenceValue is SpriteSequenceBurstManager manager && !manager.isActiveAndEnabled)
                {
                    EditorGUILayout.HelpBox("The assigned SpriteSequenceBurstManager is inactive or disabled.", MessageType.Error);
                }
            }

            EditorGUILayout.HelpBox("Benchmark component can be placed on any object; it does not need to be on the SpriteSequenceController object.", MessageType.None);
        }

        private void RefreshSceneValidation()
        {
#if UNITY_2023_1_OR_NEWER
            SpriteSequenceController[] controllers = Object.FindObjectsByType<SpriteSequenceController>(
                _ignoreInactiveSceneControllers.boolValue ? FindObjectsInactive.Exclude : FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            SpriteSequenceController[] controllers = Object.FindObjectsOfType<SpriteSequenceController>(!_ignoreInactiveSceneControllers.boolValue);
#endif
            _controllerCount = controllers?.Length ?? 0;
            _sceneValidationAvailable = true;
        }
    }
}
#endif
