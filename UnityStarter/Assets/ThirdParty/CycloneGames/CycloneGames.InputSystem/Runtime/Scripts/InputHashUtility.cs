namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Generates deterministic hash codes using FNV-1a algorithm for cross-platform consistency.
    /// </summary>
    public static class InputHashUtility
    {
        /// <summary>
        /// FNV-1a hash algorithm for cross-platform deterministic hashing.
        /// </summary>
        public static int GetDeterministicHashCode(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            unchecked
            {
                const uint fnvOffsetBasis = 2166136261u;
                const uint fnvPrime = 16777619u;
                uint hash = fnvOffsetBasis;

                for (int i = 0; i < str.Length; i++)
                {
                    hash ^= str[i];
                    hash *= fnvPrime;
                }

                return (int)hash;
            }
        }

        /// <summary>
        /// Generates action ID from map and action names. Format: "mapName/actionName"
        /// </summary>
        public static int GetActionId(string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                return 0;

            return GetDeterministicHashCode($"{mapName}/{actionName}");
        }
    }
}