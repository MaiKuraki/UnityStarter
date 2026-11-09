using CycloneGames.AnimationRuntime.Core;
using CycloneGames.Logging;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Adapts the engine-free <see cref="IAnimationDiagnostics"/> contract to a CycloneGames
    /// <see cref="LogChannel"/>. Severity mapping is one-to-one; callers still gate on
    /// <see cref="IsEnabled"/> so message formatting only happens when the channel accepts it.
    /// </summary>
    internal sealed class LoggingAnimationDiagnostics : IAnimationDiagnostics
    {
        private readonly LogChannel _channel;

        internal LoggingAnimationDiagnostics(LogChannel channel)
        {
            _channel = channel;
        }

        public bool IsEnabled(AnimationDiagnosticLevel level)
        {
            return _channel.IsEnabled(ToSeverity(level));
        }

        public void Report(AnimationDiagnosticLevel level, string message)
        {
            _channel.Write(ToSeverity(level), message);
        }

        private static LogSeverity ToSeverity(AnimationDiagnosticLevel level)
        {
            switch (level)
            {
                case AnimationDiagnosticLevel.Error:
                    return LogSeverity.Error;
                case AnimationDiagnosticLevel.Warning:
                    return LogSeverity.Warning;
                default:
                    return LogSeverity.Info;
            }
        }
    }
}
