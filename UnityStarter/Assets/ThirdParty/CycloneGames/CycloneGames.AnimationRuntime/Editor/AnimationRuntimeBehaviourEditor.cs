using CycloneGames.AnimationRuntime.Core;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Inspector for <see cref="AnimationRuntimeBehaviour"/>.
    /// Authoring fields first, then a throttled runtime block (layer weights, live playbacks, counters) fed by the
    /// runtime's zero-allocation snapshot API. The graph is never touched outside the throttled block.
    /// </summary>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AnimationRuntimeBehaviour))]
    public sealed class AnimationRuntimeBehaviourEditor : UnityEditor.Editor
    {
        private SerializedProperty _animator;
        private SerializedProperty _profile;
        private SerializedProperty _timePolicy;
        private SerializedProperty _commitPhase;
        private SerializedProperty _rootMotionOwnership;
        private SerializedProperty _rootMotionAxis;
        private SerializedProperty _rootMotionScale;
        private SerializedProperty _initializeOnAwake;
        private SerializedProperty _logDiagnostics;

        private bool _bindingFoldout = true;
        private bool _frameFoldout = true;
        private bool _runtimeFoldout = true;
        private double _nextRefresh;
        private AnimationRuntimeStats _stats;
        private int _layerCount;
        private readonly float[] _layerWeights = new float[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly int[] _layerActive = new int[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly int[] _layerCapacity = new int[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly bool[] _layerExternal = new bool[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly int[] _layerSnapshotCount = new int[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly AnimationPlaybackSnapshot[][] _layerSnapshots = new AnimationPlaybackSnapshot[Core.AnimationRuntimeLimits.MaxLayers][];

        private void OnEnable()
        {
            _animator = serializedObject.FindProperty("_animator");
            _profile = serializedObject.FindProperty("_profile");
            _timePolicy = serializedObject.FindProperty("_timePolicy");
            _commitPhase = serializedObject.FindProperty("_commitPhase");
            _rootMotionOwnership = serializedObject.FindProperty("_rootMotionOwnership");
            _rootMotionAxis = serializedObject.FindProperty("_rootMotionAxis");
            _rootMotionScale = serializedObject.FindProperty("_rootMotionScale");
            _initializeOnAwake = serializedObject.FindProperty("_initializeOnAwake");
            _logDiagnostics = serializedObject.FindProperty("_logDiagnostics");

            for (int i = 0; i < _layerSnapshots.Length; i++)
            {
                _layerSnapshots[i] = new AnimationPlaybackSnapshot[Core.AnimationRuntimeLimits.MaxLayerCapacity];
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawTitle(
                "Animation Runtime",
                "Pose backend: layers, pooled playbacks, absolute-time evaluation",
                InspectorUiUtility.RuntimeColor);

            DrawBinding();
            DrawFrameModel();
            DrawRuntimeBlock();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBinding()
        {
            if (!InspectorUiUtility.DrawSectionHeader(ref _bindingFoldout, "Binding", InspectorUiUtility.NeutralColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_animator, new GUIContent("Animator", "Sampled by the runtime graph. Falls back to a child Animator."));
                EditorGUILayout.PropertyField(_profile, new GUIContent("Layer Profile", "Layer stack for this archetype. Empty uses one full-body layer."));
                EditorGUILayout.PropertyField(_initializeOnAwake);
                EditorGUILayout.PropertyField(_logDiagnostics);
            }
        }

        private void DrawFrameModel()
        {
            if (!InspectorUiUtility.DrawSectionHeader(ref _frameFoldout, "Frame model", InspectorUiUtility.WarningColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_timePolicy);
                var policy = (AnimationTimePolicy)_timePolicy.enumValueIndex;

                if (policy == AnimationTimePolicy.External)
                {
                    EditorGUILayout.HelpBox(
                        "External time: the host owns clip time. Clips advance only when it calls BeginPlayback/UpdatePlayback with an absolute local time; the commit tick is diagnostic.",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Tick-driven time: this component advances clips from its own clock. UpdatePlayback then changes weight/speed/loop only; use SeekPlayback for an explicit jump.",
                        MessageType.Info);
                }

                EditorGUILayout.PropertyField(_commitPhase);
                var phase = (AnimationCommitPhase)_commitPhase.enumValueIndex;

                if (phase == AnimationCommitPhase.FixedUpdate)
                {
                    EditorGUILayout.HelpBox(
                        "Commits once per physics step. Set Animator.updateMode to AnimatePhysics and enable Rigidbody interpolation, otherwise pose and root motion run on different clocks.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Commits once per rendered frame. If a movement component consumes root motion inside FixedUpdate, switch this to FixedUpdate or root motion will be scaled by the render-to-fixed ratio.",
                        MessageType.None);
                }

                EditorGUILayout.PropertyField(_rootMotionOwnership);
                var ownership = (RootMotionOwnership)_rootMotionOwnership.enumValueIndex;

                if (ownership == RootMotionOwnership.Backend)
                {
                    EditorGUILayout.PropertyField(_rootMotionAxis);
                    EditorGUILayout.PropertyField(_rootMotionScale);

                    var assignedAnimator = _animator.objectReferenceValue as Animator;
                    var component = (Component)target;
                    bool childAnimator = assignedAnimator != null
                        && component != null
                        && assignedAnimator.gameObject != component.gameObject;

                    if (childAnimator)
                    {
                        EditorGUILayout.HelpBox(
                            "Backend root motion needs the Animator on this GameObject. It is on '"
                            + assignedAnimator.name
                            + "', so the runtime will reject Backend ownership and log an error.",
                            MessageType.Error);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "The runtime implements OnAnimatorMove in this mode. Remove any other OnAnimatorMove handler on the same character or root motion is applied twice.",
                            MessageType.Warning);
                    }
                }
                else if (ownership == RootMotionOwnership.External)
                {
                    EditorGUILayout.HelpBox(
                        "A movement component owns root motion and reads Animator.deltaPosition. The runtime does not implement OnAnimatorMove.",
                        MessageType.None);
                }
            }
        }

        private void DrawRuntimeBlock()
        {
            if (!InspectorUiUtility.DrawSectionHeader(ref _runtimeFoldout, "Runtime", InspectorUiUtility.RuntimeColor))
            {
                return;
            }

            var behaviour = (AnimationRuntimeBehaviour)target;
            AnimationRuntimeGraph graph = behaviour.Graph;

            if (graph == null || !graph.IsInitialized)
            {
                EditorGUILayout.HelpBox(
                    Application.isPlaying
                        ? "Graph is not initialized. Check the Animator binding and the Console."
                        : "Runtime data is available in Play Mode.",
                    MessageType.None);
                return;
            }

            bool refreshed = InspectorUiUtility.ShouldRefresh(ref _nextRefresh);
            if (refreshed)
            {
                CacheRuntime(graph);
            }

            InspectorUiUtility.BeginPanel();

            InspectorUiUtility.DrawStatusRow("Layers", _layerCount.ToString(), InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow("Time policy", _stats.TimePolicy.ToString(), InspectorUiUtility.RuntimeColor);
            InspectorUiUtility.DrawStatusRow("Playables", _stats.PlayableCount.ToString(), InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Playbacks",
                _stats.ActivePlaybacks + " / peak " + _stats.PeakPlaybacks,
                _stats.ActivePlaybacks > 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Dropped",
                _stats.DroppedPlaybacks.ToString(),
                _stats.DroppedPlaybacks > 0 ? InspectorUiUtility.WarningColor : InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Recreated playables",
                _stats.RecreatedPlayables.ToString(),
                _stats.RecreatedPlayables > 0 ? InspectorUiUtility.WarningColor : InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Reused playables",
                _stats.ReusedPlayables.ToString(),
                _stats.ReusedPlayables > 0 ? InspectorUiUtility.SuccessColor : InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow("Commits", _stats.Commits.ToString(), InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow(
                "Evaluations",
                _stats.Evaluations.ToString(),
                _stats.Evaluations < _stats.Commits ? InspectorUiUtility.SuccessColor : InspectorUiUtility.NeutralColor);
            InspectorUiUtility.DrawStatusRow("Native writes", _stats.NativeWrites.ToString(), InspectorUiUtility.NeutralColor);

            InspectorUiUtility.EndPanel();

            for (int layer = 0; layer < _layerCount; layer++)
            {
                DrawLayer(graph, layer);
            }

            if (refreshed && Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawLayer(AnimationRuntimeGraph graph, int layer)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Layer " + layer, EditorStyles.miniBoldLabel, GUILayout.Width(60f));

            Color barColor = _layerExternal[layer] ? InspectorUiUtility.WarningColor : InspectorUiUtility.RuntimeColor;
            InspectorUiUtility.DrawBar(_layerWeights[layer], barColor);

            string occupancy = _layerExternal[layer]
                ? "external"
                : _layerActive[layer] + "/" + _layerCapacity[layer];
            EditorGUILayout.LabelField(occupancy, EditorStyles.miniLabel, GUILayout.Width(64f));
            EditorGUILayout.EndHorizontal();

            if (_layerExternal[layer])
            {
                EditorGUILayout.LabelField("  driven by an external Playable (locomotion blend)", EditorStyles.miniLabel);
                return;
            }

            int count = _layerSnapshotCount[layer];
            for (int i = 0; i < count; i++)
            {
                AnimationPlaybackSnapshot playback = _layerSnapshots[layer][i];
                EditorGUILayout.LabelField(
                    "  #" + playback.InstanceId
                    + " ch " + playback.PlaybackChannel
                    + " clip " + playback.ClipChannel
                    + "  t " + playback.Time.ToString("0.000")
                    + "  w " + playback.Weight.ToString("0.00")
                    + (playback.Connected ? string.Empty : "  (pending)"),
                    EditorStyles.miniLabel);
            }
        }

        private void CacheRuntime(AnimationRuntimeGraph graph)
        {
            _stats = graph.GetStats();
            _layerCount = graph.LayerCount;

            int layers = _layerCount < _layerSnapshots.Length ? _layerCount : _layerSnapshots.Length;
            for (int layer = 0; layer < layers; layer++)
            {
                _layerWeights[layer] = graph.GetLayerWeight(layer);
                _layerActive[layer] = graph.GetLayerActivePlaybacks(layer);
                _layerCapacity[layer] = graph.GetLayerCapacity(layer);
                _layerExternal[layer] = graph.IsLayerExternallySourced(layer);
                _layerSnapshotCount[layer] = graph.CopyPlaybackSnapshots(layer, _layerSnapshots[layer]);
            }
        }
    }
}
