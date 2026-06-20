using System;
using CycloneGames.Networking.Lockstep;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Drives an <see cref="IDeterministicSimulation{TInput, TState}"/> from a <see cref="LockstepManager{TInput}"/>.
    /// The adapter owns the simulation state (lockstep itself is stateless about game state) and advances it
    /// whenever lockstep confirms a frame. Read <see cref="State"/> to render between frames.
    /// </summary>
    public sealed class DeterministicLockstepAdapter<TInput, TState>
        where TInput : unmanaged
        where TState : unmanaged
    {
        private readonly IDeterministicSimulation<TInput, TState> _simulation;
        private TState _state;

        public DeterministicLockstepAdapter(
            IDeterministicSimulation<TInput, TState> simulation,
            TState initialState = default)
        {
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _state = initialState;
        }

        public TState State => _state;

        /// <summary>
        /// Matches <see cref="LockstepManager{TInput}.SimulateFrameDelegate"/>. Subscribe via
        /// <see cref="AttachTo"/> or hook it onto <c>OnSimulateFrame</c> directly.
        /// </summary>
        public void SimulateFrame(int frame, ReadOnlySpan<TInput> peerInputs)
        {
            _state = _simulation.Simulate(_state, peerInputs);
        }

        public void AttachTo(LockstepManager<TInput> manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            manager.OnSimulateFrame += SimulateFrame;
        }

        public void DetachFrom(LockstepManager<TInput> manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            manager.OnSimulateFrame -= SimulateFrame;
        }

        public void ResetState(in TState state)
        {
            _state = state;
        }
    }
}
