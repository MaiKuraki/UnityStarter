using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Stable labels for hardware capability tiers. The enum is a serialized,
    /// protocol-safe identifier (byte); the actual knob values behind each tier live
    /// in <see cref="NetworkTierSpec"/> data and can be overridden at composition
    /// time via <see cref="NetworkDeviceProfiles.RegisterTierOverride"/> without
    /// modifying core code.
    ///
    /// This axis answers "how strong is this machine". It is intentionally
    /// independent of <see cref="NetworkSessionScale"/> ("how large is this
    /// session"); the two axes are composed by <see cref="NetworkProfileComposer"/>.
    /// </summary>
    public enum NetworkPerformanceTier : byte
    {
        /// <summary>Low-end devices: weak CPU, tight memory budget.</summary>
        Low = 0,
        /// <summary>Mainstream devices.</summary>
        Medium = 1,
        /// <summary>High-end devices.</summary>
        High = 2,
        /// <summary>Dedicated server: maximize connection count and throughput.</summary>
        Server = 3
    }

    /// <summary>
    /// Data definition of one hardware tier. Every knob is nullable; null means
    /// "inherit the default from <see cref="NetworkRuntimeProfileBuilder"/>", so a
    /// spec only has to state what it deviates on. Protocol-affecting values (MTU,
    /// payload limits) must stay uniform across tiers for cross-tier sessions to
    /// remain compatible; tier specs exist to tune capacity and rate, not protocol.
    /// </summary>
    public readonly struct NetworkTierSpec
    {
        public NetworkTierSpec(
            string profileId,
            string displayName,
            int? maxConnections = null,
            int? sendRate = null,
            int? tickRate = null,
            int? poolSize = null,
            int? snapshotBufferSize = null,
            int? maxPayloadBytes = null)
        {
            if (string.IsNullOrEmpty(profileId))
                throw new ArgumentException("Tier spec requires a non-empty profile id.", nameof(profileId));

            ProfileId = profileId;
            DisplayName = string.IsNullOrEmpty(displayName) ? profileId : displayName;
            MaxConnections = maxConnections;
            SendRate = sendRate;
            TickRate = tickRate;
            PoolSize = poolSize;
            SnapshotBufferSize = snapshotBufferSize;
            MaxPayloadBytes = maxPayloadBytes;
        }

        public string ProfileId { get; }
        public string DisplayName { get; }
        public int? MaxConnections { get; }
        public int? SendRate { get; }
        public int? TickRate { get; }
        public int? PoolSize { get; }
        public int? SnapshotBufferSize { get; }
        public int? MaxPayloadBytes { get; }

        public NetworkRuntimeProfile Build()
        {
            NetworkRuntimeProfileBuilder builder = NetworkRuntimeProfiles.CreateDefaultBuilder();
            builder.ProfileId = ProfileId;
            builder.DisplayName = DisplayName;
            if (MaxConnections.HasValue) builder.MaxConnections = MaxConnections.Value;
            if (SendRate.HasValue) builder.SendRate = SendRate.Value;
            if (TickRate.HasValue) builder.TickRate = TickRate.Value;
            if (PoolSize.HasValue) builder.PoolSize = PoolSize.Value;
            if (SnapshotBufferSize.HasValue) builder.SnapshotBufferSize = SnapshotBufferSize.Value;
            if (MaxPayloadBytes.HasValue) builder.MaxPayloadBytes = MaxPayloadBytes.Value;
            return builder.Build();
        }
    }

    /// <summary>
    /// Pure hardware description used for tier classification. All values are plain
    /// inputs supplied by the platform layer, so classification stays unit-testable
    /// and free of engine dependencies. A measured benchmark score, when present,
    /// takes precedence over static core/memory heuristics: core count is a poor
    /// proxy for networking throughput (heterogeneous big.LITTLE cores, 2-3x
    /// per-core performance spread across architectures), while
    /// <see cref="NetworkCapabilityProbe"/> measures the actual hot path.
    /// </summary>
    public readonly struct NetworkHardwareInfo
    {
        public NetworkHardwareInfo(
            int logicalProcessorCount,
            long systemMemoryMB,
            long graphicsMemoryMB = 0,
            double benchmarkScoreMBps = 0,
            bool isDedicatedServer = false)
        {
            LogicalProcessorCount = logicalProcessorCount;
            SystemMemoryMB = systemMemoryMB;
            GraphicsMemoryMB = graphicsMemoryMB;
            BenchmarkScoreMBps = benchmarkScoreMBps;
            IsDedicatedServer = isDedicatedServer;
        }

        public int LogicalProcessorCount { get; }
        public long SystemMemoryMB { get; }
        /// <summary>0 means "unknown / not reported"; graphics memory then does not participate.</summary>
        public long GraphicsMemoryMB { get; }
        /// <summary>
        /// Measured hot-path throughput from <see cref="NetworkCapabilityProbe"/>,
        /// in MB/s. 0 means "not measured"; the static heuristics are used instead.
        /// </summary>
        public double BenchmarkScoreMBps { get; }
        public bool IsDedicatedServer { get; }
    }

    /// <summary>
    /// Threshold table for hardware classification. Heuristic starting points, not
    /// universal facts: the right values depend on the target hardware generation,
    /// product scope and measured frame budget. Presets ship as named factories
    /// (<see cref="CreateStandard"/> for devices with generous memory and cooling,
    /// <see cref="CreateConstrained"/> for memory/thermally constrained devices); the
    /// platform adapter picks the appropriate table — core code encodes no
    /// assumptions about any device category. Replace globally via
    /// <see cref="NetworkDeviceProfiles.DefaultThresholds"/> at composition time,
    /// or per call via the explicit-threshold overload of
    /// <see cref="NetworkDeviceProfiles.Classify(in NetworkHardwareInfo, in NetworkTierThresholds)"/>.
    ///
    /// Semantics: a measured benchmark score is evaluated first and mostly decides
    /// the tier on its own; system memory remains a hard capacity wall that a
    /// strong CPU cannot lift. Without a score, the device lands in the weakest
    /// tier any static dimension suggests. Unreported dimensions (zero graphics
    /// memory) never participate.
    /// </summary>
    public struct NetworkTierThresholds
    {
        /// <summary>At or below this core count, the device is at most Low tier.</summary>
        public int LowMaxCores;
        /// <summary>At or below this system memory, the device is at most Low tier.</summary>
        public long LowMaxMemoryMB;
        /// <summary>At or below this core count, the device is at most Medium tier.</summary>
        public int MediumMaxCores;
        /// <summary>At or below this system memory, the device is at most Medium tier.</summary>
        public long MediumMaxMemoryMB;
        /// <summary>
        /// If graphics memory is reported (non-zero) and at or below this value,
        /// the device is capped at Medium tier. Networking throughput on clients is
        /// frequently gated by the render thread, so very weak GPUs should not be
        /// sold High-tier network budgets.
        /// </summary>
        public long MediumMaxGraphicsMemoryMB;
        /// <summary>
        /// Benchmark score (MB/s) at or below which the device is Low tier.
        /// Calibrate on real target hardware; the preset is a conservative start.
        /// </summary>
        public double LowMaxBenchmarkMBps;
        /// <summary>Benchmark score (MB/s) at or below which the device is at most Medium tier.</summary>
        public double MediumMaxBenchmarkMBps;

        /// <summary>Standard preset for devices with generous memory and sustained cooling.</summary>
        public static NetworkTierThresholds CreateStandard()
        {
            return new NetworkTierThresholds
            {
                LowMaxCores = 4,
                LowMaxMemoryMB = 4096,
                MediumMaxCores = 8,
                MediumMaxMemoryMB = 16384,
                MediumMaxGraphicsMemoryMB = 2048,
                LowMaxBenchmarkMBps = 800,
                MediumMaxBenchmarkMBps = 2500
            };
        }

        /// <summary>
        /// Constrained-device preset: tighter memory ceilings for devices whose RAM
        /// is shared aggressively with the OS/GPU, and lower benchmark ceilings for
        /// devices whose sustained throughput is thermally capped. Named after the
        /// constraint it models, not any device category — a thin-and-light laptop
        /// or handheld can be just as constrained as a phone. The platform adapter
        /// picks the table; core code encodes no category assumptions.
        /// </summary>
        public static NetworkTierThresholds CreateConstrained()
        {
            return new NetworkTierThresholds
            {
                LowMaxCores = 4,
                LowMaxMemoryMB = 3072,
                MediumMaxCores = 8,
                MediumMaxMemoryMB = 8192,
                MediumMaxGraphicsMemoryMB = 1024,
                LowMaxBenchmarkMBps = 500,
                MediumMaxBenchmarkMBps = 1500
            };
        }

        public void Validate()
        {
            if (LowMaxCores <= 0 || MediumMaxCores <= LowMaxCores)
                throw new ArgumentOutOfRangeException(nameof(MediumMaxCores), "Core thresholds must be positive and strictly increasing.");
            if (LowMaxMemoryMB <= 0 || MediumMaxMemoryMB <= LowMaxMemoryMB)
                throw new ArgumentOutOfRangeException(nameof(MediumMaxMemoryMB), "Memory thresholds must be positive and strictly increasing.");
            if (MediumMaxGraphicsMemoryMB < 0)
                throw new ArgumentOutOfRangeException(nameof(MediumMaxGraphicsMemoryMB));
            if (LowMaxBenchmarkMBps <= 0 || MediumMaxBenchmarkMBps <= LowMaxBenchmarkMBps)
                throw new ArgumentOutOfRangeException(nameof(MediumMaxBenchmarkMBps), "Benchmark thresholds must be positive and strictly increasing.");
        }
    }

    /// <summary>
    /// Hardware-tier profile table. The knobs that scale with device capability are
    /// buffer/pool capacities (memory pressure vs. churn), send rate (bandwidth vs.
    /// freshness) and connection count; the protocol itself is identical on every
    /// tier so cross-tier sessions stay compatible.
    ///
    /// Built-in tiers are plain <see cref="NetworkTierSpec"/> data exposed as
    /// <see cref="LowTierSpec"/> etc. Games can replace any tier's values via
    /// <see cref="RegisterTierOverride"/> at composition time, or bypass tiers
    /// entirely with <see cref="ForSpec"/>; core code never needs modification.
    /// Session-size concerns are deliberately NOT modeled here — see
    /// <see cref="NetworkSessionScaleTable"/> and <see cref="NetworkProfileComposer"/>.
    /// </summary>
    public static class NetworkDeviceProfiles
    {
        public const string LowTierProfileId = "tier-low";
        public const string MediumTierProfileId = "tier-medium";
        public const string HighTierProfileId = "tier-high";
        public const string ServerTierProfileId = "tier-server";

        // Baseline rationale: connection counts and pool sizes step ~4x between
        // adjacent tiers, matching the roughly 4x memory/core gap between tiers in
        // the default threshold table. Server optimizes for connection count and
        // throughput over per-frame latency.
        public static readonly NetworkTierSpec LowTierSpec = new NetworkTierSpec(
            LowTierProfileId, "Low Tier",
            maxConnections: 32, sendRate: 15, poolSize: 16, snapshotBufferSize: 32,
            maxPayloadBytes: NetworkConstants.DefaultMaxPayloadSize);

        public static readonly NetworkTierSpec MediumTierSpec = new NetworkTierSpec(
            MediumTierProfileId, "Medium Tier",
            maxConnections: NetworkConstants.DefaultMaxConnections,
            sendRate: NetworkConstants.DefaultSendRate);

        public static readonly NetworkTierSpec HighTierSpec = new NetworkTierSpec(
            HighTierProfileId, "High Tier",
            maxConnections: 128, sendRate: 30, poolSize: 64, snapshotBufferSize: 128);

        public static readonly NetworkTierSpec ServerTierSpec = new NetworkTierSpec(
            ServerTierProfileId, "Dedicated Server",
            maxConnections: 1024, tickRate: 60, sendRate: 30, poolSize: 256, snapshotBufferSize: 256);

        private static readonly Dictionary<NetworkPerformanceTier, NetworkTierSpec> s_overrides =
            new Dictionary<NetworkPerformanceTier, NetworkTierSpec>(4);

        private static NetworkTierThresholds s_thresholds = NetworkTierThresholds.CreateStandard();

        /// <summary>
        /// Thresholds used by the implicit-threshold <see cref="Classify(in NetworkHardwareInfo)"/>
        /// overload. Set once at composition time; treat as immutable afterwards.
        /// </summary>
        public static NetworkTierThresholds DefaultThresholds
        {
            get { return s_thresholds; }
            set
            {
                value.Validate();
                s_thresholds = value;
            }
        }

        /// <summary>
        /// Replaces the profile values of a built-in tier. Intended to be called
        /// once during composition/bootstrap, before any profile is consumed.
        /// Pass the corresponding built-in spec (e.g. <see cref="LowTierSpec"/>)
        /// to restore the baseline.
        /// </summary>
        public static void RegisterTierOverride(NetworkPerformanceTier tier, in NetworkTierSpec spec)
        {
            ValidateTier(tier);
            s_overrides[tier] = spec;
        }

        public static void ClearTierOverrides()
        {
            s_overrides.Clear();
        }

        public static NetworkTierSpec GetSpec(NetworkPerformanceTier tier)
        {
            ValidateTier(tier);
            if (s_overrides.TryGetValue(tier, out NetworkTierSpec spec))
                return spec;

            switch (tier)
            {
                case NetworkPerformanceTier.Low: return LowTierSpec;
                case NetworkPerformanceTier.Medium: return MediumTierSpec;
                case NetworkPerformanceTier.High: return HighTierSpec;
                case NetworkPerformanceTier.Server: return ServerTierSpec;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown performance tier.");
            }
        }

        public static NetworkRuntimeProfile ForTier(NetworkPerformanceTier tier)
        {
            return GetSpec(tier).Build();
        }

        /// <summary>Builds a profile from a fully custom spec, bypassing the tier table.</summary>
        public static NetworkRuntimeProfile ForSpec(in NetworkTierSpec spec)
        {
            return spec.Build();
        }

        public static NetworkRuntimeProfileRegistry CreateTierRegistry()
        {
            var registry = new NetworkRuntimeProfileRegistry(capacity: 8);
            registry.Register(ForTier(NetworkPerformanceTier.Low));
            registry.Register(ForTier(NetworkPerformanceTier.Medium));
            registry.Register(ForTier(NetworkPerformanceTier.High));
            registry.Register(ForTier(NetworkPerformanceTier.Server));
            return registry;
        }

        /// <summary>
        /// Heuristic device classification from host-reported capabilities, using
        /// <see cref="DefaultThresholds"/>. Kept in Core as pure inputs so the
        /// platform layer supplies SystemInfo/probe values and the mapping stays
        /// unit-testable.
        /// </summary>
        public static NetworkPerformanceTier Classify(in NetworkHardwareInfo hardware)
        {
            return Classify(hardware, s_thresholds);
        }

        /// <summary>
        /// Classification with an explicit threshold table. A measured benchmark
        /// score is evaluated first; without one, the device lands in the weakest
        /// tier any static dimension suggests. System memory is always a hard
        /// capacity wall. Unreported dimensions (zero graphics memory) never
        /// participate.
        /// </summary>
        public static NetworkPerformanceTier Classify(in NetworkHardwareInfo hardware, in NetworkTierThresholds thresholds)
        {
            thresholds.Validate();

            if (hardware.IsDedicatedServer)
                return NetworkPerformanceTier.Server;

            if (hardware.BenchmarkScoreMBps > 0)
            {
                if (hardware.BenchmarkScoreMBps <= thresholds.LowMaxBenchmarkMBps ||
                    hardware.SystemMemoryMB <= thresholds.LowMaxMemoryMB)
                    return NetworkPerformanceTier.Low;
                if (hardware.BenchmarkScoreMBps <= thresholds.MediumMaxBenchmarkMBps ||
                    hardware.SystemMemoryMB <= thresholds.MediumMaxMemoryMB)
                    return NetworkPerformanceTier.Medium;
                return NetworkPerformanceTier.High;
            }

            if (hardware.LogicalProcessorCount <= thresholds.LowMaxCores ||
                hardware.SystemMemoryMB <= thresholds.LowMaxMemoryMB)
                return NetworkPerformanceTier.Low;

            if (hardware.LogicalProcessorCount <= thresholds.MediumMaxCores ||
                hardware.SystemMemoryMB <= thresholds.MediumMaxMemoryMB ||
                (hardware.GraphicsMemoryMB > 0 && hardware.GraphicsMemoryMB <= thresholds.MediumMaxGraphicsMemoryMB))
                return NetworkPerformanceTier.Medium;

            return NetworkPerformanceTier.High;
        }

        /// <summary>
        /// Overload for callers that only report core count and memory; graphics
        /// memory and benchmark score are treated as unreported.
        /// </summary>
        public static NetworkPerformanceTier Classify(int logicalProcessorCount, long systemMemoryMB, bool isDedicatedServer)
        {
            return Classify(new NetworkHardwareInfo(logicalProcessorCount, systemMemoryMB, 0, 0, isDedicatedServer));
        }

        private static void ValidateTier(NetworkPerformanceTier tier)
        {
            if (tier < NetworkPerformanceTier.Low || tier > NetworkPerformanceTier.Server)
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown performance tier.");
        }
    }
}
