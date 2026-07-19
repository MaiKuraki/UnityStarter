using System;
using System.Collections.Generic;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.Foundation2D.Runtime
{
    internal struct SpriteSequenceBatchToken
    {
        internal ulong CommandRevision;
        internal ulong OwnershipGeneration;
    }

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
        [SerializeField] private bool playOnEnable;
        [SerializeField] private bool ignoreTimeScale = true;
        [SerializeField, Min(1)] private int maxFrameAdvancesPerUpdate = 64;
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
        private UnityEngine.Object _batchOwner;
        private ulong _commandRevision;
        private ulong _ownershipGeneration;
        private bool _hasStarted;
        private bool _hasPlaybackState;
        private bool _playOnEnablePending;
        private bool _loggedBurstFallbackWarning;
        private bool _speedRangeWarningIssued;
        private bool _invalidTimingWarningIssued;
        private bool _catchUpWarningIssued;

        private const string LogCategory = "SpriteSequence.Controller";
        private const float MinimumFrameRate = 0.01f;

        public event Action<int> OnFrameChanged;
        public event Action OnLoopComplete;
        public event Action OnPlayComplete;

        public bool IsPlaying => _state.IsPlaying;
        public bool IsPaused => _state.IsPaused;
        public int CurrentFrame => _state.CurrentFrameIndex;
        public bool IsBurstDriven => updateDriver == UpdateDriver.BurstManaged;
        public bool IgnoreTimeScale => ignoreTimeScale;
        public UpdateDriver CurrentUpdateDriver => updateDriver;
        public float RawSpeedMultiplier => speedMultiplier;
        public float EffectiveSpeedMultiplier => ResolveEffectiveSpeedMultiplier(speedMultiplier);

        private void Awake()
        {
            frames ??= new List<Sprite>();
            updateDriver = ResolveUpdateDriver(updateDriver);
            maxFrameAdvancesPerUpdate = ResolveMaxFrameAdvances(maxFrameAdvancesPerUpdate);
            ResolveRenderer();
            _state = default;
        }

        private void OnEnable()
        {
            _loggedBurstFallbackWarning = false;
            _speedRangeWarningIssued = false;
            _invalidTimingWarningIssued = false;
            _catchUpWarningIssued = false;
            _playOnEnablePending = playOnEnable;

            if (_hasStarted && playOnEnable)
            {
                Play();
            }
        }

        private void Start()
        {
            _hasStarted = true;
            if (_renderer == null)
            {
                ResolveRenderer();
            }

            if (_hasPlaybackState)
            {
                if (TryInitializeRenderer())
                {
                    TryApplyCurrentVisual(true);
                }

                return;
            }

            if (_playOnEnablePending)
            {
                Play();
                return;
            }

            InitializeRendererAtRest();
        }

        private void OnDisable()
        {
            _playOnEnablePending = false;
            InvalidateBatchSnapshots();
        }

        private void Update()
        {
            if (updateDriver == UpdateDriver.BurstManaged)
            {
                if (HasLiveBatchOwner())
                {
                    return;
                }

                if (!fallbackToMonoUpdateWhenBurstUnavailable)
                {
                    LogBurstFallbackWarning();
                    return;
                }

                LogBurstFallbackWarning();
            }

            Tick(ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        public void Play()
        {
            frames ??= new List<Sprite>();
            if (_renderer == null)
            {
                ResolveRenderer();
            }

            if (_renderer == null || frames.Count == 0 || !TryInitializeRenderer())
            {
                return;
            }

            InvalidateBatchSnapshots();
            _catchUpWarningIssued = false;
            int previousFrame = _state.CurrentFrameIndex;
            SpriteSequenceAdvanceResult result = _state.Initialize(
                ResolveDirection(playDirection),
                ResolveFiniteFrameRate(frameRate),
                ResolvePlaybackMode(playMode),
                frames.Count,
                ResolveEffectiveSpeedMultiplier(speedMultiplier),
                useFiniteLoopCount ? ResolvePositiveLoopCount(maxLoopCount) : 0,
                ResolveFiniteNonNegative(loopInterval),
                ResolveIntervalHoldMode(intervalHoldFrame));

            _hasPlaybackState = true;
            CommitVisualAndPublish(previousFrame, result, true, true, _commandRevision);
        }

        public void Pause()
        {
            if (!_state.IsPlaying)
            {
                return;
            }

            InvalidateBatchSnapshots();
            _state.Pause();
        }

        public void Resume()
        {
            if (!_state.IsPaused)
            {
                return;
            }

            InvalidateBatchSnapshots();
            _state.Resume();
        }

        public void Stop()
        {
            InvalidateBatchSnapshots();
            if (frames != null && frames.Count > 0)
            {
                _state.TotalFrameCount = frames.Count;
            }

            _state.Stop(0);
            _hasPlaybackState = true;
            TryApplyCurrentVisual(true);
        }

        public void GoToFrame(int index)
        {
            frames ??= new List<Sprite>();
            if (_renderer == null)
            {
                ResolveRenderer();
            }

            if (_renderer == null || frames.Count == 0)
            {
                return;
            }

            if (!_hasPlaybackState && !TryInitializeRenderer())
            {
                return;
            }

            InvalidateBatchSnapshots();
            int previousFrame = _state.CurrentFrameIndex;
            _state.TotalFrameCount = frames.Count;
            _state.Seek(index);
            _hasPlaybackState = true;

            SpriteSequenceAdvanceResult result = default;
            if (_state.CurrentFrameIndex != previousFrame)
            {
                result.Add(SpriteSequenceAdvanceFlags.FrameChanged);
            }

            CommitVisualAndPublish(previousFrame, result, true, true, _commandRevision);
        }

        public void SetSpeedMultiplier(float value)
        {
            float resolved = ResolveFiniteNonNegative(value);
            if (Mathf.Approximately(speedMultiplier, resolved))
            {
                return;
            }

            speedMultiplier = resolved;
            _state.SpeedMultiplier = ResolveEffectiveSpeedMultiplier(speedMultiplier);
            InvalidateBatchSnapshots();
        }

        public float QuantizeSpeedMultiplier(float value)
        {
            return ResolveEffectiveSpeedMultiplier(ResolveFiniteNonNegative(value));
        }

        public void SetUpdateDriver(UpdateDriver driver)
        {
            driver = ResolveUpdateDriver(driver);
            if (updateDriver == driver)
            {
                return;
            }

            updateDriver = driver;
            _loggedBurstFallbackWarning = false;
            InvalidateBatchSnapshots();
        }

        internal bool TryClaimBatchOwnership(UnityEngine.Object owner)
        {
            if (owner == null)
            {
                return false;
            }

            if (_batchOwner != null && _batchOwner != owner)
            {
                return false;
            }

            if (_batchOwner == owner)
            {
                return true;
            }

            _batchOwner = owner;
            _ownershipGeneration = NextRevision(_ownershipGeneration);
            InvalidateBatchSnapshots();
            _loggedBurstFallbackWarning = false;
            return true;
        }

        internal void ReleaseBatchOwnership(UnityEngine.Object owner)
        {
            if (owner == null || _batchOwner != owner)
            {
                return;
            }

            _batchOwner = null;
            _ownershipGeneration = NextRevision(_ownershipGeneration);
            InvalidateBatchSnapshots();
        }

        internal bool TryCaptureBatchSnapshot(
            UnityEngine.Object owner,
            double scaledDeltaTime,
            double unscaledDeltaTime,
            out SpriteSequencePlaybackState state,
            out double deltaTime,
            out int maxFrameAdvances,
            out SpriteSequenceBatchToken token)
        {
            state = default;
            deltaTime = 0d;
            maxFrameAdvances = 1;
            token = default;

            if (owner == null || _batchOwner != owner ||
                updateDriver != UpdateDriver.BurstManaged ||
                !isActiveAndEnabled || !_state.IsPlaying ||
                _renderer == null || frames == null || frames.Count == 0)
            {
                return false;
            }

            _state.SpeedMultiplier = ResolveEffectiveSpeedMultiplier(speedMultiplier);
            state = _state;
            deltaTime = ignoreTimeScale ? unscaledDeltaTime : scaledDeltaTime;
            maxFrameAdvances = ResolveMaxFrameAdvances(maxFrameAdvancesPerUpdate);
            token.CommandRevision = _commandRevision;
            token.OwnershipGeneration = _ownershipGeneration;
            return true;
        }

        internal bool TryApplyBatchResult(
            UnityEngine.Object owner,
            in SpriteSequenceBatchToken token,
            in SpriteSequencePlaybackState state,
            in SpriteSequenceAdvanceResult result)
        {
            if (owner == null || _batchOwner != owner ||
                updateDriver != UpdateDriver.BurstManaged ||
                token.CommandRevision != _commandRevision ||
                token.OwnershipGeneration != _ownershipGeneration)
            {
                return false;
            }

            int previousFrame = _state.CurrentFrameIndex;
            _state = state;
            CommitVisualAndPublish(previousFrame, result, false, false, token.CommandRevision);
            return true;
        }

        private void Tick(double deltaTime)
        {
            if (_renderer == null || frames == null || frames.Count == 0 || !_state.IsPlaying)
            {
                return;
            }

            _state.SpeedMultiplier = ResolveEffectiveSpeedMultiplier(speedMultiplier);
            int previousFrame = _state.CurrentFrameIndex;
            SpriteSequenceAdvanceResult result = _state.Advance(deltaTime, ResolveMaxFrameAdvances(maxFrameAdvancesPerUpdate));
            CommitVisualAndPublish(previousFrame, result, false, false, _commandRevision);
        }

        private void CommitVisualAndPublish(
            int previousFrame,
            in SpriteSequenceAdvanceResult result,
            bool forceVisualRefresh,
            bool publishFrameEvenIfUnchanged,
            ulong expectedRevision)
        {
            ReportAdvanceDiagnostics(result);

            bool visualCommitSucceeded = true;
            if (forceVisualRefresh || result.VisualCommitRequired)
            {
                visualCommitSucceeded = TryApplyCurrentVisual(forceVisualRefresh);
            }

            bool publishFrame = publishFrameEvenIfUnchanged ||
                                (result.FrameChanged && previousFrame != _state.CurrentFrameIndex);
            if (publishFrame && visualCommitSucceeded)
            {
                InvokeFrameChangedSafely(_state.CurrentFrameIndex);
                if (_commandRevision != expectedRevision)
                {
                    return;
                }
            }

            for (int i = 0; i < result.CompletedLoopCount; i++)
            {
                InvokeSafely(OnLoopComplete, nameof(OnLoopComplete));
                if (_commandRevision != expectedRevision)
                {
                    return;
                }
            }

            if (result.PlaybackCompleted)
            {
                InvokeSafely(OnPlayComplete, nameof(OnPlayComplete));
            }
        }

        private bool TryApplyCurrentVisual(bool forceRefresh)
        {
            if (_renderer == null || frames == null || frames.Count == 0)
            {
                return false;
            }

            try
            {
                if (_state.IsInInterval && _state.IntervalHoldMode == SpriteSequenceIntervalHoldMode.Blank)
                {
                    _renderer.SetVisible(false);
                    return true;
                }

                int frameIndex = Mathf.Clamp(_state.CurrentFrameIndex, 0, frames.Count - 1);
                _renderer.ApplyFrame(frameIndex, forceRefresh);
                _renderer.SetVisible(true);
                return true;
            }
            catch (Exception exception)
            {
                CLogger.LogError($"Renderer commit failed. object={name}, exception={exception}", LogCategory);
                return false;
            }
        }

        private bool TryInitializeRenderer()
        {
            if (_renderer == null || frames == null || frames.Count == 0)
            {
                return false;
            }

            try
            {
                _renderer.Initialize(frames);
                return true;
            }
            catch (Exception exception)
            {
                CLogger.LogError($"Renderer initialization failed. object={name}, exception={exception}", LogCategory);
                return false;
            }
        }

        private void InitializeRendererAtRest()
        {
            if (!TryInitializeRenderer())
            {
                return;
            }

            _state.TotalFrameCount = frames.Count;
            _state.Stop(0);
            _hasPlaybackState = true;
            TryApplyCurrentVisual(true);
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

            _renderer = GetComponent<ISpriteSequenceRenderer>();
            if (_renderer == null)
            {
                CLogger.LogError($"No ISpriteSequenceRenderer found. Add UGUISequenceRenderer or SpriteRendererSequenceRenderer. object={name}", LogCategory);
            }
        }

        private bool HasLiveBatchOwner()
        {
            return _batchOwner != null;
        }

        private void LogBurstFallbackWarning()
        {
            if (_loggedBurstFallbackWarning)
            {
                return;
            }

            CLogger.LogWarning(
                $"UpdateDriver is BurstManaged but no active batch owner has claimed this controller. " +
                $"FallbackToMonoUpdate={fallbackToMonoUpdateWhenBurstUnavailable}. object={name}",
                LogCategory);
            _loggedBurstFallbackWarning = true;
        }

        private void ReportAdvanceDiagnostics(in SpriteSequenceAdvanceResult result)
        {
            if (result.InvalidInput && !_invalidTimingWarningIssued)
            {
                LogInvalidTimingWarning();
            }

            if (result.CatchUpLimited && !_catchUpWarningIssued)
            {
                CLogger.LogWarning(
                    $"Playback catch-up exceeded maxFrameAdvancesPerUpdate={ResolveMaxFrameAdvances(maxFrameAdvancesPerUpdate)}; " +
                    $"excess elapsed time was dropped. object={name}",
                    LogCategory);
                _catchUpWarningIssued = true;
            }
        }

        private void InvokeFrameChangedSafely(int frameIndex)
        {
            Action<int> callback = OnFrameChanged;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback.Invoke(frameIndex);
            }
            catch (Exception exception)
            {
                CLogger.LogError($"{nameof(OnFrameChanged)} callback failed. object={name}, exception={exception}", LogCategory);
            }
        }

        private void InvokeSafely(Action callback, string callbackName)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback.Invoke();
            }
            catch (Exception exception)
            {
                CLogger.LogError($"{callbackName} callback failed. object={name}, exception={exception}", LogCategory);
            }
        }

        private void InvalidateBatchSnapshots()
        {
            _commandRevision = NextRevision(_commandRevision);
        }

        private static ulong NextRevision(ulong revision)
        {
            return revision == ulong.MaxValue ? 1UL : revision + 1UL;
        }

        private SpriteSequencePlaybackDirection ResolveDirection(PlayDirection direction)
        {
            switch (direction)
            {
                case PlayDirection.Forward:
                    return SpriteSequencePlaybackDirection.Forward;
                case PlayDirection.Reverse:
                    return SpriteSequencePlaybackDirection.Reverse;
                default:
                    LogInvalidTimingWarning();
                    return SpriteSequencePlaybackDirection.Forward;
            }
        }

        private SpriteSequencePlaybackMode ResolvePlaybackMode(PlayMode mode)
        {
            switch (mode)
            {
                case PlayMode.Once:
                    return SpriteSequencePlaybackMode.Once;
                case PlayMode.Loop:
                    return SpriteSequencePlaybackMode.Loop;
                case PlayMode.PingPong:
                    return SpriteSequencePlaybackMode.PingPong;
                default:
                    LogInvalidTimingWarning();
                    return SpriteSequencePlaybackMode.Loop;
            }
        }

        private SpriteSequenceIntervalHoldMode ResolveIntervalHoldMode(IntervalHoldFrame holdFrame)
        {
            switch (holdFrame)
            {
                case IntervalHoldFrame.Last:
                    return SpriteSequenceIntervalHoldMode.Last;
                case IntervalHoldFrame.First:
                    return SpriteSequenceIntervalHoldMode.First;
                case IntervalHoldFrame.Blank:
                    return SpriteSequenceIntervalHoldMode.Blank;
                default:
                    LogInvalidTimingWarning();
                    return SpriteSequenceIntervalHoldMode.Last;
            }
        }

        private UpdateDriver ResolveUpdateDriver(UpdateDriver driver)
        {
            if (driver == UpdateDriver.MonoUpdate || driver == UpdateDriver.BurstManaged)
            {
                return driver;
            }

            LogInvalidTimingWarning();
            return UpdateDriver.MonoUpdate;
        }

        private int ResolveMaxFrameAdvances(int value)
        {
            if (value > 0)
            {
                return value;
            }

            LogInvalidTimingWarning();
            return 1;
        }

        private int ResolvePositiveLoopCount(int value)
        {
            if (value > 0)
            {
                return value;
            }

            LogInvalidTimingWarning();
            return 1;
        }

        private float ResolveEffectiveSpeedMultiplier(float raw)
        {
            float clamped = ResolveFiniteNonNegative(raw);
            if (!useDiscreteSpeedMultiplier)
            {
                _speedRangeWarningIssued = false;
                return clamped;
            }

            float rangeX = ResolveFiniteNonNegative(discreteSpeedMultiplierRange.x);
            float rangeY = ResolveFiniteNonNegative(discreteSpeedMultiplierRange.y);
            float min = Mathf.Min(rangeX, rangeY);
            float max = Mathf.Max(rangeX, rangeY);
            int steps = Math.Max(2, discreteSpeedStepCount);

            bool outOfRange = clamped < min || clamped > max;
            if (warnWhenSpeedOutOfRange && outOfRange)
            {
                if (!_speedRangeWarningIssued)
                {
                    CLogger.LogWarning(
                        $"SpeedMultiplier {clamped:F3} is out of discrete range [{min:F3}, {max:F3}]. " +
                        $"It will be clamped before quantization. object={name}",
                        LogCategory);
                    _speedRangeWarningIssued = true;
                }
            }
            else
            {
                _speedRangeWarningIssued = false;
            }

            float value = Mathf.Clamp(clamped, min, max);
            float span = max - min;
            if (span <= 0.000001f)
            {
                return min;
            }

            float normalized = (value - min) / span;
            int index = Mathf.RoundToInt(normalized * (steps - 1));
            float t = index / (float)(steps - 1);
            return min + t * span;
        }

        private float ResolveFiniteFrameRate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < MinimumFrameRate)
            {
                LogInvalidTimingWarning();
                return MinimumFrameRate;
            }

            return value;
        }

        private float ResolveFiniteNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                LogInvalidTimingWarning();
                return 0f;
            }

            return value;
        }

        private void LogInvalidTimingWarning()
        {
            if (_invalidTimingWarningIssued)
            {
                return;
            }

            CLogger.LogWarning($"Invalid playback timing input was ignored or normalized. object={name}", LogCategory);
            _invalidTimingWarningIssued = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            frames ??= new List<Sprite>();
            frameRate = SanitizeFrameRate(frameRate);
            speedMultiplier = SanitizeNonNegative(speedMultiplier);
            loopInterval = SanitizeNonNegative(loopInterval);
            maxLoopCount = Math.Max(1, maxLoopCount);
            maxFrameAdvancesPerUpdate = Math.Max(1, maxFrameAdvancesPerUpdate);
            discreteSpeedStepCount = Math.Max(2, discreteSpeedStepCount);
            discreteSpeedMultiplierRange.x = SanitizeNonNegative(discreteSpeedMultiplierRange.x);
            discreteSpeedMultiplierRange.y = SanitizeNonNegative(discreteSpeedMultiplierRange.y);

            if ((int)playMode < (int)PlayMode.Once || (int)playMode > (int)PlayMode.PingPong)
            {
                playMode = PlayMode.Loop;
            }

            if ((int)playDirection < (int)PlayDirection.Forward || (int)playDirection > (int)PlayDirection.Reverse)
            {
                playDirection = PlayDirection.Forward;
            }

            if ((int)intervalHoldFrame < (int)IntervalHoldFrame.Last ||
                (int)intervalHoldFrame > (int)IntervalHoldFrame.Blank)
            {
                intervalHoldFrame = IntervalHoldFrame.Last;
            }

            if ((int)updateDriver < (int)UpdateDriver.MonoUpdate ||
                (int)updateDriver > (int)UpdateDriver.BurstManaged)
            {
                updateDriver = UpdateDriver.MonoUpdate;
            }
        }

        private static float SanitizeFrameRate(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? MinimumFrameRate
                : Mathf.Max(MinimumFrameRate, value);
        }

        private static float SanitizeNonNegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }
#endif
    }
}
