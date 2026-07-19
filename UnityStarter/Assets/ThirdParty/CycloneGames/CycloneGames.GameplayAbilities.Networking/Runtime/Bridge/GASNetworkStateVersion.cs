using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Shared owner-thread version domain for one authority ASC and one stream epoch.
    /// </summary>
    /// <remarks>
    /// Local ASC revisions and explicitly reported external identity changes advance the same
    /// monotonic wire version used by state snapshots, command results, and Gameplay Cues. The
    /// owner supplies dependency tracking; this clock creates no registry, thread, or lock.
    /// </remarks>
    public sealed class GASNetworkStateVersion
    {
        private readonly AbilitySystemComponent abilitySystem;
        private readonly int ownerThreadId;
        private uint streamEpoch;
        private ulong lastObservedLocalVersion;
        private ulong wireStateVersion;
        private bool exhausted;

        public GASNetworkStateVersion(
            AbilitySystemComponent abilitySystem,
            uint streamEpoch)
        {
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            this.streamEpoch = streamEpoch;
            lastObservedLocalVersion = abilitySystem.StateVersion;
            wireStateVersion = ToInitialWireVersion(lastObservedLocalVersion);
        }

        public int OwnerThreadId => ownerThreadId;
        public bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == ownerThreadId;

        public uint StreamEpoch
        {
            get
            {
                AssertOwnerThread();
                return streamEpoch;
            }
        }

        public ulong CurrentVersion
        {
            get
            {
                AssertOwnerThread();
                return wireStateVersion;
            }
        }

        public bool IsExhausted
        {
            get
            {
                AssertOwnerThread();
                return exhausted;
            }
        }

        internal AbilitySystemComponent AbilitySystem => abilitySystem;

        public bool TryObserveCurrentState(out ulong currentWireStateVersion)
        {
            AssertOwnerThread();
            return TryObserveLocalStateVersion(
                abilitySystem.StateVersion,
                out currentWireStateVersion);
        }

        internal bool TryObserveLocalStateVersion(
            ulong localStateVersion,
            out ulong currentWireStateVersion)
        {
            AssertOwnerThread();
            currentWireStateVersion = 0UL;
            if (exhausted)
                return false;
            if (localStateVersion < lastObservedLocalVersion)
            {
                exhausted = true;
                return false;
            }

            ulong advance = localStateVersion - lastObservedLocalVersion;
            if (advance > ulong.MaxValue - wireStateVersion)
            {
                exhausted = true;
                return false;
            }

            wireStateVersion += advance;
            lastObservedLocalVersion = localStateVersion;
            currentWireStateVersion = wireStateVersion;
            return true;
        }

        /// <summary>
        /// Advances the wire version after a known external entity identity change alters this
        /// ASC's network-visible state without mutating the ASC itself.
        /// </summary>
        public bool MarkExternalIdentityChanged()
        {
            AssertOwnerThread();
            if (!TryObserveCurrentState(out _) || wireStateVersion == ulong.MaxValue)
            {
                exhausted = true;
                return false;
            }

            wireStateVersion++;
            return true;
        }

        /// <summary>Starts a new version domain after every consumer has stopped the retired epoch.</summary>
        public void ResetEpoch(uint newStreamEpoch)
        {
            AssertOwnerThread();
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));

            ulong localStateVersion = abilitySystem.StateVersion;
            ulong initialWireStateVersion = ToInitialWireVersion(localStateVersion);
            streamEpoch = newStreamEpoch;
            lastObservedLocalVersion = localStateVersion;
            wireStateVersion = initialWireStateVersion;
            exhausted = false;
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS network state version is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }

        private static ulong ToInitialWireVersion(ulong localStateVersion)
        {
            if (localStateVersion == ulong.MaxValue)
                throw new InvalidOperationException(
                    "The GAS local state version is exhausted. Rotate the replication stream epoch before publishing more state.");
            return localStateVersion + 1UL;
        }
    }
}
