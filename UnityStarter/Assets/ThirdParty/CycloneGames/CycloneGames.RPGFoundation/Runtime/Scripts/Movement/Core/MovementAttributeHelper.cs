namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Helper methods for working with movement attributes.
    /// </summary>
    public static class MovementAttributeHelper
    {
        public static float GetFinalValue(
            MovementAttribute attribute,
            float configValue,
            IMovementAuthority authority = null)
        {
            if (authority != null)
            {
                return authority.GetFinalValue(attribute, configValue);
            }
            return configValue;
        }

        public static MovementAttributeModifier GetModifier(
            MovementAttribute attribute,
            IMovementAuthority authority = null)
        {
            if (authority != null)
            {
                return authority.GetAttributeModifier(attribute);
            }
            return new MovementAttributeModifier(null, 1f);
        }
    }
}