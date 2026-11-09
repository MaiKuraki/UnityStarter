using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>
    /// Identity of one clip voice: playback instance, competition channel and layer.
    /// Equality ignores the clip asset, so re-beginning the same instance replaces the voice.
    /// </summary>
    public readonly struct AnimationVoiceKey : IEquatable<AnimationVoiceKey>
    {
        private readonly int _instanceId;
        private readonly int _playbackChannel;
        private readonly byte _layer;

        public AnimationVoiceKey(int instanceId, int playbackChannel, int layer)
        {
            _instanceId = instanceId;
            _playbackChannel = playbackChannel;
            _layer = layer < 0 ? (byte)0 : (layer > byte.MaxValue ? byte.MaxValue : (byte)layer);
        }

        public int InstanceId => _instanceId;

        public int PlaybackChannel => _playbackChannel;

        public int Layer => _layer;

        public bool Equals(AnimationVoiceKey other)
        {
            return _instanceId == other._instanceId
                && _playbackChannel == other._playbackChannel
                && _layer == other._layer;
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationVoiceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _instanceId;
                hash = (hash * 397) ^ _playbackChannel;
                hash = (hash * 397) ^ _layer;
                return hash;
            }
        }

        public override string ToString()
        {
            return "VoiceKey(instance " + _instanceId + ", channel " + _playbackChannel + ", layer " + _layer + ")";
        }
    }
}
