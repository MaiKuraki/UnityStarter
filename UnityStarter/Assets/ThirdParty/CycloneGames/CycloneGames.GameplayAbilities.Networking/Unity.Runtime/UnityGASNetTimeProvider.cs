using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking
{
    public sealed class UnityGASNetTimeProvider : IGASNetTimeProvider
    {
        public static readonly UnityGASNetTimeProvider Instance = new UnityGASNetTimeProvider();

        private UnityGASNetTimeProvider() { }

        public double CurrentTimeSeconds => Time.unscaledTimeAsDouble;
    }
}
