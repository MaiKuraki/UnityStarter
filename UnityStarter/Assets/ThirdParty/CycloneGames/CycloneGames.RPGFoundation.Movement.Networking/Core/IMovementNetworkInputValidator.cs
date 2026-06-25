using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public interface IMovementNetworkInputValidator
    {
        NetworkActionResult Validate(
            in MovementInputCommandMessage command,
            in MovementNetworkInputValidationContext context);
    }
}
