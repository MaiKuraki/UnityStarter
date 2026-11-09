using CycloneGames.AnimationRuntime.Core;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// One clip request handed to the runtime.
    ///
    /// Under <see cref="AnimationTimePolicy.External"/> <see cref="LocalTime"/> is the absolute local clip time
    /// and the runtime never advances it. Under <see cref="AnimationTimePolicy.TickDriven"/> the runtime derives
    /// time from commit ticks; <see cref="LocalTime"/> is the initial offset, <see cref="Speed"/> and
    /// <see cref="Loop"/> describe how that clock advances, and a later <c>SeekClip</c> is the explicit way to
    /// jump. Produced by a timeline adapter (Choreography) or by gameplay code.
    /// </summary>
    public readonly struct AnimationClipRequest
    {
        public readonly int InstanceId;
        public readonly int PlaybackChannel;

        /// <summary>
        /// Provider sub-channel. Playbacks with the same instance/playback channel/layer but different clip channels
        /// coexist, which is what a cross-fade needs. Clamped to a byte.
        /// </summary>
        public readonly int ClipChannel;

        public readonly int Layer;
        public readonly AnimationClip Clip;
        public readonly double LocalTime;
        public readonly float Weight;

        /// <summary>Playback rate used by <see cref="AnimationTimePolicy.TickDriven"/>. Ignored in external mode.</summary>
        public readonly float Speed;

        /// <summary>Wrap behaviour used by <see cref="AnimationTimePolicy.TickDriven"/>. Ignored in external mode.</summary>
        public readonly AnimationLoopMode Loop;

        public readonly bool HoldLastFrame;

        public AnimationClipRequest(
            int instanceId,
            int playbackChannel,
            int layer,
            AnimationClip clip,
            double localTime,
            float weight,
            bool holdLastFrame = false,
            int clipChannel = 0,
            float speed = 1f,
            AnimationLoopMode loop = AnimationLoopMode.Clamp)
        {
            InstanceId = instanceId;
            PlaybackChannel = playbackChannel;
            ClipChannel = clipChannel < 0 ? 0 : (clipChannel > byte.MaxValue ? byte.MaxValue : clipChannel);
            Layer = layer;
            Clip = clip;
            LocalTime = double.IsNaN(localTime) || double.IsInfinity(localTime) || localTime < 0d ? 0d : localTime;
            Weight = weight <= 0f ? 0f : (weight >= 1f ? 1f : weight);
            Speed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1f : speed;
            Loop = loop == AnimationLoopMode.Loop || loop == AnimationLoopMode.PingPong ? loop : AnimationLoopMode.Clamp;
            HoldLastFrame = holdLastFrame;
        }

        public AnimationPlaybackKey Key => new AnimationPlaybackKey(InstanceId, PlaybackChannel, Layer, ClipChannel);
    }
}
