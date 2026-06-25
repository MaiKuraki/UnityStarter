using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkActionExtensions
    {
        public static NetworkActionCommand ToNetworkActionCommand(
            this MovementInputCommandMessage command,
            uint actionId = MovementNetworkActionIds.InputCommand)
        {
            return new NetworkActionCommand(
                command.EntityId,
                actionId,
                new NetworkTickId(command.ClientTick),
                new NetworkTickId(command.LastReceivedServerTick),
                command.InputSequence,
                command.PredictionKey,
                command.ButtonMask,
                command.CustomFlags,
                primaryVector: command.MoveAxes,
                secondaryVector: command.AimDirection);
        }
    }
}
