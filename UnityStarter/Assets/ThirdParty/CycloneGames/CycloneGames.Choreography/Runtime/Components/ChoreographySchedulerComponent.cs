using System;
using CycloneGames.Choreography.Core;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Unity-side time authority selection for scene components that drive choreography playback.
    /// </summary>
    public enum ChoreographyUnityClockMode
    {
        GameTime = 0,
        UnscaledTime = 1,
        FixedTick = 2,
        AudioDspTime = 3
    }

    internal struct ChoreographyUnityClockState
    {
        private double _fixedAccumulator;
        private long _fixedTickIndex;
        private bool _hasDspSample;
        private double _lastDspTime;
        private long _dspTickIndex;

        public ChoreographyTimelineStep CreateStep(ChoreographyUnityClockMode mode, double gameDeltaTime, double unscaledDeltaTime)
        {
            switch (mode)
            {
                case ChoreographyUnityClockMode.UnscaledTime:
                    return ChoreographyTimelineStep.FromDelta(unscaledDeltaTime, ChoreographyClockKind.ManualDelta);

                case ChoreographyUnityClockMode.AudioDspTime:
                    return CreateAudioDspStep();

                default:
                    return ChoreographyTimelineStep.FromDelta(gameDeltaTime, ChoreographyClockKind.ManualDelta);
            }
        }

        public bool TryCreateFixedStep(double sourceDeltaTime, double tickRate, out ChoreographyTimelineStep step)
        {
            step = default;
            if (tickRate <= 0d)
            {
                tickRate = 60d;
            }

            double tickDuration = 1d / tickRate;
            _fixedAccumulator += sourceDeltaTime > 0d ? sourceDeltaTime : 0d;
            if (_fixedAccumulator + 0.000000000001d < tickDuration)
            {
                return false;
            }

            _fixedAccumulator -= tickDuration;
            step = ChoreographyTimelineStep.FromDelta(
                tickDuration,
                ChoreographyClockKind.FixedTick,
                _fixedTickIndex++,
                tickRate);
            return true;
        }

        private ChoreographyTimelineStep CreateAudioDspStep()
        {
            double dspTime = AudioSettings.dspTime;
            if (!_hasDspSample)
            {
                _hasDspSample = true;
                _lastDspTime = dspTime;
                return ChoreographyTimelineStep.FromDelta(0d, ChoreographyClockKind.AudioDspTime, _dspTickIndex++, 0d, dspTime);
            }

            double deltaTime = dspTime - _lastDspTime;
            _lastDspTime = dspTime;
            return ChoreographyTimelineStep.FromDelta(deltaTime, ChoreographyClockKind.AudioDspTime, _dspTickIndex++, 0d, dspTime);
        }
    }

    /// <summary>
    /// Scene binding for a <see cref="ChoreographyScheduler"/>. This MonoBehaviour only coordinates lifecycle and
    /// forwards the update loop; all scheduling logic lives in the engine-free Core. A composition root wires
    /// providers via <see cref="Initialize"/>; if left uninitialized, the component best-effort discovers sibling
    /// components implementing the Core provider interfaces so it also works with a plain drag-and-drop setup.
    /// </summary>
    public sealed class ChoreographySchedulerComponent : MonoBehaviour
    {
        [Tooltip("When true and no providers are injected before the first update, discover sibling provider components.")]
        [SerializeField] private bool AutoDiscoverProviders = true;

        [Tooltip("Minimum diagnostics level routed to CycloneGames.Logger.")]
        [SerializeField] private ChoreographyLogLevel DiagnosticsLevel = ChoreographyLogLevel.Warning;

        [Tooltip("Clock authority used to advance the choreography scheduler.")]
        [SerializeField] private ChoreographyUnityClockMode ClockMode = ChoreographyUnityClockMode.GameTime;

        [Tooltip("Fixed tick rate used when Clock Mode is Fixed Tick.")]
        [SerializeField] private double FixedTickRate = 60d;

        private ChoreographyScheduler _scheduler;
        private IChoreographyDiagnostics _diagnostics;
        private ChoreographyUnityClockState _clockState;
        private bool _initialized;

        /// <summary>Raised for every timeline event crossed by any instance. Wire gameplay bridges here.</summary>
        public event Action<ChoreographyEventInvocation> EventRaised;

        /// <summary>Raised when a scheduled instance ends (completes or is stopped).</summary>
        public event Action<int> InstanceEnded;

        public bool IsInitialized => _initialized;

        public int ActiveCount => _scheduler != null ? _scheduler.ActiveCount : 0;

        /// <summary>
        /// Explicitly initializes the scheduler with a provider set and optional diagnostics. Preferred for
        /// production/DI composition. Safe to call once; subsequent calls are ignored.
        /// </summary>
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
            _scheduler = new ChoreographyScheduler(providers, _diagnostics);
            _scheduler.EventRaised += OnSchedulerEvent;
            _scheduler.InstanceEnded += OnSchedulerInstanceEnded;
            _initialized = true;
        }

        /// <summary>Requests playback. Returns a positive instance id, or <see cref="ChoreographyScheduler.InvalidInstanceId"/>.</summary>
        public int Play(IChoreographyAsset asset, in ChoreographyPlayRequest request)
        {
            EnsureInitialized();
            if (!_initialized)
            {
                return ChoreographyScheduler.InvalidInstanceId;
            }
            return _scheduler.Play(asset, request);
        }

        public void Stop(int instanceId)
        {
            if (_initialized)
            {
                _scheduler.Stop(instanceId);
            }
        }

        public void StopChannel(int channel)
        {
            if (_initialized)
            {
                _scheduler.StopChannel(channel);
            }
        }

        private void Update()
        {
            EnsureInitialized();
            if (_initialized)
            {
                TickSchedulerFromClock();
            }
        }

        private void OnDestroy()
        {
            if (!_initialized)
            {
                return;
            }
            _scheduler.StopAll();
            _scheduler.EventRaised -= OnSchedulerEvent;
            _scheduler.InstanceEnded -= OnSchedulerInstanceEnded;
        }

        private void OnSchedulerEvent(ChoreographyEventInvocation invocation) => EventRaised?.Invoke(invocation);

        private void OnSchedulerInstanceEnded(int instanceId) => InstanceEnded?.Invoke(instanceId);

        private void TickSchedulerFromClock()
        {
            if (ClockMode == ChoreographyUnityClockMode.FixedTick)
            {
                while (_clockState.TryCreateFixedStep(Time.deltaTime, FixedTickRate, out ChoreographyTimelineStep fixedStep))
                {
                    _scheduler.Tick(fixedStep);
                }
                return;
            }

            _scheduler.Tick(_clockState.CreateStep(ClockMode, Time.deltaTime, Time.unscaledDeltaTime));
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
