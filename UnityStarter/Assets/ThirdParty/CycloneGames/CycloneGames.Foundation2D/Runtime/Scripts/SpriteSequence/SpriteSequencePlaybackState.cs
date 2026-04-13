using System;
using UnityEngine;

namespace CycloneGames.Foundation2D.Runtime
{
    /// <summary>
    /// Zero-allocation struct-based playback state for sprite sequence animation.
    /// This struct contains all runtime state and can be directly serialized or used in Burst jobs.
    /// </summary>
    public struct SpriteSequencePlaybackState
    {
        // ──────────────────────────── Time State ────────────────────────────
        /// <summary>Total accumulated animation time (seconds)</summary>
        public double CurrentTime;

        /// <summary>Last frame's clock time for delta calculation</summary>
        public double LastUpdateTime;

        /// <summary>Current frame index in sequence</summary>
        public int CurrentFrameIndex;

        /// <summary>Total frame count</summary>
        public int TotalFrameCount;

        // ──────────────────────────── Playback State ────────────────────────────
        /// <summary>Playback state: 0=Stopped, 1=Playing, 2=Paused</summary>
        public int State;

        /// <summary>Playback direction: 0=Forward, 1=Reverse</summary>
        public int Direction;

        /// <summary>Play mode: 0=Once, 1=Loop, 2=PingPong</summary>
        public int PlayMode;

        /// <summary>Frame to hold during interval: 0=Last, 1=First, 2=Blank</summary>
        public int IntervalHoldFrame;

        // ──────────────────────────── Loop State ────────────────────────────
        /// <summary>Current loop/cycle count</summary>
        public int CurrentLoopCount;

        /// <summary>Maximum loops (0 = unlimited)</summary>
        public int MaxLoopCount;

        /// <summary>Duration to wait between loops (seconds)</summary>
        public double LoopIntervalDuration;

        /// <summary>Time accumulated in current interval</summary>
        public double LoopIntervalElapsed;

        /// <summary>Is currently in loop interval pause</summary>
        public bool IsInInterval;

        // ──────────────────────────── Animation Parameters ────────────────────────────
        /// <summary>Time per frame: 1.0 / frameRate</summary>
        public double FrameDuration;

        /// <summary>Playback speed multiplier</summary>
        public double TimescaleMultiplier;

        /// <summary>Internal direction multiplier: +1 or -1</summary>
        public int DirectionSign;

        /// <summary>Whether time has been properly initialized</summary>
        public bool HasInitializedTime;

        // ──────────────────────────── Core Update Logic ────────────────────────────

        /// <summary>
        /// Update playback state with new delta time.
        /// Returns true if frame was advanced, false otherwise.
        /// Completely 0-allocation, can be Burst compiled.
        /// </summary>
        public bool Update(double deltaTime, double nowTime)
        {
            // Not playing (1 = Playing)
            if (State != 1)
                return false;

            // Initialize time on first update
            if (!HasInitializedTime)
            {
                LastUpdateTime = nowTime;
                HasInitializedTime = true;
                return false;
            }

            // Apply time scale
            double scaledDelta = deltaTime * TimescaleMultiplier;

            // Handle loop interval
            if (IsInInterval)
            {
                LoopIntervalElapsed += scaledDelta;
                if (LoopIntervalElapsed < LoopIntervalDuration)
                {
                    return false;
                }

                // Interval complete
                IsInInterval = false;
                LoopIntervalElapsed = 0;
                CurrentTime = 0;

                // Reset to start frame and begin new loop
                int startFrame = DirectionSign == 1 ? 0 : TotalFrameCount - 1;
                CurrentFrameIndex = startFrame;
                return true; // Frame changed
            }

            // Normal playback: accumulate time
            CurrentTime += scaledDelta;

            bool frameChanged = false;

            // Frame advancement: support low-fps catch-up
            while (CurrentTime >= FrameDuration && TotalFrameCount > 1)
            {
                CurrentTime -= FrameDuration;

                if (AdvanceFrameInternal())
                {
                    frameChanged = true;

                    // Check if we need to enter interval after frame advance
                    if (IsInInterval)
                    {
                        return frameChanged;
                    }
                }
                else
                {
                    // Playback stopped
                    return frameChanged;
                }
            }

            return frameChanged;
        }

