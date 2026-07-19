namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Controls how ability runtime instances are created and reused.
    /// 
    /// NonInstanced: a single shared instance serves all activations (no per-actor state allowed).
    /// InstancedPerActor: one instance per ASC, reused across activations (default for most abilities).
    /// InstancedPerExecution: a new instance per activation (needed when overlapping executions must not share state).
    /// </summary>
    public enum GASInstancingPolicy : byte
    {
        NonInstanced,
        InstancedPerActor,
        InstancedPerExecution
    }

    /// <summary>
    /// Arithmetic operation applied by a modifier to its target attribute.
    /// Override bypasses all stacking — the attribute's value is set directly
    /// to the modifier's magnitude, ignoring any Add/Multiply/Divide contributions.
    /// </summary>
    public enum GASModifierOp : byte
    {
        Add,
        Multiply,
        Division,
        Override
    }

    /// <summary>
    /// Ordered modifier evaluation layers for attribute aggregators.
    /// Channel0 is the default path and is enough for most effects. Additional channels let advanced
    /// systems express "evaluate these modifiers after the previous layer" without hard-coded rules.
    /// </summary>
    public enum GASModifierEvaluationChannel : byte
    {
        Channel0,
        Channel1,
        Channel2,
        Channel3,
        Channel4,
        Channel5,
        Channel6,
        Channel7,
        Channel8,
        Channel9
    }

    public static class GASModifierEvaluationChannels
    {
        public const int MAX_CHANNEL_COUNT = 10;

        public static bool IsValid(GASModifierEvaluationChannel channel)
        {
            return (byte)channel < MAX_CHANNEL_COUNT;
        }

        public static GASModifierEvaluationChannel Normalize(GASModifierEvaluationChannel channel)
        {
            return IsValid(channel) ? channel : GASModifierEvaluationChannel.Channel0;
        }
    }

    /// <summary>
    /// Controls an effect's lifecycle.
    /// Instant: applied once and immediately consumed (damage, cost).
    /// Duration: active for a fixed number of ticks, then auto-removed (buffs, debuffs, DoTs).
    /// Infinite: active until manually removed (equipment stats, auras, passives).
    /// </summary>
    public enum GASEffectDurationPolicy : byte
    {
        Instant,
        Infinite,
        Duration
    }

}
