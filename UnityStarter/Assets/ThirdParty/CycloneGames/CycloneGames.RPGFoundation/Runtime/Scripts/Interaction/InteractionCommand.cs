namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionCommand : VitalRouter.ICommand
    {
        public readonly IInteractable Target;
        public readonly string ActionId;
        public readonly InstigatorHandle Instigator;
        public readonly int WorldId;
        public readonly ulong TargetStableId;
        public readonly ulong InstigatorStableId;

        public InteractionCommand(IInteractable target, string actionId = null, InstigatorHandle instigator = null, int worldId = 0)
        {
            Target = target;
            ActionId = actionId;
            Instigator = instigator;
            WorldId = worldId;
            TargetStableId = target is IInteractionStableIdentity identity ? identity.StableIdHash : InteractionStableId.None;
            InstigatorStableId = instigator?.StableId ?? InteractionStableId.None;
        }
    }
}
