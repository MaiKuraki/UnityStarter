using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// Identity of one clip playback: playback instance, competition channel, provider sub-channel and layer.
    ///
    /// <c>ClipChannel</c> lets one playback channel own several concurrent playbacks (a cross-fade between two clips,
    /// or two additive variants) without inventing synthetic instance ids. Equality ignores the clip asset, so
    /// re-beginning the same key replaces the playback; use a different <c>ClipChannel</c> to overlap instead.
    /// </summary>
    public readonly struct AnimationPlaybackKey : IEquatable<AnimationPlaybackKey>
    {
        private readonly int _instanceId;
        private readonly int _playbackChannel;
        private readonly byte _clipChannel;
        private readonly byte _layer;

        public AnimationPlaybackKey(int instanceId, int playbackChannel, int layer, int clipChannel = 0)
        {
            _instanceId = instanceId;
            _playbackChannel = playbackChannel;
            _clipChannel = clipChannel < 0 ? (byte)0 : (clipChannel > byte.MaxValue ? byte.MaxValue : (byte)clipChannel);
            _layer = layer < 0 ? (byte)0 : (layer > byte.MaxValue ? byte.MaxValue : (byte)layer);
        }

        public int InstanceId => _instanceId;

        public int PlaybackChannel => _playbackChannel;

        /// <summary>Provider sub-channel. Two playbacks with the same instance/channel/layer but different values coexist.</summary>
        public int ClipChannel => _clipChannel;

        public int Layer => _layer;

        public bool Equals(AnimationPlaybackKey other)
        {
            return _instanceId == other._instanceId
                && _playbackChannel == other._playbackChannel
                && _clipChannel == other._clipChannel
                && _layer == other._layer;
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationPlaybackKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _instanceId;
                hash = (hash * 397) ^ _playbackChannel;
                hash = (hash * 397) ^ _clipChannel;
                hash = (hash * 397) ^ _layer;
                return hash;
            }
        }

        public override string ToString()
        {
            return "PlaybackKey(instance " + _instanceId
                + ", channel " + _playbackChannel
                + ", clip " + _clipChannel
                + ", layer " + _layer + ")";
        }
    }
}
