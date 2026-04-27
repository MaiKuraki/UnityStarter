namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Determines where ability execution happens and how client-server authority is split.
    /// 
    /// LocalOnly: client-only, never touches the network (cosmetics, UI feedback).
    /// LocalPredicted: client runs immediately, server validates; rejected predictions are rolled back.
    /// ServerOnly: client sends a request, server runs; client waits for the result.
    /// ServerInitiated: only the server may start this ability (AI-driven abilities, boss mechanics).
    /// </summary>
    public enum GASNetExecutionPolicy : byte
    {
        LocalOnly,
        LocalPredicted,
        ServerOnly,
        ServerInitiated
    }

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
    /// Which clients receive replicated state for this effect or ability.
    /// 
    /// None: never replicated (local-only effects, offline games).
    /// OwnerOnly: only the owning client receives updates.
    /// SimulatedOnly: all clients except the owner (for visual-only effects on other players).
    /// Everyone: broadcast to all connected clients.
    /// </summary>
    public enum GASReplicationPolicy : byte
    {
        None,
        OwnerOnly,
        SimulatedOnly,
        Everyone
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

    /// <summary>
    /// Result of an ability activation attempt.
    /// Accepted: authority confirmed the activation.
    /// Predicted: client ran the activation locally; server confirmation is pending (may roll back).
    /// MissingSpec: the requested spec handle does not exist.
    /// InvalidPredictionKey: LocalPredicted activation was called without a valid prediction key.
    /// NetworkRejected: the server refused the client's predicted activation.
    /// </summary>
    public enum GASAbilityActivationResultCode : byte
    {
        Accepted,
        Predicted,
        MissingSpec,
        InvalidPredictionKey,
        NetworkRejected
    }
}
