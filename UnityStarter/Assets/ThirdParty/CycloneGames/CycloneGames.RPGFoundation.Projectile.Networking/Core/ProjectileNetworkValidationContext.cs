using System;
using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public readonly struct ProjectileNetworkValidationContext
    {
        public readonly INetConnection Sender;
        public readonly NetworkTickId ServerTick;
        public readonly NetworkTickId LastAcceptedServerTick;
        public readonly ushort LastAcceptedSequence;
        public readonly NetworkActionAuthorityMode AuthorityMode;
        public readonly int MaxAcceptedTickDrift;
        public readonly uint AllowedLifecycleFlags;
        public readonly uint RequiredLifecycleFlags;
        public readonly uint BlockedLifecycleFlags;
        public readonly float MaxInitialVelocitySqr;
        public readonly float MaxSnapshotVelocitySqr;
        public readonly float MaxRadius;
        public readonly float MaxAge;

        public ProjectileNetworkValidationContext(
            INetConnection sender,
            NetworkTickId serverTick,
            NetworkTickId lastAcceptedServerTick,
            ushort lastAcceptedSequence = 0,
            NetworkActionAuthorityMode authorityMode = NetworkActionAuthorityMode.ClientPredictedServerAuthoritative,
            int maxAcceptedTickDrift = 64,
            uint allowedLifecycleFlags = uint.MaxValue,
            uint requiredLifecycleFlags = 0U,
            uint blockedLifecycleFlags = 0U,
            float maxInitialVelocity = 512f,
            float maxSnapshotVelocity = 512f,
            float maxRadius = 16f,
            float maxAge = 3600f)
        {
            if (maxAcceptedTickDrift < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAcceptedTickDrift));
            }

            if (!IsFinite(maxInitialVelocity) || maxInitialVelocity < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInitialVelocity));
            }

            if (!IsFinite(maxSnapshotVelocity) || maxSnapshotVelocity < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSnapshotVelocity));
            }

            if (!IsFinite(maxRadius) || maxRadius < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadius));
            }

            if (!IsFinite(maxAge) || maxAge < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAge));
            }

            Sender = sender;
            ServerTick = serverTick;
            LastAcceptedServerTick = lastAcceptedServerTick;
            LastAcceptedSequence = lastAcceptedSequence;
            AuthorityMode = authorityMode;
            MaxAcceptedTickDrift = maxAcceptedTickDrift;
            AllowedLifecycleFlags = allowedLifecycleFlags;
            RequiredLifecycleFlags = requiredLifecycleFlags;
            BlockedLifecycleFlags = blockedLifecycleFlags;
            MaxInitialVelocitySqr = maxInitialVelocity * maxInitialVelocity;
            MaxSnapshotVelocitySqr = maxSnapshotVelocity * maxSnapshotVelocity;
            MaxRadius = maxRadius;
            MaxAge = maxAge;
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

        public bool IsDuplicate(NetworkTickId tick, ushort sequence)
        {
            return LastAcceptedServerTick.IsValid
                   && tick == LastAcceptedServerTick
                   && sequence == LastAcceptedSequence;
        }

        public bool IsSequenceOrdered(NetworkTickId tick, ushort sequence)
        {
            if (!LastAcceptedServerTick.IsValid || tick > LastAcceptedServerTick)
            {
                return true;
            }

            if (tick < LastAcceptedServerTick)
            {
                return false;
            }

            ushort delta = unchecked((ushort)(sequence - LastAcceptedSequence));
            return delta != 0 && delta < 32768;
        }

        public bool AllowsLifecycleFlags(uint lifecycleFlags)
        {
            return (lifecycleFlags & ~AllowedLifecycleFlags) == 0U
                   && (lifecycleFlags & RequiredLifecycleFlags) == RequiredLifecycleFlags
                   && (lifecycleFlags & BlockedLifecycleFlags) == 0U;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
