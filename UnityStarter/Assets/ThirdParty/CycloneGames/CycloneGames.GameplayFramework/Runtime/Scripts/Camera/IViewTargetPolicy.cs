namespace CycloneGames.GameplayFramework.Runtime
{
    public interface IViewTargetPolicy
    {
        Actor ResolveViewTarget(CameraContext context, Actor suggestedTarget);
    }

    public sealed class DefaultGameplayViewTargetPolicy : IViewTargetPolicy
    {
        public Actor ResolveViewTarget(CameraContext context, Actor suggestedTarget)
        {
            if (context == null || context.Owner == null)
            {
                return suggestedTarget;
            }

            if (context.ManualViewTargetOverride != null)
            {
                return context.ManualViewTargetOverride;
            }

            if (suggestedTarget != null)
            {
                return suggestedTarget;
            }

            Pawn pawn = context.Owner.GetPawn();
            if (pawn != null)
            {
                return pawn;
            }

            SpectatorPawn spectatorPawn = context.Owner.GetSpectatorPawn();
            if (spectatorPawn != null)
            {
                return spectatorPawn;
            }

            return context.Owner;
        }
    }
}