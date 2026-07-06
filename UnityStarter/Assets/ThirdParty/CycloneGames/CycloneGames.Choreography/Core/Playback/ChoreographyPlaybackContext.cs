namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Immutable per-instance playback configuration passed to <see cref="ChoreographyPlayer.Load"/>.
    /// Kept as a small value type so callers can build it on the stack without allocation.
    /// </summary>
    public readonly struct ChoreographyPlaybackContext
    {
        /// <summary>Instance id stamped on every emitted sample/event. 0 is reserved for standalone playback.</summary>
        public readonly int InstanceId;

        /// <summary>Competition channel stamped on every emitted sample; strategies group and resolve per channel.</summary>
        public readonly int Channel;

        /// <summary>Playback rate multiplier. 1 = authored speed. Must be &gt; 0.</summary>
        public readonly double Speed;

        /// <summary>When true, the player restarts from 0 on reaching the end instead of completing.</summary>
        public readonly bool Loop;

        /// <summary>Fallback strategy mode used by sections whose preferred mode is <see cref="ChoreographyPlaybackMode.Inherit"/>.</summary>
        public readonly ChoreographyPlaybackMode DefaultMode;

        public ChoreographyPlaybackContext(
            int instanceId,
            int channel = 0,
            double speed = 1d,
            bool loop = false,
            ChoreographyPlaybackMode defaultMode = ChoreographyPlaybackMode.Priority)
        {
            InstanceId = instanceId;
            Channel = channel;
            Speed = speed <= 0d ? 1d : speed;
            Loop = loop;
            DefaultMode = defaultMode == ChoreographyPlaybackMode.Inherit ? ChoreographyPlaybackMode.Priority : defaultMode;
        }

        public static ChoreographyPlaybackContext Default => new ChoreographyPlaybackContext(0, 0, 1d, false);
    }
}
