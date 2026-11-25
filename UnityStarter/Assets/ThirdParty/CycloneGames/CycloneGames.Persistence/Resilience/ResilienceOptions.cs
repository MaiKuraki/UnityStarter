using System;

namespace CycloneGames.Persistence.Resilience
{
    /// <summary>
    /// Explicit policy values for the resilience adapters. The composition root owns every bound,
    /// timeout, and retention count.
    /// </summary>
    public sealed class ResilienceOptions
    {
        public ResilienceOptions(
            string walDirectory,
            int backupGenerationCount,
            int writeGateCapacity,
            TimeSpan writeGateTimeout,
            TimeSpan leaseExpiry,
            int leaseAcquireAttempts,
            int eventBufferCapacity,
            IPersistenceEventSink eventSink)
        {
            if (string.IsNullOrWhiteSpace(walDirectory))
            {
                throw new ArgumentException("A WAL directory is required.", nameof(walDirectory));
            }

            if (backupGenerationCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(backupGenerationCount));
            }

            if (writeGateCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(writeGateCapacity));
            }

            if (writeGateTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(writeGateTimeout));
            }

            if (leaseExpiry <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseExpiry));
            }

            if (leaseAcquireAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseAcquireAttempts));
            }

            if (eventBufferCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(eventBufferCapacity));
            }

            WalDirectory = walDirectory;
            BackupGenerationCount = backupGenerationCount;
            WriteGateCapacity = writeGateCapacity;
            WriteGateTimeout = writeGateTimeout;
            LeaseExpiry = leaseExpiry;
            LeaseAcquireAttempts = leaseAcquireAttempts;
            EventBufferCapacity = eventBufferCapacity;
            EventSink = eventSink;
        }

        public string WalDirectory { get; }

        public int BackupGenerationCount { get; }

        public int WriteGateCapacity { get; }

        public TimeSpan WriteGateTimeout { get; }

        public TimeSpan LeaseExpiry { get; }

        public int LeaseAcquireAttempts { get; }

        public int EventBufferCapacity { get; }

        public IPersistenceEventSink EventSink { get; }
    }
}
