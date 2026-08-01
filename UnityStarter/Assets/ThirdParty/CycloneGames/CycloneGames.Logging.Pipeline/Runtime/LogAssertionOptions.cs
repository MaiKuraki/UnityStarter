using System;
using CycloneGames.Logging;

namespace CycloneGames.Logging
{
    public enum LogAssertionFailureBehavior : byte
    {
        LogOnly = 0,
        Throw = 1,
        LogAndThrow = 2
    }

    public sealed class LogAssertionOptions
    {
        public bool Enabled = true;
        public LogSeverity FailureSeverity = LogSeverity.Error;
        public LogAssertionFailureBehavior FailureBehavior = LogAssertionFailureBehavior.LogOnly;
        public string Category = "Assert";
        public bool FlushBeforeThrow = true;
        public int FlushTimeoutMs = 100;

        public static LogAssertionOptions Default => new LogAssertionOptions();

        public LogAssertionOptions()
        {
        }

        public LogAssertionOptions(LogAssertionOptions source)
        {
            if (source == null) source = Default;

            Enabled = source.Enabled;
            FailureSeverity = source.FailureSeverity;
            FailureBehavior = source.FailureBehavior;
            Category = source.Category;
            FlushBeforeThrow = source.FlushBeforeThrow;
            FlushTimeoutMs = source.FlushTimeoutMs;
        }

        public LogAssertionOptions Clone()
        {
            return new LogAssertionOptions(this);
        }

        internal static LogAssertionRuntimeOptions CreateRuntimeOptions(LogAssertionOptions source)
        {
            var options = new LogAssertionOptions(source);
            options.Validate();
            return new LogAssertionRuntimeOptions(
                options.Enabled,
                options.FailureSeverity,
                options.FailureBehavior,
                string.IsNullOrEmpty(options.Category) ? "Assert" : options.Category,
                options.FlushBeforeThrow,
                options.FlushTimeoutMs);
        }

        private void Validate()
        {
            if (!Enum.IsDefined(typeof(LogSeverity), FailureSeverity)) throw new ArgumentOutOfRangeException(nameof(FailureSeverity), "Unknown failure log severity.");
            if (!Enum.IsDefined(typeof(LogAssertionFailureBehavior), FailureBehavior)) throw new ArgumentOutOfRangeException(nameof(FailureBehavior), "Unknown failure behavior.");
            if (FlushTimeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(FlushTimeoutMs), "FlushTimeoutMs cannot be negative.");
        }
    }

    internal sealed class LogAssertionRuntimeOptions
    {
        public static readonly LogAssertionRuntimeOptions Default = LogAssertionOptions.CreateRuntimeOptions(LogAssertionOptions.Default);

        public readonly bool Enabled;
        public readonly LogSeverity FailureSeverity;
        public readonly LogAssertionFailureBehavior FailureBehavior;
        public readonly string Category;
        public readonly bool FlushBeforeThrow;
        public readonly int FlushTimeoutMs;

        public LogAssertionRuntimeOptions(
            bool enabled,
            LogSeverity failureLevel,
            LogAssertionFailureBehavior failureBehavior,
            string category,
            bool flushBeforeThrow,
            int flushTimeoutMs)
        {
            Enabled = enabled;
            FailureSeverity = failureLevel;
            FailureBehavior = failureBehavior;
            Category = category;
            FlushBeforeThrow = flushBeforeThrow;
            FlushTimeoutMs = flushTimeoutMs;
        }

        public bool ShouldLog => FailureSeverity != LogSeverity.None && (FailureBehavior == LogAssertionFailureBehavior.LogOnly || FailureBehavior == LogAssertionFailureBehavior.LogAndThrow);
        public bool ShouldThrow => FailureBehavior == LogAssertionFailureBehavior.Throw || FailureBehavior == LogAssertionFailureBehavior.LogAndThrow;

        public string ResolveCategory(string category)
        {
            return string.IsNullOrEmpty(category) ? Category : category;
        }
    }
}
