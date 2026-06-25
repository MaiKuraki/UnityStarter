namespace CycloneGames.Networking.Simulation
{
    public interface INetworkActionValidator
    {
        NetworkActionResult Validate(
            in NetworkActionCommand command,
            in NetworkActionValidationContext context);
    }
}
