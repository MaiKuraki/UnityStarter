namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum GameplayEffectApplicationResultCode : byte
    {
        Applied,
        Executed,
        Stacked,
        InvalidSpec,
        InvalidDefinition,
        RuntimeContextMismatch,
        StateResyncRequired,
        BlockedByImmunity,
        MissingRequiredTags,
        BlockedByForbiddenTags,
        BlockedByCustomRequirement,
        ActiveEffectLimitReached,
        PredictionLimitReached,
        PredictionUnsupported,
        ReentrantMutationRejected,
        GrantedAbilityLimitReached,
        ExecutionFailed,
        DurationCommitFailed
    }

    public readonly struct GameplayEffectApplicationResult
    {
        public GameplayEffectApplicationResult(
            GameplayEffectApplicationResultCode code,
            ActiveGameplayEffect activeEffect = null)
        {
            Code = code;
            ActiveEffect = activeEffect;
        }

        public GameplayEffectApplicationResultCode Code { get; }
        public ActiveGameplayEffect ActiveEffect { get; }
        public bool Succeeded =>
            Code == GameplayEffectApplicationResultCode.Applied ||
            Code == GameplayEffectApplicationResultCode.Executed ||
            Code == GameplayEffectApplicationResultCode.Stacked;
    }
}
