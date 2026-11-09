using System;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Root-motion axes the runtime is allowed to forward. <see cref="None"/> means "do not contribute".</summary>
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
    ///
    /// Root motion is not a layer property: a Playable output produces a single root delta, so a per-layer
    /// axis filter cannot be honoured. Root motion is filtered once, on the binding component, and forwarded
    /// as a single event.
    /// </summary>
    public readonly struct AnimationLayerDescriptor
    {
        public readonly AvatarMask Mask;
        public readonly bool Additive;
        public readonly float Weight;
        public readonly byte Capacity;
        public readonly bool ApplyFootIK;
        public readonly bool ApplyPlayableIK;

        public AnimationLayerDescriptor(
            AvatarMask mask,
            bool additive,
            float weight,
            int capacity,
            bool applyFootIK = false,
            bool applyPlayableIK = false)
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
        }

        /// <summary>Single full-body layer with four playback slots. Used when no profile is assigned.</summary>
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
                    false);
            }
        }
    }
}
