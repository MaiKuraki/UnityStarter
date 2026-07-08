using CycloneGames.Choreography.Core;
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
        private IChoreographyDiagnostics _diagnostics;
        private bool _warnedUninitialized;

        public void Initialize(IUnityChoreographyResourceResolver resolver, IChoreographyDiagnostics diagnostics = null)
        {
            _resolver = resolver;
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
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

            if (_diagnostics == null)
            {
                _diagnostics = NullChoreographyDiagnostics.Instance;
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

            _provider = new UnityPlayableAnimationProvider(
                animator,
                _resolver,
                _diagnostics,
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
            if (_diagnostics != null && _diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography.UnityAnimation",
                    "UnityPlayableAnimationProviderBehaviour has no Animator or IUnityChoreographyResourceResolver; animation playback is disabled.");
            }
        }
    }
}
