using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class PlayableMovementPresenter : MonoBehaviour,
        IMovementLocomotionSink, IMovementActionSink, IMovementAnimationBackend
    {
        [Serializable]
        private struct LocomotionBinding
        {
            public MovementStateType State;
            public AnimationClip Clip;
        }

        [Serializable]
        private struct ActionBinding
        {
            public MovementAnimationActionKind Kind;
            public AnimationClip Clip;
        }

        private static readonly LogChannel Log = RpgMovementLog.Channel;

        [SerializeField] private Animator animator;

        [Tooltip("One clip per movement state. Unassigned states keep the current locomotion clip.")]
        [SerializeField] private LocomotionBinding[] locomotionClips;

        [Tooltip("One-shot clips played on the additive action layer.")]
        [SerializeField] private ActionBinding[] actionClips;

        [SerializeField] private float blendHalfLifeSeconds = 0.08f;
        [SerializeField] private float actionFadeOutSeconds = 0.15f;

        private PlayableGraph _graph;
        private AnimationLayerMixerPlayable _root;
        private AnimationMixerPlayable _locomotionMixer;
        private AnimationMixerPlayable _actionMixer;
        private AnimationClipPlayable[] _locomotionPlayables;
        private AnimationClipPlayable[] _actionPlayables;
        private float[] _locomotionTarget;
        private float[] _locomotionWeight;
        private float[] _locomotionApplied;
        private float[] _actionWeight;
        private float[] _actionApplied;
        private bool _graphValid;
        private int _activeStateIndex = -1;
        private long _evaluationCount;

        public bool GraphIsValid { get { return _graphValid; } }
        public long EvaluationCount { get { return _evaluationCount; } }
        public int ActiveStateIndex { get { return _activeStateIndex; } }
        public int LocomotionClipCount { get { return locomotionClips == null ? 0 : locomotionClips.Length; } }
        public int ActionClipCount { get { return actionClips == null ? 0 : actionClips.Length; } }

        private void Awake()
        {
            if (!MovementAnimationBackendGuard.TryClaim(this, nameof(PlayableMovementPresenter))) return;
            BuildGraph();
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }

        private void OnDisable()
        {
            if (_graphValid && _graph.IsPlaying()) _graph.Stop();
        }

        private void OnEnable()
        {
            if (_graphValid && !_graph.IsPlaying()) _graph.Play();
        }

        private void BuildGraph()
        {
            if (animator == null) animator = GetComponent<Animator>();
            int locomotionCount = locomotionClips == null ? 0 : locomotionClips.Length;
            int actionCount = actionClips == null ? 0 : actionClips.Length;

            if (animator == null || locomotionCount == 0)
            {
                Log.Warning(
                    "[PlayableMovementPresenter] No Animator or no locomotion clips are assigned; " +
                    "the PlayableGraph is not created. Assign clips in the Inspector and re-enable the component.");
                return;
            }

            // Manual time update: evaluate on the render clock, behind the physics step.
            _graph = PlayableGraph.Create("CycloneGames.Movement.Presentation");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            _root = AnimationLayerMixerPlayable.Create(_graph, 2);
            _locomotionMixer = AnimationMixerPlayable.Create(_graph, locomotionCount);
            _actionMixer = AnimationMixerPlayable.Create(_graph, actionCount > 0 ? actionCount : 1);

            _root.ConnectInput(0, _locomotionMixer, 0);
            _root.SetInputWeight(0, 1f);
            _root.ConnectInput(1, _actionMixer, 0);
            _root.SetInputWeight(1, 0f);

            _locomotionPlayables = new AnimationClipPlayable[locomotionCount];
            _locomotionTarget = new float[locomotionCount];
            _locomotionWeight = new float[locomotionCount];
            _locomotionApplied = new float[locomotionCount];
            int firstValid = -1;
            for (int i = 0; i < locomotionCount; i++)
            {
                AnimationClip clip = locomotionClips[i].Clip;
                if (clip == null) continue;
                _locomotionPlayables[i] = AnimationClipPlayable.Create(_graph, clip);
                _locomotionMixer.ConnectInput(i, _locomotionPlayables[i], 0);
                _locomotionMixer.SetInputWeight(i, 0f);
                if (firstValid < 0) firstValid = i;
            }
            if (firstValid >= 0)
            {
                _locomotionTarget[firstValid] = 1f;
                _locomotionWeight[firstValid] = 1f;
                _locomotionApplied[firstValid] = 1f;
                _locomotionMixer.SetInputWeight(firstValid, 1f);
            }

            _actionPlayables = new AnimationClipPlayable[actionCount];
            _actionWeight = new float[actionCount];
            _actionApplied = new float[actionCount];
            for (int i = 0; i < actionCount; i++)
            {
                AnimationClip clip = actionClips[i].Clip;
                if (clip == null) continue;
                _actionPlayables[i] = AnimationClipPlayable.Create(_graph, clip);
                _actionMixer.ConnectInput(i, _actionPlayables[i], 0);
                _actionMixer.SetInputWeight(i, 0f);
            }

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Movement", animator);
            output.SetSourcePlayable(_root);
            _graph.Play();
            _graphValid = true;
        }

        private void DestroyGraph()
        {
            if (_graphValid && _graph.IsValid()) _graph.Destroy();
            _graphValid = false;
            _activeStateIndex = -1;
        }

        public void Apply(in MovementAnimationState state)
        {
            if (!_graphValid || locomotionClips == null) return;

            int index = -1;
            for (int i = 0; i < locomotionClips.Length; i++)
            {
                if (locomotionClips[i].State != state.State || locomotionClips[i].Clip == null) continue;
                index = i;
                break;
            }
            if (index < 0) return;

            _activeStateIndex = index;
            for (int i = 0; i < _locomotionTarget.Length; i++)
                _locomotionTarget[i] = i == index ? 1f : 0f;
        }

        public void Play(in MovementAnimationAction action)
        {
            if (!_graphValid || !action.IsValid || actionClips == null) return;
            for (int i = 0; i < actionClips.Length; i++)
            {
                if (actionClips[i].Kind != action.Kind || actionClips[i].Clip == null) continue;
                _actionPlayables[i].SetTime(0d);
                _actionWeight[i] = 1f;
                return;
            }
        }

        private void Update()
        {
            if (!_graphValid) return;

            float dt = Time.deltaTime;
            float halfLife = blendHalfLifeSeconds < 0.001f ? 0.001f : blendHalfLifeSeconds;
            float factor = 1f - Mathf.Exp(-dt / halfLife);
            if (factor < 0f) factor = 0f;
            if (factor > 1f) factor = 1f;

            for (int i = 0; i < _locomotionWeight.Length; i++)
            {
                float target = _locomotionTarget[i];
                float current = _locomotionWeight[i];
                float next = current + (target - current) * factor;
                _locomotionWeight[i] = next;
                _locomotionApplied[i] = next;
            }
            float locomotionSum = MovementAnimationBlendWeights.Normalize(_locomotionApplied);
            if (locomotionSum > 0f)
            {
                for (int i = 0; i < _locomotionApplied.Length; i++)
                    _locomotionMixer.SetInputWeight(i, _locomotionApplied[i]);
            }

            float fade = actionFadeOutSeconds < 0.001f ? 0.001f : actionFadeOutSeconds;
            for (int i = 0; i < _actionWeight.Length; i++)
            {
                float next = _actionWeight[i] <= 0f
                    ? 0f
                    : Mathf.Max(0f, _actionWeight[i] - dt / fade);
                _actionWeight[i] = next;
                _actionApplied[i] = next;
            }
            float actionSum = MovementAnimationBlendWeights.Normalize(_actionApplied);
            if (actionSum > 0f)
            {
                for (int i = 0; i < _actionApplied.Length; i++)
                    _actionMixer.SetInputWeight(i, _actionApplied[i]);
            }

            _root.SetInputWeight(1, MovementAnimationBlendWeights.LayerWeight(actionSum));

            _graph.Evaluate(dt);
            _evaluationCount++;
        }
    }
}
