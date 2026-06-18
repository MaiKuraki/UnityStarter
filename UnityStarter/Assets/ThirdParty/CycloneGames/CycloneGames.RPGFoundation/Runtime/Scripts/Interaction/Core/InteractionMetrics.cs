using System.Threading;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionMetricsSnapshot
    {
        public readonly long TotalRequests;
        public readonly long AcceptedRequests;
        public readonly long RejectedRequests;
        public readonly long QueuedRequests;
        public readonly long StartedInteractions;
        public readonly long CompletedInteractions;
        public readonly long FailedInteractions;
        public readonly long FaultedInteractions;
        public readonly long DroppedCommands;
        public readonly InteractionValidationFailure LastRejection;

        public InteractionMetricsSnapshot(
            long totalRequests,
            long acceptedRequests,
            long rejectedRequests,
            long queuedRequests,
            long startedInteractions,
            long completedInteractions,
            long failedInteractions,
            long faultedInteractions,
            long droppedCommands,
            InteractionValidationFailure lastRejection)
        {
            TotalRequests = totalRequests;
            AcceptedRequests = acceptedRequests;
            RejectedRequests = rejectedRequests;
            QueuedRequests = queuedRequests;
            StartedInteractions = startedInteractions;
            CompletedInteractions = completedInteractions;
            FailedInteractions = failedInteractions;
            FaultedInteractions = faultedInteractions;
            DroppedCommands = droppedCommands;
            LastRejection = lastRejection;
        }
    }

    public sealed class InteractionMetrics
    {
        private readonly long[] _rejectionsByReason = new long[(int)InteractionValidationFailure.Count];
        private long _totalRequests;
        private long _acceptedRequests;
        private long _rejectedRequests;
        private long _queuedRequests;
        private long _startedInteractions;
        private long _completedInteractions;
        private long _failedInteractions;
        private long _faultedInteractions;
        private long _droppedCommands;
        private int _lastRejection;

        public void RecordValidation(InteractionValidationResult result)
        {
            Interlocked.Increment(ref _totalRequests);
            if (result.IsAccepted)
            {
                Interlocked.Increment(ref _acceptedRequests);
                if (result.IsQueued)
                {
                    Interlocked.Increment(ref _queuedRequests);
                }

                return;
            }

            RecordRejection(result.Failure);
        }

        public void RecordStarted()
        {
            Interlocked.Increment(ref _startedInteractions);
        }

        public void RecordCompleted(bool success, InteractionCancelReason cancelReason = InteractionCancelReason.Manual)
        {
            if (success)
            {
                Interlocked.Increment(ref _completedInteractions);
                return;
            }

            Interlocked.Increment(ref _failedInteractions);
            if (cancelReason == InteractionCancelReason.Faulted)
            {
                Interlocked.Increment(ref _faultedInteractions);
            }
        }

        public void RecordDroppedCommand()
        {
            Interlocked.Increment(ref _droppedCommands);
        }

        public long GetRejectedCount(InteractionValidationFailure failure)
        {
            int index = (int)failure;
            if (index <= 0 || index >= _rejectionsByReason.Length)
            {
                return 0L;
            }

            return Interlocked.Read(ref _rejectionsByReason[index]);
        }

        public InteractionMetricsSnapshot GetSnapshot()
        {
            return new InteractionMetricsSnapshot(
                Interlocked.Read(ref _totalRequests),
                Interlocked.Read(ref _acceptedRequests),
                Interlocked.Read(ref _rejectedRequests),
                Interlocked.Read(ref _queuedRequests),
                Interlocked.Read(ref _startedInteractions),
                Interlocked.Read(ref _completedInteractions),
                Interlocked.Read(ref _failedInteractions),
                Interlocked.Read(ref _faultedInteractions),
                Interlocked.Read(ref _droppedCommands),
                (InteractionValidationFailure)Volatile.Read(ref _lastRejection));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalRequests, 0L);
            Interlocked.Exchange(ref _acceptedRequests, 0L);
            Interlocked.Exchange(ref _rejectedRequests, 0L);
            Interlocked.Exchange(ref _queuedRequests, 0L);
            Interlocked.Exchange(ref _startedInteractions, 0L);
            Interlocked.Exchange(ref _completedInteractions, 0L);
            Interlocked.Exchange(ref _failedInteractions, 0L);
            Interlocked.Exchange(ref _faultedInteractions, 0L);
            Interlocked.Exchange(ref _droppedCommands, 0L);
            Volatile.Write(ref _lastRejection, (int)InteractionValidationFailure.None);
            for (int i = 0; i < _rejectionsByReason.Length; i++)
            {
                Interlocked.Exchange(ref _rejectionsByReason[i], 0L);
            }
        }

        private void RecordRejection(InteractionValidationFailure failure)
        {
            Interlocked.Increment(ref _rejectedRequests);
            Volatile.Write(ref _lastRejection, (int)failure);

            int index = (int)failure;
            if (index > 0 && index < _rejectionsByReason.Length)
            {
                Interlocked.Increment(ref _rejectionsByReason[index]);
            }
        }
    }
}
