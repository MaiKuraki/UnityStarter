using System;

namespace CycloneGames.Logger
{
    public enum CLogAssertFailureBehavior : byte
    {
        LogOnly = 0,
        Throw = 1,
        LogAndThrow = 2
    }

    public sealed class CLogAssertOptions
    {
        public bool Enabled = true;
        public LogLevel FailureLevel = LogLevel.Error;
        public CLogAssertFailureBehavior FailureBehavior = CLogAssertFailureBehavior.LogOnly;
        public string Category = "Assert";
        public bool FlushBeforeThrow = true;
        public int FlushTimeoutMs = 100;

        public static CLogAssertOptions Default => new CLogAssertOptions();

        public CLogAssertOptions()
        {
        }

        public CLogAssertOptions(CLogAssertOptions source)
        {
            if (source == null) source = Default;

            Enabled = source.Enabled;
            FailureLevel = source.FailureLevel;
            FailureBehavior = source.FailureBehavior;
            Category = source.Category;
            FlushBeforeThrow = source.FlushBeforeThrow;
            FlushTimeoutMs = source.FlushTimeoutMs;
        }

        public CLogAssertOptions Clone()
        {
            return new CLogAssertOptions(this);
        }

        internal static CLogAssertRuntimeOptions CreateRuntimeOptions(CLogAssertOptions source)
        {
            var options = new CLogAssertOptions(source);
            options.Validate();
            return new CLogAssertRuntimeOptions(
                options.Enabled,
                options.FailureLevel,
                options.FailureBehavior,
                string.IsNullOrEmpty(options.Category) ? "Assert" : options.Category,
                options.FlushBeforeThrow,
                options.FlushTimeoutMs);
        }

        private void Validate()
        {
            if (!Enum.IsDefined(typeof(LogLevel), FailureLevel)) throw new ArgumentOutOfRangeException(nameof(FailureLevel), "Unknown failure log level.");
            if (!Enum.IsDefined(typeof(CLogAssertFailureBehavior), FailureBehavior)) throw new ArgumentOutOfRangeException(nameof(FailureBehavior), "Unknown failure behavior.");
            if (FlushTimeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(FlushTimeoutMs), "FlushTimeoutMs cannot be negative.");
        }
    }

    internal sealed class CLogAssertRuntimeOptions
    {
        public static readonly CLogAssertRuntimeOptions Default = CLogAssertOptions.CreateRuntimeOptions(CLogAssertOptions.Default);

        public readonly bool Enabled;
        public readonly LogLevel FailureLevel;
        public readonly CLogAssertFailureBehavior FailureBehavior;
        public readonly string Category;
        public readonly bool FlushBeforeThrow;
        public readonly int FlushTimeoutMs;

        public CLogAssertRuntimeOptions(
            bool enabled,
            LogLevel failureLevel,
            CLogAssertFailureBehavior failureBehavior,
            string category,
            bool flushBeforeThrow,
            int flushTimeoutMs)
        {
            Enabled = enabled;
            FailureLevel = failureLevel;
            FailureBehavior = failureBehavior;
            Category = category;
            FlushBeforeThrow = flushBeforeThrow;
            FlushTimeoutMs = flushTimeoutMs;
        }

        public bool ShouldLog => FailureLevel != LogLevel.None && (FailureBehavior == CLogAssertFailureBehavior.LogOnly || FailureBehavior == CLogAssertFailureBehavior.LogAndThrow);
        public bool ShouldThrow => FailureBehavior == CLogAssertFailureBehavior.Throw || FailureBehavior == CLogAssertFailureBehavior.LogAndThrow;

        public string ResolveCategory(string category)
        {
            return string.IsNullOrEmpty(category) ? Category : category;
        }
    }
}
