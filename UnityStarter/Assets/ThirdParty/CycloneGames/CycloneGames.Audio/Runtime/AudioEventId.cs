// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Session-scoped handle for an <see cref="AudioEvent"/>.
    /// </summary>
    /// <remarks>
    /// Resolve once with <see cref="AudioManager.TryGetEventId"/> and reuse the value on playback
    /// paths that would otherwise hash the event name on every call. The id stays valid while the
    /// owning bank is loaded and is never reissued to a different event. It is not persisted.
    /// </remarks>
    public readonly struct AudioEventId : IEquatable<AudioEventId>
    {
        /// <summary>Represents "no event". Playing or stopping it is a no-op.</summary>
        public static AudioEventId None => default;

        private readonly int value;

        internal AudioEventId(int value)
        {
            this.value = value;
        }

        /// <summary>Whether this handle refers to a registered event.</summary>
        public bool IsValid => value != 0;

        internal int Value => value;

        public bool Equals(AudioEventId other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioEventId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value;
        }

        public static bool operator ==(AudioEventId left, AudioEventId right)
        {
            return left.value == right.value;
        }

        public static bool operator !=(AudioEventId left, AudioEventId right)
        {
            return left.value != right.value;
        }

        public override string ToString()
        {
            return value.ToString();
        }
    }
}
