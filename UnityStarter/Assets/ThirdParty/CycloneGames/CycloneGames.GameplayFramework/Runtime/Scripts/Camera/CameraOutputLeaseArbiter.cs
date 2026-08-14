using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Identifies one explicit terminal cleanup pass created by a camera-output lease arbiter.
    /// The value is allocation-free and may only be used with the arbiter that created it.
    /// </summary>
    public readonly struct CameraOutputTerminalReleasePass : IEquatable<CameraOutputTerminalReleasePass>
    {
        private readonly ICameraOutputLeaseArbiter owner;
        private readonly long sequence;

        public CameraOutputTerminalReleasePass(
            ICameraOutputLeaseArbiter owner,
            long sequence)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            this.sequence = sequence;
        }

        public bool IsValid => owner != null && sequence > 0;
        public long Sequence => sequence;

        public bool IsOwnedBy(ICameraOutputLeaseArbiter arbiter) =>
            ReferenceEquals(owner, arbiter);

        public bool Equals(CameraOutputTerminalReleasePass other) =>
            ReferenceEquals(owner, other.owner) && sequence == other.sequence;

        public override bool Equals(object obj) =>
            obj is CameraOutputTerminalReleasePass other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((owner?.GetHashCode() ?? 0) * 397) ^ sequence.GetHashCode();
            }
        }

        public static bool operator ==(
            CameraOutputTerminalReleasePass left,
            CameraOutputTerminalReleasePass right) => left.Equals(right);

        public static bool operator !=(
            CameraOutputTerminalReleasePass left,
            CameraOutputTerminalReleasePass right) => !left.Equals(right);
    }

    /// <summary>
    /// Arbitrates camera backend resources across every World that shares this instance.
    /// Applications with parallel Worlds and shared persistent camera resources must inject
    /// the same arbiter into those Worlds.
    /// </summary>
    public interface ICameraOutputLeaseArbiter
    {
        CameraOutputTerminalReleasePass BeginTerminalReleasePass(World world);

        bool TryAcquire(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputResourceSet resources,
            out CameraOutputLease lease,
            out string error);

        void Release(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputLease lease);

        /// <summary>
        /// Atomically claims this lease's backend-cleanup callback for the current World terminal
        /// pass. A successful caller must immediately perform exactly one cleanup callback. The
        /// following <see cref="TryReleaseAll"/> call consumes the claim without invoking that
        /// lease again; a retained lease becomes eligible on the next explicit terminal pass.
        /// </summary>
        bool TryBeginTerminalReleaseAttempt(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputLease lease,
            in CameraOutputTerminalReleasePass releasePass);

        bool TryReleaseAll(
            World world,
            in CameraOutputTerminalReleasePass releasePass);
    }

    /// <summary>
    /// Main-thread-affine, allocation-free-after-capacity ownership registry for Unity camera
    /// resources. It never stores global state; sharing is an explicit composition decision.
    /// </summary>
    public sealed class CameraOutputLeaseArbiter : ICameraOutputLeaseArbiter
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        /// <summary>
        /// Bounded deactivation retries before a repeatedly failing lease is forcibly detached
        /// so a World shutdown can always reach a terminal state. The backend resource is
        /// leaked, never double-released; unbounded retry would wedge every World sharing this
        /// arbiter in the Stopping state.
        /// </summary>
        private const int MaximumTerminalReleaseAttempts = 3;

        private struct Ownership
        {
            public World World;
            public CameraManager Owner;
            public ICameraOutput Output;
            public CameraOutputLease Lease;
            public string DisplayName;
            public long LastReleasePassId;
            public int TerminalReleaseFailureCount;
        }

        private readonly Dictionary<int, Ownership> owners;
        private readonly int ownerThreadId;
        private int nextLeaseId;
        private long nextReleasePassId;
        private bool isReleasingAll;

        public CameraOutputLeaseArbiter(int initialResourceCapacity = 8)
        {
            if (initialResourceCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialResourceCapacity));
            }

            owners = new Dictionary<int, Ownership>(initialResourceCapacity);
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public CameraOutputTerminalReleasePass BeginTerminalReleasePass(World world)
        {
            EnsureOwnerThread();
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (isReleasingAll)
            {
                throw new InvalidOperationException(
                    "A camera output terminal pass cannot begin during terminal lease release.");
            }

            return new CameraOutputTerminalReleasePass(this, AllocateReleasePassId());
        }

        public bool TryAcquire(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputResourceSet resources,
            out CameraOutputLease lease,
            out string error)
        {
            EnsureOwnerThread();
            lease = default;
            if (isReleasingAll)
            {
                error = "Camera output acquisition cannot run during terminal lease release.";
                return false;
            }
            if (!CanAcquire(world, owner) || !IsOutputAlive(output))
            {
                error = "A live World-bound CameraManager and camera output are required.";
                return false;
            }

            if (!resources.TryValidate(out error))
            {
                return false;
            }

            if (!CanAcquire(world, owner) || !IsOutputAlive(output))
            {
                error = "Camera output acquisition was interrupted by World or owner teardown.";
                return false;
            }

            int resourceCount = resources.Count;
            for (int index = 0; index < resourceCount; index++)
            {
                int resourceId = resources.GetResourceId(index);
                if (owners.TryGetValue(resourceId, out Ownership existing))
                {
                    error =
                        $"Camera output resource instance '{resourceId}' is already leased by '{existing.DisplayName}'.";
                    return false;
                }
            }

            int leaseId = AllocateLeaseId();
            lease = new CameraOutputLease(leaseId, in resources);
            var ownership = new Ownership
            {
                World = world,
                Owner = owner,
                Output = output,
                Lease = lease,
                DisplayName = output.GetType().Name,
            };

            int addedCount = 0;
            try
            {
                for (; addedCount < resourceCount; addedCount++)
                {
                    owners.Add(lease.GetResourceId(addedCount), ownership);
                }
            }
            catch
            {
                for (int index = 0; index < addedCount; index++)
                {
                    owners.Remove(lease.GetResourceId(index));
                }

                lease = default;
                throw;
            }

            error = null;
            return true;
        }

        public void Release(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputLease lease)
        {
            EnsureOwnerThread();
            if (isReleasingAll)
            {
                throw new InvalidOperationException(
                    "Individual camera output release cannot reenter terminal lease release.");
            }
            if (!lease.IsValid || lease.ResourceCount <= 0)
            {
                return;
            }

            int firstResourceId = lease.GetResourceId(0);
            if (!owners.TryGetValue(firstResourceId, out Ownership entry) ||
                entry.Lease != lease ||
                !ReferenceEquals(entry.World, world) ||
                !ReferenceEquals(entry.Owner, owner) ||
                !ReferenceEquals(entry.Output, output))
            {
                Log.Error(
                    $"Camera output release ignored: lease '{lease.LeaseId}' does not match the registered ownership.");
                return;
            }

            for (int index = 0; index < lease.ResourceCount; index++)
            {
                if (!owners.TryGetValue(lease.GetResourceId(index), out Ownership current) ||
                    current.Lease != lease ||
                    !ReferenceEquals(current.World, world) ||
                    !ReferenceEquals(current.Owner, owner) ||
                    !ReferenceEquals(current.Output, output))
                {
                    Log.Error(
                        $"Camera output release ignored: lease '{lease.LeaseId}' resources no longer match the registered ownership.");
                    return;
                }
            }

            RemoveEntries(in lease);
        }

        public bool TryBeginTerminalReleaseAttempt(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputLease lease,
            in CameraOutputTerminalReleasePass releasePass)
        {
            EnsureOwnerThread();
            if (isReleasingAll)
            {
                throw new InvalidOperationException(
                    "Camera output cleanup attempts cannot reenter terminal lease release.");
            }
            if (world == null ||
                world.LifecycleState != WorldLifecycleState.Stopping ||
                owner == null ||
                output == null ||
                !lease.IsValid ||
                lease.ResourceCount <= 0 ||
                !releasePass.IsValid ||
                !releasePass.IsOwnedBy(this))
            {
                return false;
            }

            int firstResourceId = lease.GetResourceId(0);
            if (!owners.TryGetValue(firstResourceId, out Ownership entry) ||
                entry.Lease != lease ||
                !ReferenceEquals(entry.World, world) ||
                !ReferenceEquals(entry.Owner, owner) ||
                !ReferenceEquals(entry.Output, output) ||
                entry.LastReleasePassId == releasePass.Sequence)
            {
                return false;
            }

            for (int index = 1; index < lease.ResourceCount; index++)
            {
                if (!owners.TryGetValue(lease.GetResourceId(index), out Ownership current) ||
                    current.Lease != lease ||
                    !ReferenceEquals(current.World, world) ||
                    !ReferenceEquals(current.Owner, owner) ||
                    !ReferenceEquals(current.Output, output) ||
                    current.LastReleasePassId == releasePass.Sequence)
                {
                    return false;
                }
            }

            for (int index = 0; index < lease.ResourceCount; index++)
            {
                int resourceId = lease.GetResourceId(index);
                Ownership current = owners[resourceId];
                current.LastReleasePassId = releasePass.Sequence;
                owners[resourceId] = current;
            }

            return true;
        }

        public bool TryReleaseAll(
            World world,
            in CameraOutputTerminalReleasePass releasePass)
        {
            EnsureOwnerThread();
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (isReleasingAll)
            {
                throw new InvalidOperationException(
                    "Camera output terminal lease release cannot be reentered.");
            }
            if (!releasePass.IsValid || !releasePass.IsOwnedBy(this))
            {
                throw new ArgumentException(
                    "A valid terminal release pass created by this arbiter is required.",
                    nameof(releasePass));
            }

            bool allReleased = true;
            OutOfMemoryException terminalOutOfMemory = null;
            isReleasingAll = true;
            try
            {
                long releasePassId = releasePass.Sequence;
                while (TryGetOwnership(world, releasePassId, out Ownership entry))
                {
                    MarkReleaseAttempt(in entry.Lease, releasePassId);
                    try
                    {
                        if (!ReferenceEquals(entry.Output, null))
                        {
                            entry.Output.Deactivate(entry.Owner);
                        }

                        RemoveEntries(in entry.Lease);
                    }
                    catch (Exception exception)
                    {
                        if (TryCaptureOutOfMemory(ref terminalOutOfMemory, exception))
                        {
                            allReleased = false;
                        }
                        else
                        {
                            int failureCount = IncrementTerminalReleaseFailure(in entry.Lease);
                            bool forceDetach = failureCount >= MaximumTerminalReleaseAttempts;
                            try
                            {
                                Log.Error(
                                    exception,
                                    forceDetach
                                        ? $"Camera output '{entry.DisplayName}' failed terminal deactivation {failureCount} times; detaching its lease to unblock World shutdown."
                                        : $"Camera output '{entry.DisplayName}' failed to deactivate during World shutdown.");
                            }
                            catch (Exception loggingException)
                            {
                                TryCaptureOutOfMemory(
                                    ref terminalOutOfMemory,
                                    loggingException);
                            }

                            if (forceDetach)
                            {
                                RemoveEntries(in entry.Lease);
                            }
                            else
                            {
                                allReleased = false;
                            }
                        }
                    }
                }

                if (terminalOutOfMemory != null)
                {
                    throw terminalOutOfMemory;
                }

                return allReleased && !HasOwnership(world);
            }
            finally
            {
                isReleasingAll = false;
            }
        }

        private bool HasOwnership(World world)
        {
            foreach (KeyValuePair<int, Ownership> pair in owners)
            {
                if (ReferenceEquals(pair.Value.World, world))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCaptureOutOfMemory(
            ref OutOfMemoryException terminalOutOfMemory,
            Exception exception)
        {
            OutOfMemoryException captured = FindOutOfMemory(exception);
            if (captured == null)
            {
                return false;
            }

            if (terminalOutOfMemory == null)
            {
                terminalOutOfMemory = captured;
            }

            return true;
        }

        private static OutOfMemoryException FindOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemoryException)
            {
                return outOfMemoryException;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int index = 0; index < aggregateException.InnerExceptions.Count; index++)
                {
                    OutOfMemoryException nested = FindOutOfMemory(
                        aggregateException.InnerExceptions[index]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private bool TryGetOwnership(
            World world,
            long releasePassId,
            out Ownership ownership)
        {
            ownership = default;
            int selectedLeaseId = 0;
            bool found = false;
            foreach (KeyValuePair<int, Ownership> pair in owners)
            {
                Ownership candidate = pair.Value;
                if (ReferenceEquals(candidate.World, world) &&
                    candidate.LastReleasePassId != releasePassId &&
                    (!found || candidate.Lease.LeaseId < selectedLeaseId))
                {
                    found = true;
                    selectedLeaseId = candidate.Lease.LeaseId;
                    ownership = candidate;
                }
            }

            return found;
        }

        private void MarkReleaseAttempt(
            in CameraOutputLease lease,
            long releasePassId)
        {
            for (int index = 0; index < lease.ResourceCount; index++)
            {
                int resourceId = lease.GetResourceId(index);
                if (owners.TryGetValue(resourceId, out Ownership entry) &&
                    entry.Lease == lease)
                {
                    entry.LastReleasePassId = releasePassId;
                    owners[resourceId] = entry;
                }
            }
        }

        private void RemoveEntries(in CameraOutputLease lease)
        {
            for (int index = 0; index < lease.ResourceCount; index++)
            {
                int resourceId = lease.GetResourceId(index);
                if (owners.TryGetValue(resourceId, out Ownership entry) && entry.Lease == lease)
                {
                    owners.Remove(resourceId);
                }
            }
        }

        /// <summary>
        /// Increments the terminal-release failure counter for every resource of one lease and
        /// returns the shared counter value. The counter persists across terminal passes so a
        /// repeatedly failing backend is eventually detached instead of retried forever.
        /// </summary>
        private int IncrementTerminalReleaseFailure(in CameraOutputLease lease)
        {
            int failureCount = 0;
            for (int index = 0; index < lease.ResourceCount; index++)
            {
                int resourceId = lease.GetResourceId(index);
                if (owners.TryGetValue(resourceId, out Ownership entry) && entry.Lease == lease)
                {
                    entry.TerminalReleaseFailureCount++;
                    failureCount = entry.TerminalReleaseFailureCount;
                    owners[resourceId] = entry;
                }
            }

            return failureCount;
        }

        private int AllocateLeaseId()
        {
            int candidate = nextLeaseId;
            do
            {
                candidate = candidate == int.MaxValue ? 1 : candidate + 1;
            }
            while (IsLeaseIdInUse(candidate));

            nextLeaseId = candidate;
            return candidate;
        }

        private long AllocateReleasePassId()
        {
            long candidate = nextReleasePassId;
            do
            {
                candidate = candidate == long.MaxValue ? 1 : candidate + 1;
            }
            while (IsReleasePassIdInUse(candidate));

            nextReleasePassId = candidate;
            return candidate;
        }

        private bool IsReleasePassIdInUse(long releasePassId)
        {
            foreach (KeyValuePair<int, Ownership> pair in owners)
            {
                if (pair.Value.LastReleasePassId == releasePassId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsLeaseIdInUse(int leaseId)
        {
            foreach (KeyValuePair<int, Ownership> pair in owners)
            {
                if (pair.Value.Lease.LeaseId == leaseId)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "Camera output leases must be mutated on their composition owner thread.");
            }
        }

        private static bool CanAcquire(World world, CameraManager owner)
        {
            return world != null &&
                   owner != null &&
                   ReferenceEquals(owner.World, world) &&
                   (world.LifecycleState == WorldLifecycleState.Initializing ||
                    world.LifecycleState == WorldLifecycleState.Playing);
        }

        private static bool IsOutputAlive(ICameraOutput output)
        {
            return output != null &&
                   (!(output is UnityEngine.Object unityObject) || unityObject != null);
        }

    }
}
