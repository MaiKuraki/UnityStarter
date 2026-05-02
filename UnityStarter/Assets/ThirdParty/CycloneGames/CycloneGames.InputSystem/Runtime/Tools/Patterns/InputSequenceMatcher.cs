using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    #region Sequence Matching

    public struct SequenceStep
    {
        public string ActionMapName;
        public string ActionName;
        public ActionValueType ExpectedType;
        public float MaxDelayMs;
        public float MinDelayMs;
        public bool IsOptional;
        public Vector2? ExpectedDirection;
        public float DirectionTolerance;
    }

    public sealed class SequenceOccurrence
    {
        public float StartTime { get; }
        public float EndTime { get; }
        public float[] StepTimings { get; }
        public int[] FrameIndices { get; }

        internal SequenceOccurrence(float startTime, float endTime, float[] stepTimings, int[] frameIndices)
        {
            StartTime = startTime;
            EndTime = endTime;
            StepTimings = stepTimings;
            FrameIndices = frameIndices;
        }
    }

    public sealed class SequenceMatchResult
    {
        public bool Matched => Occurrences.Count > 0;
        public int OccurrenceCount => Occurrences.Count;
        public List<SequenceOccurrence> Occurrences { get; }
        public float BestTotalDuration { get; }
        public float AverageDuration { get; }

        internal SequenceMatchResult(List<SequenceOccurrence> occurrences)
        {
            Occurrences = occurrences ?? new List<SequenceOccurrence>();
            if (occurrences.Count > 0)
            {
                float best = float.MaxValue;
                float sum = 0f;
                for (int i = 0; i < occurrences.Count; i++)
                {
                    float dur = occurrences[i].EndTime - occurrences[i].StartTime;
                    if (dur < best) best = dur;
                    sum += dur;
                }
                BestTotalDuration = best;
                AverageDuration = sum / occurrences.Count;
            }
        }
    }

    public static class InputSequenceMatcher
    {
        public static SequenceMatchResult DetectSequence(InputRecording recording, SequenceStep[] sequence)
        {
            var occurrences = new List<SequenceOccurrence>();
            if (recording == null || sequence == null || sequence.Length == 0 || recording.FrameCount == 0)
                return new SequenceMatchResult(occurrences);

            var frames = recording.Frames;
            int frameCount = frames.Count;

            int startScanIndex = 0;
            while (startScanIndex < frameCount)
            {
                int matchStart = FindStepMatch(frames, startScanIndex, sequence[0]);
                if (matchStart < 0) break;

                var stepFrames = new int[sequence.Length];
                var stepTimings = new float[sequence.Length];
                stepFrames[0] = matchStart;
                stepTimings[0] = frames[matchStart].TimeSinceStart;

                int currentFrame = matchStart + 1;
                int stepsMatched = 1;
                bool sequenceFailed = false;

                for (int s = 1; s < sequence.Length; s++)
                {
                    var step = sequence[s];
                    float prevTime = stepTimings[s - 1];
                    int foundFrame = -1;

                    for (int f = currentFrame; f < frameCount; f++)
                    {
                        var frame = frames[f];
                        float elapsed = (frame.TimeSinceStart - prevTime) * 1000f;

                        if (step.MaxDelayMs > 0f && elapsed > step.MaxDelayMs)
                            break;

                        if (elapsed < step.MinDelayMs)
                            continue;

                        if (FrameMatchesStep(frame, step))
                        {
                            foundFrame = f;
                            break;
                        }
                    }

                    if (foundFrame >= 0)
                    {
                        stepFrames[s] = foundFrame;
                        stepTimings[s] = frames[foundFrame].TimeSinceStart;
                        currentFrame = foundFrame + 1;
                        stepsMatched++;
                    }
                    else if (!step.IsOptional)
                    {
                        sequenceFailed = true;
                        break;
                    }
                }

                if (!sequenceFailed && stepsMatched == sequence.Length)
                {
                    occurrences.Add(new SequenceOccurrence(
                        stepTimings[0],
                        stepTimings[stepsMatched - 1],
                        stepTimings,
                        stepFrames
                    ));
                }

                startScanIndex = matchStart + 1;
            }

            return new SequenceMatchResult(occurrences);
        }

        private static int FindStepMatch(List<InputFrame> frames, int startIndex, SequenceStep step)
        {
            for (int i = startIndex; i < frames.Count; i++)
            {
                if (FrameMatchesStep(frames[i], step))
                    return i;
            }
            return -1;
        }

        private static bool FrameMatchesStep(InputFrame frame, SequenceStep step)
        {
            switch (step.ExpectedType)
            {
                case ActionValueType.Button:
                    return frame.HasUnitEvent;

                case ActionValueType.Vector2:
                    if (!frame.Vector2Value.HasValue) return false;
                    if (!step.ExpectedDirection.HasValue) return true;
                    return IsDirectionMatch(frame.Vector2Value.Value, step.ExpectedDirection.Value, step.DirectionTolerance);

                case ActionValueType.Float:
                    return frame.FloatValue.HasValue;
            }
            return false;
        }

        private static bool IsDirectionMatch(Vector2 input, Vector2 expected, float tolerance)
        {
            if (input.sqrMagnitude < 0.01f) return false;
            float dot = Vector2.Dot(input.normalized, expected.normalized);
            return dot >= (1f - tolerance);
        }
    }

    #endregion

    #region Gesture Recognition

    public enum Direction8Way
    {
        Neutral = 5,
        Right = 6, DownRight = 3, Down = 2, DownLeft = 1,
        Left = 4, UpLeft = 7, Up = 8, UpRight = 9
    }

    public sealed class GestureDefinition
    {
        public string Name { get; }
        internal Direction8Way[] Motions { get; }
        public float TimeWindowSec { get; }
        public float InputDeadZone { get; }

        public GestureDefinition(string name, Direction8Way[] motions, float timeWindowSec = 0.4f, float inputDeadZone = 0.3f)
        {
            Name = name;
            Motions = motions ?? Array.Empty<Direction8Way>();
            TimeWindowSec = timeWindowSec;
            InputDeadZone = inputDeadZone;
        }

        public static readonly GestureDefinition QuarterCircleForward = new(
            "Quarter Circle Forward (↓↘→)",
            new[] { Direction8Way.Down, Direction8Way.DownRight, Direction8Way.Right }, 0.4f);

        public static readonly GestureDefinition QuarterCircleBack = new(
            "Quarter Circle Back (↓↙←)",
            new[] { Direction8Way.Down, Direction8Way.DownLeft, Direction8Way.Left }, 0.4f);

        public static readonly GestureDefinition DragonPunch = new(
            "Dragon Punch (→↓↘)",
            new[] { Direction8Way.Right, Direction8Way.Down, Direction8Way.DownRight }, 0.35f);

        public static readonly GestureDefinition HalfCircleForward = new(
            "Half Circle Forward (←↙↓↘→)",
            new[] { Direction8Way.Left, Direction8Way.DownLeft, Direction8Way.Down, Direction8Way.DownRight, Direction8Way.Right }, 0.6f);

        public static readonly GestureDefinition HalfCircleBack = new(
            "Half Circle Back (→↘↓↙←)",
            new[] { Direction8Way.Right, Direction8Way.DownRight, Direction8Way.Down, Direction8Way.DownLeft, Direction8Way.Left }, 0.6f);

        public static readonly GestureDefinition FullCircle = new(
            "Full Circle (360)",
            new[] { Direction8Way.Right, Direction8Way.DownRight, Direction8Way.Down, Direction8Way.DownLeft,
                    Direction8Way.Left, Direction8Way.UpLeft, Direction8Way.Up, Direction8Way.UpRight }, 0.8f);

        // Double-tap dash: → → within 200ms
        public static readonly GestureDefinition DashForward = new(
            "Dash Forward",
            new[] { Direction8Way.Right, Direction8Way.Neutral, Direction8Way.Right }, 0.2f);

        public static readonly GestureDefinition DashBack = new(
            "Dash Back",
            new[] { Direction8Way.Left, Direction8Way.Neutral, Direction8Way.Left }, 0.2f);
    }

    public sealed class GestureMatchResult
    {
        public GestureDefinition Gesture { get; }
        public bool Matched { get; }
        public float StartTime { get; }
        public float EndTime { get; }
        public float Duration { get; }
        public int[] DirectionTransitions { get; }
        public float TransitionSpeed { get; }

        internal GestureMatchResult(GestureDefinition gesture, bool matched, float startTime = 0f,
            float endTime = 0f, int[] transitions = null)
        {
            Gesture = gesture;
            Matched = matched;
            StartTime = startTime;
            EndTime = endTime;
            Duration = endTime - startTime;
            DirectionTransitions = transitions ?? Array.Empty<int>();
            TransitionSpeed = DirectionTransitions.Length > 0 ? Duration / DirectionTransitions.Length : 0f;
        }
    }

    public static class InputGestureRecognizer
    {
        public static GestureMatchResult DetectGesture(InputRecording recording, GestureDefinition gesture)
        {
            if (recording == null || gesture == null || recording.FrameCount == 0)
                return new GestureMatchResult(gesture, false);

            var frames = recording.Frames;
            var motions = gesture.Motions;
            int motionIdx = 0;
            float gestureStartTime = -1f;
            float prevDirectionTime = -1f;
            var transitions = new List<int>();
            Direction8Way lastDirection = Direction8Way.Neutral;

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (!frame.Vector2Value.HasValue) continue;

                var dir = QuantizeDirection(frame.Vector2Value.Value, gesture.InputDeadZone);
                if (dir == Direction8Way.Neutral) continue;

                if (motionIdx == 0)
                {
                    if (dir == motions[0])
                    {
                        gestureStartTime = frame.TimeSinceStart;
                        prevDirectionTime = frame.TimeSinceStart;
                        lastDirection = dir;
                        motionIdx = 1;
                        transitions.Add(i);
                    }
                }
                else
                {
                    float elapsed = frame.TimeSinceStart - gestureStartTime;
                    if (elapsed > gesture.TimeWindowSec)
                        return new GestureMatchResult(gesture, false);

                    if (dir == motions[motionIdx] && dir != lastDirection)
                    {
                        prevDirectionTime = frame.TimeSinceStart;
                        lastDirection = dir;
                        motionIdx++;
                        transitions.Add(i);

                        if (motionIdx == motions.Length)
                        {
                            return new GestureMatchResult(gesture, true,
                                gestureStartTime, frame.TimeSinceStart,
                                transitions.ToArray());
                        }
                    }
                }
            }

            return new GestureMatchResult(gesture, false);
        }

        public static Direction8Way QuantizeDirection(Vector2 input, float deadZone = 0.3f)
        {
            float mag = input.magnitude;
            if (mag < deadZone) return Direction8Way.Neutral;

            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            if (angle >= 337.5f || angle < 22.5f) return Direction8Way.Right;
            if (angle >= 22.5f && angle < 67.5f) return Direction8Way.UpRight;
            if (angle >= 67.5f && angle < 112.5f) return Direction8Way.Up;
            if (angle >= 112.5f && angle < 157.5f) return Direction8Way.UpLeft;
            if (angle >= 157.5f && angle < 202.5f) return Direction8Way.Left;
            if (angle >= 202.5f && angle < 247.5f) return Direction8Way.DownLeft;
            if (angle >= 247.5f && angle < 292.5f) return Direction8Way.Down;
            return Direction8Way.DownRight;
        }
    }

    #endregion
}
