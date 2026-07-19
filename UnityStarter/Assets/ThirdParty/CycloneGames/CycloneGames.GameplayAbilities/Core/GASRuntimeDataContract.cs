namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Bounds and schema identity for process-local GameplayAbilities runtime data.
    /// </summary>
    public static class GASRuntimeDataContract
    {
        public const ushort ReconciliationSchemaVersion = 1;
        public const int MaxGameplayLevel = ushort.MaxValue;
    }
}
