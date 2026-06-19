using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Replication
{
    public readonly struct AdaptiveNetworkSendSchedulerOptions
    {
        public static readonly AdaptiveNetworkSendSchedulerOptions Default = new AdaptiveNetworkSendSchedulerOptions(
            minSendInterval: 1f / 30f,
            maxSendInterval: 0.25f,
            baseBudgetBytes: 4096,
            minBudgetBytes: 512,
            maxBudgetBytes: 16384,
            baseMessageBudget: 64,
            degradedRttMs: 250f,
            degradedFrameThreshold: 3);

        public readonly float MinSendInterval;
        public readonly float MaxSendInterval;
        public readonly int BaseBudgetBytes;
        public readonly int MinBudgetBytes;
        public readonly int MaxBudgetBytes;
        public readonly int BaseMessageBudget;
        public readonly float DegradedRttMs;
        public readonly int DegradedFrameThreshold;

        public AdaptiveNetworkSendSchedulerOptions(
            float minSendInterval,
            float maxSendInterval,
            int baseBudgetBytes,
            int minBudgetBytes,
            int maxBudgetBytes,
            int baseMessageBudget,
            float degradedRttMs,
            int degradedFrameThreshold)
        {
            if (minSendInterval <= 0f || float.IsNaN(minSendInterval))
            {
                throw new ArgumentOutOfRangeException(nameof(minSendInterval));
            }

            if (maxSendInterval < minSendInterval || float.IsNaN(maxSendInterval))
            {
                throw new ArgumentOutOfRangeException(nameof(maxSendInterval));
            }

            if (minBudgetBytes < 0 || baseBudgetBytes < minBudgetBytes || maxBudgetBytes < baseBudgetBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(baseBudgetBytes));
            }

            if (baseMessageBudget <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseMessageBudget));
            }

            if (degradedRttMs <= 0f || float.IsNaN(degradedRttMs))
            {
                throw new ArgumentOutOfRangeException(nameof(degradedRttMs));
            }

            if (degradedFrameThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(degradedFrameThreshold));
            }

            MinSendInterval = minSendInterval;
            MaxSendInterval = maxSendInterval;
            BaseBudgetBytes = baseBudgetBytes;
            MinBudgetBytes = minBudgetBytes;
            MaxBudgetBytes = maxBudgetBytes;
            BaseMessageBudget = baseMessageBudget;
            DegradedRttMs = degradedRttMs;
            DegradedFrameThreshold = degradedFrameThreshold;
        }
    }

    public sealed class AdaptiveNetworkSendScheduler : IAdaptiveSendRate
    {
        private const string DegradedReason = "Connection quality is below the adaptive send threshold.";

        private readonly AdaptiveNetworkSendSchedulerOptions _options;
        private readonly Dictionary<int, ConnectionState> _states = new Dictionary<int, ConnectionState>(128);

        public AdaptiveNetworkSendScheduler()
            : this(AdaptiveNetworkSendSchedulerOptions.Default)
        {
        }

        public AdaptiveNetworkSendScheduler(AdaptiveNetworkSendSchedulerOptions options)
        {
            _options = options;
        }

        public event Action<int, string> OnConnectionDegraded;

        public float MinSendInterval
        {
            get
            {
                return _options.MinSendInterval;
            }
        }

        public float MaxSendInterval
        {
            get
            {
                return _options.MaxSendInterval;
            }
        }

        public float GetTargetSendInterval(int connectionId)
        {
            return GetState(connectionId).TargetSendInterval;
        }

        public float GetPriorityBudget(int connectionId)
        {
            return GetState(connectionId).PriorityBudget;
        }

        public NetworkSendBudget CreateSendBudget(int connectionId)
        {
            float priorityBudget = GetPriorityBudget(connectionId);
            if (priorityBudget <= 0f)
            {
                return new NetworkSendBudget(0, 0);
            }

            int bytes = Clamp(
                (int)(_options.BaseBudgetBytes * priorityBudget),
                _options.MinBudgetBytes,
                _options.MaxBudgetBytes);
            int messages = Math.Max(1, (int)MathF.Ceiling(_options.BaseMessageBudget * priorityBudget));
            return new NetworkSendBudget(bytes, messages);
        }

        public void Update(int connectionId, in NetworkStatistics stats, ConnectionQuality quality, float deltaTime)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            if (deltaTime < 0f || float.IsNaN(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            ConnectionState state = GetState(connectionId);
            float qualityBudget = CalculateQualityBudget(stats, quality);
            state.PriorityBudget = quality == ConnectionQuality.Disconnected
                ? 0f
                : Lerp(state.PriorityBudget, qualityBudget, MathF.Min(1f, deltaTime * 8f));
            state.TargetSendInterval = Lerp(
                _options.MaxSendInterval,
                _options.MinSendInterval,
                state.PriorityBudget);

            bool isDegraded = quality == ConnectionQuality.Poor
                              || quality == ConnectionQuality.Disconnected
                              || stats.AverageRttMs >= _options.DegradedRttMs;
            state.DegradedFrameCount = isDegraded ? state.DegradedFrameCount + 1 : 0;

            if (!state.HasRaisedDegraded
                && state.DegradedFrameCount >= _options.DegradedFrameThreshold)
            {
                state.HasRaisedDegraded = true;
                OnConnectionDegraded?.Invoke(connectionId, DegradedReason);
            }

            if (!isDegraded)
            {
                state.HasRaisedDegraded = false;
            }

            _states[connectionId] = state;
        }

        public void ResetConnection(int connectionId)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            _states.Remove(connectionId);
        }

        private ConnectionState GetState(int connectionId)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            if (_states.TryGetValue(connectionId, out ConnectionState state))
            {
                return state;
            }

            return ConnectionState.Default(_options);
        }

        private static float CalculateQualityBudget(in NetworkStatistics stats, ConnectionQuality quality)
        {
            float budget;
            switch (quality)
            {
                case ConnectionQuality.Excellent:
                    budget = 1f;
                    break;
                case ConnectionQuality.Good:
                    budget = 0.85f;
                    break;
                case ConnectionQuality.Fair:
                    budget = 0.6f;
                    break;
                case ConnectionQuality.Poor:
                    budget = 0.35f;
                    break;
                case ConnectionQuality.Disconnected:
                    budget = 0f;
                    break;
                default:
                    budget = 0.5f;
                    break;
            }

            if (stats.AverageRttMs > 0f)
            {
                float rttPressure = MathF.Min(1f, stats.AverageRttMs / 500f);
                budget *= 1f - rttPressure * 0.5f;
            }

            int packetTotal = stats.PacketsSent + stats.DroppedPackets;
            if (packetTotal > 0)
            {
                float dropRatio = MathF.Min(1f, (float)stats.DroppedPackets / packetTotal);
                budget *= 1f - dropRatio * 0.75f;
            }

            return Clamp01(budget);
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * Clamp01(t);
        }

        private struct ConnectionState
        {
            public float TargetSendInterval;
            public float PriorityBudget;
            public int DegradedFrameCount;
            public bool HasRaisedDegraded;

            public static ConnectionState Default(in AdaptiveNetworkSendSchedulerOptions options)
            {
                return new ConnectionState
                {
                    TargetSendInterval = options.MinSendInterval,
                    PriorityBudget = 1f,
                    DegradedFrameCount = 0,
                    HasRaisedDegraded = false
                };
            }
        }
    }
}
