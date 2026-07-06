using CycloneGames.Choreography.Core;
using CycloneGames.Logger;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Bridges the engine-free <see cref="IChoreographyDiagnostics"/> contract to CycloneGames.Logger. Inject an
    /// instance into players, schedulers, and preload runners so Core diagnostics surface through the project's
    /// standard logging pipeline without the Core layer depending on the logger.
    /// </summary>
    public sealed class UnityChoreographyDiagnostics : IChoreographyDiagnostics
    {
        public const string DefaultCategory = "Choreography";

        public static readonly UnityChoreographyDiagnostics Instance = new UnityChoreographyDiagnostics();

        private readonly ChoreographyLogLevel _minLevel;

        public UnityChoreographyDiagnostics(ChoreographyLogLevel minLevel = ChoreographyLogLevel.Info)
        {
            _minLevel = minLevel;
        }

        public bool IsEnabled(ChoreographyLogLevel level) => level >= _minLevel;

        public void Log(ChoreographyLogLevel level, string category, string message)
        {
            if (level < _minLevel)
            {
                return;
            }

            string resolvedCategory = string.IsNullOrEmpty(category) ? DefaultCategory : category;
            switch (level)
            {
                case ChoreographyLogLevel.Error:
                    CLogger.LogError(message, resolvedCategory);
                    break;
                case ChoreographyLogLevel.Warning:
                    CLogger.LogWarning(message, resolvedCategory);
                    break;
                default:
                    CLogger.LogInfo(message, resolvedCategory);
                    break;
            }
        }
    }
}
