using System.Diagnostics;

namespace Proxima
{
    internal class Log
    {
        [Conditional("PROXIMA_LOG_VERBOSE")]
        public static void Verbose(string message)
        {
            UnityEngine.Debug.Log("PXV: " + message);
        }

        public static void Info(string message)
        {
            UnityEngine.Debug.Log("PX: " + message);
        }

        public static void Warning(string message)
        {
            UnityEngine.Debug.LogWarning("PX: " + message);
        }

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError("PX: " + message);
        }

        public static void Exception(System.Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
    }
}