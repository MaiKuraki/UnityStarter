namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>Limits enforced at graph construction. Changing a value invalidates serialized profiles.</summary>
    public static class AnimationRuntimeLimits
    {
        /// <summary>Maximum number of layers a graph can build. Layer count is fixed at initialization.</summary>
        public const int MaxLayers = 8;

        /// <summary>Minimum playback slots per layer.</summary>
        public const int MinLayerCapacity = 1;

        /// <summary>Maximum playback slots per layer. Capacity is fixed at initialization.</summary>
        public const int MaxLayerCapacity = 8;

        /// <summary>Default capacity for a layer that does not specify one.</summary>
        public const int DefaultLayerCapacity = 4;

        /// <summary>Weight delta below which no native write is issued.</summary>
        public const float WeightEpsilon = 1e-5f;

        /// <summary>Clip length below which a clip is treated as a one-shot with no duration.</summary>
        public const float MinClipLength = 1e-4f;
    }
}
