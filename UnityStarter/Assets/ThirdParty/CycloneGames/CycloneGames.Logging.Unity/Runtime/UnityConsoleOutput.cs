using UnityEngine;

namespace CycloneGames.Logging.Unity
{
    /// <summary>
    /// The single Unity Console output boundary for the package. Callers must invoke it
    /// on Unity's main thread.
    /// </summary>
    internal static class UnityConsoleOutput
    {
        private static readonly object[] FormatArguments = new object[1];

        [HideInCallstack]
        internal static void Write(LogType logType, string message)
        {
            FormatArguments[0] = message;
            try
            {
                UnityEngine.Debug.LogFormat(logType, LogOption.NoStacktrace, null, "{0}", FormatArguments);
            }
            finally
            {
                FormatArguments[0] = null;
            }
        }
    }
}
