using System;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Root-motion axes a layer is allowed to contribute. <see cref="None"/> means "do not contribute".</summary>
    [Flags]
    public enum RootMotionAxis : byte
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        Rotation = 8,

        /// <summary>X and Z, the common case for ground locomotion.</summary>
        Horizontal = X | Z,

        /// <summary>Everything, equivalent to the engine default.</summary>
        All = X | Y | Z | Rotation
    }

    /// <summary>
    /// Immutable layer description consumed by <see cref="AnimationRuntimeGraph"/>. The graph never reads a
    /// <see cref="AnimationLayerProfile"/> directly, which keeps runtime tests free of ScriptableObject assets.
    /// </summary>
    public readonly struct AnimationLayerDescriptor
    {
        public readonly AvatarMask Mask;
        public readonly bool Additive;
        public readonly float Weight;
        public readonly byte Capacity;
        public readonly bool ApplyFootIK;
        public readonly bool ApplyPlayableIK;
        public readonly RootMotionAxis RootMotion;
        public readonly float RootMotionScale;

        public AnimationLayerDescriptor(
            AvatarMask mask,
            bool additive,
            float weight,
            int capacity,
            bool applyFootIK = false,
            bool applyPlayableIK = false,
            RootMotionAxis rootMotion = RootMotionAxis.Horizontal,
            float rootMotionScale = 1f)
        {
            Mask = mask;
            Additive = additive;
            Weight = weight < 0f ? 0f : (weight > 1f ? 1f : weight);
            Capacity = capacity < Core.AnimationRuntimeLimits.MinLayerCapacity
                ? (byte)Core.AnimationRuntimeLimits.MinLayerCapacity
                : (capacity > Core.AnimationRuntimeLimits.MaxLayerCapacity
                    ? (byte)Core.AnimationRuntimeLimits.MaxLayerCapacity
                    : (byte)capacity);
            ApplyFootIK = applyFootIK;
            ApplyPlayableIK = applyPlayableIK;
            RootMotion = rootMotion;
            RootMotionScale = rootMotionScale <= 0f ? 1f : rootMotionScale;
        }

        /// <summary>Single full-body layer with four voice slots. Used when no profile is assigned.</summary>
        public static AnimationLayerDescriptor Default
        {
            get
            {
                return new AnimationLayerDescriptor(
                    null,
                    false,
                    1f,
                    Core.AnimationRuntimeLimits.DefaultLayerCapacity,
                    false,
                    false,
                    RootMotionAxis.Horizontal,
                    1f);
            }
        }
    }
}
