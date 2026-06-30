using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath
{
    public static class DeterministicProjectileConversions
    {
        public static ProjectileVector3 ToProjectileVector3(FPVector3 value)
        {
            return new ProjectileVector3(value.X.ToFloat(), value.Y.ToFloat(), value.Z.ToFloat());
        }

        public static FPVector3 ToFPVector3(ProjectileVector3 value)
        {
            return new FPVector3(
                FPInt64.FromFloat(value.X),
                FPInt64.FromFloat(value.Y),
                FPInt64.FromFloat(value.Z));
        }

        public static ProjectileSnapshot ToSnapshot(in DeterministicProjectileState state)
        {
            return new ProjectileSnapshot(
                state.NetworkEntityId,
                state.OwnerEntityId,
                state.TargetEntityId,
                state.DefinitionId,
                state.LifecycleFlags,
                state.CurrentTick,
                state.PredictionKey,
                state.Age.ToFloat(),
                state.Radius.ToFloat(),
                ToProjectileVector3(state.Position),
                ToProjectileVector3(state.PreviousPosition),
                ToProjectileVector3(state.Velocity));
        }
    }
}
