using System;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Runtime
{
    public static class CameraOutputLimits
    {
        public const int MaximumPreparedResourceCount = 4;
    }

    /// <summary>
    /// Opaque, generation-safe ownership token returned by a World after it atomically leases
    /// every resource prepared by one camera output.
    /// </summary>
    public readonly struct CameraOutputLease : IEquatable<CameraOutputLease>
    {
        private readonly int leaseId;
        private readonly byte resourceCount;
        private readonly int resourceId0;
        private readonly int resourceId1;
        private readonly int resourceId2;
        private readonly int resourceId3;

        public CameraOutputLease(
            int leaseId,
            int resourceCount,
            int resourceId0,
            int resourceId1,
            int resourceId2,
            int resourceId3)
        {
            if (leaseId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseId));
            }

            if ((uint)(resourceCount - 1) >= CameraOutputLimits.MaximumPreparedResourceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(resourceCount));
            }

            this.leaseId = leaseId;
            this.resourceCount = (byte)resourceCount;
            this.resourceId0 = resourceId0;
            this.resourceId1 = resourceId1;
            this.resourceId2 = resourceId2;
            this.resourceId3 = resourceId3;
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

        public static bool operator ==(CameraOutputLease left, CameraOutputLease right) => left.Equals(right);
        public static bool operator !=(CameraOutputLease left, CameraOutputLease right) => !left.Equals(right);
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
        int PreparedResourceCount { get; }

        /// <summary>
        /// Resolves the complete ownership-resource set. The count and resource identities must
        /// remain stable until <see cref="Deactivate"/> releases the prepared state.
        /// </summary>
        bool TryPrepare(out string error);

        UnityObject GetPreparedResource(int index);
        bool TryActivate(CameraManager owner, out string error);
        void ApplyPose(in CameraPose pose);
        void Deactivate(CameraManager owner);
    }
}
