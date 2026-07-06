using System;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Scene binding for a single standalone <see cref="ChoreographyPlayer"/> driven through a
    /// <see cref="DirectProviderSink"/> (no cross-instance strategy). Use for a self-contained, non-competing
    /// choreography (e.g. a UI flourish or a one-off effect). For competing playback on shared channels, use
    /// <see cref="ChoreographySchedulerComponent"/> instead. The MonoBehaviour only coordinates lifecycle.
    /// </summary>
    public sealed class ChoreographyPlayerComponent : MonoBehaviour
    {
        [SerializeField] private ChoreographyAsset Choreography;
        [SerializeField] private bool PlayOnEnable = true;
        [SerializeField] private bool Loop;
        [SerializeField] private double Speed = 1d;
        [SerializeField] private int Channel;
        [SerializeField] private ChoreographyUnityClockMode ClockMode = ChoreographyUnityClockMode.GameTime;
        [SerializeField] private double FixedTickRate = 60d;
        [SerializeField] private bool AutoDiscoverProviders = true;
        [SerializeField] private ChoreographyLogLevel DiagnosticsLevel = ChoreographyLogLevel.Warning;

        private ChoreographyPlayer _player;
        private DirectProviderSink _sink;
        private IChoreographyDiagnostics _diagnostics;
        private ChoreographyUnityClockState _clockState;
        private bool _initialized;

        /// <summary>Raised for every timeline event crossed during playback.</summary>
        public event Action<ChoreographyEventInvocation> EventRaised;

        /// <summary>Raised when playback reaches the end without looping.</summary>
        public event Action PlaybackCompleted;

        public PlaybackStatus Status => _player != null ? _player.Status : PlaybackStatus.Idle;

        /// <summary>Initializes with a provider set. Preferred for production/DI composition.</summary>
        public void Initialize(IChoreographyProviderSet providers, IChoreographyDiagnostics diagnostics = null)
        {
            if (_initialized)
            {
                return;
            }
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            _diagnostics = diagnostics ?? new UnityChoreographyDiagnostics(DiagnosticsLevel);
            _player = new ChoreographyPlayer();
            _player.SetDiagnostics(_diagnostics);
            _sink = new DirectProviderSink(providers, _diagnostics);
            _sink.EventRaised += OnSinkEvent;
            _sink.PlaybackCompleted += OnSinkCompleted;
            _initialized = true;
        }

        /// <summary>Loads (if needed) and starts the assigned choreography.</summary>
        public void Play()
        {
            EnsureInitialized();
            if (!_initialized || Choreography == null)
            {
                return;
            }

            _player.Load(Choreography, new ChoreographyPlaybackContext(0, Channel, Speed, Loop), _sink);
            _player.Play();
        }

        public void Stop()
        {
            if (_initialized)
            {
                _player.Stop();
            }
        }

        private void OnEnable()
        {
            if (PlayOnEnable)
            {
                Play();
            }
        }

        private void Update()
        {
            if (_initialized)
            {
                TickPlayerFromClock();
            }
        }

        private void OnDisable()
        {
            if (_initialized && _player.Status == PlaybackStatus.Playing)
            {
                _player.Stop();
            }
        }

        private void OnDestroy()
        {
            if (_sink != null)
            {
                _sink.EventRaised -= OnSinkEvent;
                _sink.PlaybackCompleted -= OnSinkCompleted;
            }
        }

        private void OnSinkEvent(ChoreographyEventInvocation invocation) => EventRaised?.Invoke(invocation);

        private void OnSinkCompleted(int instanceId) => PlaybackCompleted?.Invoke();

        private void TickPlayerFromClock()
        {
            if (ClockMode == ChoreographyUnityClockMode.FixedTick)
            {
                while (_clockState.TryCreateFixedStep(Time.deltaTime, FixedTickRate, out ChoreographyTimelineStep fixedStep))
                {
                    _player.Tick(fixedStep);
                }
                return;
            }

            _player.Tick(_clockState.CreateStep(ClockMode, Time.deltaTime, Time.unscaledDeltaTime));
        }

        private void EnsureInitialized()
        {
            if (_initialized || !AutoDiscoverProviders)
            {
                return;
            }

            ChoreographyProviderRegistry registry = new ChoreographyProviderRegistry();
            registry.RegisterAnimation(GetComponentInChildren<IAnimationProvider>(true));
            registry.RegisterAudio(GetComponentInChildren<IAudioProvider>(true));
            registry.RegisterVfx(GetComponentInChildren<IVfxProvider>(true));
            registry.RegisterResources(GetComponentInChildren<IResourceProvider>(true));
            Initialize(registry, new UnityChoreographyDiagnostics(DiagnosticsLevel));
        }
    }
}
