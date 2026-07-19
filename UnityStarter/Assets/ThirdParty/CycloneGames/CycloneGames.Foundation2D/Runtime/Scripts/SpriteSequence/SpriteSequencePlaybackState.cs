using System;

namespace CycloneGames.Foundation2D.Runtime
{
    internal enum SpriteSequencePlaybackStatus : byte
    {
        Stopped = 0,
        Playing = 1,
        Paused = 2,
    }

    internal enum SpriteSequencePlaybackMode : byte
    {
        Once = 0,
        Loop = 1,
        PingPong = 2,
    }

    internal enum SpriteSequencePlaybackDirection : sbyte
    {
        Reverse = -1,
        Forward = 1,
    }

    internal enum SpriteSequenceIntervalHoldMode : byte
    {
        Last = 0,
        First = 1,
        Blank = 2,
    }

    [Flags]
    internal enum SpriteSequenceAdvanceFlags : byte
    {
        None = 0,
        FrameChanged = 1 << 0,
        IntervalVisualChanged = 1 << 1,
        PlaybackCompleted = 1 << 2,
        CatchUpLimited = 1 << 3,
        InvalidInput = 1 << 4,
    }

    internal struct SpriteSequenceAdvanceResult
    {
        internal SpriteSequenceAdvanceFlags Flags;
        internal int CompletedLoopCount;

        internal readonly bool FrameChanged => (Flags & SpriteSequenceAdvanceFlags.FrameChanged) != 0;
        internal readonly bool VisualCommitRequired =>
            (Flags & (SpriteSequenceAdvanceFlags.FrameChanged | SpriteSequenceAdvanceFlags.IntervalVisualChanged)) != 0;
        internal readonly bool PlaybackCompleted => (Flags & SpriteSequenceAdvanceFlags.PlaybackCompleted) != 0;
        internal readonly bool CatchUpLimited => (Flags & SpriteSequenceAdvanceFlags.CatchUpLimited) != 0;
        internal readonly bool InvalidInput => (Flags & SpriteSequenceAdvanceFlags.InvalidInput) != 0;

        internal void Add(SpriteSequenceAdvanceFlags flags)
        {
            Flags |= flags;
        }
    }

    /// <summary>
    /// Internal value state used by the main-thread controller and the optional Burst batch integration.
    /// This is not a save, wire, or Unity serialization contract.
    /// </summary>
    internal struct SpriteSequencePlaybackState
    {
        private const double MinimumFrameRate = 0.01d;

        internal double CurrentFrameElapsed;
        internal double FrameDuration;
        internal double SpeedMultiplier;
        internal double LoopIntervalDuration;
        internal double LoopIntervalElapsed;
        internal int CurrentFrameIndex;
        internal int TotalFrameCount;
        internal int CurrentLoopCount;
        internal int MaxLoopCount;
        internal SpriteSequencePlaybackStatus Status;
        internal SpriteSequencePlaybackMode Mode;
        internal SpriteSequencePlaybackDirection InitialDirection;
        internal SpriteSequencePlaybackDirection Direction;
        internal SpriteSequenceIntervalHoldMode IntervalHoldMode;
        internal bool IsInInterval;

        internal readonly bool IsPlaying => Status == SpriteSequencePlaybackStatus.Playing;
        internal readonly bool IsPaused => Status == SpriteSequencePlaybackStatus.Paused;

        internal SpriteSequenceAdvanceResult Initialize(
            SpriteSequencePlaybackDirection direction,
            double frameRate,
            SpriteSequencePlaybackMode mode,
            int frameCount,
            double speedMultiplier,
            int maxLoopCount,
            double loopInterval,
            SpriteSequenceIntervalHoldMode intervalHoldMode)
        {
            SpriteSequenceAdvanceResult result = default;

            InitialDirection = direction;
            Direction = direction;
            Mode = mode;
            IntervalHoldMode = intervalHoldMode;
            TotalFrameCount = frameCount > 0 ? frameCount : 0;
            CurrentFrameIndex = direction == SpriteSequencePlaybackDirection.Forward
                ? 0
                : Math.Max(0, TotalFrameCount - 1);
            CurrentFrameElapsed = 0d;
            CurrentLoopCount = 0;
            MaxLoopCount = maxLoopCount > 0 ? maxLoopCount : 0;
            LoopIntervalElapsed = 0d;
            IsInInterval = false;

            if (!IsFinite(frameRate) || frameRate <= 0d)
            {
                frameRate = MinimumFrameRate;
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput);
            }

            if (!IsFinite(speedMultiplier) || speedMultiplier < 0d)
            {
                speedMultiplier = 0d;
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput);
            }

