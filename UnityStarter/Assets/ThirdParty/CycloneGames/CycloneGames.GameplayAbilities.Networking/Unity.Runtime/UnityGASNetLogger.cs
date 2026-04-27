using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking
{
    public sealed class UnityGASNetLogger : IGASNetLogger
    {
        public static readonly UnityGASNetLogger Instance = new UnityGASNetLogger();

        private UnityGASNetLogger() { }

        public void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }

        public void LogError(string message)
        {
            Debug.LogError(message);
        }
    }
}
