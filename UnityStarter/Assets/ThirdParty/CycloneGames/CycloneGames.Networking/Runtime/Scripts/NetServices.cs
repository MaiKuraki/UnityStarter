namespace CycloneGames.Networking
{
    /// <summary>
    /// Central registry for networking services used by Cyclone gameplay modules.
    /// Defaults to no-op implementations so the project compiles and runs without any network stack.
    /// Runtime adapters (e.g., Mirror) can register themselves to override these.
    /// </summary>
    public static class NetServices
    {
        private static INetTransport _transport = new NoopNetTransport();
        private static IAbilityNetAdapter _ability = new NoopAbilityNetAdapter();

        public static INetTransport Transport
        {
            get => _transport;
            set => _transport = value ?? new NoopNetTransport();
        }

        public static IAbilityNetAdapter Ability
        {
            get => _ability;
            set => _ability = value ?? new NoopAbilityNetAdapter();
        }
    }
}