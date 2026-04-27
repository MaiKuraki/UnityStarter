using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Simulation
{
    /// <summary>
    /// Client-server time synchronization using NTP-style round-trip measurement.
    /// Provides smoothed offset and jitter estimation for snapshot interpolation.
    /// </summary>
    public sealed class NetworkTimeSync
    {
        private double _offset;
        private double _smoothedOffset;
        private double _rtt;
        private double _smoothedRtt;
        private double _jitter;
        private int _sampleCount;

        // Exponential moving average factor (lower = smoother, higher = more responsive)
        private const double SmoothFactor = 0.1;
        private const int WarmupSamples = 5;

        public double Offset => _smoothedOffset;
        public double Rtt => _smoothedRtt;
        public double Jitter => _jitter;
        public bool IsWarmedUp => _sampleCount >= WarmupSamples;

        /// <summary>
        /// Process a time sync response from the server.
        /// </summary>
        /// <param name="clientSendTime">Client local time when request was sent</param>
        /// <param name="serverTime">Server time when it received the request</param>
        /// <param name="clientReceiveTime">Client local time when response was received</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessTimeSample(double clientSendTime, double serverTime, double clientReceiveTime)
        {
            double rtt = clientReceiveTime - clientSendTime;
            double offset = serverTime - (clientSendTime + rtt * 0.5);

            if (_sampleCount == 0)
            {
                _sampleCount = 1;
                _rtt = rtt;
                _smoothedRtt = rtt;
                _offset = offset;
                _smoothedOffset = offset;
                _jitter = 0;
                return;
            }

            // Discard outliers (RTT spike > 3x smoothed) after warmup
            if (IsWarmedUp && rtt > _smoothedRtt * 3.0)
                return;

            _sampleCount++;

            double prevRtt = _smoothedRtt;
            _smoothedRtt = _smoothedRtt * (1.0 - SmoothFactor) + rtt * SmoothFactor;
            _smoothedOffset = _smoothedOffset * (1.0 - SmoothFactor) + offset * SmoothFactor;

            double jitterSample = System.Math.Abs(rtt - prevRtt);
            _jitter = _jitter * (1.0 - SmoothFactor) + jitterSample * SmoothFactor;

            _rtt = rtt;
            _offset = offset;
        }

        /// <summary>
        /// Convert local time to estimated server time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double LocalToServerTime(double localTime) => localTime + _smoothedOffset;

        /// <summary>
        /// Convert server time to estimated local time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ServerToLocalTime(double serverTime) => serverTime - _smoothedOffset;

        public void Reset()
        {
            _offset = 0;
            _smoothedOffset = 0;
            _rtt = 0;
            _smoothedRtt = 0;
            _jitter = 0;
            _sampleCount = 0;
        }
    }
}
