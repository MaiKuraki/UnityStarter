using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    public static class InteractionDeterministicMathExtensions
    {
        /// <summary>
        /// Converts a floating-point interaction vector to fixed-point.
        /// Use only for migration, editor tooling, diagnostics, or non-authoritative bridges.
        /// Deterministic authority should read an original <see cref="FPVector3"/> source or raw payload.
        /// </summary>
        [System.Obsolete("Float-to-fixed conversion is not a deterministic authority source. Use FPVector3, InteractionDeterministicVector3Payload, or IInteractionDeterministicPositionProvider for authoritative decisions.", false)]
        public static FPVector3 ToFPVector3(this InteractionVector3 value)
        {
            return new FPVector3(
                FPInt64.FromFloat(value.X),
                FPInt64.FromFloat(value.Y),
                FPInt64.FromFloat(value.Z));
        }

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

        /// <summary>
        /// Converts a floating-point target snapshot to a deterministic snapshot.
        /// Use only for migration, editor tooling, diagnostics, or non-authoritative bridges.
        /// Deterministic authority should register snapshots built directly from fixed-point simulation state.
        /// </summary>
        [System.Obsolete("Float-to-fixed snapshot conversion is not a deterministic authority source. Build InteractionDeterministicTargetSnapshot from FPVector3 simulation state instead.", false)]
        public static InteractionDeterministicTargetSnapshot ToDeterministicTargetSnapshot(this InteractionTargetSnapshot snapshot)
        {
            return new InteractionDeterministicTargetSnapshot(
                snapshot.WorldId,
                snapshot.TargetStableId,
                new FPVector3(
                    FPInt64.FromFloat(snapshot.Position.X),
                    FPInt64.FromFloat(snapshot.Position.Y),
                    FPInt64.FromFloat(snapshot.Position.Z)),
                FPInt64.FromFloat(snapshot.InteractionRange),
                snapshot.IsAvailable,
                snapshot.AllowDefaultAction,
                snapshot.EnabledActionIds,
                snapshot.Version);
        }
    }
}
