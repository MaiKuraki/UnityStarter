using System;
using UnityObject = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Runtime
{
    public static class CameraOutputLimits
    {
        public const int MaximumResourceCount = 4;
    }

    /// <summary>
    /// Immutable, allocation-free snapshot of the Unity objects that one camera output needs
    /// to own exclusively. Discovery creates this value before a lease is acquired; activation
    /// must use this exact snapshot and must not resolve a different backend resource.
    /// </summary>
    public readonly struct CameraOutputResourceSet : IEquatable<CameraOutputResourceSet>
    {
        private readonly byte count;
        private readonly UnityObject resource0;
        private readonly UnityObject resource1;
        private readonly UnityObject resource2;
        private readonly UnityObject resource3;
        private readonly int resourceId0;
        private readonly int resourceId1;
        private readonly int resourceId2;
        private readonly int resourceId3;

        public CameraOutputResourceSet(UnityObject resource0)
            : this(1, resource0, null, null, null)
        {
        }

        public CameraOutputResourceSet(UnityObject resource0, UnityObject resource1)
            : this(2, resource0, resource1, null, null)
        {
        }

        public CameraOutputResourceSet(
            UnityObject resource0,
            UnityObject resource1,
            UnityObject resource2)
            : this(3, resource0, resource1, resource2, null)
        {
        }

        public CameraOutputResourceSet(
            UnityObject resource0,
            UnityObject resource1,
            UnityObject resource2,
            UnityObject resource3)
            : this(4, resource0, resource1, resource2, resource3)
        {
        }

        private CameraOutputResourceSet(
            int count,
            UnityObject resource0,
            UnityObject resource1,
            UnityObject resource2,
            UnityObject resource3)
        {
            ValidateResource(resource0, nameof(resource0));
            if (count > 1)
            {
                ValidateResource(resource1, nameof(resource1));
            }
            if (count > 2)
            {
                ValidateResource(resource2, nameof(resource2));
            }
            if (count > 3)
            {
                ValidateResource(resource3, nameof(resource3));
            }

            int id0 = resource0.GetInstanceID();
            int id1 = count > 1 ? resource1.GetInstanceID() : 0;
            int id2 = count > 2 ? resource2.GetInstanceID() : 0;
            int id3 = count > 3 ? resource3.GetInstanceID() : 0;
            if ((count > 1 && id1 == id0) ||
                (count > 2 && (id2 == id0 || id2 == id1)) ||
                (count > 3 && (id3 == id0 || id3 == id1 || id3 == id2)))
            {
                throw new ArgumentException(
                    "Camera output ownership resources must be distinct.");
            }

            this.count = (byte)count;
            this.resource0 = resource0;
            this.resource1 = resource1;
            this.resource2 = resource2;
            this.resource3 = resource3;
            resourceId0 = id0;
            resourceId1 = id1;
            resourceId2 = id2;
            resourceId3 = id3;
        }

        public int Count => count;
        public bool IsValid => count > 0;

        /// <summary>
        /// Creates a validated snapshot from a fixed-capacity resource tuple without allocating
        /// an array or collection. Values beyond <paramref name="resourceCount"/> are ignored.
        /// </summary>
        public static bool TryCreate(
            int resourceCount,
            UnityObject resource0,
            UnityObject resource1,
            UnityObject resource2,
            UnityObject resource3,
            out CameraOutputResourceSet resources,
            out string error)
        {
            resources = default;
            if (resourceCount <= 0 ||
                resourceCount > CameraOutputLimits.MaximumResourceCount)
            {
                error = "Camera output resource snapshots must contain between one and four resources.";
                return false;
            }

            if (resource0 == null ||
                (resourceCount > 1 && resource1 == null) ||
                (resourceCount > 2 && resource2 == null) ||
                (resourceCount > 3 && resource3 == null))
            {
                error = "Camera output ownership resources must be live Unity objects.";
                return false;
            }

            int id0 = resource0.GetInstanceID();
            int id1 = resourceCount > 1 ? resource1.GetInstanceID() : 0;
            int id2 = resourceCount > 2 ? resource2.GetInstanceID() : 0;
            int id3 = resourceCount > 3 ? resource3.GetInstanceID() : 0;
            if ((resourceCount > 1 && id1 == id0) ||
                (resourceCount > 2 && (id2 == id0 || id2 == id1)) ||
                (resourceCount > 3 && (id3 == id0 || id3 == id1 || id3 == id2)))
            {
                error = "Camera output ownership resources must be distinct.";
                return false;
            }

            resources = new CameraOutputResourceSet(
                resourceCount,
                resource0,
                resource1,
                resource2,
                resource3);
            error = null;
            return true;
        }

        public UnityObject GetResource(int index)
        {
            if ((uint)index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            switch (index)
            {
                case 0: return resource0;
                case 1: return resource1;
                case 2: return resource2;
                default: return resource3;
            }
        }

        public int GetResourceId(int index)
        {
            if ((uint)index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            switch (index)
            {
                case 0: return resourceId0;
                case 1: return resourceId1;
                case 2: return resourceId2;
                default: return resourceId3;
            }
        }

        public bool TryValidate(out string error)
        {
            if (count == 0 || count > CameraOutputLimits.MaximumResourceCount)
            {
                error = "Camera output resource snapshots must contain between one and four resources.";
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                UnityObject resource = GetResource(index);
                if (resource == null || resource.GetInstanceID() != GetResourceId(index))
                {
                    error = $"Camera output resource {index} is missing, destroyed, or no longer matches its snapshot.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public bool Equals(CameraOutputResourceSet other) =>
            count == other.count &&
            resourceId0 == other.resourceId0 &&
            resourceId1 == other.resourceId1 &&
            resourceId2 == other.resourceId2 &&
            resourceId3 == other.resourceId3 &&
            ReferenceEquals(resource0, other.resource0) &&
            ReferenceEquals(resource1, other.resource1) &&
            ReferenceEquals(resource2, other.resource2) &&
            ReferenceEquals(resource3, other.resource3);

        public override bool Equals(object obj) =>
            obj is CameraOutputResourceSet other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = count;
                hash = (hash * 397) ^ resourceId0;
                hash = (hash * 397) ^ resourceId1;
                hash = (hash * 397) ^ resourceId2;
                return (hash * 397) ^ resourceId3;
            }
        }

        public static bool operator ==(
            CameraOutputResourceSet left,
            CameraOutputResourceSet right) => left.Equals(right);

        public static bool operator !=(
            CameraOutputResourceSet left,
            CameraOutputResourceSet right) => !left.Equals(right);

        private static void ValidateResource(UnityObject resource, string parameterName)
        {
            if (resource == null)
            {
                throw new ArgumentNullException(
                    parameterName,
                    "Camera output ownership resources must be live Unity objects.");
            }
        }
    }

    /// <summary>
    /// Opaque, generation-safe ownership token returned by a World after it atomically leases
    /// every resource in one immutable camera-output snapshot.
    /// </summary>
    public readonly struct CameraOutputLease : IEquatable<CameraOutputLease>
    {
        private readonly int leaseId;
        private readonly byte resourceCount;
        private readonly int resourceId0;
        private readonly int resourceId1;
        private readonly int resourceId2;
        private readonly int resourceId3;

        internal CameraOutputLease(int leaseId, in CameraOutputResourceSet resources)
        {
            if (leaseId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseId));
            }
            if (!resources.IsValid)
            {
                throw new ArgumentException(
                    "A valid camera output resource snapshot is required.",
                    nameof(resources));
            }

            this.leaseId = leaseId;
            resourceCount = (byte)resources.Count;
            resourceId0 = resources.GetResourceId(0);
            resourceId1 = resources.Count > 1 ? resources.GetResourceId(1) : 0;
            resourceId2 = resources.Count > 2 ? resources.GetResourceId(2) : 0;
            resourceId3 = resources.Count > 3 ? resources.GetResourceId(3) : 0;
        }

        public bool IsValid => leaseId != 0;
        public int LeaseId => leaseId;
        public int ResourceCount => resourceCount;

        public int GetResourceId(int index)
        {
            if ((uint)index >= resourceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            switch (index)
            {
                case 0: return resourceId0;
                case 1: return resourceId1;
                case 2: return resourceId2;
                default: return resourceId3;
            }
        }

        public bool Equals(CameraOutputLease other) =>
            leaseId == other.leaseId &&
            resourceCount == other.resourceCount &&
            resourceId0 == other.resourceId0 &&
            resourceId1 == other.resourceId1 &&
            resourceId2 == other.resourceId2 &&
            resourceId3 == other.resourceId3;

        public override bool Equals(object obj) =>
            obj is CameraOutputLease other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = leaseId;
                hash = (hash * 397) ^ resourceCount;
                hash = (hash * 397) ^ resourceId0;
                hash = (hash * 397) ^ resourceId1;
                hash = (hash * 397) ^ resourceId2;
                return (hash * 397) ^ resourceId3;
            }
        }

        public static bool operator ==(CameraOutputLease left, CameraOutputLease right) =>
            left.Equals(right);

        public static bool operator !=(CameraOutputLease left, CameraOutputLease right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Applies the final pose produced by CameraManager to one concrete camera backend.
    /// Implementations are activated and released by the owning World on its owner thread.
    /// </summary>
    public interface ICameraOutput
    {
        string DisplayName { get; }
        bool IsActive { get; }
        CameraManager Owner { get; }
        UnityObject OutputObject { get; }

        /// <summary>
        /// Discovers all exclusive backend resources as an immutable value snapshot. This method
        /// must not mutate backend state, lifecycle state, or externally visible output state.
        /// </summary>
        bool TryGetResourceSet(out CameraOutputResourceSet resources, out string error);

        /// <summary>
        /// Activates the backend using the exact resource snapshot already leased by the World.
        /// </summary>
        bool TryActivate(
            CameraManager owner,
            in CameraOutputResourceSet resources,
            out string error);

        void ApplyPose(in CameraPose pose);
        void Deactivate(CameraManager owner);
    }
}
