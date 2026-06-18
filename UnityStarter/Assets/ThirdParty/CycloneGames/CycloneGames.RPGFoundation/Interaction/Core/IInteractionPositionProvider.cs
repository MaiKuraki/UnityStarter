namespace CycloneGames.RPGFoundation.Interaction.Core
{
    public interface IInteractionPositionProvider
    {
        bool TryGetInteractionPosition(out InteractionVector3 position);
    }
}
