using CycloneGames.AnimationRuntime.Core;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Inspector for <see cref="AnimationRuntimeBehaviour"/>.
    /// Authoring fields first, then a throttled runtime block fed by the graph's zero-allocation snapshot API.
    /// Every string shown in the runtime block is rebuilt only on the throttled refresh, so repainting the
    /// inspector does not allocate. GUIContent instances and profile validation are cached the same way.
    /// </summary>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AnimationRuntimeBehaviour))]
    public sealed class AnimationRuntimeBehaviourEditor : UnityEditor.Editor
    {
        private static readonly string[] StatusLabels =
        {
            "Layers", "Time policy", "Playables", "Playbacks", "Dropped playbacks",
            "Recreated playables", "Reused playables", "Commits", "Evaluations",
            "Native writes", "Dropped clip events", "Pose hash", "Phase order"
        };

        private const int StatusRowCount = 13;

        private SerializedProperty _animator;
        private SerializedProperty _profile;
        private SerializedProperty _timePolicy;
        private SerializedProperty _commitPhase;
        private SerializedProperty _rootMotionOwnership;
        private SerializedProperty _rootMotionAxis;
        private SerializedProperty _rootMotionScale;
        private SerializedProperty _initializeOnAwake;
        private SerializedProperty _logDiagnostics;
        private SerializedProperty _dispatchClipEvents;
        private SerializedProperty _updateRateHz;
        private SerializedProperty _pauseWhenInvisible;

        private GUIContent _animatorLabel;
        private GUIContent _profileLabel;
        private GUIContent _dispatchClipEventsLabel;
        private readonly string[] _layerHeaders = new string[Core.AnimationRuntimeLimits.MaxLayers];

        private bool _bindingFoldout = true;
        private bool _frameFoldout = true;
        private bool _runtimeFoldout = true;
        private double _nextRefresh;
        private double _nextProfileValidation;
        private string _profileReport = string.Empty;
        private AnimationRuntimeStats _stats;
        private int _layerCount;
        private readonly string[] _statusValues = new string[StatusRowCount];
        private readonly Color[] _statusColors = new Color[StatusRowCount];
        private readonly float[] _layerWeights = new float[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly string[] _layerWeightLabels = new string[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly string[] _layerOccupancy = new string[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly int[] _layerActive = new int[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly int[] _layerCapacity = new int[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly bool[] _layerExternal = new bool[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly int[] _layerSnapshotCount = new int[Core.AnimationRuntimeLimits.MaxLayers];
        private readonly AnimationPlaybackSnapshot[][] _layerSnapshots = new AnimationPlaybackSnapshot[Core.AnimationRuntimeLimits.MaxLayers][];
        private readonly string[][] _layerRows = new string[Core.AnimationRuntimeLimits.MaxLayers][];

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
            _dispatchClipEvents = serializedObject.FindProperty("_dispatchClipEvents");
            _updateRateHz = serializedObject.FindProperty("_updateRateHz");
            _pauseWhenInvisible = serializedObject.FindProperty("_pauseWhenInvisible");

            _animatorLabel = new GUIContent("Animator", "Sampled by the runtime graph. Falls back to a child Animator.");
            _profileLabel = new GUIContent("Layer Profile", "Layer stack for this archetype. Empty uses one full-body layer.");
            _dispatchClipEventsLabel = new GUIContent(
                "Dispatch Clip Events",
                "Disable when a timeline (Choreography) owns event dispatch, or when Animator.fireEvents is left enabled.");

            for (int i = 0; i < _layerSnapshots.Length; i++)
            {
                _layerSnapshots[i] = new AnimationPlaybackSnapshot[Core.AnimationRuntimeLimits.MaxLayerCapacity];
                _layerRows[i] = new string[Core.AnimationRuntimeLimits.MaxLayerCapacity];
                _layerHeaders[i] = "Layer " + i;
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
                EditorGUILayout.PropertyField(_animator, _animatorLabel);
                EditorGUILayout.PropertyField(_profile, _profileLabel);
                EditorGUILayout.PropertyField(_initializeOnAwake);
                EditorGUILayout.PropertyField(_logDiagnostics);
                DrawProfileValidation();
            }
        }

        /// <summary>Profile validation is throttled; building the report allocates and is never needed per repaint.</summary>
        private void DrawProfileValidation()
        {
            if (InspectorUiUtility.ShouldRefresh(ref _nextProfileValidation))
            {
                var profile = _profile.objectReferenceValue as AnimationLayerProfile;
                _profileReport = profile != null ? profile.GetValidationReport().TrimEnd() : string.Empty;
            }

            if (!string.IsNullOrEmpty(_profileReport))
            {
                EditorGUILayout.HelpBox(_profileReport, MessageType.Warning);
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
                        "Commits once per physics step. Animator.updateMode must be AnimatePhysics and Rigidbody interpolation enabled; a mismatch is reported as an error in development builds.",
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
                            "Backend root motion needs the Animator on this GameObject. The assigned Animator is on a child object, so the runtime will reject Backend ownership and log an error. Move the component, or use RootMotionOwnership.External.",
                            MessageType.Error);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "The runtime implements OnAnimatorMove in this mode. Subscribe a movement component to RootMotionApplied and remove any other OnAnimatorMove handler, otherwise the character will not move or root motion is applied twice.",
                            MessageType.Warning);
                    }
                }
                else if (ownership == RootMotionOwnership.External)
                {
                    EditorGUILayout.HelpBox(
                        "A movement component owns root motion and reads Animator.deltaPosition. The runtime does not implement OnAnimatorMove.",
                        MessageType.None);
                }

                EditorGUILayout.PropertyField(_dispatchClipEvents, _dispatchClipEventsLabel);

                EditorGUILayout.PropertyField(_updateRateHz);
                EditorGUILayout.PropertyField(_pauseWhenInvisible);
                EditorGUILayout.HelpBox(
                    "Update Rate Hz limits commits, not pose correctness: skipped frames accumulate their full delta. 0 commits every frame/step. Culling pauses commits only; graph state is preserved.",
                    MessageType.None);
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

            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox(
                    "Runtime data below describes the first selected object only. Authoring fields apply to all selected objects.",
                    MessageType.None);
            }

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
                CacheRuntime(behaviour, graph);
            }

            InspectorUiUtility.BeginPanel();
            for (int i = 0; i < StatusRowCount; i++)
            {
                InspectorUiUtility.DrawStatusRow(StatusLabels[i], _statusValues[i], _statusColors[i]);
            }

            InspectorUiUtility.EndPanel();

            for (int layer = 0; layer < _layerCount; layer++)
            {
                DrawLayer(layer);
            }

            if (refreshed && Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawLayer(int layer)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_layerHeaders[layer], EditorStyles.miniBoldLabel, GUILayout.Width(60f));

            Color barColor = _layerExternal[layer] ? InspectorUiUtility.WarningColor : InspectorUiUtility.RuntimeColor;
            InspectorUiUtility.DrawBar(_layerWeights[layer], barColor, _layerWeightLabels[layer]);

            EditorGUILayout.LabelField(_layerOccupancy[layer], EditorStyles.miniLabel, GUILayout.Width(64f));
            EditorGUILayout.EndHorizontal();

            if (_layerExternal[layer])
            {
                EditorGUILayout.LabelField("  driven by an external Playable (locomotion blend)", EditorStyles.miniLabel);
                return;
            }

            int count = _layerSnapshotCount[layer];
            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.LabelField(_layerRows[layer][i], EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// The only place where runtime display strings are built. Runs at most four times per second, so a
        /// repaint or a mouse-move over the inspector allocates nothing.
        /// </summary>
        private void CacheRuntime(AnimationRuntimeBehaviour behaviour, AnimationRuntimeGraph graph)
        {
            _stats = graph.GetStats();
            _layerCount = graph.LayerCount;

            _statusValues[0] = _layerCount.ToString();
            _statusValues[1] = _stats.TimePolicy.ToString();
            _statusValues[2] = _stats.PlayableCount.ToString();
            _statusValues[3] = _stats.ActivePlaybacks + " / peak " + _stats.PeakPlaybacks;
            _statusValues[4] = _stats.DroppedPlaybacks.ToString();
            _statusValues[5] = _stats.RecreatedPlayables.ToString();
            _statusValues[6] = _stats.ReusedPlayables.ToString();
            _statusValues[7] = _stats.Commits.ToString();
            _statusValues[8] = _stats.Evaluations.ToString();
            _statusValues[9] = _stats.NativeWrites.ToString();
            _statusValues[10] = graph.DroppedClipEvents.ToString();
            _statusValues[11] = graph.ComputePoseHash().ToString("X16");
            _statusValues[12] = "0x" + behaviour.PhaseOrderMask.ToString("X2");

            for (int i = 0; i < StatusRowCount; i++)
            {
                _statusColors[i] = InspectorUiUtility.NeutralColor;
            }

            _statusColors[1] = InspectorUiUtility.RuntimeColor;
            if (_stats.ActivePlaybacks > 0) _statusColors[3] = InspectorUiUtility.SuccessColor;
            if (_stats.DroppedPlaybacks > 0 || graph.DroppedClipEvents > 0) _statusColors[4] = InspectorUiUtility.WarningColor;
            if (graph.DroppedClipEvents > 0) _statusColors[10] = InspectorUiUtility.WarningColor;
            if (_stats.RecreatedPlayables > 0) _statusColors[5] = InspectorUiUtility.WarningColor;
            if (_stats.ReusedPlayables > 0) _statusColors[6] = InspectorUiUtility.SuccessColor;
            if (_stats.Evaluations < _stats.Commits) _statusColors[8] = InspectorUiUtility.SuccessColor;

            int layers = _layerCount < _layerSnapshots.Length ? _layerCount : _layerSnapshots.Length;
            for (int layer = 0; layer < layers; layer++)
            {
                _layerWeights[layer] = graph.GetLayerWeight(layer);
                _layerActive[layer] = graph.GetLayerActivePlaybacks(layer);
                _layerCapacity[layer] = graph.GetLayerCapacity(layer);
                _layerExternal[layer] = graph.IsLayerExternallySourced(layer);
                _layerSnapshotCount[layer] = graph.CopyPlaybackSnapshots(layer, _layerSnapshots[layer]);

                _layerWeightLabels[layer] = _layerWeights[layer].ToString("0.000");
                _layerOccupancy[layer] = _layerExternal[layer]
                    ? "external"
                    : _layerActive[layer] + "/" + _layerCapacity[layer];

                for (int i = 0; i < _layerSnapshotCount[layer]; i++)
                {
                    AnimationPlaybackSnapshot playback = _layerSnapshots[layer][i];
                    _layerRows[layer][i] = "  #" + playback.InstanceId
                        + " ch " + playback.PlaybackChannel
                        + " clip " + playback.ClipChannel
                        + "  t " + playback.Time.ToString("0.000")
                        + "  w " + playback.Weight.ToString("0.00")
                        + (playback.Connected ? string.Empty : "  (pending)");
                }
            }
        }
    }
}
