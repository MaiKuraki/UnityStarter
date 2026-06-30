namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public enum ProjectileDefinitionValidationCode : ushort
    {
        None,
        InvalidDefinitionId,
        NonFiniteKinematicValue,
        NegativeInitialSpeed,
        NegativeMaxSpeed,
        MaxSpeedBelowInitialSpeed,
        NegativeRadius,
        InvalidMaxLifetime,
        InvalidTurnRate,
        LeadHomingWithoutLeadTime,
        NegativeLeadPredictionTime,
        NegativePierceCount,
        NegativeBounceCount,
        EmptyCollisionLayerMask,
        BounceBeforePierce,
        PredictedAndAuthoritative
    }
}
