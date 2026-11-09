using CycloneGames.AnimationRuntime.Core;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Read-only counters for telemetry and the inspector. Never allocates.</summary>
    public readonly struct AnimationRuntimeStats
    {
        public readonly int ActivePlaybacks;
        public readonly int PeakPlaybacks;
        public readonly int LayerCount;
        public readonly int PlayableCount;
        public readonly long DroppedPlaybacks;
        public readonly long RecreatedPlayables;
        public readonly long ReusedPlayables;
        public readonly long Commits;
        public readonly long Evaluations;
        public readonly long NativeWrites;
        public readonly AnimationTimePolicy TimePolicy;

        public AnimationRuntimeStats(
            int activePlaybacks,
            int peakPlaybacks,
            int layerCount,
            int playableCount,
            long droppedPlaybacks,
            long recreatedPlayables,
            long reusedPlayables,
            long commits,
            long evaluations,
            long nativeWrites,
            AnimationTimePolicy timePolicy)
        {
            ActivePlaybacks = activePlaybacks;
            PeakPlaybacks = peakPlaybacks;
            LayerCount = layerCount;
            PlayableCount = playableCount;
            DroppedPlaybacks = droppedPlaybacks;
            RecreatedPlayables = recreatedPlayables;
            ReusedPlayables = reusedPlayables;
            Commits = commits;
            Evaluations = evaluations;
            NativeWrites = nativeWrites;
            TimePolicy = timePolicy;
        }
    }

    /// <summary>Zero-allocation snapshot of one playback, consumed by editor tooling only.</summary>
    public readonly struct AnimationPlaybackSnapshot
    {
        public readonly int InstanceId;
        public readonly int PlaybackChannel;
        public readonly int ClipChannel;
        public readonly double Time;
        public readonly float Weight;
        public readonly float Length;
        public readonly bool Connected;

        public AnimationPlaybackSnapshot(
            int instanceId,
            int playbackChannel,
            int clipChannel,
            double time,
            float weight,
            float length,
            bool connected)
        {
            InstanceId = instanceId;
            PlaybackChannel = playbackChannel;
            ClipChannel = clipChannel;
            Time = time;
            Weight = weight;
            Length = length;
            Connected = connected;
        }
    }
}
