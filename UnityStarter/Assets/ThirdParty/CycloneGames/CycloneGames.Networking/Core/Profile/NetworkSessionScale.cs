using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Expected session size axis, independent of hardware capability
    /// (<see cref="NetworkPerformanceTier"/>). A low-end phone can join a Battle
    /// session and a high-end PC can host a Coop session; the two axes compose in
    /// <see cref="NetworkProfileComposer"/>.
    /// </summary>
    public enum NetworkSessionScale : byte
    {
        /// <summary>Small player-hosted sessions, up to ~8 participants.</summary>
        Coop = 0,
        /// <summary>Medium sessions, up to ~32 participants.</summary>
        Room = 1,
        /// <summary>Large sessions, up to ~128 participants. Dedicated server recommended.</summary>
        Battle = 2,
        /// <summary>
        /// Very large sessions, up to ~1024 participants per instance. Requires a
        /// dedicated server; anything beyond this scale is a world-partitioning /
        /// sharding architecture concern that per-instance profiles cannot cover.
        /// </summary>
        Massive = 3
    }

    /// <summary>Whether this endpoint is a pure client, a player-host, or a dedicated server.</summary>
    public enum NetworkEndpointRole : byte
    {
        Client = 0,
        ListenHost = 1,
        DedicatedServer = 2
    }

    /// <summary>
    /// Data definition of one session scale. Every knob is nullable; null means the
    /// composed profile keeps the value coming from the hardware tier spec.
    /// </summary>
    public readonly struct NetworkScaleSpec
    {
        public NetworkScaleSpec(
            int expectedParticipants,
            int? maxConnections = null,
            int? snapshotBufferSize = null,
            int? poolSize = null,
            int? sendRate = null)
        {
            if (expectedParticipants <= 0)
                throw new ArgumentOutOfRangeException(nameof(expectedParticipants));

            ExpectedParticipants = expectedParticipants;
            MaxConnections = maxConnections;
            SnapshotBufferSize = snapshotBufferSize;
            PoolSize = poolSize;
            SendRate = sendRate;
        }

        public int ExpectedParticipants { get; }
        public int? MaxConnections { get; }
        public int? SnapshotBufferSize { get; }
        public int? PoolSize { get; }
        public int? SendRate { get; }
    }

    /// <summary>
    /// Session-scale table with overridable built-in values. Scale values describe
    /// session pressure (participants, entity churn), not device capability; they
    /// apply on every platform unchanged.
    /// </summary>
    public static class NetworkSessionScaleTable
    {
        public static readonly NetworkScaleSpec CoopSpec = new NetworkScaleSpec(
            expectedParticipants: 8, maxConnections: 8, snapshotBufferSize: 32, poolSize: 16);

        public static readonly NetworkScaleSpec RoomSpec = new NetworkScaleSpec(
            expectedParticipants: 32, maxConnections: 32, snapshotBufferSize: 64, poolSize: 32);

        public static readonly NetworkScaleSpec BattleSpec = new NetworkScaleSpec(
            expectedParticipants: 128, maxConnections: 128, snapshotBufferSize: 128, poolSize: 64);

        public static readonly NetworkScaleSpec MassiveSpec = new NetworkScaleSpec(
            expectedParticipants: 1024, maxConnections: 1024, snapshotBufferSize: 256, poolSize: 256);

        private static readonly Dictionary<NetworkSessionScale, NetworkScaleSpec> s_overrides =
            new Dictionary<NetworkSessionScale, NetworkScaleSpec>(4);

        /// <summary>Composition-time override of a built-in scale; symmetric with tier overrides.</summary>
        public static void RegisterScaleOverride(NetworkSessionScale scale, in NetworkScaleSpec spec)
        {
            ValidateScale(scale);
            s_overrides[scale] = spec;
        }

        public static void ClearScaleOverrides()
        {
            s_overrides.Clear();
        }

        public static NetworkScaleSpec GetSpec(NetworkSessionScale scale)
        {
            ValidateScale(scale);
            if (s_overrides.TryGetValue(scale, out NetworkScaleSpec spec))
                return spec;

            switch (scale)
            {
                case NetworkSessionScale.Coop: return CoopSpec;
                case NetworkSessionScale.Room: return RoomSpec;
                case NetworkSessionScale.Battle: return BattleSpec;
                case NetworkSessionScale.Massive: return MassiveSpec;
                default: throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown session scale.");
            }
        }

        private static void ValidateScale(NetworkSessionScale scale)
        {
            if (scale < NetworkSessionScale.Coop || scale > NetworkSessionScale.Massive)
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown session scale.");
        }
    }

    /// <summary>
    /// Composes the two orthogonal axes — hardware capability and session scale —
    /// plus the endpoint role into a single runtime profile.
    ///
    /// Composition rules:
    /// - Client: connection count is meaningless (a client holds exactly one
    ///   connection); scale only raises buffering to survive larger sessions.
    ///   Effective capacities are max(tier, scale) per knob: the session must not
    ///   starve a strong device, and a weak device hosting/joining a big session
    ///   still gets the buffers the session demands.
    /// - ListenHost: same as client, plus connection count from the scale.
    /// - DedicatedServer: based on the Server tier; scale sets connection count.
    /// - Massive scale on a ListenHost is rejected: player-hosted instances cannot
    ///   safely absorb ~1024-connection pressure; fail fast at composition time.
    ///
    /// Rates (send/tick) always come from the tier axis: freshness is a function
    /// of what the machine can sustain, not of how many participants there are.
    /// Protocol values (MTU, payload limits) are never touched by either axis.
    /// </summary>
    public static class NetworkProfileComposer
    {
        public static NetworkRuntimeProfile Compose(
            NetworkPerformanceTier hardwareTier,
            NetworkSessionScale sessionScale,
            NetworkEndpointRole role)
        {
            if (role == NetworkEndpointRole.ListenHost && sessionScale == NetworkSessionScale.Massive)
            {
                throw new ArgumentException(
                    "Massive sessions require a dedicated server; a player-hosted listen server cannot absorb this scale safely.",
                    nameof(sessionScale));
            }

            NetworkTierSpec tierSpec = role == NetworkEndpointRole.DedicatedServer
                ? NetworkDeviceProfiles.GetSpec(NetworkPerformanceTier.Server)
                : NetworkDeviceProfiles.GetSpec(hardwareTier);
            NetworkScaleSpec scaleSpec = NetworkSessionScaleTable.GetSpec(sessionScale);

            NetworkRuntimeProfileBuilder builder = NetworkRuntimeProfiles.CreateDefaultBuilder();
            builder.ProfileId = tierSpec.ProfileId + "-" + sessionScale.ToString().ToLowerInvariant();
            builder.DisplayName = tierSpec.DisplayName + " / " + sessionScale;

            // Tier axis first (null = builder default already set).
            if (tierSpec.SendRate.HasValue) builder.SendRate = tierSpec.SendRate.Value;
            if (tierSpec.TickRate.HasValue) builder.TickRate = tierSpec.TickRate.Value;
            if (tierSpec.MaxPayloadBytes.HasValue) builder.MaxPayloadBytes = tierSpec.MaxPayloadBytes.Value;

            // Capacity knobs: max of what the tier offers and what the session demands.
            builder.PoolSize = MaxOrDefault(tierSpec.PoolSize, scaleSpec.PoolSize, builder.PoolSize);
            builder.SnapshotBufferSize = MaxOrDefault(tierSpec.SnapshotBufferSize, scaleSpec.SnapshotBufferSize, builder.SnapshotBufferSize);

            // Connection count only matters for endpoints that accept connections.
            if (role != NetworkEndpointRole.Client)
            {
                builder.MaxConnections = MaxOrDefault(tierSpec.MaxConnections, scaleSpec.MaxConnections, builder.MaxConnections);
            }

            return builder.Build();
        }

        private static int MaxOrDefault(int? tierValue, int? scaleValue, int defaultValue)
        {
            // Only the two axes compete; the builder default applies solely when
            // NEITHER axis specifies the knob. Folding the default into the max
            // would silently re-inflate values a tier deliberately tuned DOWN
            // (e.g. Low tier sets MaxConnections 32 < default 100 on purpose).
            int result = 0;
            bool specified = false;
            if (tierValue.HasValue) { result = tierValue.Value; specified = true; }
            if (scaleValue.HasValue && scaleValue.Value > result) { result = scaleValue.Value; specified = true; }
            return specified ? result : defaultValue;
        }
    }
}
