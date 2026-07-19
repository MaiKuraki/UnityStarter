using System;

namespace CycloneGames.Logger
{
    public enum LogQueueOverflowPolicy : byte
    {
        DropNewest = 0,
        DropOldest = 1,
        Block = 2
    }

    public sealed class LoggerProcessingOptions
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
        public const int DefaultUnityConsoleMaxQueuedMessages = 4096;
        public const int DefaultUnityConsoleMaxQueuedCharacters = 2 * 1024 * 1024;
        public const int DefaultShutdownDrainTimeoutMs = 2000;
        public const int DefaultMaintenanceIntervalMs = 250;
        public const int DefaultSinkFailureThreshold = 3;
        internal const int UnityFormattingOverheadCharacters = 256;

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
        public int UnityConsoleMaxQueuedMessages = DefaultUnityConsoleMaxQueuedMessages;
        public int UnityConsoleMaxQueuedCharacters = DefaultUnityConsoleMaxQueuedCharacters;
        public LogQueueOverflowPolicy UnityConsoleOverflowPolicy = LogQueueOverflowPolicy.DropNewest;
        public int ShutdownDrainTimeoutMs = DefaultShutdownDrainTimeoutMs;
        public int EnqueueBlockTimeoutMs = 1;
        public int MaintenanceIntervalMs = DefaultMaintenanceIntervalMs;
        public int SinkFailureThreshold = DefaultSinkFailureThreshold;
        public LogQueueOverflowPolicy OverflowPolicy = LogQueueOverflowPolicy.DropNewest;

        /// <summary>
        /// Gets or sets the severity threshold that may use the reserved queue capacity.
        /// Reserved capacity reduces loss under ordinary overload; no finite queue can guarantee delivery.
        /// </summary>
        public LogLevel CriticalLevel = LogLevel.Error;

        /// <summary>
        /// Compatibility alias for <see cref="CriticalLevel"/>.
        /// </summary>
        [Obsolete("GuaranteedLevel never provided an absolute delivery guarantee. Use CriticalLevel.")]
        public LogLevel GuaranteedLevel
        {
            get => CriticalLevel;
            set => CriticalLevel = value;
        }

        public static LoggerProcessingOptions Default => new LoggerProcessingOptions();

        public LoggerProcessingOptions()
        {
        }

