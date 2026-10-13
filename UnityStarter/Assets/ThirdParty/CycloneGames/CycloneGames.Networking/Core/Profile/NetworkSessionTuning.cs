using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Server-authoritative session rate ceiling. Freshness rates (tick/send) are
    /// a session-level contract decided by the server — not a local device choice:
    /// if every endpoint derived rates from its own hardware tier, two peers in
    /// the same session could disagree on input/snapshot cadence, which produces
    /// asymmetric simulation timing that is very hard to debug.
    ///
    /// Semantics: the server value is a CEILING, not a command. A client may
    /// reduce its rate below the ceiling when its own uplink or device cannot
    /// sustain it (a phone on a weak uplink must not be forced to blast input
    /// packets at the server's rate and drown its own connection), but it may
    /// never exceed the ceiling, so session cadence stays single-sourced.
    ///
    /// Flow: the server derives this from its own composed profile and hands it to
    /// clients during session setup (the wire format and transport of these values
    /// belong to the adapter/handshake layer, deliberately out of scope here).
    /// Clients then call <see cref="ApplyTo"/> to clamp their local profile against
    /// the ceiling while keeping their local capacity budgets (pool/buffer sizes
    /// remain a per-device concern and are NOT overridden).
    ///
    /// This type is pure data + validation; it performs no I/O and never throws on
    /// the receive path beyond guarding struct construction.
    /// </summary>
    public readonly struct NetworkSessionTuning : IEquatable<NetworkSessionTuning>
    {
        public NetworkSessionTuning(int tickRate, int sendRate)
        {
            if (tickRate < NetworkConstants.MinTickRate || tickRate > NetworkConstants.MaxTickRate)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (sendRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sendRate));

            TickRate = tickRate;
            SendRate = sendRate;
        }

        public int TickRate { get; }
        public int SendRate { get; }

        /// <summary>Derives the session tuning from the server's own profile.</summary>
        public static NetworkSessionTuning FromProfile(NetworkRuntimeProfile serverProfile)
        {
            if (serverProfile == null)
                throw new ArgumentNullException(nameof(serverProfile));

            return new NetworkSessionTuning(serverProfile.TickRate, serverProfile.SendRate);
        }

        /// <summary>
        /// Rebuilds a client-side profile clamped to the server ceiling: effective
        /// rates are min(local, server) per knob. Capacity knobs (connections,
        /// pools, buffers) and protocol values are copied through unchanged.
        /// </summary>
        public NetworkRuntimeProfile ApplyTo(NetworkRuntimeProfile localProfile)
        {
            if (localProfile == null)
                throw new ArgumentNullException(nameof(localProfile));

            NetworkRuntimeProfileBuilder builder = localProfile.ToBuilder();
            builder.TickRate = System.Math.Min(localProfile.TickRate, TickRate);
            builder.SendRate = System.Math.Min(localProfile.SendRate, SendRate);
            return builder.Build();
        }

        public bool Equals(NetworkSessionTuning other)
        {
            return TickRate == other.TickRate && SendRate == other.SendRate;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkSessionTuning other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (TickRate * 397) ^ SendRate;
        }
    }
}
