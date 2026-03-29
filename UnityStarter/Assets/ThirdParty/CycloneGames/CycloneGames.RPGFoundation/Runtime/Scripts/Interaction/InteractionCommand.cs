namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionCommand : VitalRouter.ICommand
    {
        public readonly IInteractable Target;
        public readonly string ActionId;
        public readonly InstigatorHandle Instigator;

        public InteractionCommand(IInteractable target, string actionId = null, InstigatorHandle instigator = null)
        {
            Target = target;
            ActionId = actionId;
            Instigator = instigator;
        }
    }
}