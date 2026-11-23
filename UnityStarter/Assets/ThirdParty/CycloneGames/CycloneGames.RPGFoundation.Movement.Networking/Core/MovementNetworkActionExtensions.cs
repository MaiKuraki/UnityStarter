using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkActionExtensions
    {
        public static NetworkActionCommand ToNetworkActionCommand(
            this MovementInputCommandMessage command,
            uint actionId = MovementWireIds.InputCommandAction)
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

        public static NetworkActionCommand ToNetworkActionCommand(
            this MovementInputBatchMessage batch,
            uint actionId = MovementWireIds.InputBatchAction)
        {
            MovementInputCommandMessage newest = batch.Newest;
            return new NetworkActionCommand(
                batch.EntityId,
                actionId,
                new NetworkTickId(newest.ClientTick),
                new NetworkTickId(newest.LastReceivedServerTick),
                newest.InputSequence,
                newest.PredictionKey,
                newest.ButtonMask,
                newest.CustomFlags,
                primaryVector: newest.MoveAxes,
                secondaryVector: newest.AimDirection);
        }
    }
}
