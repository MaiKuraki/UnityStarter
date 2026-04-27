using Cysharp.Threading.Tasks;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public interface INetworkInteractionSystem : IInteractionSystem
    {
        bool IsServerAuthoritative { get; }

        UniTask<InteractionResult> RequestInteractionAsync(InteractionRequest request);

        bool TryReserve(IInteractable target, InstigatorHandle instigator);
        void ReleaseReservation(IInteractable target, InstigatorHandle instigator);

        InteractionQueue GetQueue(IInteractable target);
    }
}
