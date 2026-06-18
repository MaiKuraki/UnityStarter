using System;
using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    /// <summary>
    /// Raw fixed-point vector payload for network messages, replay logs, save data, and backend protocols.
    /// This type preserves Q32.32 raw values and does not pass through floating-point conversion.
    /// </summary>
    public readonly struct InteractionDeterministicVector3Payload :
        IEquatable<InteractionDeterministicVector3Payload>,
        IInteractionDeterministicPositionProvider
    {
        public readonly long XRaw;
        public readonly long YRaw;
        public readonly long ZRaw;

        public InteractionDeterministicVector3Payload(long xRaw, long yRaw, long zRaw)
        {
            XRaw = xRaw;
            YRaw = yRaw;
            ZRaw = zRaw;
        }

        public InteractionDeterministicVector3Payload(FPVector3 position)
        {
            XRaw = position.X.RawValue;
            YRaw = position.Y.RawValue;
            ZRaw = position.Z.RawValue;
        }

        public FPVector3 ToFPVector3()
        {
            return new FPVector3(
                FPInt64.FromRaw(XRaw),
                FPInt64.FromRaw(YRaw),
                FPInt64.FromRaw(ZRaw));
        }

        public bool TryGetDeterministicInteractionPosition(out FPVector3 position)
        {
            position = ToFPVector3();
            return true;
        }

        public bool Equals(InteractionDeterministicVector3Payload other)
        {
            return XRaw == other.XRaw && YRaw == other.YRaw && ZRaw == other.ZRaw;
        }

        public override bool Equals(object obj)
        {
            return obj is InteractionDeterministicVector3Payload other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = XRaw.GetHashCode();
                hash = (hash * 397) ^ YRaw.GetHashCode();
                hash = (hash * 397) ^ ZRaw.GetHashCode();
                return hash;
            }
        }
    }
}
