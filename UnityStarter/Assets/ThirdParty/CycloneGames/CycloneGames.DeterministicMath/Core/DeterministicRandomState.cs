using System;

namespace CycloneGames.DeterministicMath
{
    public readonly struct DeterministicRandomState : IEquatable<DeterministicRandomState>
    {
        public readonly ulong S0;
        public readonly ulong S1;
        public readonly ulong S2;
        public readonly ulong S3;

        public bool IsValid => (S0 | S1 | S2 | S3) != 0;

        public DeterministicRandomState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool Equals(DeterministicRandomState other) =>
            S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;

        public override bool Equals(object obj) => obj is DeterministicRandomState state && Equals(state);
        public override int GetHashCode() => S0.GetHashCode() ^ S1.GetHashCode() ^ S2.GetHashCode() ^ S3.GetHashCode();
    }
}
