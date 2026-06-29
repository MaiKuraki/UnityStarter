namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Wire-compatible dirty-state bits for GAS replication planning.
    /// The bit layout mirrors CycloneGames.GameplayAbilities.Core.AbilitySystemStateChangeMask without making
    /// the transport-neutral networking core depend on Unity-facing runtime types.
    /// </summary>
    public static class GASReplicationChangeMask
    {
        public const uint None = 0u;
        public const uint GrantedAbilities = 1u << 0;
        public const uint ActiveEffects = 1u << 1;
        public const uint Attributes = 1u << 2;
        public const uint Tags = 1u << 3;
        public const uint All = GrantedAbilities | ActiveEffects | Attributes | Tags;
    }
}