        /// <summary>
        /// Advance to next frame, handling mode-specific logic.
        /// Returns false if playback should stop.
        /// </summary>
        private bool AdvanceFrameInternal()
        {
            int nextFrame = CurrentFrameIndex + DirectionSign;

            switch (PlayMode)
            {
                case 0: // Once
                    if (nextFrame < 0 || nextFrame >= TotalFrameCount)
                    {
                        // Playback ends
                        State = 0; // Stopped
                        return false;
                    }
                    break;

                case 1: // Loop
                    if (nextFrame >= TotalFrameCount || nextFrame < 0)
                    {
                        CurrentLoopCount++;

                        // Check finite loop limit
                        if (MaxLoopCount > 0 && CurrentLoopCount >= MaxLoopCount)
                        {
                            State = 0; // Stopped
                            return false;
                        }

                        // Enter interval if needed
                        if (LoopIntervalDuration > 0)
                        {
                            IsInInterval = true;
                            LoopIntervalElapsed = 0;

                            // Handle interval hold frame
                            switch (IntervalHoldFrame)
                            {
                                case 1: // First
                                    CurrentFrameIndex = DirectionSign == 1 ? 0 : TotalFrameCount - 1;
                                    break;
                                    // Blank (2) and Last (0) don't change frame
                            }
                            return true;
                        }

                        // Wrap around
                        nextFrame = nextFrame >= TotalFrameCount ? 0 : TotalFrameCount - 1;
                    }
                    break;

                case 2: // PingPong
                    if (nextFrame >= TotalFrameCount)
                    {
                        DirectionSign = -1;
                        nextFrame = TotalFrameCount - 2;
                        if (nextFrame < 0) nextFrame = 0;

                        CurrentLoopCount++;

                        if (MaxLoopCount > 0 && CurrentLoopCount >= MaxLoopCount)
                        {
                            State = 0; // Stopped
                            return false;
                        }

                        if (LoopIntervalDuration > 0)
                        {
                            IsInInterval = true;
                            LoopIntervalElapsed = 0;
                            CurrentFrameIndex = nextFrame;
                            return true;
                        }
                    }
                    else if (nextFrame < 0)
                    {
                        DirectionSign = 1;
                        nextFrame = 1;
                        if (nextFrame >= TotalFrameCount) nextFrame = 0;

                        CurrentLoopCount++;

                        if (MaxLoopCount > 0 && CurrentLoopCount >= MaxLoopCount)
                        {
                            State = 0; // Stopped
                            return false;
                        }

                        if (LoopIntervalDuration > 0)
                        {
                            IsInInterval = true;
                            LoopIntervalElapsed = 0;
                            CurrentFrameIndex = nextFrame;
                            return true;
                        }
                    }
                    break;
            }

            CurrentFrameIndex = nextFrame;
            return true;
        }

        /// <summary>Initialize playback state (call when Play() is invoked)</summary>
        public void InitializePlayback(int direction, float frameRate, int playMode,
                int frameCount, bool ignoreTimeScale, float timescale, int maxLoops,
                double loopInterval, int holdFrame)
        {
            DirectionSign = direction == 0 ? 1 : -1; // 0 = Forward
            CurrentFrameIndex = DirectionSign == 1 ? 0 : frameCount - 1;
            TotalFrameCount = frameCount;
            CurrentTime = 0;
            FrameDuration = 1.0 / Mathf.Max(0.01f, frameRate);
            TimescaleMultiplier = Mathf.Max(0f, timescale);
            PlayMode = playMode;
            Direction = direction;
            IntervalHoldFrame = holdFrame;
            State = 1; // Playing
            CurrentLoopCount = 0;
            MaxLoopCount = maxLoops;
            LoopIntervalDuration = loopInterval;
            LoopIntervalElapsed = 0;
            IsInInterval = false;
            HasInitializedTime = false;
        }

        /// <summary>Synchronize clock after state change (prevents time jump)</summary>
        public void SyncClock(double nowTime)
        {
            LastUpdateTime = nowTime;
            HasInitializedTime = true;
        }

        /// <summary>Check if playback is active (not stopped), 0=Stopped, 1=Playing, 2=Paused</summary>
        public bool IsActive => State != 0;
    }
}