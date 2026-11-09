namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>Severity of a runtime diagnostic message.</summary>
    public enum AnimationDiagnosticLevel : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// Minimal diagnostics seam. The core assembly has no logging dependency; hosts bind it to
    /// CycloneGames.Logging or to UnityEngine.Debug through an adapter.
    /// </summary>
    public interface IAnimationDiagnostics
    {
        /// <summary>Whether a message at this level would be emitted. Callers must check before formatting.</summary>
        bool IsEnabled(AnimationDiagnosticLevel level);

        /// <summary>Emits a message. Callers must only build the string when <see cref="IsEnabled"/> is true.</summary>
        void Report(AnimationDiagnosticLevel level, string message);
    }

    /// <summary>Diagnostics implementation that drops everything. Always the default.</summary>
    public sealed class NullAnimationDiagnostics : IAnimationDiagnostics
    {
        public static readonly NullAnimationDiagnostics Instance = new NullAnimationDiagnostics();

        private NullAnimationDiagnostics()
        {
        }

        public bool IsEnabled(AnimationDiagnosticLevel level) => false;

        public void Report(AnimationDiagnosticLevel level, string message)
        {
        }
    }
}
