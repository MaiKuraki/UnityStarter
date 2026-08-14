using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Arbitrates camera backend resources across every World that shares this instance.
    /// Applications with parallel Worlds and shared persistent camera resources must inject
    /// the same arbiter into those Worlds.
    /// </summary>
    public interface ICameraOutputLeaseArbiter
    {
        bool TryAcquire(
            World world,
            CameraManager owner,
            ICameraOutput output,
            out CameraOutputLease lease,
            out string error);

        void Release(
            World world,
            CameraManager owner,
            ICameraOutput output,
            in CameraOutputLease lease);

        void ReleaseAll(World world);
    }

    /// <summary>
    /// Main-thread-affine, allocation-free-after-capacity ownership registry for Unity camera
    /// resources. It never stores global state; sharing is an explicit composition decision.
    /// </summary>
    public sealed class CameraOutputLeaseArbiter : ICameraOutputLeaseArbiter
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        private struct Ownership
        {
            public World World;
            public CameraManager Owner;
            public ICameraOutput Output;
            public CameraOutputLease Lease;
            public string DisplayName;
        }

        private readonly Dictionary<int, Ownership> owners;
        private readonly int ownerThreadId;
        private int nextLeaseId;

        public CameraOutputLeaseArbiter(int initialResourceCapacity = 8)
        {
            if (initialResourceCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialResourceCapacity));
            }

            owners = new Dictionary<int, Ownership>(initialResourceCapacity);
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool TryAcquire(
            World world,
            CameraManager owner,
            ICameraOutput output,
            out CameraOutputLease lease,
            out string error)
        {
            EnsureOwnerThread();
            lease = default;
            if (!CanAcquire(world, owner) || !IsOutputAlive(output))
            {
                error = "A live World-bound CameraManager and camera output are required.";
                return false;
            }

            int resourceCount;
            try
            {
                resourceCount = output.PreparedResourceCount;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                error = $"Camera output resource count could not be read: {exception.Message}";
                return false;
            }

            if (resourceCount <= 0 ||
                resourceCount > CameraOutputLimits.MaximumPreparedResourceCount)
            {
                error =
                    $"Camera output must prepare between 1 and {CameraOutputLimits.MaximumPreparedResourceCount} ownership resources.";
                return false;
            }

            int resourceId0 = 0;
            int resourceId1 = 0;
            int resourceId2 = 0;
            int resourceId3 = 0;
            for (int index = 0; index < resourceCount; index++)
            {
                UnityEngine.Object resource;
                try
                {
                    resource = output.GetPreparedResource(index);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    error = $"Camera output resource {index} could not be read: {exception.Message}";
                    return false;
                }

                if (resource == null)
                {
                    error = $"Camera output resource {index} is missing or destroyed.";
                    return false;
                }

                int resourceId = resource.GetInstanceID();
                if ((index > 0 && resourceId == resourceId0) ||
                    (index > 1 && resourceId == resourceId1) ||
                    (index > 2 && resourceId == resourceId2))
                {
                    error = $"Camera output resource {index} duplicates an earlier resource.";
                    return false;
                }

                switch (index)
                {
                    case 0: resourceId0 = resourceId; break;
                    case 1: resourceId1 = resourceId; break;
                    case 2: resourceId2 = resourceId; break;
                    default: resourceId3 = resourceId; break;
                }
            }

            string displayName = GetSafeDisplayName(output);
            if (!CanAcquire(world, owner) || !IsOutputAlive(output))
            {
                error = "Camera output acquisition was interrupted by World or owner teardown.";
                return false;
            }

            for (int index = 0; index < resourceCount; index++)
            {
                int resourceId = GetResourceId(
                    index,
                    resourceId0,
                    resourceId1,
                    resourceId2,
                    resourceId3);
                if (owners.TryGetValue(resourceId, out Ownership existing))
                {
                    error =
                        $"Camera output resource instance '{resourceId}' is already leased by '{existing.DisplayName}'.";
                    return false;
                }
            }

            int leaseId = AllocateLeaseId();
            lease = new CameraOutputLease(
                leaseId,
                resourceCount,
                resourceId0,
                resourceId1,
                resourceId2,
                resourceId3);
            var ownership = new Ownership
            {
                World = world,
                Owner = owner,
                Output = output,
                Lease = lease,
                DisplayName = displayName,
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
                    return;
                }
            }

            RemoveEntries(in lease);
        }

        public void ReleaseAll(World world)
        {
            EnsureOwnerThread();
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            while (TryGetOwnership(world, out Ownership entry))
            {
                RemoveEntries(in entry.Lease);
                try
                {
                    if (!ReferenceEquals(entry.Output, null))
                    {
                        entry.Output.Deactivate(entry.Owner);
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        exception,
                        $"Camera output '{entry.DisplayName}' failed to deactivate during World shutdown.");
                }
            }
        }

        private bool TryGetOwnership(World world, out Ownership ownership)
        {
            foreach (KeyValuePair<int, Ownership> pair in owners)
            {
                if (ReferenceEquals(pair.Value.World, world))
                {
                    ownership = pair.Value;
                    return true;
                }
            }

            ownership = default;
            return false;
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

        private static string GetSafeDisplayName(ICameraOutput output)
        {
            try
            {
                return output.DisplayName ?? "Unknown";
            }
            catch
            {
                return "Destroyed output";
            }
        }

        private static int GetResourceId(
            int index,
            int resourceId0,
            int resourceId1,
            int resourceId2,
            int resourceId3)
        {
            switch (index)
            {
                case 0: return resourceId0;
                case 1: return resourceId1;
                case 2: return resourceId2;
                default: return resourceId3;
            }
        }
    }
}
