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

        public static readonly CLogAssertOptions Default = new CLogAssertOptions();

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
                string.IsNullOrEmpty(options.Category) ? "Assert" : options.Category);
        }

        private void Validate()
        {
            if (!Enum.IsDefined(typeof(LogLevel), FailureLevel)) throw new ArgumentOutOfRangeException(nameof(FailureLevel), "Unknown failure log level.");
            if (!Enum.IsDefined(typeof(CLogAssertFailureBehavior), FailureBehavior)) throw new ArgumentOutOfRangeException(nameof(FailureBehavior), "Unknown failure behavior.");
        }
    }

    internal sealed class CLogAssertRuntimeOptions
    {
        public static readonly CLogAssertRuntimeOptions Default = CLogAssertOptions.CreateRuntimeOptions(CLogAssertOptions.Default);

        public readonly bool Enabled;
        public readonly LogLevel FailureLevel;
        public readonly CLogAssertFailureBehavior FailureBehavior;
        public readonly string Category;

        public CLogAssertRuntimeOptions(bool enabled, LogLevel failureLevel, CLogAssertFailureBehavior failureBehavior, string category)
        {
            Enabled = enabled;
            FailureLevel = failureLevel;
            FailureBehavior = failureBehavior;
            Category = category;
        }

        public bool ShouldLog => FailureLevel != LogLevel.None && (FailureBehavior == CLogAssertFailureBehavior.LogOnly || FailureBehavior == CLogAssertFailureBehavior.LogAndThrow);
        public bool ShouldThrow => FailureBehavior == CLogAssertFailureBehavior.Throw || FailureBehavior == CLogAssertFailureBehavior.LogAndThrow;

        public string ResolveCategory(string category)
        {
            return string.IsNullOrEmpty(category) ? Category : category;
        }
    }
}
