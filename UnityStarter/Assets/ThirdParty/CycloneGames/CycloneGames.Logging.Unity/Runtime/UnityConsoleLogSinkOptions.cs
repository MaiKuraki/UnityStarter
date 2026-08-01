using System;
using CycloneGames.Logging;

namespace CycloneGames.Logging.Unity
{
    /// <summary>
    /// Capacity and backpressure policy for the Unity main-thread console handoff.
    /// This queue is independent from the core <see cref="LogPipeline"/> queue.
    /// </summary>
    public sealed class UnityConsoleLogSinkOptions
    {
        public const int DefaultMaxQueuedMessages = 4096;
        public const int DefaultMaxQueuedCharacters = 2 * 1024 * 1024;
        public const int DefaultReservedCriticalMessages = 64;
        public const int DefaultReservedCriticalCharacters = 64 * 1024;
        public const int MaxSupportedQueuedMessages = 256 * 1024;
        public const int MaxSupportedQueuedCharacters = 256 * 1024 * 1024;
        public const int MaxSupportedRetainedEntryCharacters = 16 * 1024 * 1024;
        internal const int FormattingOverheadCharacters = 256;

#if UNITY_EDITOR
        private const int RetainedSourcePathCopies = 3;
#else
        private const int RetainedSourcePathCopies = 2;
#endif

        public int MaxQueuedMessages = DefaultMaxQueuedMessages;
        public int MaxQueuedCharacters = DefaultMaxQueuedCharacters;
        public int MaximumRetainedEntryCharacters = EstimateRetainedCharacters(
            LogPipelineOptions.DefaultMaxMessageCharacters,
            LogPipelineOptions.DefaultMaxCategoryCharacters,
            LogPipelineOptions.DefaultMaxSourcePathCharacters);
        public int ReservedCriticalMessages = DefaultReservedCriticalMessages;
        public int ReservedCriticalCharacters = DefaultReservedCriticalCharacters;
        public LogQueueOverflowPolicy OverflowPolicy = LogQueueOverflowPolicy.DropNewest;
        public LogSeverity CriticalSeverity = LogSeverity.Error;

        public static UnityConsoleLogSinkOptions Default => new UnityConsoleLogSinkOptions();

        public UnityConsoleLogSinkOptions()
        {
        }

        public UnityConsoleLogSinkOptions(UnityConsoleLogSinkOptions source)
        {
            if (source == null)
            {
                source = Default;
            }

            MaxQueuedMessages = source.MaxQueuedMessages;
            MaxQueuedCharacters = source.MaxQueuedCharacters;
            MaximumRetainedEntryCharacters = source.MaximumRetainedEntryCharacters;
            ReservedCriticalMessages = source.ReservedCriticalMessages;
            ReservedCriticalCharacters = source.ReservedCriticalCharacters;
            OverflowPolicy = source.OverflowPolicy;
            CriticalSeverity = source.CriticalSeverity;
        }

        public UnityConsoleLogSinkOptions Clone()
        {
            return new UnityConsoleLogSinkOptions(this);
        }

        internal static UnityConsoleLogSinkOptions CreateValidated(UnityConsoleLogSinkOptions source)
        {
            var options = new UnityConsoleLogSinkOptions(source);
            options.NormalizeReservedCapacity();
            options.Validate();
            return options;
        }

        internal static int EstimateRetainedCharacters(
            int messageCharacters,
            int categoryCharacters,
            int sourcePathCharacters)
        {
            long estimate = Math.Max(messageCharacters, 0);
            estimate += Math.Max(categoryCharacters, 0);
            estimate += (long)Math.Max(sourcePathCharacters, 0) * RetainedSourcePathCopies;
            estimate += FormattingOverheadCharacters;
            return estimate >= int.MaxValue ? int.MaxValue : (int)estimate;
        }

        private void NormalizeReservedCapacity()
        {
            if (MaxQueuedMessages > 0)
            {
                ReservedCriticalMessages = Math.Min(ReservedCriticalMessages, MaxQueuedMessages - 1);
            }

            if (MaxQueuedCharacters > 0 && MaximumRetainedEntryCharacters > 0)
            {
                int maximumReserve = Math.Max(0, MaxQueuedCharacters - MaximumRetainedEntryCharacters);
                ReservedCriticalCharacters = Math.Min(ReservedCriticalCharacters, maximumReserve);
            }
        }

        private void Validate()
        {
            if (MaxQueuedMessages < 1 || MaxQueuedMessages > MaxSupportedQueuedMessages)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedMessages), "MaxQueuedMessages is outside the supported bounded range.");
            }

            if (MaxQueuedCharacters < 1 || MaxQueuedCharacters > MaxSupportedQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedCharacters), "MaxQueuedCharacters is outside the supported bounded range.");
            }

            if (MaximumRetainedEntryCharacters < 1
                || MaximumRetainedEntryCharacters > MaxSupportedRetainedEntryCharacters
                || MaximumRetainedEntryCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumRetainedEntryCharacters),
                    "MaximumRetainedEntryCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (ReservedCriticalMessages < 0 || ReservedCriticalMessages >= MaxQueuedMessages)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ReservedCriticalMessages),
                    "ReservedCriticalMessages must be non-negative and smaller than MaxQueuedMessages.");
            }

            if (ReservedCriticalCharacters < 0 || ReservedCriticalCharacters >= MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ReservedCriticalCharacters),
                    "ReservedCriticalCharacters must be non-negative and smaller than MaxQueuedCharacters.");
            }

            if (OverflowPolicy != LogQueueOverflowPolicy.DropNewest
                && OverflowPolicy != LogQueueOverflowPolicy.DropOldest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(OverflowPolicy),
                    "The Unity main-thread handoff supports only DropNewest or DropOldest.");
            }

            if (!Enum.IsDefined(typeof(LogSeverity), CriticalSeverity)
                || CriticalSeverity == LogSeverity.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(CriticalSeverity),
                    "CriticalSeverity must be a logging severity.");
            }
        }
    }
}
