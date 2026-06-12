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
        public const int DefaultUnityConsoleMaxQueuedMessages = 4096;
        public const int DefaultShutdownDrainTimeoutMs = 2000;

        public int MaxQueuedMessages = DefaultMaxQueuedMessages;
        public int UnityConsoleMaxQueuedMessages = DefaultUnityConsoleMaxQueuedMessages;
        public int ShutdownDrainTimeoutMs = DefaultShutdownDrainTimeoutMs;
        public int EnqueueBlockTimeoutMs = 1;
        public LogQueueOverflowPolicy OverflowPolicy = LogQueueOverflowPolicy.DropNewest;
        public LogLevel GuaranteedLevel = LogLevel.Error;

        public static readonly LoggerProcessingOptions Default = new LoggerProcessingOptions();

        public LoggerProcessingOptions()
        {
        }

        public LoggerProcessingOptions(LoggerProcessingOptions source)
        {
            if (source == null) source = Default;

            MaxQueuedMessages = source.MaxQueuedMessages;
            UnityConsoleMaxQueuedMessages = source.UnityConsoleMaxQueuedMessages;
            ShutdownDrainTimeoutMs = source.ShutdownDrainTimeoutMs;
            EnqueueBlockTimeoutMs = source.EnqueueBlockTimeoutMs;
            OverflowPolicy = source.OverflowPolicy;
            GuaranteedLevel = source.GuaranteedLevel;
        }

        public LoggerProcessingOptions Clone()
        {
            return new LoggerProcessingOptions(this);
        }

        internal static LoggerProcessingOptions CreateValidated(LoggerProcessingOptions source)
        {
            var options = new LoggerProcessingOptions(source);
            options.Validate();
            return options;
        }

        internal void Validate()
        {
            if (MaxQueuedMessages < 1) throw new ArgumentOutOfRangeException(nameof(MaxQueuedMessages), "MaxQueuedMessages must be greater than zero.");
            if (UnityConsoleMaxQueuedMessages < 1) throw new ArgumentOutOfRangeException(nameof(UnityConsoleMaxQueuedMessages), "UnityConsoleMaxQueuedMessages must be greater than zero.");
            if (ShutdownDrainTimeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(ShutdownDrainTimeoutMs), "ShutdownDrainTimeoutMs cannot be negative.");
            if (EnqueueBlockTimeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(EnqueueBlockTimeoutMs), "EnqueueBlockTimeoutMs cannot be negative.");
            if (!Enum.IsDefined(typeof(LogQueueOverflowPolicy), OverflowPolicy)) throw new ArgumentOutOfRangeException(nameof(OverflowPolicy), "Unknown overflow policy.");
            if (!Enum.IsDefined(typeof(LogLevel), GuaranteedLevel)) throw new ArgumentOutOfRangeException(nameof(GuaranteedLevel), "Unknown guaranteed log level.");
        }
    }
}
