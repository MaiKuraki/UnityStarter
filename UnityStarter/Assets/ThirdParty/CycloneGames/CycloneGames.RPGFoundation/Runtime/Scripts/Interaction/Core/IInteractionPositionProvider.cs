namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface IInteractionPositionProvider
    {
        bool TryGetInteractionPosition(out InteractionVector3 position);
    }
}
