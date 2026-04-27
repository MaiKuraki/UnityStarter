using CycloneGames.Networking;

namespace CycloneGames.Networking.Unity.Runtime
{
    public sealed class UnityNetLogger : INetLogger
    {
        public static readonly UnityNetLogger Instance = new UnityNetLogger();

        private LogLevel _minLevel = LogLevel.Warning;

        public LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public bool IsLogLevelEnabled(LogLevel level) => level >= _minLevel;

        public void Log(LogLevel level, string message, string category = null)
        {
            if (!IsLogLevelEnabled(level)) return;

            switch (level)
            {
                case LogLevel.Debug:
                    UnityEngine.Debug.LogFormat("[{0}] {1}", category ?? LogCategory.Network, message);
                    break;
                case LogLevel.Info:
                    UnityEngine.Debug.LogFormat("[{0}] {1}", category ?? LogCategory.Network, message);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarningFormat("[{0}] {1}", category ?? LogCategory.Network, message);
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogErrorFormat("[{0}] {1}", category ?? LogCategory.Network, message);
                    break;
            }
        }
    }
}