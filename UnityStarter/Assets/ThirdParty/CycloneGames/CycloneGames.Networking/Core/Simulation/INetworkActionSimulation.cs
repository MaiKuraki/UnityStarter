namespace CycloneGames.Networking.Simulation
{
    public interface INetworkActionSimulation<TState>
        where TState : unmanaged
    {
        NetworkActionResult Apply(
            in NetworkActionCommand command,
            in TState currentState,
            out TState nextState);

        bool StatesEqual(in TState predicted, in TState authoritative);

        ulong ComputeStateHash(in TState state);
    }
}
