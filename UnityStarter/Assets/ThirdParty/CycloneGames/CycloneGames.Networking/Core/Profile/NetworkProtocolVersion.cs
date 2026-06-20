using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Immutable protocol version window shared by every domain networking module.
    /// </summary>
    /// <remarks>
    /// Replaces the per-module <c>PROTOCOL_VERSION</c> / <c>MIN_SUPPORTED_PROTOCOL_VERSION</c>
    /// byte constants and the duplicated <c>IsSupportedProtocolVersion</c> checks that each
    /// CycloneGames *.Networking package used to copy-paste. Keeping the window in one type
    /// removes the drift risk of those independent reimplementations.
    /// </remarks>
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

        public static NetworkProtocolVersion Create(byte current, byte minimumSupported = 1)
        {
            return new NetworkProtocolVersion(current, minimumSupported);
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
