namespace CycloneGames.RPGFoundation.Movement.Core
{
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
        RotationSpeed,
        ClimbSpeed,
        SwimSpeed,
        FlySpeed
    }

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