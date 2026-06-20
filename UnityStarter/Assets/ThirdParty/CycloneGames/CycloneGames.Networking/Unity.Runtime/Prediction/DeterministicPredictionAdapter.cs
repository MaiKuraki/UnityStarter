using System;
using CycloneGames.Networking.Prediction;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Bridges an <see cref="IDeterministicSimulation{TInput, TState}"/> to
    /// <see cref="IPredictable{TInput, TState}"/> so it can be driven by
    /// <see cref="ClientPredictionSystem{TInput, TState}"/>. The adapter owns the predicted state; the host
    /// supplies a local input source because capturing player input is game-specific and outside the
    /// deterministic step.
    /// </summary>
    /// <remarks>
    /// The reconciliation delta-time is ignored: the unified contract is fixed-step and expects any delta to be
    /// encoded inside <typeparamref name="TInput"/>. A single input is widened to a one-element span so the same
    /// <see cref="IDeterministicSimulation{TInput, TState}.Simulate"/> definition serves prediction and the
    /// multi-peer topologies.
    /// </remarks>
    public sealed class DeterministicPredictionAdapter<TInput, TState>
        : IPredictable<TInput, TState>
        where TInput : unmanaged
        where TState : unmanaged
    {
        private readonly IDeterministicSimulation<TInput, TState> _simulation;
        private readonly Func<TInput> _captureInput;
        private TState _state;

        public DeterministicPredictionAdapter(
            IDeterministicSimulation<TInput, TState> simulation,
            Func<TInput> captureInput,
            TState initialState = default)
        {
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _captureInput = captureInput ?? throw new ArgumentNullException(nameof(captureInput));
            _state = initialState;
        }

        public TState State => _state;

        public TInput CaptureInput()
        {
            return _captureInput();
        }

        public TState CaptureState()
        {
            return _state;
        }

        public void ApplyState(in TState state)
        {
            _state = state;
        }

        public void SimulateStep(in TInput input, float deltaTime)
        {
            Span<TInput> single = stackalloc TInput[1];
            single[0] = input;
            _state = _simulation.Simulate(_state, single);
        }

        public bool StatesMatch(in TState predicted, in TState authoritative)
        {
            return _simulation.StatesEqual(predicted, authoritative);
        }
    }
}
