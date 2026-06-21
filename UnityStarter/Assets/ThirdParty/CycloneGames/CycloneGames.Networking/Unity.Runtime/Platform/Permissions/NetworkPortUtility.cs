namespace CycloneGames.Networking.Platform
{
    public static class NetworkPortUtility
    {
        public const int MIN_PORT = 1;
        public const int MAX_PORT = 65535;

        public static bool IsValidPort(int port)
        {
            return port >= MIN_PORT && port <= MAX_PORT;
        }

        public static string CreateInvalidPortMessage(int port)
        {
            return $"Port {port} is invalid. Use a value from {MIN_PORT} to {MAX_PORT}.";
        }

        public static NetworkHostPermissionCheckResult CreateInvalidPortResult(int port, string platformName)
        {
            return new NetworkHostPermissionCheckResult(
                NetworkHostPermissionStatus.Failed,
                false,
                false,
                platformName,
                CreateInvalidPortMessage(port));
        }

        public static NetworkHostPermissionRequestResult CreateInvalidPortRequestResult(int port)
        {
            return new NetworkHostPermissionRequestResult(
                NetworkHostPermissionRequestOutcome.InvalidInput,
                CreateInvalidPortMessage(port));
        }
    }
}
