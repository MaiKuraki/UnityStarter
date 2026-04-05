using System;
using UnityEngine;

namespace CycloneGames.Cheat.Runtime
{
    public sealed class UnityDebugCheatLogger : ICheatLogger
    {
        public void LogError(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Debug.LogError(string.Concat("[CheatCommand] ", message));
            }
        }

        public void LogException(Exception exception)
        {
            if (exception != null)
            {
                Debug.LogException(exception);
            }
        }
    }
}


