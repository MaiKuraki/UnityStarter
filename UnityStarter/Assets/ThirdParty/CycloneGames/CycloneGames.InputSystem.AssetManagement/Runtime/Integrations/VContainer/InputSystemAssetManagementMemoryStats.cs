namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    public readonly struct InputSystemAssetManagementMemoryStats
    {
        public InputSystemAssetManagementMemoryStats(
            int pendingRequestCount,
            int activeLeaseCount,
            int peakPendingRequestCount,
            int peakActiveLeaseCount,
            long requestCount,
            long succeededRequestCount,
            long failedRequestCount,
            long cancelledRequestCount,
            long policyRejectedRequestCount,
            long rawCapabilityFallbackCount,
            long acquiredLeaseCount,
            long releasedLeaseCount)
        {
            PendingRequestCount = pendingRequestCount;
            ActiveLeaseCount = activeLeaseCount;
            PeakPendingRequestCount = peakPendingRequestCount;
            PeakActiveLeaseCount = peakActiveLeaseCount;
            RequestCount = requestCount;
            SucceededRequestCount = succeededRequestCount;
            FailedRequestCount = failedRequestCount;
            CancelledRequestCount = cancelledRequestCount;
            PolicyRejectedRequestCount = policyRejectedRequestCount;
            RawCapabilityFallbackCount = rawCapabilityFallbackCount;
            AcquiredLeaseCount = acquiredLeaseCount;
            ReleasedLeaseCount = releasedLeaseCount;
        }

        public int PendingRequestCount { get; }
        public int ActiveLeaseCount { get; }
        public int PeakPendingRequestCount { get; }
        public int PeakActiveLeaseCount { get; }
        public long RequestCount { get; }
        public long SucceededRequestCount { get; }
        public long FailedRequestCount { get; }
        public long CancelledRequestCount { get; }
        public long PolicyRejectedRequestCount { get; }
        public long RawCapabilityFallbackCount { get; }
        public long AcquiredLeaseCount { get; }
        public long ReleasedLeaseCount { get; }
    }

    /// <summary>Explicit main-thread diagnostics owner for loader operations and transient provider leases.</summary>
    public sealed class InputSystemAssetManagementDiagnostics
    {
        private int _pendingRequestCount;
        private int _activeLeaseCount;
        private int _peakPendingRequestCount;
        private int _peakActiveLeaseCount;
        private long _requestCount;
        private long _succeededRequestCount;
        private long _failedRequestCount;
        private long _cancelledRequestCount;
        private long _policyRejectedRequestCount;
        private long _rawCapabilityFallbackCount;
        private long _acquiredLeaseCount;
        private long _releasedLeaseCount;

        internal void BeginRequest()
        {
            _requestCount++;
            _pendingRequestCount++;
            if (_pendingRequestCount > _peakPendingRequestCount) _peakPendingRequestCount = _pendingRequestCount;
        }

        internal void EndRequest(bool succeeded, bool cancelled)
        {
            if (_pendingRequestCount > 0) _pendingRequestCount--;
            if (succeeded) _succeededRequestCount++;
            else if (cancelled) _cancelledRequestCount++;
            else _failedRequestCount++;
        }

        internal void BeginLease()
        {
            _activeLeaseCount++;
            _acquiredLeaseCount++;
            if (_activeLeaseCount > _peakActiveLeaseCount) _peakActiveLeaseCount = _activeLeaseCount;
        }

        internal void EndLease()
        {
            if (_activeLeaseCount > 0) _activeLeaseCount--;
            _releasedLeaseCount++;
        }

        internal void RecordPolicyRejection() => _policyRejectedRequestCount++;
        internal void RecordRawCapabilityFallback() => _rawCapabilityFallbackCount++;

        public InputSystemAssetManagementMemoryStats GetMemoryStats()
        {
            return new InputSystemAssetManagementMemoryStats(
                _pendingRequestCount,
                _activeLeaseCount,
                _peakPendingRequestCount,
                _peakActiveLeaseCount,
                _requestCount,
                _succeededRequestCount,
                _failedRequestCount,
                _cancelledRequestCount,
                _policyRejectedRequestCount,
                _rawCapabilityFallbackCount,
                _acquiredLeaseCount,
                _releasedLeaseCount);
        }
    }
}
