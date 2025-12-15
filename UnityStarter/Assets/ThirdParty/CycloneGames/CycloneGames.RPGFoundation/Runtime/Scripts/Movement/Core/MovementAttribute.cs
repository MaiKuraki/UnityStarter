namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Identifies movement attributes that can be modified.
    /// </summary>
    public enum MovementAttribute
    {
        WalkSpeed,
        RunSpeed,
        SprintSpeed,
        CrouchSpeed,
        JumpForce,
        MaxJumpCount,
        Gravity,
        AirControlMultiplier,
        RotationSpeed
    }

    /// <summary>
    /// Modifier data for a movement attribute.
    /// </summary>
    public struct MovementAttributeModifier
    {
        public float? BaseValueOverride;
        public float Multiplier;

        public MovementAttributeModifier(float? baseOverride, float multiplier)
        {
            BaseValueOverride = baseOverride;
            Multiplier = multiplier;
        }

        public float Apply(float baseValue)
        {
            float finalBase = BaseValueOverride ?? baseValue;
            return finalBase * Multiplier;
        }
    }
}