using System;

namespace CycloneGames.Networking.Simulation
{
    public readonly struct NetworkActionValidationContext
    {
        public readonly INetConnection Sender;
        public readonly NetworkTickId ServerTick;
        public readonly NetworkTickId LastAcceptedClientTick;
        public readonly ushort LastAcceptedSequence;
        public readonly NetworkActionAuthorityMode AuthorityMode;
        public readonly int MaxAcceptedTickDrift;
        public readonly uint AllowedInputMask;
        public readonly uint RequiredFlags;
        public readonly uint BlockedFlags;

        public NetworkActionValidationContext(
            INetConnection sender,
            NetworkTickId serverTick,
            NetworkTickId lastAcceptedClientTick,
            ushort lastAcceptedSequence = 0,
            NetworkActionAuthorityMode authorityMode = NetworkActionAuthorityMode.ServerAuthoritative,
            int maxAcceptedTickDrift = 32,
            uint allowedInputMask = uint.MaxValue,
            uint requiredFlags = 0U,
            uint blockedFlags = 0U)
        {
            if (maxAcceptedTickDrift < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAcceptedTickDrift));
            }

            Sender = sender;
            ServerTick = serverTick;
            LastAcceptedClientTick = lastAcceptedClientTick;
            LastAcceptedSequence = lastAcceptedSequence;
            AuthorityMode = authorityMode;
            MaxAcceptedTickDrift = maxAcceptedTickDrift;
            AllowedInputMask = allowedInputMask;
            RequiredFlags = requiredFlags;
            BlockedFlags = blockedFlags;
        }

        public bool IsAuthenticated
        {
            get
            {
                return Sender == null || Sender.IsAuthenticated;
            }
        }

        public bool IsTickInAcceptedWindow(NetworkTickId tick)
        {
            if (!tick.IsValid)
            {
                return false;
            }

            if (!ServerTick.IsValid || MaxAcceptedTickDrift < 0)
            {
                return true;
            }

            long delta = tick.Value - ServerTick.Value;
            return delta >= -MaxAcceptedTickDrift && delta <= MaxAcceptedTickDrift;
        }

        public bool IsTickOrdered(NetworkTickId tick)
        {
            return !LastAcceptedClientTick.IsValid || tick >= LastAcceptedClientTick;
        }

        public bool IsDuplicate(NetworkTickId tick, ushort sequence)
        {
            return LastAcceptedClientTick.IsValid
                   && tick == LastAcceptedClientTick
                   && sequence == LastAcceptedSequence;
        }

        public bool IsSequenceOrdered(NetworkTickId tick, ushort sequence)
        {
            if (!LastAcceptedClientTick.IsValid || tick > LastAcceptedClientTick)
            {
                return true;
            }

            if (tick < LastAcceptedClientTick)
            {
                return false;
            }

            ushort delta = unchecked((ushort)(sequence - LastAcceptedSequence));
            return delta != 0 && delta < 32768;
        }

        public bool AllowsInputMask(uint inputMask)
        {
            return (inputMask & ~AllowedInputMask) == 0U;
        }

        public bool AllowsCustomFlags(uint customFlags)
        {
            return (customFlags & RequiredFlags) == RequiredFlags
                   && (customFlags & BlockedFlags) == 0U;
        }
    }
}
