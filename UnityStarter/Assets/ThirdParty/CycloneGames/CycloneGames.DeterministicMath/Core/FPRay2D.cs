namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 2D ray.</summary>
    public readonly struct FPRay2D
    {
        public readonly FPVector2 Origin;
        public readonly FPVector2 Direction;

        public FPRay2D(FPVector2 origin, FPVector2 direction)
        {
            Origin = origin;
            Direction = direction;
        }
    }
}