        public LoggerProcessingOptions(LoggerProcessingOptions source)
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
            UnityConsoleMaxQueuedMessages = source.UnityConsoleMaxQueuedMessages;
            UnityConsoleMaxQueuedCharacters = source.UnityConsoleMaxQueuedCharacters;
            UnityConsoleOverflowPolicy = source.UnityConsoleOverflowPolicy;
            ShutdownDrainTimeoutMs = source.ShutdownDrainTimeoutMs;
            EnqueueBlockTimeoutMs = source.EnqueueBlockTimeoutMs;
            MaintenanceIntervalMs = source.MaintenanceIntervalMs;
            SinkFailureThreshold = source.SinkFailureThreshold;
            OverflowPolicy = source.OverflowPolicy;
            CriticalLevel = source.CriticalLevel;
        }

        public LoggerProcessingOptions Clone()
        {
            return new LoggerProcessingOptions(this);
        }

        internal static LoggerProcessingOptions CreateValidated(LoggerProcessingOptions source)
        {
            var options = new LoggerProcessingOptions(source);
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
            if (MaxQueuedMessages > 0 && UnityConsoleMaxQueuedMessages > 0)
            {
                int sharedMessageCapacity = Math.Min(MaxQueuedMessages, UnityConsoleMaxQueuedMessages);
                ReservedCriticalMessages = Math.Min(ReservedCriticalMessages, sharedMessageCapacity - 1);
            }

            if (MaxQueuedCharacters > 0)
            {
                MaxCategoryCharacters = Math.Min(MaxCategoryCharacters, MaxQueuedCharacters);
                MaxSourcePathCharacters = Math.Min(MaxSourcePathCharacters, MaxQueuedCharacters);
                MaxMemberNameCharacters = Math.Min(MaxMemberNameCharacters, MaxQueuedCharacters);
            }

            if (MaxQueuedCharacters > 0 && UnityConsoleMaxQueuedCharacters > 0)
            {
                long maxCoreEntryCharacters = (long)MaxMessageCharacters
                    + MaxCategoryCharacters
                    + MaxSourcePathCharacters
                    + MaxMemberNameCharacters;
                long maxUnityEntryCharacters = (long)MaxMessageCharacters
                    + MaxCategoryCharacters
                    + (long)MaxSourcePathCharacters * 2L
                    + UnityFormattingOverheadCharacters;
                long maxCoreReserve = Math.Max(0L, MaxQueuedCharacters - maxCoreEntryCharacters);
                long maxUnityReserve = Math.Max(0L, UnityConsoleMaxQueuedCharacters - maxUnityEntryCharacters);
                long maxSharedReserve = Math.Min(maxCoreReserve, maxUnityReserve);
                ReservedCriticalCharacters = (int)Math.Min(ReservedCriticalCharacters, maxSharedReserve);
            }
        }

        internal void Validate()
        {
            if (MaxQueuedMessages < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedMessages), "MaxQueuedMessages must be greater than zero.");
            }

            if (MaxQueuedCharacters < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedCharacters), "MaxQueuedCharacters must be greater than zero.");
            }

            if (MaxMessageCharacters < 1 || MaxMessageCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMessageCharacters), "MaxMessageCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxCategoryCharacters < 1 || MaxCategoryCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCategoryCharacters), "MaxCategoryCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxSourcePathCharacters < 1 || MaxSourcePathCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxSourcePathCharacters), "MaxSourcePathCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxMemberNameCharacters < 1 || MaxMemberNameCharacters > MaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMemberNameCharacters), "MaxMemberNameCharacters must be positive and cannot exceed MaxQueuedCharacters.");
            }

            if (MaxFilterCategories < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFilterCategories), "MaxFilterCategories must be greater than zero.");
            }

            if (MaxFilterCharacters < MaxCategoryCharacters)
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

            if (UnityConsoleMaxQueuedMessages < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(UnityConsoleMaxQueuedMessages), "UnityConsoleMaxQueuedMessages must be greater than zero.");
            }

            if (UnityConsoleMaxQueuedCharacters < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(UnityConsoleMaxQueuedCharacters), "UnityConsoleMaxQueuedCharacters must be greater than zero.");
            }

            if (UnityConsoleOverflowPolicy != LogQueueOverflowPolicy.DropNewest
                && UnityConsoleOverflowPolicy != LogQueueOverflowPolicy.DropOldest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(UnityConsoleOverflowPolicy),
                    "UnityConsoleOverflowPolicy supports only DropNewest or DropOldest because Unity main-thread handoff cannot block producers.");
            }


            long maxUnityEntryCharacters = (long)MaxMessageCharacters
                + MaxCategoryCharacters
                + (long)MaxSourcePathCharacters * 2L
                + UnityFormattingOverheadCharacters;
            if (maxUnityEntryCharacters > UnityConsoleMaxQueuedCharacters)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(UnityConsoleMaxQueuedCharacters),
                    "UnityConsoleMaxQueuedCharacters must contain one maximally formatted Unity entry.");
            }

            if (ShutdownDrainTimeoutMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ShutdownDrainTimeoutMs), "ShutdownDrainTimeoutMs cannot be negative.");
            }

            if (EnqueueBlockTimeoutMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(EnqueueBlockTimeoutMs), "EnqueueBlockTimeoutMs cannot be negative.");
            }

            if (MaintenanceIntervalMs < 10)
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

            if (!Enum.IsDefined(typeof(LogLevel), CriticalLevel) || CriticalLevel == LogLevel.None)
            {
                throw new ArgumentOutOfRangeException(nameof(CriticalLevel), "CriticalLevel must be a logging severity.");
            }
        }
    }
}
