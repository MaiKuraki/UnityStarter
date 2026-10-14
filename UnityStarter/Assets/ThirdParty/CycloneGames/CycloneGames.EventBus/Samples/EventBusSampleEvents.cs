namespace CycloneGames.EventBus.Sample
{
    /// <summary>Directly published every frame by the sample bootstrap.</summary>
    public struct SampleValueChangedEvent
    {
        public int Id;
        public float NewValue;
    }

    /// <summary>Produced on a background thread and drained through an MpscEventQueue.</summary>
    public struct SampleAssetLoadedEvent
    {
        public int AssetId;
        public long TimestampTicks;
    }

    /// <summary>Batched through an EventStream and flushed once per frame.</summary>
    public struct SampleMetricTickEvent
    {
        public int Sequence;
        public float Value;
    }

    /// <summary>Published in bursts to exercise the pump budgets.</summary>
    public struct SampleBurstEvent
    {
        public int BurstId;
        public int IndexInBurst;
    }
}
