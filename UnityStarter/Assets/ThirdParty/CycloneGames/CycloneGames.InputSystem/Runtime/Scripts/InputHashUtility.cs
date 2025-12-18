namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Provides high-performance deterministic hashing (FNV-1a).
    /// </summary>
    public static class InputHashUtility
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// Computes a deterministic 32-bit FNV-1a hash for a string.
        /// </summary>
        public static int GetDeterministicHashCode(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            unchecked
            {
                uint hash = FnvOffsetBasis;
                int len = str.Length;
                for (int i = 0; i < len; i++)
                {
                    hash ^= str[i];
                    hash *= FnvPrime;
                }
                return (int)hash;
            }
        }

        /// <summary>
        /// Generates a composite Action ID from map and action names without string concatenation.
        /// Logically equivalent to hashing "mapName/actionName".
        /// </summary>
        public static int GetActionId(string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                return 0;

            unchecked
            {
                uint hash = FnvOffsetBasis;

                // Hash mapName
                int mapLen = mapName.Length;
                for (int i = 0; i < mapLen; i++)
                {
                    hash ^= mapName[i];
                    hash *= FnvPrime;
                }

                // Hash separator '/'
                hash ^= '/';
                hash *= FnvPrime;

                // Hash actionName
                int actionLen = actionName.Length;
                for (int i = 0; i < actionLen; i++)
                {
                    hash ^= actionName[i];
                    hash *= FnvPrime;
                }

                return (int)hash;
            }
        }

        /// <summary>
        /// Generates a composite Action ID from context, map and action names without string concatenation.
        /// Logically equivalent to hashing "contextName/mapName/actionName".
        /// This ensures different contexts with the same action map and action name produce different IDs.
        /// </summary>
        public static int GetActionId(string contextName, string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(contextName) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                return 0;

            unchecked
            {
                uint hash = FnvOffsetBasis;

                // Hash contextName
                int contextLen = contextName.Length;
                for (int i = 0; i < contextLen; i++)
                {
                    hash ^= contextName[i];
                    hash *= FnvPrime;
                }

                // Hash separator '/'
                hash ^= '/';
                hash *= FnvPrime;

                // Hash mapName
                int mapLen = mapName.Length;
                for (int i = 0; i < mapLen; i++)
                {
                    hash ^= mapName[i];
                    hash *= FnvPrime;
                }

                // Hash separator '/'
                hash ^= '/';
                hash *= FnvPrime;

                // Hash actionName
                int actionLen = actionName.Length;
                for (int i = 0; i < actionLen; i++)
                {
                    hash ^= actionName[i];
                    hash *= FnvPrime;
                }

                return (int)hash;
            }
        }
    }
}