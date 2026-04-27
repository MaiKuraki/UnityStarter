namespace CycloneGames.GameplayAbilities.Networking
{
    public static class GASNetLogger
    {
        public static IGASNetLogger Instance { get; set; }

        public static void LogWarning(string message)
        {
            Instance?.LogWarning(message);
        }

        public static void LogError(string message)
        {
            Instance?.LogError(message);
        }
    }
}
