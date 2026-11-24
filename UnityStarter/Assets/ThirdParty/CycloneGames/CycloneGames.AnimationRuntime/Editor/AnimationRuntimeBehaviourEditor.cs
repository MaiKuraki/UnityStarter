using CycloneGames.AnimationRuntime.Core;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Inspector for <see cref="AnimationRuntimeBehaviour"/>: authoring groups, field-scoped validation,
    /// and a throttled runtime panel fed by the graph's zero-allocation snapshot API.
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
        private SerializedProperty _dispatchClipEvents;
        private SerializedProperty _updateRateHz;
        private SerializedProperty _pauseWhenInvisible;

        private readonly AnimationSetupFinding[] _findings =
            new AnimationSetupFinding[AnimationRuntimeSetupValidator.MaxFindings];
        private string _subtitle = "Default profile";
        private Object _lastProfile;
        private int _lastPhase = -1;
        private double _nextSetupRefresh;
        private bool _hasRenderer;
        private bool _hasTimelineProvider;
        private int _findingCount;

        private GUIContent _animatorLabel;
        private GUIContent _profileLabel;
        private GUIContent _dispatchClipEventsLabel;

        private bool _bindingFoldout = true;
        private bool _setupFoldout = true;
        private bool _frameFoldout = true;
        private bool _runtimeFoldout = true;
        private double _nextProfileValidation;
        private string _profileReport = string.Empty;

        private AnimationRuntimeRuntimePanel _runtimePanel;

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

            _runtimePanel = new AnimationRuntimeRuntimePanel(this);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawTitle(
                "Animation Runtime",
                BuildSubtitle(),
                InspectorUiUtility.RuntimeColor);

            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox(
                    "Authoring fields apply to all selected objects. Validation and runtime data describe the first object.",
                    MessageType.None);
            }

            ValidateSetup();

            DrawBinding();
            DrawSetup();
            DrawFrameModel();
            DrawRuntimeBlock();

            serializedObject.ApplyModifiedProperties();
        }

        private void ValidateSetup()
        {
            RefreshSetupCache();

            var component = (Component)target;
            Animator animator = _animator.objectReferenceValue as Animator;
            if (animator == null && component != null)
            {
                animator = component.GetComponentInChildren<Animator>(true);
            }

            var behaviour = (AnimationRuntimeBehaviour)target;
            bool onThisGameObject = animator != null && component != null
                && animator.gameObject == component.gameObject;

            _findingCount = AnimationRuntimeSetupValidator.Validate(
                animator,
                onThisGameObject,
                (AnimationCommitPhase)_commitPhase.enumValueIndex,
                (RootMotionOwnership)_rootMotionOwnership.enumValueIndex,
                _dispatchClipEvents.boolValue,
                animator != null && animator.fireEvents,
                behaviour != null && behaviour.HasIKPass,
                _pauseWhenInvisible.boolValue,
                _hasRenderer,
                _hasTimelineProvider,
                _findings);
        }

        private void DrawFindings(AnimationSetupField field)
        {
            for (int i = 0; i < _findingCount; i++)
            {
                AnimationSetupFinding finding = _findings[i];
                if (finding.Field != field)
                {
                    continue;
                }

                EditorGUILayout.HelpBox(finding.Message, ToMessageType(finding.Severity));
            }
        }

        private void RefreshSetupCache()
        {
            if (!InspectorUiUtility.ShouldRefresh(ref _nextSetupRefresh))
            {
                return;
            }

            var component = (Component)target;
            _hasRenderer = component != null && component.GetComponent<Renderer>() != null;
            _hasTimelineProvider = component != null
                && component.GetComponent("UnityPlayableAnimationProviderBehaviour") != null;
        }

        private static MessageType ToMessageType(AnimationSetupSeverity severity)
        {
            switch (severity)
            {
                case AnimationSetupSeverity.Error:
                    return MessageType.Error;
                case AnimationSetupSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private string BuildSubtitle()
        {
            Object profile = _profile.objectReferenceValue;
            int phase = _commitPhase.enumValueIndex;
            if (!ReferenceEquals(profile, _lastProfile) || phase != _lastPhase)
            {
                _lastProfile = profile;
                _lastPhase = phase;
                string profileName = profile != null ? profile.name : "Default profile";
                _subtitle = profileName + " · " + _commitPhase.enumDisplayNames[phase];
            }

            return _subtitle;
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
                if (_profile.objectReferenceValue == null)
                {
                    EditorGUILayout.LabelField("Using default: 1 layer x 4 slots.", EditorStyles.miniLabel);
                }

                DrawProfileValidation();
                DrawFindings(AnimationSetupField.Binding);
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

        private void DrawSetup()
        {
            if (!InspectorUiUtility.DrawSectionHeader(ref _setupFoldout, "Setup", InspectorUiUtility.NeutralColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_initializeOnAwake);
                EditorGUILayout.PropertyField(_logDiagnostics);
                EditorGUILayout.PropertyField(_dispatchClipEvents, _dispatchClipEventsLabel);
                DrawFindings(AnimationSetupField.Setup);
            }
        }

        private void DrawFrameModel()
        {
            if (!InspectorUiUtility.DrawSectionHeader(ref _frameFoldout, "Frame", InspectorUiUtility.NeutralColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_timePolicy);
                var policy = (AnimationTimePolicy)_timePolicy.enumValueIndex;

                if (policy == AnimationTimePolicy.TickDriven)
                {
                    EditorGUILayout.HelpBox(
                        "Tick-driven: the component advances clips from its own clock. UpdatePlayback then changes weight, speed and loop only.",
                        MessageType.Info);
                }

                EditorGUILayout.PropertyField(_commitPhase);
                var phase = (AnimationCommitPhase)_commitPhase.enumValueIndex;

                if (phase == AnimationCommitPhase.FixedUpdate)
                {
                    EditorGUILayout.HelpBox(
                        "Commits once per physics step. Animator.updateMode must be the fixed update mode; a mismatch is flagged below.",
                        MessageType.Info);
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
                    EditorGUILayout.LabelField(
                        "Root motion is read by a movement component; this runtime does not implement OnAnimatorMove.",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(4f);
                EditorGUILayout.PropertyField(_updateRateHz);
                EditorGUILayout.PropertyField(_pauseWhenInvisible);
                EditorGUILayout.LabelField(
                    "0 = every frame or fixed step. Skipped frames carry their full delta; culling pauses commits only.",
                    EditorStyles.miniLabel);
                DrawFindings(AnimationSetupField.Frame);
            }
        }

        private void DrawRuntimeBlock()
        {
            if (!InspectorUiUtility.DrawSectionHeader(ref _runtimeFoldout, "Runtime", InspectorUiUtility.RuntimeColor))
            {
                return;
            }

            _runtimePanel.Draw((AnimationRuntimeBehaviour)target);
        }
    }
}
