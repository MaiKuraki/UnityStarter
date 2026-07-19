using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public sealed class NetworkRuntimeProfile
    {
        private readonly Dictionary<string, int> _intSettings;
        private readonly Dictionary<string, float> _floatSettings;
        private readonly Dictionary<string, string> _stringSettings;

        internal NetworkRuntimeProfile(NetworkRuntimeProfileBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ProfileId = string.IsNullOrEmpty(builder.ProfileId) ? "default" : builder.ProfileId;
            DisplayName = string.IsNullOrEmpty(builder.DisplayName) ? ProfileId : builder.DisplayName;
            MaxConnections = ValidateNonNegative(builder.MaxConnections, nameof(builder.MaxConnections));
            TickRate = ValidateRange(builder.TickRate, NetworkConstants.MinTickRate, NetworkConstants.MaxTickRate, nameof(builder.TickRate));
            SendRate = ValidatePositive(builder.SendRate, nameof(builder.SendRate));
            Mtu = ValidatePositive(builder.Mtu, nameof(builder.Mtu));
            MaxPayloadBytes = ValidateNonNegative(builder.MaxPayloadBytes, nameof(builder.MaxPayloadBytes));
            BufferSize = ValidatePositive(builder.BufferSize, nameof(builder.BufferSize));
            PoolSize = ValidateNonNegative(builder.PoolSize, nameof(builder.PoolSize));
            SnapshotBufferSize = ValidateNonNegative(builder.SnapshotBufferSize, nameof(builder.SnapshotBufferSize));
            SessionSearchMaxResults = ValidateNonNegative(builder.SessionSearchMaxResults, nameof(builder.SessionSearchMaxResults));
            TimeoutSeconds = ValidateNonNegative(builder.TimeoutSeconds, nameof(builder.TimeoutSeconds));
            HeartbeatIntervalSeconds = ValidateNonNegative(builder.HeartbeatIntervalSeconds, nameof(builder.HeartbeatIntervalSeconds));
            DisconnectTimeoutSeconds = ValidateNonNegative(builder.DisconnectTimeoutSeconds, nameof(builder.DisconnectTimeoutSeconds));
            ReconnectWindowSeconds = ValidateNonNegative(builder.ReconnectWindowSeconds, nameof(builder.ReconnectWindowSeconds));
            HostMigrationTimeoutSeconds = ValidateNonNegative(builder.HostMigrationTimeoutSeconds, nameof(builder.HostMigrationTimeoutSeconds));

            _intSettings = new Dictionary<string, int>(builder.IntSettings, StringComparer.Ordinal);
            _floatSettings = new Dictionary<string, float>(builder.FloatSettings, StringComparer.Ordinal);
            _stringSettings = new Dictionary<string, string>(builder.StringSettings, StringComparer.Ordinal);
        }

        public string ProfileId { get; }
        public string DisplayName { get; }
        public int MaxConnections { get; }
        public int TickRate { get; }
        public int SendRate { get; }
        public int Mtu { get; }
        public int MaxPayloadBytes { get; }
        public int BufferSize { get; }
        public int PoolSize { get; }
        public int SnapshotBufferSize { get; }
        public int SessionSearchMaxResults { get; }
        public float TimeoutSeconds { get; }
        public float HeartbeatIntervalSeconds { get; }
        public float DisconnectTimeoutSeconds { get; }
        public double ReconnectWindowSeconds { get; }
        public double HostMigrationTimeoutSeconds { get; }

        public IReadOnlyDictionary<string, int> IntSettings
        {
            get
            {
                return _intSettings;
            }
        }

        public IReadOnlyDictionary<string, float> FloatSettings
        {
            get
            {
                return _floatSettings;
            }
        }

        public IReadOnlyDictionary<string, string> StringSettings
        {
            get
            {
                return _stringSettings;
            }
        }

        public bool TryGetInt(string key, out int value)
        {
            if (!string.IsNullOrEmpty(key) && _intSettings.TryGetValue(key, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetFloat(string key, out float value)
        {
            if (!string.IsNullOrEmpty(key) && _floatSettings.TryGetValue(key, out value))
            {
                return true;
            }

            value = 0f;
            return false;
        }

        public bool TryGetString(string key, out string value)
        {
            if (!string.IsNullOrEmpty(key) && _stringSettings.TryGetValue(key, out value))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        public NetworkRuntimeProfileBuilder ToBuilder()
        {
            var builder = new NetworkRuntimeProfileBuilder
            {
                ProfileId = ProfileId,
                DisplayName = DisplayName,
                MaxConnections = MaxConnections,
                TickRate = TickRate,
                SendRate = SendRate,
                Mtu = Mtu,
                MaxPayloadBytes = MaxPayloadBytes,
                BufferSize = BufferSize,
                PoolSize = PoolSize,
                SnapshotBufferSize = SnapshotBufferSize,
                SessionSearchMaxResults = SessionSearchMaxResults,
                TimeoutSeconds = TimeoutSeconds,
                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                DisconnectTimeoutSeconds = DisconnectTimeoutSeconds,
                ReconnectWindowSeconds = ReconnectWindowSeconds,
                HostMigrationTimeoutSeconds = HostMigrationTimeoutSeconds
            };

            foreach (KeyValuePair<string, int> pair in _intSettings)
            {
                builder.SetInt(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<string, float> pair in _floatSettings)
            {
                builder.SetFloat(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<string, string> pair in _stringSettings)
            {
                builder.SetString(pair.Key, pair.Value);
            }

            return builder;
        }

        private static int ValidatePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static int ValidateNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static float ValidateNonNegative(float value, string name)
        {
            if (value < 0f || float.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static double ValidateNonNegative(double value, string name)
        {
            if (value < 0d || double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        private static int ValidateRange(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }
    }

    public sealed class NetworkRuntimeProfileBuilder
    {
        internal readonly Dictionary<string, int> IntSettings = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, float> FloatSettings = new Dictionary<string, float>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> StringSettings = new Dictionary<string, string>(StringComparer.Ordinal);

        public string ProfileId { get; set; } = "default";
        public string DisplayName { get; set; } = "Default";
        public int MaxConnections { get; set; } = NetworkConstants.DefaultMaxConnections;
        public int TickRate { get; set; } = NetworkConstants.DefaultTickRate;
        public int SendRate { get; set; } = NetworkConstants.DefaultSendRate;
        public int Mtu { get; set; } = NetworkConstants.DefaultMTU;
        public int MaxPayloadBytes { get; set; } = NetworkConstants.DefaultMaxPayloadSize;
        public int BufferSize { get; set; } = NetworkConstants.DefaultBufferSize;
        public int PoolSize { get; set; } = NetworkConstants.DefaultPoolSize;
        public int SnapshotBufferSize { get; set; } = NetworkConstants.MaxSnapshotBufferSize;
        public int SessionSearchMaxResults { get; set; } = NetworkConstants.DefaultSessionSearchMaxResults;
        public float TimeoutSeconds { get; set; } = NetworkConstants.DefaultTimeoutSeconds;
        public float HeartbeatIntervalSeconds { get; set; } = NetworkConstants.DefaultHeartbeatInterval;
        public float DisconnectTimeoutSeconds { get; set; } = NetworkConstants.DefaultDisconnectTimeout;
        public double ReconnectWindowSeconds { get; set; } = NetworkConstants.DefaultReconnectWindowSeconds;
        public double HostMigrationTimeoutSeconds { get; set; } = NetworkConstants.DefaultHostMigrationTimeoutSeconds;

        public NetworkRuntimeProfileBuilder SetInt(string key, int value)
        {
            ValidateKey(key);
            IntSettings[key] = value;
            return this;
        }

        public NetworkRuntimeProfileBuilder SetFloat(string key, float value)
        {
            ValidateKey(key);
            if (float.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            FloatSettings[key] = value;
            return this;
        }

        public NetworkRuntimeProfileBuilder SetString(string key, string value)
        {
            ValidateKey(key);
            StringSettings[key] = value ?? string.Empty;
            return this;
        }

        public NetworkRuntimeProfile Build()
        {
            return new NetworkRuntimeProfile(this);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Profile setting key must not be null or empty.", nameof(key));
            }
        }
    }

    public static class NetworkRuntimeProfiles
    {
        public static NetworkRuntimeProfile Default
        {
            get
            {
                return CreateDefaultBuilder().Build();
            }
        }

        public static NetworkRuntimeProfileBuilder CreateDefaultBuilder()
        {
            return new NetworkRuntimeProfileBuilder();
        }
    }

    public sealed class NetworkRuntimeProfileRegistry
    {
        private const int DEFAULT_PROFILE_CAPACITY = 8;

        private readonly Dictionary<string, NetworkRuntimeProfile> _profiles;

        public NetworkRuntimeProfileRegistry(int capacity = DEFAULT_PROFILE_CAPACITY)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _profiles = new Dictionary<string, NetworkRuntimeProfile>(capacity, StringComparer.Ordinal);
            Register(NetworkRuntimeProfiles.Default);
        }

        public int Count
        {
            get
            {
                return _profiles.Count;
            }
        }

        public void Register(NetworkRuntimeProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            _profiles[profile.ProfileId] = profile;
        }

        public bool TryGet(string profileId, out NetworkRuntimeProfile profile)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                profile = null;
                return false;
            }

            return _profiles.TryGetValue(profileId, out profile);
        }

        public NetworkRuntimeProfile GetOrDefault(string profileId)
        {
            return TryGet(profileId, out NetworkRuntimeProfile profile)
                ? profile
                : _profiles["default"];
        }

        public void Clear(bool includeDefault = false)
        {
            _profiles.Clear();
            if (!includeDefault)
            {
                Register(NetworkRuntimeProfiles.Default);
            }
        }
    }

    public static class NetworkRuntimeProfileContextExtensions
    {
        public static INetworkRuntimeContextBuilder AddRuntimeProfile(
            this INetworkRuntimeContextBuilder builder,
            NetworkRuntimeProfile profile)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddService(profile ?? throw new ArgumentNullException(nameof(profile)));
            return builder;
        }

        public static bool TryGetRuntimeProfile(
            this INetworkRuntimeContext context,
            out NetworkRuntimeProfile profile)
        {
            if (context != null && context.TryGetService(out profile))
            {
                return true;
            }

            profile = null;
            return false;
        }
    }
}
