using System;

namespace CycloneGames.Logging
{
    public enum LogQueueOverflowPolicy : byte
    {
        DropNewest = 0,
        DropOldest = 1,
        Block = 2
    }

    public sealed class LogPipelineOptions
    {
        public const int DefaultMaxQueuedMessages = 8192;
        public const int DefaultMaxQueuedCharacters = 4 * 1024 * 1024;
        public const int DefaultMaxMessageCharacters = 16 * 1024;
        public const int DefaultMaxCategoryCharacters = 256;
        public const int DefaultMaxSourcePathCharacters = 2048;
        public const int DefaultMaxMemberNameCharacters = 256;
        public const int DefaultMaxFilterCategories = 1024;
        public const int DefaultMaxFilterCharacters = 64 * 1024;
        public const int DefaultReservedCriticalMessages = 64;
        public const int DefaultReservedCriticalCharacters = 64 * 1024;
        public const int DefaultShutdownDrainTimeoutMs = 2000;
        public const int DefaultMaintenanceIntervalMs = 250;
        public const int DefaultSinkFailureThreshold = 3;
        public const int MaxSupportedQueuedMessages = 256 * 1024;
        public const int MaxSupportedQueuedCharacters = 256 * 1024 * 1024;
        public const int MaxSupportedMessageCharacters = 1024 * 1024;
        public const int MaxSupportedCategoryCharacters = 4 * 1024;
        public const int MaxSupportedSourcePathCharacters = 32 * 1024;
        public const int MaxSupportedMemberNameCharacters = 4 * 1024;
        public const int MaxSupportedFilterCategories = 64 * 1024;
        public const int MaxSupportedFilterCharacters = 16 * 1024 * 1024;
        public const int MaxSupportedShutdownDrainTimeoutMs = 10 * 60 * 1000;
        public const int MaxSupportedEnqueueBlockTimeoutMs = 60 * 1000;
        public const int MaxSupportedMaintenanceIntervalMs = 10 * 60 * 1000;

        public int MaxQueuedMessages = DefaultMaxQueuedMessages;
        public int MaxQueuedCharacters = DefaultMaxQueuedCharacters;
        public int MaxMessageCharacters = DefaultMaxMessageCharacters;
        public int MaxCategoryCharacters = DefaultMaxCategoryCharacters;
        public int MaxSourcePathCharacters = DefaultMaxSourcePathCharacters;
        public int MaxMemberNameCharacters = DefaultMaxMemberNameCharacters;
        public int MaxFilterCategories = DefaultMaxFilterCategories;
        public int MaxFilterCharacters = DefaultMaxFilterCharacters;
        public int ReservedCriticalMessages = DefaultReservedCriticalMessages;
        public int ReservedCriticalCharacters = DefaultReservedCriticalCharacters;
        public int ShutdownDrainTimeoutMs = DefaultShutdownDrainTimeoutMs;
        public int EnqueueBlockTimeoutMs = 1;
        public int MaintenanceIntervalMs = DefaultMaintenanceIntervalMs;
        public int SinkFailureThreshold = DefaultSinkFailureThreshold;
        public LogQueueOverflowPolicy OverflowPolicy = LogQueueOverflowPolicy.DropNewest;

        /// <summary>
        /// Gets or sets the severity threshold that may use the reserved queue capacity.
        /// Reserved capacity reduces loss under ordinary overload; no finite queue can guarantee delivery.
        /// </summary>
        public LogSeverity CriticalSeverity = LogSeverity.Error;

        public static LogPipelineOptions Default => new LogPipelineOptions();

        public LogPipelineOptions()
        {
        }

        public LogPipelineOptions(LogPipelineOptions source)
        {
            if (source == null)
            {
                source = Default;
            }

            MaxQueuedMessages = source.MaxQueuedMessages;
            MaxQueuedCharacters = source.MaxQueuedCharacters;
            MaxMessageCharacters = source.MaxMessageCharacters;
            MaxCategoryCharacters = source.MaxCategoryCharacters;
            MaxSourcePathCharacters = source.MaxSourcePathCharacters;
            MaxMemberNameCharacters = source.MaxMemberNameCharacters;
            MaxFilterCategories = source.MaxFilterCategories;
            MaxFilterCharacters = source.MaxFilterCharacters;
            ReservedCriticalMessages = source.ReservedCriticalMessages;
            ReservedCriticalCharacters = source.ReservedCriticalCharacters;
            ShutdownDrainTimeoutMs = source.ShutdownDrainTimeoutMs;
            EnqueueBlockTimeoutMs = source.EnqueueBlockTimeoutMs;
            MaintenanceIntervalMs = source.MaintenanceIntervalMs;
            SinkFailureThreshold = source.SinkFailureThreshold;
            OverflowPolicy = source.OverflowPolicy;
            CriticalSeverity = source.CriticalSeverity;
        }

        public LogPipelineOptions Clone()
        {
            return new LogPipelineOptions(this);
        }

        internal static LogPipelineOptions CreateValidated(LogPipelineOptions source)
        {
            var options = new LogPipelineOptions(source);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (options.OverflowPolicy == LogQueueOverflowPolicy.Block)
            {
                throw new PlatformNotSupportedException("Block overflow policy is unavailable in WebGL players. Use DropNewest or DropOldest.");
            }
#endif
            options.NormalizeReservedCapacity();
            options.Validate();
            return options;
        }

