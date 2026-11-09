namespace CycloneGames.AnimationRuntime
{
    /// <summary>Read-only counters for telemetry and the inspector. Never allocates.</summary>
    public readonly struct AnimationRuntimeStats
    {
        public readonly int ActiveVoices;
        public readonly int PeakVoices;
        public readonly int LayerCount;
        public readonly int PlayableCount;
        public readonly long DroppedVoices;
        public readonly long RecreatedPlayables;
        public readonly long Commits;
        public readonly long NativeWrites;

        public AnimationRuntimeStats(
            int activeVoices,
            int peakVoices,
            int layerCount,
            int playableCount,
            long droppedVoices,
            long recreatedPlayables,
            long commits,
            long nativeWrites)
        {
            ActiveVoices = activeVoices;
            PeakVoices = peakVoices;
            LayerCount = layerCount;
            PlayableCount = playableCount;
            DroppedVoices = droppedVoices;
            RecreatedPlayables = recreatedPlayables;
            Commits = commits;
            NativeWrites = nativeWrites;
        }
    }

    /// <summary>Zero-allocation snapshot of one voice, consumed by editor tooling only.</summary>
    public readonly struct AnimationVoiceSnapshot
    {
        public readonly int InstanceId;
        public readonly int PlaybackChannel;
        public readonly double Time;
        public readonly float Weight;
        public readonly float Length;
        public readonly bool Connected;

        public AnimationVoiceSnapshot(int instanceId, int playbackChannel, double time, float weight, float length, bool connected)
        {
            InstanceId = instanceId;
            PlaybackChannel = playbackChannel;
            Time = time;
            Weight = weight;
            Length = length;
            Connected = connected;
        }
    }
}
