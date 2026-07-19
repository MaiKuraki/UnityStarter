using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    public static class InteractionDeterministicMathExtensions
    {
        /// <summary>
        /// Converts fixed-point state to a floating-point interaction vector for presentation or non-authoritative reporting.
        /// </summary>
        public static InteractionVector3 ToInteractionVector3(this FPVector3 value)
        {
            return new InteractionVector3(
                value.X.ToFloat(),
                value.Y.ToFloat(),
                value.Z.ToFloat());
        }

        public static InteractionDeterministicVector3Payload ToDeterministicPayload(this FPVector3 value)
        {
            return new InteractionDeterministicVector3Payload(value);
        }

        public static FPVector3 ToFPVector3(this InteractionDeterministicVector3Payload value)
        {
            return value.ToFPVector3();
        }
    }
}
