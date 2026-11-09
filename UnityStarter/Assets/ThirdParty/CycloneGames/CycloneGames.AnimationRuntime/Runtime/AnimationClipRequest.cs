using CycloneGames.AnimationRuntime.Core;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// One clip request handed to the runtime. Time is absolute local clip time; the runtime never advances it.
    /// Produced by the Choreography adapter (or by any host that owns a timeline).
    /// </summary>
    public readonly struct AnimationClipRequest
    {
        public readonly int InstanceId;
        public readonly int PlaybackChannel;
        public readonly int Layer;
        public readonly AnimationClip Clip;
        public readonly double LocalTime;
        public readonly float Weight;
        public readonly bool HoldLastFrame;

        public AnimationClipRequest(
            int instanceId,
            int playbackChannel,
            int layer,
            AnimationClip clip,
            double localTime,
            float weight,
            bool holdLastFrame = false)
        {
            InstanceId = instanceId;
            PlaybackChannel = playbackChannel;
            Layer = layer;
            Clip = clip;
            LocalTime = double.IsNaN(localTime) || double.IsInfinity(localTime) || localTime < 0d ? 0d : localTime;
            Weight = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
            HoldLastFrame = holdLastFrame;
        }

        public AnimationVoiceKey Key => new AnimationVoiceKey(InstanceId, PlaybackChannel, Layer);
    }
}
