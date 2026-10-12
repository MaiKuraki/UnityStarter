namespace CycloneGames.Networking
{
    /// <summary>
    /// Deterministic channel negotiation. Maps a requested QoS channel onto the
    /// closest channel the transport actually supports, never throwing on the
    /// hot path. Fallback preserves the strongest guarantee available:
    /// ReliableUnordered degrades to Reliable (keeps delivery, adds ordering),
    /// UnreliableSequenced degrades to Unreliable (keeps loss tolerance).
    /// </summary>
    public static class NetworkChannelNegotiation
    {
        public static bool TryResolve(NetworkChannel requested, NetworkChannelFlags supported, out NetworkChannel resolved)
        {
            resolved = requested;
            NetworkChannelFlags requestedFlag = NetworkTransportCapabilities.ToFlag(requested);
            if (requestedFlag != NetworkChannelFlags.None && (supported & requestedFlag) != 0)
                return true;

            switch (requested)
            {
                case NetworkChannel.ReliableUnordered:
                    if ((supported & NetworkChannelFlags.Reliable) != 0)
                    {
                        resolved = NetworkChannel.Reliable;
                        return true;
                    }
                    break;
                case NetworkChannel.UnreliableSequenced:
                    if ((supported & NetworkChannelFlags.Unreliable) != 0)
                    {
                        resolved = NetworkChannel.Unreliable;
                        return true;
                    }
                    break;
                case NetworkChannel.Reliable:
                    if ((supported & NetworkChannelFlags.ReliableUnordered) != 0)
                    {
                        resolved = NetworkChannel.ReliableUnordered;
                        return true;
                    }
                    break;
                case NetworkChannel.Unreliable:
                    if ((supported & NetworkChannelFlags.UnreliableSequenced) != 0)
                    {
                        resolved = NetworkChannel.UnreliableSequenced;
                        return true;
                    }
                    break;
            }

            return false;
        }

        public static NetworkChannel ResolveOrThrow(NetworkChannel requested, NetworkChannelFlags supported)
        {
            if (TryResolve(requested, supported, out NetworkChannel resolved))
                return resolved;

            throw new System.ArgumentException(
                $"Transport supports none of the fallback channels for {requested}.", nameof(requested));
        }
    }
}
