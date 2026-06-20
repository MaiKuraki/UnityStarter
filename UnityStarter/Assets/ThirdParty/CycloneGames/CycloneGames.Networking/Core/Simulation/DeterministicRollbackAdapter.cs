using System;
using CycloneGames.DeterministicMath;
using CycloneGames.Networking.Lockstep;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Bridges an <see cref="IDeterministicSimulation{TInput, TState}"/> to
    /// <see cref="RollbackNetcode{TInput, TState}.IRollbackSimulation"/>. Because the simulation state is a plain
    /// value, snapshots are just copies: <see cref="SaveState"/> returns the current state and
    /// <see cref="LoadState"/> restores it, giving GGPO-style rollback for free.
    /// </summary>
    /// <remarks>
    /// The fixed timestep supplied by rollback is ignored: the unified contract is fixed-step and expects any
    /// delta to be encoded inside <typeparamref name="TInput"/>. Remote-input prediction defaults to repeating
    /// the last known input unless an <see cref="IRemoteInputPredictor{TInput}"/> is provided.
    /// </remarks>
    public sealed class DeterministicRollbackAdapter<TInput, TState>
        : RollbackNetcode<TInput, TState>.IRollbackSimulation
        where TInput : unmanaged, IEquatable<TInput>
        where TState : unmanaged
    {
        private readonly IDeterministicSimulation<TInput, TState> _simulation;
        private readonly IRemoteInputPredictor<TInput> _inputPredictor;
        private TState _state;

        public DeterministicRollbackAdapter(
            IDeterministicSimulation<TInput, TState> simulation,
            TState initialState = default,
            IRemoteInputPredictor<TInput> inputPredictor = null)
        {
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _inputPredictor = inputPredictor;
            _state = initialState;
        }

        public TState State => _state;

        public TInput PredictInput(int peerId, TInput lastKnownInput)
        {
            return _inputPredictor != null
                ? _inputPredictor.PredictRemoteInput(peerId, lastKnownInput)
                : lastKnownInput;
        }

        public TState SaveState()
        {
            return _state;
        }

        public void LoadState(in TState state)
        {
            _state = state;
        }

        public void Simulate(ReadOnlySpan<TInput> peerInputs, FPInt64 deltaTime)
        {
            _state = _simulation.Simulate(_state, peerInputs);
        }

        public void OnRollback(int framesToRollback)
        {
        }
    }
}
