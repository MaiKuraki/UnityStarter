using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Cold-path construction policy. Cancellation is sampled at a power-of-two row interval so
    /// large index builds remain cancellable without adding division or allocation to each row.
    /// </summary>
    public readonly struct DataTableBuildContext
    {
        public const int DEFAULT_CANCELLATION_CHECK_INTERVAL = 1024;
        public const int MAX_CANCELLATION_CHECK_INTERVAL = 1 << 20;

        private readonly int _cancellationCheckMask;

        public DataTableBuildContext(
            DataTableLoadLimits limits,
            CancellationToken cancellationToken = default,
            int cancellationCheckInterval = DEFAULT_CANCELLATION_CHECK_INTERVAL)
        {
            limits.EnsureValid(nameof(limits));
            if (cancellationCheckInterval <= 0 ||
                cancellationCheckInterval > MAX_CANCELLATION_CHECK_INTERVAL ||
                (cancellationCheckInterval & (cancellationCheckInterval - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cancellationCheckInterval),
                    cancellationCheckInterval,
                    $"Cancellation check interval must be a power of two between 1 and {MAX_CANCELLATION_CHECK_INTERVAL}.");
            }

            Limits = limits;
            CancellationToken = cancellationToken;
            CancellationCheckInterval = cancellationCheckInterval;
            _cancellationCheckMask = cancellationCheckInterval - 1;
        }

        public DataTableLoadLimits Limits { get; }

        public CancellationToken CancellationToken { get; }

        public int CancellationCheckInterval { get; }

        public bool IsValid => Limits.IsValid && CancellationCheckInterval > 0;

        public void EnsureValid(string parameterName = null)
        {
            if (!IsValid)
            {
                throw new ArgumentException(
                    "Data-table build context is not initialized or contains invalid values.",
                    parameterName ?? "context");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfCancellationRequested(int completedWork)
        {
            if (CancellationToken.CanBeCanceled && (completedWork & _cancellationCheckMask) == 0)
            {
                CancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
