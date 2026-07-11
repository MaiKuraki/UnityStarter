namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 3D ray.</summary>
    public readonly struct FPRay3D
    {
        public readonly FPVector3 Origin;
        public readonly FPVector3 Direction;

        public FPRay3D(FPVector3 origin, FPVector3 direction)
        {
            Origin = origin;
            Direction = direction;
        }
    }
}