        private void NormalizeReservedCapacity()
        {
            if (MaxQueuedMessages > 0)
            {
                ReservedCriticalMessages = Math.Min(ReservedCriticalMessages, MaxQueuedMessages - 1);
            }

            if (MaxQueuedCharacters > 0)
            {
                MaxCategoryCharacters = Math.Min(MaxCategoryCharacters, MaxQueuedCharacters);
                MaxSourcePathCharacters = Math.Min(MaxSourcePathCharacters, MaxQueuedCharacters);
                MaxMemberNameCharacters = Math.Min(MaxMemberNameCharacters, MaxQueuedCharacters);
            }

            if (MaxQueuedCharacters > 0)
            {
                long maxCoreEntryCharacters = (long)MaxMessageCharacters
                    + MaxCategoryCharacters
                    + MaxSourcePathCharacters
                    + MaxMemberNameCharacters;
                long maxCoreReserve = Math.Max(0L, MaxQueuedCharacters - maxCoreEntryCharacters);
                ReservedCriticalCharacters = (int)Math.Min(ReservedCriticalCharacters, maxCoreReserve);
            }
        }

        internal void Validate()
        {
            if (MaxQueuedMessages < 1 || MaxQueuedMessages > MaxSupportedQueuedMessages)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedMessages), "MaxQueuedMessages is outside the supported bounded range.");
            }

            if (MaxQueuedCharacters < 1 || MaxQueuedCharacters > MaxSupportedQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedCharacters), "MaxQueuedCharacters is outside the supported bounded range.");
            }

            if (MaxMessageCharacters < 1
                || MaxMessageCharacters > MaxSupportedMessageCharacters
                || MaxMessageCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMessageCharacters), "MaxMessageCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxCategoryCharacters < 1
                || MaxCategoryCharacters > MaxSupportedCategoryCharacters
                || MaxCategoryCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCategoryCharacters), "MaxCategoryCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxSourcePathCharacters < 1
                || MaxSourcePathCharacters > MaxSupportedSourcePathCharacters
                || MaxSourcePathCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxSourcePathCharacters), "MaxSourcePathCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxMemberNameCharacters < 1
                || MaxMemberNameCharacters > MaxSupportedMemberNameCharacters
                || MaxMemberNameCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMemberNameCharacters), "MaxMemberNameCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxFilterCategories < 1 || MaxFilterCategories > MaxSupportedFilterCategories)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFilterCategories), "MaxFilterCategories must be greater than zero.");
            }

            if (MaxFilterCharacters < MaxCategoryCharacters
                || MaxFilterCharacters > MaxSupportedFilterCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFilterCharacters), "MaxFilterCharacters must contain at least one category at MaxCategoryCharacters.");
            }

            long maxRetainedEntryCharacters = (long)MaxMessageCharacters
                + MaxCategoryCharacters
                + MaxSourcePathCharacters
                + MaxMemberNameCharacters;
            if (maxRetainedEntryCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxQueuedCharacters),
                    "MaxQueuedCharacters must contain one entry at all configured message and metadata limits.");
            }

            if (ReservedCriticalMessages < 0 || ReservedCriticalMessages >= MaxQueuedMessages)
            {
                throw new ArgumentOutOfRangeException(nameof(ReservedCriticalMessages), "ReservedCriticalMessages must be non-negative and smaller than MaxQueuedMessages.");
            }

            if (ReservedCriticalCharacters < 0 || ReservedCriticalCharacters >= MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(ReservedCriticalCharacters), "ReservedCriticalCharacters must be non-negative and smaller than MaxQueuedCharacters.");
            }

            if (ShutdownDrainTimeoutMs < 0
                || ShutdownDrainTimeoutMs > MaxSupportedShutdownDrainTimeoutMs)
            {
                throw new ArgumentOutOfRangeException(nameof(ShutdownDrainTimeoutMs), "ShutdownDrainTimeoutMs cannot be negative.");
            }

            if (EnqueueBlockTimeoutMs < 0
                || EnqueueBlockTimeoutMs > MaxSupportedEnqueueBlockTimeoutMs)
            {
                throw new ArgumentOutOfRangeException(nameof(EnqueueBlockTimeoutMs), "EnqueueBlockTimeoutMs cannot be negative.");
            }

            if (MaintenanceIntervalMs < 10
                || MaintenanceIntervalMs > MaxSupportedMaintenanceIntervalMs)
            {
                throw new ArgumentOutOfRangeException(nameof(MaintenanceIntervalMs), "MaintenanceIntervalMs must be at least 10 milliseconds.");
            }

            if (SinkFailureThreshold < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(SinkFailureThreshold), "SinkFailureThreshold must be greater than zero.");
            }

            if (!Enum.IsDefined(typeof(LogQueueOverflowPolicy), OverflowPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(OverflowPolicy), "Unknown overflow policy.");
            }

            if (!Enum.IsDefined(typeof(LogSeverity), CriticalSeverity) || CriticalSeverity == LogSeverity.None)
            {
                throw new ArgumentOutOfRangeException(nameof(CriticalSeverity), "CriticalSeverity must be a logging severity.");
            }
        }
    }
}
