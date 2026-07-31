using System;
using CycloneGames.Choreography.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Choreography.UnityAnimation
{
    /// <summary>
    /// Component wrapper for <see cref="UnityPlayableAnimationProvider"/>. It can be auto-discovered by Choreography
    /// player and scheduler components as an <see cref="IAnimationProvider"/>.
    /// </summary>
    public sealed class UnityPlayableAnimationProviderBehaviour : MonoBehaviour, IAnimationProvider
    {
        [Tooltip("Animator sampled by the Choreography playable graph. Leave empty to use a child Animator.")]
        [SerializeField] private Animator TargetAnimator;

        [Tooltip("Optional component implementing IUnityChoreographyResourceResolver. Leave empty to auto-discover one in children.")]
        [SerializeField] private MonoBehaviour ResourceResolver;

        [Tooltip("When true, graph evaluation is batched once in LateUpdate instead of after every provider sample.")]
        [SerializeField] private bool EvaluateInLateUpdate = true;

        [Tooltip("Initial voice capacity for the playable mixer.")]
        [SerializeField] private int InitialCapacity = 4;

        private UnityPlayableAnimationProvider _provider;
        private IUnityChoreographyResourceResolver _resolver;
        private ILogWriter _logWriter;
        private LogChannel _log = ChoreographyUnityAnimationLog.Channel;
        private bool _warnedUninitialized;

        public void Initialize(IUnityChoreographyResourceResolver resolver)
        {
            InitializeCore(resolver, null);
        }

        public void Initialize(IUnityChoreographyResourceResolver resolver, ILogWriter logWriter)
        {
            InitializeCore(resolver, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        private void InitializeCore(IUnityChoreographyResourceResolver resolver, ILogWriter logWriter)
        {
            _resolver = resolver;
            _logWriter = logWriter;
            _log = logWriter == null
                ? ChoreographyUnityAnimationLog.Channel
                : ChoreographyUnityAnimationLog.Create(logWriter);
            BuildProvider();
        }

        public void BeginClip(in ChoreographyPlaybackSample sample)
        {
            EnsureProvider();
            if (_provider == null)
            {
                WarnUninitialized();
                return;
            }

            _provider.BeginClip(in sample);
        }

        public void UpdateClip(in ChoreographyPlaybackSample sample)
        {
            _provider?.UpdateClip(in sample);
        }

        public void EndClip(in ChoreographyClipStop stop)
        {
            _provider?.EndClip(in stop);
        }

        private void LateUpdate()
        {
            if (EvaluateInLateUpdate)
            {
                _provider?.Evaluate();
            }
        }

        private void OnDestroy()
        {
            _provider?.Dispose();
        }

        private void EnsureProvider()
        {
            if (_provider != null)
            {
                return;
            }

            if (_resolver == null)
            {
                _resolver = ResolveResourceProvider();
            }
            BuildProvider();
        }

        private void BuildProvider()
        {
            if (_provider != null)
            {
                return;
            }

            Animator animator = TargetAnimator != null ? TargetAnimator : GetComponentInChildren<Animator>(true);
            if (animator == null || _resolver == null)
            {
                return;
            }

            _provider = _logWriter == null
                ? new UnityPlayableAnimationProvider(
                    animator,
                    _resolver,
                    !EvaluateInLateUpdate,
                    InitialCapacity)
                : new UnityPlayableAnimationProvider(
                    animator,
                    _resolver,
                    _logWriter,
                    !EvaluateInLateUpdate,
                    InitialCapacity);
        }

        private IUnityChoreographyResourceResolver ResolveResourceProvider()
        {
            if (ResourceResolver is IUnityChoreographyResourceResolver assigned)
            {
                return assigned;
            }

            return GetComponentInChildren<IUnityChoreographyResourceResolver>(true);
        }

        private void WarnUninitialized()
        {
            if (_warnedUninitialized)
            {
                return;
            }

            _warnedUninitialized = true;
            if (_log.IsEnabled(LogSeverity.Warning))
            {
                _log.Warning(
                    "UnityPlayableAnimationProviderBehaviour has no Animator or IUnityChoreographyResourceResolver; animation playback is disabled.");
            }
        }
    }
}
