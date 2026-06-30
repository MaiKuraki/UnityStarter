using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath
{
    public readonly struct DeterministicProjectileVector3Payload
    {
        public readonly long XRaw;
        public readonly long YRaw;
        public readonly long ZRaw;

        public DeterministicProjectileVector3Payload(long xRaw, long yRaw, long zRaw)
        {
            XRaw = xRaw;
            YRaw = yRaw;
            ZRaw = zRaw;
        }

        public DeterministicProjectileVector3Payload(FPVector3 value)
        {
            XRaw = value.X.RawValue;
            YRaw = value.Y.RawValue;
            ZRaw = value.Z.RawValue;
        }

        public FPVector3 ToFPVector3()
        {
            return new FPVector3(
                FPInt64.FromRaw(XRaw),
                FPInt64.FromRaw(YRaw),
                FPInt64.FromRaw(ZRaw));
        }
    }
}
