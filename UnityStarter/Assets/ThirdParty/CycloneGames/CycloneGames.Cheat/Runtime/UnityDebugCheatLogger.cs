using System;
using UnityEngine;

namespace CycloneGames.Cheat.Runtime
{
    /// <summary>
    /// Default logger implementation using Unity's Debug API.
    /// Logs errors and exceptions to Unity Console.
    /// </summary>
    public sealed class UnityDebugCheatLogger : ICheatLogger
    {
        /// <summary>
        /// Logs an error message using Unity's Debug.LogError.
        /// </summary>
        public void LogError(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Debug.LogError($"[CheatCommand] {message}");
            }
        }

        /// <summary>
        /// Logs an exception using Unity's Debug.LogException.
        /// </summary>
        public void LogException(Exception exception)
        {
            if (exception != null)
            {
                Debug.LogException(exception);
            }
        }
    }
}
