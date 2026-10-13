using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Immutable protocol version window shared by every domain networking module.
    /// Derived once from the manifest's <c>CurrentVersion</c> / <c>MinimumSupportedVersion</c>
    /// so a module never stores a second, drift-prone copy of the version.
    /// </summary>
    public readonly struct NetworkProtocolVersion : IEquatable<NetworkProtocolVersion>
    {
        public NetworkProtocolVersion(byte current, byte minimumSupported)
        {
            if (current == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(current), "Protocol version must be greater than zero.");
            }

            if (minimumSupported == 0 || minimumSupported > current)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSupported), "Minimum supported version must be in the range (0, current].");
            }

            Current = current;
            MinimumSupported = minimumSupported;
        }

        public byte Current { get; }
        public byte MinimumSupported { get; }

        /// <summary>True when a remote peer advertising <paramref name="remoteVersion"/> falls inside this window.</summary>
        public bool Supports(byte remoteVersion)
        {
            return remoteVersion >= MinimumSupported && remoteVersion <= Current;
        }

        /// <summary>True when the local and remote version windows overlap (mutual interoperability).</summary>
        public bool IsCompatibleWith(in NetworkProtocolVersion remote)
        {
            return MinimumSupported <= remote.Current && remote.MinimumSupported <= Current;
        }

        public bool Equals(NetworkProtocolVersion other)
        {
            return Current == other.Current && MinimumSupported == other.MinimumSupported;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkProtocolVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Current << 8) | MinimumSupported;
        }

        public override string ToString()
        {
            return $"v{Current} (min v{MinimumSupported})";
        }
    }
}
