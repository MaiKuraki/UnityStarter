namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>Topology contracts (serialized) and runtime budgets.</summary>
    public static class AnimationRuntimeLimits
    {
        /// <summary>Maximum number of layers a graph can build. Layer count is fixed at initialization.</summary>
        public static readonly int MaxLayers = 8;

        /// <summary>Minimum playback slots per layer.</summary>
        public static readonly int MinLayerCapacity = 1;

        /// <summary>Maximum playback slots per layer. Capacity is fixed at initialization.</summary>
        public static readonly int MaxLayerCapacity = 8;

        /// <summary>Default capacity for a layer that does not specify one.</summary>
        public static readonly int DefaultLayerCapacity = 4;

        /// <summary>Weight delta below which no native write is issued.</summary>
        public static readonly float WeightEpsilon = 1e-5f;

        /// <summary>Clip length below which a clip is treated as a one-shot with no duration.</summary>
        public static readonly float MinClipLength = 1e-4f;

        /// <summary>Hard bound on the legs one event or exit-time sweep may walk.</summary>
        public static readonly int MaxSweepLegs = 64;

        /// <summary>Maximum clip markers dispatched per commit.</summary>
        public static readonly int MaxPendingClipEventsPerCommit = 4096;

        /// <summary>First allocation for the clip-event dispatch queue. Zero until the first marker is queued.</summary>
        public static readonly int InitialClipEventQueueCapacity = 32;

        /// <summary>Queue capacity retained after a drain.</summary>
        public static readonly int SteadyClipEventQueueCapacity = 512;
    }
}
