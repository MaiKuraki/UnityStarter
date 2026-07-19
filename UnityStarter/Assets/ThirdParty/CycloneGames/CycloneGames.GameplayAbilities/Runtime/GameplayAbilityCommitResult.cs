namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum GameplayAbilityCommitResultCode : byte
    {
        Committed,
        MissingOwner,
        InvalidCostDefinition,
        InvalidCooldownDefinition,
        CostUnavailable,
        CooldownActive,
        CostEffectRejected,
        CooldownEffectRejected
    }

    public readonly struct GameplayAbilityCommitResult
    {
        public GameplayAbilityCommitResult(
            GameplayAbilityCommitResultCode code,
            GameplayEffectApplicationResultCode effectResult = GameplayEffectApplicationResultCode.Applied)
        {
            Code = code;
            EffectResult = effectResult;
        }

        public GameplayAbilityCommitResultCode Code { get; }
        public GameplayEffectApplicationResultCode EffectResult { get; }
        public bool Succeeded => Code == GameplayAbilityCommitResultCode.Committed;
    }
}
