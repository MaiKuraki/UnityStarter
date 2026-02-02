using R3;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface IInteractionDetector
    {
        ReadOnlyReactiveProperty<IInteractable> CurrentInteractable { get; }
        void TryInteract();
        void SetDetectionEnabled(bool enabled);
    }
}