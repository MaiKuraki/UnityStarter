using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileSpaceProfile
    {
        public readonly ProjectileSimulationPlane Plane;
        public readonly float LockedAxisValue;
        public readonly ProjectileVector3 Gravity;

        public ProjectileSpaceProfile(
            ProjectileSimulationPlane plane,
            float lockedAxisValue,
            ProjectileVector3 gravity)
        {
            Plane = plane;
            LockedAxisValue = lockedAxisValue;
            Gravity = gravity;
        }

        public static ProjectileSpaceProfile Full3D(float gravityY = -9.81f)
        {
            return new ProjectileSpaceProfile(
                ProjectileSimulationPlane.Full3D,
                0f,
                new ProjectileVector3(0f, gravityY, 0f));
        }

        public static ProjectileSpaceProfile SideScroller2D(float lockedZ = 0f, float gravityY = -9.81f)
        {
            return new ProjectileSpaceProfile(
                ProjectileSimulationPlane.XY,
                lockedZ,
                new ProjectileVector3(0f, gravityY, 0f));
        }

        public static ProjectileSpaceProfile TopDown2D(float lockedY = 0f)
        {
            return new ProjectileSpaceProfile(
                ProjectileSimulationPlane.XZ,
                lockedY,
                ProjectileVector3.Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProjectileVector3 ProjectPosition(ProjectileVector3 value)
        {
            switch (Plane)
            {
                case ProjectileSimulationPlane.XY:
                    return new ProjectileVector3(value.X, value.Y, LockedAxisValue);
                case ProjectileSimulationPlane.XZ:
                    return new ProjectileVector3(value.X, LockedAxisValue, value.Z);
                default:
                    return value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProjectileVector3 ProjectDirection(ProjectileVector3 value)
        {
            switch (Plane)
            {
                case ProjectileSimulationPlane.XY:
                    return new ProjectileVector3(value.X, value.Y, 0f).NormalizedOrZero();
                case ProjectileSimulationPlane.XZ:
                    return new ProjectileVector3(value.X, 0f, value.Z).NormalizedOrZero();
                default:
                    return value.NormalizedOrZero();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProjectileVector3 ProjectVector(ProjectileVector3 value)
        {
            switch (Plane)
            {
                case ProjectileSimulationPlane.XY:
                    return new ProjectileVector3(value.X, value.Y, 0f);
                case ProjectileSimulationPlane.XZ:
                    return new ProjectileVector3(value.X, 0f, value.Z);
                default:
                    return value;
            }
        }
    }
}
