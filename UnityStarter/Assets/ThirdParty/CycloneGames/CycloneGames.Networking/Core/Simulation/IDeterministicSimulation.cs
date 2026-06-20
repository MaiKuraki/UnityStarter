using System;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Genre-agnostic, numeric-agnostic deterministic simulation contract. A game implements this once to
    /// describe how one fixed simulation step advances state from a set of inputs, and can then drive it
    /// through any networking topology (lockstep, rollback, client prediction) via the matching adapter —
    /// without rewriting the simulation per topology.
    /// </summary>
    /// <typeparam name="TInput">
    /// Per-peer input for a single step. Must be <c>unmanaged</c> so it can live in the topologies' pre-allocated
    /// ring buffers with zero garbage. If a step needs a timestep, encode a fixed delta inside this struct; the
    /// contract is deliberately fixed-step and takes no delta-time parameter (variable delta breaks determinism).
    /// </typeparam>
    /// <typeparam name="TState">
    /// Complete simulation state. Must be <c>unmanaged</c> so snapshots are plain value copies the topologies can
    /// store, compare and roll back without allocation. Keep all simulation-relevant data inside this struct.
    /// </typeparam>
    /// <remarks>
    /// Determinism is the implementer's responsibility: given the same starting state and the same input span,
    /// <see cref="Simulate"/> must return an identical result on every platform. Use the fixed-point types in
    /// <c>CycloneGames.DeterministicMath</c> (not <c>float</c>) for any math whose result must match bit-for-bit
    /// across machines, as required by lockstep and rollback.
    /// </remarks>
    public interface IDeterministicSimulation<TInput, TState>
        where TInput : unmanaged
        where TState : unmanaged
    {
        /// <summary>
        /// Advances <paramref name="state"/> by exactly one fixed step using this frame's inputs. The span holds
        /// one input per peer for lockstep/rollback, or a single input for client prediction. Pure function:
        /// it must not read or mutate anything outside its arguments.
        /// </summary>
        TState Simulate(in TState state, ReadOnlySpan<TInput> inputs);

        /// <summary>
        /// Returns true when two states are equal for reconciliation/desync purposes. Client prediction uses this
        /// to decide whether a server snapshot requires a rollback; it may apply a tolerance for visual-only fields.
        /// </summary>
        bool StatesEqual(in TState a, in TState b);
    }

    /// <summary>
    /// Optional companion used by rollback to guess a peer's input for frames whose real input has not arrived.
    /// When not supplied, rollback repeats the last known input (the conventional default).
    /// </summary>
    public interface IRemoteInputPredictor<TInput>
        where TInput : unmanaged
    {
        TInput PredictRemoteInput(int peerId, in TInput lastKnownInput);
    }
}
