using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Instance-scoped tier table. Each registry owns its overrides and thresholds,
    /// so parallel tests, multi-session composition and DI roots stay isolated.
    /// Override state lives here; NetworkDeviceProfiles exposes read-only built-in specs.
    /// </summary>
    public sealed class NetworkTierRegistry
    {
        private readonly Dictionary<NetworkPerformanceTier, NetworkTierSpec> _overrides =
            new Dictionary<NetworkPerformanceTier, NetworkTierSpec>(4);

        private NetworkTierThresholds _thresholds;

        public NetworkTierRegistry(in NetworkTierThresholds thresholds)
        {
            thresholds.Validate();
            _thresholds = thresholds;
        }

        public static NetworkTierRegistry CreateStandard() =>
            new NetworkTierRegistry(NetworkTierThresholds.CreateStandard());

        public static NetworkTierRegistry CreateConstrained() =>
            new NetworkTierRegistry(NetworkTierThresholds.CreateConstrained());

        public NetworkTierThresholds Thresholds
        {
            get => _thresholds;
            set
            {
                value.Validate();
                _thresholds = value;
            }
        }

        public NetworkTierSpec GetSpec(NetworkPerformanceTier tier)
        {
            if (_overrides.TryGetValue(tier, out NetworkTierSpec spec))
                return spec;

            switch (tier)
            {
                case NetworkPerformanceTier.Low: return NetworkDeviceProfiles.LowTierSpec;
                case NetworkPerformanceTier.Medium: return NetworkDeviceProfiles.MediumTierSpec;
                case NetworkPerformanceTier.High: return NetworkDeviceProfiles.HighTierSpec;
                case NetworkPerformanceTier.Server: return NetworkDeviceProfiles.ServerTierSpec;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown performance tier.");
            }
        }

        public void RegisterTierOverride(NetworkPerformanceTier tier, in NetworkTierSpec spec)
        {
            ValidateTier(tier);
            _overrides[tier] = spec;
        }

        public void ClearTierOverrides() => _overrides.Clear();

        public NetworkRuntimeProfile ForTier(NetworkPerformanceTier tier) => GetSpec(tier).Build();

        public NetworkRuntimeProfile ForSpec(in NetworkTierSpec spec) => spec.Build();

        public NetworkPerformanceTier Classify(in NetworkHardwareInfo hardware) =>
            NetworkDeviceProfiles.Classify(hardware, _thresholds);

        private static void ValidateTier(NetworkPerformanceTier tier)
        {
            if (tier < NetworkPerformanceTier.Low || tier > NetworkPerformanceTier.Server)
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown performance tier.");
        }
    }

    /// <summary>
    /// Instance-scoped session-scale table; symmetric with
    /// <see cref="NetworkTierRegistry"/>.
    /// </summary>
    public sealed class NetworkSessionScaleRegistry
    {
        private readonly Dictionary<NetworkSessionScale, NetworkScaleSpec> _overrides =
            new Dictionary<NetworkSessionScale, NetworkScaleSpec>(4);

        public NetworkScaleSpec GetSpec(NetworkSessionScale scale)
        {
            if (_overrides.TryGetValue(scale, out NetworkScaleSpec spec))
                return spec;

            switch (scale)
            {
                case NetworkSessionScale.Coop: return NetworkSessionScaleTable.CoopSpec;
                case NetworkSessionScale.Room: return NetworkSessionScaleTable.RoomSpec;
                case NetworkSessionScale.Battle: return NetworkSessionScaleTable.BattleSpec;
                case NetworkSessionScale.Massive: return NetworkSessionScaleTable.MassiveSpec;
                default: throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown session scale.");
            }
        }

        public void RegisterScaleOverride(NetworkSessionScale scale, in NetworkScaleSpec spec)
        {
            ValidateScale(scale);
            _overrides[scale] = spec;
        }

        public void ClearScaleOverrides() => _overrides.Clear();

        private static void ValidateScale(NetworkSessionScale scale)
        {
            if (scale < NetworkSessionScale.Coop || scale > NetworkSessionScale.Massive)
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown session scale.");
        }
    }
}
