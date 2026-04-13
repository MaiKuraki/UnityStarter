using System;
using System.Collections.Generic;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.Foundation2D.Runtime
{
    public sealed class SpriteSequenceController : MonoBehaviour
    {
        public enum PlayMode { Once = 0, Loop = 1, PingPong = 2 }
        public enum PlayDirection { Forward = 0, Reverse = 1 }
        public enum IntervalHoldFrame { Last = 0, First = 1, Blank = 2 }
        public enum UpdateDriver { MonoUpdate = 0, BurstManaged = 1 }

        [Header("Frames")]
        [SerializeField] private List<Sprite> frames = new();

        [Header("Playback")]
        [SerializeField, Min(0.01f)] private float frameRate = 12f;
        [SerializeField] private PlayMode playMode = PlayMode.Loop;
        [SerializeField] private PlayDirection playDirection = PlayDirection.Forward;
        [SerializeField] private UpdateDriver updateDriver = UpdateDriver.MonoUpdate;
        [SerializeField] private bool fallbackToMonoUpdateWhenBurstUnavailable = true;
        [SerializeField] private bool playOnEnable = false;
        [SerializeField] private bool ignoreTimeScale = true;
        [SerializeField, Min(0f)] private float speedMultiplier = 1f;
        [SerializeField] private bool useDiscreteSpeedMultiplier;
        [SerializeField, Min(2)] private int discreteSpeedStepCount = 5;
        [SerializeField] private Vector2 discreteSpeedMultiplierRange = new(0f, 2f);
        [SerializeField] private bool warnWhenSpeedOutOfRange = true;

        [Header("Loop")]
        [SerializeField, Min(0f)] private float loopInterval;
        [SerializeField] private IntervalHoldFrame intervalHoldFrame = IntervalHoldFrame.Last;
        [SerializeField] private bool useFiniteLoopCount;
        [SerializeField, Min(1)] private int maxLoopCount = 1;

        [Header("Renderer")]
        [Tooltip("Assign a component implementing ISpriteSequenceRenderer, e.g. UGUISequenceRenderer or SpriteRendererSequenceRenderer")]
        [SerializeField] private MonoBehaviour rendererComponent;

        private ISpriteSequenceRenderer _renderer;
        private SpriteSequencePlaybackState _state;
        private bool _loggedBurstFallbackWarning;
        private bool _speedRangeWarningIssued;

        private const string LogCategory = "SpriteSequence.Controller";

        public event Action<int> OnFrameChanged;
        public event Action OnLoopComplete;
        public event Action OnPlayComplete;

        public bool IsPlaying => _state.State == 1;
        public int CurrentFrame => _state.CurrentFrameIndex;
        public bool IsBurstDriven => updateDriver == UpdateDriver.BurstManaged;
        public bool IgnoreTimeScale => ignoreTimeScale;
        public UpdateDriver CurrentUpdateDriver => updateDriver;
        public float RawSpeedMultiplier => speedMultiplier;
        public float EffectiveSpeedMultiplier => ResolveEffectiveSpeedMultiplier(speedMultiplier);

        private void Awake()
        {
            ResolveRenderer();
            _state = default;
            _state.State = 0;

            if (frames.Count > 0 && _renderer != null)
            {
                _renderer.Initialize(frames);
                _renderer.ApplyFrame(0, true);
                _renderer.SetVisible(true);
            }
        }

        private void OnEnable()
        {
            SpriteSequenceBurstManager.RegisterKnownController(this);
            _loggedBurstFallbackWarning = false;
            _speedRangeWarningIssued = false;
            if (playOnEnable)
            {
                Play();
            }
        }

        private void OnDisable()
        {
            SpriteSequenceBurstManager.UnregisterKnownController(this);
        }

        private void Update()
        {
            if (updateDriver == UpdateDriver.BurstManaged)
            {
                if (!ShouldFallbackToMonoUpdateForBurstUnavailable())
                {
                    return;
                }
            }

            if (_renderer == null || frames.Count <= 1 || _state.State != 1)
            {
                return;
            }

            _state.TimescaleMultiplier = ResolveEffectiveSpeedMultiplier(speedMultiplier);

            int prevLoop = _state.CurrentLoopCount;
            int prevState = _state.State;
            int prevFrame = _state.CurrentFrameIndex;
            bool prevInterval = _state.IsInInterval;
            double now = GetClockTime();
            double dt = ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

            bool changed = _state.Update(dt, now);
            if (_state.CurrentLoopCount != prevLoop)
            {
                OnLoopComplete?.Invoke();
            }

            if (prevState == 1 && _state.State == 0)
            {
                OnPlayComplete?.Invoke();
            }

            if (!changed)
            {
                return;
            }

            if (_state.IsInInterval)
            {
                HandleIntervalVisual();
                return;
            }

            if (prevInterval && !_state.IsInInterval)
            {
                ApplyCurrentFrame(true);
                OnFrameChanged?.Invoke(_state.CurrentFrameIndex);
                return;
            }

            if (_state.CurrentFrameIndex != prevFrame)
            {
                ApplyCurrentFrame(false);
                OnFrameChanged?.Invoke(_state.CurrentFrameIndex);
            }
        }

        public void Play()
        {
            if (_renderer == null || frames.Count == 0)
            {
                return;
            }

            _state.InitializePlayback(
                (int)playDirection,
                frameRate,
                (int)playMode,
                frames.Count,
                ignoreTimeScale,
                ResolveEffectiveSpeedMultiplier(speedMultiplier),
                useFiniteLoopCount ? maxLoopCount : 0,
                loopInterval,
                (int)intervalHoldFrame);

            _renderer.Initialize(frames);
            _renderer.SetVisible(true);
            ApplyCurrentFrame(true);
            _state.SyncClock(GetClockTime());
            OnFrameChanged?.Invoke(_state.CurrentFrameIndex);
        }

        public void Pause()
        {
            if (_state.State != 1)
            {
                return;
            }

            _state.State = 2;
            _state.HasInitializedTime = false;
        }

        public void Resume()
        {
            if (_state.State != 2)
            {
                return;
            }

            _state.State = 1;
            _state.SyncClock(GetClockTime());
        }

        public void Stop()
        {
            _state.State = 0;
            _state.IsInInterval = false;
            _state.HasInitializedTime = false;
            _renderer?.SetVisible(true);

            if (frames.Count > 0)
            {
                _state.CurrentFrameIndex = 0;
                ApplyCurrentFrame(true);
            }
        }

        public void GoToFrame(int index)
        {
            if (_renderer == null || frames.Count == 0)
            {
                return;
            }

            _state.CurrentFrameIndex = Mathf.Clamp(index, 0, frames.Count - 1);
            _state.CurrentTime = 0;
            ApplyCurrentFrame(false);
            _state.SyncClock(GetClockTime());
            OnFrameChanged?.Invoke(_state.CurrentFrameIndex);
        }

        public SpriteSequencePlaybackState GetPlaybackState()
        {
            // Keep snapshot parameters in sync for BurstManaged path where Update() may early-return.
            _state.TimescaleMultiplier = ResolveEffectiveSpeedMultiplier(speedMultiplier);
            return _state;
        }

        public void SetSpeedMultiplier(float value)
        {
            speedMultiplier = Mathf.Max(0f, value);
            _state.TimescaleMultiplier = ResolveEffectiveSpeedMultiplier(speedMultiplier);
        }

        public float QuantizeSpeedMultiplier(float value)
        {
            return ResolveEffectiveSpeedMultiplier(Mathf.Max(0f, value));
        }

        public void SetUpdateDriver(UpdateDriver driver)
        {
            updateDriver = driver;
            _loggedBurstFallbackWarning = false;
        }

        public void SetPlaybackStateFromJob(SpriteSequencePlaybackState state, bool frameMayChange)
        {
            int prevLoop = _state.CurrentLoopCount;
            int prevState = _state.State;
            bool prevInterval = _state.IsInInterval;
            int prev = _state.CurrentFrameIndex;
            _state = state;

            if (_state.CurrentLoopCount != prevLoop)
            {
                OnLoopComplete?.Invoke();
            }

            if (prevState == 1 && _state.State == 0)
            {
                OnPlayComplete?.Invoke();
            }

            if (_renderer == null || frames.Count == 0)
            {
                return;
            }

            if (_state.IsInInterval)
            {
                HandleIntervalVisual();
                return;
            }

            if (prevInterval && !_state.IsInInterval)
            {
                ApplyCurrentFrame(true);
                OnFrameChanged?.Invoke(_state.CurrentFrameIndex);
                return;
            }

            if (frameMayChange && _state.CurrentFrameIndex != prev)
            {
                ApplyCurrentFrame(false);
                OnFrameChanged?.Invoke(_state.CurrentFrameIndex);
            }
        }

        private void ApplyCurrentFrame(bool forceRefresh)
        {
            if (_renderer == null || frames.Count == 0)
            {
                return;
            }

            int idx = Mathf.Clamp(_state.CurrentFrameIndex, 0, frames.Count - 1);
            _renderer.ApplyFrame(idx, forceRefresh);
            _renderer.SetVisible(true);
        }

        private void HandleIntervalVisual()
        {
            if (_renderer == null || frames.Count == 0)
            {
                return;
            }

            switch ((IntervalHoldFrame)_state.IntervalHoldFrame)
            {
                case IntervalHoldFrame.First:
                    {
                        int first = _state.DirectionSign == 1 ? 0 : frames.Count - 1;
                        _state.CurrentFrameIndex = first;
                        ApplyCurrentFrame(false);
                        break;
                    }
                case IntervalHoldFrame.Blank:
                    _renderer.SetVisible(false);
                    break;
                default:
                    _renderer.SetVisible(true);
                    break;
            }
        }

        private void ResolveRenderer()
        {
            _renderer = rendererComponent as ISpriteSequenceRenderer;
            if (_renderer != null)
            {
                return;
            }

            if (rendererComponent != null)
            {
                CLogger.LogError($"Assigned rendererComponent does not implement ISpriteSequenceRenderer. object={name}", LogCategory);
                return;
            }

            // Auto-resolve common renderers.
            _renderer = GetComponent<ISpriteSequenceRenderer>();
            if (_renderer == null)
            {
                CLogger.LogError($"No ISpriteSequenceRenderer found. Add UGUISequenceRenderer or SpriteRendererSequenceRenderer. object={name}", LogCategory);
            }
        }

        private double GetClockTime()
        {
#if UNITY_2020_2_OR_NEWER
            return ignoreTimeScale ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
#else
        return ignoreTimeScale ? Time.unscaledTime : Time.time;
#endif
        }

        private bool ShouldFallbackToMonoUpdateForBurstUnavailable()
        {
#if BURST_JOBS
            if (SpriteSequenceBurstManager.HasActiveManager && SpriteSequenceBurstManager.IsControllerManaged(this))
            {
                return false;
            }

            if (!_loggedBurstFallbackWarning)
            {
                CLogger.LogWarning($"UpdateDriver is BurstManaged but this controller is not managed by an active SpriteSequenceBurstManager. Falling back to MonoUpdate. object={name}", LogCategory);
                _loggedBurstFallbackWarning = true;
            }

            return fallbackToMonoUpdateWhenBurstUnavailable;
#else
        if (!_loggedBurstFallbackWarning)
        {
            CLogger.LogWarning($"UpdateDriver is BurstManaged but BURST_JOBS is not defined. Falling back to MonoUpdate. object={name}", LogCategory);
            _loggedBurstFallbackWarning = true;
        }

        return fallbackToMonoUpdateWhenBurstUnavailable;
#endif
        }

        private float ResolveEffectiveSpeedMultiplier(float raw)
        {
            float clamped = Mathf.Max(0f, raw);
            if (!useDiscreteSpeedMultiplier)
            {
                _speedRangeWarningIssued = false;
                return clamped;
            }

            float min = Mathf.Max(0f, Mathf.Min(discreteSpeedMultiplierRange.x, discreteSpeedMultiplierRange.y));
            float max = Mathf.Max(min, Mathf.Max(discreteSpeedMultiplierRange.x, discreteSpeedMultiplierRange.y));
            int steps = Mathf.Max(2, discreteSpeedStepCount);

            bool outOfRange = clamped < min || clamped > max;
            if (warnWhenSpeedOutOfRange)
            {
                if (outOfRange)
                {
                    if (!_speedRangeWarningIssued)
                    {
                        CLogger.LogWarning($"SpeedMultiplier {clamped:F3} is out of discrete range [{min:F3}, {max:F3}]. It will be clamped before quantization. object={name}", LogCategory);
                        _speedRangeWarningIssued = true;
                    }
                }
                else
                {
                    _speedRangeWarningIssued = false;
                }
            }
            else
            {
                _speedRangeWarningIssued = false;
            }

            float v = Mathf.Clamp(clamped, min, max);
            float span = max - min;
            if (span <= 0.000001f)
            {
                return min;
            }

            float normalized = (v - min) / span;
            int index = Mathf.RoundToInt(normalized * (steps - 1));
            float t = index / (float)(steps - 1);
            return min + t * span;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            frameRate = Mathf.Max(0.01f, frameRate);
            speedMultiplier = Mathf.Max(0f, speedMultiplier);
            loopInterval = Mathf.Max(0f, loopInterval);
            maxLoopCount = Mathf.Max(1, maxLoopCount);
            discreteSpeedStepCount = Mathf.Max(2, discreteSpeedStepCount);
            discreteSpeedMultiplierRange.x = Mathf.Max(0f, discreteSpeedMultiplierRange.x);
            discreteSpeedMultiplierRange.y = Mathf.Max(0f, discreteSpeedMultiplierRange.y);
        }
#endif
    }
}