            if (!IsFinite(loopInterval) || loopInterval < 0d)
            {
                loopInterval = 0d;
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput);
            }

            FrameDuration = 1d / Math.Max(MinimumFrameRate, frameRate);
            SpeedMultiplier = speedMultiplier;
            LoopIntervalDuration = loopInterval;

            if (TotalFrameCount == 0)
            {
                Status = SpriteSequencePlaybackStatus.Stopped;
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput);
                return result;
            }

            Status = SpriteSequencePlaybackStatus.Playing;
            result.Add(SpriteSequenceAdvanceFlags.FrameChanged);
            return result;
        }

        internal SpriteSequenceAdvanceResult Advance(double deltaTime, int maxFrameAdvances)
        {
            SpriteSequenceAdvanceResult result = default;
            if (!IsPlaying)
            {
                return result;
            }

            if (!IsFinite(deltaTime) || deltaTime < 0d ||
                !IsFinite(SpeedMultiplier) || SpeedMultiplier < 0d ||
                !IsFinite(FrameDuration) || FrameDuration <= 0d)
            {
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput);
                return result;
            }

            if (deltaTime == 0d || SpeedMultiplier == 0d)
            {
                return result;
            }

            double remaining = deltaTime * SpeedMultiplier;
            if (!IsFinite(remaining) || remaining < 0d)
            {
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput);
                return result;
            }

            int remainingAdvanceBudget = maxFrameAdvances > 0 ? maxFrameAdvances : 1;
            while (remaining > 0d)
            {
                if (IsInInterval)
                {
                    double intervalRemaining = LoopIntervalDuration - LoopIntervalElapsed;
                    if (intervalRemaining > remaining)
                    {
                        LoopIntervalElapsed += remaining;
                        return result;
                    }

                    remaining -= Math.Max(0d, intervalRemaining);
                    ExitInterval(ref result);
                    if (remaining <= 0d)
                    {
                        return result;
                    }
                }

                if (remainingAdvanceBudget == 0)
                {
                    PreserveFractionalPhaseAndDropBacklog(remaining, ref result);
                    return result;
                }

                double frameRemaining = FrameDuration - CurrentFrameElapsed;
                if (frameRemaining > remaining)
                {
                    CurrentFrameElapsed += remaining;
                    return result;
                }

                remaining -= Math.Max(0d, frameRemaining);
                CurrentFrameElapsed = 0d;
                remainingAdvanceBudget--;
                AdvanceOneFrame(ref result);

                if (!IsPlaying)
                {
                    return result;
                }

                if (remainingAdvanceBudget == 0 && remaining > 0d)
                {
                    PreserveFractionalPhaseAndDropBacklog(remaining, ref result);
                    return result;
                }
            }

            return result;
        }

        internal void Pause()
        {
            if (IsPlaying)
            {
                Status = SpriteSequencePlaybackStatus.Paused;
            }
        }

        internal void Resume()
        {
            if (IsPaused)
            {
                Status = SpriteSequencePlaybackStatus.Playing;
            }
        }

        internal void Stop(int resetFrameIndex)
        {
            Status = SpriteSequencePlaybackStatus.Stopped;
            IsInInterval = false;
            LoopIntervalElapsed = 0d;
            CurrentFrameElapsed = 0d;
            CurrentFrameIndex = TotalFrameCount > 0
                ? Clamp(resetFrameIndex, 0, TotalFrameCount - 1)
                : 0;
        }

        internal void Seek(int frameIndex)
        {
            if (TotalFrameCount <= 0)
            {
                CurrentFrameIndex = 0;
                return;
            }

            CurrentFrameIndex = Clamp(frameIndex, 0, TotalFrameCount - 1);
            CurrentFrameElapsed = 0d;
            LoopIntervalElapsed = 0d;
            IsInInterval = false;
        }

        private void AdvanceOneFrame(ref SpriteSequenceAdvanceResult result)
        {
            if (TotalFrameCount <= 0)
            {
                Status = SpriteSequencePlaybackStatus.Stopped;
                result.Add(SpriteSequenceAdvanceFlags.InvalidInput | SpriteSequenceAdvanceFlags.PlaybackCompleted);
                return;
            }

            if (TotalFrameCount == 1)
            {
                if (Mode == SpriteSequencePlaybackMode.Once)
                {
                    Status = SpriteSequencePlaybackStatus.Stopped;
                    result.Add(SpriteSequenceAdvanceFlags.PlaybackCompleted);
                    return;
                }

                if (!CompleteLoopBoundary(ref result) && LoopIntervalDuration > 0d)
                {
                    EnterInterval(ref result);
                }
                return;
            }

            int directionSign = (int)Direction;
            int nextFrame = CurrentFrameIndex + directionSign;
            switch (Mode)
            {
                case SpriteSequencePlaybackMode.Once:
                    if (nextFrame < 0 || nextFrame >= TotalFrameCount)
                    {
                        Status = SpriteSequencePlaybackStatus.Stopped;
                        result.Add(SpriteSequenceAdvanceFlags.PlaybackCompleted);
                        return;
                    }

                    SetFrame(nextFrame, ref result);
                    return;

                case SpriteSequencePlaybackMode.Loop:
                    if (nextFrame >= 0 && nextFrame < TotalFrameCount)
                    {
                        SetFrame(nextFrame, ref result);
                        return;
                    }

                    if (CompleteLoopBoundary(ref result))
                    {
                        return;
                    }

                    int restartFrame = Direction == SpriteSequencePlaybackDirection.Forward
                        ? 0
                        : TotalFrameCount - 1;
                    if (LoopIntervalDuration > 0d)
                    {
                        if (IntervalHoldMode == SpriteSequenceIntervalHoldMode.First)
                        {
                            SetFrame(restartFrame, ref result);
                        }

                        EnterInterval(ref result);
                        return;
                    }

                    SetFrame(restartFrame, ref result);
                    return;

                case SpriteSequencePlaybackMode.PingPong:
                    if (nextFrame >= TotalFrameCount)
                    {
                        Direction = SpriteSequencePlaybackDirection.Reverse;
                        SetFrame(TotalFrameCount - 2, ref result);
                        CompletePingPongCycleAtStart(ref result);
                        return;
                    }

                    if (nextFrame < 0)
                    {
                        Direction = SpriteSequencePlaybackDirection.Forward;
                        SetFrame(1, ref result);
                        CompletePingPongCycleAtStart(ref result);
                        return;
                    }

                    SetFrame(nextFrame, ref result);
                    CompletePingPongCycleAtStart(ref result);
                    return;

                default:
                    Status = SpriteSequencePlaybackStatus.Stopped;
                    result.Add(SpriteSequenceAdvanceFlags.InvalidInput | SpriteSequenceAdvanceFlags.PlaybackCompleted);
                    return;
            }
        }

        private bool CompleteLoopBoundary(ref SpriteSequenceAdvanceResult result)
        {
            CurrentLoopCount++;
            result.CompletedLoopCount++;
            if (MaxLoopCount > 0 && CurrentLoopCount >= MaxLoopCount)
            {
                Status = SpriteSequencePlaybackStatus.Stopped;
                result.Add(SpriteSequenceAdvanceFlags.PlaybackCompleted);
                return true;
            }

            return false;
        }

        private void CompletePingPongCycleAtStart(ref SpriteSequenceAdvanceResult result)
        {
            int startFrame = InitialDirection == SpriteSequencePlaybackDirection.Forward
                ? 0
                : TotalFrameCount - 1;
            if (CurrentFrameIndex != startFrame || Direction == InitialDirection)
            {
                return;
            }

            Direction = InitialDirection;
            if (!CompleteLoopBoundary(ref result) && LoopIntervalDuration > 0d)
            {
                EnterInterval(ref result);
            }
        }

        private void EnterInterval(ref SpriteSequenceAdvanceResult result)
        {
            IsInInterval = true;
            LoopIntervalElapsed = 0d;
            result.Add(SpriteSequenceAdvanceFlags.IntervalVisualChanged);
        }

        private void ExitInterval(ref SpriteSequenceAdvanceResult result)
        {
            IsInInterval = false;
            LoopIntervalElapsed = 0d;
            result.Add(SpriteSequenceAdvanceFlags.IntervalVisualChanged);

            if (Mode != SpriteSequencePlaybackMode.Loop || TotalFrameCount <= 1)
            {
                return;
            }

            int restartFrame = Direction == SpriteSequencePlaybackDirection.Forward
                ? 0
                : TotalFrameCount - 1;
            SetFrame(restartFrame, ref result);
        }

        private void PreserveFractionalPhaseAndDropBacklog(
            double remaining,
            ref SpriteSequenceAdvanceResult result)
        {
            result.Add(SpriteSequenceAdvanceFlags.CatchUpLimited);
            if (remaining <= 0d)
            {
                return;
            }

            // The visual player intentionally drops whole overdue frames once the configured
            // transition budget is exhausted, while retaining the fractional phase.
            if (IsInInterval)
            {
                double intervalRemaining = Math.Max(0d, LoopIntervalDuration - LoopIntervalElapsed);
                if (remaining < intervalRemaining)
                {
                    LoopIntervalElapsed += remaining;
                    return;
                }

                remaining -= intervalRemaining;
                ExitInterval(ref result);
            }

            CurrentFrameElapsed = FrameDuration > 0d ? remaining % FrameDuration : 0d;
        }

        private void SetFrame(int frameIndex, ref SpriteSequenceAdvanceResult result)
        {
            if (CurrentFrameIndex == frameIndex)
            {
                return;
            }

            CurrentFrameIndex = frameIndex;
            result.Add(SpriteSequenceAdvanceFlags.FrameChanged);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }
    }
}
