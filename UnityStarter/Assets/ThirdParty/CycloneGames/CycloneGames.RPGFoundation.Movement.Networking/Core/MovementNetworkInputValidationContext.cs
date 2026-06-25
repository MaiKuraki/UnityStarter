using System;

using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public readonly struct MovementNetworkInputValidationContext
    {
        public readonly INetConnection Sender;
        public readonly NetworkTickId ServerTick;
        public readonly NetworkTickId LastAcceptedClientTick;
        public readonly ushort LastAcceptedInputSequence;
        public readonly NetworkActionAuthorityMode AuthorityMode;
        public readonly int MaxAcceptedTickDrift;
        public readonly uint AllowedButtonMask;
        public readonly uint AllowedCustomFlags;
        public readonly uint RequiredCustomFlags;
        public readonly uint BlockedCustomFlags;
        public readonly float MaxMoveAxesMagnitudeSqr;
        public readonly float MinAimDirectionMagnitudeSqr;
        public readonly float MaxAimDirectionMagnitudeSqr;
        public readonly bool RequireNormalizedAimDirection;

        public MovementNetworkInputValidationContext(
            INetConnection sender,
            NetworkTickId serverTick,
            NetworkTickId lastAcceptedClientTick,
            ushort lastAcceptedInputSequence = 0,
            NetworkActionAuthorityMode authorityMode = NetworkActionAuthorityMode.ClientPredictedServerAuthoritative,
            int maxAcceptedTickDrift = 32,
            uint allowedButtonMask = uint.MaxValue,
            uint allowedCustomFlags = uint.MaxValue,
            uint requiredCustomFlags = 0U,
            uint blockedCustomFlags = 0U,
            float maxMoveAxesMagnitude = 1.25f,
            float minAimDirectionMagnitude = 0f,
            float maxAimDirectionMagnitude = 1.25f,
            bool requireNormalizedAimDirection = false)
        {
            if (maxAcceptedTickDrift < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAcceptedTickDrift));
            }

            if (!float.IsFinite(maxMoveAxesMagnitude) || maxMoveAxesMagnitude < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxMoveAxesMagnitude));
            }

            if (!float.IsFinite(minAimDirectionMagnitude) || minAimDirectionMagnitude < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(minAimDirectionMagnitude));
            }

            if (!float.IsFinite(maxAimDirectionMagnitude) || maxAimDirectionMagnitude < minAimDirectionMagnitude)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAimDirectionMagnitude));
            }

            Sender = sender;
            ServerTick = serverTick;
            LastAcceptedClientTick = lastAcceptedClientTick;
            LastAcceptedInputSequence = lastAcceptedInputSequence;
            AuthorityMode = authorityMode;
            MaxAcceptedTickDrift = maxAcceptedTickDrift;
            AllowedButtonMask = allowedButtonMask;
            AllowedCustomFlags = allowedCustomFlags;
            RequiredCustomFlags = requiredCustomFlags;
            BlockedCustomFlags = blockedCustomFlags;
            MaxMoveAxesMagnitudeSqr = maxMoveAxesMagnitude * maxMoveAxesMagnitude;
            MinAimDirectionMagnitudeSqr = minAimDirectionMagnitude * minAimDirectionMagnitude;
            MaxAimDirectionMagnitudeSqr = maxAimDirectionMagnitude * maxAimDirectionMagnitude;
            RequireNormalizedAimDirection = requireNormalizedAimDirection;
        }

        public NetworkActionValidationContext ToActionValidationContext()
        {
            return new NetworkActionValidationContext(
                Sender,
                ServerTick,
                LastAcceptedClientTick,
                LastAcceptedInputSequence,
                AuthorityMode,
                MaxAcceptedTickDrift,
                AllowedButtonMask,
                RequiredCustomFlags,
                BlockedCustomFlags);
        }

        public bool AllowsButtonMask(uint buttonMask)
        {
            return (buttonMask & ~AllowedButtonMask) == 0U;
        }

        public bool AllowsCustomFlags(uint customFlags)
        {
            return (customFlags & ~AllowedCustomFlags) == 0U
                   && (customFlags & RequiredCustomFlags) == RequiredCustomFlags
                   && (customFlags & BlockedCustomFlags) == 0U;
        }
    }
}
