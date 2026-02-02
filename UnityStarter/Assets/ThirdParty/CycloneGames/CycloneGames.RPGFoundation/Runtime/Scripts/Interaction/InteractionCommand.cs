namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionCommand : VitalRouter.ICommand
    {
        public readonly IInteractable Target;

        public InteractionCommand(IInteractable target)
        {
            Target = target;
        }
    }
}