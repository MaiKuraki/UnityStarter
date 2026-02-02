namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface ITwoStateInteraction
    {
        bool IsActivated { get; }
        void ActivateState();
        void DeactivateState();
        void ToggleState();
    }
}