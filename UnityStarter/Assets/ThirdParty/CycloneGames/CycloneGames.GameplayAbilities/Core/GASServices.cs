namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Service locator for GAS services. Provides default implementations while allowing DI override.
    /// Thread-safe with volatile read/write for lock-free access after initialization.
    /// </summary>
    public static class GASServices
    {
        private static volatile IGameplayCueManager s_CueManager;
        private static volatile ISimulationTimeProvider s_TimeProvider;
        private static volatile ISimulationRandomProvider s_RandomProvider;
        private static volatile IGASNetworkBridge s_NetworkBridge;
        private static volatile IGASReplicationResolver s_ReplicationResolver;
        private static volatile IGASDefinitionRegistry s_DefinitionRegistry;
        private static volatile IGASAttributeRegistry s_AttributeRegistry;

        /// <summary>
        /// Gets or sets the GameplayCue manager. Returns NullGameplayCueManager if not set.
        /// </summary>
        public static IGameplayCueManager CueManager
        {
            get => s_CueManager ?? NullGameplayCueManager.Instance;
            set => s_CueManager = value;
        }

        /// <summary>
        /// Gets or sets the time provider for simulation. Allows deterministic replay.
        /// </summary>
        public static ISimulationTimeProvider TimeProvider
        {
            get => s_TimeProvider ?? DefaultTimeProvider.Instance;
            set => s_TimeProvider = value;
        }

        /// <summary>
        /// Gets or sets the random provider for simulation. Allows deterministic replay.
        /// </summary>
        public static ISimulationRandomProvider RandomProvider
        {
            get => s_RandomProvider ?? DefaultRandomProvider.Instance;
            set => s_RandomProvider = value;
        }

        /// <summary>
        /// Gets or sets the network bridge.
        /// Defaults to <see cref="GASNullNetworkBridge.Instance"/> (local single-player routing).
        /// Replace with your networking-library implementation before any ability activation occurs.
        /// <example>
        /// // In your NetworkManager bootstrap:
        /// GASServices.NetworkBridge = new MyNetcodeGASBridge();
        /// </example>
        /// </summary>
        public static IGASNetworkBridge NetworkBridge
        {
            get => s_NetworkBridge ?? GASNullNetworkBridge.Instance;
            set => s_NetworkBridge = value;
        }

        /// <summary>
        /// Gets or sets the replication resolver.
        /// Defaults to <see cref="GASLocalReplicationResolver.Instance"/> (in-process stable IDs).
        /// </summary>
        public static IGASReplicationResolver ReplicationResolver
        {
            get => s_ReplicationResolver ?? GASLocalReplicationResolver.Instance;
            set => s_ReplicationResolver = value;
        }

        public static IGASDefinitionRegistry DefinitionRegistry
        {
            get => s_DefinitionRegistry ?? GASDefaultDefinitionRegistry.Instance;
            set => s_DefinitionRegistry = value;
        }

        public static IGASAttributeRegistry AttributeRegistry
        {
            get => s_AttributeRegistry ?? GASDefaultAttributeRegistry.Instance;
            set => s_AttributeRegistry = value;
        }

        /// <summary>
        /// Resets all services to null. Call during game shutdown or test teardown.
        /// </summary>
        public static void Reset()
        {
            s_CueManager = null;
            s_TimeProvider = null;
            s_RandomProvider = null;
            s_NetworkBridge = null;
            s_ReplicationResolver = null;
            s_DefinitionRegistry = null;
            s_AttributeRegistry = null;
        }
    }
}
